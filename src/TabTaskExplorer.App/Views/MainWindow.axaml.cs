using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>主窗口：自绘标题栏 + 标签条 + 工具栏 + 侧边栏，支持最小化到托盘常驻。</summary>
public partial class MainWindow : Window
{
    private readonly List<ListBoxItem> tabHooked = new List<ListBoxItem>();
    private FileTabViewModel? breadcrumbTab;
    private bool navSyncing;
    private bool navSelectionChangedDuringPress;
    private bool forceClose;
    private bool ideCloseConfirmed;

    // ======== 标签拖动排序与拖出成新窗口的状态 ========
    /// <summary>按下的标签（null 表示当前没有可拖动的按下）。</summary>
    private object? dragTab;

    /// <summary>按下坐标（相对标签条），用于拖动阈值判定。</summary>
    private Point dragStartPoint;

    /// <summary>是否已进入拖动态（超过水平阈值）。</summary>
    private bool tabDragActive;

    /// <summary>拖动过程中是否发生过排序移动。</summary>
    private bool tabDragMoved;

    /// <summary>脱出候选：指针已越出标签条 / 窗口范围，释放时脱离为新窗口。</summary>
    private bool tabDetachCandidate;

    /// <summary>按下时标签的起始序号（排序日志用）。</summary>
    private int dragStartIndex = -1;

    /// <summary>拖拽结束后吞掉下一次按下，避免收尾点击误触发。</summary>
    private bool suppressNextTabPress;

    public MainWindow()
    {
        this.InitializeComponent();
        UiLogger.AttachWindow(this);
        UiLogger.Log("程序行为", "主窗口创建完成");
        // 标签拖动排序与拖出成新窗口：按下 / 移动 / 释放三个阶段在标签条上统一处理
        this.TabStrip.AddHandler(InputElement.PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(this.OnTabStripPointerPressed), RoutingStrategies.Bubble, true);
        this.TabStrip.AddHandler(InputElement.PointerMovedEvent,
            new EventHandler<PointerEventArgs>(this.OnTabStripPointerMoved), RoutingStrategies.Bubble, true);
        this.TabStrip.AddHandler(InputElement.PointerReleasedEvent,
            new EventHandler<PointerReleasedEventArgs>(this.OnTabStripPointerReleased), RoutingStrategies.Bubble, true);
        this.Closing += this.OnWindowClosing;
        this.DataContextChanged += this.OnDataContextChanged;
    }

    /// <summary>主窗口视图模型。</summary>
    private MainWindowViewModel? ViewModel
    {
        get { return this.DataContext as MainWindowViewModel; }
    }

