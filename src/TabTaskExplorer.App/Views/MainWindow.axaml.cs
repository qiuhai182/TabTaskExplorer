using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>主窗口：自绘标题栏 + 标签条 + 工具栏 + 侧边栏，支持最小化到托盘常驻。</summary>
public partial class MainWindow : Window
{
    private readonly List<ListBoxItem> tabHooked = new List<ListBoxItem>();
    private bool forceClose;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Closing += this.OnWindowClosing;
        this.DataContextChanged += this.OnDataContextChanged;
    }

    /// <summary>主窗口视图模型。</summary>
    private MainWindowViewModel? ViewModel
    {
        get { return this.DataContext as MainWindowViewModel; }
    }

    /// <summary>窗口关闭时：默认最小化到托盘常驻；托盘菜单选择退出时才真正关闭并保存会话。</summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel != null && viewModel.Settings.CloseToTray && !this.forceClose)
        {
            e.Cancel = true;
            this.Hide();
            return;
        }
        viewModel?.SaveOpenTabs();
    }

    /// <summary>托盘菜单"退出"调用：跳过托盘常驻，真正关闭窗口。</summary>
    public void ForceClose()
    {
        this.forceClose = true;
        this.Close();
    }

    /// <summary>DataContext 就绪后订阅视图模型变化，驱动标签条高亮。</summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
        viewModel.Tabs.CollectionChanged -= this.OnTabsCollectionChanged;
        viewModel.Tabs.CollectionChanged += this.OnTabsCollectionChanged;
        this.UpdateTabStripHighlights();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
        {
            this.UpdateTabStripHighlights();
        }
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.UpdateTabStripHighlights();
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
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
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
    }

    private void OnCloseWindowClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    /// <summary>全局快捷键：Ctrl+T、Ctrl+W、Ctrl+Tab、F5、F2、Delete。</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
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

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.AddTab(string.Empty);
    }

    private void OnTaskBoardClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.OpenTaskBoardTab();
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

    private void OnSidebarItemClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null || viewModel.SelectedFileTab == null)
        {
            return;
        }
        if (sender is Button button && button.DataContext is QuickAccessItem item)
        {
            viewModel.SelectedFileTab.OpenPath(item.Path);
        }
    }

    private void OnUnpinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is QuickAccessItem item)
        {
            this.ViewModel?.Unpin(item.Path);
        }
    }

    private void OnNetworkClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.ShowMessage("网络位置功能即将支持");
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        SettingsWindow window = new SettingsWindow(viewModel.Settings);
        await window.ShowDialog(this);
        App current = (App)App.Current!;
        current.SyncTrayAutoStartState();
        AutoStartManager.Apply(viewModel.Settings.AutoStart);
        DefaultFileManagerManager.Apply(viewModel.Settings.DefaultFileManager);
        viewModel.SaveSettingsAndApply();
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

    private void OnViewModeClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SelectedFileTab?.CycleViewMode();
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
            e.Handled = true;
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
