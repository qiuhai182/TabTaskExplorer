using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>比对面板：目录双栏状态列表与文件左右对齐视图，双击目录行进入、双击文件行新开文件比对。</summary>
public partial class ComparePanel : UserControl
{
    /// <summary>当前已订阅属性变化的视图模型。</summary>
    private CompareTabViewModel? subscribedViewModel;

    public ComparePanel()
    {
        this.InitializeComponent();
    }

    /// <summary>当前比对标签视图模型。</summary>
    private CompareTabViewModel? ViewModel
    {
        get { return this.DataContext as CompareTabViewModel; }
    }

    /// <summary>数据上下文切换时重新订阅视图模型（标签切换后由内容模板重建面板）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        this.AttachViewModelSubscription();
    }

    /// <summary>面板挂载到可视树时订阅视图模型，用于把差异导航结果滚动到可视区域。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        this.AttachViewModelSubscription();
    }

    /// <summary>面板离开可视树时取消订阅，避免标签关闭后仍持有引用。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        this.DetachViewModelSubscription();
    }

    /// <summary>订阅视图模型选中行变化。</summary>
    private void AttachViewModelSubscription()
    {
        this.DetachViewModelSubscription();
        CompareTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        this.subscribedViewModel = viewModel;
        viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
    }

    /// <summary>取消订阅视图模型。</summary>
    private void DetachViewModelSubscription()
    {
        CompareTabViewModel? viewModel = this.subscribedViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        this.subscribedViewModel = null;
    }

    /// <summary>选中行变化时把对应列表滚动到该行。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        CompareTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        if (e.PropertyName == nameof(CompareTabViewModel.SelectedDirectoryRow))
        {
            DirectoryCompareRow? row = viewModel.SelectedDirectoryRow;
            if (row != null)
            {
                this.DirectoryList.ScrollIntoView(row);
            }
        }
        else if (e.PropertyName == nameof(CompareTabViewModel.SelectedFileRow))
        {
            FileDiffRow? row = viewModel.SelectedFileRow;
            if (row != null)
            {
                this.FileList.ScrollIntoView(row);
            }
        }
    }

    /// <summary>开始 / 重新比对当前输入的两侧路径。</summary>
    private void OnCompareClick(object? sender, RoutedEventArgs e)
    {
        CompareTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("比对", "点击开始比对：比对视图模型为空 结果=失败，本次操作取消");
            return;
        }
        viewModel.Reload();
        UiLogger.Log("程序行为", "点击开始比对：左侧路径=" + viewModel.LeftPathInput
            + " 右侧路径=" + viewModel.RightPathInput + " 结果=已提交比对请求");
    }

    /// <summary>交换左右两侧并重新比对。</summary>
    private void OnSwapClick(object? sender, RoutedEventArgs e)
    {
        CompareTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.SwapSides();
    }

    /// <summary>目录比对返回上一层。</summary>
    private void OnNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        CompareTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.NavigateUp();
    }

    /// <summary>选中上一处差异。</summary>
    private void OnPreviousDifferenceClick(object? sender, RoutedEventArgs e)
    {
        CompareTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.SelectPreviousDifference();
        UiLogger.Log("程序行为", "点击上一处差异：当前位置=" + viewModel.DifferencePositionText);
    }

    /// <summary>选中下一处差异。</summary>
    private void OnNextDifferenceClick(object? sender, RoutedEventArgs e)
    {
        CompareTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.SelectNextDifference();
        UiLogger.Log("程序行为", "点击下一处差异：当前位置=" + viewModel.DifferencePositionText);
    }

    /// <summary>双击目录比对行：目录进入下一层，文件新开文件比对标签。</summary>
    private void OnDirectoryRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        CompareTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        Border? border = sender as Border;
        if (border == null || border.DataContext is not DirectoryCompareRow row)
        {
            return;
        }
        viewModel.OpenRow(row);
    }
}
