using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Media;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>设置窗口：模仿 TabX Explorer 设置中心的分页布局。</summary>
public partial class SettingsWindow : Window
{
    /// <summary>右键菜单分页的一行数据：显示名、注册表路径、状态角标与启停开关。</summary>
    private sealed class SystemMenuRow
    {
        /// <summary>菜单项显示名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>完整注册表路径，用于界面展示与日志记录。</summary>
        public string PathText { get; set; } = string.Empty;

        /// <summary>状态角标文字：只读、需管理员、系统项、已启用或已禁用。</summary>
        public string BadgeText { get; set; } = string.Empty;

        /// <summary>开关是否可操作：只读条目不可启停。</summary>
        public bool CanToggle { get; set; }

        /// <summary>开关当前勾选状态，对应菜单项是否已启用。</summary>
        public bool ToggleOn { get; set; }

        /// <summary>对应的系统右键菜单条目，启停时用于写回注册表。</summary>
        public SystemContextMenuEntry Entry { get; set; } = null!;
    }

    private readonly ObservableCollection<FileOpenRule> openRules = new ObservableCollection<FileOpenRule>();

    // 快捷键编辑副本：窗口内增删改只作用于副本，点击确定后才写回共享设置
    private readonly ObservableCollection<HotkeySetting> shortcutRows = new ObservableCollection<HotkeySetting>();

    // 是否已一键停用全部快捷键：为真时保存后持久化为停用状态，重启不会回填默认快捷键
    private bool shortcutsCleared;

    // 快捷键变更立即生效回调：由主窗口注入，用于即时注销全局热键与低级键盘钩子并落盘
    private readonly Action? applyShortcutsImmediately;

    // 当前正在等待用户按下组合键的快捷键行；为空表示未处于捕获状态
    private HotkeySetting? capturingShortcut;

    // 已记录过「拦下滚轮」日志的下拉框，避免每一次滚轮都刷日志
    private readonly HashSet<ComboBox> wheelGuardedComboBoxes = new HashSet<ComboBox>();

    // 网页端配置的编辑副本：窗口内改动只作用于副本，点击确定后才写回共享设置
    private List<WebPortSetting> webPorts = new List<WebPortSetting>();
    private List<WebAccessProfile> webProfiles = new List<WebAccessProfile>();
    private readonly ObservableCollection<string> webPortLabels = new ObservableCollection<string>();
    private readonly ObservableCollection<string> webProfileLabels = new ObservableCollection<string>();
    private readonly ObservableCollection<string> webProfileChoices = new ObservableCollection<string>();
    private int selectedPortIndex = -1;
    private int selectedProfileIndex = -1;
    private bool suppressWebSelection;
    private bool loadingWebFields;

    // 系统右键菜单服务：由打开设置窗口一方注入，平台不支持时该分页只展示不支持提示
    private readonly ISystemContextMenuService? systemContextMenus;
    // 右键菜单分页的行数据，随筛选条件一起重建
    private readonly ObservableCollection<SystemMenuRow> systemMenuRows = new ObservableCollection<SystemMenuRow>();
    // 分页说明文字的初始内容：切到“不支持”提示后，下次加载时按此还原
    private string rightMenuIntroText = string.Empty;
    // 是否已完成首次加载，避免每次切回该分页都重新扫描
    private bool systemMenuLoaded;
    // 截图录屏分页是否已经按当前设置初始化过控件
    private bool capturePageLoaded;
    // 填充列表期间屏蔽开关事件，避免回填勾选状态被当成用户启停操作
    private bool suppressSystemMenuToggle;
    // 主窗口注入的启动项管理标签打开回调：设置窗口内不重复实现启动项列表，只做统一入口跳转
    private readonly Action? openStartupManager;
    // 日志查看器窗口：同一时间只保留一个，重复点击时激活既有窗口
    private LogViewerWindow? logViewerWindow;
    // 诊断包是否正在后台生成，避免重复点击造成并发打包
    private bool diagnosticsExporting;

    /// <summary>「基本设置」分页在左侧分页列表中的序号。</summary>
    public const int BasicSectionIndex = 0;

    /// <summary>「右键菜单」分页在左侧分页列表中的序号。</summary>
    public const int RightMenuSectionIndex = 7;

    /// <summary>「启动项管理」分页在左侧分页列表中的序号。</summary>
    public const int StartupSectionIndex = 8;

    public SettingsWindow()
    {
        this.InitializeComponent();
        // 快捷键捕获与下拉框滚轮拦截都走窗口级的隧道阶段处理，可覆盖数据模板动态生成的下拉框
        this.AddHandler(KeyDownEvent, this.OnShortcutCaptureKeyDown, RoutingStrategies.Tunnel, true);
        this.AddHandler(InputElement.PointerWheelChangedEvent, this.OnSettingsPointerWheelChanged, RoutingStrategies.Tunnel, true);
        // 设置窗口是主窗口的模态附属窗口，不单独占用任务栏按钮
        WindowHostStyler.MakeAuxiliary(this);
        UiLogger.AttachWindow(this);
        this.OpenRuleList.ItemsSource = this.openRules;
        this.ShortcutList.ItemsSource = this.shortcutRows;
        this.WebPortList.ItemsSource = this.webPortLabels;
        this.WebProfileList.ItemsSource = this.webProfileLabels;
        this.WebProfileBox.ItemsSource = this.webProfileChoices;
        this.RightMenuList.ItemsSource = this.systemMenuRows;
        this.rightMenuIntroText = this.RightMenuIntroText.Text ?? string.Empty;
        // 选中第一项必须在 XAML 初始化完成后进行：初始化阶段各分页容器尚未创建，此时触发分页切换会取到空引用
        this.SectionList.SelectedIndex = 0;
    }

