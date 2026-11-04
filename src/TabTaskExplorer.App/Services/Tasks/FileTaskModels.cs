using System;
using System.Collections.Generic;

namespace TabTaskExplorer.App.Services.Tasks;

/// <summary>统一任务类型：本轮只覆盖本地复制、移动、删除。</summary>
public enum FileTaskKind
{
    Copy = 0,
    Move = 1,
    Delete = 2,
}

/// <summary>任务状态。</summary>
public enum FileTaskState
{
    Queued = 0,
    Running = 1,
    WaitingForConflict = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5,
    Paused = 6,
}

/// <summary>目标已存在时的处理策略。</summary>
public enum FileConflictPolicy
{
    Skip = 0,
    Overwrite = 1,
    Rename = 2,
    Ask = 3,

    /// <summary>文件夹冲突：递归合并（两边独有内容都保留）。</summary>
    Merge = 4,

    /// <summary>文件夹冲突：逐项询问文件夹内同名项。</summary>
    PerItem = 5,
}

/// <summary>询问冲突时用户的选择。</summary>
public enum FileConflictAction
{
    Skip = 0,
    Overwrite = 1,
    Rename = 2,
    Cancel = 3,

    /// <summary>文件夹冲突：递归合并。</summary>
    Merge = 4,

    /// <summary>文件夹冲突：逐项处理文件夹内同名项。</summary>
    PerItem = 5,
}

/// <summary>提交任务时的请求。</summary>
public sealed class FileTaskRequest
{
    /// <summary>任务类型。</summary>
    public FileTaskKind Kind { get; set; } = FileTaskKind.Copy;

    /// <summary>源路径列表。</summary>
    public List<string> Sources { get; set; } = new List<string>();

    /// <summary>目标目录；删除任务可为空。</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>冲突策略。</summary>
    public FileConflictPolicy ConflictPolicy { get; set; } = FileConflictPolicy.Ask;
}

/// <summary>任务快照：网页端与桌面端共用同一结构。</summary>
public sealed class FileTaskSnapshot
{
    /// <summary>任务编号。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>任务类型：copy/move/delete（远程为 upload/download 映射到 copy）。</summary>
    public string Kind { get; set; } = "copy";

    /// <summary>是否来自远程传输队列。</summary>
    public bool IsRemote { get; set; }

    /// <summary>状态：queued/running/waitingConflict/completed/failed/canceled。</summary>
    public string State { get; set; } = "queued";

    /// <summary>总项数。</summary>
    public int Total { get; set; }

    /// <summary>已处理项数。</summary>
    public int Processed { get; set; }

    /// <summary>失败项数。</summary>
    public int Failed { get; set; }

    /// <summary>进度百分比（0-100）。</summary>
    public int Progress { get; set; }

    /// <summary>源路径摘要。</summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>目标路径摘要。</summary>
    public string DestinationText { get; set; } = string.Empty;

    /// <summary>状态消息或失败原因。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>是否允许取消。</summary>
    public bool CanCancel { get; set; }

    /// <summary>是否允许重试失败项。</summary>
    public bool CanRetry { get; set; }

    /// <summary>等待用户选择冲突的路径；无冲突时为空。</summary>
    public string ConflictPath { get; set; } = string.Empty;

    /// <summary>冲突文件名（网页端提示用）。</summary>
    public string ConflictName { get; set; } = string.Empty;

    /// <summary>冲突项是否为文件夹：true 时提供合并 / 替换 / 逐项处理 / 改名共存四项。</summary>
    public bool ConflictIsDirectory { get; set; }

    /// <summary>创建时间文本。</summary>
    public string CreatedAt { get; set; } = string.Empty;
}
