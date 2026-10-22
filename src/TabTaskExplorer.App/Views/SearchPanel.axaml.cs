using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 全局搜索面板：输入关键字后执行全盘文件名搜索，结果以虚拟化列表展示。
/// </summary>
public partial class SearchPanel : UserControl
{
    public SearchPanel()
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
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
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
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
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

    /// <summary>右键菜单"复制路径"：把该结果路径复制到剪贴板。</summary>
    private void OnMenuCopyPathClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单复制路径：未找到主窗口视图模型 结果=失败，本次复制取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            mainViewModel.CopyPathsToClipboard(new List<string> { row.FullPath });
            UiLogger.Log("程序行为", "右键菜单复制路径：行路径=" + row.FullPath
                + " 结果=成功，已把该路径放入剪贴板，粘贴时生效");
        }
    }

    /// <summary>打开结果路径的公共逻辑：目录进入文件夹视图（新标签或当前标签），文件用系统默认程序打开。</summary>
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

    /// <summary>点击搜索按钮执行搜索：先记录触发与关键字日志，再调用视图模型搜索。</summary>
    private void OnSearchButtonClick(object? sender, RoutedEventArgs e)
    {
        this.LogSearchTrigger("点击搜索按钮");
        this.RunSearch();
    }

    /// <summary>搜索框内按回车执行搜索。</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            this.LogSearchTrigger("搜索框回车提交");
            this.RunSearch();
            e.Handled = true;
        }
    }

    /// <summary>调用标签页视图模型的搜索方法。</summary>
    private void RunSearch()
    {
        if (this.DataContext is SearchTabViewModel viewModel)
        {
            viewModel.ExecuteSearch();
        }
    }

    /// <summary>记录一次搜索触发日志：触发控件、搜索关键字与本次是否真正发起搜索。</summary>
    private void LogSearchTrigger(string triggerName)
    {
        if (this.DataContext is not SearchTabViewModel viewModel)
        {
            UiLogger.Log("程序行为", triggerName + "：未找到搜索视图模型 结果=失败，本次搜索取消");
            return;
        }
        string keyword = viewModel.QueryText == null ? string.Empty : viewModel.QueryText.Trim();
        string resultText;
        if (keyword.Length == 0)
        {
            resultText = "失败，搜索关键字为空，未发起搜索";
        }
        else if (viewModel.IsSearching)
        {
            resultText = "失败，已有搜索正在进行，未发起新搜索";
        }
        else
        {
            resultText = "成功，已发起搜索";
        }
        UiLogger.Log("程序行为", triggerName + "：搜索关键字=" + (keyword.Length == 0 ? "（空）" : keyword)
            + " 结果=" + resultText);
    }

    /// <summary>点击表头：按该列排序（重复点击同一列切换升序降序），随后刷新表头箭头。</summary>
    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not SearchTabViewModel viewModel || sender is not Button button || button.Tag is not string sortField)
        {
            return;
        }
        UiLogger.Log("全局搜索", "界面操作：点击表头排序按钮 表头=" + sortField);
        viewModel.SetSortField(sortField);
        this.UpdateSortArrows();
    }

    /// <summary>按当前排序状态刷新三个表头的升序 / 降序箭头（未排序的列不显示箭头）。</summary>
    private void UpdateSortArrows()
    {
        if (this.DataContext is not SearchTabViewModel viewModel)
        {
            return;
        }
        string field = viewModel.SortField;
        string arrow = viewModel.SortAscending ? "▲" : "▼";
        this.ArrowName.Text = field == "Name" ? arrow : string.Empty;
        this.ArrowModified.Text = field == "Modified" ? arrow : string.Empty;
        this.ArrowSize.Text = field == "Size" ? arrow : string.Empty;
    }
}
