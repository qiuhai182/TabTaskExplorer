using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    /// <summary>点击搜索按钮执行搜索。</summary>
    private void OnSearchButtonClick(object? sender, RoutedEventArgs e)
    {
        this.RunSearch();
    }

    /// <summary>搜索框内按回车执行搜索。</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
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
}
