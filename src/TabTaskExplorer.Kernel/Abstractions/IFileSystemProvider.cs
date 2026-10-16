using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 文件系统服务：目录浏览与基础文件管理操作。
/// </summary>
public interface IFileSystemProvider
{
    /// <summary>
    /// 列出指定目录下的全部条目（包含隐藏条目）。
    /// </summary>
    /// <param name="directoryPath">目录完整路径。</param>
    /// <returns>该目录下的全部条目。</returns>
    IReadOnlyList<FileEntry> ListDirectory(string directoryPath);

    /// <summary>
    /// 按选项列出目录条目。
    /// </summary>
    /// <param name="directoryPath">目录完整路径。</param>
    /// <param name="options">列举选项，例如是否包含隐藏条目。</param>
    /// <returns>符合条件的条目列表。</returns>
    IReadOnlyList<FileEntry> ListDirectory(string directoryPath, DirectoryListingOptions options);

    /// <summary>
    /// 创建文件夹（含上级目录）。
    /// </summary>
    /// <param name="directoryPath">要创建的文件夹完整路径。</param>
    void CreateDirectory(string directoryPath);

    /// <summary>
    /// 重命名文件或文件夹（保持在同一目录内）。
    /// </summary>
    /// <param name="path">文件或文件夹的当前完整路径。</param>
    /// <param name="newName">新的名称（不含路径）。</param>
    void Rename(string path, string newName);

    /// <summary>
    /// 删除文件或文件夹。Windows 实现会移入回收站，其他平台直接删除。
    /// </summary>
    /// <param name="path">要删除的完整路径。</param>
    void Delete(string path);

    /// <summary>
    /// 复制文件或文件夹到目标目录（保留原名称，目标同名时覆盖）。
    /// </summary>
    /// <param name="sourcePath">源文件或文件夹的完整路径。</param>
    /// <param name="targetDirectory">目标目录的完整路径。</param>
    void Copy(string sourcePath, string targetDirectory);

    /// <summary>
    /// 移动文件或文件夹到目标目录（跨卷时自动回退为先复制后删除）。
    /// </summary>
    /// <param name="sourcePath">源文件或文件夹的完整路径。</param>
    /// <param name="targetDirectory">目标目录的完整路径。</param>
    void Move(string sourcePath, string targetDirectory);
}
