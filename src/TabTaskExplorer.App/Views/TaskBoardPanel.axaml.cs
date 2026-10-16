using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
            this.AddTaskFromBox();
            e.Handled = true;
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        this.AddTaskFromBox();
    }

    private void OnMoveForwardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is TaskItem task)
        {
            if (task.State == TaskState.Todo)
            {
                this.ViewModel?.MoveTask(task, TaskState.InProgress);
            }
            else if (task.State == TaskState.InProgress)
            {
                this.ViewModel?.MoveTask(task, TaskState.Done);
            }
        }
    }

    private void OnMoveBackClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is TaskItem task)
        {
            if (task.State == TaskState.Done)
            {
                this.ViewModel?.MoveTask(task, TaskState.InProgress);
            }
            else if (task.State == TaskState.InProgress)
            {
                this.ViewModel?.MoveTask(task, TaskState.Todo);
            }
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is TaskItem task)
        {
            this.ViewModel?.DeleteTask(task);
        }
    }

    /// <summary>从输入框创建任务并清空输入。</summary>
    private void AddTaskFromBox()
    {
        TaskBoardViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        string title = this.NewTaskBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }
        viewModel.CreateTask(title);
        this.NewTaskBox.Text = string.Empty;
    }
}
