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
    private bool forceClose;
    private bool ideCloseConfirmed;

    public MainWindow()
    {
        this.InitializeComponent();
        UiLogger.AttachWindow(this);
        UiLogger.Log("程序行为", "主窗口创建完成");
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
            _ = this.ConfirmIdeEditorsAndCloseAsync(viewModel);
            return;
        }
        if (viewModel.Settings.CloseToTray && !this.forceClose)
        {
            e.Cancel = true;
            UiLogger.Log("程序行为", "关闭主窗口：按设置最小化到托盘常驻");
            this.Hide();
            return;
        }
        UiLogger.Log("程序退出", "关闭主窗口：保存会话后退出");
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
                return;
            }
            if (choice == SavePromptChoice.Save)
            {
                editor.Save();
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
            return;
        }

        if (viewModel.IsIdeMode)
        {
            this.HandleIdeKeyDown(viewModel, e);
            return;
        }

        if (e.Key == Key.T && e.KeyModifiers == KeyModifiers.Control)
        {
            viewModel.AddTab(string.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Control)
        {
            this.CloseCurrentTab();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
            {
                viewModel.CycleTab(-1);
            }
            else
            {
                viewModel.CycleTab(1);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            viewModel.SelectedFileTab?.Refresh();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            _ = this.RenameCheckedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            viewModel.SelectedFileTab?.DeleteChecked();
            e.Handled = true;
        }
    }

    /// <summary>IDE 模式快捷键：保存 / 全部保存 / 关闭编辑器 / 快速打开 / 查找，按键组合取自自定义快捷键配置。</summary>
    private void HandleIdeKeyDown(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        IdeShellViewModel shell = viewModel.IdeShell;
        if (this.MatchesHotkey(viewModel, "Save", e))
        {
            shell.SaveActive();
            e.Handled = true;
        }
        else if (this.MatchesHotkey(viewModel, "SaveAll", e))
        {
            shell.SaveAll();
            e.Handled = true;
        }
        else if (this.MatchesHotkey(viewModel, "CloseEditor", e))
        {
            IdeEditorTabViewModel? editor = shell.SelectedEditor;
            if (editor != null)
            {
                _ = this.CloseIdeEditorAsync(shell, editor);
            }
            e.Handled = true;
        }
        else if (this.MatchesHotkey(viewModel, "QuickOpen", e))
        {
            shell.OpenQuickOpen();
            e.Handled = true;
        }
        else if (this.MatchesHotkey(viewModel, "Find", e))
        {
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
                return;
            }
            if (choice == SavePromptChoice.Save)
            {
                editor.Save();
            }
        }
        shell.CloseEditor(editor);
    }

    /// <summary>IDE 模式切换按钮：在资源管理器模式与 IDE 模式之间一键切换。</summary>
    private void OnToggleIdeModeClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.ToggleIdeMode();
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.AddTab(string.Empty);
    }

    private void OnTaskBoardClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.OpenTaskBoardTab();
    }

    private void OnGlobalSearchClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.OpenSearchTab();
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is object tab)
        {
            this.CloseCurrentTab(tab);
        }
    }

    private void OnTabHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null || !viewModel.Settings.DoubleClickTabClose)
        {
            return;
        }
        if (sender is Grid headerGrid && headerGrid.DataContext is object tab)
        {
            this.CloseCurrentTab(tab);
        }
    }

    /// <summary>书签栏点击：在当前文件标签中打开目标路径（无文件标签时自动新建）。</summary>
    private void OnBookmarkItemClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        if (sender is Button button && button.DataContext is QuickAccessItem item)
        {
            this.OpenInFileTab(item.Path);
        }
    }

    /// <summary>书签栏取消固定。</summary>
    private void OnBookmarkUnpinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is QuickAccessItem item)
        {
            this.ViewModel?.Unpin(item.Path);
        }
    }

    /// <summary>徽标点击：回到此电脑。</summary>
    private void OnLogoClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        if (viewModel.SelectedFileTab != null)
        {
            viewModel.SelectedFileTab.OpenPath(string.Empty);
        }
        else
        {
            viewModel.AddTab(string.Empty);
        }
    }

    /// <summary>工具盒子按钮：打开任务看板标签页。</summary>
    private void OnToolboxClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.OpenTaskBoardTab();
    }

    /// <summary>主题切换按钮：深浅色互换并持久化。</summary>
    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.ToggleTheme();
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
            this.ViewModel?.SelectedFileTab?.OpenPath(path);
        }
    }

    /// <summary>面包屑进入编辑模式：显示地址输入框（点击路径区空白即触发）。</summary>
    private void OnBreadcrumbEditClick(object? sender, RoutedEventArgs e)
    {
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
        this.navSyncing = true;
        ListBox other = object.ReferenceEquals(sender, this.PinnedNavList) ? this.PcNavList : this.PinnedNavList;
        other.SelectedItem = null;
        this.navSyncing = false;
        if (sender is ListBox list && list.SelectedItem is QuickAccessItem item)
        {
            this.OpenInFileTab(item.Path);
        }
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
            if (tab != null)
            {
                tab.SetViewMode(mode);
            }
        }
    }

    /// <summary>查看菜单：打开系统文件夹选项对话框。</summary>
    private void OnFolderOptionsClick(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "folders")
        {
            UseShellExecute = true,
        });
    }

    /// <summary>复制菜单：按变体复制勾选条目（文件 / 所在路径 / 完整路径 / 名称 / 带扩展名名称）。</summary>
    private async void OnCopyVariantClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null || sender is not MenuItem item || item.Tag is not string variant)
        {
            return;
        }
        List<FileRow> rows = tab.GetCheckedRows();
        if (rows.Count == 0)
        {
            tab.ShowMessage("请先勾选要复制的条目");
            return;
        }
        if (variant == "files")
        {
            this.ViewModel?.CopyPathsToClipboard(rows.Select(row => row.FullPath).ToList());
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
        }
    }

    /// <summary>工具栏剪切：剪切勾选条目。</summary>
    private void OnToolbarCutClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            return;
        }
        List<FileRow> rows = tab.GetCheckedRows();
        if (rows.Count == 0)
        {
            tab.ShowMessage("请先勾选要剪切的条目");
            return;
        }
        this.ViewModel?.CutPathsToClipboard(rows.Select(row => row.FullPath).ToList());
    }

    /// <summary>工具栏粘贴：粘贴剪贴板内容到当前目录。</summary>
    private void OnToolbarPasteClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.PasteFromClipboard();
    }

    /// <summary>标签右键菜单：新建标签。</summary>
    private void OnTabMenuNewTab(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.AddTab(string.Empty);
    }

    /// <summary>标签右键菜单：复制当前标签（新标签打开同一目录）。</summary>
    private void OnTabMenuDuplicate(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            this.ViewModel?.AddTab(tab.CurrentPath);
        }
    }

    /// <summary>标签右键菜单：把该标签目录添加到书签栏。</summary>
    private void OnTabMenuAddToBookmarks(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            this.ViewModel?.PinPath(tab.CurrentPath);
        }
    }

    /// <summary>标签右键菜单：关闭该标签。</summary>
    private void OnTabMenuClose(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            this.CloseCurrentTab(tab);
        }
    }

    /// <summary>标签右键菜单：关闭其他全部文件标签。</summary>
    private void OnTabMenuCloseOthers(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            this.ViewModel?.CloseOtherTabs(tab);
        }
    }

    /// <summary>标签右键菜单：固定 / 取消固定该标签。</summary>
    private void OnTabMenuTogglePin(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileTabViewModel tab)
        {
            this.ViewModel?.TogglePinTab(tab);
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

    private void OnToolbarDeleteClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.DeleteChecked();
    }

    private void OnToolbarHiddenClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.Settings.ShowHiddenFiles = !viewModel.Settings.ShowHiddenFiles;
        viewModel.SaveSettingsAndApply();
    }

    private void OnToolbarInfoPaneClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab != null)
        {
            tab.ShowInfoPane = !tab.ShowInfoPane;
        }
    }

    private void OnToolbarRefreshClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.Refresh();
    }

    /// <summary>接管 / 恢复默认资源管理器按钮：切换设置并应用。</summary>
    private void OnDefaultExplorerClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.Settings.DefaultFileManager = !viewModel.Settings.DefaultFileManager;
        DefaultFileManagerManager.Apply(viewModel.Settings.DefaultFileManager);
        viewModel.SaveSettingsAndApply();
        this.UpdateDefaultExplorerButtons();
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
        this.ViewModel?.OpenConnectionsTab();
    }

    private void OnDiskViewClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.OpenDiskViewTab();
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

    private void OnProcessMonitorClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        ProcessWindow window = new ProcessWindow();
        window.DataContext = viewModel.CreateProcessPanel();
        window.Show();
    }

    private void OnInfoPaneClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab != null)
        {
            tab.ShowInfoPane = !tab.ShowInfoPane;
        }
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.SelectAll();
    }

    private async void OnBatchRenameClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            return;
        }
        List<FileRow> checkedRows = tab.GetCheckedRows();
        if (checkedRows.Count == 0)
        {
            tab.ShowMessage("请先勾选要改名的条目");
            return;
        }
        BatchRenameDialog dialog = new BatchRenameDialog(checkedRows);
        await dialog.ShowDialog(this);
        if (dialog.RenamePlan.Count > 0)
        {
            tab.ApplyBatchRenames(dialog.RenamePlan);
        }
    }

    private void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
        if (tab == null)
        {
            return;
        }
        _ = this.CreateFolderAsync(tab);
    }

    private async Task CreateFolderAsync(FileTabViewModel tab)
    {
        string? folderName = await InputDialog.Show(this, "新建文件夹", "请输入文件夹名称：", "新建文件夹");
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            tab.CreateFolder(folderName);
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.DeleteChecked();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.Refresh();
    }

    private void OnCloseAllTabsClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.CloseAllTabs();
        if (viewModel.Tabs.Count == 0)
        {
            this.Close();
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.GoBack();
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.GoForward();
    }

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.NavigateUp();
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;
            string? pathText = this.AddressBox.Text;
            if (tab != null && !string.IsNullOrWhiteSpace(pathText))
            {
                tab.OpenPath(pathText.Trim());
            }
            // 回车后退出编辑模式，恢复面包屑显示
            this.ExitAddressEditMode();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Esc 取消编辑，不跳转
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
        this.ViewModel?.SelectedFileTab?.Refresh();
    }

    /// <summary>地址栏收藏按钮：把当前文件夹固定到书签。</summary>
    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.PinCurrentTab();
    }

    /// <summary>最近按钮：打开下拉列表（列表内容绑定 RecentPaths）。</summary>
    private void OnRecentClick(object? sender, RoutedEventArgs e)
    {
    }

    /// <summary>最近列表选中项：跳转对应路径。</summary>
    private void OnRecentSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is string path)
        {
            this.ViewModel?.SelectedFileTab?.OpenPath(path);
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
            tab.SearchText = this.SearchBox.Text ?? string.Empty;
        }
    }

    /// <summary>关闭当前选中的标签页；最后一个标签关闭时同时关闭窗口。</summary>
    private void CloseCurrentTab()
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel != null && viewModel.SelectedTab != null)
        {
            this.CloseCurrentTab(viewModel.SelectedTab);
        }
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
            return;
        }
        FileRow? row = tab.GetSingleCheckedRow();
        if (row == null)
        {
            return;
        }
        string? newName = await InputDialog.Show(this, "重命名", "请输入新的名称：", row.DisplayName);
        if (!string.IsNullOrWhiteSpace(newName) && newName != row.DisplayName)
        {
            tab.RenameChecked(newName);
        }
    }
}
