using System;
using System.Collections.Generic;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 快捷键设置：一个动作对应一组修饰键与主键。
/// </summary>
public class HotkeySetting
{
    /// <summary>动作标识（固定英文标识，供程序匹配，界面上显示中文名）。</summary>
    public string ActionName { get; set; } = string.Empty;

    /// <summary>主键名称（Key 枚举名，例如 T、F5、Delete）。</summary>
    public string KeyText { get; set; } = string.Empty;

    /// <summary>是否需要 Ctrl。</summary>
    public bool Ctrl { get; set; }

    /// <summary>是否需要 Alt。</summary>
    public bool Alt { get; set; }

    /// <summary>是否需要 Shift。</summary>
    public bool Shift { get; set; }

    /// <summary>动作的中文显示名称。</summary>
    public string DisplayName
    {
        get { return GetDisplayName(this.ActionName); }
    }

    /// <summary>可选主键列表（设置界面下拉框使用）。</summary>
    public static readonly List<string> KeyOptions = BuildKeyOptions();

    /// <summary>创建默认快捷键配置。</summary>
    public static List<HotkeySetting> CreateDefaults()
    {
        List<HotkeySetting> defaults = new List<HotkeySetting>();
        defaults.Add(Create("NewTab", "T", true, false, false));
        defaults.Add(Create("CloseTab", "W", true, false, false));
        defaults.Add(Create("NextTab", "Tab", true, false, false));
        defaults.Add(Create("PreviousTab", "Tab", true, false, true));
        defaults.Add(Create("Refresh", "F5", false, false, false));
        defaults.Add(Create("Rename", "F2", false, false, false));
        defaults.Add(Create("Delete", "Delete", false, false, false));
        defaults.Add(Create("Copy", "C", true, false, false));
        defaults.Add(Create("Cut", "X", true, false, false));
        defaults.Add(Create("Paste", "V", true, false, false));
        defaults.Add(Create("Save", "S", true, false, false));
        defaults.Add(Create("SaveAll", "S", true, false, true));
        defaults.Add(Create("CloseEditor", "F4", true, false, false));
        defaults.Add(Create("QuickOpen", "P", true, false, false));
        defaults.Add(Create("Find", "F", true, false, false));
        return defaults;
    }

    /// <summary>
    /// 创建默认的全局快捷键配置：系统任何程序在前台时都能触发截图与录屏，
    /// 与「快捷操作」中的应用内快捷键互不影响。
    /// </summary>
    public static List<HotkeySetting> CreateGlobalDefaults()
    {
        List<HotkeySetting> defaults = new List<HotkeySetting>();
        defaults.Add(Create("ScreenshotRegion", "PrintScreen", false, false, false));
        defaults.Add(Create("ScreenshotFull", "PrintScreen", true, false, false));
        defaults.Add(Create("ScreenshotWindow", "PrintScreen", true, true, false));
        defaults.Add(Create("RecordToggle", "R", true, false, true));
        return defaults;
    }

    /// <summary>把动作标识翻译成中文显示名。</summary>
    public static string GetDisplayName(string actionName)
    {
        switch (actionName)
        {
            case "NewTab": return "新建标签";
            case "CloseTab": return "关闭当前标签";
            case "NextTab": return "切换到下一个标签";
            case "PreviousTab": return "切换到上一个标签";
            case "Refresh": return "刷新";
            case "Rename": return "重命名";
            case "Delete": return "删除勾选项";
            case "Copy": return "复制勾选项";
            case "Cut": return "剪切勾选项";
            case "Paste": return "粘贴";
            case "Save": return "保存（IDE 模式）";
            case "SaveAll": return "全部保存（IDE 模式）";
            case "CloseEditor": return "关闭编辑器（IDE 模式）";
            case "QuickOpen": return "快速打开（IDE 模式）";
            case "Find": return "查找（IDE 模式）";
            case "ScreenshotRegion": return "区域截图（全局）";
            case "ScreenshotFull": return "全屏截图（全局）";
            case "ScreenshotWindow": return "活动窗口截图（全局）";
            case "RecordToggle": return "开始 / 停止录屏（全局）";
            default: return actionName;
        }
    }

    private static HotkeySetting Create(string actionName, string keyText, bool ctrl, bool alt, bool shift)
    {
        HotkeySetting setting = new HotkeySetting();
        setting.ActionName = actionName;
        setting.KeyText = keyText;
        setting.Ctrl = ctrl;
        setting.Alt = alt;
        setting.Shift = shift;
        return setting;
    }

    private static List<string> BuildKeyOptions()
    {
        List<string> keys = new List<string>();
        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            keys.Add(letter.ToString());
        }
        for (int number = 0; number <= 9; number++)
        {
            keys.Add("D" + number);
        }
        for (int function = 1; function <= 12; function++)
        {
            keys.Add("F" + function);
        }
        keys.Add("Tab");
        keys.Add("Enter");
        keys.Add("Space");
        keys.Add("Delete");
        keys.Add("Insert");
        keys.Add("Backspace");
        keys.Add("Home");
        keys.Add("End");
        keys.Add("PageUp");
        keys.Add("PageDown");
        keys.Add("PrintScreen");
        keys.Add("Escape");
        return keys;
    }
}