    /// <summary>窗口关闭时：IDE 模式先确认未保存编辑器；默认最小化到托盘常驻；托盘菜单选择退出时才真正关闭并保存会话。</summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        if (viewModel.IsIdeMode && viewModel.IdeShell.HasDirtyEditors() && !this.ideCloseConfirmed)
        {
            e.Cancel = true;
            UiLogger.Log("程序行为", "关闭窗口：IDE 模式存在未保存的编辑器，先逐个确认，确认完成前不关闭");
            _ = this.ConfirmIdeEditorsAndCloseAsync(viewModel);
            return;
        }
        if (viewModel.Settings.CloseToTray && !this.forceClose && WindowRegistry.IsLastWindow(this))
        {
            e.Cancel = true;
            UiLogger.Log("程序行为", "关闭最后一个窗口：按设置最小化到托盘常驻");
            this.Hide();
            return;
        }
        if (viewModel.Settings.CloseToTray && !this.forceClose)
        {
            // 还有其他窗口时直接关闭本窗口（会话保存取全窗口并集，不丢其他窗口标签）
            UiLogger.Log("程序行为", "关闭非最后窗口：直接关闭该窗口，不进入托盘常驻");
        }
        UiLogger.Log("程序退出", "关闭窗口：保存会话（全窗口标签并集）后退出");
        viewModel.SaveOpenTabs();
    }

    /// <summary>关闭窗口前逐个确认 IDE 中未保存的编辑器：全部处理完毕后再真正关闭。</summary>
    private async Task ConfirmIdeEditorsAndCloseAsync(MainWindowViewModel viewModel)
    {
        foreach (IdeEditorTabViewModel editor in viewModel.IdeShell.Editors)
        {
            if (!editor.IsDirty)
            {
                continue;
            }
            SavePromptChoice choice = await MessageDialog.ConfirmSaveAsync(this, editor.Title);
            if (choice == SavePromptChoice.Cancel)
            {
                UiLogger.Log("程序行为", "关闭窗口保存确认：编辑器标题=" + editor.Title + " 选择=取消 结果=已取消本次关闭");
                return;
            }
            if (choice == SavePromptChoice.Save)
            {
                UiLogger.Log("程序行为", "关闭窗口保存确认：编辑器标题=" + editor.Title + " 选择=保存 结果=已保存该编辑器");
                editor.Save();
            }
            else
            {
                UiLogger.Log("程序行为", "关闭窗口保存确认：编辑器标题=" + editor.Title + " 选择=不保存 结果=保留未保存更改，继续关闭流程");
            }
        }
        this.ideCloseConfirmed = true;
        this.Close();
    }

    /// <summary>托盘菜单"退出"调用：跳过托盘常驻，真正关闭窗口。</summary>
    public void ForceClose()
    {
        UiLogger.Log("程序退出", "执行强制退出：跳过托盘常驻，直接关闭主窗口");
        this.forceClose = true;
        this.Close();
    }

    /// <summary>DataContext 就绪后订阅视图模型变化，驱动标签条高亮。</summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (this.ViewModel != null)
        {
            this.ViewModel.OwnerWindow = this;
        }
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        this.ApplyTopWhiteStrip(viewModel.ShowTopWhiteStrip);
        viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
        viewModel.Tabs.CollectionChanged -= this.OnTabsCollectionChanged;
        viewModel.Tabs.CollectionChanged += this.OnTabsCollectionChanged;
        this.UpdateTabStripHighlights();
        this.UpdateDefaultExplorerButtons();
        this.AttachSelectedTabListener();
        this.RefreshBreadcrumbAndStatus();
        this.UpdateNavSelection();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ShowTopWhiteStrip))
        {
            MainWindowViewModel? current = this.ViewModel;
            if (current != null)
            {
                this.ApplyTopWhiteStrip(current.ShowTopWhiteStrip);
            }
            return;
        }
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
        {
            // IDE 模式下点选资源管理器标签：自动切回资源管理器模式展示该标签内容
            MainWindowViewModel? viewModel = this.ViewModel;
            if (viewModel != null && viewModel.IsIdeMode && viewModel.SelectedTab != null)
            {
                viewModel.ExitIdeMode();
            }
            if (viewModel != null)
            {
                UiLogger.Log("程序行为", "切换标签：当前标签序号=" + viewModel.Tabs.IndexOf(viewModel.SelectedTab));
            }
            this.UpdateTabStripHighlights();
            this.AttachSelectedTabListener();
            this.RefreshBreadcrumbAndStatus();
            this.UpdateNavSelection();
        }
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.UpdateTabStripHighlights();
        this.RefreshBreadcrumbAndStatus();
        this.UpdateNavSelection();
    }

    /// <summary>刷新标签条激活高亮，并为已实现容器挂接悬停事件。</summary>
    private void UpdateTabStripHighlights()
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        foreach (Control container in this.TabStrip.GetRealizedContainers())
        {
            if (container is not ListBoxItem item)
            {
                continue;
            }
            if (!this.tabHooked.Contains(item))
            {
                item.PointerEntered += this.OnTabPointerEntered;
                item.PointerExited += this.OnTabPointerExited;
                this.tabHooked.Add(item);
            }
            item.Classes.Remove("tabhover");
            if (object.ReferenceEquals(item.DataContext, viewModel.SelectedTab))
            {
                item.Classes.Add("tabactive");
            }
            else
            {
                item.Classes.Remove("tabactive");
            }
        }
    }

    private void OnTabPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is ListBoxItem item && !item.Classes.Contains("tabactive"))
        {
            item.Classes.Add("tabhover");
        }
    }

    private void OnTabPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.Classes.Remove("tabhover");
        }
    }

    /// <summary>在标题栏空白处按下左键：拖动窗口。</summary>
    private void OnCaptionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            UiLogger.Log("程序行为", "标题栏拖动：开始移动窗口");
            this.BeginMoveDrag(e);
        }
    }

    /// <summary>标题栏双击：最大化 / 还原。</summary>
    private void OnCaptionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Button)
        {
            return;
        }
        if (this.WindowState == WindowState.Maximized)
        {
            this.WindowState = WindowState.Normal;
        }
        else
        {
            this.WindowState = WindowState.Maximized;
        }
        UiLogger.Log("程序行为", "标题栏双击：窗口状态切换为 " + this.WindowState);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("程序行为", "点击最小化按钮");
        this.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
        {
            this.WindowState = WindowState.Normal;
        }
        else
        {
            this.WindowState = WindowState.Maximized;
        }
        UiLogger.Log("程序行为", "点击最大化按钮：窗口状态切换为 " + this.WindowState);
    }

    private void OnCloseWindowClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("程序行为", "点击关闭按钮");
        this.Close();
    }

    /// <summary>全局快捷键：IDE 模式下走 IDE 快捷键，否则走资源管理器快捷键。</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "处理全局快捷键：当前没有可用的主窗口视图模型，未处理任何快捷键");
            return;
        }

        if (viewModel.IsIdeMode)
        {
            this.HandleIdeKeyDown(viewModel, e);
            return;
        }

        if (e.Key == Key.T && e.KeyModifiers == KeyModifiers.Control)
        {
            UiLogger.Log("程序行为", "快捷键：控制键与字母键T同按 新建标签 结果=已请求新建标签");
            viewModel.AddTab(string.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Control)
        {
            UiLogger.Log("程序行为", "快捷键：控制键与字母键W同按 关闭当前选中标签");
            this.CloseCurrentTab();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            if (viewModel.Tabs.Count == 0 || viewModel.SelectedTab == null)
            {
                UiLogger.Log("程序行为", "快捷键：控制键与制表键同按 切换标签 当前没有可切换的标签，未执行");
            }
            else if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
            {
                UiLogger.Log("程序行为", "快捷键：控制键与制表键与上档键同按 向前切换标签 结果=已切换");
                viewModel.CycleTab(-1);
            }
            else
            {
                UiLogger.Log("程序行为", "快捷键：控制键与制表键同按 向后切换标签 结果=已切换");
                viewModel.CycleTab(1);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            FileTabViewModel? refreshTab = viewModel.SelectedFileTab;
            if (refreshTab == null)
            {
                UiLogger.Log("程序行为", "快捷键：功能键F5 刷新 当前没有文件标签，未刷新");
            }
            else
            {
                UiLogger.Log("程序行为", "快捷键：功能键F5 刷新 当前目录="
                    + (string.IsNullOrEmpty(refreshTab.CurrentPath) ? "此电脑" : refreshTab.CurrentPath)
                    + " 结果=已重新加载当前目录");
                refreshTab.Refresh();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            _ = this.RenameCheckedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            FileTabViewModel? deleteTab = viewModel.SelectedFileTab;
            if (deleteTab == null)
            {
                UiLogger.Log("程序行为", "快捷键：删除键 删除勾选条目 当前没有文件标签，未删除");
            }
            else
            {
                int deleteCheckedCount = deleteTab.GetCheckedRows().Count;
                UiLogger.Log("程序行为", "快捷键：删除键 删除勾选条目 当前目录="
                    + (string.IsNullOrEmpty(deleteTab.CurrentPath) ? "此电脑" : deleteTab.CurrentPath)
                    + " 勾选条目数=" + deleteCheckedCount
                    + " 结果=" + (deleteCheckedCount == 0 ? "没有勾选任何条目，未删除" : "已执行删除并刷新列表"));
                deleteTab.DeleteChecked();
            }
            e.Handled = true;
        }
    }

    /// <summary>IDE 模式快捷键：保存 / 全部保存 / 关闭编辑器 / 快速打开 / 查找，按键组合取自自定义快捷键配置。</summary>
    private void HandleIdeKeyDown(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        IdeShellViewModel shell = viewModel.IdeShell;
        if (this.MatchesHotkey(viewModel, "Save", e))
        {
            UiLogger.Log("程序行为", "快捷键触发保存当前编辑器 结果=已请求保存");
            shell.SaveActive();
            e.Handled = true;
        }
        else if (this.MatchesHotkey(viewModel, "SaveAll", e))
        {
            UiLogger.Log("程序行为", "快捷键触发全部保存 结果=已请求保存全部编辑器");
            shell.SaveAll();
            e.Handled = true;
        }
        else if (this.MatchesHotkey(viewModel, "CloseEditor", e))
        {
            IdeEditorTabViewModel? editor = shell.SelectedEditor;
            if (editor != null)
            {
                UiLogger.Log("程序行为", "快捷键触发关闭当前编辑器：编辑器标题=" + editor.Title);
                _ = this.CloseIdeEditorAsync(shell, editor);
            }
            else
            {
                UiLogger.Log("程序行为", "快捷键触发关闭当前编辑器：当前没有打开的编辑器，未执行");
            }
            e.Handled = true;
        }
        else if (this.MatchesHotkey(viewModel, "QuickOpen", e))
        {
            UiLogger.Log("程序行为", "快捷键触发快速打开面板 结果=已显示快速打开面板");
            shell.OpenQuickOpen();
            e.Handled = true;
        }
        else if (this.MatchesHotkey(viewModel, "Find", e))
        {
            UiLogger.Log("程序行为", "快捷键触发显示查找面板 结果=已显示查找面板");
            shell.ShowFindPanel = true;
            e.Handled = true;
        }
    }

    /// <summary>判断按键事件是否命中指定动作的自定义快捷键（修饰键需完全一致）。</summary>
    private bool MatchesHotkey(MainWindowViewModel viewModel, string actionName, KeyEventArgs e)
    {
        foreach (HotkeySetting setting in viewModel.Settings.Hotkeys)
        {
            if (setting.ActionName != actionName)
            {
                continue;
            }
            Key key;
            if (!Enum.TryParse(setting.KeyText, out key))
            {
                return false;
            }
            KeyModifiers required = KeyModifiers.None;
            if (setting.Ctrl)
            {
                required |= KeyModifiers.Control;
            }
            if (setting.Alt)
            {
                required |= KeyModifiers.Alt;
            }
            if (setting.Shift)
            {
                required |= KeyModifiers.Shift;
            }
            return e.Key == key && e.KeyModifiers == required;
        }
        return false;
    }

    /// <summary>关闭 IDE 编辑器标签：脏内容先弹「保存 / 不保存 / 取消」确认框。</summary>
    public async Task CloseIdeEditorAsync(IdeShellViewModel shell, IdeEditorTabViewModel editor)
    {
        if (editor.IsDirty)
        {
            SavePromptChoice choice = await MessageDialog.ConfirmSaveAsync(this, editor.Title);
            if (choice == SavePromptChoice.Cancel)
            {
                UiLogger.Log("程序行为", "关闭编辑器确认：编辑器标题=" + editor.Title + " 选择=取消 结果=已取消关闭该编辑器");
                return;
            }
            if (choice == SavePromptChoice.Save)
            {
                UiLogger.Log("程序行为", "关闭编辑器确认：编辑器标题=" + editor.Title + " 选择=保存 结果=已保存并关闭该编辑器");
                editor.Save();
            }
            else
            {
                UiLogger.Log("程序行为", "关闭编辑器确认：编辑器标题=" + editor.Title + " 选择=不保存 结果=已不保存并关闭该编辑器");
            }
        }
        else
        {
            UiLogger.Log("程序行为", "关闭编辑器：编辑器标题=" + editor.Title + " 没有未保存的更改 结果=直接关闭该编辑器");
        }
        shell.CloseEditor(editor);
    }

    /// <summary>IDE 模式切换按钮：在资源管理器模式与 IDE 模式之间一键切换。</summary>
    private void OnToggleIdeModeClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击模式切换按钮：当前没有可用的主窗口视图模型，未切换");
            return;
        }
        bool willEnterIdeMode = !viewModel.IsIdeMode;
        viewModel.ToggleIdeMode();
        UiLogger.Log("程序行为", "点击模式切换按钮：结果=已切换到" + (willEnterIdeMode ? "IDE 模式" : "资源管理器模式"));
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击新建标签按钮：当前没有可用的主窗口视图模型，未新建");
            return;
        }
        viewModel.AddTab(string.Empty);
        UiLogger.Log("程序行为", "点击新建标签按钮：新建起始位置=此电脑 结果=已新建标签，标签总数=" + viewModel.Tabs.Count);
    }

    private void OnTaskBoardClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击任务看板按钮：当前没有可用的主窗口视图模型，未打开");
            return;
        }
        viewModel.OpenTaskBoardTab();
        UiLogger.Log("程序行为", "点击任务看板按钮：结果=已打开任务看板标签 标签总数=" + viewModel.Tabs.Count);
    }

    private void OnGlobalSearchClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击全局搜索按钮：当前没有可用的主窗口视图模型，未打开");
            return;
        }
        viewModel.OpenSearchTab();
        UiLogger.Log("程序行为", "点击全局搜索按钮：结果=已打开全局搜索标签 标签总数=" + viewModel.Tabs.Count);
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is object tab)
        {
            UiLogger.Log("程序行为", "点击标签关闭按钮：" + this.DescribeTab(tab) + " 结果=已请求关闭该标签");
            this.CloseCurrentTab(tab);
        }
    }

    private void OnTabHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "双击标签头部：当前没有可用的主窗口视图模型，未关闭标签");
            return;
        }
        if (!viewModel.Settings.DoubleClickTabClose)
        {
            UiLogger.Log("程序行为", "双击标签头部：设置中未开启双击关闭标签，未关闭");
            return;
        }
        if (sender is Grid headerGrid && headerGrid.DataContext is object tab)
        {
            UiLogger.Log("程序行为", "双击标签头部：" + this.DescribeTab(tab) + " 结果=已请求关闭该标签");
            this.CloseCurrentTab(tab);
        }
    }

    // ======== 标签拖动排序与拖出成新窗口 ========

    /// <summary>标签条按下：记录按下标签并初始化拖拽状态；右键菜单、列表选中与关闭按钮保持原有行为。</summary>
    private void OnTabStripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this.suppressNextTabPress)
        {
            this.suppressNextTabPress = false;
            return;
        }
        if (!e.GetCurrentPoint(this.TabStrip).Properties.IsLeftButtonPressed)
        {
            // 右键按下不参与标签拖拽，交由右键菜单处理
            return;
        }
        Visual? source = e.Source as Visual;
        if (source == null || IsInsideButton(source))
        {
            // 关闭按钮等内部控件不参与拖拽
            return;
        }
        ListBoxItem? container = this.FindTabContainer(source);
        MainWindowViewModel? viewModel = this.ViewModel;
        if (container == null || viewModel == null)
        {
            return;
        }
        object? tab = container.DataContext;
        if (tab == null || !viewModel.Tabs.Contains(tab))
        {
            return;
        }
        this.dragTab = tab;
        this.dragStartPoint = e.GetPosition(this.TabStrip);
        this.tabDragActive = false;
        this.tabDragMoved = false;
        this.tabDetachCandidate = false;
        this.dragStartIndex = viewModel.Tabs.IndexOf(tab);
    }

    /// <summary>标签条移动：超过水平阈值进入拖拽，实时计算换位或判定已越出标签条与窗口边界。</summary>
    private void OnTabStripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (this.dragTab == null)
        {
            return;
        }
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            this.ResetTabDrag();
            return;
        }
        Point stripPoint = e.GetPosition(this.TabStrip);
        if (!this.tabDragActive)
        {
            double horizontal = Math.Abs(stripPoint.X - this.dragStartPoint.X);
            double vertical = Math.Abs(stripPoint.Y - this.dragStartPoint.Y);
            if (horizontal < 6 && vertical < 6)
            {
                return;
            }
            if (horizontal < vertical)
            {
                // 以垂直为主的移动不视为标签拖动
                this.ResetTabDrag();
                return;
            }
            this.tabDragActive = true;
            e.Pointer.Capture(this.TabStrip);
            e.Handled = true;
        }

        // 越界判定：指针越过标签条上下各 12 像素，或越出窗口范围
        Point windowPoint = e.GetPosition(this);
        bool outsideStrip = stripPoint.Y < -12 || stripPoint.Y > this.TabStrip.Bounds.Height + 12;
        bool outsideWindow = windowPoint.X < 0 || windowPoint.X > this.Bounds.Width
            || windowPoint.Y < 0 || windowPoint.Y > this.Bounds.Height;
        if (outsideStrip || outsideWindow)
        {
            this.tabDetachCandidate = true;
        }
        if (this.tabDetachCandidate)
        {
            // 越界状态成立后停止换位移动，等待松开时判定为新窗口
            e.Handled = true;
            return;
        }

        int currentIndex = viewModel.Tabs.IndexOf(this.dragTab);
        if (currentIndex < 0)
        {
            this.ResetTabDrag();
            return;
        }
        int targetIndex = this.ComputeTabDropIndex(stripPoint, currentIndex);
        if (targetIndex >= 0 && targetIndex != currentIndex)
        {
            viewModel.Tabs.Move(currentIndex, targetIndex);
            this.tabDragMoved = true;
        }
        e.Handled = true;
    }

    /// <summary>标签条松开：按最终状态判定拖出为新窗口，或记录拖动排序日志，然后恢复正常状态。</summary>
    private void OnTabStripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (this.dragTab == null)
        {
            return;
        }
        object tab = this.dragTab;
        bool wasActive = this.tabDragActive;
        bool detach = this.tabDetachCandidate;
        bool moved = this.tabDragMoved;
        int startIndex = this.dragStartIndex;
        this.ResetTabDrag();
        if (!wasActive)
        {
            return;
        }
        this.suppressNextTabPress = true;
        e.Pointer.Capture(null);

        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        if (detach)
        {
            if (viewModel.Tabs.IndexOf(tab) < 0)
            {
                return;
            }
            // 从原窗口移除（内部会先关闭标签页），再创建新窗口挂载该标签并选中
            viewModel.DetachTab(tab);
            MainWindow newWindow = WindowRegistry.CreateSecondaryWindow(viewModel.Settings, this);
            if (newWindow.DataContext is MainWindowViewModel newViewModel)
            {
                newViewModel.AttachTab(tab);
            }
            UiLogger.Log("程序行为", "标签拖出成为新窗口：" + this.DescribeTab(tab)
                + " 源窗口剩余标签数=" + viewModel.Tabs.Count);
            return;
        }
        if (moved)
        {
            UiLogger.Log("程序行为", "标签拖动排序：" + this.DescribeTab(tab)
                + " 开始序号=" + startIndex + " 结束序号=" + viewModel.Tabs.IndexOf(tab));
        }
    }

    /// <summary>计算拖拽落点序号：按标签条上水平方向位于指针左侧的其他标签数目确定目标序号。</summary>
    private int ComputeTabDropIndex(Point stripPoint, int currentIndex)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return currentIndex;
        }
        int countLeft = 0;
        for (int i = 0; i < viewModel.Tabs.Count; i++)
        {
            if (i == currentIndex)
            {
                continue;
            }
            if (!(this.TabStrip.ContainerFromIndex(i) is Control container))
            {
                continue;
            }
            Point? origin = container.TranslatePoint(new Point(0, 0), this.TabStrip);
            if (origin == null)
            {
                continue;
            }
            double centerX = origin.Value.X + container.Bounds.Width / 2.0;
            if (stripPoint.X > centerX)
            {
                countLeft++;
            }
        }
        return countLeft;
    }

    /// <summary>清理拖动状态。</summary>
    private void ResetTabDrag()
    {
        this.dragTab = null;
        this.tabDragActive = false;
        this.tabDragMoved = false;
        this.tabDetachCandidate = false;
        this.dragStartIndex = -1;
    }

    /// <summary>从事件源向上层查找所在的标签容器。</summary>
    private ListBoxItem? FindTabContainer(Visual source)
    {
        Visual? current = source;
        while (current != null)
        {
            if (current is ListBoxItem item)
            {
                return item;
            }
            current = current.GetVisualParent();
        }
        return null;
    }

    /// <summary>判断事件源是否位于按钮内部（关闭按钮等不参与拖拽）。</summary>
    private static bool IsInsideButton(Visual source)
    {
        Visual? current = source;
        while (current != null)
        {
            if (current is Button)
            {
                return true;
            }
            current = current.GetVisualParent();
        }
        return false;
    }

    /// <summary>标签描述（日志用）：文件标签写路径，其他标签写类型名。</summary>
    private string DescribeTab(object tab)
    {
        if (tab is FileTabViewModel fileTab)
        {
            return "路径=" + fileTab.CurrentPath;
        }
        return "类型=" + tab.GetType().Name;
    }

    /// <summary>书签栏点击：在当前文件标签中打开目标路径（无文件标签时自动新建）。</summary>
    private void OnBookmarkItemClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("导航行为", "点击书签栏项目：当前没有可用的主窗口视图模型，未打开");
            return;
        }
        if (sender is Button button && button.DataContext is QuickAccessItem item)
        {
            UiLogger.Log("导航行为", "点击书签栏项目：项目名=" + item.Name + " 书签路径=" + item.Path + " 结果=已在文件标签中打开（无文件标签时自动新建）");
            this.OpenInFileTab(item.Path);
        }
    }

    /// <summary>书签栏取消固定。</summary>
    private void OnBookmarkUnpinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is QuickAccessItem item)
        {
            if (this.ViewModel == null)
            {
                UiLogger.Log("程序行为", "点击书签取消固定按钮：当前没有可用的主窗口视图模型，未取消固定");
                return;
            }
            this.ViewModel.Unpin(item.Path);
            UiLogger.Log("程序行为", "点击书签取消固定按钮：书签路径=" + item.Path + " 结果=已从书签栏移除");
        }
    }

    /// <summary>徽标点击：回到此电脑。</summary>
    private void OnLogoClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("导航行为", "点击徽标按钮：当前没有可用的主窗口视图模型，未跳转");
            return;
        }
        if (viewModel.SelectedFileTab != null)
        {
            viewModel.SelectedFileTab.OpenPath(string.Empty);
            UiLogger.Log("导航行为", "点击徽标按钮：结果=当前文件标签已回到此电脑");
        }
        else
        {
            viewModel.AddTab(string.Empty);
            UiLogger.Log("导航行为", "点击徽标按钮：当前没有文件标签 结果=已新建文件标签并打开此电脑");
        }
    }

    /// <summary>工具盒子按钮：打开任务看板标签页。</summary>
    private void OnToolboxClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击工具箱按钮：当前没有可用的主窗口视图模型，未打开");
            return;
        }
        viewModel.OpenTaskBoardTab();
        UiLogger.Log("程序行为", "点击工具箱按钮：结果=已打开任务看板标签 标签总数=" + viewModel.Tabs.Count);
    }

    /// <summary>主题切换按钮：深浅色互换并持久化。</summary>
    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击主题切换按钮：当前没有可用的主窗口视图模型，未切换");
            return;
        }
        string previousTheme = viewModel.Settings.Theme;
        viewModel.ToggleTheme();
        UiLogger.Log("程序行为", "点击主题切换按钮：原主题=" + (previousTheme == "Light" ? "浅色" : "深色")
            + " 结果=已切换为" + (viewModel.Settings.Theme == "Light" ? "浅色" : "深色") + "并保存设置");
    }

    // ======== 面包屑地址栏 + 状态栏 + 侧边栏联动 ========

    /// <summary>挂接当前选中文件标签的属性监听（驱动面包屑与状态栏联动）。</summary>
    private void AttachSelectedTabListener()
    {
        FileTabViewModel? current = this.ViewModel?.SelectedFileTab;
        if (object.ReferenceEquals(current, this.breadcrumbTab))
        {
            return;
        }
        if (this.breadcrumbTab != null)
        {
            this.breadcrumbTab.PropertyChanged -= this.OnTabPropertyChanged;
        }
        this.breadcrumbTab = current;
        if (this.breadcrumbTab != null)
        {
            this.breadcrumbTab.PropertyChanged += this.OnTabPropertyChanged;
        }
    }

    /// <summary>文件标签属性变化：路径变化重建面包屑，列表变化刷新项目数。</summary>
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileTabViewModel.CurrentPath))
        {
            this.RebuildBreadcrumb();
            this.UpdateNavSelection();
        }
        else if (e.PropertyName == nameof(FileTabViewModel.VisibleRows))
        {
            this.UpdateStatusCount();
        }
    }

    /// <summary>刷新面包屑与状态栏项目数。</summary>
    private void RefreshBreadcrumbAndStatus()
    {
        this.RebuildBreadcrumb();
        this.UpdateStatusCount();
    }

    /// <summary>状态栏：显示当前标签的项目数。</summary>
    private void UpdateStatusCount()
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            this.StatusCountText.Text = "—";
            return;
        }
        this.StatusCountText.Text = tab.VisibleRows.Count + " 个项目";
    }

    /// <summary>按当前路径重建面包屑分段。</summary>
    private void RebuildBreadcrumb()
    {
        this.BreadcrumbPanel.Children.Clear();
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        string path = tab?.CurrentPath ?? string.Empty;
        this.AddBreadcrumbSegment("此电脑", string.Empty);
        if (!string.IsNullOrEmpty(path))
        {
            string[] parts = path.TrimEnd('\\').Split('\\');
            string accumulated = string.Empty;
            foreach (string part in parts)
            {
                if (part.Length == 0)
                {
                    continue;
                }
                if (part.EndsWith(":", StringComparison.Ordinal))
                {
                    accumulated = part + "\\";
                    this.AddBreadcrumbSegment(part + "\\", accumulated);
                    continue;
                }
                accumulated = accumulated.TrimEnd('\\') + "\\" + part;
                this.AddBreadcrumbSegment(part, accumulated);
            }
        }
        else
        {
            // 此电脑视图：仅保留「此电脑」根分段
        }
    }

    /// <summary>向面包屑追加一个分段（前面加分隔箭头）。</summary>
    private void AddBreadcrumbSegment(string name, string path)
    {
        if (this.BreadcrumbPanel.Children.Count > 0)
        {
            TextBlock separator = new TextBlock
            {
                Text = "\uE76C",
                FontFamily = (FontFamily)this.Resources["IconFont"],
                FontSize = 10,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            this.BreadcrumbPanel.Children.Add(separator);
        }
        Button segment = new Button
        {
            Content = name,
            Tag = path,
            Padding = new Thickness(6, 3, 6, 3),
        };
        segment.Classes.Add("nav");
        segment.Click += this.OnBreadcrumbSegmentClick;
        this.BreadcrumbPanel.Children.Add(segment);
    }

    /// <summary>点击面包屑分段：跳转到该路径。</summary>
    private void OnBreadcrumbSegmentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            string targetDirectory = string.IsNullOrEmpty(path) ? "此电脑" : path;
            FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
            if (tab == null)
            {
                UiLogger.Log("导航行为", "点击面包屑分段：目标路径=" + targetDirectory + " 当前没有文件标签，未跳转");
                return;
            }
            tab.OpenPath(path);
            UiLogger.Log("导航行为", "点击面包屑分段：目标路径=" + targetDirectory + " 结果=已在当前标签打开");
        }
    }

    /// <summary>面包屑进入编辑模式：显示地址输入框（点击路径区空白即触发）。</summary>
    private void OnBreadcrumbEditClick(object? sender, RoutedEventArgs e)
    {
        string currentDirectory = this.ViewModel?.SelectedFileTab?.CurrentPath ?? string.Empty;
        UiLogger.Log("程序行为", "点击路径区空白进入地址编辑：当前路径="
            + (string.IsNullOrEmpty(currentDirectory) ? "此电脑" : currentDirectory)
            + " 结果=已显示地址输入框并全选路径");
        this.BreadcrumbPanel.IsVisible = false;
        this.AddressBox.IsVisible = true;
        this.AddressBox.Text = this.ViewModel?.SelectedFileTab?.CurrentPath ?? string.Empty;
        this.AddressBox.Focus();
        this.AddressBox.SelectAll();
    }

    /// <summary>侧边栏选中变化：打开目标路径（当前不是文件标签时新建文件标签），并同步另一组列表的选中。</summary>
    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.navSyncing)
        {
            return;
        }
        // 本次按下已改变选中：说明是新一次点击，选中变化流程已打开目标，松开时不再重复处理
        this.navSelectionChangedDuringPress = true;
        this.navSyncing = true;
        ListBox other = object.ReferenceEquals(sender, this.PinnedNavList) ? this.PcNavList : this.PinnedNavList;
        other.SelectedItem = null;
        this.navSyncing = false;
        if (sender is ListBox list && list.SelectedItem is QuickAccessItem item)
        {
            string listName = object.ReferenceEquals(list, this.PinnedNavList) ? "固定快捷访问列表" : "此电脑列表";
            this.OpenInFileTab(item.Path);
            UiLogger.Log("导航行为", "点击左侧导航项：列表名称=" + listName + " 项目名=" + item.Name + " 路径=" + item.Path + " 结果=已在文件标签中打开（无文件标签时自动新建）");
        }
    }

    /// <summary>快捷访问列表按下后松开：仅当本次按下没有改变选中（即重复点击当前选中项）时处理，
    /// 路径与当前相同则回到导航根目录（此电脑），否则按首次点击打开所选路径。</summary>
    private void OnNavPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool selectionChanged = this.navSelectionChangedDuringPress;
        this.navSelectionChangedDuringPress = false;
        if (selectionChanged || e.InitialPressMouseButton != MouseButton.Left || this.navSyncing)
        {
            return;
        }
        if (sender is not ListBox list)
        {
            return;
        }
        Visual? source = e.Source as Visual;
        ListBoxItem? container = null;
        while (source != null && container == null)
        {
            container = source as ListBoxItem;
            source = source.GetVisualParent();
        }
        if (container?.DataContext is not QuickAccessItem item || !object.ReferenceEquals(item, list.SelectedItem))
        {
            return;
        }
        string currentPath = this.ViewModel?.SelectedFileTab?.CurrentPath ?? string.Empty;
        if (string.Equals(currentPath, item.Path, StringComparison.OrdinalIgnoreCase))
        {
            UiLogger.Log("导航行为", "再次点击快捷访问：路径与当前目录相同，回到导航根目录（此电脑） 项目名=" + item.Name + " 路径=" + item.Path);
            this.OpenInFileTab(string.Empty);
            return;
        }
        UiLogger.Log("导航行为", "再次点击快捷访问：选中未变化，打开所选路径 项目名=" + item.Name + " 路径=" + item.Path);
        this.OpenInFileTab(item.Path);
    }

    /// <summary>地址栏失去焦点：退出编辑模式并恢复中间栏显示的当前路径（回车确认打开）。</summary>
    private void OnAddressBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        this.ExitAddressEditMode();
    }

    /// <summary>在文件标签中打开路径：当前没有文件标签时自动新建一个，避免导航失效。</summary>
    private void OpenInFileTab(string path)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        if (viewModel.SelectedFileTab == null)
        {
            viewModel.AddTab(path);
            return;
        }
        viewModel.SelectedFileTab.OpenPath(path);
    }

    /// <summary>按当前路径同步侧边栏两组列表的选中高亮。</summary>
    private void UpdateNavSelection()
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        this.navSyncing = true;
        QuickAccessItem? match = viewModel.MatchNavByPath(viewModel.SelectedFileTab?.CurrentPath);
        this.PinnedNavList.SelectedItem = match != null && viewModel.QuickAccessItems.Contains(match) ? match : null;
        this.PcNavList.SelectedItem = match != null && viewModel.ThisPcItems.Contains(match) ? match : null;
        this.navSyncing = false;
    }

    // ======== 工具栏（TabX 同款按钮组） ========

    /// <summary>查看菜单：切换到指定视图模式。</summary>
    private void OnViewModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string modeText
            && Enum.TryParse(modeText, out FileViewMode mode))
        {
            FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
            if (tab == null)
            {
                UiLogger.Log("程序行为", "点击查看菜单视图模式：当前没有文件标签，未切换");
                return;
            }
            tab.SetViewMode(mode);
            UiLogger.Log("程序行为", "点击查看菜单视图模式：视图模式=" + item.Header + " 结果=已切换当前标签视图");
        }
    }

    /// <summary>查看菜单：打开系统文件夹选项对话框。</summary>
    private void OnFolderOptionsClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("程序行为", "点击查看菜单文件夹选项：结果=已调起系统文件夹选项窗口");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "folders")
        {
            UseShellExecute = true,
        });
    }

    /// <summary>复制菜单：按变体复制勾选条目（文件 / 所在路径 / 完整路径 / 名称 / 带扩展名名称）。</summary>
    private async void OnCopyVariantClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击复制菜单项：当前没有文件标签，未复制");
            return;
        }
        if (sender is not MenuItem item || item.Tag is not string variant)
        {
            return;
        }
        List<FileRow> rows = tab.GetCheckedRows();
        if (rows.Count == 0)
        {
            tab.ShowMessage("请先勾选要复制的条目");
            UiLogger.Log("程序行为", "点击复制菜单项：复制方式=" + item.Header + " 当前没有勾选任何条目，未复制");
            return;
        }
        if (variant == "files")
        {
            this.ViewModel?.CopyPathsToClipboard(rows.Select(row => row.FullPath).ToList());
            UiLogger.Log("程序行为", "点击复制菜单项：复制方式=" + item.Header + " 勾选条目数=" + rows.Count + " 结果=已复制到内部剪贴板");
            return;
        }
        IEnumerable<string> texts = rows.Select(row => variant switch
        {
            "dir" => Path.GetDirectoryName(row.FullPath) ?? string.Empty,
            "full" => row.FullPath,
            "name" => Path.GetFileNameWithoutExtension(row.FullPath),
            "nameext" => Path.GetFileName(row.FullPath),
            _ => row.FullPath,
        });
        string payload = string.Join(Environment.NewLine, texts);
        if (this.Clipboard != null)
        {
            DataTransfer transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(payload));
            await this.Clipboard.SetDataAsync(transfer);
            UiLogger.Log("程序行为", "点击复制菜单项：复制方式=" + item.Header + " 勾选条目数=" + rows.Count + " 结果=已写入系统剪贴板");
        }
    }

    /// <summary>工具栏剪切：剪切勾选条目。</summary>
    private void OnToolbarCutClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击工具栏剪切按钮：当前没有文件标签，未剪切");
            return;
        }
        List<FileRow> rows = tab.GetCheckedRows();
        if (rows.Count == 0)
        {
            tab.ShowMessage("请先勾选要剪切的条目");
            UiLogger.Log("程序行为", "点击工具栏剪切按钮：当前没有勾选任何条目，未剪切");
            return;
        }
        this.ViewModel?.CutPathsToClipboard(rows.Select(row => row.FullPath).ToList());
        UiLogger.Log("程序行为", "点击工具栏剪切按钮：勾选条目数=" + rows.Count + " 结果=已剪切到内部剪贴板，等待粘贴到目标文件夹");
    }

    /// <summary>工具栏粘贴：粘贴剪贴板内容到当前目录。</summary>
    private void OnToolbarPasteClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击工具栏粘贴按钮：当前没有文件标签，未粘贴");
            return;
        }
        UiLogger.Log("程序行为", "点击工具栏粘贴按钮：目标目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 结果=已请求粘贴剪贴板内容（剪贴板为空或目标目录不可写时由界面提示）");
        this.ViewModel?.PasteFromClipboard();
    }

    /// <summary>标签右键菜单：新建标签。</summary>
    private void OnTabMenuNewTab(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击标签菜单新建标签：当前没有可用的主窗口视图模型，未新建");
            return;
        }
        viewModel.AddTab(string.Empty);
        UiLogger.Log("程序行为", "点击标签菜单新建标签：新建起始位置=此电脑 结果=已新建标签，标签总数=" + viewModel.Tabs.Count);
    }

    /// <summary>标签右键菜单：复制当前标签（新标签打开同一目录）。</summary>
    private void OnTabMenuDuplicate(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            MainWindowViewModel? viewModel = this.ViewModel;
            if (viewModel == null)
            {
                UiLogger.Log("程序行为", "点击标签菜单复制当前标签：当前没有可用的主窗口视图模型，未复制");
                return;
            }
            viewModel.AddTab(tab.CurrentPath);
            UiLogger.Log("程序行为", "点击标签菜单复制当前标签：源目录="
                + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
                + " 结果=已新建同目录标签，标签总数=" + viewModel.Tabs.Count);
        }
    }

    /// <summary>标签右键菜单：把该标签目录添加到书签栏。</summary>
    private void OnTabMenuAddToBookmarks(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            MainWindowViewModel? viewModel = this.ViewModel;
            if (viewModel == null)
            {
                UiLogger.Log("程序行为", "点击标签菜单添加到书签：当前没有可用的主窗口视图模型，未添加");
                return;
            }
            if (string.IsNullOrEmpty(tab.CurrentPath))
            {
                UiLogger.Log("程序行为", "点击标签菜单添加到书签：当前在此电脑视图，没有可收藏的路径，未添加");
                return;
            }
            string bookmarkPath = tab.CurrentPath;
            bool alreadyPinned = viewModel.Settings.PinnedPaths.Contains(tab.CurrentPath);
            viewModel.PinPath(tab.CurrentPath);
            UiLogger.Log("程序行为", "点击标签菜单添加到书签：书签路径=" + bookmarkPath
                + " 结果=" + (alreadyPinned ? "该路径已在书签栏中，未重复添加" : "已添加到书签栏"));
        }
    }

    /// <summary>标签右键菜单：关闭该标签。</summary>
    private void OnTabMenuClose(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            UiLogger.Log("程序行为", "点击标签菜单关闭标签：" + this.DescribeTab(tab) + " 结果=已请求关闭该标签");
            this.CloseCurrentTab(tab);
        }
    }

    /// <summary>标签右键菜单：关闭其他全部文件标签。</summary>
    private void OnTabMenuCloseOthers(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            MainWindowViewModel? viewModel = this.ViewModel;
            if (viewModel == null)
            {
                UiLogger.Log("程序行为", "点击标签菜单关闭其他标签：当前没有可用的主窗口视图模型，未关闭");
                return;
            }
            int closableCount = viewModel.Tabs.Count(item => item is FileTabViewModel file
                && !object.ReferenceEquals(file, tab));
            viewModel.CloseOtherTabs(tab);
            UiLogger.Log("程序行为", "点击标签菜单关闭其他标签：保留标签=" + this.DescribeTab(tab)
                + " 结果=" + (closableCount == 0 ? "没有其他文件标签，未关闭任何标签" : "已关闭其他文件标签数=" + closableCount));
        }
    }

    /// <summary>标签右键菜单：固定 / 取消固定该标签。</summary>
    private void OnTabMenuTogglePin(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            this.ViewModel?.TogglePinTab(tab);
            UiLogger.Log("程序行为", "点击标签菜单固定标签：" + this.DescribeTab(tab)
                + " 结果=" + (tab.IsPinned ? "已固定并移动到标签条最前" : "已取消固定"));
        }
    }

    private void OnToolbarNewFolderClick(object? sender, RoutedEventArgs e)
    {
        this.OnNewFolderClick(sender, e);
    }

    private void OnToolbarBatchRenameClick(object? sender, RoutedEventArgs e)
    {
        this.OnBatchRenameClick(sender, e);
    }

    private void OnToolbarEncodeConvertClick(object? sender, RoutedEventArgs e)
    {
        this.OnEncodeConvertClick(sender, e);
    }

    private void OnToolbarDeleteClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击工具栏删除按钮：当前没有可用的文件标签，未删除");
            return;
        }
        int deleteCheckedCount = tab.GetCheckedRows().Count;
        UiLogger.Log("程序行为", "点击工具栏删除按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 勾选条目数=" + deleteCheckedCount
            + " 结果=" + (deleteCheckedCount == 0 ? "没有勾选任何条目，未删除" : "已执行删除并刷新列表"));
        tab.DeleteChecked();
    }

    private void OnToolbarHiddenClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击显示隐藏文件按钮：当前没有可用的主窗口视图模型，未切换");
            return;
        }
        viewModel.Settings.ShowHiddenFiles = !viewModel.Settings.ShowHiddenFiles;
        viewModel.SaveSettingsAndApply();
        UiLogger.Log("程序行为", "点击显示隐藏文件按钮：结果=隐藏文件切换为"
            + (viewModel.Settings.ShowHiddenFiles ? "显示" : "隐藏") + "，设置已保存并应用到全部标签");
    }

    private void OnToolbarInfoPaneClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击信息面板按钮：当前没有文件标签，未切换");
            return;
        }
        tab.ShowInfoPane = !tab.ShowInfoPane;
        UiLogger.Log("程序行为", "点击信息面板按钮：结果=信息面板已" + (tab.ShowInfoPane ? "显示" : "隐藏"));
    }

    private void OnToolbarRefreshClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击工具栏刷新按钮：当前没有文件标签，未刷新");
            return;
        }
        UiLogger.Log("程序行为", "点击工具栏刷新按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 结果=已重新加载当前目录");
        tab.Refresh();
    }

    /// <summary>接管 / 恢复默认资源管理器按钮：切换设置并应用。</summary>
    private void OnDefaultExplorerClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击默认资源管理器按钮：当前没有可用的主窗口视图模型，未切换");
            return;
        }
        viewModel.Settings.DefaultFileManager = !viewModel.Settings.DefaultFileManager;
        DefaultFileManagerManager.Apply(viewModel.Settings.DefaultFileManager);
        viewModel.SaveSettingsAndApply();
        this.UpdateDefaultExplorerButtons();
        UiLogger.Log("程序行为", "点击默认资源管理器按钮：结果="
            + (viewModel.Settings.DefaultFileManager ? "已接管为默认资源管理器" : "已恢复系统默认资源管理器"));
    }

    /// <summary>按设置切换「设为默认 / 恢复默认」按钮的显示。</summary>
    private void UpdateDefaultExplorerButtons()
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        bool isDefault = viewModel?.Settings.DefaultFileManager == true;
        this.DefaultExplorerBtn.IsVisible = !isDefault;
        this.RestoreExplorerBtn.IsVisible = isDefault;
    }

    private void OnConnectionsClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击连接按钮：当前没有可用的主窗口视图模型，未打开");
            return;
        }
        viewModel.OpenConnectionsTab();
        UiLogger.Log("程序行为", "点击连接按钮：结果=已打开连接管理标签 标签总数=" + viewModel.Tabs.Count);
    }

    private void OnDiskViewClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击磁盘视图按钮：当前没有可用的主窗口视图模型，未打开");
            return;
        }
        viewModel.OpenDiskViewTab();
        UiLogger.Log("程序行为", "点击磁盘视图按钮：结果=已打开磁盘视图标签 标签总数=" + viewModel.Tabs.Count);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        UiLogger.Log("程序行为", "打开设置窗口");
        SettingsWindow window = new SettingsWindow(viewModel.Settings);
        await window.ShowDialog(this);
        UiLogger.Log("程序行为", "设置窗口已关闭，开始应用设置");
        App current = (App)App.Current!;
        // 开机自启：写入后校验系统真实结果，未生效时把设置回退为关
        bool autoStartApplied = AutoStartManager.Apply(viewModel.Settings.AutoStart);
        if (viewModel.Settings.AutoStart && !autoStartApplied)
        {
            viewModel.Settings.AutoStart = false;
        }
        current.SyncTrayAutoStartState();
        DefaultFileManagerManager.Apply(viewModel.Settings.DefaultFileManager);
        viewModel.SaveSettingsAndApply();
    }

    /// <summary>按设置切换窗口装饰：显示白条时保留 Avalonia 绘制的窗口边框，隐藏时不绘制任何装饰。</summary>
    private void ApplyTopWhiteStrip(bool show)
    {
        if (show)
        {
            this.WindowDecorations = Avalonia.Controls.WindowDecorations.BorderOnly;
        }
        else
        {
            this.WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        }
        UiLogger.Log("程序行为", "应用顶部白色条设置：是否显示=" + show + " 当前窗口装饰=" + this.WindowDecorations);
    }

    /// <summary>工具栏“进程监控”按钮：打开进程监控标签页。</summary>
    private void OnProcessMonitorClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击进程监控按钮：当前没有可用的主窗口视图模型，未打开");
            return;
        }
        viewModel.OpenProcessMonitorTab();
        UiLogger.Log("程序行为", "点击进程监控按钮：结果=已打开进程监控标签 标签总数=" + viewModel.Tabs.Count);
    }

    private void OnInfoPaneClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "切换信息面板：当前没有文件标签，未切换");
            return;
        }
        tab.ShowInfoPane = !tab.ShowInfoPane;
        UiLogger.Log("程序行为", "切换信息面板：结果=信息面板已" + (tab.ShowInfoPane ? "显示" : "隐藏"));
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击全选按钮：当前没有文件标签，未执行");
            return;
        }
        tab.SelectAll();
        UiLogger.Log("程序行为", "点击全选按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 结果=已勾选当前目录全部条目");
    }

    private async void OnBatchRenameClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击批量重命名按钮：当前没有文件标签，未执行");
            return;
        }
        List<FileRow> checkedRows = tab.GetCheckedRows();
        if (checkedRows.Count == 0)
        {
            tab.ShowMessage("请先勾选要改名的条目");
            UiLogger.Log("程序行为", "点击批量重命名按钮：当前目录="
                + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
                + " 当前没有勾选任何条目，未执行");
            return;
        }
        UiLogger.Log("程序行为", "点击批量重命名按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 勾选条目数=" + checkedRows.Count + " 结果=已打开批量重命名对话框");
        BatchRenameDialog dialog = new BatchRenameDialog(checkedRows);
        await dialog.ShowDialog(this);
        if (dialog.RenamePlan.Count > 0)
        {
            tab.ApplyBatchRenames(dialog.RenamePlan);
            UiLogger.Log("程序行为", "批量重命名对话框确认：改名条目数=" + dialog.RenamePlan.Count + " 结果=已执行批量改名并刷新列表");
        }
        else
        {
            UiLogger.Log("程序行为", "批量重命名对话框关闭：未生成改名计划，未执行改名");
        }
    }

    /// <summary>编码转换：把勾选的条目作为选中项打开编码转换对话框并执行计划。</summary>
    private async void OnEncodeConvertClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击编码转换按钮：当前没有文件标签，未执行");
            return;
        }
        List<FileRow> checkedRows = tab.GetCheckedRows();
        if (checkedRows.Count == 0)
        {
            tab.ShowMessage("请先勾选要转换编码的条目");
            UiLogger.Log("程序行为", "点击编码转换按钮：当前目录="
                + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
                + " 当前没有勾选任何条目，未执行");
            return;
        }
        UiLogger.Log("程序行为", "点击编码转换按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 勾选条目数=" + checkedRows.Count + " 结果=已打开编码转换对话框");
        List<string> targetPaths = new List<string>();
        foreach (FileRow row in checkedRows)
        {
            targetPaths.Add(row.FullPath);
        }
        EncodeConvertDialog dialog = new EncodeConvertDialog(tab.FileSystemProvider, targetPaths);
        await dialog.ShowDialog(this);
        if (dialog.EncodeConvertPlan.Count > 0)
        {
            tab.ApplyBatchRenames(dialog.EncodeConvertPlan);
            UiLogger.Log("程序行为", "编码转换对话框确认：转换条目数=" + dialog.EncodeConvertPlan.Count + " 结果=已执行文件名编码转换并刷新列表");
        }
        else
        {
            UiLogger.Log("程序行为", "编码转换对话框关闭：未生成转换计划，未执行转换");
        }
    }

    private void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击新建文件夹按钮：当前没有文件标签，未新建");
            return;
        }
        UiLogger.Log("程序行为", "点击新建文件夹按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 结果=已弹出文件夹名称输入框");
        _ = this.CreateFolderAsync(tab);
    }

    private async Task CreateFolderAsync(FileTabViewModel tab)
    {
        string? folderName = await InputDialog.Show(this, "新建文件夹", "请输入文件夹名称：", "新建文件夹");
        if (string.IsNullOrWhiteSpace(folderName))
        {
            UiLogger.Log("程序行为", "新建文件夹：未输入名称或取消输入 当前目录="
                + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
                + " 结果=未创建");
            return;
        }
        UiLogger.Log("程序行为", "新建文件夹：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 文件夹名称=" + folderName + " 结果=已提交创建（创建失败时状态栏提示）");
        tab.CreateFolder(folderName);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击删除按钮：当前没有可用的文件标签，未删除");
            return;
        }
        int deleteCheckedCount = tab.GetCheckedRows().Count;
        UiLogger.Log("程序行为", "点击删除按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 勾选条目数=" + deleteCheckedCount
            + " 结果=" + (deleteCheckedCount == 0 ? "没有勾选任何条目，未删除" : "已执行删除并刷新列表"));
        tab.DeleteChecked();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击刷新按钮：当前没有文件标签，未刷新");
            return;
        }
        UiLogger.Log("程序行为", "点击刷新按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 结果=已重新加载当前目录");
        tab.Refresh();
    }

    private void OnCloseAllTabsClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "点击关闭全部标签：当前没有可用的主窗口视图模型，未关闭");
            return;
        }
        int beforeCount = viewModel.Tabs.Count;
        viewModel.CloseAllTabs();
        if (viewModel.Tabs.Count == 0)
        {
            UiLogger.Log("程序行为", "点击关闭全部标签：关闭前标签数=" + beforeCount + " 结果=全部标签已关闭，随之关闭窗口");
            this.Close();
        }
        else
        {
            UiLogger.Log("程序行为", "点击关闭全部标签：关闭前标签数=" + beforeCount + " 结果=文件标签已全部关闭，保留非文件标签数=" + viewModel.Tabs.Count);
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("导航行为", "点击后退按钮：当前没有文件标签，未后退");
            return;
        }
        bool canGoBack = tab.CanGoBack;
        string sourceDirectory = string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath;
        tab.GoBack();
        UiLogger.Log("导航行为", "点击后退按钮：出发目录=" + sourceDirectory
            + " 结果=" + (canGoBack ? "已后退到上一个访问的路径" : "没有后退历史，停留在当前目录"));
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("导航行为", "点击前进按钮：当前没有文件标签，未前进");
            return;
        }
        bool canGoForward = tab.CanGoForward;
        string sourceDirectory = string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath;
        tab.GoForward();
        UiLogger.Log("导航行为", "点击前进按钮：出发目录=" + sourceDirectory
            + " 结果=" + (canGoForward ? "已前进到下一个访问的路径" : "没有前进历史，停留在当前目录"));
    }

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("导航行为", "点击上一级按钮：当前没有文件标签，未返回上一级");
            return;
        }
        string sourceDirectory = string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath;
        tab.NavigateUp();
        UiLogger.Log("导航行为", "点击上一级按钮：出发目录=" + sourceDirectory
            + " 结果=已请求返回上一级目录（已在根视图或压缩包根时停留在原处）");
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
            string? pathText = this.AddressBox.Text;
            if (tab != null && !string.IsNullOrWhiteSpace(pathText))
            {
                UiLogger.Log("导航行为", "地址栏回车：目标路径=" + pathText.Trim() + " 结果=已在当前标签打开");
                tab.OpenPath(pathText.Trim());
            }
            else
            {
                UiLogger.Log("导航行为", "地址栏回车：输入路径为空或当前没有文件标签，未跳转");
            }
            // 回车后退出编辑模式，恢复面包屑显示
            this.ExitAddressEditMode();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Esc 取消编辑，不跳转
            UiLogger.Log("导航行为", "地址栏按下退出键：已放弃本次路径输入，未跳转 结果=恢复面包屑显示");
            this.ExitAddressEditMode();
            e.Handled = true;
        }
    }

    /// <summary>退出地址编辑模式，恢复面包屑显示。</summary>
    private void ExitAddressEditMode()
    {
        this.AddressBox.IsVisible = false;
        this.BreadcrumbPanel.IsVisible = true;
        this.RefreshBreadcrumbAndStatus();
    }

    /// <summary>地址栏刷新按钮：刷新当前目录。</summary>
    private void OnAddressRefreshClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "点击地址栏刷新按钮：当前没有文件标签，未刷新");
            return;
        }
        UiLogger.Log("程序行为", "点击地址栏刷新按钮：当前目录="
            + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
            + " 结果=已重新加载当前目录");
        tab.Refresh();
    }

    /// <summary>地址栏收藏按钮：把当前文件夹固定到书签。</summary>
    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        FileTabViewModel? tab = viewModel?.SelectedFileTab;
        if (viewModel == null || tab == null)
        {
            UiLogger.Log("导航行为", "点击地址栏收藏按钮：当前没有文件标签，未收藏");
            return;
        }
        if (string.IsNullOrEmpty(tab.CurrentPath))
        {
            UiLogger.Log("导航行为", "点击地址栏收藏按钮：当前在此电脑视图，没有可收藏的路径，未收藏");
            return;
        }
        string pinPath = tab.CurrentPath;
        bool alreadyPinned = viewModel.Settings.PinnedPaths.Contains(pinPath);
        viewModel.PinCurrentTab();
        UiLogger.Log("导航行为", "点击地址栏收藏按钮：路径=" + pinPath
            + " 结果=" + (alreadyPinned ? "该路径已在书签栏中，未重复添加" : "已加入书签栏"));
    }

    /// <summary>最近按钮：打开下拉列表（列表内容绑定 RecentPaths）。</summary>
    private void OnRecentClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("导航行为", "点击最近访问按钮：结果=已展开最近访问下拉列表");
    }

    /// <summary>最近列表选中项：跳转对应路径。</summary>
    private void OnRecentSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is string path)
        {
            FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
            if (tab == null)
            {
                UiLogger.Log("导航行为", "选择最近访问项：目标路径=" + path + " 当前没有文件标签，未跳转");
            }
            else
            {
                tab.OpenPath(path);
                UiLogger.Log("导航行为", "选择最近访问项：目标路径=" + path + " 结果=已在当前标签打开");
            }
            if (this.RecentButton.Flyout is Flyout flyout)
            {
                flyout.Hide();
            }
            list.SelectedItem = null;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab != null)
        {
            string searchText = this.SearchBox.Text ?? string.Empty;
            tab.SearchText = searchText;
            UiLogger.Log("程序行为", "搜索框内容变化：搜索关键字=" + (searchText.Length == 0 ? "空" : searchText)
                + " 结果=" + (searchText.Length == 0 ? "已清除筛选，显示当前目录全部条目" : "已按关键字筛选当前目录列表"));
        }
    }

    /// <summary>关闭当前选中的标签页；最后一个标签关闭时同时关闭窗口。</summary>
    private void CloseCurrentTab()
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "关闭当前标签：当前没有可用的主窗口视图模型，未执行");
            return;
        }
        if (viewModel.SelectedTab == null)
        {
            UiLogger.Log("程序行为", "关闭当前标签：当前没有选中标签，未执行");
            return;
        }
        this.CloseCurrentTab(viewModel.SelectedTab);
    }

    private void CloseCurrentTab(object tab)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.CloseTab(tab);
        if (viewModel.Tabs.Count == 0)
        {
            this.Close();
        }
    }

    /// <summary>对当前文件标签中唯一勾选的条目执行重命名流程。</summary>
    private async Task RenameCheckedAsync()
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "快捷键功能键F2重命名：当前没有文件标签，未执行");
            return;
        }
        FileRow? row = tab.GetSingleCheckedRow();
        if (row == null)
        {
            UiLogger.Log("程序行为", "快捷键功能键F2重命名：当前目录="
                + (string.IsNullOrEmpty(tab.CurrentPath) ? "此电脑" : tab.CurrentPath)
                + " 没有唯一勾选的条目，未执行");
            return;
        }
        string originalName = row.DisplayName;
        string? newName = await InputDialog.Show(this, "重命名", "请输入新的名称：", originalName);
        if (!string.IsNullOrWhiteSpace(newName) && newName != originalName)
        {
            tab.RenameChecked(newName);
            UiLogger.Log("程序行为", "快捷键功能键F2重命名：原名称=" + originalName + " 新名称=" + newName + " 结果=已执行改名（失败时状态栏提示）");
        }
        else
        {
            UiLogger.Log("程序行为", "快捷键功能键F2重命名：原名称=" + originalName + " 未输入新名称或名称未变化，未改名");
        }
    }
}