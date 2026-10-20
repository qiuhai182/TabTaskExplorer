using FluentFTP;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Protocol.Ftp;

/// <summary>
/// 基于 FTP 的远程文件系统提供者：路径规则与 SFTP 提供者一致（远端绝对路径），
/// 能力上比 SFTP 少权限修改（FTP 协议无 POSIX 权限概念）。
/// </summary>
public class FtpFileSystemProvider : IFileSystemProvider, IFileContentAccess
{
    private readonly object connectGate = new object();
    private readonly RemoteConnectionProfile profile;
    private readonly string credential;
    private FtpClient? client;

    /// <summary>使用连接配置与明文凭据创建提供者；连接延迟到首次操作时建立。</summary>
    public FtpFileSystemProvider(RemoteConnectionProfile profile, string credential)
    {
        this.profile = profile;
        this.credential = credential;
    }

    /// <summary>确保 FTP 连接已建立；全部操作开始前调用。</summary>
    private FtpClient EnsureClient()
    {
        lock (this.connectGate)
        {
            if (this.client != null && this.client.IsConnected)
            {
                return this.client;
            }
            if (this.client != null)
            {
                try
                {
                    this.client.Disconnect();
                }
                catch
                {
                    // 断开失败不影响重连
                }
                this.client.Dispose();
                this.client = null;
            }
            FtpClient created = FtpSessionFactory.CreateClient(this.profile, this.credential);
            this.client = created;
            return created;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<FileEntry> ListDirectory(string directoryPath)
    {
        return this.ListDirectory(directoryPath, new DirectoryListingOptions());
    }

    /// <inheritdoc />
    public IReadOnlyList<FileEntry> ListDirectory(string directoryPath, DirectoryListingOptions options)
    {
        FtpClient ftp = this.EnsureClient();
        List<FileEntry> entries = new List<FileEntry>();
        string remotePath = RemotePathUtility.Normalize(directoryPath);

        foreach (FtpListItem item in ftp.GetListing(remotePath))
        {
            bool isHidden = item.Name.StartsWith(".", StringComparison.Ordinal);
            if (isHidden && !options.ShowHiddenFiles)
            {
                continue;
            }
            bool isDirectory = item.Type == FtpObjectType.Directory;
            FileEntry entry = new FileEntry();
            entry.Name = item.Name;
            entry.FullPath = RemotePathUtility.Join(remotePath, item.Name);
            entry.IsDirectory = isDirectory;
            entry.IsHidden = isHidden;
            entry.LengthBytes = isDirectory ? 0 : item.Size;
            entry.ModifiedTimeUtc = item.RawModified > DateTime.MinValue ? item.RawModified.ToUniversalTime() : item.Modified.ToUniversalTime();
            entries.Add(entry);
        }
        return entries;
    }

    /// <inheritdoc />
    public void CreateDirectory(string directoryPath)
    {
        FtpClient ftp = this.EnsureClient();
        string remotePath = RemotePathUtility.Normalize(directoryPath);
        if (ftp.DirectoryExists(remotePath))
        {
            return;
        }
        string parent = RemotePathUtility.GetParentPath(remotePath);
        if (parent != remotePath && !ftp.DirectoryExists(parent))
        {
            this.CreateDirectory(parent);
        }
        ftp.CreateDirectory(remotePath);
    }

    /// <inheritdoc />
    public void Rename(string path, string newName)
    {
        FtpClient ftp = this.EnsureClient();
        string remotePath = RemotePathUtility.Normalize(path);
        string targetPath = RemotePathUtility.Join(RemotePathUtility.GetParentPath(remotePath), newName);
        ftp.Rename(remotePath, targetPath);
    }

    /// <inheritdoc />
    public void Delete(string path)
    {
        FtpClient ftp = this.EnsureClient();
        string remotePath = RemotePathUtility.Normalize(path);
        if (ftp.DirectoryExists(remotePath))
        {
            this.DeleteDirectoryRecursive(ftp, remotePath);
        }
        else if (ftp.FileExists(remotePath))
        {
            ftp.DeleteFile(remotePath);
        }
    }

    /// <inheritdoc />
    public void Copy(string sourcePath, string targetDirectory)
    {
        // FTP 无服务器端复制：经内存/分块流转，目录时递归
        FtpClient ftp = this.EnsureClient();
        string source = RemotePathUtility.Normalize(sourcePath);
        string targetPath = RemotePathUtility.Join(RemotePathUtility.Normalize(targetDirectory), RemotePathUtility.GetFileName(source));
        if (ftp.DirectoryExists(source))
        {
            ftp.CreateDirectory(targetPath);
            foreach (FtpListItem item in ftp.GetListing(source))
            {
                if (item.Name == "." || item.Name == "..")
                {
                    continue;
                }
                this.Copy(item.FullName, targetPath);
            }
            return;
        }
        byte[] content = this.ReadAllBytes(source);
        this.WriteAllBytes(targetPath, content);
    }

    /// <inheritdoc />
    public void Move(string sourcePath, string targetDirectory)
    {
        string source = RemotePathUtility.Normalize(sourcePath);
        string targetPath = RemotePathUtility.Join(RemotePathUtility.Normalize(targetDirectory), RemotePathUtility.GetFileName(source));
        this.EnsureClient().Rename(source, targetPath);
    }

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path)
    {
        FtpClient ftp = this.EnsureClient();
        ftp.DownloadBytes(out byte[]? data, RemotePathUtility.Normalize(path), 0, null, 0);
        return data ?? Array.Empty<byte>();
    }

    /// <inheritdoc />
    public void WriteAllBytes(string path, byte[] content)
    {
        FtpClient ftp = this.EnsureClient();
        string remotePath = RemotePathUtility.Normalize(path);
        FtpStatus status = ftp.UploadBytes(content, remotePath, FtpRemoteExists.Overwrite, false, null);
        if (status == FtpStatus.Failed)
        {
            throw new IOException("FTP 上传失败：" + remotePath);
        }
    }

    /// <summary>递归删除远端目录（含全部子项）。</summary>
    private void DeleteDirectoryRecursive(FtpClient ftp, string remotePath)
    {
        foreach (FtpListItem item in ftp.GetListing(remotePath))
        {
            if (item.Type == FtpObjectType.Directory)
            {
                this.DeleteDirectoryRecursive(ftp, item.FullName);
            }
            else
            {
                ftp.DeleteFile(item.FullName);
            }
        }
        ftp.DeleteDirectory(remotePath);
    }
}
