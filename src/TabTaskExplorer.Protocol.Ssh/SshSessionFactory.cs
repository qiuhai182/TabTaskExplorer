using System.Text;
using Renci.SshNet;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Protocol.Ssh;

/// <summary>
/// SSH / SFTP 远程会话工厂：按连接配置建立并复用 SSH 连接，
/// 提供远程终端、远程文件系统与传输队列三种能力。
/// 同一连接配置复用同一条 SSH 连接，避免重复握手。
/// </summary>
public class SshSessionFactory : IRemoteSessionFactory
{
    private readonly object gate = new object();
    private readonly Dictionary<string, SshClient> clients = new Dictionary<string, SshClient>();

    /// <summary>建立（或复用）指定连接配置的 SSH 客户端。</summary>
    public SshClient GetClient(RemoteConnectionProfile profile, string credential)
    {
        lock (this.gate)
        {
            SshClient? existing;
            if (this.clients.TryGetValue(profile.Id, out existing))
            {
                if (existing.IsConnected)
                {
                    return existing;
                }
                this.clients.Remove(profile.Id);
                try
                {
                    existing.Disconnect();
                }
                catch
                {
                    // 断开失败不影响重新连接
                }
                existing.Dispose();
            }

            SshClient client = CreateClient(profile, credential);
            client.Connect();
            if (!client.IsConnected)
            {
                client.Dispose();
                throw new InvalidOperationException("SSH 连接失败：服务器无响应");
            }
            this.clients[profile.Id] = client;
            return client;
        }
    }

    /// <summary>关闭并移除指定连接配置的 SSH 客户端。</summary>
    public void CloseClient(string profileId)
    {
        lock (this.gate)
        {
            SshClient? client;
            if (this.clients.TryGetValue(profileId, out client))
            {
                this.clients.Remove(profileId);
                try
                {
                    client.Disconnect();
                }
                catch
                {
                    // 忽略断开异常
                }
                client.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public ITerminalSession OpenTerminal(RemoteConnectionProfile profile, string credential, int columns, int rows)
    {
        SshClient client = this.GetClient(profile, credential);
        return new SshTerminalSession(this, profile.Id, client, columns, rows);
    }

    /// <inheritdoc />
    public IFileSystemProvider OpenFileSystem(RemoteConnectionProfile profile, string credential)
    {
        return new SftpFileSystemProvider(profile, credential);
    }

    /// <inheritdoc />
    public IRemoteTransferService OpenTransferService(RemoteConnectionProfile profile, string credential)
    {
        return new SftpTransferService(profile, credential);
    }

    /// <summary>根据连接配置与凭据构造 SSH 客户端（保活间隔参考 Xshell 会话保活机制）。</summary>
    private static SshClient CreateClient(RemoteConnectionProfile profile, string credential)
    {
        ConnectionInfo info;
        if (profile.AuthType == RemoteAuthType.PrivateKey && !string.IsNullOrEmpty(profile.PrivateKeyPath))
        {
            PrivateKeyAuthenticationMethod keyMethod = new PrivateKeyAuthenticationMethod(
                profile.Username, new PrivateKeyFile(profile.PrivateKeyPath, credential));
            info = new ConnectionInfo(profile.Host, profile.GetEffectivePort(), profile.Username, keyMethod);
        }
        else
        {
            PasswordAuthenticationMethod passwordMethod = new PasswordAuthenticationMethod(profile.Username, credential);
            info = new ConnectionInfo(profile.Host, profile.GetEffectivePort(), profile.Username, passwordMethod);
        }

        // 保活：每 15 秒发送一次忽略包，防止 NAT 与防火墙切断空闲连接
        info.RetryAttempts = 3;
        info.Timeout = TimeSpan.FromSeconds(15);
        SshClient client = new SshClient(info);
        client.KeepAliveInterval = TimeSpan.FromSeconds(15);
        return client;
    }
}
