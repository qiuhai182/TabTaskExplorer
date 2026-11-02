using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 文本行比对引擎：按行动态规划求最长公共子序列，输出左右行对齐结果与行内差异区间。
/// 大文本超过矩阵上限时降级为“去公共前后缀后按行配对”的线性对齐，保证界面不因超大文件卡死。
/// </summary>
public class TextDiffEngine
{
    /// <summary>动态规划矩阵单元格上限（约 1600 万个单元格，方向位图约 4MB）。</summary>
    public const long MaxMatrixCells = 16000000L;

    /// <summary>
    /// 比较两段文本。
    /// </summary>
    /// <param name="leftText">左侧文本。</param>
    /// <param name="rightText">右侧文本。</param>
    /// <returns>按行对齐的比对结果。</returns>
    public TextCompareResult Compare(string leftText, string rightText)
    {
        string left = leftText ?? string.Empty;
        string right = rightText ?? string.Empty;
        string[] leftLines = SplitLines(left);
        string[] rightLines = SplitLines(right);

        TextCompareResult result = new TextCompareResult();
        result.LeftLineCount = leftLines.Length;
        result.RightLineCount = rightLines.Length;

        long cells = (long)leftLines.Length * (long)rightLines.Length;
        if (cells > MaxMatrixCells)
        {
            result.UsedFallbackAlignment = true;
            this.BuildFallbackRows(leftLines, rightLines, result);
            return result;
        }

        this.BuildOptimalRows(leftLines, rightLines, result);
        return result;
    }

