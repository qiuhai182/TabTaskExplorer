using System;
using System.IO;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>文件复制与移动的递归行为测试。</summary>
public class FileTransferTests
{
    [Fact]
    public void CopyDirectoryCopiesWholeTree()
    {
        string sourceDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string targetDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "子目录"));
        File.WriteAllText(Path.Combine(sourceDirectory, "根文件.txt"), "根内容");
        File.WriteAllText(Path.Combine(sourceDirectory, "子目录", "子文件.txt"), "子内容");
        try
        {
            DefaultFileSystemProvider provider = new DefaultFileSystemProvider();
            provider.Copy(sourceDirectory, targetDirectory);

            // 复制语义与资源管理器一致：源文件夹整体作为子文件夹出现在目标目录下
            string copiedRoot = Path.Combine(targetDirectory, Path.GetFileName(sourceDirectory));
            Assert.True(Directory.Exists(copiedRoot));
            string copiedFile = Path.Combine(copiedRoot, "根文件.txt");
            string copiedNested = Path.Combine(copiedRoot, "子目录", "子文件.txt");
            Assert.True(File.Exists(copiedFile));
            Assert.Equal("根内容", File.ReadAllText(copiedFile));
            Assert.True(File.Exists(copiedNested));
            Assert.Equal("子内容", File.ReadAllText(copiedNested));
        }
        finally
        {
            Directory.Delete(sourceDirectory, true);
            Directory.Delete(targetDirectory, true);
        }
    }

    [Fact]
    public void MoveFileRemovesSource()
    {
        string sourceDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string targetDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(targetDirectory);
        string sourceFile = Path.Combine(sourceDirectory, "移动我.txt");
        File.WriteAllText(sourceFile, "移动内容");
        try
        {
            DefaultFileSystemProvider provider = new DefaultFileSystemProvider();
            provider.Move(sourceFile, targetDirectory);

            Assert.False(File.Exists(sourceFile));
            string movedFile = Path.Combine(targetDirectory, "移动我.txt");
            Assert.True(File.Exists(movedFile));
            Assert.Equal("移动内容", File.ReadAllText(movedFile));
        }
        finally
        {
            Directory.Delete(sourceDirectory, true);
            Directory.Delete(targetDirectory, true);
        }
    }
}
