using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>磁盘视图面板：展示目录占用统计，双击条目进入子目录。</summary>
public partial class DiskViewPanel : UserControl
{
    public DiskViewPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>刷新按钮：按当前路径重新扫描。</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            viewModel.StartRefresh();
        }
    }

    /// <summary>打开按钮：按输入新路径扫描。</summary>
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

    /// <summary>双击列表条目：目录则进入，文件则打开所在目录。</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel && sender is ListBox listBox)
        {
            DiskRow? row = listBox.SelectedItem as DiskRow;
            viewModel.Open(row);
        }
    }
}
