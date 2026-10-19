using Renci.SshNet;
using Renci.SshNet.Sftp;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Protocol.Ssh;

/// <summary>
/// 基于 SFTP 的远程传输服务：单个后台工作线程按提交顺序执行队列任务，
/// 支持 64KB 分块、传输中暂停、失败或取消后从断点续传。
/// </summary>
public class SftpTransferService : IRemoteTransferService
{
    private const int ChunkSize = 64 * 1024;

    private readonly object gate = new object();
    private readonly RemoteConnectionProfile profile;
    private readonly string credential;
    private readonly List<RemoteTransferJob> jobs = new List<RemoteTransferJob>();
    private readonly Dictionary<Guid, JobControl> controls = new Dictionary<Guid, JobControl>();
    private readonly Queue<Guid> pendingQueue = new Queue<Guid>();
    private Thread? worker;
    private bool disposed;

    /// <inheritdoc />
    public event Action<RemoteTransferJob>? JobChanged;

    /// <summary>使用连接配置与明文凭据创建传输服务；连接延迟到首个任务执行时建立。</summary>
    public SftpTransferService(RemoteConnectionProfile profile, string credential)
    {
        this.profile = profile;
        this.credential = credential;
    }

    /// <inheritdoc />
    public RemoteTransferJob EnqueueDownload(string remotePath, string localPath)
    {
        return this.Enqueue(RemoteTransferDirection.Download, remotePath, localPath);
    }

    /// <inheritdoc />
    public RemoteTransferJob EnqueueUpload(string localPath, string remotePath)
    {
        return this.Enqueue(RemoteTransferDirection.Upload, remotePath, localPath);
    }

