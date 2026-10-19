using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 远程传输服务：按提交顺序排队执行上传与下载，支持暂停、继续、取消与断点续传。
/// 实现方应在后台线程执行传输，不得阻塞调用线程。
/// </summary>
public interface IRemoteTransferService : IDisposable
{
    /// <summary>任一任务状态或进度变化时触发（后台线程触发，界面层需自行调度）。</summary>
    event Action<RemoteTransferJob>? JobChanged;

    /// <summary>把一个下载任务加入队列（远端文件 → 本地路径）。</summary>
    RemoteTransferJob EnqueueDownload(string remotePath, string localPath);

    /// <summary>把一个上传任务加入队列（本地文件 → 远端路径）。</summary>
    RemoteTransferJob EnqueueUpload(string localPath, string remotePath);

    /// <summary>暂停指定任务；仅对排队或传输中的任务有效。</summary>
    void Pause(Guid jobId);

    /// <summary>继续执行已暂停的任务；文件传输从断点继续。</summary>
    void Resume(Guid jobId);

    /// <summary>取消指定任务；已完成任务无效。</summary>
    void Cancel(Guid jobId);

    /// <summary>读取当前全部任务（含已完成）。</summary>
    IReadOnlyList<RemoteTransferJob> GetJobs();
}
