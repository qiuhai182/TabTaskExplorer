using System.Collections.Generic;
using System.Text;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 处理器亲和性掩码工具：核心编号列表与位掩码之间的互相转换。
/// 位掩码的第 n 位代表第 n 个逻辑处理器核心。
/// </summary>
public static class AffinityMaskUtility
{
    /// <summary>表示"读不到或不支持"的掩码值。</summary>
    public const long UnsupportedMask = -1;

    /// <summary>
    /// 把核心编号列表转换成位掩码。
    /// 列表为空、编号为负数或编号超出核心总数时返回 -1（视为不支持）。
    /// </summary>
    public static long ToMask(IReadOnlyList<int> processorIndexes, int processorCount)
    {
        if (processorIndexes == null || processorIndexes.Count == 0 || processorCount <= 0)
        {
            return UnsupportedMask;
        }
        long mask = 0;
        for (int index = 0; index < processorIndexes.Count; index++)
        {
            int processorIndex = processorIndexes[index];
            if (processorIndex < 0 || processorIndex >= processorCount || processorIndex >= 64)
            {
                return UnsupportedMask;
            }
            mask |= 1L << processorIndex;
        }
        if (mask == 0)
        {
            return UnsupportedMask;
        }
        return mask;
    }

    /// <summary>
    /// 把位掩码转换成界面显示文本："全部核心" / "0,1,2" / "不支持" / "无"。
    /// </summary>
    public static string ToText(long affinityMask, int processorCount)
    {
        if (affinityMask == UnsupportedMask)
        {
            return "不支持";
        }
        if (affinityMask == 0)
        {
            return "无";
        }
        int limit = processorCount;
        if (limit <= 0 || limit > 64)
        {
            limit = 64;
        }
        bool allSelected = true;
        for (int processorIndex = 0; processorIndex < limit; processorIndex++)
        {
            if ((affinityMask & (1L << processorIndex)) == 0)
            {
                allSelected = false;
                break;
            }
        }
        if (allSelected)
        {
            return "全部核心";
        }
        StringBuilder builder = new StringBuilder();
        for (int processorIndex = 0; processorIndex < limit; processorIndex++)
        {
            if ((affinityMask & (1L << processorIndex)) == 0)
            {
                continue;
            }
            if (builder.Length > 0)
            {
                builder.Append(',');
            }
            builder.Append(processorIndex);
        }
        if (builder.Length == 0)
        {
            return "无";
        }
        return builder.ToString();
    }

    /// <summary>
    /// 解析界面上输入的核心编号列表文本（形如 "0,1,3"）。
    /// 解析失败或没有有效编号时返回空列表。
    /// </summary>
    public static List<int> ParseIndexes(string? processorIndexesText)
    {
        List<int> result = new List<int>();
        if (string.IsNullOrWhiteSpace(processorIndexesText))
        {
            return result;
        }
        string[] parts = processorIndexesText.Split(new char[] { ',', ';', '，', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index].Trim();
            if (part.Length == 0)
            {
                continue;
            }
            int processorIndex;
            if (!int.TryParse(part, out processorIndex))
            {
                continue;
            }
            if (processorIndex < 0 || processorIndex >= 64)
            {
                continue;
            }
            if (!result.Contains(processorIndex))
            {
                result.Add(processorIndex);
            }
        }
        return result;
    }
}
