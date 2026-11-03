using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Protocol.Ssh;

/// <summary>
/// SSH / SFTP 远程会话工厂：按连接配置复用 SSH 终端连接与 SFTP 文件系统，
/// 连接前强制校验主机密钥；空闲连接按阈值回收，断线后下一次操作自动重建。
/// </summary>
public class SshSessionFactory : IRemoteSessionFactory
{
    /// <summary>空闲回收阈值（分钟）：超过后释放连接，下次连接重新握手。</summary>
    private const int IdleMinutes = 10;

    private readonly KnownHostStore knownHosts;
    private readonly object gate = new object();
    private readonly Dictionary<string, ClientEntry> clients = new Dictionary<string, ClientEntry>();
    private readonly Dictionary<string, ProviderEntry> fileSystems = new Dictionary<string, ProviderEntry>();

    public SshSessionFactory(KnownHostStore knownHosts)
    {
        this.knownHosts = knownHosts;
    }

    /// <summary>建立（或复用）指定连接配置的 SSH 客户端；主机密钥未信任或发生变化时抛出专用异常。</summary>
    public SshClient GetClient(RemoteConnectionProfile profile, string credential)
    {
        lock (this.gate)
        {
            this.PruneIdle();
            ClientEntry? existing;
            if (this.clients.TryGetValue(profile.Id, out existing))
            {
                if (existing.Client.IsConnected)
                {
                    existing.LastUsed = DateTime.Now;
                    return existing.Client;
                }
                this.clients.Remove(profile.Id);
                try
                {
                    existing.Client.Disconnect();
                }
                catch
                {
                    // 断开失败不影响重新连接
                }
                existing.Client.Dispose();
            }

            SshClient client = CreateClient(profile, credential);
            string keyType = string.Empty;
            string actualFingerprint = string.Empty;
            string expectedFingerprint = string.Empty;
            bool unknown = false;
            bool mismatch = false;
            client.HostKeyReceived += delegate(object? sender, HostKeyEventArgs e)
            {
                keyType = string.IsNullOrEmpty(e.HostKeyName) ? "unknown" : e.HostKeyName;
                actualFingerprint = e.FingerPrintSHA256;
                KnownHostEntry? known = this.knownHosts.Find(profile.Host, profile.GetEffectivePort(), keyType);
                if (known == null)
                {
                    unknown = true;
                    e.CanTrust = false;
                    return;
                }
                if (!string.Equals(known.FingerprintSha256, NormalizeFingerprint(actualFingerprint),
                    StringComparison.OrdinalIgnoreCase))
                {
                    mismatch = true;
                    expectedFingerprint = known.FingerprintSha256;
                    e.CanTrust = false;
                    return;
                }
                e.CanTrust = true;
            };

            Exception? failure = null;
            try
            {
                client.Connect();
            }
            catch (Exception error)
            {
                failure = error;
            }
            if (failure != null)
            {
                client.Dispose();
                if (unknown)
                {
                    throw new HostKeyUnknownException(profile.Host, profile.GetEffectivePort(), keyType,
                        actualFingerprint, failure);
                }
                if (mismatch)
                {
                    throw new HostKeyMismatchException(profile.Host, profile.GetEffectivePort(), keyType,
                        expectedFingerprint, actualFingerprint, failure);
                }
                throw failure;
            }
            if (!client.IsConnected)
            {
                client.Dispose();
                throw new InvalidOperationException("SSH 连接失败：服务器无响应");
            }
            this.clients[profile.Id] = new ClientEntry(client);
            return client;
        }
    }

