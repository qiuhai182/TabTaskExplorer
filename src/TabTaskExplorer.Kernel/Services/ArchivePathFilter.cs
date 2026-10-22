using System;
using System.Collections.Generic;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 压缩包内条目路径过滤与重命名辅助：
/// 支持精确匹配、目录前缀匹配（删除文件夹时连带其下条目、按文件夹筛选时连带子文件），
/// 供 tar 引擎与 ZIP / 7z 等字符串型引擎共用。
/// </summary>
public static class ArchivePathFilter
{
    /// <summary>把路径列表归一化为过滤集合（'\' 转 '/'、去首尾 '/'，同时保留带尾斜杠形式）；空列表返回 null。</summary>
    public static HashSet<string>? Build(IEnumerable<string>? names)
    {
        if (names == null)
        {
            return null;
        }
        HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? raw in names)
        {
            if (raw == null)
            {
                continue;
            }
            string name = raw.Replace('\\', '/').TrimStart('/').TrimEnd('/');
            if (name.Length > 0)
            {
                set.Add(name);
            }
        }
        return set.Count == 0 ? null : set;
    }

    /// <summary>
    /// 判断条目名是否命中过滤集合：null 集合恒命中；
    /// 命中条件为精确相等、忽略尾部斜杠相等、或位于某个过滤项的目录前缀之下。
    /// </summary>
    public static bool Matches(HashSet<string>? set, string name)
    {
        if (set == null)
        {
            return true;
        }
        string trimmed = name.TrimEnd('/');
        if (set.Contains(name) || set.Contains(trimmed))
        {
            return true;
        }
        foreach (string item in set)
        {
            if (trimmed.StartsWith(item + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 按重命名映射求条目新名字：精确命中直接替换；
    /// 位于被重命名文件夹之下时做前缀替换（文件夹连同其下条目一起改名）。未命中返回 false。
    /// </summary>
    public static bool TryRename(Dictionary<string, string> renameMap, string name, out string newName)
    {
        string trimmed = name.TrimEnd('/');
        if (renameMap.TryGetValue(trimmed, out newName!))
        {
            return true;
        }
        if (renameMap.TryGetValue(name, out newName!))
        {
            return true;
        }
        foreach (KeyValuePair<string, string> pair in renameMap)
        {
            if (trimmed.StartsWith(pair.Key + "/", StringComparison.Ordinal))
            {
                newName = pair.Value + trimmed.Substring(pair.Key.Length);
                return true;
            }
        }
        newName = string.Empty;
        return false;
    }

    /// <summary>把重命名映射列表构建为字典（元素为 [旧名, 新名] 两元素数组）。</summary>
    public static Dictionary<string, string> BuildRenameMap(IReadOnlyList<string[]> renamedPairs)
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < renamedPairs.Count; i++)
        {
            string[]? pair = renamedPairs[i];
            if (pair != null && pair.Length == 2 && !string.IsNullOrEmpty(pair[0]))
            {
                string oldName = pair[0].Replace('\\', '/').TrimStart('/').TrimEnd('/');
                string newName = (pair[1] ?? string.Empty).Replace('\\', '/').TrimStart('/').TrimEnd('/');
                if (oldName.Length > 0 && newName.Length > 0)
                {
                    map[oldName] = newName;
                }
            }
        }
        return map;
    }
}
