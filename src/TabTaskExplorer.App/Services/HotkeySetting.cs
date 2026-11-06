using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 快捷键配置：一个动作 + 一组修饰键 + 一个主键 + 作用域（应用内 / 全局）。
/// 同一动作可以配置任意多条记录，实现「一个功能绑定多个快捷键组合」。
/// </summary>
public class HotkeySetting : INotifyPropertyChanged
{
    private string actionName = string.Empty;
    private string keyText = string.Empty;
    private bool ctrl;
    private bool alt;
    private bool shift;
    private bool win;
    private string scope = "Local";
    private bool capturing;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>动作标识（固定英文标识，供程序匹配，界面上显示中文名）。</summary>
    public string ActionName
    {
        get { return this.actionName; }
        set
        {
            if (string.Equals(this.actionName, value, StringComparison.Ordinal))
            {
                return;
            }
            this.actionName = value == null ? string.Empty : value;
            this.RaisePropertyChanged(nameof(this.ActionName));
            this.RaisePropertyChanged(nameof(this.DisplayName));
            this.RaisePropertyChanged(nameof(this.ActionOption));
        }
    }

    /// <summary>主键名称（Key 枚举名，例如 T、F5、Delete、PrintScreen）；空文本表示尚未设置按键。</summary>
    public string KeyText
    {
        get { return this.keyText; }
        set
        {
            string normalized = value == null ? string.Empty : value;
            if (string.Equals(this.keyText, normalized, StringComparison.Ordinal))
            {
                return;
            }
            this.keyText = normalized;
            this.RaisePropertyChanged(nameof(this.KeyText));
            this.RaisePropertyChanged(nameof(this.ShortcutText));
        }
    }

    /// <summary>是否需要 Ctrl 控制键。</summary>
    public bool Ctrl
    {
        get { return this.ctrl; }
        set
        {
            if (this.ctrl == value)
            {
                return;
            }
            this.ctrl = value;
            this.RaisePropertyChanged(nameof(this.Ctrl));
            this.RaisePropertyChanged(nameof(this.ShortcutText));
        }
    }

    /// <summary>是否需要 Alt 替换键。</summary>
    public bool Alt
    {
        get { return this.alt; }
        set
        {
            if (this.alt == value)
            {
                return;
            }
            this.alt = value;
            this.RaisePropertyChanged(nameof(this.Alt));
            this.RaisePropertyChanged(nameof(this.ShortcutText));
        }
    }

    /// <summary>是否需要 Shift 上档键。</summary>
    public bool Shift
    {
        get { return this.shift; }
        set
        {
            if (this.shift == value)
            {
                return;
            }
            this.shift = value;
            this.RaisePropertyChanged(nameof(this.Shift));
            this.RaisePropertyChanged(nameof(this.ShortcutText));
        }
    }

    /// <summary>是否需要 Win（Windows 键 / macOS Command 键）修饰键。</summary>
    public bool Win
    {
        get { return this.win; }
        set
        {
            if (this.win == value)
            {
                return;
            }
            this.win = value;
            this.RaisePropertyChanged(nameof(this.Win));
            this.RaisePropertyChanged(nameof(this.ShortcutText));
        }
    }

    /// <summary>是否正在等待用户按下组合键（仅界面状态，不写入配置文件）。</summary>
    [JsonIgnore]
    public bool IsCapturing
    {
        get { return this.capturing; }
        set
        {
            if (this.capturing == value)
            {
                return;
            }
            this.capturing = value;
            this.RaisePropertyChanged(nameof(this.IsCapturing));
            this.RaisePropertyChanged(nameof(this.ShortcutText));
        }
    }

    /// <summary>
    /// 界面按钮上显示的组合键文本：捕获中显示提示，未设置显示引导文字，
    /// 已设置按 Ctrl、Alt、Shift、Win 的固定顺序规范化显示。
    /// </summary>
    [JsonIgnore]
    public string ShortcutText
    {
        get
        {
            if (this.capturing)
            {
                return "请按下快捷键…（Esc 取消）";
            }
            string text = FormatCombination(this.ctrl, this.alt, this.shift, this.win, this.keyText);
            return string.IsNullOrEmpty(text) ? "未设置：点击后按下组合键" : text;
        }
    }

    /// <summary>作用域：Local（仅本程序前台）或 Global（系统全局）。</summary>
    public string Scope
    {
        get { return this.scope; }
        set
        {
            string normalized = string.Equals(value, "Global", StringComparison.OrdinalIgnoreCase) ? "Global" : "Local";
            if (string.Equals(this.scope, normalized, StringComparison.Ordinal))
            {
                return;
            }
            this.scope = normalized;
            this.RaisePropertyChanged(nameof(this.Scope));
            this.RaisePropertyChanged(nameof(this.IsGlobal));
        }
    }

    /// <summary>是否系统全局快捷键（界面上用「全局」复选框编辑）。</summary>
    [JsonIgnore]
    public bool IsGlobal
    {
        get { return string.Equals(this.scope, "Global", StringComparison.Ordinal); }
        set { this.Scope = value ? "Global" : "Local"; }
    }

