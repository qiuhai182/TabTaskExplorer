using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>主窗口：书签栏 + 侧边栏 + 动态标签页容器。</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        this.Closing += this.OnWindowClosing;
    }

    /// <summary>主窗口视图模型。</summary>
    private MainWindowViewModel? ViewModel
    {
        get { return this.DataContext as MainWindowViewModel; }
    }

    /// <summary>窗口关闭时保存当前打开的标签页，供下次启动恢复。</summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        this.ViewModel?.SaveOpenTabs();
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
            viewModel.SelectedTab?.Refresh();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            _ = this.RenameCheckedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            viewModel.SelectedTab?.DeleteChecked();
            e.Handled = true;
        }
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.AddTab(string.Empty);
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is FileTabViewModel tab)
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
        if (sender is Grid headerGrid && headerGrid.DataContext is FileTabViewModel tab)
        {
            this.CloseCurrentTab(tab);
        }
    }

    private void OnSidebarItemClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null || viewModel.SelectedTab == null)
        {
            return;
        }
        if (sender is Button button && button.DataContext is QuickAccessItem item)
        {
            viewModel.SelectedTab.OpenPath(item.Path);
        }
    }

    private void OnUnpinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is QuickAccessItem item)
        {
            this.ViewModel?.Unpin(item.Path);
        }
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
        Services.AutoStartManager.Apply(viewModel.Settings.AutoStart);
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

    /// <summary>关闭当前选中的标签页；最后一个标签关闭时同时关闭窗口。</summary>
    private void CloseCurrentTab()
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel != null && viewModel.SelectedTab != null)
        {
            this.CloseCurrentTab(viewModel.SelectedTab);
        }
    }

    private void CloseCurrentTab(FileTabViewModel tab)
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

    /// <summary>对唯一勾选的条目执行重命名流程。</summary>
    private async Task RenameCheckedAsync()
    {
        FileTabViewModel? tab = this.ViewModel?.SelectedTab;
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
