using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Views;

/// <summary>任务看板面板：待办、进行中、已完成三列。</summary>
public partial class TaskBoardPanel : UserControl
{
    public TaskBoardPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>任务看板视图模型。</summary>
    private TaskBoardViewModel? ViewModel
    {
        get { return this.DataContext as TaskBoardViewModel; }
    }

    private void OnNewTaskKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UiLogger.Log("程序行为", "按键=回车（新建任务输入框）：任务标题=" + (this.NewTaskBox.Text ?? string.Empty)
                + " 结果=已提交创建任务请求");
            this.AddTaskFromBox();
            e.Handled = true;
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("程序行为", "按钮=添加任务 被点击：任务标题=" + (this.NewTaskBox.Text ?? string.Empty)
            + " 结果=已提交创建任务请求");
        this.AddTaskFromBox();
    }

    private void OnMoveForwardClick(object? sender, RoutedEventArgs e)
    {
        TaskBoardViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=任务前进 被点击：任务看板视图模型未就绪，结果=失败，未移动任何任务");
            return;
        }
        if (sender is Button button && button.DataContext is TaskItem task)
        {
            if (task.State == TaskState.Todo)
            {
                viewModel.MoveTask(task, TaskState.InProgress);
                UiLogger.Log("程序行为", "按钮=任务前进 被点击：任务标题=" + task.Title + " 当前状态=待办"
                    + " 结果=成功，任务已移动到进行中列并保存到存储");
            }
            else if (task.State == TaskState.InProgress)
            {
                viewModel.MoveTask(task, TaskState.Done);
                UiLogger.Log("程序行为", "按钮=任务前进 被点击：任务标题=" + task.Title + " 当前状态=进行中"
                    + " 结果=成功，任务已移动到已完成列并记录完成时间，已保存到存储");
            }
        }
        else
        {
            UiLogger.Log("程序行为", "按钮=任务前进 被点击：触发源不是任务卡片按钮或未绑定任务数据，结果=失败，未移动任何任务");
        }
    }

    private void OnMoveBackClick(object? sender, RoutedEventArgs e)
    {
        TaskBoardViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=任务后退 被点击：任务看板视图模型未就绪，结果=失败，未移动任何任务");
            return;
        }
        if (sender is Button button && button.DataContext is TaskItem task)
        {
            if (task.State == TaskState.Done)
            {
                viewModel.MoveTask(task, TaskState.InProgress);
                UiLogger.Log("程序行为", "按钮=任务后退 被点击：任务标题=" + task.Title + " 当前状态=已完成"
                    + " 结果=成功，任务已重新打开并移动到进行中列，完成时间已清除，已保存到存储");
            }
            else if (task.State == TaskState.InProgress)
            {
                viewModel.MoveTask(task, TaskState.Todo);
                UiLogger.Log("程序行为", "按钮=任务后退 被点击：任务标题=" + task.Title + " 当前状态=进行中"
                    + " 结果=成功，任务已退回待办列并保存到存储");
            }
        }
        else
        {
            UiLogger.Log("程序行为", "按钮=任务后退 被点击：触发源不是任务卡片按钮或未绑定任务数据，结果=失败，未移动任何任务");
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        TaskBoardViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=删除任务 被点击：任务看板视图模型未就绪，结果=失败，未删除任何任务");
            return;
        }
        if (sender is Button button && button.DataContext is TaskItem task)
        {
            viewModel.DeleteTask(task);
            UiLogger.Log("程序行为", "按钮=删除任务 被点击：任务标题=" + task.Title
                + " 结果=成功，任务已从看板删除并保存到存储");
        }
        else
        {
            UiLogger.Log("程序行为", "按钮=删除任务 被点击：触发源不是任务卡片按钮或未绑定任务数据，结果=失败，未删除任何任务");
        }
    }

    /// <summary>从输入框创建任务并清空输入。</summary>
    private void AddTaskFromBox()
    {
        TaskBoardViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "创建任务执行结果：触发方式=新建任务输入框回车或添加任务按钮"
                + " 结果=失败，任务看板视图模型未就绪，未创建任何任务");
            return;
        }
        string title = this.NewTaskBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            UiLogger.Log("程序行为", "创建任务执行结果：触发方式=新建任务输入框回车或添加任务按钮 任务标题=（空白）"
                + " 结果=失败，任务标题不能为空，未创建任何任务");
            return;
        }
        viewModel.CreateTask(title);
        this.NewTaskBox.Text = string.Empty;
        UiLogger.Log("程序行为", "创建任务执行结果：触发方式=新建任务输入框回车或添加任务按钮 任务标题=" + title.Trim()
            + " 结果=成功，任务已加入待办列并保存到存储，输入框已清空");
    }
}
