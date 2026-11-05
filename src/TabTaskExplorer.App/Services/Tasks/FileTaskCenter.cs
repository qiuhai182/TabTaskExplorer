using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TabTaskExplorer.App.Services.Tasks;

/// <summary>
/// 统一任务中心：串行执行本地复制/移动/删除，提供进度、冲突询问、取消与失败重试。
/// 远程传输队列由 <see cref="RemoteTaskAdapter"/> 映射为同一快照结构。
/// </summary>
public sealed class FileTaskCenter
{
    private readonly object gate = new object();
    private readonly List<FileTaskEntry> order = new List<FileTaskEntry>();
    private readonly Dictionary<string, FileTaskEntry> entries = new Dictionary<string, FileTaskEntry>();
    private readonly Channel<FileTaskEntry> queue = Channel.CreateUnbounded<FileTaskEntry>();
    private readonly CancellationTokenSource shutdown = new CancellationTokenSource();

    /// <summary>任务进入终态（完成/失败/取消）时触发，供桌面刷新列表。</summary>
    public event Action<FileTaskSnapshot>? TaskFinished;

    /// <summary>任务开始等待用户处理冲突时触发；不会自动跳过，必须由用户选择或取消。</summary>
    public event Action<FileTaskSnapshot>? ConflictPending;

    public FileTaskCenter()
    {
        Task.Run(this.WorkerAsync);
    }

