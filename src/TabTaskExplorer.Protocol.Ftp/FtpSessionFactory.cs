using FluentFTP;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Protocol.Ftp;

/// <summary>
/// FTP 远程会话工厂：提供远程文件系统与传输队列两种能力。
/// FTP 协议没有远程终端，OpenTerminal 调用会抛出异常。
/// </summary>
public class FtpSessionFactory : IRemoteSessionFactory
{
    /// <summary>创建（或复用）指定连接配置的 FTP 客户端。</summary>
    internal static FtpClient CreateClient(RemoteConnectionProfile profile, string credential)
    {
        FtpClient client = new FtpClient(profile.Host, profile.Username, credential, profile.GetEffectivePort());
        // 每 30 秒发送 NOOP 防止控制连接被切断（对应 SSH 保活机制）
        client.Config.NoopInterval = 30;
        client.Config.ConnectTimeout = 15000;
        client.Config.ReadTimeout = 30000;
        client.Connect();
        if (!client.IsConnected)
        {
            client.Dispose();
            throw new InvalidOperationException("FTP 连接失败：服务器无响应");
        }
        return client;
    }

    /// <inheritdoc />
    public ITerminalSession OpenTerminal(RemoteConnectionProfile profile, string credential, int columns, int rows)
    {
        throw new NotSupportedException("FTP 协议不支持远程终端，请使用 SSH 连接");
    }

    /// <inheritdoc />
    public IFileSystemProvider OpenFileSystem(RemoteConnectionProfile profile, string credential)
    {
        return new FtpFileSystemProvider(profile, credential);
    }

    /// <inheritdoc />
    public IRemoteTransferService OpenTransferService(RemoteConnectionProfile profile, string credential)
    {
        return new FtpTransferService(profile, credential);
    }
}
