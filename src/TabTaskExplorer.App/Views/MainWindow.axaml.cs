using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>主窗口：书签栏 + 侧边栏 + 混合标签页容器（文件浏览 / 任务看板）。</summary>
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

    /// <summary>按键分发：全部快捷键来自设置中的配置。</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }

        // 标签管理类快捷键在任何输入框内都生效
        if (this.Matches("NewTab", e))
        {
            viewModel.AddTab(string.Empty);
            e.Handled = true;
            return;
        }
        if (this.Matches("CloseTab", e))
        {
            this.CloseCurrentTab();
            e.Handled = true;
            return;
        }
        if (this.Matches("PreviousTab", e))
        {
            viewModel.CycleTab(-1);
            e.Handled = true;
            return;
        }
        if (this.Matches("NextTab", e))
        {
            viewModel.CycleTab(1);
            e.Handled = true;
            return;
        }

        // 文件操作类快捷键在输入框内不生效，避免误触
        bool inTextBox = e.Source is TextBox;
        if (inTextBox)
        {
            return;
        }

        if (this.Matches("Refresh", e))
        {
            viewModel.SelectedFileTab?.Refresh();
            e.Handled = true;
            return;
        }
        if (this.Matches("Rename", e))
        {
            _ = this.RenameCheckedAsync();
            e.Handled = true;
            return;
        }
        if (this.Matches("Delete", e))
        {
            viewModel.SelectedFileTab?.DeleteChecked();
            e.Handled = true;
            return;
        }
        if (this.Matches("Copy", e))
        {
            viewModel.CopyCheckedToClipboard();
            e.Handled = true;
            return;
        }
        if (this.Matches("Cut", e))
        {
            viewModel.CutCheckedToClipboard();
            e.Handled = true;
            return;
        }
        if (this.Matches("Paste", e))
        {
            viewModel.PasteFromClipboard();
            e.Handled = true;
        }
    }

    /// <summary>判断当前按键是否命中指定动作的快捷键配置。</summary>
    private bool Matches(string actionName, KeyEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return false;
        }
        foreach (HotkeySetting setting in viewModel.Settings.Hotkeys)
        {
            if (setting.ActionName != actionName)
            {
                continue;
            }
            if (!Enum.TryParse(setting.KeyText, out Key key))
            {
                return false;
            }
            if (e.Key != key)
            {
                return false;
            }
            bool ctrlPressed = (e.KeyModifiers & KeyModifiers.Control) != 0;
            bool altPressed = (e.KeyModifiers & KeyModifiers.Alt) != 0;
            bool shiftPressed = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            return ctrlPressed == setting.Ctrl && altPressed == setting.Alt && shiftPressed == setting.Shift;
        }
        return false;
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

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        SettingsWindow window = new SettingsWindow(viewModel.Settings);
        await window.ShowDialog(this);
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
