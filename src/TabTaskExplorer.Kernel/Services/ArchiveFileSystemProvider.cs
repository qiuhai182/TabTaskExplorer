using System;
using System.Collections.Generic;
using System.IO;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 压缩包虚拟文件系统提供者：把压缩包当作一个可浏览、可增删改的目录树，
/// 让文件标签页直接复用现有的浏览 / 重命名 / 删除 / 粘贴逻辑（"预览压缩包"能力）。
/// 虚拟路径 = 压缩包文件路径 + 平台分隔符 + 包内相对路径（包内统一用 '/'，展示时换成分隔符）；
/// 压缩包本身即目录树的根（虚拟路径等于压缩包路径时表示包根）。
/// </summary>
public sealed class ArchiveFileSystemProvider : IFileSystemProvider
{
    private readonly IArchiveService archiveService;
    private readonly string archiveRootPath;

    public ArchiveFileSystemProvider(IArchiveService archiveService, string archiveRootPath)
    {
        this.archiveService = archiveService;
        this.archiveRootPath = archiveRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>压缩包根路径（虚拟目录树顶层，值为压缩包文件本身）。</summary>
    public string ArchiveRootPath
    {
        get { return this.archiveRootPath; }
    }

    /// <summary>给定路径是否正好是压缩包根本身。</summary>
    public bool IsRoot(string path)
    {
        return !string.IsNullOrEmpty(path)
            && string.Equals(path, this.archiveRootPath, StringComparison.Ordinal);
    }

    /// <summary>给定路径是否位于本压缩包虚拟目录树内（含压缩包根本身）。</summary>
    public bool OwnsPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        if (this.IsRoot(path))
        {
            return true;
        }
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.StartsWith(this.archiveRootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// 沿路径向上寻找真实存在的压缩包文件，命中即创建对应的虚拟文件系统提供者。
    /// 用于打开压缩包标签（普通标签双击压缩包）与解析来自压缩包的粘贴源；
    /// 路径不在任何压缩包内时返回 false。
    /// </summary>
    public static bool TryCreate(IArchiveService archiveService, string path, out ArchiveFileSystemProvider? provider)
    {
        provider = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        string? candidate = path;
        while (!string.IsNullOrEmpty(candidate))
        {
            if (archiveService.IsArchivePath(candidate) && archiveService.Probe(candidate) != null)
            {
                provider = new ArchiveFileSystemProvider(archiveService, candidate);
                return true;
            }
            string? parent = Path.GetDirectoryName(candidate);
            if (string.Equals(parent, candidate, StringComparison.Ordinal))
            {
                break;
            }
            candidate = parent;
        }
        return false;
    }

    // ================================================================
    //  浏览
    // ================================================================

    public IReadOnlyList<FileEntry> ListDirectory(string directoryPath)
    {
        return this.ListDirectory(directoryPath, new DirectoryListingOptions());
    }

    public IReadOnlyList<FileEntry> ListDirectory(string directoryPath, DirectoryListingOptions options)
    {
        string innerPrefix = this.ToInnerPath(directoryPath);
        string matchPrefix = innerPrefix.Length == 0 ? string.Empty : innerPrefix + "/";
        IReadOnlyList<ArchiveEntry> entries = this.archiveService.ListEntries(this.archiveRootPath);

        // 聚合直接子条目：归档里没有显式目录条目时，由更深条目的父路径推导出目录
        Dictionary<string, FileEntry> children = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            ArchiveEntry entry = entries[i];
            string name = entry.Name;
            if (matchPrefix.Length > 0)
            {
                if (!name.StartsWith(matchPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                name = name.Substring(matchPrefix.Length);
            }
            if (name.Length == 0)
            {
                continue;
            }

            int slash = name.IndexOf('/');
            string childName;
            bool childIsDirectory;
            if (slash >= 0)
            {
                childName = name.Substring(0, slash);
                childIsDirectory = true;
            }
            else
            {
                childName = name;
                childIsDirectory = entry.IsDirectory;
            }

            FileEntry? existing;
            if (childIsDirectory)
            {
                if (!children.TryGetValue(childName, out existing))
                {
                    children[childName] = BuildEntry(directoryPath, childName, true, 0, entry.ModifiedTimeUtc);
                }
                else
                {
                    // 同名冲突时目录优先（条目数据不可信，按目录呈现）
                    existing.IsDirectory = true;
                    existing.LengthBytes = 0;
                }
            }
            else if (!children.ContainsKey(childName))
            {
                children[childName] = BuildEntry(directoryPath, childName, false, entry.LengthBytes, entry.ModifiedTimeUtc);
            }
        }

        List<FileEntry> result = new List<FileEntry>(children.Count);
        foreach (KeyValuePair<string, FileEntry> pair in children)
        {
            result.Add(pair.Value);
        }
        return result;
    }

    private static FileEntry BuildEntry(string directoryPath, string name, bool isDirectory,
        long lengthBytes, DateTime modifiedTimeUtc)
    {
        FileEntry entry = new FileEntry();
        entry.Name = name;
        entry.FullPath = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar + name;
        entry.IsDirectory = isDirectory;
        entry.IsHidden = false;
        entry.LengthBytes = lengthBytes;
        entry.ModifiedTimeUtc = modifiedTimeUtc;
        return entry;
    }

    // ================================================================
    //  修改（走 IArchiveService 修改链：临时文件改写后原地替换）
    // ================================================================

    public void CreateDirectory(string directoryPath)
    {
        string inner = this.ToInnerPath(directoryPath);
        this.archiveService.AddDirectory(this.archiveRootPath, inner);
    }

    public void Rename(string path, string newName)
    {
        string inner = this.ToInnerPath(path);
        if (inner.Length == 0)
        {
            throw new InvalidOperationException("不能重命名压缩包根本身");
        }
        int lastSlash = inner.LastIndexOf('/');
        string newInner = lastSlash >= 0 ? inner.Substring(0, lastSlash + 1) + newName : newName;
        this.archiveService.RenameEntry(this.archiveRootPath, inner, newInner);
    }

    public void Delete(string path)
    {
        string inner = this.ToInnerPath(path);
        if (inner.Length == 0)
        {
            throw new InvalidOperationException("不能删除压缩包根目录");
        }
        List<string> names = new List<string>();
        names.Add(inner);
        this.archiveService.RemoveEntries(this.archiveRootPath, names);
    }

    // ================================================================
    //  复制 / 移动（压缩包内 ? 压缩包外跨界传输）
    // ================================================================

    public void Copy(string sourcePath, string targetDirectory)
    {
        bool sourceInArchive = this.OwnsPath(sourcePath);
        bool targetInArchive = this.OwnsPath(targetDirectory);

        if (sourceInArchive && targetInArchive)
        {
            throw new NotSupportedException("同一压缩包内暂不支持直接复制，请使用重命名或剪切");
        }
        if (sourceInArchive)
        {
            this.CopyOutOfArchive(sourcePath, targetDirectory);
            return;
        }
        if (targetInArchive)
        {
            this.CopyIntoArchive(sourcePath, targetDirectory);
            return;
        }
        throw new NotSupportedException("目标目录不在压缩包内，应交由系统文件系统处理");
    }

    public void Move(string sourcePath, string targetDirectory)
    {
        bool sourceInArchive = this.OwnsPath(sourcePath);
        this.Copy(sourcePath, targetDirectory);

        if (sourceInArchive)
        {
            if (this.IsRoot(sourcePath))
            {
                // 把压缩包自身移动出去：复制完成后删除原压缩包文件
                File.Delete(this.archiveRootPath);
            }
            else
            {
                this.Delete(sourcePath);
            }
            return;
        }
        DeleteRealSource(sourcePath);
    }

    /// <summary>
    /// 压缩包内 → 压缩包外：先解压到临时目录，再按"保留原名称"落位到目标目录
    /// （文件直接落名，文件夹落名后逐层合并进目标同名文件夹，符合文件系统复制语义）。
    /// </summary>
    private void CopyOutOfArchive(string sourcePath, string targetDirectory)
    {
        if (this.IsRoot(sourcePath))
        {
            // 移动的是压缩包文件本身：按普通文件复制
            string fileName = Path.GetFileName(this.archiveRootPath);
            File.Copy(this.archiveRootPath, Path.Combine(targetDirectory, fileName), true);
            return;
        }
        string inner = this.ToInnerPath(sourcePath);
        string tempDir = Path.Combine(Path.GetTempPath(), "TabTaskExplorer", "archive-copy",
            Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tempDir);
        try
        {
            ArchiveExtractOptions options = new ArchiveExtractOptions();
            options.TargetDirectory = tempDir;
            options.Overwrite = true;
            List<string> names = new List<string>();
            names.Add(inner);
            this.archiveService.ExtractEntries(this.archiveRootPath, names, options);

            string stagedPath = Path.Combine(tempDir, inner.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(stagedPath))
            {
                string targetFile = Path.Combine(targetDirectory, Path.GetFileName(stagedPath));
                File.Move(stagedPath, targetFile, true);
            }
            else if (Directory.Exists(stagedPath))
            {
                PlaceDirectoryIntoTarget(stagedPath, targetDirectory);
            }
            else
            {
                throw new FileNotFoundException("未能解出压缩包内条目：" + inner, stagedPath);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // 临时目录清理失败不影响复制结果
            }
        }
    }

    /// <summary>把暂存文件夹落位到目标目录（保留原文件夹名，同名时逐层合并）。</summary>
    private static void PlaceDirectoryIntoTarget(string stagedDirectoryPath, string targetDirectory)
    {
        string directoryName = Path.GetFileName(stagedDirectoryPath);
        string destinationDirectory = Path.Combine(targetDirectory, directoryName);
        Directory.CreateDirectory(destinationDirectory);
        foreach (string subPath in Directory.EnumerateFileSystemEntries(stagedDirectoryPath))
        {
            if (Directory.Exists(subPath))
            {
                PlaceDirectoryIntoTarget(subPath, destinationDirectory);
            }
            else
            {
                string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(subPath));
                File.Move(subPath, destinationFile, true);
            }
        }
    }

    /// <summary>压缩包外 → 压缩包内：把源文件 / 文件夹追加到目标包内目录。</summary>
    private void CopyIntoArchive(string sourcePath, string targetDirectory)
    {
        string destinationInner = this.ToInnerPath(targetDirectory);
        List<string> sources = new List<string>();
        sources.Add(sourcePath);
        this.archiveService.AddPaths(this.archiveRootPath, sources, destinationInner);
    }

    /// <summary>删除压缩包外的真实源（移动跨界时用）。</summary>
    private static void DeleteRealSource(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
        else
        {
            throw new FileNotFoundException("源不存在：" + path, path);
        }
    }

    // ================================================================
    //  包内文件打开（解出临时文件后交给系统默认程序）
    // ================================================================

    /// <summary>把压缩包内的文件解出到独立临时目录并返回临时文件路径（双击打开包内文件用）。</summary>
    public string ExtractFileToTemp(string path)
    {
        string inner = this.ToInnerPath(path);
        if (inner.Length == 0)
        {
            throw new InvalidOperationException("压缩包根目录不是文件");
        }
        string tempDir = Path.Combine(Path.GetTempPath(), "TabTaskExplorer", "archive-open",
            Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tempDir);

        ArchiveExtractOptions options = new ArchiveExtractOptions();
        options.TargetDirectory = tempDir;
        options.Overwrite = true;
        List<string> names = new List<string>();
        names.Add(inner);
        this.archiveService.ExtractEntries(this.archiveRootPath, names, options);

        string extracted = Path.Combine(tempDir, inner.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(extracted))
        {
            throw new FileNotFoundException("未能解出压缩包内文件：" + inner, extracted);
        }
        return extracted;
    }

    // ================================================================
    //  虚拟路径 ? 包内相对路径
    // ================================================================

    /// <summary>把虚拟路径换算为包内相对路径（'/' 分隔；压缩包根本身返回空串）。</summary>
    private string ToInnerPath(string virtualPath)
    {
        if (this.IsRoot(virtualPath))
        {
            return string.Empty;
        }
        string normalized = (virtualPath ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string rootPrefix = this.archiveRootPath + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("路径不在压缩包内：" + virtualPath);
        }
        return normalized.Substring(rootPrefix.Length).Replace(Path.DirectorySeparatorChar, '/');
    }
}
