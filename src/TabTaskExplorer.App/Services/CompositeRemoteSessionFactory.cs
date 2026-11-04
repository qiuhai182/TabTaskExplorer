using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using TabTaskExplorer.Protocol.Ftp;
using TabTaskExplorer.Protocol.Ssh;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 组合远程会话工厂：按连接配置的协议把请求分派给 SSH 或 FTP 适配工程。
/// 应用层其余代码只依赖 IRemoteSessionFactory，不感知具体协议实现。
/// </summary>
public class CompositeRemoteSessionFactory : IRemoteSessionFactory
{
    private readonly SshSessionFactory sshFactory;
    private readonly FtpSessionFactory ftpFactory = new FtpSessionFactory();

    /// <summary>注入已知主机密钥存储与凭据保护器，SSH 连接前校验主机密钥并按需解密跳板凭据。</summary>
    public CompositeRemoteSessionFactory(KnownHostStore knownHosts, ISecretProtector secretProtector)
    {
        this.sshFactory = new SshSessionFactory(knownHosts, secretProtector,
            message => UiLogger.Log("远程连接审计", message));
    }

    /// <inheritdoc />
    public ITerminalSession OpenTerminal(RemoteConnectionProfile profile, string credential, int columns, int rows)
    {
        if (profile.Protocol == RemoteProtocol.Ftp)
        {
            throw new NotSupportedException("FTP 协议不支持远程终端，请使用 SSH 连接");
        }
        return this.sshFactory.OpenTerminal(profile, credential, columns, rows);
    }

    /// <inheritdoc />
    public IFileSystemProvider OpenFileSystem(RemoteConnectionProfile profile, string credential)
    {
        if (profile.Protocol == RemoteProtocol.Ftp)
        {
            return this.ftpFactory.OpenFileSystem(profile, credential);
        }
        return this.sshFactory.OpenFileSystem(profile, credential);
    }

    /// <inheritdoc />
    public IRemoteTransferService OpenTransferService(RemoteConnectionProfile profile, string credential)
    {
        if (profile.Protocol == RemoteProtocol.Ftp)
        {
            return this.ftpFactory.OpenTransferService(profile, credential);
        }
        return this.sshFactory.OpenTransferService(profile, credential);
    }
}
