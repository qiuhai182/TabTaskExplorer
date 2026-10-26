using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>磁盘视图面板：展示目录占用统计，单击文件夹原地展开或收起（不进入下一级）。</summary>
public partial class DiskViewPanel : UserControl
{
    public DiskViewPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>主窗口视图模型（用于新标签打开、复制路径到剪贴板）。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get { return TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel; }
    }

    /// <summary>右键菜单"打开"：文件夹进入该路径，文件用系统默认程序打开。</summary>
    private void OnMenuOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is DiskRow row)
        {
            if (this.MainViewModel == null)
            {
                UiLogger.Log("程序行为", "右键菜单打开：未找到主窗口视图模型 结果=失败，未打开该条目");
                return;
            }
            this.OpenRowPath(row.FullPath, row.IsDirectory, false);
            UiLogger.Log("程序行为", "右键菜单打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已进入该目录" : "已调用系统默认程序打开该文件"));
        }
    }

    /// <summary>右键菜单"在新标签打开"：在新标签页打开该路径（文件则用系统默认程序打开）。</summary>
    private void OnMenuOpenInNewTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is DiskRow row)
        {
            if (this.MainViewModel == null)
            {
                UiLogger.Log("程序行为", "右键菜单在新标签打开：未找到主窗口视图模型 结果=失败，未打开该条目");
                return;
            }
            this.OpenRowPath(row.FullPath, row.IsDirectory, true);
            UiLogger.Log("程序行为", "右键菜单在新标签打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已新建标签打开该目录" : "已调用系统默认程序打开该文件"));
        }
    }

    /// <summary>右键菜单"复制路径"：把该行路径复制到剪贴板。</summary>
    private void OnMenuCopyPathClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单复制路径：未找到主窗口视图模型 结果=失败，本次复制取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is DiskRow row)
        {
            mainViewModel.CopyPathsToClipboard(new List<string> { row.FullPath });
            UiLogger.Log("程序行为", "右键菜单复制路径：行路径=" + row.FullPath
                + " 结果=成功，已把该路径放入剪贴板，粘贴时生效");
        }
    }

    /// <summary>右键菜单"粘贴"：文件夹条目粘贴进该文件夹，文件条目粘贴进它所在的文件夹。</summary>
    private async void OnMenuPasteClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴：未找到主窗口视图模型 结果=失败，本次粘贴取消");
            return;
        }
        if (sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴：菜单项未关联磁盘行数据 结果=失败，本次粘贴取消");
            return;
        }
        string targetDirectory = row.IsDirectory
            ? row.FullPath
            : (Path.GetDirectoryName(row.FullPath) ?? string.Empty);
        if (targetDirectory.Length == 0)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴：行路径=" + row.FullPath + " 目标目录=未取到 结果=失败，本次粘贴取消");
            return;
        }
        await mainViewModel.PasteFromClipboardAsync(targetDirectory);
        UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
            + " 目标目录=" + targetDirectory + " 结果=已请求把剪贴板条目粘贴到该目录（剪贴板为空或目录不可写时由界面提示）");
    }

    /// <summary>打开行路径的公共逻辑：目录进入文件夹视图（新标签或当前标签），文件用系统默认程序打开。</summary>
    private void OpenRowPath(string path, bool isDirectory, bool inNewTab)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            return;
        }
        if (isDirectory)
        {
            if (inNewTab || mainViewModel.SelectedFileTab == null)
            {
                mainViewModel.AddTab(path);
            }
            else
            {
                mainViewModel.SelectedFileTab.OpenPath(path);
            }
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiLogger.LogError("程序行为", "调用系统默认程序打开文件：行路径=" + path + " 结果=失败", ex);
        }
    }

    /// <summary>刷新按钮：完全重扫当前路径（忽略快照）。</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            string currentRoot = viewModel.RootPathInput == null ? string.Empty : viewModel.RootPathInput;
            viewModel.StartFullRefresh();
            UiLogger.Log("程序行为", "点击刷新按钮：当前根目录=" + (currentRoot.Length == 0 ? "此电脑" : currentRoot)
                + " 结果=成功，已请求完全重扫该根目录");
        }
        else
        {
            UiLogger.Log("程序行为", "点击刷新按钮：未找到磁盘视图模型 结果=失败，本次刷新取消");
        }
    }

    /// <summary>打开按钮：按输入框路径增量扫描。</summary>
    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            string targetRoot = viewModel.RootPathInput == null ? string.Empty : viewModel.RootPathInput;
            viewModel.StartRefresh(viewModel.RootPathInput);
            UiLogger.Log("程序行为", "点击打开按钮：路径输入框内容=" + (targetRoot.Length == 0 ? "（空）" : targetRoot)
                + " 结果=" + (targetRoot.Length == 0 ? "成功，已请求加载此电脑视图" : "成功，已请求按该路径扫描"));
        }
        else
        {
            UiLogger.Log("程序行为", "点击打开按钮：未找到磁盘视图模型 结果=失败，本次打开取消");
        }
    }

    /// <summary>上一级按钮：回到上级目录。</summary>
    private void OnNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            string originalRoot = viewModel.RootPathInput == null ? string.Empty : viewModel.RootPathInput;
            viewModel.NavigateUp();
            string newRoot = viewModel.RootPathInput == null ? string.Empty : viewModel.RootPathInput;
            UiLogger.Log("程序行为", "点击上一级按钮：原根目录=" + (originalRoot.Length == 0 ? "此电脑" : originalRoot)
                + " 新根目录=" + (newRoot.Length == 0 ? "此电脑" : newRoot)
                + " 结果=成功，已请求返回上一级根目录并刷新视图");
        }
        else
        {
            UiLogger.Log("程序行为", "点击上一级按钮：未找到磁盘视图模型 结果=失败，本次返回上级取消");
        }
    }

    /// <summary>单击列表条目：文件夹原地展开或收起，文件忽略。</summary>
    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (this.DataContext is not DiskViewPanelViewModel viewModel || e.Source is not Control control)
        {
            UiLogger.Log("程序行为", "点击列表条目：视图模型或事件来源缺失 结果=已忽略，未展开或收起");
            return;
        }
        DiskRow? row = control.DataContext as DiskRow;
        if (row == null)
        {
            UiLogger.Log("程序行为", "点击列表条目：未关联磁盘行数据 结果=已忽略，未展开或收起");
            return;
        }
        if (!row.IsDirectory)
        {
            UiLogger.Log("程序行为", "点击文件条目：行路径=" + row.FullPath + " 结果=已忽略，文件不参与展开或收起");
            return;
        }
        UiLogger.Log("程序行为", "点击目录条目：行路径=" + row.FullPath + " 结果=已请求切换该目录的展开或收起状态");
        viewModel.ToggleRow(row);
    }

    /// <summary>点击表头：按该列排序（重复点击同一列切换升序降序），随后刷新表头箭头。</summary>
    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not DiskViewPanelViewModel viewModel || sender is not Button button || button.Tag is not string sortField)
        {
            return;
        }
        UiLogger.Log("磁盘视图", "界面操作：点击表头排序按钮 表头=" + sortField);
        viewModel.SetSortField(sortField);
        this.UpdateSortArrows();
    }

    /// <summary>按当前排序状态刷新六个表头的升序 / 降序箭头（未排序的列不显示箭头）。</summary>
    private void UpdateSortArrows()
    {
        if (this.DataContext is not DiskViewPanelViewModel viewModel)
        {
            return;
        }
        string field = viewModel.SortField;
        string arrow = viewModel.SortAscending ? "▲" : "▼";
        this.ArrowName.Text = field == "Name" ? arrow : string.Empty;
        this.ArrowType.Text = field == "Type" ? arrow : string.Empty;
        this.ArrowSize.Text = field == "Size" ? arrow : string.Empty;
        this.ArrowFree.Text = field == "Free" ? arrow : string.Empty;
        this.ArrowUsage.Text = field == "Usage" ? arrow : string.Empty;
        this.ArrowFileSystem.Text = field == "FileSystem" ? arrow : string.Empty;
    }
}
