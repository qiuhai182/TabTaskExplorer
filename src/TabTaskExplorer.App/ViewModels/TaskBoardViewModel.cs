using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 任务看板：待办、进行中、已完成三列，数据经内核 ITaskStore 持久化。
/// </summary>
public class TaskBoardViewModel : INotifyPropertyChanged
{
    private readonly ITaskStore taskStore;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>标签标题。</summary>
    public string Title { get; private set; } = "任务看板";

    /// <summary>待办列。</summary>
    public ObservableCollection<TaskItem> TodoItems { get; } = new ObservableCollection<TaskItem>();

    /// <summary>进行中列。</summary>
    public ObservableCollection<TaskItem> InProgressItems { get; } = new ObservableCollection<TaskItem>();

    /// <summary>已完成列。</summary>
    public ObservableCollection<TaskItem> DoneItems { get; } = new ObservableCollection<TaskItem>();

    public TaskBoardViewModel(ITaskStore taskStore)
    {
        this.taskStore = taskStore;
        this.Reload();
    }

    /// <summary>从存储重载全部任务到三列。</summary>
    public void Reload()
    {
        this.TodoItems.Clear();
        this.InProgressItems.Clear();
        this.DoneItems.Clear();
        foreach (TaskItem task in this.taskStore.LoadAll())
        {
            if (task.State == TaskState.Todo)
            {
                this.TodoItems.Add(task);
            }
            else if (task.State == TaskState.InProgress)
            {
                this.InProgressItems.Add(task);
            }
            else
            {
                this.DoneItems.Add(task);
            }
        }
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
    }

    /// <summary>新建任务（进入待办列）。</summary>
    public void CreateTask(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }
        TaskItem task = new TaskItem();
        task.Title = title.Trim();
        List<TaskItem> all = this.CollectAllTasks();
        all.Add(task);
        this.taskStore.SaveAll(all);
        this.Reload();
    }

    /// <summary>把任务移动到指定状态并持久化。</summary>
    public void MoveTask(TaskItem task, TaskState newState)
    {
        task.State = newState;
        if (newState == TaskState.Done)
        {
            task.CompletedTimeUtc = DateTime.UtcNow;
        }
        else
        {
            task.CompletedTimeUtc = null;
        }
        this.taskStore.SaveAll(this.CollectAllTasks());
        this.Reload();
    }

    /// <summary>删除任务并持久化。</summary>
    public void DeleteTask(TaskItem task)
    {
        List<TaskItem> remaining = this.CollectAllTasks();
        remaining.Remove(task);
        this.taskStore.SaveAll(remaining);
        this.Reload();
    }

    /// <summary>汇总三列全部任务；extra 会在汇总前加入。</summary>
    private List<TaskItem> CollectAllTasks(TaskItem? extra = null)
    {
        List<TaskItem> all = new List<TaskItem>();
        all.AddRange(this.TodoItems);
        all.AddRange(this.InProgressItems);
        all.AddRange(this.DoneItems);
        if (extra != null)
        {
            all.Add(extra);
        }
        return all;
    }
}
