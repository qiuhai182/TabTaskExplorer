using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 进程树构建器：把扁平进程列表按父子关系展开成带层级的树形行（参照 Process Explorer 的进程树视图）。
/// 父进程编号缺失、指向不存在的进程或形成环时，该进程按顶层节点处理，保证每个进程都会输出一次。
/// </summary>
public static class ProcessTreeBuilder
{
    /// <summary>
    /// 构建树形行列表：编号升序保证顺序稳定，父进程输出后紧跟其全部子进程。
    /// </summary>
    /// <param name="processes">扁平进程列表。</param>
    /// <returns>带缩进层级的树形行列表。</returns>
    public static IReadOnlyList<ProcessTreeRow> Build(IReadOnlyList<ProcessInfo> processes)
    {
        List<ProcessInfo> ordered = new List<ProcessInfo>(processes);
        ordered.Sort(CompareByProcessId);

        Dictionary<int, ProcessInfo> byProcessId = new Dictionary<int, ProcessInfo>();
        foreach (ProcessInfo info in ordered)
        {
            byProcessId[info.ProcessId] = info;
        }

        Dictionary<int, List<ProcessInfo>> childrenByParent = new Dictionary<int, List<ProcessInfo>>();
        List<ProcessInfo> roots = new List<ProcessInfo>();
        foreach (ProcessInfo info in ordered)
        {
            bool hasParent = info.ParentProcessId > 0
                && info.ParentProcessId != info.ProcessId
                && byProcessId.ContainsKey(info.ParentProcessId);
            if (hasParent)
            {
                List<ProcessInfo>? children;
                if (!childrenByParent.TryGetValue(info.ParentProcessId, out children))
                {
                    children = new List<ProcessInfo>();
                    childrenByParent[info.ParentProcessId] = children;
                }
                children.Add(info);
            }
            else
            {
                roots.Add(info);
            }
        }

        List<ProcessTreeRow> rows = new List<ProcessTreeRow>();
        HashSet<int> visited = new HashSet<int>();
        foreach (ProcessInfo root in roots)
        {
            AppendRow(root, 0, childrenByParent, visited, rows);
        }

        // 父子关系成环时环内节点不是任何人的普通子树入口，这里补为顶层输出，避免漏掉进程
        foreach (ProcessInfo info in ordered)
        {
            if (!visited.Contains(info.ProcessId))
            {
                AppendRow(info, 0, childrenByParent, visited, rows);
            }
        }
        return rows;
    }

    /// <summary>深度优先输出一个节点及其子树。</summary>
    private static void AppendRow(
        ProcessInfo info,
        int depth,
        Dictionary<int, List<ProcessInfo>> childrenByParent,
        HashSet<int> visited,
        List<ProcessTreeRow> rows)
    {
        if (!visited.Add(info.ProcessId))
        {
            // 已输出过：用它打断递归，避免父子关系成环时无限展开
            return;
        }
        ProcessTreeRow row = new ProcessTreeRow();
        row.Process = info;
        row.Depth = depth;
        rows.Add(row);

        List<ProcessInfo>? children;
        if (childrenByParent.TryGetValue(info.ProcessId, out children))
        {
            foreach (ProcessInfo child in children)
            {
                AppendRow(child, depth + 1, childrenByParent, visited, rows);
            }
        }
    }

    /// <summary>按进程编号升序比较。</summary>
    private static int CompareByProcessId(ProcessInfo left, ProcessInfo right)
    {
        return left.ProcessId.CompareTo(right.ProcessId);
    }
}

/// <summary>
/// 进程树中的一行：进程数据与树深度（界面按深度做缩进）。
/// </summary>
public class ProcessTreeRow
{
    /// <summary>进程信息。</summary>
    public ProcessInfo Process { get; set; } = new ProcessInfo();

    /// <summary>树深度：0 表示顶层进程。</summary>
    public int Depth { get; set; }
}
