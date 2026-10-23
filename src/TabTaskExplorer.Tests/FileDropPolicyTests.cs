using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Input;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using TabTaskExplorer.Protocol.Ssh;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>文件拖放策略测试：同目录 / 子目录判定、落点合法性、拖放效果与提供者参与范围。</summary>
public class FileDropPolicyTests : IDisposable
{
    private readonly string tempRoot;
    private readonly string sourceDirectory;
    private readonly string childDirectory;
    private readonly string siblingDirectory;

    public FileDropPolicyTests()
    {
        this.tempRoot = Path.Combine(Path.GetTempPath(), "file-drop-policy-tests-" + Guid.NewGuid().ToString("N"));
        this.sourceDirectory = Path.Combine(this.tempRoot, "源目录");
        this.childDirectory = Path.Combine(this.sourceDirectory, "子目录");
        this.siblingDirectory = Path.Combine(this.tempRoot, "源目录扩展名不同");
        Directory.CreateDirectory(this.childDirectory);
        Directory.CreateDirectory(this.siblingDirectory);
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

    /// <summary>同目录判定：忽略大小写与尾部分隔符。</summary>
    [Fact]
    public void IsSameDirectory_忽略大小写与尾分隔符_判定同一目录()
    {
        Assert.True(FileDropPolicy.IsSameDirectory(@"C:\Windows\Temp", @"c:\windows\temp\"));
        Assert.True(FileDropPolicy.IsSameDirectory(this.sourceDirectory, this.sourceDirectory + Path.DirectorySeparatorChar));
        Assert.False(FileDropPolicy.IsSameDirectory(this.sourceDirectory, this.childDirectory));
        Assert.False(FileDropPolicy.IsSameDirectory(string.Empty, this.sourceDirectory));
    }

    /// <summary>子目录判定：目标是源的子目录时拒绝，仅共享前缀的不同目录不受影响。</summary>
    [Fact]
    public void IsSelfOrDescendant_目标为源子目录_判定为真()
    {
        Assert.True(FileDropPolicy.IsSelfOrDescendant(this.sourceDirectory, this.childDirectory));
        Assert.True(FileDropPolicy.IsSelfOrDescendant(this.sourceDirectory, this.sourceDirectory));
        Assert.False(FileDropPolicy.IsSelfOrDescendant(@"D:\ab", @"D:\a"));
        Assert.False(FileDropPolicy.IsSelfOrDescendant(this.sourceDirectory, this.siblingDirectory));
    }

    /// <summary>落点合法性：目标目录为空或不存在时拒绝。</summary>
    [Fact]
    public void IsDropTargetValid_目标目录为空或不存在_判定无效()
    {
        List<string> sources = new List<string> { Path.Combine(this.sourceDirectory, "文件.txt") };

        Assert.False(FileDropPolicy.IsDropTargetValid(sources, string.Empty, true));
        Assert.False(FileDropPolicy.IsDropTargetValid(sources, Path.Combine(this.tempRoot, "不存在的目录"), false));
    }

    /// <summary>落点合法性：源与目标同目录、或把文件夹拖进自己的子目录时拒绝。</summary>
    [Fact]
    public void IsDropTargetValid_同目录或拖入自身子目录_判定无效()
    {
        List<string> fileSources = new List<string> { Path.Combine(this.sourceDirectory, "文件.txt") };
        List<string> directorySources = new List<string> { this.sourceDirectory };

        Assert.False(FileDropPolicy.IsDropTargetValid(fileSources, this.sourceDirectory, true));
        Assert.False(FileDropPolicy.IsDropTargetValid(directorySources, this.childDirectory, true));
        Assert.False(FileDropPolicy.IsDropTargetValid(new List<string> { this.childDirectory }, this.sourceDirectory, true));
        Assert.True(FileDropPolicy.IsDropTargetValid(fileSources, this.childDirectory, true));
        Assert.True(FileDropPolicy.IsDropTargetValid(directorySources, this.siblingDirectory, true));
    }

    /// <summary>默认效果：同卷移动，跨卷复制（跨卷需要两个盘符，仅 Windows 具备该语义）。</summary>
    [Fact]
    public void ResolveEffects_无修饰键_同卷移动跨卷复制()
    {
        string root = Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString();
        string sameVolumeSource = Path.Combine(root, "拖放测试", "源.txt");
        string sameVolumeTarget = Path.Combine(root, "拖放测试", "目标");

        Assert.Equal(DragDropEffects.Move, FileDropPolicy.ResolveEffects(sameVolumeSource, sameVolumeTarget, KeyModifiers.None));
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        Assert.Equal(DragDropEffects.Copy, FileDropPolicy.ResolveEffects(@"D:\卷二\源.txt", @"C:\卷一\目标", KeyModifiers.None));
        Assert.False(FileDropPolicy.IsSameVolume(@"D:\卷二\源.txt", @"C:\卷一\目标"));
        Assert.True(FileDropPolicy.IsSameVolume(@"C:\卷一\源.txt", @"C:\卷一\目标"));
    }

    /// <summary>修饰键：Ctrl 强制复制、Shift 强制移动，且 Ctrl 优先于 Shift。</summary>
    [Fact]
    public void ResolveEffects_按住修饰键_覆盖默认效果()
    {
        string root = Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString();
        string source = Path.Combine(root, "源.txt");
        string target = Path.Combine(root, "目标");

        Assert.Equal(DragDropEffects.Copy, FileDropPolicy.ResolveEffects(source, target, KeyModifiers.Control));
        Assert.Equal(DragDropEffects.Move, FileDropPolicy.ResolveEffects(source, target, KeyModifiers.Shift));
        Assert.Equal(DragDropEffects.Copy, FileDropPolicy.ResolveEffects(source, target, KeyModifiers.Control | KeyModifiers.Shift));
    }

    /// <summary>参与范围：本机提供者全场景可用，远程提供者仅限标签内部，压缩包提供者全部拒绝。</summary>
    [Fact]
    public void 参与范围_按提供者类型收口()
    {
        IFileSystemProvider localProvider = new DefaultFileSystemProvider();
        IFileSystemProvider remoteProvider = new SftpFileSystemProvider(new RemoteConnectionProfile(), string.Empty);

        Assert.True(FileDropPolicy.CanDragFrom(localProvider));
        Assert.True(FileDropPolicy.CanDropToExternal(localProvider));
        Assert.True(FileDropPolicy.CanDropInsideTab(localProvider));

        Assert.True(FileDropPolicy.CanDragFrom(remoteProvider));
        Assert.False(FileDropPolicy.CanDropToExternal(remoteProvider));
        Assert.True(FileDropPolicy.CanDropInsideTab(remoteProvider));

        string zipPath = Path.Combine(this.tempRoot, "测试包.zip");
        ArchiveService archiveService = new ArchiveService(new DefaultArchivePathAccess());
        archiveService.Create(zipPath, new List<string> { this.childDirectory }, new ArchiveCreateOptions());
        IFileSystemProvider archiveProvider = new ArchiveFileSystemProvider(archiveService, zipPath);

        Assert.False(FileDropPolicy.CanDragFrom(archiveProvider));
        Assert.False(FileDropPolicy.CanDropToExternal(archiveProvider));
        Assert.False(FileDropPolicy.CanDropInsideTab(archiveProvider));
    }

    /// <summary>日志文案：效果与修饰键都要给出纯中文的可读描述。</summary>
    [Fact]
    public void 描述输出_效果与修饰键为中文可读文本()
    {
        Assert.Equal("移动", FileDropPolicy.DescribeEffects(DragDropEffects.Move));
        Assert.Equal("复制", FileDropPolicy.DescribeEffects(DragDropEffects.Copy));
        Assert.Equal("禁止放置", FileDropPolicy.DescribeEffects(DragDropEffects.None));
        Assert.Equal("无", FileDropPolicy.DescribeModifiers(KeyModifiers.None));
        Assert.Equal("Ctrl+Shift", FileDropPolicy.DescribeModifiers(KeyModifiers.Control | KeyModifiers.Shift));
    }
}
