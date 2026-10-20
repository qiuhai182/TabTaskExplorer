using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>DefaultDiskUsageProvider 的目录统计测试。</summary>
public class DefaultDiskUsageProviderTests
{
    [Fact]
    public void GetTopLevelEntriesListsFilesAndDirectories()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            string subDirectoryA = Path.Combine(tempDirectory, "子目录A");
            string subDirectoryB = Path.Combine(tempDirectory, "子目录B");
            Directory.CreateDirectory(subDirectoryA);
            Directory.CreateDirectory(subDirectoryB);
            string documentPath = Path.Combine(tempDirectory, "文档.txt");
            File.WriteAllText(documentPath, "四个汉字");
            string readmePath = Path.Combine(tempDirectory, "readme.txt");
            File.WriteAllText(readmePath, "hello");

            DefaultDiskUsageProvider provider = new DefaultDiskUsageProvider();
            IReadOnlyList<DiskEntry> entries = provider.GetTopLevelEntries(tempDirectory);

            Assert.Equal(4, entries.Count);
            foreach (DiskEntry entry in entries)
            {
                Assert.Equal(tempDirectory, entry.ParentPath);
                Assert.Equal(Path.Combine(tempDirectory, entry.Name), entry.FullPath);
                if (entry.Name == "子目录A" || entry.Name == "子目录B")
                {
                    Assert.Equal(DiskEntryKind.Directory, entry.EntryType);
                    Assert.Equal(0, entry.SizeBytes);
                }
                else if (entry.Name == "文档.txt")
                {
                    Assert.Equal(DiskEntryKind.File, entry.EntryType);
                    Assert.Equal(12, entry.SizeBytes);
                }
                else if (entry.Name == "readme.txt")
                {
                    Assert.Equal(DiskEntryKind.File, entry.EntryType);
                    Assert.Equal(5, entry.SizeBytes);
                }
                else
                {
                    Assert.Fail("出现了列表之外的条目：" + entry.Name);
                }
            }
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void ComputeSizeReturnsFileSize()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            string filePath = Path.Combine(tempDirectory, "文件.txt");
            File.WriteAllText(filePath, "12345");

            DefaultDiskUsageProvider provider = new DefaultDiskUsageProvider();
            long sizeBytes = provider.ComputeSize(filePath, CancellationToken.None);

            Assert.Equal(5, sizeBytes);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void ComputeSizeSumsNestedDirectoryFiles()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            // 第一层：根目录下直接放一个文件
            string directFilePath = Path.Combine(tempDirectory, "直接文件.txt");
            File.WriteAllText(directFilePath, "1234567890");

            // 第二层：根目录下放一个子目录，子目录里放一个文件
            string subDirectory = Path.Combine(tempDirectory, "子目录");
            Directory.CreateDirectory(subDirectory);
            string subFilePath = Path.Combine(subDirectory, "子文件.txt");
            File.WriteAllText(subFilePath, "12345");

            // 第三层：子目录里再放一层子目录，验证递归统计
            string grandDirectory = Path.Combine(subDirectory, "孙目录");
            Directory.CreateDirectory(grandDirectory);
            string grandFilePath = Path.Combine(grandDirectory, "孙文件.txt");
            File.WriteAllText(grandFilePath, "123");

            DefaultDiskUsageProvider provider = new DefaultDiskUsageProvider();
            long sizeBytes = provider.ComputeSize(tempDirectory, CancellationToken.None);

            Assert.Equal(18, sizeBytes);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }
}