    /// <summary>提交任务并返回任务编号；源列表为空时返回空字符串。</summary>
    public string Submit(FileTaskRequest request)
    {
        if (request == null || request.Kind != FileTaskKind.Delete && string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return string.Empty;
        }
        List<string> sources = new List<string>();
        foreach (string source in request.Sources ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(source))
            {
                string trimmed = source.Trim();
                if (!sources.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    sources.Add(trimmed);
                }
            }
        }
        if (sources.Count == 0)
        {
            return string.Empty;
        }
        FileTaskEntry entry = new FileTaskEntry(new FileTaskRequest
        {
            Kind = request.Kind,
            Sources = sources,
            DestinationPath = request.DestinationPath ?? string.Empty,
            ConflictPolicy = request.ConflictPolicy,
        });
        entry.ConflictPending = this.NotifyConflictPending;
        lock (this.gate)
        {
            // 快速连续粘贴会提交完全相同的任务：已有未结束的同源同目标任务时直接复用，避免重复复制
            for (int index = this.order.Count - 1; index >= 0; index--)
            {
                FileTaskEntry existing = this.order[index];
                if (!existing.IsTerminal && existing.Request.Kind == entry.Request.Kind
                    && string.Equals(existing.Request.DestinationPath, entry.Request.DestinationPath, StringComparison.OrdinalIgnoreCase)
                    && FileTaskCenter.SourcesEqual(existing.Request.Sources, sources))
                {
                    UiLogger.Log("任务中心", "任务去重：已存在相同的未结束任务 任务=" + existing.Id
                        + " 类型=" + entry.Request.Kind + " 目标=" + entry.Request.DestinationPath + " 结果=复用既有任务");
                    return existing.Id;
                }
            }
            this.entries[entry.Id] = entry;
            this.order.Add(entry);
            this.Prune();
        }
        this.queue.Writer.TryWrite(entry);
        return entry.Id;
    }

    /// <summary>取消指定任务：排队中直接标记取消，运行中通过取消令牌在下一项前停止。</summary>
    public bool Cancel(string id)
    {
        FileTaskEntry? entry = this.Find(id);
        return entry != null && entry.Cancel();
    }

    /// <summary>重试失败项：生成一个只包含失败源的新任务。</summary>
    public bool Retry(string id)
    {
        FileTaskEntry? entry = this.Find(id);
        if (entry == null)
        {
            return false;
        }
        if (!entry.IsTerminal)
        {
            return false;
        }
        List<string> failed = entry.GetFailedSources();
        if (failed.Count == 0)
        {
            // 整任务失败（例如执行器抛出异常）没有逐项记录，重试全部源
            failed = new List<string>(entry.Request.Sources);
        }
        if (failed.Count == 0)
        {
            return false;
        }
        return this.Submit(new FileTaskRequest
        {
            Kind = entry.Request.Kind,
            Sources = failed,
            DestinationPath = entry.Request.DestinationPath,
            ConflictPolicy = entry.Request.ConflictPolicy,
        }) != string.Empty;
    }

    /// <summary>处理冲突询问：文件（替换 / 改名共存）、文件夹（合并 / 替换 / 逐项处理 / 改名共存）、取消；applyToAll 后同类冲突不再询问。</summary>
    public bool ResolveConflict(string id, string action, bool applyToAll)
    {
        FileTaskEntry? entry = this.Find(id);
        if (entry == null)
        {
            return false;
        }
        FileConflictAction parsed;
        if (!Enum.TryParse(action, true, out parsed))
        {
            return false;
        }
        entry.Resolve(parsed, applyToAll);
        UiLogger.Log("任务中心", "冲突处理：任务=" + id + " 选择=" + parsed + " 本批统一=" + applyToAll + " 结果=已写回");
        return true;
    }

    private static bool SourcesEqual(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>读取全部本地任务快照（新→旧以外的稳定提交顺序）。</summary>
    public IReadOnlyList<FileTaskSnapshot> Snapshot()
    {
        lock (this.gate)
        {
            List<FileTaskSnapshot> result = new List<FileTaskSnapshot>(this.order.Count);
            foreach (FileTaskEntry entry in this.order)
            {
                result.Add(entry.ToSnapshot());
            }
            return result;
        }
    }

    private FileTaskEntry? Find(string id)
    {
        lock (this.gate)
        {
            FileTaskEntry? entry;
            return this.entries.TryGetValue(id, out entry) ? entry : null;
        }
    }

    private void Prune()
    {
        if (this.order.Count <= 200)
        {
            return;
        }
        for (int index = 0; index < this.order.Count && this.order.Count > 200; index++)
        {
            FileTaskEntry entry = this.order[index];
            if (entry.IsTerminal)
            {
                this.order.RemoveAt(index);
                this.entries.Remove(entry.Id);
                index--;
            }
        }
    }

    private async Task WorkerAsync()
    {
        try
        {
            await foreach (FileTaskEntry entry in this.queue.Reader.ReadAllAsync(this.shutdown.Token))
            {
                if (entry.IsTerminal)
                {
                    this.NotifyFinished(entry);
                    continue;
                }
                entry.SetState(FileTaskState.Running);
                try
                {
                    await LocalFileTaskExecutor.RunAsync(entry);
                    entry.Complete();
                }
                catch (OperationCanceledException)
                {
                    entry.MarkCanceled();
                }
                catch (Exception error)
                {
                    entry.MarkFailed(error.Message);
                }
                this.NotifyFinished(entry);
            }
        }
        catch (OperationCanceledException)
        {
            // 服务结束
        }
    }

    private void NotifyFinished(FileTaskEntry entry)
    {
        Action<FileTaskSnapshot>? handler = this.TaskFinished;
        if (handler == null)
        {
            return;
        }
        try
        {
            handler(entry.ToSnapshot());
        }
        catch (Exception)
        {
            // 订阅方异常不影响任务队列
        }
    }

    private void NotifyConflictPending(FileTaskSnapshot snapshot)
    {
        Action<FileTaskSnapshot>? handler = this.ConflictPending;
        if (handler == null)
        {
            return;
        }
        try
        {
            handler(snapshot);
        }
        catch (Exception)
        {
            // 订阅方异常不影响任务队列
        }
    }
}

/// <summary>单个任务的可变状态；所有字段由 Sync 锁保护。</summary>
internal sealed class FileTaskEntry
{
    internal readonly object Sync = new object();
    internal readonly FileTaskRequest Request;
    internal readonly string Id = "ft-" + Guid.NewGuid().ToString("N").Substring(0, 12);
    internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
    internal readonly DateTime CreatedAt = DateTime.Now;
    internal Action<FileTaskSnapshot>? ConflictPending;
    internal FileTaskState State = FileTaskState.Queued;
    internal int Total;
    internal int Processed;
    internal int Failed;
    internal int Skipped;
    internal string Message = "排队中";
    internal string LastError = string.Empty;
    internal string ConflictPath = string.Empty;
    internal string ConflictName = string.Empty;
    internal bool ConflictIsDirectory;
    private readonly List<string> failedSources = new List<string>();
    // 文件与文件夹的「本批统一」分开记忆：文件夹选择合并不应影响之后的同名文件询问
    private FileConflictAction? applyToAllFile;
    private FileConflictAction? applyToAllDirectory;
    private TaskCompletionSource<FileConflictAction>? conflictSource;