    /// <inheritdoc />
    public void Pause(Guid jobId)
    {
        lock (this.gate)
        {
            JobControl? control;
            if (this.controls.TryGetValue(jobId, out control))
            {
                RemoteTransferJob? job = this.FindJob(jobId);
                if (job != null && (job.Status == RemoteTransferStatus.Pending || job.Status == RemoteTransferStatus.Running))
                {
                    control.PauseRequested = true;
                    if (job.Status == RemoteTransferStatus.Pending)
                    {
                        job.Status = RemoteTransferStatus.Paused;
                        this.RaiseChanged(job);
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public void Resume(Guid jobId)
    {
        lock (this.gate)
        {
            JobControl? control;
            if (!this.controls.TryGetValue(jobId, out control))
            {
                return;
            }
            RemoteTransferJob? job = this.FindJob(jobId);
            if (job == null)
            {
                return;
            }

            if (job.Status == RemoteTransferStatus.Paused)
            {
                // 传输中暂停：解除暂停标记并唤醒工作线程
                control.PauseRequested = false;
                Monitor.PulseAll(this.gate);
                this.RaiseChanged(job);
            }
            else if (job.Status == RemoteTransferStatus.Failed || job.Status == RemoteTransferStatus.Canceled)
            {
                // 失败或取消后重排：执行时自动从断点续传
                control.PauseRequested = false;
                control.CancelRequested = false;
                job.Status = RemoteTransferStatus.Pending;
                job.ErrorMessage = string.Empty;
                this.pendingQueue.Enqueue(jobId);
                Monitor.PulseAll(this.gate);
                this.RaiseChanged(job);
            }
        }
    }

    /// <inheritdoc />
    public void Cancel(Guid jobId)
    {
        lock (this.gate)
        {
            JobControl? control;
            if (!this.controls.TryGetValue(jobId, out control))
            {
                return;
            }
            RemoteTransferJob? job = this.FindJob(jobId);
            if (job == null)
            {
                return;
            }

            control.CancelRequested = true;
            if (job.Status == RemoteTransferStatus.Pending || job.Status == RemoteTransferStatus.Paused)
            {
                job.Status = RemoteTransferStatus.Canceled;
                Monitor.PulseAll(this.gate);
                this.RaiseChanged(job);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RemoteTransferJob> GetJobs()
    {
        lock (this.gate)
        {
            return this.jobs.ToList();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            Monitor.PulseAll(this.gate);
        }
        Thread? workerThread = this.worker;
        if (workerThread != null && workerThread.IsAlive)
        {
            workerThread.Join(3000);
        }
    }

    /// <summary>把一个传输任务加入队列并确保工作线程已启动。</summary>
    private RemoteTransferJob Enqueue(RemoteTransferDirection direction, string remotePath, string localPath)
    {
        RemoteTransferJob job = new RemoteTransferJob(direction, remotePath, localPath);
        lock (this.gate)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(SftpTransferService));
            }
            this.jobs.Add(job);
            this.controls[job.Id] = new JobControl();
            this.pendingQueue.Enqueue(job.Id);
            if (this.worker == null)
            {
                this.worker = new Thread(this.WorkerLoop);
                this.worker.IsBackground = true;
                this.worker.Name = "SftpTransferWorker";
                this.worker.Start();
            }
            Monitor.PulseAll(this.gate);
            this.RaiseChanged(job);
        }
        return job;
    }

    /// <summary>工作线程主循环：依次取出待执行任务直到服务释放。</summary>
    private void WorkerLoop()
    {
        while (true)
        {
            Guid jobId;
            lock (this.gate)
            {
                while (this.pendingQueue.Count == 0 && !this.disposed)
                {
                    Monitor.Wait(this.gate, 1000);
                }
                if (this.disposed)
                {
                    return;
                }
                jobId = this.pendingQueue.Dequeue();
            }

            RemoteTransferJob? job = this.FindJob(jobId);
            if (job == null || job.Status != RemoteTransferStatus.Pending)
            {
                continue;
            }

            this.ExecuteJob(job);
        }
    }

    /// <summary>执行单个传输任务，处理暂停门控与断点续传。</summary>
    private void ExecuteJob(RemoteTransferJob job)
    {
        JobControl control = this.controls[job.Id];
        this.SetStatus(job, RemoteTransferStatus.Running);

        try
        {
            using (SftpClient sftp = this.CreateConnectedClient())
            {
                if (job.Direction == RemoteTransferDirection.Download)
                {
                    this.ExecuteDownload(sftp, job, control);
                }
                else
                {
                    this.ExecuteUpload(sftp, job, control);
                }
            }
            if (job.Status == RemoteTransferStatus.Running)
            {
                this.SetStatus(job, RemoteTransferStatus.Completed);
            }
        }
        catch (Exception ex)
        {
            if (job.Status == RemoteTransferStatus.Running || job.Status == RemoteTransferStatus.Paused)
            {
                job.Status = RemoteTransferStatus.Failed;
                job.ErrorMessage = ex.Message;
                this.RaiseChanged(job);
            }
        }
    }

    /// <summary>执行下载：本地已有部分文件时从断点续传。</summary>
    private void ExecuteDownload(SftpClient sftp, RemoteTransferJob job, JobControl control)
    {
        long remoteSize = sftp.GetAttributes(job.RemotePath).Size;
        job.TotalBytes = remoteSize;

        long startOffset = 0;
        FileMode localMode = FileMode.Create;
        if (File.Exists(job.LocalPath))
        {
            long localSize = new FileInfo(job.LocalPath).Length;
            if (localSize == remoteSize && remoteSize > 0)
            {
                job.TransferredBytes = remoteSize;
                return; // 目标已完整，直接完成
            }
            if (localSize > 0 && localSize < remoteSize)
            {
                startOffset = localSize; // 断点续传
                localMode = FileMode.Append;
            }
        }

        string? localDirectory = Path.GetDirectoryName(job.LocalPath);
        if (!string.IsNullOrEmpty(localDirectory))
        {
            Directory.CreateDirectory(localDirectory);
        }

        using (SftpFileStream remoteStream = sftp.OpenRead(job.RemotePath))
        using (FileStream localStream = new FileStream(job.LocalPath, localMode, FileAccess.Write))
        {
            remoteStream.Seek(startOffset, SeekOrigin.Begin);
            job.TransferredBytes = startOffset;
            byte[] buffer = new byte[ChunkSize];
            while (true)
            {
                this.WaitWhilePaused(job, control);
                int read = remoteStream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                localStream.Write(buffer, 0, read);
                job.TransferredBytes += read;
                this.RaiseChanged(job);
            }
        }
    }

    /// <summary>执行上传：远端已有部分文件时从断点续传。</summary>
    private void ExecuteUpload(SftpClient sftp, RemoteTransferJob job, JobControl control)
    {
        long localSize = new FileInfo(job.LocalPath).Length;
        job.TotalBytes = localSize;

        long startOffset = 0;
        if (sftp.Exists(job.RemotePath))
        {
            long remoteSize = sftp.GetAttributes(job.RemotePath).Size;
            if (remoteSize == localSize && localSize > 0)
            {
                job.TransferredBytes = localSize;
                return; // 远端已完整，直接完成
            }
            if (remoteSize > 0 && remoteSize < localSize)
            {
                startOffset = remoteSize; // 断点续传
            }
        }

        string remoteDirectory = SftpFileSystemProvider.GetParentRemotePath(job.RemotePath);
        if (remoteDirectory != "/" && !sftp.Exists(remoteDirectory))
        {
            this.CreateDirectoryRecursive(sftp, remoteDirectory);
        }

        using (FileStream localStream = new FileStream(job.LocalPath, FileMode.Open, FileAccess.Read))
        using (SftpFileStream remoteStream = sftp.Open(job.RemotePath, FileMode.OpenOrCreate, FileAccess.Write))
        {
            localStream.Seek(startOffset, SeekOrigin.Begin);
            remoteStream.Seek(startOffset, SeekOrigin.Begin);
            job.TransferredBytes = startOffset;
            byte[] buffer = new byte[ChunkSize];
            while (true)
            {
                this.WaitWhilePaused(job, control);
                int read = localStream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                remoteStream.Write(buffer, 0, read);
                job.TransferredBytes += read;
                this.RaiseChanged(job);
            }
        }
    }

    /// <summary>暂停门控：请求暂停时阻塞，直到继续、取消或服务释放。</summary>
    private void WaitWhilePaused(RemoteTransferJob job, JobControl control)
    {
        lock (this.gate)
        {
            while (control.PauseRequested && !control.CancelRequested && !this.disposed)
            {
                if (job.Status == RemoteTransferStatus.Running)
                {
                    job.Status = RemoteTransferStatus.Paused;
                    this.RaiseChanged(job);
                }
                Monitor.Wait(this.gate, 500);
            }
            if (this.disposed || control.CancelRequested)
            {
                if (!this.disposed && control.CancelRequested)
                {
                    job.Status = RemoteTransferStatus.Canceled;
                }
                throw new OperationCanceledException("传输已取消");
            }
            if (job.Status == RemoteTransferStatus.Paused)
            {
                job.Status = RemoteTransferStatus.Running;
                this.RaiseChanged(job);
            }
        }
    }

    /// <summary>逐级创建远端目录。</summary>
    private void CreateDirectoryRecursive(SftpClient sftp, string remotePath)
    {
        string parent = SftpFileSystemProvider.GetParentRemotePath(remotePath);
        if (parent != remotePath && !sftp.Exists(parent))
        {
            this.CreateDirectoryRecursive(sftp, parent);
        }
        if (!sftp.Exists(remotePath))
        {
            sftp.CreateDirectory(remotePath);
        }
    }

    /// <summary>建立独立的 SFTP 连接（与文件浏览互不干扰）。</summary>
    private SftpClient CreateConnectedClient()
    {
        ConnectionInfo info = SshSessionFactory.BuildConnectionInfo(this.profile, this.credential);
        SftpClient sftp = new SftpClient(info);
        sftp.KeepAliveInterval = TimeSpan.FromSeconds(15);
        sftp.Connect();
        if (!sftp.IsConnected)
        {
            sftp.Dispose();
            throw new InvalidOperationException("SFTP 连接失败：服务器无响应");
        }
        return sftp;
    }

    /// <summary>按编号查找任务。</summary>
    private RemoteTransferJob? FindJob(Guid jobId)
    {
        for (int index = 0; index < this.jobs.Count; index++)
        {
            if (this.jobs[index].Id == jobId)
            {
                return this.jobs[index];
            }
        }
        return null;
    }

    /// <summary>更新状态并通知。</summary>
    private void SetStatus(RemoteTransferJob job, RemoteTransferStatus status)
    {
        job.Status = status;
        this.RaiseChanged(job);
    }

    /// <summary>触发任务变化事件。</summary>
    private void RaiseChanged(RemoteTransferJob job)
    {
        Action<RemoteTransferJob>? handler = this.JobChanged;
        if (handler != null)
        {
            handler(job);
        }
    }

    /// <summary>单个任务的暂停 / 取消控制标记。</summary>
    private class JobControl
    {
        /// <summary>是否请求暂停。</summary>
        public bool PauseRequested;

        /// <summary>是否请求取消。</summary>
        public bool CancelRequested;
    }
}
