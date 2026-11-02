using System.Collections.Generic;
using System.Text;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>文本行比对引擎测试：相同、修改配对、单侧行、行内差异与超大文本降级。</summary>
public class TextDiffEngineTests
{
    [Fact]
    public void Compare_IdenticalTextProducesSameRows()
    {
        TextDiffEngine engine = new TextDiffEngine();
        TextCompareResult result = engine.Compare("第一行\n第二行\n", "第一行\n第二行\n");

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(3, result.SameCount);
        Assert.Equal(0, result.ModifiedCount);
        Assert.Equal(0, result.LeftOnlyCount);
        Assert.Equal(0, result.RightOnlyCount);
        foreach (DiffRow row in result.Rows)
        {
            Assert.Equal(DiffRowKind.Same, row.Kind);
        }
    }

    [Fact]
    public void Compare_PairsChangedLinesAndComputesInlineRange()
    {
        TextDiffEngine engine = new TextDiffEngine();
        TextCompareResult result = engine.Compare(
            "alpha\nvalue = 1;\nomega",
            "alpha\nvalue = 2;\nomega");

        Assert.Equal(3, result.Rows.Count);
        DiffRow modified = result.Rows[1];
        Assert.Equal(DiffRowKind.Modified, modified.Kind);
        Assert.Equal(2, modified.LeftLineNumber);
        Assert.Equal(2, modified.RightLineNumber);
        Assert.Equal("value = 1;", modified.LeftText);
        Assert.Equal("value = 2;", modified.RightText);
        Assert.Equal(8, modified.LeftChangedStart);
        Assert.Equal(1, modified.LeftChangedLength);
        Assert.Equal(1, modified.RightChangedLength);
    }

    [Fact]
    public void Compare_DetectsInsertedAndDeletedLines()
    {
        TextDiffEngine engine = new TextDiffEngine();
        TextCompareResult result = engine.Compare(
            "keep\nleft-only\nkeep2",
            "keep\nkeep2\nright-only");

        Assert.Equal(2, result.SameCount);
        Assert.Equal(1, result.LeftOnlyCount);
        Assert.Equal(1, result.RightOnlyCount);
        Assert.Equal(0, result.ModifiedCount);
        Assert.True(result.Rows.Count >= 4);
    }

    [Fact]
    public void Compare_LargeInputFallsBackToLinearAlignment()
    {
        StringBuilder left = new StringBuilder();
        StringBuilder right = new StringBuilder();
        for (int index = 0; index < 5000; index++)
        {
            left.Append("line ").Append(index).Append('\n');
            right.Append("line ").Append(index).Append('\n');
        }
        // 末尾各加一行不同内容，确保降级路径仍能给出差异
        left.Append("tail-left");
        right.Append("tail-right");

        TextDiffEngine engine = new TextDiffEngine();
        TextCompareResult result = engine.Compare(left.ToString(), right.ToString());

        Assert.True(result.UsedFallbackAlignment);
        Assert.True(result.Rows.Count > 0);
        Assert.True(result.ModifiedCount >= 1);
    }

    [Fact]
    public void Compare_EmptyAndSingleLineFiles()
    {
        TextDiffEngine engine = new TextDiffEngine();
        TextCompareResult empty = engine.Compare(string.Empty, string.Empty);
        Assert.Empty(empty.Rows);

        TextCompareResult oneSide = engine.Compare(string.Empty, "right");
        Assert.Equal(1, oneSide.RightOnlyCount);
        Assert.Single(oneSide.Rows);
        Assert.Equal(0, oneSide.Rows[0].LeftLineNumber);
    }
}