    /// <summary>
    /// 绑定共享设置对象与系统右键菜单服务：改动即时生效，点确定保存到磁盘。
    /// 初始分页序号用于侧边栏「右键管理」等入口直接定位到对应分页。
    /// </summary>
    public SettingsWindow(AppSettings settings, ISystemContextMenuService systemContextMenus, string captureEnvironmentText,
        int initialSectionIndex = 0, Action? openStartupManager = null, Action? applyShortcutsImmediately = null) : this()
    {
        this.systemContextMenus = systemContextMenus;
        this.openStartupManager = openStartupManager;
        this.applyShortcutsImmediately = applyShortcutsImmediately;
        this.DataContext = settings;
        this.LogLevelBox.SelectedIndex = (int)UiLogger.ParseLevel(settings.LogLevel, LogLevel.Info);
        this.LogDirectoryText.Text = "日志目录=" + UiLogger.LogDirectory;
        this.EditorFontSizeBox.Text = settings.EditorFontSize.ToString();
        this.EditorTabWidthBox.Text = settings.EditorTabWidth.ToString();
        this.DefaultOpenModeBox.SelectedIndex = settings.DefaultOpenMode == "Ide" ? 1 : 0;
        this.DefaultPriorityBox.SelectedIndex = settings.FileOpenDefaultPriority == FileOpenPriority.System ? 1 : 0;
        if (settings.FileOpenRules != null)
        {
            foreach (FileOpenRule rule in settings.FileOpenRules)
            {
                if (rule != null)
                {
                    this.openRules.Add(rule);
                }
            }
        }
        this.InitializeWebSettings(settings);
        this.LoadShortcuts(settings);
        this.LoadCaptureSettings();
        if (!string.IsNullOrEmpty(captureEnvironmentText))
        {
            this.CaptureEnvironmentText.Text = captureEnvironmentText;
        }
        // 按调用入口定位初始分页：侧边栏「右键管理」直接进入右键菜单分页，默认仍为基本设置
        if (initialSectionIndex >= 0 && initialSectionIndex < this.SectionList.ItemCount)
        {
            this.SectionList.SelectedIndex = initialSectionIndex;
            UiLogger.Log("设置窗口", "按入口定位分页：初始分页序号=" + initialSectionIndex + " 结果=成功");
        }
        else
        {
            UiLogger.Log("设置窗口", "按入口定位分页：初始分页序号=" + initialSectionIndex
                + " 超出分页总数=" + this.SectionList.ItemCount + " 结果=失败，保持默认分页");
        }
    }

    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = this.SectionList.SelectedIndex;
        // XAML 初始化时 ListBox 会因第一项 IsSelected="True" 触发本事件，此时各分页容器尚未创建，直接跳过
        if (this.BasicPage == null || this.MousePage == null || this.FilePage == null
            || this.EditorPage == null || this.OpenWithPage == null || this.MediaPage == null
            || this.WebPage == null || this.RightMenuPage == null || this.StartupPage == null
            || this.CapturePage == null || this.AboutPage == null)
        {
            UiLogger.Log("设置窗口", "分页切换：初始化阶段分页容器尚未创建，跳过 index=" + index);
            return;
        }
        UiLogger.Log("设置窗口", "分页切换：index=" + index);
        if (this.capturingShortcut != null && index != 1)
        {
            this.CancelShortcutCapture("切换到其他设置分页");
        }
        this.BasicPage.IsVisible = index == BasicSectionIndex;
        this.MousePage.IsVisible = index == 1;
        this.FilePage.IsVisible = index == 2;
        this.EditorPage.IsVisible = index == 3;
        this.OpenWithPage.IsVisible = index == 4;
        this.MediaPage.IsVisible = index == 5;
        this.WebPage.IsVisible = index == 6;
        this.RightMenuPage.IsVisible = index == RightMenuSectionIndex;
        this.StartupPage.IsVisible = index == StartupSectionIndex;
        this.CapturePage.IsVisible = index == 9;
        this.AboutPage.IsVisible = index == 10;
        if (index == 5 && !this.mediaPageLoaded)
        {
            this.LoadMediaSettings();
        }
        if (index == 9 && !this.capturePageLoaded)
        {
            this.LoadCaptureSettings();
        }
        if (index == 7 && !this.systemMenuLoaded)
        {
            this.LoadSystemMenuList();
            UiLogger.Log("设置窗口", "分页切换：index=7 分组=" + this.GetSelectedGroupText()
                + " 状态=" + this.GetSelectedStatusText()
                + " 搜索词=" + this.GetSearchText()
                + " 结果=成功，已首次加载系统右键菜单项列表");
        }
    }

    /// <summary>新增一条按类型打开规则，并选中等待填写扩展名。</summary>
    private void OnAddOpenRuleClick(object? sender, RoutedEventArgs e)
    {
        FileOpenRule rule = new FileOpenRule();
        rule.Extension = string.Empty;
        rule.Priority = FileOpenPriority.Builtin;
        this.openRules.Add(rule);
        this.OpenRuleList.SelectedItem = rule;
        UiLogger.Log("设置窗口", "点击添加类型：已新增一条待填写扩展名的规则"
            + " 当前规则条数=" + this.openRules.Count
            + " 结果=成功");
    }

    /// <summary>删除选中的打开方式规则。</summary>
    private void OnRemoveOpenRuleClick(object? sender, RoutedEventArgs e)
    {
        FileOpenRule? rule = this.OpenRuleList.SelectedItem as FileOpenRule;
        if (rule == null)
        {
            UiLogger.Log("设置窗口", "点击删除选中规则被拒绝：未选中任何规则 结果=失败");
            return;
        }
        this.openRules.Remove(rule);
        UiLogger.Log("设置窗口", "点击删除选中规则：扩展名=" + rule.Extension
            + " 当前规则条数=" + this.openRules.Count
            + " 结果=成功");
    }

    /// <summary>新增一条快捷键记录：默认绑定「新建标签」，等待用户选择功能与按键。</summary>
    private void OnAddShortcutClick(object? sender, RoutedEventArgs e)
    {
        HotkeySetting row = new HotkeySetting();
        row.ActionName = "NewTab";
        row.Scope = "Local";
        row.KeyText = string.Empty;
        // 手动添加说明用户需要快捷键，解除「全部停用」状态
        this.shortcutsCleared = false;
        this.UpdateShortcutDisabledHint();
        this.shortcutRows.Add(row);
        UiLogger.Log("设置窗口", "添加快捷键：已新增一条待配置记录 当前快捷键条数=" + this.shortcutRows.Count
            + " 是否全部停用=否");
    }

    /// <summary>删除按钮所在行的快捷键记录。</summary>
    private void OnRemoveShortcutClick(object? sender, RoutedEventArgs e)
    {
        Button? button = sender as Button;
        HotkeySetting? row = button == null ? null : button.DataContext as HotkeySetting;
        if (row == null)
        {
            UiLogger.Log("设置窗口", "删除快捷键失败：未取到快捷键记录 结果=未删除");
            return;
        }
        if (ReferenceEquals(this.capturingShortcut, row))
        {
            this.CancelShortcutCapture("删除正在捕获的快捷键记录");
        }
        this.shortcutRows.Remove(row);
        UiLogger.Log("设置窗口", "删除快捷键：功能=" + row.DisplayName
            + " 主键=" + (string.IsNullOrEmpty(row.KeyText) ? "未设置" : row.KeyText)
            + " 剩余快捷键条数=" + this.shortcutRows.Count
            + " 结果=成功");
    }

    /// <summary>把快捷键配置恢复为默认值（应用内默认项 + 全局截图录屏默认项），并立即重新启用全局热键。</summary>
    private void OnResetHotkeysClick(object? sender, RoutedEventArgs e)
    {
        bool wasDisabled = this.shortcutsCleared;
        this.CancelShortcutCapture("恢复默认快捷键");
        this.shortcutsCleared = false;
        this.shortcutRows.Clear();
        foreach (HotkeySetting setting in HotkeySetting.CreateDefaultShortcuts())
        {
            this.shortcutRows.Add(CloneShortcut(setting));
        }
        this.UpdateShortcutDisabledHint();
        // 恢复默认是「一键清空」的对向操作，立即写回并重新注册，用户无需再点确定才生效
        this.ApplyShortcutsNow();
        UiLogger.Log("设置窗口", "恢复默认快捷键：已重建默认项"
            + " 恢复前是否全部停用=" + (wasDisabled ? "是" : "否")
            + " 快捷键条数=" + this.shortcutRows.Count
            + " 全局条数=" + CountGlobalShortcuts(this.shortcutRows)
            + " 结果=成功");
    }

    /// <summary>
    /// 一键清空全部快捷键：二次确认后清空配置，并立即注销已注册的全局热键与低级键盘钩子，
    /// 同时把停用状态写入设置文件，重启后不会因默认值回填而恢复。
    /// </summary>
    private async void OnClearAllShortcutsClick(object? sender, RoutedEventArgs e)
    {
        int beforeCount = this.shortcutRows.Count;
        int beforeGlobalCount = CountGlobalShortcuts(this.shortcutRows);
        bool confirmed = await MessageDialog.ConfirmAsync(this, "清空全部快捷键",
            "确定要清空全部快捷键吗？\n\n"
            + "后果：所有快捷键（应用内与系统全局，含截图、录屏、滚动长截图、贴图、取色、IDE 与标签操作）都会立即失效，"
            + "进程内的全局热键与低级键盘钩子也会一并注销；需要重新设置或点击「恢复默认快捷键」才能恢复。",
            "清空并立即停用", "取消");
        if (!confirmed)
        {
            UiLogger.Log("设置窗口", "一键清空快捷键：用户取消 原有快捷键条数=" + beforeCount
                + " 原有全局条数=" + beforeGlobalCount + " 结果=未停用");
            return;
        }
        this.CancelShortcutCapture("一键清空全部快捷键");
        this.shortcutRows.Clear();
        this.shortcutsCleared = true;
        this.UpdateShortcutDisabledHint();
        // 立即写回共享设置并触发全局热键注销与钩子卸载，避免用户还要等点击确定才生效
        this.ApplyShortcutsNow();
        UiLogger.Log("设置窗口", "一键清空快捷键：原有快捷键条数=" + beforeCount
            + " 原有全局条数=" + beforeGlobalCount
            + " 清空后快捷键条数=0"
            + " 是否全部停用=是"
            + " 全局热键=已注销 低级键盘钩子=已卸载"
            + " 结果=成功");
    }

    /// <summary>把快捷键编辑副本立即写回共享设置并触发全局热键重新注册；用于清空与恢复默认的即时生效。</summary>
    private void ApplyShortcutsNow()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings != null)
        {
            this.CommitShortcuts(settings);
        }
        if (this.applyShortcutsImmediately != null)
        {
            this.applyShortcutsImmediately();
        }
    }

    /// <summary>刷新「全部快捷键已停用」提示文字的可见性。</summary>
    private void UpdateShortcutDisabledHint()
    {
        if (this.ShortcutDisabledHint != null)
        {
            this.ShortcutDisabledHint.IsVisible = this.shortcutsCleared;
        }
    }

    /// <summary>点击组合键按钮：进入捕获状态；再次点击同一条则取消捕获。</summary>
    private void OnCaptureShortcutClick(object? sender, RoutedEventArgs e)
    {
        Button? button = sender as Button;
        HotkeySetting? row = button != null ? button.DataContext as HotkeySetting : null;
        if (row == null)
        {
            UiLogger.Log("设置窗口", "快捷键捕获：未能识别对应的快捷键行 结果=已忽略");
            return;
        }
        if (ReferenceEquals(this.capturingShortcut, row))
        {
            this.CancelShortcutCapture("用户再次点击组合键按钮");
            return;
        }
        this.CancelShortcutCapture("切换到另一条快捷键");
        this.capturingShortcut = row;
        row.IsCapturing = true;
        UiLogger.Log("设置窗口", "进入快捷键捕获：功能=" + row.DisplayName
            + " 作用域=" + (row.IsGlobal ? "系统全局" : "应用内")
            + " 当前组合=" + HotkeySetting.FormatCombination(row.Ctrl, row.Alt, row.Shift, row.Win, row.KeyText)
            + " 提示=请按下快捷键，按 Esc 取消捕获");
    }

    /// <summary>
    /// 捕获状态下的按键处理：Esc 取消，仅修饰键不结束，其余按键组合直接成为该功能的快捷键；
    /// 与同作用域已有快捷键重复时给出提示并继续等待输入。
    /// </summary>
    private void OnShortcutCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        HotkeySetting? row = this.capturingShortcut;
        if (row == null)
        {
            return;
        }
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            this.CancelShortcutCapture("用户按下 Esc");
            return;
        }
        if (e.Key == Key.None || IsModifierOnlyKey(e.Key))
        {
            // 只按住修饰键时不结束捕获，等待用户按下真正的主键
            e.Handled = true;
            return;
        }
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool win = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        string keyText = e.Key.ToString();
        if (string.IsNullOrEmpty(keyText) || string.Equals(keyText, "None", StringComparison.Ordinal))
        {
            e.Handled = true;
            return;
        }
        HotkeySetting? conflict = this.FindShortcutConflict(row, ctrl, alt, shift, win, keyText);
        if (conflict != null)
        {
            e.Handled = true;
            string combination = HotkeySetting.FormatCombination(ctrl, alt, shift, win, keyText);
            string scopeText = row.IsGlobal ? "系统全局" : "应用内";
            UiLogger.Log("设置窗口", "快捷键捕获冲突：功能=" + row.DisplayName
                + " 组合=" + combination
                + " 作用域=" + scopeText
                + " 冲突功能=" + conflict.DisplayName
                + " 结果=已提示，继续等待用户重新按下组合键");
            MessageDialog.Show(this, "快捷键冲突",
                "组合「" + combination + "」已被「" + conflict.DisplayName + "」使用（同为" + scopeText
                + "），请按下其他组合键，或按 Esc 取消捕获。");
            return;
        }
        string previousText = HotkeySetting.FormatCombination(row.Ctrl, row.Alt, row.Shift, row.Win, row.KeyText);
        row.Ctrl = ctrl;
        row.Alt = alt;
        row.Shift = shift;
        row.Win = win;
        row.KeyText = keyText;
        row.IsCapturing = false;
        this.capturingShortcut = null;
        e.Handled = true;
        UiLogger.Log("设置窗口", "快捷键捕获完成：功能=" + row.DisplayName
            + " 作用域=" + (row.IsGlobal ? "系统全局" : "应用内")
            + " 原组合=" + (string.IsNullOrEmpty(previousText) ? "未设置" : previousText)
            + " 新组合=" + row.ShortcutText
            + " 结果=成功");
    }

    /// <summary>结束当前捕获状态并记录原因，不修改该行原有组合。</summary>
    private void CancelShortcutCapture(string reason)
    {
        HotkeySetting? row = this.capturingShortcut;
        if (row == null)
        {
            return;
        }
        row.IsCapturing = false;
        this.capturingShortcut = null;
        UiLogger.Log("设置窗口", "结束快捷键捕获：功能=" + row.DisplayName
            + " 保留组合=" + HotkeySetting.FormatCombination(row.Ctrl, row.Alt, row.Shift, row.Win, row.KeyText)
            + " 原因=" + reason
            + " 结果=未修改");
    }

    /// <summary>判断按键是否只是修饰键或锁定键：这类按键不作为组合键的主键。</summary>
    private static bool IsModifierOnlyKey(Key key)
    {
        switch (key)
        {
            case Key.LeftCtrl:
            case Key.RightCtrl:
            case Key.LeftAlt:
            case Key.RightAlt:
            case Key.LeftShift:
            case Key.RightShift:
            case Key.LWin:
            case Key.RWin:
            case Key.CapsLock:
            case Key.NumLock:
            case Key.Scroll:
                return true;
            default:
                return false;
        }
    }

    /// <summary>查找同作用域下已占用该组合的其他快捷键行。</summary>
    private HotkeySetting? FindShortcutConflict(HotkeySetting row, bool ctrl, bool alt, bool shift, bool win, string keyText)
    {
        foreach (HotkeySetting other in this.shortcutRows)
        {
            if (ReferenceEquals(other, row) || string.IsNullOrEmpty(other.KeyText))
            {
                continue;
            }
            if (other.IsGlobal != row.IsGlobal)
            {
                continue;
            }
            if (other.Ctrl != ctrl || other.Alt != alt || other.Shift != shift || other.Win != win)
            {
                continue;
            }
            if (string.Equals(other.KeyText, keyText, StringComparison.OrdinalIgnoreCase))
            {
                return other;
            }
        }
        return null;
    }

    /// <summary>
    /// 下拉框未展开时拦截滚轮：不改变下拉框选中值，改为滚动最近的外层页面；
    /// 下拉框展开时不做拦截，滚轮仍用于切换选项。
    /// </summary>
    private void OnSettingsPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        Visual? source = e.Source as Visual;
        if (source == null)
        {
            return;
        }
        ComboBox? comboBox = source.FindAncestorOfType<ComboBox>(true);
        if (comboBox == null || comboBox.IsDropDownOpen)
        {
            return;
        }
        ScrollViewer? viewer = comboBox.FindAncestorOfType<ScrollViewer>();
        if (viewer == null)
        {
            return;
        }
        double maximumOffset = Math.Max(0.0, viewer.Extent.Height - viewer.Viewport.Height);
        double targetOffset = viewer.Offset.Y - (e.Delta.Y * 48.0);
        if (targetOffset < 0.0)
        {
            targetOffset = 0.0;
        }
        if (targetOffset > maximumOffset)
        {
            targetOffset = maximumOffset;
        }
        viewer.Offset = new Vector(viewer.Offset.X, targetOffset);
        e.Handled = true;
        if (this.wheelGuardedComboBoxes.Add(comboBox))
        {
            UiLogger.Log("设置窗口", "下拉框未展开时拦截滚轮：已阻止滚动改值并转交外层页面滚动 当前值="
                + (comboBox.SelectedItem != null ? comboBox.SelectedItem.ToString() : "空")
                + " 结果=成功");
        }
    }

    /// <summary>把共享设置里的快捷键复制到窗口内编辑副本。</summary>
    private void LoadShortcuts(AppSettings settings)
    {
        this.shortcutRows.Clear();
        if (settings.Shortcuts != null)
        {
            foreach (HotkeySetting setting in settings.Shortcuts)
            {
                if (setting != null)
                {
                    this.shortcutRows.Add(CloneShortcut(setting));
                }
            }
        }
        this.shortcutsCleared = settings.ShortcutsDisabled;
        this.UpdateShortcutDisabledHint();
        UiLogger.Log("设置窗口", "快捷键分页初始化：快捷键条数=" + this.shortcutRows.Count
            + " 其中全局条数=" + CountGlobalShortcuts(this.shortcutRows)
            + " 是否全部停用=" + (this.shortcutsCleared ? "是" : "否"));
    }

    /// <summary>把窗口内编辑副本写回共享设置。</summary>
    private void CommitShortcuts(AppSettings settings)
    {
        if (settings.Shortcuts == null)
        {
            settings.Shortcuts = new List<HotkeySetting>();
        }
        settings.Shortcuts.Clear();
        foreach (HotkeySetting row in this.shortcutRows)
        {
            // 捕获状态仅为界面提示，不随设置写入
            row.IsCapturing = false;
            settings.Shortcuts.Add(row);
        }
        settings.ShortcutsMigrated = true;
        settings.ShortcutsDisabled = this.shortcutsCleared;
        UiLogger.Log("配置更改", "快捷键设置：快捷键条数=" + settings.Shortcuts.Count
            + " 其中全局条数=" + CountGlobalShortcuts(settings.Shortcuts)
            + " 是否全部停用=" + (settings.ShortcutsDisabled ? "是" : "否"));
    }

    /// <summary>复制一条快捷键配置，避免窗口内修改直接影响磁盘设置。</summary>
    private static HotkeySetting CloneShortcut(HotkeySetting source)
    {
        HotkeySetting copy = new HotkeySetting();
        copy.ActionName = source.ActionName;
        copy.KeyText = source.KeyText;
        copy.Ctrl = source.Ctrl;
        copy.Alt = source.Alt;
        copy.Shift = source.Shift;
        copy.Win = source.Win;
        copy.Scope = source.Scope;
        return copy;
    }

    /// <summary>统计全局作用域的快捷键条数。</summary>
    private static int CountGlobalShortcuts(IList<HotkeySetting> shortcuts)
    {
        int count = 0;
        for (int index = 0; index < shortcuts.Count; index++)
        {
            if (shortcuts[index].IsGlobal)
            {
                count++;
            }
        }
        return count;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        this.CancelShortcutCapture("点击确定保存设置");
        this.CommitShortcuts(this.DataContext as AppSettings ?? new AppSettings());
        this.CommitWebSettings();
        this.CommitEditorSettings();
        this.CommitOpenRules();
        this.CommitMediaSettings();
        this.CommitCaptureSettings();
        this.CommitLogSettings();
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings != null)
        {
            UiLogger.Log("配置更改", "设置窗口确定：日志最低级别=" + settings.LogLevel
                + " 网页端端口数=" + settings.WebPorts.Count
                + " 网页端权限方案数=" + settings.WebAccessProfiles.Count
                + " 编辑器字号=" + settings.EditorFontSize
                + " 编辑器Tab宽度=" + settings.EditorTabWidth
                + " 默认打开模式=" + settings.DefaultOpenMode
                + " 打开方式默认优先级=" + settings.FileOpenDefaultPriority
                + " 打开方式规则数=" + (settings.FileOpenRules != null ? settings.FileOpenRules.Count : 0)
                + " 快捷键条数=" + settings.Shortcuts.Count
                + " 全局快捷键条数=" + CountGlobalShortcuts(settings.Shortcuts)
                + " 截图保存目录=" + (string.IsNullOrEmpty(settings.CaptureSaveDirectory) ? "默认图片目录" : settings.CaptureSaveDirectory)
                + " 截图后复制到剪贴板=" + settings.CaptureCopyToClipboard
                + " 完成后自动打开产物文件夹=" + settings.CaptureOpenFolderAfter
                + " 截图包含鼠标指针=" + settings.CaptureIncludeCursor
                + " 截图延迟秒数=" + settings.CaptureDelaySeconds
                + " 录屏帧率=" + settings.CaptureRecordFps
                + " 录屏画质参数=" + settings.CaptureRecordQuality
                + " 录屏麦克风设备=" + (string.IsNullOrEmpty(settings.CaptureMicrophoneDevice) ? "未启用" : settings.CaptureMicrophoneDevice)
                + " FFmpeg路径=" + (string.IsNullOrEmpty(settings.FfmpegPath) ? "自动查找" : settings.FfmpegPath));
            foreach (WebPortSetting port in settings.WebPorts)
            {
                UiLogger.Log("配置更改", "网页端端口：端口号=" + port.Port + " 是否启用=" + port.Enabled
                    + " 监听范围=" + port.BindAddress + " 权限方案=" + port.ProfileName);
            }
            foreach (WebAccessProfile profile in settings.WebAccessProfiles)
            {
                UiLogger.Log("配置更改", "网页端权限方案：方案名称=" + profile.Name
                    + " 允许修改文件=" + profile.AllowModify
                    + " 允许新建删除=" + profile.AllowCreateDelete
                    + " 需要密码=" + profile.RequirePassword
                    + " 验证账号=" + profile.Account
                    + " 来源地址条数=" + profile.AllowedAddresses.Count
                    + " 允许路径条数=" + profile.AllowedPaths.Count);
            }
        }
        UiLogger.Log("设置窗口", "点击确定：提交设置并关闭");
        this.Close(true);
    }

    /// <summary>媒体分页是否已经按当前设置初始化过控件。</summary>
    private bool mediaPageLoaded;

    /// <summary>按当前设置初始化媒体分页的控件选中项。</summary>
    private void LoadMediaSettings()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        this.mediaPageLoaded = true;
        if (settings.MediaOpenMode == MediaOpenModeValues.System)
        {
            this.MediaModeBox.SelectedIndex = 1;
        }
        else if (settings.MediaOpenMode == MediaOpenModeValues.Ask)
        {
            this.MediaModeBox.SelectedIndex = 2;
        }
        else
        {
            this.MediaModeBox.SelectedIndex = 0;
        }
        this.MediaScopeBox.SelectedIndex = string.Equals(settings.MediaPlaylistScope, "AllMedia", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        int maxSize = settings.MediaImageMaxDecodeSize;
        if (maxSize <= 0)
        {
            this.MediaDecodeBox.SelectedIndex = 3;
        }
        else if (maxSize <= 2048)
        {
            this.MediaDecodeBox.SelectedIndex = 0;
        }
        else if (maxSize <= 4096)
        {
            this.MediaDecodeBox.SelectedIndex = 1;
        }
        else
        {
            this.MediaDecodeBox.SelectedIndex = 2;
        }
        UiLogger.Log("设置窗口", "媒体分页初始化：打开方式=" + settings.MediaOpenMode
            + " 播放列表范围=" + settings.MediaPlaylistScope
            + " 解码上限=" + maxSize);
    }

    /// <summary>提交媒体分页设置：打开方式、播放列表范围与图片解码上限。</summary>
    private void CommitMediaSettings()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        int modeIndex = this.MediaModeBox.SelectedIndex;
        if (modeIndex == 1)
        {
            settings.MediaOpenMode = MediaOpenModeValues.System;
        }
        else if (modeIndex == 2)
        {
            settings.MediaOpenMode = MediaOpenModeValues.Ask;
        }
        else
        {
            settings.MediaOpenMode = MediaOpenModeValues.Builtin;
        }
        settings.MediaPlaylistScope = this.MediaScopeBox.SelectedIndex == 1 ? "AllMedia" : "SameKind";
        int[] sizes = new int[] { 2048, 4096, 8192, 0 };
        int sizeIndex = this.MediaDecodeBox.SelectedIndex;
        if (sizeIndex < 0 || sizeIndex >= sizes.Length)
        {
            sizeIndex = 1;
        }
        settings.MediaImageMaxDecodeSize = sizes[sizeIndex];
        UiLogger.Log("配置更改", "媒体设置：打开方式=" + settings.MediaOpenMode
            + " 播放列表范围=" + settings.MediaPlaylistScope
            + " 解码上限=" + settings.MediaImageMaxDecodeSize);
    }

    /// <summary>按当前设置初始化截图录屏分页的控件。</summary>
    private void LoadCaptureSettings()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        this.capturePageLoaded = true;
        this.CaptureSaveDirectoryBox.Text = settings.CaptureSaveDirectory;
        this.FfmpegPathBox.Text = settings.FfmpegPath;
        this.CaptureMicrophoneBox.Text = settings.CaptureMicrophoneDevice;
        this.OcrTesseractPathBox.Text = settings.OcrTesseractPath;
        this.OcrLanguageBox.Text = settings.OcrLanguage;
        int[] delays = new int[] { 0, 3, 5, 10 };
        this.CaptureDelayBox.SelectedIndex = FindIntegerIndex(delays, settings.CaptureDelaySeconds, 0);
        int[] frames = new int[] { 24, 30, 60 };
        this.CaptureFpsBox.SelectedIndex = FindIntegerIndex(frames, settings.CaptureRecordFps, 1);
        int[] qualities = new int[] { 18, 23, 28 };
        this.CaptureQualityBox.SelectedIndex = FindIntegerIndex(qualities, settings.CaptureRecordQuality, 1);
        UiLogger.Log("设置窗口", "截图录屏分页初始化：保存目录=" + (string.IsNullOrEmpty(settings.CaptureSaveDirectory) ? "默认图片目录" : settings.CaptureSaveDirectory)
            + " FFmpeg路径=" + (string.IsNullOrEmpty(settings.FfmpegPath) ? "自动查找" : settings.FfmpegPath)
            + " 延迟秒数=" + settings.CaptureDelaySeconds
            + " 帧率=" + settings.CaptureRecordFps
            + " 画质参数=" + settings.CaptureRecordQuality
            + " 麦克风设备=" + (string.IsNullOrEmpty(settings.CaptureMicrophoneDevice) ? "未启用" : settings.CaptureMicrophoneDevice)
            + " 快捷键总条数=" + settings.Shortcuts.Count
            + " 全局快捷键条数=" + CountGlobalShortcuts(settings.Shortcuts));
    }

    /// <summary>提交截图录屏分页设置：文本与下拉框写入共享设置对象，确定后由主窗口统一保存。</summary>
    private void CommitCaptureSettings()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        if (!this.capturePageLoaded)
        {
            // 从未初始化控件时不要回写空值，避免把用户已有的截图录屏设置覆盖为默认值
            UiLogger.Log("设置窗口", "截图录屏设置未提交：控件尚未初始化");
            return;
        }
        settings.CaptureSaveDirectory = this.CaptureSaveDirectoryBox.Text == null
            ? string.Empty : this.CaptureSaveDirectoryBox.Text.Trim();
        settings.FfmpegPath = this.FfmpegPathBox.Text == null
            ? string.Empty : this.FfmpegPathBox.Text.Trim();
        settings.CaptureMicrophoneDevice = this.CaptureMicrophoneBox.Text == null
            ? string.Empty : this.CaptureMicrophoneBox.Text.Trim();
        settings.OcrTesseractPath = this.OcrTesseractPathBox.Text == null
            ? string.Empty : this.OcrTesseractPathBox.Text.Trim();
        settings.OcrLanguage = this.OcrLanguageBox.Text == null
            ? string.Empty : this.OcrLanguageBox.Text.Trim();
        int[] delays = new int[] { 0, 3, 5, 10 };
        int delayIndex = this.CaptureDelayBox.SelectedIndex;
        if (delayIndex < 0 || delayIndex >= delays.Length)
        {
            delayIndex = 0;
        }
        settings.CaptureDelaySeconds = delays[delayIndex];
        int[] frames = new int[] { 24, 30, 60 };
        int frameIndex = this.CaptureFpsBox.SelectedIndex;
        if (frameIndex < 0 || frameIndex >= frames.Length)
        {
            frameIndex = 1;
        }
        settings.CaptureRecordFps = frames[frameIndex];
        int[] qualities = new int[] { 18, 23, 28 };
        int qualityIndex = this.CaptureQualityBox.SelectedIndex;
        if (qualityIndex < 0 || qualityIndex >= qualities.Length)
        {
            qualityIndex = 1;
        }
        settings.CaptureRecordQuality = qualities[qualityIndex];
        UiLogger.Log("配置更改", "截图录屏设置：保存目录=" + (string.IsNullOrEmpty(settings.CaptureSaveDirectory) ? "默认图片目录" : settings.CaptureSaveDirectory)
            + " FFmpeg路径=" + (string.IsNullOrEmpty(settings.FfmpegPath) ? "自动查找" : settings.FfmpegPath)
            + " 延迟秒数=" + settings.CaptureDelaySeconds
            + " 帧率=" + settings.CaptureRecordFps
            + " 画质参数=" + settings.CaptureRecordQuality
            + " 麦克风设备=" + (string.IsNullOrEmpty(settings.CaptureMicrophoneDevice) ? "未启用" : settings.CaptureMicrophoneDevice));
    }

    /// <summary>查找整数值在候选数组中的索引；不存在时返回默认索引。</summary>
    private static int FindIntegerIndex(int[] options, int value, int fallbackIndex)
    {
        for (int index = 0; index < options.Length; index++)
        {
            if (options[index] == value)
            {
                return index;
            }
        }
        return fallbackIndex;
    }

    /// <summary>浏览选择截图录屏产物保存目录。</summary>
    private async void OnBrowseCaptureFolderClick(object? sender, RoutedEventArgs e)
    {
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (owner == null)
        {
            UiLogger.Log("设置窗口", "浏览截图保存目录：顶层窗口未就绪，结果=未打开选择框");
            return;
        }
        FolderPickerOpenOptions options = new FolderPickerOpenOptions();
        options.Title = "选择截图与录屏产物保存目录";
        options.AllowMultiple = false;
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count == 0)
        {
            UiLogger.Log("设置窗口", "浏览截图保存目录：用户取消选择");
            return;
        }
        string? localPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            UiLogger.Log("设置窗口", "浏览截图保存目录：选择结果无法转换为本机路径");
            return;
        }
        this.CaptureSaveDirectoryBox.Text = localPath;
        UiLogger.Log("设置窗口", "浏览截图保存目录：已选择目录=" + localPath);
    }

    /// <summary>恢复默认产物目录（留空即使用「我的图片\TabTaskExplorer\截图录屏」）。</summary>
    private void OnResetCaptureFolderClick(object? sender, RoutedEventArgs e)
    {
        this.CaptureSaveDirectoryBox.Text = string.Empty;
        UiLogger.Log("设置窗口", "恢复默认截图保存目录：已清空自定义目录，将使用默认图片目录");
    }

    /// <summary>浏览选择 FFmpeg 可执行文件。</summary>
    private async void OnBrowseFfmpegClick(object? sender, RoutedEventArgs e)
    {
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (owner == null)
        {
            UiLogger.Log("设置窗口", "浏览 FFmpeg：顶层窗口未就绪，结果=未打开选择框");
            return;
        }
        FilePickerOpenOptions options = new FilePickerOpenOptions();
        options.Title = "选择 FFmpeg 可执行文件";
        options.AllowMultiple = false;
        List<FilePickerFileType> fileTypes = new List<FilePickerFileType>();
        FilePickerFileType executableType = new FilePickerFileType("FFmpeg 可执行文件");
        executableType.Patterns = new List<string> { "ffmpeg.exe", "ffmpeg" };
        fileTypes.Add(executableType);
        options.FileTypeFilter = fileTypes;
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
        {
            UiLogger.Log("设置窗口", "浏览 FFmpeg：用户取消选择");
            return;
        }
        string? localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            UiLogger.Log("设置窗口", "浏览 FFmpeg：选择结果无法转换为本机路径");
            return;
        }
        this.FfmpegPathBox.Text = localPath;
        UiLogger.Log("设置窗口", "浏览 FFmpeg：已选择文件=" + localPath);
    }

    /// <summary>清空自定义 FFmpeg 路径，恢复自动查找。</summary>
    private void OnClearFfmpegPathClick(object? sender, RoutedEventArgs e)
    {
        this.FfmpegPathBox.Text = string.Empty;
        UiLogger.Log("设置窗口", "FFmpeg 路径已清空：将按 PATH 与程序目录自动查找");
    }

    /// <summary>浏览选择 Tesseract 可执行文件（Linux 与 macOS 必需，Windows 可选）。</summary>
    private async void OnBrowseTesseractClick(object? sender, RoutedEventArgs e)
    {
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (owner == null)
        {
            UiLogger.Log("设置窗口", "浏览 Tesseract：顶层窗口未就绪，结果=未打开选择框");
            return;
        }
        FilePickerOpenOptions options = new FilePickerOpenOptions();
        options.Title = "选择 Tesseract 可执行文件";
        options.AllowMultiple = false;
        List<FilePickerFileType> fileTypes = new List<FilePickerFileType>();
        FilePickerFileType executableType = new FilePickerFileType("Tesseract 可执行文件");
        executableType.Patterns = new List<string> { "tesseract.exe", "tesseract" };
        fileTypes.Add(executableType);
        options.FileTypeFilter = fileTypes;
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
        {
            UiLogger.Log("设置窗口", "浏览 Tesseract：用户取消选择");
            return;
        }
        string? localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            UiLogger.Log("设置窗口", "浏览 Tesseract：选择结果无法转换为本机路径");
            return;
        }
        this.OcrTesseractPathBox.Text = localPath;
        UiLogger.Log("设置窗口", "浏览 Tesseract：已选择文件=" + localPath);
    }

    /// <summary>清空 Tesseract 路径，恢复自动查找。</summary>
    private void OnClearTesseractPathClick(object? sender, RoutedEventArgs e)
    {
        this.OcrTesseractPathBox.Text = string.Empty;
        UiLogger.Log("设置窗口", "Tesseract 路径已清空：将按常见安装目录与系统路径自动查找");
    }

    /// <summary>提交打开方式设置：扩展名统一规整为小写带点形式，重复类型只保留一条规则。</summary>
    private void CommitOpenRules()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        settings.FileOpenDefaultPriority = this.DefaultPriorityBox.SelectedIndex == 1 ? FileOpenPriority.System : FileOpenPriority.Builtin;

        List<FileOpenRule> committed = new List<FileOpenRule>();
        foreach (FileOpenRule rule in this.openRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Extension))
            {
                continue;
            }
            string extension = FileOpenPolicy.NormalizeExtension(rule.Extension);
            bool duplicated = false;
            foreach (FileOpenRule existing in committed)
            {
                if (existing.Extension == extension)
                {
                    duplicated = true;
                    break;
                }
            }
            if (duplicated)
            {
                continue;
            }
            FileOpenRule kept = new FileOpenRule();
            kept.Extension = extension;
            kept.Priority = rule.Priority;
            committed.Add(kept);
        }
        settings.FileOpenRules = committed;

        this.openRules.Clear();
        foreach (FileOpenRule rule in committed)
        {
            this.openRules.Add(rule);
        }
    }

    /// <summary>提交编辑器设置：字号与 Tab 宽度只接受合法范围内的数字，非法输入还原为原值。</summary>
    private void CommitEditorSettings()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        double fontSize;
        if (double.TryParse(this.EditorFontSizeBox.Text, out fontSize) && fontSize >= 8 && fontSize <= 48)
        {
            settings.EditorFontSize = fontSize;
        }
        this.EditorFontSizeBox.Text = settings.EditorFontSize.ToString();

        int tabWidth;
        if (int.TryParse(this.EditorTabWidthBox.Text, out tabWidth) && tabWidth >= 1 && tabWidth <= 16)
        {
            settings.EditorTabWidth = tabWidth;
        }
        this.EditorTabWidthBox.Text = settings.EditorTabWidth.ToString();

        settings.DefaultOpenMode = this.DefaultOpenModeBox.SelectedIndex == 1 ? "Ide" : "Explorer";
    }

    /// <summary>把设置里的网页端端口与权限方案复制到窗口内的编辑副本，并选中第一项。</summary>
    private void InitializeWebSettings(AppSettings settings)
    {
        this.webPorts.Clear();
        this.webProfiles.Clear();
        if (settings.WebAccessProfiles != null)
        {
            foreach (WebAccessProfile profile in settings.WebAccessProfiles)
            {
                if (profile != null)
                {
                    this.webProfiles.Add(CopyProfile(profile));
                }
            }
        }
        if (this.webProfiles.Count == 0)
        {
            this.webProfiles.Add(WebAccessProfile.CreateDefault());
        }
        if (settings.WebPorts != null)
        {
            foreach (WebPortSetting port in settings.WebPorts)
            {
                if (port != null)
                {
                    this.webPorts.Add(CopyPort(port));
                }
            }
        }
        if (this.webPorts.Count == 0)
        {
            WebPortSetting port = new WebPortSetting();
            port.ProfileName = this.webProfiles[0].Name;
            this.webPorts.Add(port);
        }
        foreach (WebPortSetting port in this.webPorts)
        {
            if (this.FindProfileByName(port.ProfileName) == null)
            {
                port.ProfileName = this.webProfiles[0].Name;
            }
        }
        this.selectedPortIndex = 0;
        this.selectedProfileIndex = 0;
        this.RefreshWebLists();
        this.LoadSelectedWebPort();
        this.LoadSelectedWebProfile();
        UiLogger.Log("设置窗口", "网页端配置初始化：端口数量=" + this.webPorts.Count + " 权限方案数量=" + this.webProfiles.Count);
    }

    /// <summary>刷新两个列表的显示文字与权限方案下拉项，期间屏蔽选中事件避免误提交。</summary>
    private void RefreshWebLists()
    {
        this.suppressWebSelection = true;
        this.webPortLabels.Clear();
        foreach (WebPortSetting port in this.webPorts)
        {
            this.webPortLabels.Add("端口 " + port.Port
                + (port.Enabled ? "（已启用）" : "（未启用）")
                + " · " + port.ProfileName
                + (port.BindAddress == WebBindScope.Any ? " · 局域网" : " · 仅本机"));
        }
        this.WebPortList.SelectedIndex = this.selectedPortIndex;
        this.webProfileLabels.Clear();
        foreach (WebAccessProfile profile in this.webProfiles)
        {
            this.webProfileLabels.Add(profile.Name
                + (profile.AllowModify ? " · 可修改文件" : " · 只读")
                + (profile.RequirePassword ? " · 需密码" : string.Empty));
        }
        this.WebProfileList.SelectedIndex = this.selectedProfileIndex;
        this.webProfileChoices.Clear();
        foreach (WebAccessProfile profile in this.webProfiles)
        {
            this.webProfileChoices.Add(profile.Name);
        }
        this.suppressWebSelection = false;
        this.SyncProfileChoiceToSelectedPort();
    }

    /// <summary>把权限方案下拉框对齐到当前选中端口的引用方案。</summary>
    private void SyncProfileChoiceToSelectedPort()
    {
        this.loadingWebFields = true;
        if (this.selectedPortIndex >= 0 && this.selectedPortIndex < this.webPorts.Count)
        {
            this.WebProfileBox.SelectedItem = this.webPorts[this.selectedPortIndex].ProfileName;
        }
        else
        {
            this.WebProfileBox.SelectedItem = null;
        }
        this.loadingWebFields = false;
    }

    /// <summary>把当前选中端口的配置显示到编辑区。</summary>
    private void LoadSelectedWebPort()
    {
        this.loadingWebFields = true;
        if (this.selectedPortIndex >= 0 && this.selectedPortIndex < this.webPorts.Count)
        {
            WebPortSetting port = this.webPorts[this.selectedPortIndex];
            this.WebPortBox.Text = port.Port.ToString();
            this.WebPortEnabledBox.IsChecked = port.Enabled;
            this.WebBindScopeBox.SelectedIndex = port.BindAddress == WebBindScope.Any ? 1 : 0;
            this.WebProfileBox.SelectedItem = port.ProfileName;
        }
        else
        {
            this.WebPortBox.Text = string.Empty;
            this.WebPortEnabledBox.IsChecked = false;
            this.WebBindScopeBox.SelectedIndex = 0;
            this.WebProfileBox.SelectedItem = null;
        }
        this.loadingWebFields = false;
    }

    /// <summary>把当前选中权限方案的配置显示到编辑区；密码框始终留空，避免回显明文。</summary>
    private void LoadSelectedWebProfile()
    {
        this.loadingWebFields = true;
        if (this.selectedProfileIndex >= 0 && this.selectedProfileIndex < this.webProfiles.Count)
        {
            WebAccessProfile profile = this.webProfiles[this.selectedProfileIndex];
            this.WebProfileNameBox.Text = profile.Name;
            this.WebAllowModifyBox.IsChecked = profile.AllowModify;
            this.WebAllowCreateDeleteBox.IsChecked = profile.AllowCreateDelete;
            this.WebRequirePasswordBox.IsChecked = profile.RequirePassword;
            this.WebAccountBox.Text = profile.Account;
            this.WebPasswordBox.Text = string.Empty;
            this.WebPasswordBox.PlaceholderText = profile.PasswordHash.Length > 0 ? "已设置，留空表示不修改" : "尚未设置密码";
            this.WebAllowedAddressBox.Text = JoinLines(profile.AllowedAddresses);
            this.WebAllowedPathBox.Text = JoinLines(profile.AllowedPaths);
        }
        else
        {
            this.WebProfileNameBox.Text = string.Empty;
            this.WebAllowModifyBox.IsChecked = false;
            this.WebAllowCreateDeleteBox.IsChecked = false;
            this.WebRequirePasswordBox.IsChecked = false;
            this.WebAccountBox.Text = string.Empty;
            this.WebPasswordBox.Text = string.Empty;
            this.WebPasswordBox.PlaceholderText = string.Empty;
            this.WebAllowedAddressBox.Text = string.Empty;
            this.WebAllowedPathBox.Text = string.Empty;
        }
        this.loadingWebFields = false;
    }

    private void OnWebPortSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (this.suppressWebSelection)
        {
            return;
        }
        this.CommitSelectedWebPort();
        this.selectedPortIndex = this.WebPortList.SelectedIndex;
        this.LoadSelectedWebPort();
        UiLogger.Log("设置窗口", "选中网页端端口：列表序号=" + this.selectedPortIndex);
    }

    private void OnWebProfileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (this.suppressWebSelection)
        {
            return;
        }
        this.CommitSelectedWebProfile();
        this.selectedProfileIndex = this.WebProfileList.SelectedIndex;
        this.LoadSelectedWebProfile();
        UiLogger.Log("设置窗口", "选中网页端权限方案：列表序号=" + this.selectedProfileIndex);
    }

    /// <summary>端口切换引用方案时立即写回当前端口，并刷新端口列表的显示文字。</summary>
    private void OnWebProfileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.loadingWebFields)
        {
            return;
        }
        string? profileName = this.WebProfileBox.SelectedItem as string;
        if (string.IsNullOrEmpty(profileName))
        {
            UiLogger.Log("设置窗口", "更改端口引用的权限方案被拒绝：未选择权限方案 结果=失败");
            return;
        }
        if (this.selectedPortIndex < 0 || this.selectedPortIndex >= this.webPorts.Count)
        {
            UiLogger.Log("设置窗口", "更改端口引用的权限方案被拒绝：未选中任何端口 结果=失败");
            return;
        }
        int portNumber = this.webPorts[this.selectedPortIndex].Port;
        this.webPorts[this.selectedPortIndex].ProfileName = profileName;
        this.RefreshWebLists();
        UiLogger.Log("设置窗口", "更改端口引用的权限方案：端口号=" + portNumber
            + " 权限方案=" + profileName
            + " 结果=成功");
    }

    /// <summary>新增端口：自动挑选一个未被占用的端口号，默认引用第一个权限方案。</summary>
    private void OnAddWebPortClick(object? sender, RoutedEventArgs e)
    {
        this.CommitSelectedWebPort();
        WebPortSetting port = new WebPortSetting();
        port.Port = this.SuggestNextPort();
        port.ProfileName = this.webProfiles[0].Name;
        this.webPorts.Add(port);
        this.selectedPortIndex = this.webPorts.Count - 1;
        this.RefreshWebLists();
        this.LoadSelectedWebPort();
        UiLogger.Log("设置窗口", "新增网页端端口：端口号=" + port.Port + " 权限方案=" + port.ProfileName);
    }

    private void OnRemoveWebPortClick(object? sender, RoutedEventArgs e)
    {
        if (this.selectedPortIndex < 0 || this.selectedPortIndex >= this.webPorts.Count)
        {
            UiLogger.Log("设置窗口", "点击删除网页端端口被拒绝：未选中任何端口 结果=失败");
            return;
        }
        int removedPort = this.webPorts[this.selectedPortIndex].Port;
        this.webPorts.RemoveAt(this.selectedPortIndex);
        if (this.selectedPortIndex >= this.webPorts.Count)
        {
            this.selectedPortIndex = this.webPorts.Count - 1;
        }
        this.RefreshWebLists();
        this.LoadSelectedWebPort();
        UiLogger.Log("设置窗口", "删除网页端端口：端口号=" + removedPort + " 剩余端口数量=" + this.webPorts.Count);
    }

    /// <summary>新增权限方案：默认只读，名称自动避开重名。</summary>
    private void OnAddWebProfileClick(object? sender, RoutedEventArgs e)
    {
        this.CommitSelectedWebProfile();
        WebAccessProfile profile = WebAccessProfile.CreateDefault();
        profile.Name = this.SuggestProfileName();
        this.webProfiles.Add(profile);
        this.selectedProfileIndex = this.webProfiles.Count - 1;
        this.RefreshWebLists();
        this.LoadSelectedWebProfile();
        UiLogger.Log("设置窗口", "新增网页端权限方案：方案名称=" + profile.Name);
    }

    /// <summary>删除权限方案：至少保留一个；被删除方案的端口自动改引用第一个方案。</summary>
    private void OnRemoveWebProfileClick(object? sender, RoutedEventArgs e)
    {
        if (this.selectedProfileIndex < 0 || this.selectedProfileIndex >= this.webProfiles.Count)
        {
            UiLogger.Log("设置窗口", "点击删除网页端权限方案被拒绝：未选中任何方案 结果=失败");
            return;
        }
        if (this.webProfiles.Count <= 1)
        {
            UiLogger.Log("设置窗口", "删除网页端权限方案被拒绝：至少需要保留一个权限方案");
            return;
        }
        this.CommitSelectedWebProfile();
        string removedName = this.webProfiles[this.selectedProfileIndex].Name;
        this.webProfiles.RemoveAt(this.selectedProfileIndex);
        if (this.selectedProfileIndex >= this.webProfiles.Count)
        {
            this.selectedProfileIndex = this.webProfiles.Count - 1;
        }
        foreach (WebPortSetting port in this.webPorts)
        {
            if (string.Equals(port.ProfileName, removedName, StringComparison.Ordinal))
            {
                port.ProfileName = this.webProfiles[0].Name;
            }
        }
        this.RefreshWebLists();
        this.LoadSelectedWebProfile();
        this.LoadSelectedWebPort();
        UiLogger.Log("设置窗口", "删除网页端权限方案：方案名称=" + removedName + " 剩余方案数量=" + this.webProfiles.Count);
    }

    /// <summary>把编辑区的端口配置写回当前选中端口。</summary>
    private void CommitSelectedWebPort()
    {
        if (this.selectedPortIndex < 0 || this.selectedPortIndex >= this.webPorts.Count)
        {
            return;
        }
        WebPortSetting port = this.webPorts[this.selectedPortIndex];
        int portNumber;
        if (int.TryParse(this.WebPortBox.Text, out portNumber) && portNumber >= 1024 && portNumber <= 65535)
        {
            port.Port = portNumber;
        }
        else
        {
            this.WebPortBox.Text = port.Port.ToString();
        }
        port.Enabled = this.WebPortEnabledBox.IsChecked == true;
        port.BindAddress = this.WebBindScopeBox.SelectedIndex == 1 ? WebBindScope.Any : WebBindScope.Loopback;
        string? profileName = this.WebProfileBox.SelectedItem as string;
        if (!string.IsNullOrEmpty(profileName))
        {
            port.ProfileName = profileName;
        }
    }

    /// <summary>把编辑区的权限方案写回当前选中方案；改名时同步更新引用该方案的端口。</summary>
    private void CommitSelectedWebProfile()
    {
        if (this.selectedProfileIndex < 0 || this.selectedProfileIndex >= this.webProfiles.Count)
        {
            return;
        }
        WebAccessProfile profile = this.webProfiles[this.selectedProfileIndex];
        string oldName = profile.Name;
        string newName = this.WebProfileNameBox.Text == null ? string.Empty : this.WebProfileNameBox.Text.Trim();
        if (newName.Length == 0)
        {
            newName = oldName;
        }
        profile.Name = newName;
        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            foreach (WebPortSetting port in this.webPorts)
            {
                if (string.Equals(port.ProfileName, oldName, StringComparison.Ordinal))
                {
                    port.ProfileName = newName;
                }
            }
        }
        profile.AllowModify = this.WebAllowModifyBox.IsChecked == true;
        profile.AllowCreateDelete = this.WebAllowCreateDeleteBox.IsChecked == true;
        string account = this.WebAccountBox.Text == null ? string.Empty : this.WebAccountBox.Text.Trim();
        profile.Account = account.Length == 0 ? "admin" : account;

        bool requirePassword = this.WebRequirePasswordBox.IsChecked == true;
        if (!string.IsNullOrEmpty(this.WebPasswordBox.Text))
        {
            profile.SetPassword(this.WebPasswordBox.Text);
            this.WebPasswordBox.Text = string.Empty;
            this.WebPasswordBox.PlaceholderText = "已设置，留空表示不修改";
        }
        if (requirePassword && string.IsNullOrEmpty(profile.PasswordHash))
        {
            requirePassword = false;
            UiLogger.Log("设置窗口", "网页端权限方案未设置密码：方案名称=" + profile.Name + "，已自动关闭密码验证");
        }
        profile.RequirePassword = requirePassword;
        profile.AllowedAddresses = SplitLines(this.WebAllowedAddressBox.Text);
        profile.AllowedPaths = SplitLines(this.WebAllowedPathBox.Text);
    }

    /// <summary>提交网页端配置：写回共享设置并同步旧的单端口字段，供启动日志与旧版本读取路径使用。</summary>
    private void CommitWebSettings()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        this.CommitSelectedWebPort();
        this.CommitSelectedWebProfile();

        for (int first = 0; first < this.webPorts.Count; first++)
        {
            for (int second = first + 1; second < this.webPorts.Count; second++)
            {
                if (this.webPorts[first].Enabled && this.webPorts[second].Enabled
                    && this.webPorts[first].Port == this.webPorts[second].Port)
                {
                    UiLogger.Log("设置窗口", "网页端端口重复：端口号=" + this.webPorts[first].Port
                        + "，重复端口只有先绑定成功的那个生效，失败原因会写入运行日志");
                }
            }
        }

        settings.WebPorts = new List<WebPortSetting>(this.webPorts);
        settings.WebAccessProfiles = new List<WebAccessProfile>(this.webProfiles);

        settings.EnableWebService = false;
        foreach (WebPortSetting port in this.webPorts)
        {
            if (port.Enabled)
            {
                settings.EnableWebService = true;
                settings.WebServicePort = port.Port;
                break;
            }
        }
        this.RefreshWebLists();
    }

    /// <summary>挑选一个未被占用的端口号，从默认端口开始逐个尝试。</summary>
    private int SuggestNextPort()
    {
        int candidate = WebServiceHost.DefaultPort;
        while (this.IsPortUsed(candidate))
        {
            candidate = candidate + 1;
            if (candidate > 65535)
            {
                candidate = 1024;
            }
        }
        return candidate;
    }

    private bool IsPortUsed(int portNumber)
    {
        foreach (WebPortSetting port in this.webPorts)
        {
            if (port.Port == portNumber)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>生成不重名的权限方案名称。</summary>
    private string SuggestProfileName()
    {
        int index = this.webProfiles.Count + 1;
        while (true)
        {
            string candidate = "方案 " + index;
            if (this.FindProfileByName(candidate) == null)
            {
                return candidate;
            }
            index = index + 1;
        }
    }

    private WebAccessProfile? FindProfileByName(string profileName)
    {
        if (string.IsNullOrEmpty(profileName))
        {
            return null;
        }
        foreach (WebAccessProfile profile in this.webProfiles)
        {
            if (string.Equals(profile.Name, profileName, StringComparison.Ordinal))
            {
                return profile;
            }
        }
        return null;
    }

    private static WebPortSetting CopyPort(WebPortSetting source)
    {
        WebPortSetting copy = new WebPortSetting();
        copy.Port = source.Port;
        copy.Enabled = source.Enabled;
        copy.ProfileName = source.ProfileName;
        copy.BindAddress = source.BindAddress;
        return copy;
    }

    private static WebAccessProfile CopyProfile(WebAccessProfile source)
    {
        WebAccessProfile copy = new WebAccessProfile();
        copy.Name = source.Name;
        copy.AllowModify = source.AllowModify;
        copy.AllowCreateDelete = source.AllowCreateDelete;
        copy.RequirePassword = source.RequirePassword;
        copy.Account = source.Account;
        copy.PasswordHash = source.PasswordHash;
        copy.PasswordSalt = source.PasswordSalt;
        copy.AllowedAddresses = new List<string>();
        if (source.AllowedAddresses != null)
        {
            foreach (string address in source.AllowedAddresses)
            {
                if (address != null)
                {
                    copy.AllowedAddresses.Add(address);
                }
            }
        }
        copy.AllowedPaths = new List<string>();
        if (source.AllowedPaths != null)
        {
            foreach (string path in source.AllowedPaths)
            {
                if (path != null)
                {
                    copy.AllowedPaths.Add(path);
                }
            }
        }
        return copy;
    }

    /// <summary>多行文本框拆成清单：每行一条，忽略空行与首尾空白。</summary>
    private static List<string> SplitLines(string? text)
    {
        List<string> values = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return values;
        }
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                values.Add(trimmed);
            }
        }
        return values;
    }

    private static string JoinLines(List<string> values)
    {
        if (values == null || values.Count == 0)
        {
            return string.Empty;
        }
        return string.Join(Environment.NewLine, values);
    }

    /// <summary>按当前分组、状态与搜索词重新填充右键菜单列表，返回填充条数。</summary>
    private int LoadSystemMenuList()
    {
        if (this.RightMenuList == null || this.RightMenuEmptyText == null || this.RightMenuIntroText == null)
        {
            UiLogger.Log("设置窗口", "加载系统右键菜单项：分页控件尚未创建 结果=失败，未加载列表");
            return 0;
        }
        this.suppressSystemMenuToggle = true;
        try
        {
            this.systemMenuRows.Clear();
            if (this.systemContextMenus == null || !this.systemContextMenus.IsSupported)
            {
                this.RightMenuIntroText.Text = "当前系统不支持系统右键菜单管理";
                this.RightMenuEmptyText.IsVisible = false;
                this.systemMenuLoaded = true;
                UiLogger.Log("设置窗口", "加载系统右键菜单项：分组=" + this.GetSelectedGroupText()
                    + " 状态=" + this.GetSelectedStatusText()
                    + " 搜索词=" + this.GetSearchText()
                    + " 条目总数=0 结果条数=0 结果=当前系统不支持系统右键菜单管理，列表已清空");
                return 0;
            }
            this.RightMenuIntroText.Text = this.rightMenuIntroText;
            SystemMenuGroup? selectedGroup = this.GetSelectedGroup();
            int statusIndex = this.RightMenuStatusBox != null ? this.RightMenuStatusBox.SelectedIndex : 0;
            string searchText = this.GetSearchText();
            IReadOnlyList<SystemContextMenuEntry> allEntries = this.systemContextMenus.Enumerate();
            foreach (SystemContextMenuEntry entry in allEntries)
            {
                // 分组、状态、名称三重筛选：Win11 新版菜单组只读展示，不受状态筛选影响
                if ((!selectedGroup.HasValue || entry.Group == selectedGroup.Value)
                    && (entry.Group == SystemMenuGroup.ModernMenu || statusIndex != 1 || entry.IsEnabled)
                    && (entry.Group == SystemMenuGroup.ModernMenu || statusIndex != 2 || !entry.IsEnabled)
                    && (searchText.Length <= 0 || entry.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
                {
                    this.systemMenuRows.Add(CreateSystemMenuRow(entry));
                }
            }
            this.RightMenuEmptyText.IsVisible = this.systemMenuRows.Count == 0;
            this.systemMenuLoaded = true;
            UiLogger.Log("设置窗口", "加载系统右键菜单项：分组=" + this.GetSelectedGroupText()
                + " 状态=" + this.GetSelectedStatusText()
                + " 搜索词=" + searchText
                + " 条目总数=" + allEntries.Count
                + " 结果条数=" + this.systemMenuRows.Count
                + " 结果=成功，已按筛选条件重排列表");
            return this.systemMenuRows.Count;
        }
        finally
        {
            this.suppressSystemMenuToggle = false;
        }
    }

    /// <summary>把一条系统右键菜单条目转换成列表行数据：名称、注册表路径、状态角标与开关状态。</summary>
    private static SystemMenuRow CreateSystemMenuRow(SystemContextMenuEntry entry)
    {
        SystemMenuRow row = new SystemMenuRow();
        row.Name = entry.DisplayName;
        row.PathText = entry.RegistryPath;
        if (entry.IsReadOnly)
        {
            row.BadgeText = "只读";
        }
        else if (entry.RequiresAdministrator)
        {
            row.BadgeText = "需管理员";
        }
        else if (entry.IsSystemItem)
        {
            row.BadgeText = "系统项";
        }
        else
        {
            row.BadgeText = entry.IsEnabled ? "已启用" : "已禁用";
        }
        row.CanToggle = !entry.IsReadOnly;
        row.ToggleOn = entry.IsEnabled;
        row.Entry = entry;
        return row;
    }

    /// <summary>取分组下拉框当前选中的分组；选中「全部」或控件未创建时返回空，表示不限分组。</summary>
    private SystemMenuGroup? GetSelectedGroup()
    {
        if (this.RightMenuGroupBox == null)
        {
            return null;
        }
        return this.RightMenuGroupBox.SelectedIndex switch
        {
            1 => (SystemMenuGroup?)SystemMenuGroup.FileContext,
            2 => (SystemMenuGroup?)SystemMenuGroup.DesktopContext,
            3 => (SystemMenuGroup?)SystemMenuGroup.ShellNew,
            4 => (SystemMenuGroup?)SystemMenuGroup.ModernMenu,
            _ => null,
        };
    }

    /// <summary>取分组下拉框当前选中项的文字，用于日志；取不到时按「全部」记录。</summary>
    private string GetSelectedGroupText()
    {
        if (this.RightMenuGroupBox != null && this.RightMenuGroupBox.SelectedItem is ComboBoxItem { Content: string content })
        {
            return content;
        }
        return "全部";
    }

    /// <summary>取状态下拉框当前选中项的文字，用于日志；取不到时按「全部」记录。</summary>
    private string GetSelectedStatusText()
    {
        if (this.RightMenuStatusBox != null && this.RightMenuStatusBox.SelectedItem is ComboBoxItem { Content: string content })
        {
            return content;
        }
        return "全部";
    }

    /// <summary>取搜索框当前关键字；控件未创建或未输入时返回空串，表示不按名称过滤。</summary>
    private string GetSearchText()
    {
        if (this.RightMenuSearchBox == null)
        {
            return string.Empty;
        }
        return this.RightMenuSearchBox.Text ?? string.Empty;
    }

    /// <summary>判断右键菜单分页是否已创建并完成首次加载：未就绪时记录日志并拒绝重新筛选。</summary>
    private bool CanReselectSystemMenus(string sourceName)
    {
        if (this.RightMenuList == null || this.RightMenuGroupBox == null || this.RightMenuStatusBox == null
            || !this.systemMenuLoaded)
        {
            UiLogger.Log("设置窗口", sourceName + "：右键菜单分页尚未创建或尚未加载 结果=失败，未重新筛选列表");
            return false;
        }
        return true;
    }

    /// <summary>点击刷新：丢弃缓存重新扫描系统右键菜单项，并按当前筛选条件重排列表。</summary>
    private void OnRightMenuRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (this.RightMenuList == null || this.RightMenuGroupBox == null)
        {
            UiLogger.Log("设置窗口", "点击刷新系统右键菜单：分页控件尚未创建 结果=失败，未刷新");
            return;
        }
        int entryTotal = 0;
        if (this.systemContextMenus != null && this.systemContextMenus.IsSupported)
        {
            this.systemContextMenus.Refresh();
            entryTotal = this.systemContextMenus.Enumerate().Count;
        }
        int loadedCount = this.LoadSystemMenuList();
        UiLogger.Log("设置窗口", "点击刷新系统右键菜单：分组=" + this.GetSelectedGroupText()
            + " 状态=" + this.GetSelectedStatusText()
            + " 搜索词=" + this.GetSearchText()
            + " 条目总数=" + entryTotal
            + " 结果条数=" + loadedCount
            + " 结果=成功，已重新扫描并重排列表");
    }

    /// <summary>分组下拉框改变：按新分组重新筛选右键菜单列表。</summary>
    private void OnRightMenuGroupChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.CanReselectSystemMenus("分组筛选"))
        {
            int loadedCount = this.LoadSystemMenuList();
            UiLogger.Log("设置窗口", "右键菜单分组筛选：分组=" + this.GetSelectedGroupText()
                + " 状态=" + this.GetSelectedStatusText()
                + " 搜索词=" + this.GetSearchText()
                + " 结果条数=" + loadedCount
                + " 结果=成功，已按新分组重排列表");
        }
    }

    /// <summary>状态下拉框改变：按新状态重新筛选右键菜单列表。</summary>
    private void OnRightMenuStatusChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.CanReselectSystemMenus("状态筛选"))
        {
            int loadedCount = this.LoadSystemMenuList();
            UiLogger.Log("设置窗口", "右键菜单状态筛选：分组=" + this.GetSelectedGroupText()
                + " 状态=" + this.GetSelectedStatusText()
                + " 搜索词=" + this.GetSearchText()
                + " 结果条数=" + loadedCount
                + " 结果=成功，已按新状态重排列表");
        }
    }

    /// <summary>搜索关键字改变：按新关键字重新筛选右键菜单列表。</summary>
    private void OnRightMenuSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.CanReselectSystemMenus("按名称搜索"))
        {
            int loadedCount = this.LoadSystemMenuList();
            UiLogger.Log("设置窗口", "右键菜单按名称搜索：分组=" + this.GetSelectedGroupText()
                + " 状态=" + this.GetSelectedStatusText()
                + " 搜索词=" + this.GetSearchText()
                + " 结果条数=" + loadedCount
                + " 结果=成功，已按新搜索词重排列表");
        }
    }

    /// <summary>切换某条系统右键菜单项的启用状态：写入注册表，失败或只读时把开关回退到实际状态。</summary>
    private void OnRightMenuToggle(object? sender, RoutedEventArgs e)
    {
        if (this.suppressSystemMenuToggle)
        {
            return;
        }
        if (sender is ToggleSwitch { DataContext: SystemMenuRow row, IsChecked: var isChecked } toggleSwitch)
        {
            bool targetEnabled = isChecked == true;
            if (targetEnabled == row.Entry.IsEnabled)
            {
                UiLogger.Log("设置窗口", "切换系统右键菜单项开关：菜单项名称=" + row.Name
                    + " 注册表完整路径=" + row.PathText
                    + " 目标状态=" + (targetEnabled ? "启用" : "禁用")
                    + " 结果=开关回填与实际状态一致，未写入注册表");
            }
            else if (row.Entry.IsReadOnly)
            {
                this.suppressSystemMenuToggle = true;
                toggleSwitch.IsChecked = row.Entry.IsEnabled;
                this.suppressSystemMenuToggle = false;
                UiLogger.Log("设置窗口", "切换系统右键菜单项开关：菜单项名称=" + row.Name
                    + " 注册表完整路径=" + row.PathText
                    + " 目标状态=" + (targetEnabled ? "启用" : "禁用")
                    + " 结果=只读菜单项不可启停，已撤回开关");
            }
            else if (this.systemContextMenus == null)
            {
                this.suppressSystemMenuToggle = true;
                toggleSwitch.IsChecked = row.Entry.IsEnabled;
                this.suppressSystemMenuToggle = false;
                UiLogger.Log("设置窗口", "切换系统右键菜单项开关：菜单项名称=" + row.Name
                    + " 注册表完整路径=" + row.PathText
                    + " 目标状态=" + (targetEnabled ? "启用" : "禁用")
                    + " 结果=失败，系统右键菜单服务为空，已回退开关显示");
            }
            else if (this.systemContextMenus.SetEnabled(row.Entry, targetEnabled))
            {
                UiLogger.Log("设置窗口", "切换系统右键菜单项开关：菜单项名称=" + row.Name
                    + " 注册表完整路径=" + row.PathText
                    + " 目标状态=" + (targetEnabled ? "启用" : "禁用")
                    + " 结果=成功，已写入注册表并重新加载列表");
                this.LoadSystemMenuList();
            }
            else
            {
                this.suppressSystemMenuToggle = true;
                toggleSwitch.IsChecked = row.Entry.IsEnabled;
                this.suppressSystemMenuToggle = false;
                UiLogger.Log("设置窗口", "切换系统右键菜单项开关：菜单项名称=" + row.Name
                    + " 注册表完整路径=" + row.PathText
                    + " 目标状态=" + (targetEnabled ? "启用" : "禁用")
                    + " 结果=失败，注册表写入未成功，已回退开关显示");
            }
        }
        else
        {
            UiLogger.Log("设置窗口", "切换系统右键菜单项开关：开关或菜单行为空 结果=失败，本次启停未执行");
        }
    }

    /// <summary>「启动项管理」分页：请求主窗口打开（或切换到）启动项管理标签页，本窗口内不重复承载列表视图。</summary>
    private void OnOpenStartupManagerClick(object? sender, RoutedEventArgs e)
    {
        if (this.openStartupManager == null)
        {
            this.StartupOpenStatusText.Text = "当前入口未连接主窗口，无法打开启动项管理标签页。";
            this.StartupOpenStatusText.IsVisible = true;
            UiLogger.Log("设置窗口", "点击打开启动项管理标签页：未注入打开回调 结果=失败");
            return;
        }
        this.openStartupManager();
        this.StartupOpenStatusText.Text = "已打开启动项管理标签页（若已存在则直接切换到该标签），关闭本窗口后即可操作。";
        this.StartupOpenStatusText.IsVisible = true;
        UiLogger.Log("设置窗口", "点击打开启动项管理标签页：结果=成功，已请求主窗口复用或新建启动项管理标签");
    }

    /// <summary>提交日志分页的级别设置：写入共享设置对象，点击确定后由主窗口统一保存与应用。</summary>
    private void CommitLogSettings()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        int index = this.LogLevelBox.SelectedIndex;
        if (index < 0 || index > (int)LogLevel.Error)
        {
            index = (int)LogLevel.Info;
        }
        LogLevel level = (LogLevel)index;
        settings.LogLevel = level.ToString();
        UiLogger.Log("配置更改", "日志最低级别：" + UiLogger.DescribeLevel(level));
    }

    /// <summary>打开日志查看器：已存在可见窗口时激活，否则新建一个归属本窗口的查看器。</summary>
    private void OnOpenLogViewerClick(object? sender, RoutedEventArgs e)
    {
        if (this.logViewerWindow != null && this.logViewerWindow.IsVisible)
        {
            this.logViewerWindow.Activate();
            UiLogger.LogInfo(LogCategories.Diagnostics, "打开日志查看器", "结果=激活已打开的查看器窗口");
            return;
        }
        LogViewerWindow window = new LogViewerWindow();
        this.logViewerWindow = window;
        window.Closed += this.OnLogViewerWindowClosed;
        window.Show(this);
        UiLogger.LogInfo(LogCategories.Diagnostics, "打开日志查看器",
            "结果=新建查看器窗口 日志目录=" + UiLogger.LogDirectory);
    }

    /// <summary>日志查看器关闭后清空引用，下次点击可重新创建。</summary>
    private void OnLogViewerWindowClosed(object? sender, EventArgs e)
    {
        this.logViewerWindow = null;
    }

    /// <summary>
    /// 导出诊断包：在后台收集系统信息、脱敏配置、最近日志、崩溃记录、Shell 扩展与
    /// known_hosts 指纹，压缩为 zip 后把保存路径显示在设置窗口内。
    /// </summary>
    private async void OnExportDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (this.diagnosticsExporting)
        {
            UiLogger.LogWarn(LogCategories.Diagnostics, "导出诊断包", "结果=已有导出任务在进行，已忽略本次点击");
            return;
        }
        this.diagnosticsExporting = true;
        this.ExportDiagnosticsButton.IsEnabled = false;
        this.DiagnosticsStatusText.IsVisible = true;
        this.DiagnosticsStatusText.Text = "正在准备诊断包…";
        try
        {
            DiagnosticsBundleOptions options = new DiagnosticsBundleOptions();
            options.Settings = this.DataContext as AppSettings;
            options.SystemContextMenus = this.systemContextMenus;
            options.ScreenDescriptions = this.DescribeScreens();
            Progress<string> progress = new Progress<string>(this.OnDiagnosticsProgress);
            options.Progress = progress;
            DiagnosticsBundleResult result = await DiagnosticsBundle.CreateAsync(options);
            if (!result.Success)
            {
                this.DiagnosticsStatusText.Text = "诊断包生成失败：" + result.ErrorMessage;
                MessageDialog.Show(this, "诊断包生成失败",
                    "无法生成诊断包：" + result.ErrorMessage);
                return;
            }
            this.DiagnosticsStatusText.Text = "诊断包已生成：" + result.ZipPath
                + "（条目数=" + result.Entries.Count + "）";
            MessageDialog.Show(this, "诊断包已生成",
                "保存路径：\n" + result.ZipPath + "\n\n条目数：" + result.Entries.Count
                + "\n\n可直接把这个 zip 文件提供给开发者排查问题。");
        }
        catch (Exception error)
        {
            this.DiagnosticsStatusText.Text = "诊断包生成异常：" + error.Message;
            UiLogger.LogError(LogCategories.Diagnostics, "导出诊断包", "结果=未捕获异常", error);
            MessageDialog.Show(this, "诊断包生成失败", "发生异常：" + error.Message);
        }
        finally
        {
            this.diagnosticsExporting = false;
            this.ExportDiagnosticsButton.IsEnabled = true;
        }
    }

    /// <summary>诊断包进度回调：在界面线程更新状态文字。</summary>
    private void OnDiagnosticsProgress(string message)
    {
        this.DiagnosticsStatusText.IsVisible = true;
        this.DiagnosticsStatusText.Text = message;
    }

    /// <summary>采集显示器描述：序号、主屏标记、边界、工作区与缩放比例。</summary>
    private List<string> DescribeScreens()
    {
        List<string> lines = new List<string>();
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return lines;
        }
        var screens = topLevel.Screens;
        if (screens == null)
        {
            return lines;
        }
        int index = 0;
        foreach (Screen screen in screens.All)
        {
            index = index + 1;
            lines.Add("显示器序号=" + index
                + " 主显示器=" + screen.IsPrimary
                + " 边界=左上角横坐标" + screen.Bounds.X + " 左上角纵坐标" + screen.Bounds.Y
                + " 宽度" + screen.Bounds.Width + " 高度" + screen.Bounds.Height
                + " 工作区=左上角横坐标" + screen.WorkingArea.X + " 左上角纵坐标" + screen.WorkingArea.Y
                + " 宽度" + screen.WorkingArea.Width + " 高度" + screen.WorkingArea.Height
                + " 缩放比例=" + screen.Scaling);
        }
        return lines;
    }

    /// <summary>用系统文件管理器打开日志目录。</summary>
    private void OnOpenLogDirectoryClick(object? sender, RoutedEventArgs e)
    {
        string directory = UiLogger.LogDirectory;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory)
            {
                UseShellExecute = true,
            });
            UiLogger.LogInfo(LogCategories.Diagnostics, "打开日志目录", "目录=" + directory + " 结果=成功");
        }
        catch (Exception error)
        {
            UiLogger.LogError(LogCategories.Diagnostics, "打开日志目录", "目录=" + directory, error);
            MessageDialog.Show(this, "打开失败", "无法打开日志目录：" + error.Message);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("设置窗口", "点击取消：关闭设置窗口");
        this.Close(false);
    }
}
