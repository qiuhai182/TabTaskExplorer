namespace TabTaskExplorer.Kernel.Services;

/// <summary>远端目录列举条目：供传输服务递归展开文件夹使用。</summary>
public class RemoteTransferListEntry
{
    /// <summary>条目名称。</summary>
    public string Name { get; }

    /// <summary>条目完整远端路径。</summary>
    public string FullPath { get; }

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; }

    /// <summary>文件大小（字节），文件夹为 0。</summary>
    public long Size { get; }

    /// <summary>使用条目字段创建实例。</summary>
    public RemoteTransferListEntry(string name, string fullPath, bool isDirectory, long size)
    {
        this.Name = name;
        this.FullPath = fullPath;
        this.IsDirectory = isDirectory;
        this.Size = size;
    }
}

/// <summary>
/// 远程传输服务模板基类：实现队列、64KB 分块、传输中暂停、失败或取消后断点续传、
/// 文件夹递归传输与自动重试（参考 Xftp 传输队列的 RetryCount / RetryTimer 机制）。
/// 协议差异只保留在连接与流访问的抽象方法里，SFTP 与 FTP 适配工程各实现一个薄子类。
/// </summary>
public abstract class RemoteTransferServiceBase : Abstractions.IRemoteTransferService
{
    /// <summary>分块大小：64KB。</summary>
    protected const int ChunkSize = 64 * 1024;

    /// <summary>失败自动重试次数（含首次执行）。</summary>
    protected const int MaxAttempts = 3;

    /// <summary>重试前等待秒数。</summary>
    protected const int RetryDelaySeconds = 3;

    private readonly object gate = new object();
    private readonly List<Models.RemoteTransferJob> jobs = new List<Models.RemoteTransferJob>();
    private readonly Dictionary<Guid, JobControl> controls = new Dictionary<Guid, JobControl>();
    private readonly Queue<Guid> pendingQueue = new Queue<Guid>();
    private Thread? worker;
    private bool disposed;

    /// <inheritdoc />
    public event Action<Models.RemoteTransferJob>? JobChanged;

    /// <inheritdoc />
    public Models.RemoteTransferJob EnqueueDownload(string remotePath, string localPath)
    {
        return this.Enqueue(Models.RemoteTransferDirection.Download, remotePath, localPath);
    }

    /// <inheritdoc />
    public Models.RemoteTransferJob EnqueueUpload(string localPath, string remotePath)
    {
        return this.Enqueue(Models.RemoteTransferDirection.Upload, remotePath, localPath);
    }

