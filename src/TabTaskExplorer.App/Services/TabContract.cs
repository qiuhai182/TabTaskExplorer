using System;
using System.Collections.Generic;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 标签视图统一契约：桌面端与网页端都按同一套类型与快照字段展示，
/// 新增标签视图时只需实现本接口，不再需要在两处状态构建器里各写一段镜像代码。
/// </summary>
public interface ITabView
{
    /// <summary>标签类型标识（file / disk / process / search / board / connections）。</summary>
    string Kind { get; }

    /// <summary>标签标题。</summary>
    string Title { get; }

    /// <summary>把当前视图状态写入快照（由桌面与网页共用，只写内容字段，不写标题）。</summary>
    void FillSnapshot(DebugTabState tabState);
}

/// <summary>标签类型常量与打开动作映射：桌面与网页共用一份清单。</summary>
public static class TabKinds
{
    /// <summary>文件列表标签。</summary>
    public const string File = "file";

    /// <summary>磁盘视图标签。</summary>
    public const string Disk = "disk";

    /// <summary>进程监控标签。</summary>
    public const string Process = "process";

    /// <summary>全局搜索标签。</summary>
    public const string Search = "search";

    /// <summary>任务看板标签。</summary>
    public const string Board = "board";

    /// <summary>连接管理标签。</summary>
    public const string Connections = "connections";

    /// <summary>网页端无法渲染的桌面专用标签（终端、传输队列、编辑器等）。</summary>
    public const string Unknown = "unknown";

    /// <summary>全部可打开且网页端可渲染的标签类型。</summary>
    public static IReadOnlyList<string> All { get; } = new string[]
    {
        File, Disk, Process, Search, Board, Connections,
    };

    /// <summary>判断类型标识是否是受支持的标签类型。</summary>
    public static bool IsKnown(string kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return false;
        }
        foreach (string item in All)
        {
            if (item == kind)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>把 openxxx 动作解析为标签类型；不是打开视图动作时返回 null。</summary>
    public static string? FromOpenAction(string action)
    {
        if (string.IsNullOrEmpty(action) || !action.StartsWith("open", StringComparison.Ordinal))
        {
            return null;
        }
        string kind = action.Substring("open".Length);
        return IsKnown(kind) ? kind : null;
    }

    /// <summary>把标签类型转成网页端使用的打开动作名称。</summary>
    public static string ToOpenAction(string kind)
    {
        return "open" + kind;
    }
}

/// <summary>把标签对象写入快照的统一入口：桌面端与网页端状态构建器都调用这里。</summary>
public static class TabSnapshotBuilder
{
    /// <summary>按标签是否实现 ITabView 写入快照；桌面专用标签回退为 unknown。</summary>
    public static void Fill(object tab, DebugTabState tabState)
    {
        ITabView? view = tab as ITabView;
        if (view == null)
        {
            tabState.TabType = TabKinds.Unknown;
            return;
        }
        tabState.TabType = view.Kind;
        view.FillSnapshot(tabState);
    }
}

/// <summary>各标签视图的快照字段映射：唯一实现，桌面端与网页端共用。</summary>
public static class TabSnapshotFiller
{
    /// <summary>文件列表标签快照。</summary>
    public static void FillFile(FileTabViewModel fileTab, DebugTabState tabState)
    {
        tabState.CurrentPath = fileTab.CurrentPath;
        tabState.SortField = fileTab.SortField;
        tabState.SortAscending = fileTab.SortAscending;
        tabState.SearchText = fileTab.SearchText;
        tabState.ViewMode = fileTab.ViewMode.ToString();
        tabState.ShowInfoPane = fileTab.ShowInfoPane;
        tabState.CanGoBack = fileTab.CanGoBack;
        tabState.CanGoForward = fileTab.CanGoForward;
        tabState.CheckedCount = fileTab.GetCheckedRows().Count;
        tabState.SummaryText = fileTab.SummaryText;
        tabState.TotalRows = fileTab.VisibleRows.Count;
        int limit = tabState.TotalRows > 1000 ? 1000 : tabState.TotalRows;
        for (int rowIndex = 0; rowIndex < limit; rowIndex++)
        {
            FileRow row = fileTab.VisibleRows[rowIndex];
            DebugFileRow debugRow = new DebugFileRow();
            debugRow.Name = row.DisplayName;
            debugRow.IsDirectory = row.IsDirectory;
            debugRow.FullPath = row.FullPath;
            debugRow.Type = row.TypeText;
            debugRow.Size = row.SizeText;
            debugRow.Modified = row.ModifiedText;
            debugRow.IsSelected = row.IsSelected;
            debugRow.IconGlyph = row.IconGlyph;
            debugRow.IconColor = row.IconColor;
            tabState.Rows.Add(debugRow);
        }
    }

    /// <summary>磁盘视图标签快照。</summary>
    public static void FillDisk(DiskViewPanelViewModel diskTab, DebugTabState tabState)
    {
        tabState.CurrentPath = diskTab.RootPathInput;
        tabState.SummaryText = diskTab.SummaryText;
        tabState.TotalRows = diskTab.Rows.Count;
        tabState.SortField = diskTab.SortField;
        tabState.SortAscending = diskTab.SortAscending;
        foreach (DiskRow row in diskTab.Rows)
        {
            DebugFileRow debugRow = new DebugFileRow();
            debugRow.Name = row.Name;
            debugRow.IsDirectory = row.IsDirectory;
            debugRow.FullPath = row.FullPath;
            debugRow.Type = row.EntryTypeText;
            debugRow.Size = row.SizeText;
            debugRow.Free = row.FreeText;
            debugRow.Usage = row.UsageText;
            debugRow.FileSystem = row.FileSystemText;
            debugRow.Level = row.Level;
            debugRow.Expanded = row.IsExpanded;
            tabState.Rows.Add(debugRow);
        }
    }