    internal FileTaskEntry(FileTaskRequest request)
    {
        this.Request = request;
        this.Total = request.Sources.Count;
    }

    internal CancellationToken Token
    {
        get { return this.Cancellation.Token; }
    }

    internal bool IsTerminal
    {
        get
        {
            lock (this.Sync)
            {
                return this.State == FileTaskState.Completed
                    || this.State == FileTaskState.Failed
                    || this.State == FileTaskState.Canceled;
            }
        }
    }

    internal bool Cancel()
    {
        TaskCompletionSource<FileConflictAction>? pending = null;
        lock (this.Sync)
        {
            if (this.State == FileTaskState.Completed || this.State == FileTaskState.Failed || this.State == FileTaskState.Canceled)
            {
                return false;
            }
            this.Cancellation.Cancel();
            if (this.State == FileTaskState.Queued)
            {
                this.State = FileTaskState.Canceled;
                this.Message = "已取消（尚未开始）";
            }
            pending = this.conflictSource;
            this.conflictSource = null;
        }
        pending?.TrySetResult(FileConflictAction.Cancel);
        return true;
    }

    internal void SetState(FileTaskState state)
    {
        lock (this.Sync)
        {
            this.State = state;
            if (state == FileTaskState.Running && this.Message == "排队中")
            {
                this.Message = "正在处理……";
            }
        }
    }

    internal void ReportProcessed()
    {
        lock (this.Sync)
        {
            this.Processed++;
            this.Message = "已完成 " + this.Processed + " / " + this.Total + " 项";
        }
    }

    internal void ReportSkipped()
    {
        lock (this.Sync)
        {
            this.Processed++;
            this.Skipped++;
            this.Message = "已跳过 " + this.Skipped + " 项";
        }
    }

    /// <summary>合并目录时报告当前项，让大量小文件也有可见进度（不改变顶层进度计数）。</summary>
    internal void ReportNestedProgress(string path, int copiedCount)
    {
        lock (this.Sync)
        {
            this.Message = "正在处理：" + path + "（已复制 " + copiedCount + " 个文件）";
        }
    }

    internal void ReportFailure(string source, string error)
    {
        lock (this.Sync)
        {
            this.Processed++;
            this.Failed++;
            this.failedSources.Add(source);
            this.LastError = Path.GetFileName(source) + "：" + error;
            this.Message = "失败：" + this.LastError;
        }
    }

    internal void Complete()
    {
        lock (this.Sync)
        {
            this.State = this.Failed > 0 ? FileTaskState.Failed : FileTaskState.Completed;
            this.Message = (this.Failed > 0 ? "部分失败：" : "完成：")
                + "成功 " + Math.Max(0, this.Processed - this.Failed - this.Skipped)
                + " 项，跳过 " + this.Skipped + " 项，失败 " + this.Failed + " 项"
                + (this.Failed > 0 && this.LastError.Length > 0 ? "；最近失败：" + this.LastError : string.Empty);
        }
    }

    internal void MarkFailed(string error)
    {
        lock (this.Sync)
        {
            this.State = FileTaskState.Failed;
            this.Message = "任务失败：" + error;
        }
    }

    internal void MarkCanceled()
    {
        lock (this.Sync)
        {
            this.State = FileTaskState.Canceled;
            this.Message = "已取消：已处理 " + this.Processed + " 项";
        }
    }

    internal List<string> GetFailedSources()
    {
        lock (this.Sync)
        {
            return this.failedSources.ToList();
        }
    }

