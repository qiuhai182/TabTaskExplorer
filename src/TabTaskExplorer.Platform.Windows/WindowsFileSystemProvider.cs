using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 平台的文件系统实现：删除时移入回收站，其余沿用跨平台默认实现。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsFileSystemProvider : DefaultFileSystemProvider
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_SILENT = 0x04;

    public override void Delete(string path)
    {
        try
        {
            if (DeleteToRecycleBin(path))
            {
                return;
            }
        }
        catch
        {
            // 回收站不可用时退回直接删除
        }
        base.Delete(path);
    }

    /// <summary>调用系统外壳接口把文件或文件夹移入回收站。</summary>
    private static bool DeleteToRecycleBin(string path)
    {
        SHFILEOPSTRUCT operation = new SHFILEOPSTRUCT();
        operation.hwnd = IntPtr.Zero;
        operation.wFunc = FO_DELETE;
        operation.pFrom = path + "\0";
        operation.pTo = null;
        operation.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT);
        int result = SHFileOperation(ref operation);
        return result == 0 && !operation.fAnyOperationsAborted;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
