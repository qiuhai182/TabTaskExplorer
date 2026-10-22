using System;
using System.Collections.Generic;
using System.IO;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>
/// 压缩包虚拟文件系统提供者测试：目录聚合浏览、包内增删改、跨界复制与压缩包根解析。
/// </summary>
public class ArchiveFileSystemProviderTests : IDisposable
{
    private readonly string tempRoot;
    private readonly string sourceDir;
    private readonly string zipPath;
    private readonly ArchiveService service;
    private readonly ArchiveFileSystemProvider provider;

    public ArchiveFileSystemProviderTests()
    {
        this.tempRoot = Path.Combine(Path.GetTempPath(), "archive-provider-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempRoot);

        this.sourceDir = Path.Combine(this.tempRoot, "源");
        Directory.CreateDirectory(Path.Combine(this.sourceDir, "子目录"));
        File.WriteAllText(Path.Combine(this.sourceDir, "你好.txt"), "中文内容abc");
        File.WriteAllBytes(Path.Combine(this.sourceDir, "子目录", "内部.dat"), CreatePatternBytes(3000));

        this.zipPath = Path.Combine(this.tempRoot, "测试包.zip");
        this.service = new ArchiveService(new DefaultArchivePathAccess());
        this.service.Create(this.zipPath, new List<string> { this.sourceDir }, new ArchiveCreateOptions());

        this.provider = new ArchiveFileSystemProvider(this.service, this.zipPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.tempRoot, true);
        }
        catch
        {
            // 测试临时目录清理失败不影响测试结论
        }
    }

    private static byte[] CreatePatternBytes(int count)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            bytes[i] = (byte)(i % 251);
        }
        return bytes;
    }

    private string Virtual(params string[] parts)
    {
        string path = this.zipPath;
        foreach (string part in parts)
        {
            path = path + Path.DirectorySeparatorChar + part;
        }
        return path;
    }

    [Fact]
    public void ListDirectory_Root_AggregatesImplicitDirectory()
    {
        // zip 不一定写显式目录条目，根列表应由 "源/..." 子路径推导出"源"目录
        IReadOnlyList<FileEntry> rootEntries = this.provider.ListDirectory(this.zipPath);
        Assert.Single(rootEntries);
        Assert.Equal("源", rootEntries[0].Name);
        Assert.True(rootEntries[0].IsDirectory);
        Assert.Equal(this.Virtual("源"), rootEntries[0].FullPath);

        IReadOnlyList<FileEntry> sourceEntries = this.provider.ListDirectory(this.Virtual("源"));
        Assert.Equal(2, sourceEntries.Count);
        Assert.Contains(sourceEntries, entry => entry.Name == "你好.txt" && !entry.IsDirectory);
        Assert.Contains(sourceEntries, entry => entry.Name == "子目录" && entry.IsDirectory);

        IReadOnlyList<FileEntry> subEntries = this.provider.ListDirectory(this.Virtual("源", "子目录"));
        Assert.Single(subEntries);
        Assert.Equal("内部.dat", subEntries[0].Name);
        Assert.Equal(3000, subEntries[0].LengthBytes);
    }

    [Fact]
    public void CreateDirectory_Rename_Delete_GoThroughModifyChain()
    {
        // 包内新建文件夹
        this.provider.CreateDirectory(this.Virtual("新目录"));
        Assert.Contains(this.service.ListEntries(this.zipPath),
            entry => entry.Name == "新目录" && entry.IsDirectory);

        // 重命名文件条目（数据不变）
        this.provider.Rename(this.Virtual("源", "你好.txt"), "改名.txt");
        Assert.Contains(this.service.ListEntries(this.zipPath), entry => entry.Name == "源/改名.txt");

        // 删除整个"源"目录：连同其下全部条目一并删除
        this.provider.Delete(this.Virtual("源"));
        IReadOnlyList<ArchiveEntry> remaining = this.service.ListEntries(this.zipPath);
        Assert.DoesNotContain(remaining, entry => entry.Name.StartsWith("源"));
        Assert.Contains(remaining, entry => entry.Name == "新目录");
    }

    [Fact]
    public void CopyOut_FileAndDirectory_KeepOriginalName()
    {
        string outDir = Path.Combine(this.tempRoot, "输出");
        Directory.CreateDirectory(outDir);

        // 单文件：落位为原名称（不含包内父路径）
        this.provider.Copy(this.Virtual("源", "你好.txt"), outDir);
        string extractedFile = Path.Combine(outDir, "你好.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal("中文内容abc", File.ReadAllText(extractedFile));

        // 目录：落位为原文件夹名并保留内部结构
        string outDir2 = Path.Combine(this.tempRoot, "输出2");
        Directory.CreateDirectory(outDir2);
        this.provider.Copy(this.Virtual("源"), outDir2);
        Assert.True(File.Exists(Path.Combine(outDir2, "源", "你好.txt")));
        Assert.True(File.Exists(Path.Combine(outDir2, "源", "子目录", "内部.dat")));
    }

    [Fact]
    public void CopyIn_AddsExternalFileIntoArchiveSubFolder()
    {
        string externalFile = Path.Combine(this.tempRoot, "外来文件.txt");
        File.WriteAllText(externalFile, "外部内容");

        this.provider.Copy(externalFile, this.Virtual("源"));

        IReadOnlyList<ArchiveEntry> entries = this.service.ListEntries(this.zipPath);
        Assert.Contains(entries, entry => entry.Name == "源/外来文件.txt");
        Assert.True(File.Exists(externalFile), "复制进压缩包不应删除源文件");
    }

    [Fact]
    public void MoveOut_DeletesArchiveSourceEntry()
    {
        string outDir = Path.Combine(this.tempRoot, "移动输出");
        Directory.CreateDirectory(outDir);

        this.provider.Move(this.Virtual("源", "你好.txt"), outDir);

        Assert.True(File.Exists(Path.Combine(outDir, "你好.txt")));
        Assert.DoesNotContain(this.service.ListEntries(this.zipPath), entry => entry.Name == "源/你好.txt");
    }

    [Fact]
    public void TryCreate_FindsArchiveRootByWalkingUp_AndRejectsNormalPath()
    {
        // 压缩包根本身
        Assert.True(ArchiveFileSystemProvider.TryCreate(this.service, this.zipPath, out ArchiveFileSystemProvider? rootProvider));
        Assert.Equal(this.zipPath, rootProvider!.ArchiveRootPath);

        // 包内虚拟路径：向上找到压缩包根
        Assert.True(ArchiveFileSystemProvider.TryCreate(this.service, this.Virtual("源", "你好.txt"), out ArchiveFileSystemProvider? childProvider));
        Assert.Equal(this.zipPath, childProvider!.ArchiveRootPath);

        // 普通文件路径：向上找不到任何压缩包
        Assert.False(ArchiveFileSystemProvider.TryCreate(this.service, Path.Combine(this.tempRoot, "你好.txt"), out ArchiveFileSystemProvider? noneProvider));
        Assert.Null(noneProvider);
    }

    [Fact]
    public void OwnsAndIsRoot_DistinguishArchivePaths()
    {
        Assert.True(this.provider.IsRoot(this.zipPath));
        Assert.True(this.provider.OwnsPath(this.zipPath));
        Assert.True(this.provider.OwnsPath(this.Virtual("源", "你好.txt")));
        Assert.False(this.provider.OwnsPath(Path.Combine(this.tempRoot, "你好.txt")));
        Assert.False(this.provider.OwnsPath(string.Empty));
    }
}
