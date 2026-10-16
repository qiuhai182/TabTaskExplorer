using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>批量改名名称计算测试。</summary>
public class BatchRenamePlannerTests
{
    [Fact]
    public void ReplacesTextInStem()
    {
        BatchRenameOptions options = new BatchRenameOptions();
        options.FindText = "report";
        options.ReplaceText = "报表";

        string result = BatchRenamePlanner.BuildNewName("report(1).txt", options, 1);

        Assert.Equal("报表(1).txt", result);
    }

    [Fact]
    public void AddsPrefixSuffixAndSequence()
    {
        BatchRenameOptions options = new BatchRenameOptions();
        options.PrefixText = "P-";
        options.SuffixText = "-S";
        options.UseSequence = true;

        string result = BatchRenamePlanner.BuildNewName("a.txt", options, 5);

        Assert.Equal("P-a-S(5).txt", result);
    }

    [Fact]
    public void ChangesExtensionWithAndWithoutDot()
    {
        BatchRenameOptions withDot = new BatchRenameOptions();
        withDot.NewExtension = ".png";
        Assert.Equal("photo.png", BatchRenamePlanner.BuildNewName("photo.jpeg", withDot, 1));

        BatchRenameOptions withoutDot = new BatchRenameOptions();
        withoutDot.NewExtension = "png";
        Assert.Equal("photo.png", BatchRenamePlanner.BuildNewName("photo.jpeg", withoutDot, 1));
    }

    [Fact]
    public void KeepsExtensionWhenNotSet()
    {
        BatchRenameOptions options = new BatchRenameOptions();
        options.PrefixText = "新_";

        string result = BatchRenamePlanner.BuildNewName("文档.docx", options, 1);

        Assert.Equal("新_文档.docx", result);
    }
}
