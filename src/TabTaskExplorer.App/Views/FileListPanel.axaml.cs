using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Views;

/// <summary>文件浏览面板：导航工具栏、地址栏、搜索框、文件列表、视图切换与信息窗格。</summary>
public partial class FileListPanel : UserControl
{
    public FileListPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>当前标签页视图模型。</summary>
    private FileTabViewModel? ViewModel
    {
        get { return this.DataContext as FileTabViewModel; }
    }

    /// <summary>主窗口视图模型（用于固定快捷访问、打开进程监控、新标签打开）。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get
        {
            if (this.GetVisualRoot() is Window ownerWindow)
            {
                return ownerWindow.DataContext as MainWindowViewModel;
            }
            return null;
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.GoBack();
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.GoForward();
    }

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.NavigateUp();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.Refresh();
    }

    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        this.MainViewModel?.PinCurrentTab();
    }

    private void OnViewModeClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.CycleViewMode();
    }

    private void OnInfoPaneClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel != null)
        {
            viewModel.ShowInfoPane = !viewModel.ShowInfoPane;
            this.UpdateInfoPane();
        }
    }

    private void OnProcessClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            return;
        }
        ProcessWindow window = new ProcessWindow();
        window.DataContext = mainViewModel.CreateProcessPanel();
        window.Show();
    }

    private async void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        Window? owner = this.GetVisualRoot() as Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            return;
        }
        string? folderName = await InputDialog.Show(owner, "新建文件夹", "请输入文件夹名称：", "新建文件夹");
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            viewModel.CreateFolder(folderName);
        }
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        Window? owner = this.GetVisualRoot() as Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            return;
        }
        FileRow? row = viewModel.GetSingleCheckedRow();
        if (row == null)
        {
            return;
        }
        string? newName = await InputDialog.Show(owner, "重命名", "请输入新的名称：", row.DisplayName);
        if (!string.IsNullOrWhiteSpace(newName) && newName != row.DisplayName)
        {
            viewModel.RenameChecked(newName);
        }
    }

    private async void OnBatchRenameClick(object? sender, RoutedEventArgs e)
    {
        Window? owner = this.GetVisualRoot() as Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            return;
        }
        List<FileRow> checkedRows = viewModel.GetCheckedRows();
        if (checkedRows.Count == 0)
        {
            viewModel.ShowMessage("请先勾选要改名的条目");
            return;
        }
        BatchRenameDialog dialog = new BatchRenameDialog(checkedRows);
        await dialog.ShowDialog(owner);
        if (dialog.RenamePlan.Count > 0)
        {
            viewModel.ApplyBatchRenames(dialog.RenamePlan);
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.DeleteChecked();
    }

    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sortField)
        {
            this.ViewModel?.SetSortField(sortField);
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel != null)
        {
            viewModel.SearchText = this.SearchBox.Text ?? string.Empty;
        }
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FileTabViewModel? viewModel = this.ViewModel;
            string? pathText = this.AddressBox.Text;
            if (viewModel != null && !string.IsNullOrWhiteSpace(pathText))
            {
                viewModel.OpenPath(pathText.Trim());
            }
            e.Handled = true;
        }
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is FileRow row)
        {
            this.ViewModel?.OpenEntry(row);
            e.Handled = true;
        }
    }

    /// <summary>双击列表空白处返回上一级。</summary>
    private void OnListBackgroundDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }
        this.ViewModel?.NavigateUp();
        e.Handled = true;
    }

    private void OnRowCheckChanged(object? sender, RoutedEventArgs e)
    {
        this.UpdateInfoPane();
    }

    private void OnMenuOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            this.ViewModel?.OpenEntry(row);
        }
    }

    private void OnMenuOpenInNewTabClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        FileTabViewModel? viewModel = this.ViewModel;
        if (mainViewModel == null || viewModel == null)
        {
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            if (row.IsDirectory)
            {
                mainViewModel.AddTab(row.FullPath);
            }
            else
            {
                viewModel.OpenEntry(row);
            }
        }
    }

    private async void OnMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        Window? owner = this.GetVisualRoot() as Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            string? newName = await InputDialog.Show(owner, "重命名", "请输入新的名称：", row.DisplayName);
            if (!string.IsNullOrWhiteSpace(newName) && newName != row.DisplayName)
            {
                viewModel.RenameRow(row, newName);
            }
        }
    }

    private void OnMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            this.ViewModel?.DeleteRow(row);
        }
    }

    /// <summary>刷新右侧信息窗格内容。</summary>
    private void UpdateInfoPane()
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        FileRow? row = viewModel.FindSingleCheckedRow();
        if (row == null)
        {
            this.InfoNameText.Text = "未勾选条目";
            this.InfoTypeText.Text = string.Empty;
            this.InfoSizeText.Text = string.Empty;
            this.InfoModifiedText.Text = string.Empty;
            this.InfoPathText.Text = string.Empty;
            return;
        }
        this.InfoNameText.Text = row.DisplayName;
        this.InfoTypeText.Text = row.TypeText;
        this.InfoSizeText.Text = row.SizeText;
        this.InfoModifiedText.Text = row.ModifiedText;
        this.InfoPathText.Text = row.FullPath;
    }
}
