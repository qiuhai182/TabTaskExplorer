namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 任务优先级，取值思路参考 Taskwarrior 的优先级设计。
/// </summary>
public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}

/// <summary>
/// 任务状态：待办、进行中、已完成。
/// </summary>
public enum TaskState
{
    Todo = 0,
    InProgress = 1,
    Done = 2,
}

/// <summary>
/// 任务条目：任务看板的最小数据单元。
/// </summary>
public class TaskItem
{
    /// <summary>任务唯一编号。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>任务标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>任务补充说明。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>任务优先级。</summary>
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    /// <summary>任务状态。</summary>
    public TaskState State { get; set; } = TaskState.Todo;

    /// <summary>截止时间（世界协调时），无截止时间为 null。</summary>
    public DateTime? DueTimeUtc { get; set; }

    /// <summary>创建时间（世界协调时）。</summary>
    public DateTime CreatedTimeUtc { get; set; } = DateTime.UtcNow;

    /// <summary>完成时间（世界协调时），未完成为 null。</summary>
    public DateTime? CompletedTimeUtc { get; set; }

    /// <summary>标签列表，例如：工作、紧急。</summary>
    public List<string> Tags { get; set; } = new List<string>();
}
