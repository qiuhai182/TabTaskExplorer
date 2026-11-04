using System;
using System.Collections.Generic;
using System.Linq;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services.Tasks;

/// <summary>
/// 远程传输队列到统一任务快照的只读适配：不重构远程传输服务，
/// 仅把已有 RemoteTransferJob 的进度、取消与继续能力映射到同一任务模型。
/// </summary>
public static class RemoteTaskAdapter
{
    /// <summary>汇总所有远程传输面板中的任务。</summary>
    public static List<FileTaskSnapshot> Snapshot(IEnumerable<object> tabs)
    {
        List<FileTaskSnapshot> result = new List<FileTaskSnapshot>();
        foreach (object tab in tabs)
        {
            RemoteTransferPanelViewModel? panel = tab as RemoteTransferPanelViewModel;
            if (panel == null)
            {
                continue;
            }
            foreach (TransferJobRow row in panel.Jobs)
            {
                result.Add(ToSnapshot(row));
            }
        }
        return result;
    }

    private static FileTaskSnapshot ToSnapshot(TransferJobRow row)
    {
        RemoteTransferJob job = row.Job;
        bool download = job.Direction == RemoteTransferDirection.Download;
        long total = job.TotalBytes;
        int progress = total > 0 ? (int)Math.Round(job.TransferredBytes * 100.0 / total) : 0;
        return new FileTaskSnapshot
        {
            Id = job.Id.ToString("N"),
            Kind = download ? "download" : "upload",
            IsRemote = true,
            State = StateText(job.Status),
            Total = 1,
            Processed = job.Status == RemoteTransferStatus.Completed ? 1 : 0,
            Failed = job.Status == RemoteTransferStatus.Failed ? 1 : 0,
            Progress = progress,
            SourceText = download ? job.RemotePath : job.LocalPath,
            DestinationText = download ? job.LocalPath : job.RemotePath,
            Message = job.ErrorMessage.Length > 0 ? job.ErrorMessage : row.StatusText,
            CanCancel = row.CanCancel,
            CanRetry = row.CanResume,
            CreatedAt = string.Empty,
        };
    }

    private static string StateText(RemoteTransferStatus status)
    {
        if (status == RemoteTransferStatus.Pending) { return "queued"; }
        if (status == RemoteTransferStatus.Running) { return "running"; }
        if (status == RemoteTransferStatus.Paused) { return "paused"; }
        if (status == RemoteTransferStatus.Completed) { return "completed"; }
        if (status == RemoteTransferStatus.Failed) { return "failed"; }
        if (status == RemoteTransferStatus.Canceled) { return "canceled"; }
        return "queued";
    }

    /// <summary>取消远程任务；找不到对应面板或行返回 false。</summary>
    public static bool Cancel(IEnumerable<object> tabs, string id)
    {
        foreach (object tab in tabs)
        {
            RemoteTransferPanelViewModel? panel = tab as RemoteTransferPanelViewModel;
            if (panel == null)
            {
                continue;
            }
            foreach (TransferJobRow row in panel.Jobs)
            {
                if (row.Job.Id.ToString("N") == id)
                {
                    panel.Cancel(row);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>继续/重试远程任务；找不到对应面板或行返回 false。</summary>
    public static bool Retry(IEnumerable<object> tabs, string id)
    {
        foreach (object tab in tabs)
        {
            RemoteTransferPanelViewModel? panel = tab as RemoteTransferPanelViewModel;
            if (panel == null)
            {
                continue;
            }
            foreach (TransferJobRow row in panel.Jobs)
            {
                if (row.Job.Id.ToString("N") == id)
                {
                    if (!row.CanResume)
                    {
                        return false;
                    }
                    panel.Resume(row);
                    return true;
                }
            }
        }
        return false;
    }
}