    /// <summary>等待冲突处理选择：不会自动跳过，必须由用户选择或取消任务；applyToAll 后直接返回。</summary>
    internal async Task<FileConflictAction> RequestResolutionAsync(string path, bool isDirectory, CancellationToken token)
    {
        TaskCompletionSource<FileConflictAction> source;
        lock (this.Sync)
        {
            FileConflictAction? memo = isDirectory ? this.applyToAllDirectory : this.applyToAllFile;
            if (memo.HasValue)
            {
                UiLogger.Log("任务中心", "冲突判定：路径=" + path + " 类型=" + (isDirectory ? "文件夹" : "文件")
                    + " 策略=本批统一 结果=沿用 " + memo.Value);
                return memo.Value;
            }
            this.State = FileTaskState.WaitingForConflict;
            this.ConflictPath = path;
            this.ConflictName = Path.GetFileName(path);
            this.ConflictIsDirectory = isDirectory;
            this.Message = isDirectory
                ? "目标已存在同名文件夹：" + this.ConflictName
                    + "。请选择合并 / 替换 / 逐项处理 / 改名共存（替换=清空目标后写入源内容，删除走回收站；不会自动跳过）"
                : "目标已存在同名文件：" + this.ConflictName
                    + "。请选择替换或改名共存（不会自动跳过，可取消任务）";
            source = new TaskCompletionSource<FileConflictAction>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.conflictSource = source;
            UiLogger.Log("任务中心", "冲突判定：路径=" + path + " 类型=" + (isDirectory ? "文件夹" : "文件")
                + " 策略=询问 结果=等待用户选择（不会自动跳过）");
        }
        Action<FileTaskSnapshot>? conflictHandler = this.ConflictPending;
        if (conflictHandler != null)
        {
            try
            {
                conflictHandler(this.ToSnapshot());
            }
            catch (Exception)
            {
                // 订阅方异常不影响任务等待
            }
        }
        FileConflictAction action;
        try
        {
            action = await source.Task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (this.Sync)
            {
                this.conflictSource = null;
            }
            throw;
        }
        lock (this.Sync)
        {
            this.State = FileTaskState.Running;
            this.ConflictPath = string.Empty;
            this.ConflictName = string.Empty;
            // 保留 ConflictIsDirectory 供 Resolve 记忆本批统一；下次询问会覆盖
            if (this.Message.StartsWith("目标已存在", StringComparison.Ordinal))
            {
                this.Message = "继续处理……";
            }
        }
        return action;
    }

    internal void Resolve(FileConflictAction action, bool applyToAll)
    {
        TaskCompletionSource<FileConflictAction>? source = null;
        lock (this.Sync)
        {
            if (applyToAll && action != FileConflictAction.Cancel)
            {
                if (this.ConflictIsDirectory)
                {
                    this.applyToAllDirectory = action;
                }
                else
                {
                    this.applyToAllFile = action;
                }
            }
            source = this.conflictSource;
            this.conflictSource = null;
        }
        source?.TrySetResult(action);
    }

    internal FileTaskSnapshot ToSnapshot()
    {
        lock (this.Sync)
        {
            return new FileTaskSnapshot
            {
                Id = this.Id,
                Kind = this.Request.Kind.ToString().ToLowerInvariant(),
                IsRemote = false,
                State = StateText(this.State),
                Total = this.Total,
                Processed = this.Processed,
                Failed = this.Failed,
                Progress = this.Total > 0 ? (int)Math.Round(this.Processed * 100.0 / this.Total) : 0,
                SourceText = string.Join("；", this.Request.Sources.Take(3)) + (this.Request.Sources.Count > 3 ? " 等 " + this.Request.Sources.Count + " 项" : string.Empty),
                DestinationText = this.Request.DestinationPath,
                Message = this.Message,
                CanCancel = this.State == FileTaskState.Queued || this.State == FileTaskState.Running || this.State == FileTaskState.WaitingForConflict,
                // 整任务失败（未逐项记录）时也允许重试全部源，保证失败项在 UI 上有可见的重试入口
                CanRetry = this.State == FileTaskState.Failed
                    && (this.failedSources.Count > 0 || this.Request.Sources.Count > 0),
                ConflictPath = this.ConflictPath,
                ConflictName = this.ConflictName,
                ConflictIsDirectory = this.ConflictIsDirectory,
                CreatedAt = this.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            };
        }
    }

    internal static string StateText(FileTaskState state)
    {
        if (state == FileTaskState.Queued) { return "queued"; }
        if (state == FileTaskState.Running) { return "running"; }
        if (state == FileTaskState.WaitingForConflict) { return "waitingConflict"; }
        if (state == FileTaskState.Completed) { return "completed"; }
        if (state == FileTaskState.Failed) { return "failed"; }
        if (state == FileTaskState.Canceled) { return "canceled"; }
        if (state == FileTaskState.Paused) { return "paused"; }
        return "queued";
    }
}