    /// <summary>进程监控标签快照。</summary>
    public static void FillProcess(ProcessPanelViewModel processTab, DebugTabState tabState)
    {
        tabState.ResourceText = processTab.ResourceText;
        tabState.SummaryText = processTab.SummaryText;
        tabState.TotalRows = processTab.Rows.Count;
        tabState.DetailText = processTab.DetailText;
        tabState.AffinityInput = processTab.AffinityInput;
        tabState.PriorityIndex = processTab.SelectedPriorityIndex;
        tabState.DetailSupported = processTab.IsDetailSupported;
        tabState.TrendVersion = processTab.TrendUpdatedAt.Ticks;
        tabState.SortField = processTab.SortField;
        tabState.SortAscending = processTab.SortAscending;
        CopyTrend(processTab.SystemCpuTrend, tabState.SystemCpuTrend);
        CopyTrend(processTab.SystemMemoryTrend, tabState.SystemMemoryTrend);
        CopyTrend(processTab.SelectedCpuTrend, tabState.SelectedCpuTrend);
        int processLimit = tabState.TotalRows > 1000 ? 1000 : tabState.TotalRows;
        for (int processIndex = 0; processIndex < processLimit; processIndex++)
        {
            ProcessRow row = processTab.Rows[processIndex];
            DebugProcessRow debugRow = new DebugProcessRow();
            debugRow.ProcessId = row.ProcessId;
            debugRow.IndentText = row.IndentText;
            debugRow.Name = row.Name;
            debugRow.CpuText = row.CpuText;
            debugRow.MemoryText = row.MemoryText;
            debugRow.WindowTitle = row.WindowTitle;
            debugRow.UserNameText = row.UserNameText;
            debugRow.ThreadText = row.ThreadText;
            debugRow.HandleText = row.HandleText;
            debugRow.PriorityText = row.PriorityText;
            debugRow.IoText = row.IoText;
            debugRow.StartTimeText = row.StartTimeText;
            debugRow.CommandLineText = row.CommandLineText;
            debugRow.IsSelected = object.ReferenceEquals(row, processTab.SelectedRow);
            tabState.ProcessRows.Add(debugRow);
        }
    }

    /// <summary>全局搜索标签快照。</summary>
    public static void FillSearch(SearchTabViewModel searchTab, DebugTabState tabState)
    {
        tabState.SearchText = searchTab.QueryText;
        tabState.SummaryText = searchTab.StatusText;
        tabState.SortField = searchTab.SortField;
        tabState.SortAscending = searchTab.SortAscending;
        tabState.TotalRows = searchTab.Results.Count;
        int searchLimit = tabState.TotalRows > 1000 ? 1000 : tabState.TotalRows;
        for (int resultIndex = 0; resultIndex < searchLimit; resultIndex++)
        {
            FileRow row = searchTab.Results[resultIndex];
            DebugFileRow debugRow = new DebugFileRow();
            debugRow.Name = row.DisplayName;
            debugRow.IsDirectory = row.IsDirectory;
            debugRow.FullPath = row.FullPath;
            debugRow.Type = row.TypeText;
            debugRow.Size = row.SizeText;
            debugRow.Modified = row.ModifiedText;
            debugRow.IsSelected = row.IsSelected;
            debugRow.IconGlyph = row.IconGlyph;
            debugRow.IconColor = row.IconColor;
            tabState.Rows.Add(debugRow);
        }
    }

    /// <summary>任务看板标签快照。</summary>
    public static void FillBoard(TaskBoardViewModel boardTab, DebugTabState tabState)
    {
        foreach (TaskItem item in boardTab.TodoItems)
        {
            tabState.Todo.Add(item.Title);
        }
        foreach (TaskItem item in boardTab.InProgressItems)
        {
            tabState.InProgress.Add(item.Title);
        }
        foreach (TaskItem item in boardTab.DoneItems)
        {
            tabState.Done.Add(item.Title);
        }
    }

    /// <summary>连接管理标签快照。</summary>
    public static void FillConnections(ConnectionManagerViewModel connections, DebugTabState tabState)
    {
        RemoteConnectionProfile? selected = connections.SelectedConnection;
        foreach (RemoteConnectionProfile profile in connections.Connections)
        {
            DebugConnection mirror = new DebugConnection();
            mirror.Id = profile.Id;
            mirror.Name = profile.Name;
            mirror.Group = profile.Group;
            mirror.Protocol = profile.Protocol == RemoteProtocol.Ftp ? "FTP" : "SSH";
            mirror.Host = profile.Host;
            mirror.Port = profile.Port;
            mirror.Username = profile.Username;
            mirror.AuthType = profile.AuthType == RemoteAuthType.PrivateKey ? "PrivateKey" : "Password";
            mirror.PrivateKeyPath = profile.PrivateKeyPath;
            mirror.KeepAliveSeconds = profile.KeepAliveSeconds;
            mirror.Summary = profile.Summary;
            mirror.Selected = selected != null && selected.Id == profile.Id;
            tabState.Connections.Add(mirror);
        }
        tabState.SelectedConnection = selected != null ? selected.Name : string.Empty;
        tabState.SelectedConnectionId = selected != null ? selected.Id : string.Empty;
    }

    /// <summary>把走势图序列复制到快照列表（复制而非引用，避免快照与界面状态竞争）。</summary>
    private static void CopyTrend(IReadOnlyList<double> source, List<double> target)
    {
        target.Clear();
        for (int index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }
}
