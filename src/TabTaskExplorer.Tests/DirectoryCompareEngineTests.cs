using System;
using System.IO;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>目录比对引擎测试：递归、仅单侧、内容比较、时间比较与类型不一致。</summary>
public class DirectoryCompareEngineTests
{
    /// <summary>创建一对临时目录，并在测试结束后删除。</summary>
    private static void PrepareDirectories(out string leftRoot, out string rightRoot)
    {
        string root = Path.Combine(Path.GetTempPath(), "tabx-compare-" + Guid.NewGuid().ToString("N"));
        leftRoot = Path.Combine(root, "left");
        rightRoot = Path.Combine(root, "right");
        Directory.CreateDirectory(leftRoot);
        Directory.CreateDirectory(rightRoot);
    }

    [Fact]
    public void Compare_ReportsSameDifferentAndOneSidedEntries()
    {
        string leftRoot;
        string rightRoot;
        PrepareDirectories(out leftRoot, out rightRoot);
        try
        {
            File.WriteAllText(Path.Combine(leftRoot, "same.txt"), "same");
            File.WriteAllText(Path.Combine(rightRoot, "same.txt"), "same");
            File.WriteAllText(Path.Combine(leftRoot, "changed.txt"), "left-version");
            File.WriteAllText(Path.Combine(rightRoot, "changed.txt"), "right-version");
            File.WriteAllText(Path.Combine(leftRoot, "left-only.txt"), "left");
            File.WriteAllText(Path.Combine(rightRoot, "right-only.txt"), "right");

            Directory.CreateDirectory(Path.Combine(leftRoot, "common"));
            Directory.CreateDirectory(Path.Combine(rightRoot, "common"));
            File.WriteAllText(Path.Combine(leftRoot, "common", "nested.txt"), "nested");
            File.WriteAllText(Path.Combine(rightRoot, "common", "nested.txt"), "nested");

            DirectoryCompareEngine engine = new DirectoryCompareEngine();
            DirectoryCompareOptions options = new DirectoryCompareOptions();
            options.CompareFileContent = true;
            DirectoryCompareResult result = engine.Compare(leftRoot, rightRoot, options, CancellationToken.None);

            Assert.Equal(6, result.Entries.Count);
            Assert.Equal(3, result.SameCount);
            Assert.Equal(1, result.DifferentCount);
            Assert.Equal(1, result.LeftOnlyCount);
            Assert.Equal(1, result.RightOnlyCount);

            DirectoryCompareEntry changed = FindEntry(result, "changed.txt");
            Assert.Equal(CompareEntryStatus.Different, changed.Status);
            DirectoryCompareEntry leftOnly = FindEntry(result, "left-only.txt");
            Assert.Equal(CompareEntryStatus.LeftOnly, leftOnly.Status);
            Assert.Equal(string.Empty, leftOnly.RightPath);
            DirectoryCompareEntry nested = FindEntry(result, "common/nested.txt");
            Assert.Equal(CompareEntryStatus.Same, nested.Status);
        }
        finally
        {
            DeleteParent(leftRoot);
        }
    }

    [Fact]
    public void Compare_WithoutContentUsesTimestampTolerance()
    {
        string leftRoot;
        string rightRoot;
        PrepareDirectories(out leftRoot, out rightRoot);
        try
        {
            string leftFile = Path.Combine(leftRoot, "time.txt");
            string rightFile = Path.Combine(rightRoot, "time.txt");
            File.WriteAllText(leftFile, "same-size");
            File.WriteAllText(rightFile, "same-size");
            File.SetLastWriteTimeUtc(leftFile, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(rightFile, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            DirectoryCompareEngine engine = new DirectoryCompareEngine();
            DirectoryCompareOptions options = new DirectoryCompareOptions();
            options.ModifiedTimeToleranceSeconds = 0;
            DirectoryCompareResult result = engine.Compare(leftRoot, rightRoot, options, CancellationToken.None);

            DirectoryCompareEntry entry = FindEntry(result, "time.txt");
            Assert.Equal(CompareEntryStatus.RightNewer, entry.Status);
            Assert.Equal(1, result.DifferentCount);
        }
        finally
        {
            DeleteParent(leftRoot);
        }
    }

    [Fact]
    public void Compare_TypeMismatchAndMissingRoot()
    {
        string leftRoot;
        string rightRoot;
        PrepareDirectories(out leftRoot, out rightRoot);
        try
        {
            Directory.CreateDirectory(Path.Combine(leftRoot, "item"));
            File.WriteAllText(Path.Combine(rightRoot, "item"), "file");

            DirectoryCompareEngine engine = new DirectoryCompareEngine();
            DirectoryCompareOptions options = new DirectoryCompareOptions();
            DirectoryCompareResult result = engine.Compare(leftRoot, rightRoot, options, CancellationToken.None);

            DirectoryCompareEntry entry = FindEntry(result, "item");
            Assert.Equal(CompareEntryStatus.TypeMismatch, entry.Status);
            Assert.Equal(1, result.DifferentCount);

            // 右侧根目录不存在：全部条目按仅左侧处理，供“进入单侧目录”继续浏览
            DirectoryCompareResult missingRight = engine.Compare(leftRoot, Path.Combine(leftRoot, "no-such-dir"),
                options, CancellationToken.None);
            Assert.Equal(1, missingRight.LeftOnlyCount);
            Assert.Equal(0, missingRight.RightOnlyCount);
        }
        finally
        {
            DeleteParent(leftRoot);
        }
    }

    private static DirectoryCompareEntry FindEntry(DirectoryCompareResult result, string relativePath)
    {
        foreach (DirectoryCompareEntry entry in result.Entries)
        {
            if (string.Equals(entry.RelativePath, relativePath, StringComparison.Ordinal))
            {
                return entry;
            }
        }
        throw new InvalidOperationException("未找到比对条目：" + relativePath);
    }

    private static void DeleteParent(string path)
    {
        DirectoryInfo? parent = Directory.GetParent(path);
        if (parent != null && parent.Exists)
        {
            parent.Delete(true);
        }
    }
}
