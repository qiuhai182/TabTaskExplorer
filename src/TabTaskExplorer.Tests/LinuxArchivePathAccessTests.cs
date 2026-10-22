using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Platform.Linux;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>
/// Linux 原始字节路径访问：Linux 上做磁盘字节往返验证，
/// 其余平台验证平台守卫（原始字节调用必须拒绝，字符串模式仍由内核默认实现接管）。
/// </summary>
public class LinuxArchivePathAccessTests
{
    [Fact]
    public void RawBytePaths_AreUsableOnlyOnLinux()
    {
        LinuxArchivePathAccess access = new LinuxArchivePathAccess();
        Assert.True(access.SupportsRawBytePaths);
        if (!OperatingSystem.IsLinux())
        {
            Assert.Throws<PlatformNotSupportedException>(() => access.FileExistsRaw(new byte[] { 0x2F }));
            Assert.Throws<PlatformNotSupportedException>(() => access.ListDirectoryRaw(new byte[] { 0x2F }));
            return;
        }
        Assert.False(access.FileExistsRaw(Encoding.UTF8.GetBytes("/TabTaskExplorer-不存在的路径")));
    }

    [Fact]
    public void RawByteName_RoundTrip_KeepsDiskBytes()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        LinuxArchivePathAccess access = new LinuxArchivePathAccess();
        string directoryText = Path.Combine(Path.GetTempPath(), "TabTaskExplorer-raw-" + Guid.NewGuid().ToString("N"));
        byte[] directoryBytes = Encoding.UTF8.GetBytes(directoryText);
        // GBK 编码的“中文”目录与“中文.txt”文件名，模拟非 UTF-8 文件名的字节透传
        byte[] subDirectoryName = new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 };
        byte[] fileName = new byte[] { 0xD6, 0xD0, 0xCE, 0xC4, 0x2E, 0x74, 0x78, 0x74 };
        byte[] content = new byte[] { 0xD6, 0xD0, 0xCE, 0xC4, 0xC4, 0xDC };
        try
        {
            access.CreateDirectoryRaw(directoryBytes);
            byte[] subDirectoryBytes = JoinBytes(directoryBytes, subDirectoryName);
            access.CreateDirectoryRaw(subDirectoryBytes);
            byte[] filePath = JoinBytes(subDirectoryBytes, fileName);
            using (Stream output = access.CreateFileRaw(filePath))
            {
                output.Write(content, 0, content.Length);
            }
            Assert.True(access.FileExistsRaw(filePath));

            IReadOnlyList<ArchiveFsRawEntry> rootEntries = access.ListDirectoryRaw(directoryBytes);
            ArchiveFsRawEntry rootEntry = Assert.Single(rootEntries);
            Assert.Equal(subDirectoryName, rootEntry.Name);
            Assert.True(rootEntry.IsDirectory);

            IReadOnlyList<ArchiveFsRawEntry> subEntries = access.ListDirectoryRaw(subDirectoryBytes);
            ArchiveFsRawEntry fileEntry = Assert.Single(subEntries);
            Assert.Equal(fileName, fileEntry.Name);
            Assert.False(fileEntry.IsDirectory);
            Assert.Equal(content.Length, fileEntry.LengthBytes);

            using (Stream input = access.OpenReadRaw(filePath))
            using (MemoryStream copy = new MemoryStream())
            {
                input.CopyTo(copy);
                Assert.Equal(content, copy.ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(directoryText))
            {
                Directory.Delete(directoryText, true);
            }
        }
    }

    /// <summary>拼接两段字节路径（左段结尾无斜杠时补一个斜杠）。</summary>
    private static byte[] JoinBytes(byte[] left, byte[] right)
    {
        byte[] result = new byte[left.Length + 1 + right.Length];
        Array.Copy(left, 0, result, 0, left.Length);
        result[left.Length] = (byte)'/';
        Array.Copy(right, 0, result, left.Length + 1, right.Length);
        return result;
    }
}
