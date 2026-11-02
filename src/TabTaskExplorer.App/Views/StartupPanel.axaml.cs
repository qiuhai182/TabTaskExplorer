using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>启动项管理面板：系统启动项表格、自定义启动项列表与编辑表单。</summary>
public partial class StartupPanel : UserControl
{
    public StartupPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>启动项管理标签视图模型。</summary>
    private StartupManagerViewModel? ViewModel
    {
        get { return this.DataContext as StartupManagerViewModel; }
    }

    /// <summary>刷新按钮：重新读取系统启动项与自定义启动项状态。</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("启动项", "界面操作：点击刷新按钮");
        StartupManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.Refresh();
        viewModel.RefreshCustomItems();
    }

    /// <summary>启用 / 禁用当前选中的系统启动项。</summary>
    private void OnToggleSystemClick(object? sender, RoutedEventArgs e)
    {
        StartupRow? row = this.ViewModel == null ? null : this.ViewModel.SelectedSystemItem;
        UiLogger.Log("启动项", "界面操作：点击启用 / 禁用系统启动项按钮 选中名称="
            + (row == null ? "无" : row.Name));
        this.ViewModel?.ToggleSelectedSystem();
    }

    /// <summary>列表行内的启用 / 禁用按钮：直接操作该行对应的启动项。</summary>
    private void OnRowToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not StartupRow row)
        {
            UiLogger.Log("启动项", "界面操作：行内启停按钮来源无法识别，已忽略");
            return;
        }
        UiLogger.Log("启动项", "界面操作：点击行内启停按钮 名称=" + row.Name + " 来源=" + row.SourceText
            + " 目标状态=" + (row.IsEnabled ? "禁用" : "启用"));
        this.ViewModel?.ToggleSystem(row);
    }

    /// <summary>新建自定义启动项：清空编辑表单。</summary>
    private void OnNewCustomClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("启动项", "界面操作：点击新建自定义启动项按钮");
        this.ViewModel?.NewCustom();
    }

    /// <summary>保存编辑表单中的自定义启动项。</summary>
    private void OnSaveCustomClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("启动项", "界面操作：点击保存自定义启动项按钮");
        this.ViewModel?.SaveCustom();
    }

    /// <summary>删除当前选中的自定义启动项。</summary>
    private void OnDeleteCustomClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("启动项", "界面操作：点击删除自定义启动项按钮");
        this.ViewModel?.DeleteSelectedCustom();
    }

    /// <summary>立即运行当前选中的自定义启动项。</summary>
    private void OnRunCustomClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("启动项", "界面操作：点击立即运行自定义启动项按钮");
        this.ViewModel?.RunSelectedCustom();
    }

    /// <summary>切换当前选中自定义启动项的启用状态。</summary>
    private void OnToggleCustomClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("启动项", "界面操作：点击启用 / 禁用自定义启动项按钮");
        this.ViewModel?.ToggleSelectedCustom();
    }

    /// <summary>浏览并选择启动目标文件。</summary>
    private async void OnBrowseTargetClick(object? sender, RoutedEventArgs e)
    {
        StartupManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            UiLogger.Log("启动项", "界面操作：浏览启动目标失败，当前控件没有顶级窗口");
            return;
        }
        FilePickerOpenOptions options = new FilePickerOpenOptions();
        options.Title = "选择启动目标";
        options.AllowMultiple = false;
        try
        {
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0)
            {
                UiLogger.Log("启动项", "界面操作：浏览启动目标已取消");
                return;
            }
            string path = files[0].TryGetLocalPath() ?? files[0].Path.ToString();
            viewModel.EditTarget = path;
            UiLogger.Log("启动项", "界面操作：浏览启动目标已选择 路径=" + path);
        }
        catch (Exception error)
        {
            UiLogger.LogError("启动项", "界面操作：浏览启动目标失败", error);
        }
    }
}
