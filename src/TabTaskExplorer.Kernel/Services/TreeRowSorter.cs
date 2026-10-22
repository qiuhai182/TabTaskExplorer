using System;
using System.Collections.Generic;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>取出一行的树形层级（0 为顶层，每层加 1）。</summary>
/// <typeparam name="T">行类型。</typeparam>
/// <param name="row">一行数据。</param>
/// <returns>该行的树形层级。</returns>
public delegate int TreeLevelSelector<in T>(T row);

/// <summary>
/// 树形行排序器：把「子行紧跟父行」的深度优先扁平行列表按同级分组排序。
/// 同一父级下的兄弟子树整体换位，子树内部递归处理，父子结构与层级缩进始终不变
/// （参照 Process Explorer 进程树与磁盘视图的树形排序方式）。
/// </summary>
public static class TreeRowSorter
{
    /// <summary>树排序时记录一段子树在原列表中的位置区间。</summary>
    /// <typeparam name="T">行类型。</typeparam>
    private struct Block<T>
    {
        /// <summary>子树起始下标（含）。</summary>
        public int Start;

        /// <summary>子树结束下标（不含）。</summary>
        public int End;
    }

    /// <summary>
    /// 对深度优先扁平行列表做同级排序：先按比较器重排顶层子树，
    /// 再对每棵子树内部的同级子节点递归重排，最后原样写回原列表。
    /// </summary>
    /// <typeparam name="T">行类型。</typeparam>
    /// <param name="rows">深度优先排列的行列表（子行紧跟父行，层级逐层递增）。</param>
    /// <param name="levelOf">取出行的树形层级的方法。</param>
    /// <param name="compare">同级行之间的比较器（比较的是子树的根行）。</param>
    public static void SortSameLevel<T>(IList<T> rows, TreeLevelSelector<T> levelOf, Comparison<T> compare)
    {
        if (rows == null || rows.Count <= 1 || levelOf == null || compare == null)
        {
            return;
        }
        List<T> source = new List<T>(rows);
        List<T> output = new List<T>(rows.Count);
        SortRange(source, 0, source.Count, levelOf, compare, output);
        for (int index = 0; index < rows.Count; index++)
        {
            rows[index] = output[index];
        }
    }

    /// <summary>
    /// 对区间内的行按同级切块：每块是一棵完整的子树（块首行 + 层级更深的后续行），
    /// 块之间按比较器排序，块内部再以同样方式递归处理同级子节点，处理结果追加进输出列表。
    /// </summary>
    /// <typeparam name="T">行类型。</typeparam>
    /// <param name="rows">源行列表。</param>
    /// <param name="start">区间起始下标（含）。</param>
    /// <param name="end">区间结束下标（不含）。</param>
    /// <param name="levelOf">取出行的树形层级的方法。</param>
    /// <param name="compare">同级行之间的比较器。</param>
    /// <param name="output">输出列表（按深度优先顺序追加）。</param>
    private static void SortRange<T>(
        List<T> rows,
        int start,
        int end,
        TreeLevelSelector<T> levelOf,
        Comparison<T> compare,
        List<T> output)
    {
        // 切块：从块首行开始，其后所有层级更深的行都属于同一棵子树
        List<Block<T>> blocks = new List<Block<T>>();
        int index = start;
        while (index < end)
        {
            Block<T> block;
            block.Start = index;
            int blockLevel = levelOf(rows[index]);
            index++;
            while (index < end && levelOf(rows[index]) > blockLevel)
            {
                index++;
            }
            block.End = index;
            blocks.Add(block);
        }

        // 块之间按块首行比较排序（快速排序，比较器决定升降序）
        List<Block<T>> sorted = new List<Block<T>>(blocks);
        sorted.Sort(delegate(Block<T> left, Block<T> right)
        {
            return compare(rows[left.Start], rows[right.Start]);
        });

        // 按排序结果深度优先输出，并对块内同级子节点递归处理
        for (int blockIndex = 0; blockIndex < sorted.Count; blockIndex++)
        {
            Block<T> block = sorted[blockIndex];
            output.Add(rows[block.Start]);
            if (block.End - block.Start > 1)
            {
                SortRange(rows, block.Start + 1, block.End, levelOf, compare, output);
            }
        }
    }
}