    /// <inheritdoc />
    public void Pause(Guid jobId)
    {
        lock (this.gate)
        {
            JobControl? control;
            if (this.controls.TryGetValue(jobId, out control))
            {
                Models.RemoteTransferJob? job = this.FindJob(jobId);
                if (job != null && (job.Status == Models.RemoteTransferStatus.Pending || job.Status == Models.RemoteTransferStatus.Running))
                {
                    control.PauseRequested = true;
                    if (job.Status == Models.RemoteTransferStatus.Pending)
                    {
                        job.Status = Models.RemoteTransferStatus.Paused;
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
            Models.RemoteTransferJob? job = this.FindJob(jobId);
            if (job == null)
            {
                return;
            }

            if (job.Status == Models.RemoteTransferStatus.Paused)
            {
                control.PauseRequested = false;
                Monitor.PulseAll(this.gate);
                this.RaiseChanged(job);
            }
            else if (job.Status == Models.RemoteTransferStatus.Failed || job.Status == Models.RemoteTransferStatus.Canceled)
            {
                control.PauseRequested = false;
                control.CancelRequested = false;
                job.Status = Models.RemoteTransferStatus.Pending;
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
            Models.RemoteTransferJob? job = this.FindJob(jobId);
            if (job == null)
            {
                return;
            }

            control.CancelRequested = true;
            if (job.Status == Models.RemoteTransferStatus.Pending || job.Status == Models.RemoteTransferStatus.Paused)
            {
                job.Status = Models.RemoteTransferStatus.Canceled;
                Monitor.PulseAll(this.gate);
                this.RaiseChanged(job);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Models.RemoteTransferJob> GetJobs()
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

    /// <summary>
    /// 一次任务执行期间建立的远端连接：封装流访问与目录列举。
    /// 每个任务开始时创建、结束时释放。
    /// </summary>
    protected interface IRemoteTransferConnection : IDisposable
    {
        /// <summary>打开远端文件只读流，定位到指定偏移。</summary>
        Stream OpenRemoteRead(string remotePath, long startPosition);

        /// <summary>打开远端文件写入流（不存在则创建），定位到指定偏移。</summary>
        Stream OpenRemoteWrite(string remotePath, long startPosition);

        /// <summary>取远端文件大小；不存在返回 -1。</summary>
        long GetRemoteFileSize(string remotePath);

        /// <summary>判断远端路径是否为文件夹。</summary>
        bool IsRemoteDirectory(string remotePath);

        /// <summary>逐级创建远端目录。</summary>
        void EnsureRemoteDirectory(string remotePath);

        /// <summary>列举远端目录的直接子项（不含 . 与 ..）。</summary>
        IReadOnlyList<RemoteTransferListEntry> ListRemoteDirectory(string remotePath);
    }

    /// <summary>建立一次任务专用的远端连接。</summary>
    protected abstract IRemoteTransferConnection OpenConnection();

    /// <summary>把一个传输任务加入队列并确保工作线程已启动。</summary>
    protected Models.RemoteTransferJob Enqueue(Models.RemoteTransferDirection direction, string remotePath, string localPath)
    {
        Models.RemoteTransferJob job = new Models.RemoteTransferJob(direction, remotePath, localPath);
        lock (this.gate)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(RemoteTransferServiceBase));
            }
            this.jobs.Add(job);
            this.controls[job.Id] = new JobControl();
            this.pendingQueue.Enqueue(job.Id);
            if (this.worker == null)
            {
                this.worker = new Thread(this.WorkerLoop);
                this.worker.IsBackground = true;
                this.worker.Name = "RemoteTransferWorker";
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

            Models.RemoteTransferJob? job = this.FindJob(jobId);
            if (job == null || job.Status != Models.RemoteTransferStatus.Pending)
            {
                continue;
            }

            this.ExecuteJobWithRetry(job);
        }
    }

    /// <summary>带自动重试的任务执行：失败后从断点继续重试，重试间隔 RetryDelaySeconds。</summary>
    private void ExecuteJobWithRetry(Models.RemoteTransferJob job)
    {
        JobControl control = this.controls[job.Id];
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (this.disposed || control.CancelRequested)
            {
                if (!this.disposed && control.CancelRequested && job.Status != Models.RemoteTransferStatus.Canceled)
                {
                    job.Status = Models.RemoteTransferStatus.Canceled;
                    this.RaiseChanged(job);
                }
                return;
            }

            this.SetStatus(job, Models.RemoteTransferStatus.Running);
            try
            {
                using (IRemoteTransferConnection connection = this.OpenConnection())
                {
                    if (job.Direction == Models.RemoteTransferDirection.Download)
                    {
                        this.ExecuteDownload(connection, job, control);
                    }
                    else
                    {
                        this.ExecuteUpload(connection, job, control);
                    }
                }
                if (job.Status == Models.RemoteTransferStatus.Running || job.Status == Models.RemoteTransferStatus.Paused)
                {
                    this.SetStatus(job, Models.RemoteTransferStatus.Completed);
                }
                return;
            }
            catch (Exception ex)
            {
                if (this.disposed || control.CancelRequested)
                {
                    if (!this.disposed)
                    {
                        job.Status = Models.RemoteTransferStatus.Canceled;
                        this.RaiseChanged(job);
                    }
                    return;
                }
                if (attempt >= MaxAttempts)
                {
                    job.Status = Models.RemoteTransferStatus.Failed;
                    job.ErrorMessage = ex.Message;
                    this.RaiseChanged(job);
                    return;
                }
                job.Status = Models.RemoteTransferStatus.Pending;
                job.ErrorMessage = "第 " + attempt + " 次失败，将重试：" + ex.Message;
                this.RaiseChanged(job);
                Thread.Sleep(TimeSpan.FromSeconds(RetryDelaySeconds));
            }
        }
    }

    /// <summary>执行下载：远端是文件夹时递归展开，本地已有部分文件时从断点续传。</summary>
    private void ExecuteDownload(IRemoteTransferConnection connection, Models.RemoteTransferJob job, JobControl control)
    {
        if (connection.IsRemoteDirectory(job.RemotePath))
        {
            this.TransferDirectoryRecursive(connection, job, control, Models.RemoteTransferDirection.Download);
            return;
        }

        long remoteSize = connection.GetRemoteFileSize(job.RemotePath);
        if (remoteSize < 0)
        {
            throw new FileNotFoundException("远端文件不存在：" + job.RemotePath);
        }
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

        using (Stream remoteStream = connection.OpenRemoteRead(job.RemotePath, startOffset))
        using (FileStream localStream = new FileStream(job.LocalPath, localMode, FileAccess.Write))
        {
            job.TransferredBytes = startOffset;
            this.CopyChunked(remoteStream, localStream, job, control);
        }
    }

    /// <summary>执行上传：本地是文件夹时递归展开，远端已有部分文件时从断点续传。</summary>
    private void ExecuteUpload(IRemoteTransferConnection connection, Models.RemoteTransferJob job, JobControl control)
    {
        if (Directory.Exists(job.LocalPath) && (File.GetAttributes(job.LocalPath) & FileAttributes.Directory) != 0)
        {
            this.TransferDirectoryRecursive(connection, job, control, Models.RemoteTransferDirection.Upload);
            return;
        }

        long localSize = new FileInfo(job.LocalPath).Length;
        job.TotalBytes = localSize;

        long startOffset = 0;
        long remoteSize = connection.GetRemoteFileSize(job.RemotePath);
        if (remoteSize == localSize && localSize > 0)
        {
            job.TransferredBytes = localSize;
            return; // 远端已完整，直接完成
        }
        if (remoteSize > 0 && remoteSize < localSize)
        {
            startOffset = remoteSize; // 断点续传
        }

        string remoteDirectory = GetRemoteParentPath(job.RemotePath);
        connection.EnsureRemoteDirectory(remoteDirectory);

        using (FileStream localStream = new FileStream(job.LocalPath, FileMode.Open, FileAccess.Read))
        using (Stream remoteStream = connection.OpenRemoteWrite(job.RemotePath, startOffset))
        {
            localStream.Seek(startOffset, SeekOrigin.Begin);
            job.TransferredBytes = startOffset;
            this.CopyChunked(localStream, remoteStream, job, control);
        }
    }

    /// <summary>递归传输整个文件夹：先统计总字节数，再逐文件传输并累计进度。</summary>
    private void TransferDirectoryRecursive(
        IRemoteTransferConnection connection,
        Models.RemoteTransferJob job,
        JobControl control,
        Models.RemoteTransferDirection direction)
    {
        List<RemoteTransferListEntry> files = new List<RemoteTransferListEntry>();
        this.CollectFilesRecursive(connection, job.RemotePath, files);
        long totalBytes = 0;
        foreach (RemoteTransferListEntry entry in files)
        {
            totalBytes += entry.Size;
        }
        job.TotalBytes = totalBytes;
        job.TransferredBytes = 0;

        foreach (RemoteTransferListEntry entry in files)
        {
            this.WaitWhilePaused(job, control);
            string relativePath = entry.FullPath.Length > job.RemotePath.Length
                ? entry.FullPath.Substring(job.RemotePath.Length).TrimStart('/')
                : entry.Name;
            if (direction == Models.RemoteTransferDirection.Download)
            {
                string localTarget = Path.Combine(job.LocalPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                this.DownloadSingleFile(connection, entry.FullPath, localTarget, job, control);
            }
            else
            {
                string remoteTarget = job.RemotePath + "/" + relativePath;
                this.UploadSingleFile(connection, Path.Combine(job.LocalPath, relativePath), remoteTarget, job, control);
            }
        }
    }

    /// <summary>递归收集远端目录下的全部文件。</summary>
    private void CollectFilesRecursive(IRemoteTransferConnection connection, string remoteDirectory, List<RemoteTransferListEntry> files)
    {
        foreach (RemoteTransferListEntry entry in connection.ListRemoteDirectory(remoteDirectory))
        {
            if (entry.IsDirectory)
            {
                this.CollectFilesRecursive(connection, entry.FullPath, files);
            }
            else
            {
                files.Add(entry);
            }
        }
    }

    /// <summary>文件夹模式下下载单个文件并累计进度。</summary>
    private void DownloadSingleFile(
        IRemoteTransferConnection connection, string remotePath, string localPath,
        Models.RemoteTransferJob job, JobControl control)
    {
        long remoteSize = connection.GetRemoteFileSize(remotePath);
        long startOffset = 0;
        FileMode localMode = FileMode.Create;
        if (File.Exists(localPath))
        {
            long localSize = new FileInfo(localPath).Length;
            if (localSize == remoteSize && remoteSize > 0)
            {
                job.TransferredBytes += remoteSize;
                this.RaiseChanged(job);
                return; // 已完整，跳过
            }
            if (localSize > 0 && localSize < remoteSize)
            {
                startOffset = localSize;
                localMode = FileMode.Append;
            }
        }
        string? localDirectory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(localDirectory))
        {
            Directory.CreateDirectory(localDirectory);
        }
        using (Stream remoteStream = connection.OpenRemoteRead(remotePath, startOffset))
        using (FileStream localStream = new FileStream(localPath, localMode, FileAccess.Write))
        {
            this.CopyChunked(remoteStream, localStream, job, control);
        }
    }

    /// <summary>文件夹模式下上传单个文件并累计进度。</summary>
    private void UploadSingleFile(
        IRemoteTransferConnection connection, string localPath, string remotePath,
        Models.RemoteTransferJob job, JobControl control)
    {
        long localSize = new FileInfo(localPath).Length;
        long startOffset = 0;
        long remoteSize = connection.GetRemoteFileSize(remotePath);
        if (remoteSize == localSize && localSize > 0)
        {
            job.TransferredBytes += localSize;
            this.RaiseChanged(job);
            return;
        }
        if (remoteSize > 0 && remoteSize < localSize)
        {
            startOffset = remoteSize;
        }
        connection.EnsureRemoteDirectory(GetRemoteParentPath(remotePath));
        using (FileStream localStream = new FileStream(localPath, FileMode.Open, FileAccess.Read))
        using (Stream remoteStream = connection.OpenRemoteWrite(remotePath, startOffset))
        {
            localStream.Seek(startOffset, SeekOrigin.Begin);
            this.CopyChunked(localStream, remoteStream, job, control);
        }
    }

    /// <summary>分块复制：每块写入前检查暂停与取消门控并上报进度。</summary>
    private void CopyChunked(Stream source, Stream target, Models.RemoteTransferJob job, JobControl control)
    {
        byte[] buffer = new byte[ChunkSize];
        while (true)
        {
            this.WaitWhilePaused(job, control);
            int read = source.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }
            target.Write(buffer, 0, read);
            job.TransferredBytes += read;
            this.RaiseChanged(job);
        }
    }

    /// <summary>远端路径的父目录：根目录返回自身；子类负责路径风格适配时在子类处理。</summary>
    private static string GetRemoteParentPath(string remotePath)
    {
        string normalized = remotePath.Replace('\\', '/');
        int index = normalized.LastIndexOf('/');
        if (index <= 0)
        {
            return "/";
        }
        return normalized.Substring(0, index);
    }

    /// <summary>暂停门控：请求暂停时阻塞，直到继续、取消或服务释放。</summary>
    private void WaitWhilePaused(Models.RemoteTransferJob job, JobControl control)
    {
        lock (this.gate)
        {
            while (control.PauseRequested && !control.CancelRequested && !this.disposed)
            {
                if (job.Status == Models.RemoteTransferStatus.Running)
                {
                    job.Status = Models.RemoteTransferStatus.Paused;
                    this.RaiseChanged(job);
                }
                Monitor.Wait(this.gate, 500);
            }
            if (this.disposed || control.CancelRequested)
            {
                if (!this.disposed && control.CancelRequested)
                {
                    job.Status = Models.RemoteTransferStatus.Canceled;
                }
                throw new OperationCanceledException("传输已取消");
            }
            if (job.Status == Models.RemoteTransferStatus.Paused)
            {
                job.Status = Models.RemoteTransferStatus.Running;
                this.RaiseChanged(job);
            }
        }
    }

    /// <summary>按编号查找任务。</summary>
    private Models.RemoteTransferJob? FindJob(Guid jobId)
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
    private void SetStatus(Models.RemoteTransferJob job, Models.RemoteTransferStatus status)
    {
        job.Status = status;
        this.RaiseChanged(job);
    }

    /// <summary>触发任务变化事件。</summary>
    private void RaiseChanged(Models.RemoteTransferJob job)
    {
        Action<Models.RemoteTransferJob>? handler = this.JobChanged;
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
