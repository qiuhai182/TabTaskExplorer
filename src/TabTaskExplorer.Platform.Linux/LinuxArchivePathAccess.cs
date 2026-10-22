using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.Linux;

/// <summary>
/// Linux 平台的压缩包路径访问实现：
/// 字符串模式复用内核通用实现；原始字节模式通过 libc 系统调用按字节读写路径，
/// 文件名字节不经过任何字符集转换（等价于 gnu-tar 在 LC_ALL=C 下的字节透传语义）。
/// 仅在 Linux 上注册（见组合根），Windows / macOS 使用内核默认实现。
/// </summary>
public sealed class LinuxArchivePathAccess : IArchivePathAccess
{
    // ---- libc 目录条目结构（64 位小端，glibc 与 musl 一致）----
    private const int DirentReclenOffset = 16;   // unsigned short d_reclen
    private const int DirentNameOffset = 19;     // char d_name[]（d_ino 8 + d_off 8 + d_reclen 2 + d_type 1）
    private const int DirentNameCapacity = 256;  // d_name 容量上限

    // ---- 内核文件状态结构（64 位小端；大小与修改时间各架构一致，权限位偏移按架构区分）----
    private const int StatBufferSize = 256;          // 实际结构 128~144 字节，留足余量
    private const int StatModeOffsetX64 = 24;        // x86_64：st_dev(8) + st_ino(8) + st_nlink(8) 后是 st_mode
    private const int StatModeOffsetArm64 = 16;      // arm64 等通用架构：st_dev(8) + st_ino(8) 后是 st_mode
    private const int StatSizeOffset = 48;           // st_size
    private const int StatMtimeSecondsOffset = 88;   // st_mtime 秒
    private const int StatMtimeNanosecondsOffset = 96; // st_mtime 纳秒
    private const long MaxValidMtimeSeconds = 4102444800L; // 2100-01-01，超过视为无效

    // ---- open / mkdir / lstat 常量（Linux x86_64 与 arm64 相同）----
    private const int OpenReadOnly = 0;              // O_RDONLY
    private const int OpenWriteOnlyTruncateCreate = 1 | 64 | 512; // O_WRONLY | O_CREAT | O_TRUNC
    private const int ModeCreateFile = 420;          // 0644
    private const int ModeCreateDirectory = 493;     // 0755
    private const int ErrorNoEntry = 2;              // ENOENT
    private const int ErrorExists = 17;              // EEXIST

    // ---- 文件类型与权限位掩码 ----
    private const uint FileModeMask = 0xF000;        // S_IFMT
    private const uint FileModeDirectory = 0x4000;   // S_IFDIR
    private const uint UnixPermissionMask = 0xFFF;   // 07777 权限位

    /// <summary>字符串模式复用内核通用实现（Linux 字符串场景与其行为保持一致）。</summary>
    private readonly DefaultArchivePathAccess stringAccess = new DefaultArchivePathAccess();

    /// <summary>Linux 支持原始字节路径。</summary>
    public bool SupportsRawBytePaths => true;

    /// <summary>列出目录全部条目（含隐藏条目，不递归）。</summary>
    public IReadOnlyList<ArchiveFsEntry> ListDirectory(string directoryPath)
    {
        return this.stringAccess.ListDirectory(directoryPath);
    }

    /// <summary>打开文件供读取。</summary>
    public Stream OpenRead(string filePath)
    {
        return this.stringAccess.OpenRead(filePath);
    }

    /// <summary>创建（或覆盖）文件并返回可写流；父目录必须已存在。</summary>
    public Stream CreateFile(string filePath)
    {
        return this.stringAccess.CreateFile(filePath);
    }

    /// <summary>递归创建目录（已存在时无操作）。</summary>
    public void CreateDirectory(string directoryPath)
    {
        this.stringAccess.CreateDirectory(directoryPath);
    }

    /// <summary>删除文件（不存在时无操作）。</summary>
    public void DeleteFile(string filePath)
    {
        this.stringAccess.DeleteFile(filePath);
    }

    /// <summary>判断文件是否存在。</summary>
    public bool FileExists(string filePath)
    {
        return this.stringAccess.FileExists(filePath);
    }

    /// <summary>把源文件覆盖替换到目标路径（修改压缩包后原地替换用）。</summary>
    public void ReplaceFile(string sourceFilePath, string targetFilePath)
    {
        this.stringAccess.ReplaceFile(sourceFilePath, targetFilePath);
    }

