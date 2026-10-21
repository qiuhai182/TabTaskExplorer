using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>磁盘视图面板：展示目录占用统计，单击文件夹原地展开或收起（不进入下一级）。</summary>
public partial class DiskViewPanel : UserControl
{
    public DiskViewPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>刷新按钮：完全重扫当前路径（忽略快照）。</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            viewModel.StartFullRefresh();
        }
    }

    /// <summary>打开按钮：按输入框路径增量扫描。</summary>
    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            viewModel.StartRefresh(viewModel.RootPathInput);
        }
    }

    /// <summary>上一级按钮：回到上级目录。</summary>
    private void OnNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            viewModel.NavigateUp();
        }
    }

    /// <summary>单击列表条目：文件夹原地展开或收起，文件忽略。</summary>
    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel && e.Source is Control control)
        {
            DiskRow? row = control.DataContext as DiskRow;
            viewModel.ToggleRow(row);
        }
    }
}