    /// <summary>动作的中文显示名称。</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get { return ShortcutActionCatalog.GetDisplayName(this.actionName); }
    }

    /// <summary>设置界面下拉框使用的动作定义；写入时同步 ActionName。</summary>
    [JsonIgnore]
    public ShortcutActionDefinition? ActionOption
    {
        get { return ShortcutActionCatalog.Find(this.actionName); }
        set
        {
            if (value != null && !string.Equals(this.actionName, value.ActionName, StringComparison.Ordinal))
            {
                this.ActionName = value.ActionName;
            }
        }
    }

    /// <summary>
    /// 把修饰键与主键规范化为界面显示文本：固定按 Ctrl、Alt、Shift、Win 的顺序输出，主键放在最后。
    /// 主键为空时返回空字符串，表示尚未设置。
    /// </summary>
    public static string FormatCombination(bool ctrl, bool alt, bool shift, bool win, string keyText)
    {
        if (string.IsNullOrEmpty(keyText))
        {
            return string.Empty;
        }
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        if (ctrl)
        {
            builder.Append("Ctrl+");
        }
        if (alt)
        {
            builder.Append("Alt+");
        }
        if (shift)
        {
            builder.Append("Shift+");
        }
        if (win)
        {
            builder.Append("Win+");
        }
        builder.Append(FormatKeyName(keyText));
        return builder.ToString();
    }

    /// <summary>把程序内部主键名转换为界面显示名（数字键 D1 显示为 1，Escape 显示为 Esc）。</summary>
    public static string FormatKeyName(string keyText)
    {
        if (string.IsNullOrEmpty(keyText))
        {
            return string.Empty;
        }
        if (keyText.Length == 2 && keyText[0] == 'D' && keyText[1] >= '0' && keyText[1] <= '9')
        {
            return keyText.Substring(1);
        }
        if (string.Equals(keyText, "Escape", StringComparison.Ordinal))
        {
            return "Esc";
        }
        return keyText;
    }

    /// <summary>创建默认的应用内快捷键配置（每条对应一个动作，作用域为 Local）。</summary>
    public static List<HotkeySetting> CreateDefaults()
    {
        List<HotkeySetting> defaults = new List<HotkeySetting>();
        defaults.Add(Create("NewTab", "T", true, false, false, false, "Local"));
        defaults.Add(Create("CloseTab", "W", true, false, false, false, "Local"));
        defaults.Add(Create("NextTab", "Tab", true, false, false, false, "Local"));
        defaults.Add(Create("PreviousTab", "Tab", true, false, true, false, "Local"));
        defaults.Add(Create("Refresh", "F5", false, false, false, false, "Local"));
        defaults.Add(Create("Rename", "F2", false, false, false, false, "Local"));
        defaults.Add(Create("Delete", "Delete", false, false, false, false, "Local"));
        defaults.Add(Create("Copy", "C", true, false, false, false, "Local"));
        defaults.Add(Create("Cut", "X", true, false, false, false, "Local"));
        defaults.Add(Create("Paste", "V", true, false, false, false, "Local"));
        defaults.Add(Create("Save", "S", true, false, false, false, "Local"));
        defaults.Add(Create("SaveAll", "S", true, false, true, false, "Local"));
        defaults.Add(Create("CloseEditor", "F4", true, false, false, false, "Local"));
        defaults.Add(Create("QuickOpen", "P", true, false, false, false, "Local"));
        defaults.Add(Create("Find", "F", true, false, false, false, "Local"));
        return defaults;
    }

    /// <summary>创建默认的全局快捷键配置（每条对应一个动作，作用域为 Global）。</summary>
    public static List<HotkeySetting> CreateGlobalDefaults()
    {
        List<HotkeySetting> defaults = new List<HotkeySetting>();
        defaults.Add(Create("ScreenshotRegion", "PrintScreen", false, false, false, false, "Global"));
        defaults.Add(Create("ScreenshotFull", "PrintScreen", true, false, false, false, "Global"));
        defaults.Add(Create("ScreenshotWindow", "PrintScreen", true, true, false, false, "Global"));
        defaults.Add(Create("ScreenshotLastRegion", "PrintScreen", true, false, true, false, "Global"));
        // 贴图截图使用 Shift+PrintScreen，与普通截图、全屏、活动窗口、上次区域四组默认键互不冲突
        defaults.Add(CreatePinShotDefault());
        // 滚动长截图使用 Ctrl+Alt+Shift+PrintScreen，与上述五组默认键互不冲突
        defaults.Add(CreateScrollShotDefault());
        defaults.Add(Create("RecordToggle", "R", true, false, true, false, "Global"));
        return defaults;
    }

    /// <summary>创建默认的「贴图截图」全局快捷键：Shift+PrintScreen。</summary>
    public static HotkeySetting CreatePinShotDefault()
    {
        return Create("ScreenshotPin", "PrintScreen", false, false, true, false, "Global");
    }

    /// <summary>创建默认的「滚动长截图」全局快捷键：Ctrl+Alt+Shift+PrintScreen。</summary>
    public static HotkeySetting CreateScrollShotDefault()
    {
        return Create("ScreenshotScroll", "PrintScreen", true, true, true, false, "Global");
    }

    /// <summary>创建完整的默认快捷键清单：应用内默认项 + 系统全局默认项。</summary>
    public static List<HotkeySetting> CreateDefaultShortcuts()
    {
        List<HotkeySetting> shortcuts = new List<HotkeySetting>();
        shortcuts.AddRange(CreateDefaults());
        shortcuts.AddRange(CreateGlobalDefaults());
        return shortcuts;
    }

    /// <summary>把动作标识翻译成中文显示名。</summary>
    public static string GetDisplayName(string actionName)
    {
        return ShortcutActionCatalog.GetDisplayName(actionName);
    }

    private static HotkeySetting Create(string actionName, string keyText, bool ctrl, bool alt, bool shift, bool win, string scope)
    {
        HotkeySetting setting = new HotkeySetting();
        setting.ActionName = actionName;
        setting.KeyText = keyText;
        setting.Ctrl = ctrl;
        setting.Alt = alt;
        setting.Shift = shift;
        setting.Win = win;
        setting.Scope = scope;
        return setting;
    }

    private void RaisePropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
