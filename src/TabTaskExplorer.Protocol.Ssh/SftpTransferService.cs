using Renci.SshNet;
using Renci.SshNet.Sftp;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Protocol.Ssh;

/// <summary>
/// 基于 SFTP 的远程传输服务：队列、暂停、断点续传、文件夹递归与重试逻辑
/// 由内核 RemoteTransferServiceBase 模板实现，本类只提供 SFTP 连接与流访问。
/// </summary>
public class SftpTransferService : RemoteTransferServiceBase
{
    private readonly RemoteConnectionProfile profile;
    private readonly string credential;

    /// <summary>使用连接配置与明文凭据创建传输服务；连接延迟到任务执行时建立。</summary>
    public SftpTransferService(RemoteConnectionProfile profile, string credential)
    {
        this.profile = profile;
        this.credential = credential;
    }

    /// <summary>建立本次任务专用的 SFTP 连接。</summary>
    protected override IRemoteTransferConnection OpenConnection()
    {
        return new SftpTransferConnection(this.profile, this.credential);
    }

    /// <summary>单任务 SFTP 连接：任务结束即释放。</summary>
    private class SftpTransferConnection : IRemoteTransferConnection
    {
        private readonly SftpClient client;

        /// <summary>建立并校验 SFTP 连接。</summary>
        public SftpTransferConnection(RemoteConnectionProfile profile, string credential)
        {
            ConnectionInfo info = SshSessionFactory.BuildConnectionInfo(profile, credential);
            this.client = new SftpClient(info);
            this.client.KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(profile.KeepAliveSeconds, 5));
            this.client.Connect();
            if (!this.client.IsConnected)
            {
                this.client.Dispose();
                throw new InvalidOperationException("SFTP 连接失败：服务器无响应");
            }
        }

        /// <summary>打开远端只读流并定位。</summary>
        public Stream OpenRemoteRead(string remotePath, long startPosition)
        {
            SftpFileStream stream = this.client.OpenRead(remotePath);
            stream.Seek(startPosition, SeekOrigin.Begin);
            return stream;
        }

        /// <summary>打开远端写入流（不存在则创建）并定位。</summary>
        public Stream OpenRemoteWrite(string remotePath, long startPosition)
        {
            SftpFileStream stream = this.client.Open(remotePath, FileMode.OpenOrCreate, FileAccess.Write);
            stream.Seek(startPosition, SeekOrigin.Begin);
            return stream;
        }

        /// <summary>取远端文件大小；不存在返回 -1。</summary>
        public long GetRemoteFileSize(string remotePath)
        {
            if (!this.client.Exists(remotePath))
            {
                return -1;
            }
            return this.client.GetAttributes(remotePath).Size;
        }

        /// <summary>判断远端路径是否为文件夹。</summary>
        public bool IsRemoteDirectory(string remotePath)
        {
            return this.client.Exists(remotePath) && this.client.GetAttributes(remotePath).IsDirectory;
        }

        /// <summary>逐级创建远端目录。</summary>
        public void EnsureRemoteDirectory(string remotePath)
        {
            string normalized = SftpFileSystemProvider.NormalizeRemotePath(remotePath);
            if (normalized == "/" || this.client.Exists(normalized))
            {
                return;
            }
            string parent = SftpFileSystemProvider.GetParentRemotePath(normalized);
            if (parent != normalized)
            {
                this.EnsureRemoteDirectory(parent);
            }
            if (!this.client.Exists(normalized))
            {
                this.client.CreateDirectory(normalized);
            }
        }

        /// <summary>列举远端目录的直接子项。</summary>
        public IReadOnlyList<RemoteTransferListEntry> ListRemoteDirectory(string remotePath)
        {
            List<RemoteTransferListEntry> entries = new List<RemoteTransferListEntry>();
            foreach (ISftpFile file in this.client.ListDirectory(remotePath))
            {
                if (file.Name == "." || file.Name == "..")
                {
                    continue;
                }
                entries.Add(new RemoteTransferListEntry(
                    file.Name, SftpFileSystemProvider.JoinRemotePath(remotePath, file.Name),
                    file.IsDirectory, file.IsDirectory ? 0 : file.Length));
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
