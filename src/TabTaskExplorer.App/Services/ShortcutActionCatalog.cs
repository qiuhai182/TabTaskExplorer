using System;
using System.Collections.Generic;

namespace TabTaskExplorer.App.Services;

/// <summary>一个可配置快捷键的功能动作：动作标识固定，显示名称用于界面。</summary>
public class ShortcutActionDefinition
{
    /// <summary>动作标识（持久化与执行时使用）。</summary>
    public string ActionName { get; set; } = string.Empty;

    /// <summary>界面显示名称。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>下拉框未使用模板时也能显示中文名称。</summary>
    public override string ToString()
    {
        return this.DisplayName;
    }
}

/// <summary>
/// 快捷键动作目录：应用内所有可绑定快捷键的功能都在这里登记，
/// 设置中心按此目录生成功能下拉框，执行层按动作标识分派。
/// </summary>
public static class ShortcutActionCatalog
{
    /// <summary>全部可配置动作。</summary>
    public static IReadOnlyList<ShortcutActionDefinition> All { get; } = Build();

    /// <summary>按动作标识查找定义；未知动作返回 null。</summary>
    public static ShortcutActionDefinition? Find(string actionName)
    {
        if (string.IsNullOrEmpty(actionName))
        {
            return null;
        }
        foreach (ShortcutActionDefinition definition in All)
        {
            if (string.Equals(definition.ActionName, actionName, StringComparison.Ordinal))
            {
                return definition;
            }
        }
        return null;
    }

    /// <summary>把动作标识翻译成中文名称；未知动作原样返回。</summary>
    public static string GetDisplayName(string actionName)
    {
        ShortcutActionDefinition? definition = Find(actionName);
        return definition == null ? actionName : definition.DisplayName;
    }

    /// <summary>判断动作是否属于截图 / 录屏类动作（由截图录屏协调器执行）。</summary>
    public static bool IsCaptureAction(string actionName)
    {
        return string.Equals(actionName, "ScreenshotRegion", StringComparison.Ordinal)
            || string.Equals(actionName, "ScreenshotFull", StringComparison.Ordinal)
            || string.Equals(actionName, "ScreenshotPrimary", StringComparison.Ordinal)
            || string.Equals(actionName, "ScreenshotWindow", StringComparison.Ordinal)
            || string.Equals(actionName, "ScreenshotLastRegion", StringComparison.Ordinal)
            || string.Equals(actionName, "RecordToggle", StringComparison.Ordinal)
            || string.Equals(actionName, "ColorPick", StringComparison.Ordinal)
            || string.Equals(actionName, "PastePin", StringComparison.Ordinal);
    }

    /// <summary>判断动作是否仅在 IDE 模式有效。</summary>
    public static bool IsIdeAction(string actionName)
    {
        return string.Equals(actionName, "Save", StringComparison.Ordinal)
            || string.Equals(actionName, "SaveAll", StringComparison.Ordinal)
            || string.Equals(actionName, "CloseEditor", StringComparison.Ordinal)
            || string.Equals(actionName, "QuickOpen", StringComparison.Ordinal)
            || string.Equals(actionName, "Find", StringComparison.Ordinal);
    }

    private static IReadOnlyList<ShortcutActionDefinition> Build()
    {
        List<ShortcutActionDefinition> definitions = new List<ShortcutActionDefinition>();
        definitions.Add(Create("NewTab", "新建标签"));
        definitions.Add(Create("CloseTab", "关闭当前标签"));
        definitions.Add(Create("NextTab", "切换到下一个标签"));
        definitions.Add(Create("PreviousTab", "切换到上一个标签"));
        definitions.Add(Create("Refresh", "刷新当前目录"));
        definitions.Add(Create("Back", "后退"));
        definitions.Add(Create("Forward", "前进"));
        definitions.Add(Create("Up", "上一级"));
        definitions.Add(Create("ThisPc", "打开此电脑"));
        definitions.Add(Create("TaskBoard", "打开任务看板"));
        definitions.Add(Create("GlobalSearch", "打开全局搜索"));
        definitions.Add(Create("Connections", "打开连接管理"));
        definitions.Add(Create("DiskView", "打开磁盘视图"));
        definitions.Add(Create("ProcessMonitor", "打开进程监控"));
        definitions.Add(Create("CaptureTab", "打开截图与录屏"));
        definitions.Add(Create("ToggleIdeMode", "切换 IDE / 资源管理器模式"));
        definitions.Add(Create("ToggleTheme", "切换深浅色主题"));
        definitions.Add(Create("ToggleHiddenFiles", "显示 / 隐藏隐藏文件"));
        definitions.Add(Create("ToggleInfoPane", "显示 / 隐藏详细信息窗格"));
        definitions.Add(Create("NewFolder", "新建文件夹"));
        definitions.Add(Create("BatchRename", "批量改名"));
        definitions.Add(Create("EncodeConvert", "编码转换"));
        definitions.Add(Create("AddBookmark", "收藏当前文件夹"));
        definitions.Add(Create("ToggleDefaultFileManager", "设为 / 恢复默认资源管理器"));
        definitions.Add(Create("CloseAllTabs", "关闭全部标签"));
        definitions.Add(Create("Copy", "复制勾选项"));
        definitions.Add(Create("Cut", "剪切勾选项"));
        definitions.Add(Create("Paste", "粘贴"));
        definitions.Add(Create("Rename", "重命名"));
        definitions.Add(Create("Delete", "删除勾选项"));
        definitions.Add(Create("Save", "保存当前编辑器（IDE 模式）"));
        definitions.Add(Create("SaveAll", "保存全部编辑器（IDE 模式）"));
        definitions.Add(Create("CloseEditor", "关闭当前编辑器（IDE 模式）"));
        definitions.Add(Create("QuickOpen", "快速打开（IDE 模式）"));
        definitions.Add(Create("Find", "查找（IDE 模式）"));
        definitions.Add(Create("ScreenshotRegion", "区域截图"));
        definitions.Add(Create("ScreenshotFull", "全屏截图"));
        definitions.Add(Create("ScreenshotPrimary", "主屏截图"));
        definitions.Add(Create("ScreenshotWindow", "活动窗口截图"));
        definitions.Add(Create("ScreenshotLastRegion", "上次区域截图"));
        definitions.Add(Create("RecordToggle", "开始 / 停止录屏"));
        definitions.Add(Create("ColorPick", "屏幕取色（复制十六进制颜色）"));
        definitions.Add(Create("PastePin", "贴图（把剪贴板图片贴到屏幕）"));
        return definitions;
    }

    private static ShortcutActionDefinition Create(string actionName, string displayName)
    {
        ShortcutActionDefinition definition = new ShortcutActionDefinition();
        definition.ActionName = actionName;
        definition.DisplayName = displayName;
        return definition;
    }
}
