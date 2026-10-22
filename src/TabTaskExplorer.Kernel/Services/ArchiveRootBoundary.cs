using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 压缩包根目录导航边界：以"压缩包文件路径本身"为根，
/// 文件标签页在压缩包根时禁止继续向上导航（避免离开压缩包后仍用压缩包提供器列出真实目录）。
/// </summary>
public sealed class ArchiveRootBoundary : IArchiveNavigationBoundary
{
    private readonly string archiveRootPath;

    public ArchiveRootBoundary(string archiveRootPath)
    {
        this.archiveRootPath = archiveRootPath ?? string.Empty;
    }

    /// <summary>给定路径是否正好是该压缩包的根（压缩包文件本身）。</summary>
    public bool IsArchiveRoot(string path)
    {
        return !string.IsNullOrEmpty(path)
            && string.Equals(path, this.archiveRootPath, System.StringComparison.Ordinal);
    }
}