    /// <summary>按原始字节路径列出目录全部条目（文件名字节不转码，跳过 . 与 ..）。</summary>
    public IReadOnlyList<ArchiveFsRawEntry> ListDirectoryRaw(byte[] directoryPath)
    {
        EnsureLinux();
        IntPtr dir = opendir(ToCString(directoryPath));
        if (dir == IntPtr.Zero)
        {
            throw BuildIoError("打开目录", directoryPath, Marshal.GetLastPInvokeError());
        }
        List<ArchiveFsRawEntry> result = new List<ArchiveFsRawEntry>();
        try
        {
            while (true)
            {
                IntPtr dirEntry = readdir(dir);
                if (dirEntry == IntPtr.Zero)
                {
                    break;
                }
                byte[] name = ReadDirentName(dirEntry);
                if (name.Length == 0 || IsDotEntry(name))
                {
                    continue;
                }
                byte[] fullPath = JoinPathBytes(directoryPath, name);
                if (!TryLStat(fullPath, out bool isDirectory, out long lengthBytes, out DateTime modifiedTimeUtc, out int unixMode, out int errorNumber))
                {
                    if (errorNumber == ErrorNoEntry)
                    {
                        // 条目在列目录与取状态之间消失时跳过
                        continue;
                    }
                    throw BuildIoError("读取条目状态", fullPath, errorNumber);
                }
                ArchiveFsRawEntry entry = new ArchiveFsRawEntry();
                entry.Name = name;
                entry.IsDirectory = isDirectory;
                entry.LengthBytes = isDirectory ? 0 : lengthBytes;
                entry.ModifiedTimeUtc = modifiedTimeUtc;
                entry.UnixMode = unixMode;
                result.Add(entry);
            }
        }
        finally
        {
            closedir(dir);
        }
        return result;
    }

    /// <summary>按原始字节路径打开文件供读取。</summary>
    public Stream OpenReadRaw(byte[] filePath)
    {
        EnsureLinux();
        int descriptor = open(ToCString(filePath), OpenReadOnly, 0);
        if (descriptor < 0)
        {
            throw BuildIoError("打开文件读取", filePath, Marshal.GetLastPInvokeError());
        }
        SafeFileHandle handle = new SafeFileHandle(new IntPtr(descriptor), true);
        return new FileStream(handle, FileAccess.Read);
    }

    /// <summary>按原始字节路径创建（或覆盖）文件；父目录必须已存在。</summary>
    public Stream CreateFileRaw(byte[] filePath)
    {
        EnsureLinux();
        int descriptor = open(ToCString(filePath), OpenWriteOnlyTruncateCreate, ModeCreateFile);
        if (descriptor < 0)
        {
            throw BuildIoError("创建文件", filePath, Marshal.GetLastPInvokeError());
        }
        SafeFileHandle handle = new SafeFileHandle(new IntPtr(descriptor), true);
        return new FileStream(handle, FileAccess.Write);
    }

    /// <summary>按原始字节路径递归创建目录（逐级创建，已存在时无操作）。</summary>
    public void CreateDirectoryRaw(byte[] directoryPath)
    {
        EnsureLinux();
        if (directoryPath.Length == 0)
        {
            return;
        }
        for (int index = 1; index <= directoryPath.Length; index++)
        {
            if (index < directoryPath.Length && directoryPath[index] != (byte)'/')
            {
                continue;
            }
            byte[] component = new byte[index];
            Array.Copy(directoryPath, 0, component, 0, index);
            if (component.Length == 1 && component[0] == (byte)'/')
            {
                // 根目录必然存在，无需创建
                continue;
            }
            int result = mkdir(ToCString(component), ModeCreateDirectory);
            if (result != 0)
            {
                int errorNumber = Marshal.GetLastPInvokeError();
                if (errorNumber != ErrorExists)
                {
                    throw BuildIoError("创建目录", component, errorNumber);
                }
            }
        }
    }

    /// <summary>按原始字节路径判断文件是否存在（lstat 不跟随符号链接）。</summary>
    public bool FileExistsRaw(byte[] filePath)
    {
        EnsureLinux();
        byte[] buffer = new byte[StatBufferSize];
        return lstat(ToCString(filePath), buffer) == 0;
    }

