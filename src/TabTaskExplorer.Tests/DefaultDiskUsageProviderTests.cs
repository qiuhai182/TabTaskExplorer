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

    [Fact]
    public void ComputeSizeWithSnapshotReusesUnchangedDirectoryTree()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            // 根目录放一个文件，子目录里再放一个文件：正常全扫应记录根与子目录两条
            File.WriteAllText(Path.Combine(tempDirectory, "根文件.txt"), "1234567890");
            string subDirectory = Path.Combine(tempDirectory, "子目录");
            Directory.CreateDirectory(subDirectory);
            File.WriteAllText(Path.Combine(subDirectory, "子文件.txt"), "12345");

            DefaultDiskUsageProvider provider = new DefaultDiskUsageProvider();
            DiskUsageSnapshot snapshot = new DiskUsageSnapshot();

            long firstSize = provider.ComputeSize(tempDirectory, CancellationToken.None, null, snapshot, false);
            long scannedAfterFirst = snapshot.ScannedDirectoryCount;
            long reusedAfterFirst = snapshot.ReusedDirectoryCount;

            // 目录没有变化：第二次扫描整棵子树复用，不再重扫也不回写
            long secondSize = provider.ComputeSize(tempDirectory, CancellationToken.None, null, snapshot, false);

            Assert.Equal(15, firstSize);
            Assert.Equal(firstSize, secondSize);
            Assert.Equal(2, scannedAfterFirst);
            Assert.Equal(0, reusedAfterFirst);
            Assert.True(snapshot.ReusedDirectoryCount > reusedAfterFirst);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void ComputeSizeWithSnapshotRescansChangedDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            string subDirectory = Path.Combine(tempDirectory, "子目录");
            Directory.CreateDirectory(subDirectory);
            File.WriteAllText(Path.Combine(subDirectory, "原有文件.txt"), "1234567890");

            DefaultDiskUsageProvider provider = new DefaultDiskUsageProvider();
            DiskUsageSnapshot snapshot = new DiskUsageSnapshot();

            long firstSize = provider.ComputeSize(subDirectory, CancellationToken.None, null, snapshot, false);
            long scannedAfterFirst = snapshot.ScannedDirectoryCount;
            long reusedAfterFirst = snapshot.ReusedDirectoryCount;

            // 目录里新增一个文件并更新修改时间：修改过的目录必须重新统计
            File.WriteAllText(Path.Combine(subDirectory, "新增文件.txt"), "1234567");
            Directory.SetLastWriteTimeUtc(subDirectory, DateTime.UtcNow);
            long secondSize = provider.ComputeSize(subDirectory, CancellationToken.None, null, snapshot, false);

            Assert.Equal(10, firstSize);
            Assert.Equal(17, secondSize);
            Assert.True(snapshot.ScannedDirectoryCount > scannedAfterFirst);
            Assert.Equal(reusedAfterFirst, snapshot.ReusedDirectoryCount);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void ComputeSizeWithExpiredSnapshotRecordRescansFully()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "根文件.txt"), "1234567890");
            string subDirectory = Path.Combine(tempDirectory, "子目录");
            Directory.CreateDirectory(subDirectory);
            File.WriteAllText(Path.Combine(subDirectory, "子文件.txt"), "12345");

            DefaultDiskUsageProvider provider = new DefaultDiskUsageProvider();
            DiskUsageSnapshot snapshot = new DiskUsageSnapshot();
            // 把记录有效期压到 1 毫秒并等待其过期：验证过期记录不再复用
            snapshot.RecordTtl = TimeSpan.FromMilliseconds(1);

            long firstSize = provider.ComputeSize(tempDirectory, CancellationToken.None, null, snapshot, false);
            long scannedAfterFirst = snapshot.ScannedDirectoryCount;
            Thread.Sleep(50);
            long secondSize = provider.ComputeSize(tempDirectory, CancellationToken.None, null, snapshot, false);

            Assert.Equal(firstSize, secondSize);
            Assert.True(snapshot.ScannedDirectoryCount > scannedAfterFirst);
            Assert.Equal(0, snapshot.ReusedDirectoryCount);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void ComputeSizeWithForceFullIgnoresSnapshotReuse()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "根文件.txt"), "1234567890");
            string subDirectory = Path.Combine(tempDirectory, "子目录");
            Directory.CreateDirectory(subDirectory);
            File.WriteAllText(Path.Combine(subDirectory, "子文件.txt"), "12345");

            DefaultDiskUsageProvider provider = new DefaultDiskUsageProvider();
            DiskUsageSnapshot snapshot = new DiskUsageSnapshot();

            long firstSize = provider.ComputeSize(tempDirectory, CancellationToken.None, null, snapshot, false);
            long scannedAfterFirst = snapshot.ScannedDirectoryCount;
            long reusedAfterFirst = snapshot.ReusedDirectoryCount;

            // 强制完全重扫：忽略复用判定重新统计，但仍把新结果回写快照
            long secondSize = provider.ComputeSize(tempDirectory, CancellationToken.None, null, snapshot, true);

            Assert.Equal(firstSize, secondSize);
            Assert.Equal(scannedAfterFirst + 2, snapshot.ScannedDirectoryCount);
            Assert.Equal(reusedAfterFirst, snapshot.ReusedDirectoryCount);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void SnapshotSaveAndLoadRoundTripKeepsRecords()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string snapshotPath = Path.Combine(tempDirectory, "快照.tsv.gz");
        try
        {
            DiskUsageSnapshot snapshot = new DiskUsageSnapshot();
            // 一条完整记录（路径含空格与中文，验证持久化不丢字符）与一条不完整记录
            snapshot.Record(@"C:\测试\含空格 目录", 123456789L, 987654321L, true);
            snapshot.Record(@"C:\测试\不完整", 222L, 333L, false);
            snapshot.Save(snapshotPath);

            DiskUsageSnapshot loaded = new DiskUsageSnapshot();
            loaded.Load(snapshotPath);

            Assert.Equal(2, loaded.Count);
            long recordedTicks;
            long reusedSize;
            Assert.True(loaded.TryGetRecordedMtime(@"C:\测试\含空格 目录", out recordedTicks));
            Assert.Equal(123456789L, recordedTicks);
            Assert.True(loaded.TryReuse(@"C:\测试\含空格 目录", 123456789L, out reusedSize));
            Assert.Equal(987654321L, reusedSize);
            // 不完整的记录即使修改时间一致也必须重新统计
            Assert.False(loaded.TryReuse(@"C:\测试\不完整", 222L, out reusedSize));
            // 修改时间不一致：不能复用
            Assert.False(loaded.TryReuse(@"C:\测试\含空格 目录", 999L, out reusedSize));
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }
}
