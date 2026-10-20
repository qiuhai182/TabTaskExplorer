using FluentFTP;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Protocol.Ftp;

/// <summary>
/// 基于 FTP 的远程传输服务：队列、暂停、断点续传、文件夹递归与重试逻辑
/// 由内核 RemoteTransferServiceBase 模板实现，本类只提供 FTP 连接与流访问。
/// </summary>
public class FtpTransferService : RemoteTransferServiceBase
{
    private readonly RemoteConnectionProfile profile;
    private readonly string credential;

    /// <summary>使用连接配置与明文凭据创建传输服务；连接延迟到任务执行时建立。</summary>
    public FtpTransferService(RemoteConnectionProfile profile, string credential)
    {
        this.profile = profile;
        this.credential = credential;
    }

    /// <summary>建立本次任务专用的 FTP 连接。</summary>
    protected override IRemoteTransferConnection OpenConnection()
    {
        return new FtpTransferConnection(this.profile, this.credential);
    }

    /// <summary>单任务 FTP 连接：任务结束即释放。</summary>
    private class FtpTransferConnection : IRemoteTransferConnection
    {
        private readonly FtpClient client;

        /// <summary>建立并校验 FTP 连接。</summary>
        public FtpTransferConnection(RemoteConnectionProfile profile, string credential)
        {
            this.client = FtpSessionFactory.CreateClient(profile, credential);
        }

        /// <summary>打开远端只读流并定位（断点续传用 restartPosition）。</summary>
        public Stream OpenRemoteRead(string remotePath, long startPosition)
        {
            return this.client.OpenRead(remotePath, FtpDataType.Binary, 0, startPosition);
        }

        /// <summary>打开远端写入流并定位（断点续传从已有长度追加）。</summary>
        public Stream OpenRemoteWrite(string remotePath, long startPosition)
        {
            return this.client.OpenWrite(remotePath, FtpDataType.Binary, startPosition);
        }

        /// <summary>取远端文件大小；不存在返回 -1。</summary>
        public long GetRemoteFileSize(string remotePath)
        {
            if (!this.client.FileExists(remotePath))
            {
                return -1;
            }
            long? size = this.client.GetFileSize(remotePath);
            return size ?? -1;
        }

        /// <summary>判断远端路径是否为文件夹。</summary>
        public bool IsRemoteDirectory(string remotePath)
        {
            return this.client.DirectoryExists(remotePath);
        }

        /// <summary>逐级创建远端目录。</summary>
        public void EnsureRemoteDirectory(string remotePath)
        {
            string normalized = RemotePathUtility.Normalize(remotePath);
            if (normalized == "/" || this.client.DirectoryExists(normalized))
            {
                return;
            }
            string parent = RemotePathUtility.GetParentPath(normalized);
            if (parent != normalized)
            {
                this.EnsureRemoteDirectory(parent);
            }
            if (!this.client.DirectoryExists(normalized))
            {
                this.client.CreateDirectory(normalized);
            }
        }

        /// <summary>列举远端目录的直接子项。</summary>
        public IReadOnlyList<RemoteTransferListEntry> ListRemoteDirectory(string remotePath)
        {
            List<RemoteTransferListEntry> entries = new List<RemoteTransferListEntry>();
            foreach (FtpListItem item in this.client.GetListing(remotePath))
            {
                if (item.Name == "." || item.Name == "..")
                {
                    continue;
                }
                bool isDirectory = item.Type == FtpObjectType.Directory;
                entries.Add(new RemoteTransferListEntry(
                    item.Name, RemotePathUtility.Join(remotePath, item.Name),
                    isDirectory, isDirectory ? 0 : item.Size));
            }
            return entries;
        }

        /// <summary>释放连接。</summary>
        public void Dispose()
        {
            try
            {
                this.client.Disconnect();
            }
            catch
            {
                // 忽略断开异常
            }
            this.client.Dispose();
        }
    }
}