    /// <summary>仅允许在 Linux 上使用（其他平台由内核默认实现接管）。</summary>
    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("原始字节路径访问仅在 Linux 平台可用");
        }
    }

    /// <summary>从目录条目结构中读出文件名字节（遇到结尾零字节为止）。</summary>
    private static byte[] ReadDirentName(IntPtr dirEntry)
    {
        int limit = DirentNameOffset + DirentNameCapacity;
        int recordLimit = (ushort)Marshal.ReadInt16(dirEntry, DirentReclenOffset); // d_reclen 是整条记录的长度
        if (limit > recordLimit)
        {
            limit = recordLimit;
        }
        int nameLength = 0;
        while (DirentNameOffset + nameLength < limit && Marshal.ReadByte(dirEntry, DirentNameOffset + nameLength) != 0)
        {
            nameLength++;
        }
        byte[] name = new byte[nameLength];
        for (int index = 0; index < nameLength; index++)
        {
            name[index] = Marshal.ReadByte(dirEntry, DirentNameOffset + index);
        }
        return name;
    }

    /// <summary>判断条目名是否为 . 或 ..（与字符串模式枚举结果保持一致，二者不进入归档）。</summary>
    private static bool IsDotEntry(byte[] name)
    {
        if (name.Length == 1)
        {
            return name[0] == (byte)'.';
        }
        if (name.Length == 2)
        {
            return name[0] == (byte)'.' && name[1] == (byte)'.';
        }
        return false;
    }

    /// <summary>拼接目录字节路径与条目名字节（目录已带结尾斜杠时不再重复）。</summary>
    private static byte[] JoinPathBytes(byte[] directoryPath, byte[] name)
    {
        bool needSeparator = directoryPath.Length == 0 || directoryPath[directoryPath.Length - 1] != (byte)'/';
        byte[] result = new byte[directoryPath.Length + (needSeparator ? 1 : 0) + name.Length];
        Array.Copy(directoryPath, 0, result, 0, directoryPath.Length);
        if (needSeparator)
        {
            result[directoryPath.Length] = (byte)'/';
        }
        Array.Copy(name, 0, result, directoryPath.Length + (needSeparator ? 1 : 0), name.Length);
        return result;
    }

    /// <summary>把字节路径补成以零字节结尾的形式（libc 按 C 字符串读取路径）。</summary>
    private static byte[] ToCString(byte[] path)
    {
        if (path.Length > 0 && path[path.Length - 1] == 0)
        {
            return path;
        }
        byte[] text = new byte[path.Length + 1];
        Array.Copy(path, 0, text, 0, path.Length);
        return text;
    }

    /// <summary>
    /// 取路径的文件状态：是否目录、大小、最后修改时间（世界协调时）与 Unix 权限位。
    /// lstat 失败时（条目已消失等）返回 false，并给出失败的错误码。
    /// </summary>
    private static bool TryLStat(byte[] path, out bool isDirectory, out long lengthBytes, out DateTime modifiedTimeUtc, out int unixMode, out int errorNumber)
    {
        isDirectory = false;
        lengthBytes = 0;
        modifiedTimeUtc = DateTime.UnixEpoch;
        unixMode = 0;
        errorNumber = 0;
        byte[] buffer = new byte[StatBufferSize];
        if (lstat(ToCString(path), buffer) != 0)
        {
            errorNumber = Marshal.GetLastPInvokeError();
            return false;
        }
        int modeOffset = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? StatModeOffsetArm64 : StatModeOffsetX64;
        uint mode = BitConverter.ToUInt32(buffer, modeOffset);
        isDirectory = (mode & FileModeMask) == FileModeDirectory;
        unixMode = (int)(mode & UnixPermissionMask);
        lengthBytes = BitConverter.ToInt64(buffer, StatSizeOffset);
        long seconds = BitConverter.ToInt64(buffer, StatMtimeSecondsOffset);
        if (seconds >= 0 && seconds <= MaxValidMtimeSeconds)
        {
            long nanoseconds = BitConverter.ToInt64(buffer, StatMtimeNanosecondsOffset);
            modifiedTimeUtc = DateTime.UnixEpoch.AddSeconds(seconds).AddTicks(nanoseconds / 100);
        }
        return true;
    }

    /// <summary>按系统调用失败生成中文错误信息（路径不可解码，以十六进制给出）。</summary>
    private static IOException BuildIoError(string action, byte[] path, int errorNumber)
    {
        StringBuilder hex = new StringBuilder(path.Length * 2);
        for (int index = 0; index < path.Length; index++)
        {
            hex.Append(path[index].ToString("X2"));
        }
        return new IOException(action + "失败：错误码=" + errorNumber + " 路径十六进制=" + hex.ToString());
    }

    // ---- libc 接口（仅 Linux 调用，构造与调用前均做平台判定）----

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr opendir(byte[] path);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr readdir(IntPtr dir);

    [DllImport("libc", SetLastError = true)]
    private static extern int closedir(IntPtr dir);

    [DllImport("libc", SetLastError = true)]
    private static extern int open(byte[] path, int flags, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int mkdir(byte[] path, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int lstat(byte[] path, byte[] buffer);
}
