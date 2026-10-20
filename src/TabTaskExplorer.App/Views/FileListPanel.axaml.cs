using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Views;

/// <summary>文件浏览面板：文件列表、视图切换、信息窗格（导航工具栏与地址栏在主窗口）。</summary>
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

    /// <summary>主窗口视图模型（用于新标签打开、复制剪切到剪贴板）。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get { return TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel; }
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

    private void OnMenuCopyClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel != null && sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            mainViewModel.CopyPathsToClipboard(new List<string> { row.FullPath });
        }
    }

    private void OnMenuCutClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel != null && sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            mainViewModel.CutPathsToClipboard(new List<string> { row.FullPath });
        }
    }

    /// <summary>修改远程文件权限（仅远程连接支持，输入八进制权限如 644）。</summary>
    private async void OnMenuChangePermissionsClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            return;
        }
        if (viewModel.PermissionChanger == null)
        {
            viewModel.ShowMessage("当前标签不是远程连接，无法修改权限");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            string? modeText = await InputDialog.Show(owner, "修改权限", "请输入八进制权限（如 644、755）：", "644");
            if (string.IsNullOrWhiteSpace(modeText))
            {
                return;
            }
            if (!int.TryParse(modeText.Trim(), System.Globalization.NumberStyles.Integer, null, out int octalMode) || octalMode < 0 || octalMode > 511)
            {
                viewModel.ShowMessage("权限格式无效（0~777 八进制）");
                return;
            }
            string targetPath = row.FullPath;
            try
            {
                await System.Threading.Tasks.Task.Run(delegate
                {
                    viewModel.PermissionChanger.ChangePermissions(targetPath, Convert.ToInt32(octalMode.ToString(), 8));
                });
                viewModel.ShowMessage("权限已修改：" + row.DisplayName + " → " + modeText.Trim());
            }
            catch (Exception error)
            {
                viewModel.ShowMessage("修改权限失败：" + error.Message);
            }
        }
    }

    private async void OnMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
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

    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sortField)
        {
            this.ViewModel?.SetSortField(sortField);
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
