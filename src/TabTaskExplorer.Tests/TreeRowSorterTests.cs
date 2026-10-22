using System.Collections.Generic;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>树形同级排序器的测试行（名称 + 树形层级）。</summary>
internal class TreeSortRow
{
    public string Name { get; set; } = string.Empty;

    public int Level { get; set; }
}

/// <summary>树形同级排序器测试：子树整体换位、块内递归、层级结构保持不变。</summary>
public class TreeRowSorterTests
{
    private static int SelectLevel(TreeSortRow row)
    {
        return row.Level;
    }

    private static List<TreeSortRow> BuildRows()
    {
        // 顶层 B（子 b1、b2）、顶层 A（子 a1）、顶层 C
        return new List<TreeSortRow>
        {
            new TreeSortRow { Name = "B", Level = 0 },
            new TreeSortRow { Name = "b1", Level = 1 },
            new TreeSortRow { Name = "b2", Level = 1 },
            new TreeSortRow { Name = "A", Level = 0 },
            new TreeSortRow { Name = "a1", Level = 1 },
            new TreeSortRow { Name = "C", Level = 0 },
        };
    }

    private static string JoinNames(List<TreeSortRow> rows)
    {
        List<string> names = new List<string>();
        foreach (TreeSortRow row in rows)
        {
            names.Add(row.Name);
        }
        return string.Join(",", names);
    }

    private static int CompareByNameAscending(TreeSortRow left, TreeSortRow right)
    {
        return string.CompareOrdinal(left.Name, right.Name);
    }

    [Fact]
    public void SortSameLevel_ReordersTopLevelBlocksAndKeepsChildrenAttached()
    {
        List<TreeSortRow> rows = TreeRowSorterTests.BuildRows();

        TreeRowSorter.SortSameLevel<TreeSortRow>(rows, TreeRowSorterTests.SelectLevel, TreeRowSorterTests.CompareByNameAscending);

        // A、B、C 升序换位，各自子行仍紧跟其后
        Assert.Equal("A,a1,B,b1,b2,C", TreeRowSorterTests.JoinNames(rows));
    }

    [Fact]
    public void SortSameLevel_SortsInsideSubtreesRecursively()
    {
        List<TreeSortRow> rows = new List<TreeSortRow>
        {
            new TreeSortRow { Name = "B", Level = 0 },
            new TreeSortRow { Name = "b2", Level = 1 },
            new TreeSortRow { Name = "b1", Level = 1 },
            new TreeSortRow { Name = "A", Level = 0 },
        };

        TreeRowSorter.SortSameLevel<TreeSortRow>(rows, TreeRowSorterTests.SelectLevel, TreeRowSorterTests.CompareByNameAscending);

        // 顶层换位为 A、B，B 的子节点 b1、b2 也在块内递归排为升序
        Assert.Equal("A,B,b1,b2", TreeRowSorterTests.JoinNames(rows));
    }

    [Fact]
    public void SortSameLevel_DescendingComparisonKeepsTreeStructure()
    {
        List<TreeSortRow> rows = TreeRowSorterTests.BuildRows();
        Comparison<TreeSortRow> descending = delegate(TreeSortRow left, TreeSortRow right)
        {
            return -TreeRowSorterTests.CompareByNameAscending(left, right);
        };

        TreeRowSorter.SortSameLevel<TreeSortRow>(rows, TreeRowSorterTests.SelectLevel, descending);

        // 降序：C、B、A 整体换位，子行不脱离父行，块内同样按降序排为 b2、b1
        Assert.Equal("C,B,b2,b1,A,a1", TreeRowSorterTests.JoinNames(rows));
    }

    [Fact]
    public void SortSameLevel_SingleRowOrEmptyListKeepsOriginalOrder()
    {
        List<TreeSortRow> single = new List<TreeSortRow> { new TreeSortRow { Name = "only", Level = 0 } };
        TreeRowSorter.SortSameLevel<TreeSortRow>(single, TreeRowSorterTests.SelectLevel, TreeRowSorterTests.CompareByNameAscending);
        Assert.Equal("only", TreeRowSorterTests.JoinNames(single));

        List<TreeSortRow> empty = new List<TreeSortRow>();
        TreeRowSorter.SortSameLevel<TreeSortRow>(empty, TreeRowSorterTests.SelectLevel, TreeRowSorterTests.CompareByNameAscending);
        Assert.Empty(empty);
    }
}
