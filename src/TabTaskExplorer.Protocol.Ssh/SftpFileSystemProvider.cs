using Renci.SshNet;
using Renci.SshNet.Sftp;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Protocol.Ssh;

/// <summary>
/// 基于 SFTP 的远程文件系统提供者：把远端目录接入标准 IFileSystemProvider 能力，
/// 路径采用远端风格的绝对路径（例如 /home/user），
/// 因此远程文件夹打开为文件标签后，批量改名、搜索、信息窗格等既有能力全部自动复用。
/// </summary>
public class SftpFileSystemProvider : IFileSystemProvider, IFileContentAccess, IRemotePermissionChanger
{
    private const int CopyChunkSize = 64 * 1024;

    private readonly object connectGate = new object();
    private readonly RemoteConnectionProfile profile;
    private readonly string credential;
    private SftpClient? client;

    /// <summary>使用连接配置与明文凭据创建提供者；连接延迟到首次操作时建立，避免阻塞调用线程。</summary>
    public SftpFileSystemProvider(RemoteConnectionProfile profile, string credential)
    {
        this.profile = profile;
        this.credential = credential;
    }

    /// <summary>确保 SFTP 连接已建立；全部操作开始前调用。</summary>
    private SftpClient EnsureClient()
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

            ConnectionInfo info = SshSessionFactory.BuildConnectionInfo(this.profile, this.credential);
            SftpClient created = new SftpClient(info);
            created.KeepAliveInterval = TimeSpan.FromSeconds(15);
            created.Connect();
            if (!created.IsConnected)
            {
                created.Dispose();
                throw new InvalidOperationException("SFTP 连接失败：服务器无响应");
            }
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
        List<FileEntry> entries = new List<FileEntry>();
        string remotePath = NormalizeRemotePath(directoryPath);
        SftpClient sftp = this.EnsureClient();

