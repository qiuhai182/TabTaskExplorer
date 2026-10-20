using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>传输任务行：包装 RemoteTransferJob 并提供界面展示文本。</summary>
public class TransferJobRow : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>底层传输任务。</summary>
    public RemoteTransferJob Job { get; }

    /// <summary>任务摘要文本。</summary>
    public string Summary
    {
        get { return this.Job.Summary; }
    }

    /// <summary>状态与进度展示文本。</summary>
    public string StatusText
    {
        get
        {
            string status = this.Job.Status switch
            {
                RemoteTransferStatus.Pending => "排队中",
                RemoteTransferStatus.Running => "传输中",
                RemoteTransferStatus.Paused => "已暂停",
                RemoteTransferStatus.Completed => "已完成",
                RemoteTransferStatus.Failed => "失败",
                RemoteTransferStatus.Canceled => "已取消",
                _ => "未知",
            };
            if (this.Job.TotalBytes > 0)
            {
                double percent = Math.Round(this.Job.TransferredBytes * 100.0 / this.Job.TotalBytes, 1);
                return status + " " + percent + "%（" + FormatBytes(this.Job.TransferredBytes) + " / " + FormatBytes(this.Job.TotalBytes) + "）";
            }
            return status + "（" + FormatBytes(this.Job.TransferredBytes) + "）";
        }
    }

    /// <summary>是否允许暂停（排队或传输中）。</summary>
    public bool CanPause
    {
        get { return this.Job.Status == RemoteTransferStatus.Pending || this.Job.Status == RemoteTransferStatus.Running; }
    }

    /// <summary>是否允许继续（暂停、失败或取消后均可从断点继续）。</summary>
    public bool CanResume
    {
        get
        {
            return this.Job.Status == RemoteTransferStatus.Paused
                || this.Job.Status == RemoteTransferStatus.Failed
                || this.Job.Status == RemoteTransferStatus.Canceled;
        }
    }

    /// <summary>是否允许取消（尚未结束的任务）。</summary>
    public bool CanCancel
    {
        get
        {
            return this.Job.Status == RemoteTransferStatus.Pending
                || this.Job.Status == RemoteTransferStatus.Running
                || this.Job.Status == RemoteTransferStatus.Paused;
        }
    }

    /// <summary>使用传输任务创建展示行。</summary>
    public TransferJobRow(RemoteTransferJob job)
    {
        this.Job = job;
    }

    /// <summary>底层任务状态变化后刷新展示文本。</summary>
    public void Refresh()
    {
        this.RaisePropertyChanged(nameof(this.StatusText));
        this.RaisePropertyChanged(nameof(this.CanPause));
        this.RaisePropertyChanged(nameof(this.CanResume));
        this.RaisePropertyChanged(nameof(this.CanCancel));
    }

    /// <summary>字节数格式化：自动选择 KB / MB / GB 单位。</summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1073741824)
        {
            return Math.Round(bytes / 1073741824.0, 2) + " GB";
        }
        if (bytes >= 1048576)
        {
            return Math.Round(bytes / 1048576.0, 2) + " MB";
        }
        if (bytes >= 1024)
        {
            return Math.Round(bytes / 1024.0, 1) + " KB";
        }
        return bytes + " B";
    }

    /// <summary>触发属性变更通知。</summary>
    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

/// <summary>
/// 远程传输队列标签页：展示指定连接的传输任务列表（参考 Xftp 传输队列），
/// 支持上传 / 下载入队、暂停、继续（断点续传）与取消。
/// </summary>
public class RemoteTransferPanelViewModel : INotifyPropertyChanged
{
    private readonly IRemoteTransferService service;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>标签页标题。</summary>
    public string Title { get; }

    /// <summary>关联的连接配置编号。</summary>
    public string AttachedProfileId { get; set; } = string.Empty;

    /// <summary>任务行集合。</summary>
    public ObservableCollection<TransferJobRow> Jobs { get; } = new ObservableCollection<TransferJobRow>();

    /// <summary>使用指定连接名称与传输服务创建面板。</summary>
    public RemoteTransferPanelViewModel(string connectionName, IRemoteTransferService service)
    {
        this.Title = connectionName + " - 传输队列";
        this.service = service;
        this.service.JobChanged += this.OnJobChanged;

        foreach (RemoteTransferJob job in this.service.GetJobs())
        {
            this.Jobs.Add(new TransferJobRow(job));
        }
    }

    /// <summary>把本地文件上传到指定远端路径。</summary>
    public void EnqueueUpload(string localPath, string remotePath)
    {
        this.service.EnqueueUpload(localPath, remotePath);
    }

    /// <summary>把远端文件下载到指定本地路径。</summary>
    public void EnqueueDownload(string remotePath, string localPath)
    {
        this.service.EnqueueDownload(remotePath, localPath);
    }

    /// <summary>暂停指定任务。</summary>
    public void Pause(TransferJobRow row)
    {
        this.service.Pause(row.Job.Id);
    }

    /// <summary>继续（或断点续传）指定任务。</summary>
    public void Resume(TransferJobRow row)
    {
        this.service.Resume(row.Job.Id);
    }

    /// <summary>取消指定任务。</summary>
    public void Cancel(TransferJobRow row)
    {
        this.service.Cancel(row.Job.Id);
    }

    /// <summary>任务状态变化（可能在后台线程触发）：调度到 UI 线程刷新对应行。</summary>
    private void OnJobChanged(RemoteTransferJob job)
    {
        Dispatcher.UIThread.Post(delegate
        {
            foreach (TransferJobRow row in this.Jobs)
            {
                if (row.Job.Id == job.Id)
                {
                    row.Refresh();
                    return;
                }
            }
            this.Jobs.Add(new TransferJobRow(job));
        });
    }

    /// <summary>触发属性变更通知。</summary>
    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
