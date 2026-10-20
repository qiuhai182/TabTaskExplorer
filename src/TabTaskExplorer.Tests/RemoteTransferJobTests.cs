using TabTaskExplorer.Kernel.Models;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>远程传输任务模型测试。</summary>
public class RemoteTransferJobTests
{
    /// <summary>新任务默认排队状态且无错误信息。</summary>
    [Fact]
    public void NewJob_StartsPendingWithoutError()
    {
        RemoteTransferJob job = new RemoteTransferJob(RemoteTransferDirection.Download, "/tmp/a.log", @"C:\temp\a.log");

        Assert.Equal(RemoteTransferStatus.Pending, job.Status);
        Assert.Equal(string.Empty, job.ErrorMessage);
        Assert.Equal(-1, job.TotalBytes);
        Assert.Equal(0, job.TransferredBytes);
    }

    /// <summary>下载任务摘要以"下载"开头并取本地文件名。</summary>
    [Fact]
    public void DownloadSummary_UsesLocalFileName()
    {
        RemoteTransferJob job = new RemoteTransferJob(RemoteTransferDirection.Download, "/var/log/syslog", "/home/user/syslog");

        Assert.Equal("下载 syslog", job.Summary);
    }

    /// <summary>上传任务摘要以"上传"开头并取本地文件名。</summary>
    [Fact]
    public void UploadSummary_UsesLocalFileName()
    {
        RemoteTransferJob job = new RemoteTransferJob(RemoteTransferDirection.Upload, "/tmp/backup.zip", @"D:\data\backup.zip");

        Assert.Equal("上传 backup.zip", job.Summary);
    }

    /// <summary>不同任务分配不同编号。</summary>
    [Fact]
    public void Jobs_HaveUniqueIds()
    {
        RemoteTransferJob first = new RemoteTransferJob(RemoteTransferDirection.Download, "/a", @"C:\a");
        RemoteTransferJob second = new RemoteTransferJob(RemoteTransferDirection.Download, "/b", @"C:\b");

        Assert.NotEqual(first.Id, second.Id);
    }
}