        IEnumerable<ISftpFile> files = sftp.ListDirectory(remotePath);
        foreach (ISftpFile file in files)
        {
            // SFTP 目录列表包含 . 与 .. 两个特殊条目，与本地目录列举行为保持一致而过滤掉
            if (file.Name == "." || file.Name == "..")
            {
                continue;
            }
            bool isHidden = file.Name.StartsWith(".", StringComparison.Ordinal);
            if (isHidden && !options.ShowHiddenFiles)
            {
                continue;
            }

            FileEntry entry = new FileEntry();
            entry.Name = file.Name;
            entry.FullPath = JoinRemotePath(remotePath, file.Name);
            entry.IsDirectory = file.IsDirectory;
            entry.IsHidden = isHidden;
            entry.LengthBytes = file.IsDirectory ? 0 : file.Length;
            entry.ModifiedTimeUtc = file.LastWriteTimeUtc;
            entries.Add(entry);
        }
        return entries;
    }

    /// <inheritdoc />
    public void CreateDirectory(string directoryPath)
    {
        SftpClient sftp = this.EnsureClient();
        string remotePath = NormalizeRemotePath(directoryPath);
        if (sftp.Exists(remotePath))
        {
            return;
        }

        // 逐级补齐缺失的上级目录
        string parent = GetParentRemotePath(remotePath);
        if (parent != remotePath)
        {
            this.CreateDirectory(parent);
        }
        sftp.CreateDirectory(remotePath);
    }

    /// <inheritdoc />
    public void Rename(string path, string newName)
    {
        SftpClient sftp = this.EnsureClient();
        string remotePath = NormalizeRemotePath(path);
        string targetPath = JoinRemotePath(GetParentRemotePath(remotePath), newName);
        if (sftp.Exists(remotePath))
        {
            if (sftp.Exists(targetPath))
            {
                throw new IOException("目标名称已存在：" + newName);
            }
        }
        sftp.RenameFile(remotePath, targetPath);
    }

    /// <inheritdoc />
    public void Delete(string path)
    {
        SftpClient sftp = this.EnsureClient();
        string remotePath = NormalizeRemotePath(path);
        if (!sftp.Exists(remotePath))
        {
            return;
        }

        SftpFileAttributes attributes = sftp.GetAttributes(remotePath);
        if (attributes.IsDirectory)
        {
            this.DeleteDirectoryRecursive(sftp, remotePath);
        }
        else
        {
            sftp.DeleteFile(remotePath);
        }
    }

    /// <inheritdoc />
    public void Copy(string sourcePath, string targetDirectory)
    {
        SftpClient sftp = this.EnsureClient();
        string source = NormalizeRemotePath(sourcePath);
        string targetDir = NormalizeRemotePath(targetDirectory);
        string targetPath = JoinRemotePath(targetDir, GetRemoteFileName(source));
        this.CopyRecursive(sftp, source, targetPath);
    }

    /// <inheritdoc />
    public void Move(string sourcePath, string targetDirectory)
    {
        SftpClient sftp = this.EnsureClient();
        string source = NormalizeRemotePath(sourcePath);
        string targetDir = NormalizeRemotePath(targetDirectory);
        string targetPath = JoinRemotePath(targetDir, GetRemoteFileName(source));
        sftp.RenameFile(source, targetPath);
    }

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path)
    {
        SftpClient sftp = this.EnsureClient();
        using (SftpFileStream stream = sftp.OpenRead(NormalizeRemotePath(path)))
        using (MemoryStream buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    /// <inheritdoc />
    public void WriteAllBytes(string path, byte[] content)
    {
        SftpClient sftp = this.EnsureClient();
        using (SftpFileStream stream = sftp.OpenWrite(NormalizeRemotePath(path)))
        {
            stream.Write(content, 0, content.Length);
            stream.SetLength(content.Length);
        }
    }

    /// <inheritdoc />
    public void ChangePermissions(string path, int octalMode)
    {
        SftpClient sftp = this.EnsureClient();
        sftp.ChangePermissions(NormalizeRemotePath(path), (short)octalMode);
    }

    /// <summary>把任意输入规整为以 / 开头的远端绝对路径。</summary>
    public static string NormalizeRemotePath(string path)
    {
        string trimmed = (path ?? string.Empty).Replace('\\', '/').Trim();
        if (trimmed.Length == 0)
        {
            return "/";
        }
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = "/" + trimmed;
        }

        // 折叠连续斜杠并去除结尾斜杠
        while (trimmed.Contains("//"))
        {
            trimmed = trimmed.Replace("//", "/");
        }
        while (trimmed.Length > 1 && trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimEnd('/');
        }
        return trimmed;
    }

    /// <summary>取远端路径的最后一段名称。</summary>
    public static string GetRemoteFileName(string path)
    {
        string normalized = NormalizeRemotePath(path);
        if (normalized == "/")
        {
            return normalized;
        }
        int index = normalized.LastIndexOf('/');
        return normalized.Substring(index + 1);
    }

    /// <summary>取远端路径的父目录；根目录返回自身。</summary>
    public static string GetParentRemotePath(string path)
    {
        string normalized = NormalizeRemotePath(path);
        int index = normalized.LastIndexOf('/');
        if (index <= 0)
        {
            return "/";
        }
        return normalized.Substring(0, index);
    }

    /// <summary>拼接远端目录与名称。</summary>
    public static string JoinRemotePath(string directory, string name)
    {
        string dir = NormalizeRemotePath(directory);
        if (dir == "/")
        {
            return "/" + name;
        }
        return dir + "/" + name;
    }

    /// <summary>递归删除远端目录（含全部子项）。</summary>
    private void DeleteDirectoryRecursive(SftpClient sftp, string remotePath)
    {
        foreach (ISftpFile file in sftp.ListDirectory(remotePath))
        {
            if (file.Name == "." || file.Name == "..")
            {
                continue;
            }
            if (file.IsDirectory)
            {
                this.DeleteDirectoryRecursive(sftp, file.FullName);
            }
            else
            {
                sftp.DeleteFile(file.FullName);
            }
        }
        sftp.DeleteDirectory(remotePath);
    }

    /// <summary>递归复制：SFTP 无服务器端复制，文件经分块读写流转。</summary>
    private void CopyRecursive(SftpClient sftp, string source, string target)
    {
        SftpFileAttributes attributes = sftp.GetAttributes(source);
        if (!attributes.IsDirectory)
        {
            this.CopyFileChunked(sftp, source, target);
            return;
        }

        if (!sftp.Exists(target))
        {
            sftp.CreateDirectory(target);
        }
        foreach (ISftpFile file in sftp.ListDirectory(source))
        {
            if (file.Name == "." || file.Name == "..")
            {
                continue;
            }
            this.CopyRecursive(sftp, JoinRemotePath(source, file.Name), JoinRemotePath(target, file.Name));
        }
    }

    /// <summary>分块复制单个远端文件。</summary>
    private void CopyFileChunked(SftpClient sftp, string source, string target)
    {
        using (SftpFileStream sourceStream = sftp.OpenRead(source))
        using (SftpFileStream targetStream = sftp.OpenWrite(target))
        {
            byte[] buffer = new byte[CopyChunkSize];
            while (true)
            {
                int read = sourceStream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                targetStream.Write(buffer, 0, read);
            }
        }
    }
}
