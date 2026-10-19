namespace TabTaskExplorer.Kernel.Models;

/// <summary>远程传输方向。</summary>
public enum RemoteTransferDirection
{
    /// <summary>下载：远端到本地。</summary>
    Download = 0,

    /// <summary>上传：本地到远端。</summary>
    Upload = 1,
}

/// <summary>远程传输任务状态。</summary>
public enum RemoteTransferStatus
{
    /// <summary>排队等待。</summary>
    Pending = 0,

    /// <summary>传输中。</summary>
    Running = 1,

    /// <summary>已暂停。</summary>
    Paused = 2,

    /// <summary>已完成。</summary>
    Completed = 3,

    /// <summary>失败。</summary>
    Failed = 4,

    /// <summary>已取消。</summary>
    Canceled = 5,
}

/// <summary>
/// 远程传输任务：一次上传或下载的队列单元。
/// 字段由传输服务的后台线程更新，界面层读取前应先调度到 UI 线程。
/// </summary>
public class RemoteTransferJob
{
    /// <summary>任务唯一标识。</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>传输方向。</summary>
    public RemoteTransferDirection Direction { get; }

    /// <summary>远端路径（下载为源，上传为目标）。</summary>
    public string RemotePath { get; }

    /// <summary>本地路径（下载为目标，上传为源）。</summary>
    public string LocalPath { get; }

    /// <summary>文件总字节数；无法预知时为 -1。</summary>
    public long TotalBytes { get; internal set; } = -1;

    /// <summary>已传输字节数。</summary>
    public long TransferredBytes { get; internal set; }

    /// <summary>当前状态。</summary>
    public RemoteTransferStatus Status { get; internal set; } = RemoteTransferStatus.Pending;

    /// <summary>失败原因；未失败时为空文本。</summary>
    public string ErrorMessage { get; internal set; } = string.Empty;

    /// <summary>任务摘要：方向箭头 + 文件名。</summary>
    public string Summary
    {
        get
        {
            string fileName = this.LocalPath;
            int slashIndex = Math.Max(this.LocalPath.LastIndexOf('/'), this.LocalPath.LastIndexOf('\\'));
            if (slashIndex >= 0)
            {
                fileName = this.LocalPath.Substring(slashIndex + 1);
            }
            if (this.Direction == RemoteTransferDirection.Download)
            {
                return "下载 " + fileName;
            }
            return "上传 " + fileName;
        }
    }

    /// <summary>使用方向与路径创建任务。</summary>
    public RemoteTransferJob(RemoteTransferDirection direction, string remotePath, string localPath)
    {
        this.Direction = direction;
        this.RemotePath = remotePath;
        this.LocalPath = localPath;
    }
}
