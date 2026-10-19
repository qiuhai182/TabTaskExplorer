using System;
using System.Collections.Generic;

namespace TabTaskExplorer.App.Services;

/// <summary>浏览器调试镜像的界面状态快照：与桌面界面同构。</summary>
public class DebugUiState
{
    /// <summary>快照生成时间。</summary>
    public string GeneratedTime { get; set; } = string.Empty;

    /// <summary>当前主题：Dark / Light（镜像页面跟随配色）。</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>全部标签页（与桌面标签条同序）。</summary>
    public List<DebugTabState> Tabs { get; } = new List<DebugTabState>();

    /// <summary>侧边栏：快捷访问组（与桌面导航栏同构）。</summary>
    public List<DebugNavItem> NavPinned { get; } = new List<DebugNavItem>();

    /// <summary>侧边栏：此电脑组（与桌面导航栏同构）。</summary>
    public List<DebugNavItem> NavPc { get; } = new List<DebugNavItem>();
}

/// <summary>侧边栏导航镜像项。</summary>
public class DebugNavItem
{
    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>目标路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>是否命中当前路径（选中高亮）。</summary>
    public bool Selected { get; set; }
}

/// <summary>单个标签页的镜像状态。</summary>
public class DebugTabState
{
    /// <summary>标签下标（回传选中时使用）。</summary>
    public int Index { get; set; }

    /// <summary>标签类型：file / board / connections。</summary>
    public string TabType { get; set; } = string.Empty;

    /// <summary>标签标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>是否为当前选中标签。</summary>
    public bool Selected { get; set; }

    /// <summary>文件标签：当前路径。</summary>
    public string CurrentPath { get; set; } = string.Empty;

    /// <summary>文件标签：可见条目（与桌面列表同构）。</summary>
    public List<DebugFileRow> Rows { get; } = new List<DebugFileRow>();

    /// <summary>任务看板：待办标题。</summary>
    public List<string> Todo { get; } = new List<string>();

    /// <summary>任务看板：进行中标题。</summary>
    public List<string> InProgress { get; } = new List<string>();

    /// <summary>任务看板：已完成标题。</summary>
    public List<string> Done { get; } = new List<string>();

    /// <summary>连接管理：全部连接（名称 / 摘要）。</summary>
    public List<string[]> Connections { get; } = new List<string[]>();

    /// <summary>连接管理：当前选中的连接名称。</summary>
    public string SelectedConnection { get; set; } = string.Empty;
}

/// <summary>文件列表镜像行。</summary>
public class DebugFileRow
{
    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>类型。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>大小。</summary>
    public string Size { get; set; } = string.Empty;

    /// <summary>修改日期。</summary>
    public string Modified { get; set; } = string.Empty;
}