    /// <summary>把文本切成行：统一 CRLF / CR / LF，保留末尾空行（与编辑器行号一致）。</summary>
    private static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }
        string normalized = text.Replace("\r\n", "\n");
        normalized = normalized.Replace('\r', '\n');
        return normalized.Split('\n');
    }

    /// <summary>用最长公共子序列矩阵对齐左右行，并配对相邻的删除 / 新增块形成修改行。</summary>
    private void BuildOptimalRows(string[] leftLines, string[] rightLines, TextCompareResult result)
    {
        int leftCount = leftLines.Length;
        int rightCount = rightLines.Length;
        byte[] directions = new byte[((long)leftCount * rightCount + 3) / 4];
        int[] nextRow = new int[rightCount + 1];
        int[] currentRow = new int[rightCount + 1];

        // 自下而上填表：nextRow 表示第 i+1 行的结果，currentRow 表示第 i 行；
        // 同时把每个单元格的走法压成 2 位写入方向位图，供正序重建差异序列。
        for (int leftIndex = leftCount - 1; leftIndex >= 0; leftIndex--)
        {
            currentRow[rightCount] = 0;
            for (int rightIndex = rightCount - 1; rightIndex >= 0; rightIndex--)
            {
                long cellIndex = (long)leftIndex * rightCount + rightIndex;
                if (string.Equals(leftLines[leftIndex], rightLines[rightIndex], StringComparison.Ordinal))
                {
                    currentRow[rightIndex] = nextRow[rightIndex + 1] + 1;
                    SetDirection(directions, cellIndex, 0);
                }
                else if (nextRow[rightIndex] >= currentRow[rightIndex + 1])
                {
                    currentRow[rightIndex] = nextRow[rightIndex];
                    SetDirection(directions, cellIndex, 1);
                }
                else
                {
                    currentRow[rightIndex] = currentRow[rightIndex + 1];
                    SetDirection(directions, cellIndex, 2);
                }
            }
            int[] swap = nextRow;
            nextRow = currentRow;
            currentRow = swap;
        }

        List<DiffOperation> operations = new List<DiffOperation>();
        int leftCursor = 0;
        int rightCursor = 0;
        while (leftCursor < leftCount && rightCursor < rightCount)
        {
            byte direction = GetDirection(directions, (long)leftCursor * rightCount + rightCursor);
            if (direction == 0)
            {
                DiffOperation match = new DiffOperation();
                match.Kind = DiffRowKind.Same;
                match.LeftIndex = leftCursor;
                match.RightIndex = rightCursor;
                operations.Add(match);
                leftCursor++;
                rightCursor++;
            }
            else if (direction == 1)
            {
                DiffOperation delete = new DiffOperation();
                delete.Kind = DiffRowKind.LeftOnly;
                delete.LeftIndex = leftCursor;
                delete.RightIndex = -1;
                operations.Add(delete);
                leftCursor++;
            }
            else
            {
                DiffOperation insert = new DiffOperation();
                insert.Kind = DiffRowKind.RightOnly;
                insert.LeftIndex = -1;
                insert.RightIndex = rightCursor;
                operations.Add(insert);
                rightCursor++;
            }
        }
        while (leftCursor < leftCount)
        {
            DiffOperation delete = new DiffOperation();
            delete.Kind = DiffRowKind.LeftOnly;
            delete.LeftIndex = leftCursor;
            delete.RightIndex = -1;
            operations.Add(delete);
            leftCursor++;
        }
        while (rightCursor < rightCount)
        {
            DiffOperation insert = new DiffOperation();
            insert.Kind = DiffRowKind.RightOnly;
            insert.LeftIndex = -1;
            insert.RightIndex = rightCursor;
            operations.Add(insert);
            rightCursor++;
        }

        // 把连续的非相同行按左右配对成修改行，多出的部分保持单侧行
        int operationIndex = 0;
        while (operationIndex < operations.Count)
        {
            DiffOperation operation = operations[operationIndex];
            if (operation.Kind == DiffRowKind.Same)
            {
                this.AddRow(result, leftLines, rightLines, DiffRowKind.Same,
                    operation.LeftIndex, operation.RightIndex);
                operationIndex++;
                continue;
            }

            List<int> leftBlock = new List<int>();
            List<int> rightBlock = new List<int>();
            while (operationIndex < operations.Count && operations[operationIndex].Kind != DiffRowKind.Same)
            {
                DiffOperation blockItem = operations[operationIndex];
                if (blockItem.LeftIndex >= 0)
                {
                    leftBlock.Add(blockItem.LeftIndex);
                }
                if (blockItem.RightIndex >= 0)
                {
                    rightBlock.Add(blockItem.RightIndex);
                }
                operationIndex++;
            }
            int pairCount = Math.Min(leftBlock.Count, rightBlock.Count);
            for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                this.AddRow(result, leftLines, rightLines, DiffRowKind.Modified,
                    leftBlock[pairIndex], rightBlock[pairIndex]);
            }
            for (int extraIndex = pairCount; extraIndex < leftBlock.Count; extraIndex++)
            {
                this.AddRow(result, leftLines, rightLines, DiffRowKind.LeftOnly, leftBlock[extraIndex], -1);
            }
            for (int extraIndex = pairCount; extraIndex < rightBlock.Count; extraIndex++)
            {
                this.AddRow(result, leftLines, rightLines, DiffRowKind.RightOnly, -1, rightBlock[extraIndex]);
            }
        }
    }

    /// <summary>超大文本降级对齐：去掉公共前后缀后，剩余行按位置配对。</summary>
    private void BuildFallbackRows(string[] leftLines, string[] rightLines, TextCompareResult result)
    {
        int prefixCount = 0;
        int maxPrefix = Math.Min(leftLines.Length, rightLines.Length);
        while (prefixCount < maxPrefix
            && string.Equals(leftLines[prefixCount], rightLines[prefixCount], StringComparison.Ordinal))
        {
            this.AddRow(result, leftLines, rightLines, DiffRowKind.Same, prefixCount, prefixCount);
            prefixCount++;
        }

        int suffixCount = 0;
        while (suffixCount < maxPrefix - prefixCount
            && string.Equals(leftLines[leftLines.Length - 1 - suffixCount],
                rightLines[rightLines.Length - 1 - suffixCount], StringComparison.Ordinal))
        {
            suffixCount++;
        }

        int leftRemaining = leftLines.Length - prefixCount - suffixCount;
        int rightRemaining = rightLines.Length - prefixCount - suffixCount;
        int pairCount = Math.Min(leftRemaining, rightRemaining);
        for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
        {
            this.AddRow(result, leftLines, rightLines, DiffRowKind.Modified,
                prefixCount + pairIndex, prefixCount + pairIndex);
        }
        for (int extraIndex = pairCount; extraIndex < leftRemaining; extraIndex++)
        {
            this.AddRow(result, leftLines, rightLines, DiffRowKind.LeftOnly, prefixCount + extraIndex, -1);
        }
        for (int extraIndex = pairCount; extraIndex < rightRemaining; extraIndex++)
        {
            this.AddRow(result, leftLines, rightLines, DiffRowKind.RightOnly, -1, prefixCount + extraIndex);
        }

        int suffixStartLeft = leftLines.Length - suffixCount;
        int suffixStartRight = rightLines.Length - suffixCount;
        for (int suffixIndex = 0; suffixIndex < suffixCount; suffixIndex++)
        {
            this.AddRow(result, leftLines, rightLines, DiffRowKind.Same,
                suffixStartLeft + suffixIndex, suffixStartRight + suffixIndex);
        }
    }

    /// <summary>追加一行对齐结果并同步统计与行内差异区间。</summary>
    private void AddRow(TextCompareResult result, string[] leftLines, string[] rightLines,
        DiffRowKind kind, int leftIndex, int rightIndex)
    {
        DiffRow row = new DiffRow();
        row.Kind = kind;
        if (leftIndex >= 0 && leftIndex < leftLines.Length)
        {
            row.LeftLineNumber = leftIndex + 1;
            row.LeftText = leftLines[leftIndex];
        }
        if (rightIndex >= 0 && rightIndex < rightLines.Length)
        {
            row.RightLineNumber = rightIndex + 1;
            row.RightText = rightLines[rightIndex];
        }
        if (kind == DiffRowKind.Modified)
        {
            ComputeInlineChange(row);
            result.ModifiedCount++;
        }
        else if (kind == DiffRowKind.LeftOnly)
        {
            result.LeftOnlyCount++;
        }
        else if (kind == DiffRowKind.RightOnly)
        {
            result.RightOnlyCount++;
        }
        else
        {
            result.SameCount++;
        }
        result.Rows.Add(row);
    }

    /// <summary>计算修改行的行内差异区间：取公共前缀与公共后缀，中间部分标记为变化。</summary>
    private static void ComputeInlineChange(DiffRow row)
    {
        string left = row.LeftText;
        string right = row.RightText;
        int maxPrefix = Math.Min(left.Length, right.Length);
        int prefixLength = 0;
        while (prefixLength < maxPrefix && left[prefixLength] == right[prefixLength])
        {
            prefixLength++;
        }
        int suffixLimit = maxPrefix - prefixLength;
        int suffixLength = 0;
        while (suffixLength < suffixLimit
            && left[left.Length - 1 - suffixLength] == right[right.Length - 1 - suffixLength])
        {
            suffixLength++;
        }
        row.LeftChangedStart = prefixLength;
        row.LeftChangedLength = left.Length - prefixLength - suffixLength;
        row.RightChangedStart = prefixLength;
        row.RightChangedLength = right.Length - prefixLength - suffixLength;
    }

    /// <summary>把 2 位方向值写入位图。</summary>
    private static void SetDirection(byte[] directions, long cellIndex, byte direction)
    {
        int byteIndex = (int)(cellIndex / 4);
        int shift = (int)((cellIndex % 4) * 2);
        directions[byteIndex] = (byte)((directions[byteIndex] & ~(3 << shift)) | (direction << shift));
    }

    /// <summary>从位图读取 2 位方向值。</summary>
    private static byte GetDirection(byte[] directions, long cellIndex)
    {
        int byteIndex = (int)(cellIndex / 4);
        int shift = (int)((cellIndex % 4) * 2);
        return (byte)((directions[byteIndex] >> shift) & 3);
    }

    /// <summary>一次差异操作：种类与左右行下标（不存在的一侧为 -1）。</summary>
    private sealed class DiffOperation
    {
        /// <summary>操作产生的行类型。</summary>
        public DiffRowKind Kind;

        /// <summary>左侧行下标；不存在时为 -1。</summary>
        public int LeftIndex;

        /// <summary>右侧行下标；不存在时为 -1。</summary>
        public int RightIndex;
    }
}
