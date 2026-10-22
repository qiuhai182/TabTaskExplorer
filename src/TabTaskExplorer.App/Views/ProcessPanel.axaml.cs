using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>进程监控标签面板：资源摘要 + 进程树列表 + 走势图 + 详情与调节 + 刷新 / 结束进程。</summary>
public partial class ProcessPanel : UserControl
{
    public ProcessPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>进程监控标签视图模型。</summary>
    private ProcessPanelViewModel? ViewModel
    {
        get { return this.DataContext as ProcessPanelViewModel; }
    }

    /// <summary>刷新按钮：立即重新采样一次系统资源与进程列表。</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        ProcessPanelViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        UiLogger.Log("进程监控", "界面操作：点击刷新按钮");
        viewModel.StartRefresh();
    }

    /// <summary>结束进程按钮：结束当前选中的进程。</summary>
    private void OnKillClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("进程监控", "界面操作：点击结束进程按钮");
        this.ViewModel?.TerminateSelected();
    }

    /// <summary>优先级"应用"按钮：把下拉框当前档位应用到选中进程。</summary>
    private void OnApplyPriorityClick(object? sender, RoutedEventArgs e)
    {
        ProcessPanelViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        int priorityIndex = this.PriorityBox.SelectedIndex;
        UiLogger.Log("进程监控", "界面操作：点击优先级应用按钮 目标进程=" + ProcessPanelViewModel.DescribeSelected(viewModel)
            + " 档位下标=" + priorityIndex);
        viewModel.ApplyPrioritySelected(priorityIndex);
    }

    /// <summary>CPU 亲和性"应用"按钮：把输入框中的核心编号列表应用到选中进程。</summary>
    private void OnApplyAffinityClick(object? sender, RoutedEventArgs e)
    {
        ProcessPanelViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        string processorIndexesText = this.AffinityBox.Text ?? string.Empty;
        UiLogger.Log("进程监控", "界面操作：点击处理器亲和性应用按钮 目标进程=" + ProcessPanelViewModel.DescribeSelected(viewModel)
            + " 核心编号输入=" + processorIndexesText);
        viewModel.ApplyAffinitySelected(processorIndexesText);
    }

    /// <summary>点击表头：按该列排序（重复点击同一列切换升序降序），随后刷新表头箭头。</summary>
    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        ProcessPanelViewModel? viewModel = this.ViewModel;
        if (viewModel == null || sender is not Button button || button.Tag is not string sortField)
        {
            return;
        }
        UiLogger.Log("进程监控", "界面操作：点击表头排序按钮 表头=" + sortField);
        viewModel.SetSortField(sortField);
        this.UpdateSortArrows();
    }

    /// <summary>按当前排序状态刷新五个表头的升序 / 降序箭头（未排序的列不显示箭头）。</summary>
    private void UpdateSortArrows()
    {
        ProcessPanelViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        string field = viewModel.SortField;
        string arrow = viewModel.SortAscending ? "▲" : "▼";
        this.ArrowCpu.Text = field == "Cpu" ? arrow : string.Empty;
        this.ArrowName.Text = field == "Name" ? arrow : string.Empty;
        this.ArrowMemory.Text = field == "Memory" ? arrow : string.Empty;
        this.ArrowProcessId.Text = field == "ProcessId" ? arrow : string.Empty;
        this.ArrowWindowTitle.Text = field == "WindowTitle" ? arrow : string.Empty;
    }
}