    /// <summary>关闭并移除指定连接配置的 SSH 客户端。</summary>
    public void CloseClient(string profileId)
    {
        lock (this.gate)
        {
            ClientEntry? entry;
            if (this.clients.TryGetValue(profileId, out entry))
            {
                this.clients.Remove(profileId);
                try
                {
                    entry.Client.Disconnect();
                }
                catch
                {
                    // 忽略断开异常
                }
                entry.Client.Dispose();
            }
            this.fileSystems.Remove(profileId);
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
        string credentialHash = HashCredential(credential);
        lock (this.gate)
        {
            this.PruneIdle();
            ProviderEntry? existing;
            if (this.fileSystems.TryGetValue(profile.Id, out existing)
                && existing.CredentialHash == credentialHash)
            {
                existing.LastUsed = DateTime.Now;
                return existing.Provider;
            }
            SftpFileSystemProvider provider = new SftpFileSystemProvider(profile, credential, this.knownHosts);
            this.fileSystems[profile.Id] = new ProviderEntry(provider, credentialHash);
            return provider;
        }
    }

    /// <inheritdoc />
    public IRemoteTransferService OpenTransferService(RemoteConnectionProfile profile, string credential)
    {
        return new SftpTransferService(profile, credential, this.knownHosts);
    }

    /// <summary>根据连接配置与凭据构造 SSH 客户端（保活间隔参考 Xshell 会话保活机制）。</summary>
    private static SshClient CreateClient(RemoteConnectionProfile profile, string credential)
    {
        ConnectionInfo info = BuildConnectionInfo(profile, credential);
        SshClient client = new SshClient(info);
        int keepAliveSeconds = profile.KeepAliveSeconds > 0 ? profile.KeepAliveSeconds : 15;
        client.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveSeconds);
        return client;
    }

    /// <summary>根据连接配置与凭据构造连接信息，供本工程各实现共用。</summary>
    internal static ConnectionInfo BuildConnectionInfo(RemoteConnectionProfile profile, string credential)
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
            // 部分服务器（UOS / 麒麟等 PAM 环境）只开放 keyboard-interactive，不开放 password；
            // 两种方式同时提供给 SSH.NET，由服务器协商选择，避免仅支持纯密码时认证被拒
            KeyboardInteractiveAuthenticationMethod keyboardMethod = new KeyboardInteractiveAuthenticationMethod(profile.Username);
            keyboardMethod.AuthenticationPrompt += delegate(object? sender, AuthenticationPromptEventArgs e)
            {
                foreach (AuthenticationPrompt prompt in e.Prompts)
                {
                    prompt.Response = credential;
                }
            };
            info = new ConnectionInfo(profile.Host, profile.GetEffectivePort(), profile.Username, passwordMethod, keyboardMethod);
        }

        info.RetryAttempts = 3;
        info.Timeout = TimeSpan.FromSeconds(15);
        return info;
    }

    /// <summary>清理超过空闲阈值的终端连接与文件系统。</summary>
    private void PruneIdle()
    {
        DateTime threshold = DateTime.Now.AddMinutes(-IdleMinutes);
        List<string> expired = new List<string>();
        foreach (KeyValuePair<string, ClientEntry> pair in this.clients)
        {
            if (pair.Value.LastUsed < threshold)
            {
                expired.Add(pair.Key);
            }
        }
        foreach (string profileId in expired)
        {
            ClientEntry entry = this.clients[profileId];
            this.clients.Remove(profileId);
            try
            {
                entry.Client.Disconnect();
            }
            catch
            {
                // 忽略断开异常
            }
            entry.Client.Dispose();
        }
        List<string> expiredProviders = new List<string>();
        foreach (KeyValuePair<string, ProviderEntry> pair in this.fileSystems)
        {
            if (pair.Value.LastUsed < threshold)
            {
                expiredProviders.Add(pair.Key);
            }
        }
        foreach (string profileId in expiredProviders)
        {
            this.fileSystems.Remove(profileId);
        }
    }

    /// <summary>凭据指纹只用于判断缓存是否过期，不落盘、不写日志。</summary>
    private static string HashCredential(string credential)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(credential ?? string.Empty));
        return Convert.ToHexString(hash).Substring(0, 16);
    }

    private static string NormalizeFingerprint(string fingerprint)
    {
        string text = (fingerprint ?? string.Empty).Trim();
        if (text.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring("SHA256:".Length);
        }
        return text;
    }

    /// <summary>终端连接缓存项：记录最近使用时间用于空闲回收。</summary>
    private sealed class ClientEntry
    {
        public ClientEntry(SshClient client)
        {
            this.Client = client;
            this.LastUsed = DateTime.Now;
        }

        public SshClient Client { get; }

        public DateTime LastUsed { get; set; }
    }

    /// <summary>SFTP 文件系统缓存项：按凭据指纹判断是否需要重建。</summary>
    private sealed class ProviderEntry
    {
        public ProviderEntry(SftpFileSystemProvider provider, string credentialHash)
        {
            this.Provider = provider;
            this.CredentialHash = credentialHash;
            this.LastUsed = DateTime.Now;
        }

        public SftpFileSystemProvider Provider { get; }

        public string CredentialHash { get; }

        public DateTime LastUsed { get; set; }
    }
}
