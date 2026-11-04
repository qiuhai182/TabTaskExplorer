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
    private readonly ISecretProtector? protector;
    private readonly Action<string>? log;
    private readonly object gate = new object();
    private readonly Dictionary<string, ClientEntry> clients = new Dictionary<string, ClientEntry>();
    private readonly Dictionary<string, ProviderEntry> fileSystems = new Dictionary<string, ProviderEntry>();

    /// <summary>注入已知主机存储；protector 用于解密跳板凭据，log 记录逐跳连接审计。</summary>
    public SshSessionFactory(KnownHostStore knownHosts, ISecretProtector? protector = null, Action<string>? log = null)
    {
        this.knownHosts = knownHosts;
        this.protector = protector;
        this.log = log;
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
                if (existing.Chain != null)
                {
                    existing.Chain.Dispose();
                }
            }

            SshJumpChain? chain = this.CreateJumpChain(profile, credential);
            SshClient client = CreateClient(profile, credential, chain);
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
                if (chain != null)
                {
                    chain.Dispose();
                }
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
                if (chain != null)
                {
                    chain.Dispose();
                }
                throw new InvalidOperationException("SSH 连接失败：服务器无响应");
            }
            this.clients[profile.Id] = new ClientEntry(client, chain);
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
                if (entry.Chain != null)
                {
                    entry.Chain.Dispose();
                }
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
        string credentialHash = HashCredential(credential + "|" + BuildJumpSignature(profile));
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
            SftpFileSystemProvider provider = new SftpFileSystemProvider(profile, credential, this.knownHosts,
                delegate { return this.CreateJumpChain(profile, credential); });
            this.fileSystems[profile.Id] = new ProviderEntry(provider, credentialHash);
            return provider;
        }
    }

    /// <inheritdoc />
    public IRemoteTransferService OpenTransferService(RemoteConnectionProfile profile, string credential)
    {
        Func<SshJumpChain?> jumpChainFactory = delegate { return this.CreateJumpChain(profile, credential); };
        return new SftpTransferService(profile, credential, this.knownHosts, jumpChainFactory);
    }

    /// <summary>建立跳板链；未配置跳板时返回 null。凭据只在此处解密，明文不进入日志与模型。</summary>
    private SshJumpChain? CreateJumpChain(RemoteConnectionProfile profile, string credential)
    {
        if (!profile.HasJumpHops)
        {
            return null;
        }
        string? validationError = profile.ValidateJumpChain();
        if (validationError != null)
        {
            throw new ArgumentException(validationError, nameof(profile));
        }
        List<SshJumpHopCredential> hops = new List<SshJumpHopCredential>();
        foreach (RemoteJumpHop hop in profile.JumpHops)
        {
            string hopSecret = string.Empty;
            if (!string.IsNullOrEmpty(hop.ProtectedSecret))
            {
                hopSecret = this.protector == null ? hop.ProtectedSecret : this.protector.Unprotect(hop.ProtectedSecret);
            }
            hops.Add(new SshJumpHopCredential(hop, hopSecret));
        }
        this.Log("跳板链开始建立：" + profile.Name + " 跳数=" + hops.Count + " 链路=" + profile.JumpSummary);
        return SshJumpChain.Connect(profile, credential, hops, this.knownHosts, this.Log);
    }

    private void Log(string message)
    {
        Action<string>? handler = this.log;
        if (handler != null)
        {
            handler(message);
        }
    }

    /// <summary>跳板配置指纹：用于判断缓存的远程文件系统是否需要因跳板变更而重建，不记录明文凭据。</summary>
    private static string BuildJumpSignature(RemoteConnectionProfile profile)
    {
        if (!profile.HasJumpHops)
        {
            return string.Empty;
        }
        string text = string.Empty;
        foreach (RemoteJumpHop hop in profile.JumpHops)
        {
            text += hop.Host + ":" + hop.GetEffectivePort() + "|" + hop.Username + "|" + hop.AuthType + "|"
                + hop.ProtectedSecret + ";";
        }
        return text;
    }

    /// <summary>根据连接配置与凭据构造 SSH 客户端（保活间隔参考 Xshell 会话保活机制）。</summary>
    private static SshClient CreateClient(RemoteConnectionProfile profile, string credential, SshJumpChain? chain)
    {
        ConnectionInfo info = chain == null
            ? BuildConnectionInfo(profile, credential)
            : chain.TargetInfo;
        SshClient client = new SshClient(info);
        int keepAliveSeconds = profile.KeepAliveSeconds > 0 ? profile.KeepAliveSeconds : 15;
        client.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveSeconds);
        return client;
    }

    /// <summary>根据连接配置与凭据构造连接信息，供本工程各实现共用。</summary>
    internal static ConnectionInfo BuildConnectionInfo(RemoteConnectionProfile profile, string credential)
    {
        return BuildConnectionInfo(profile.Host, profile.GetEffectivePort(), profile.Username, profile.AuthType,
            credential, profile.PrivateKeyPath, 3);
    }

    /// <summary>按主机、端口与独立认证方式构造连接信息；跳板每一跳与目标主机共用同一套认证构造。</summary>
    internal static ConnectionInfo BuildConnectionInfo(string host, int port, string username,
        RemoteAuthType authType, string credential, string privateKeyPath, int retryAttempts = 1)
    {
        ConnectionInfo info;
        if (authType == RemoteAuthType.PrivateKey && !string.IsNullOrEmpty(privateKeyPath))
        {
            PrivateKeyAuthenticationMethod keyMethod = new PrivateKeyAuthenticationMethod(
                username, new PrivateKeyFile(privateKeyPath, credential));
            info = new ConnectionInfo(host, port, username, keyMethod);
        }
        else
        {
            PasswordAuthenticationMethod passwordMethod = new PasswordAuthenticationMethod(username, credential);
            // 部分服务器（UOS / 麒麟等 PAM 环境）只开放 keyboard-interactive，不开放 password；
            // 两种方式同时提供给 SSH.NET，由服务器协商选择，避免仅支持纯密码时认证被拒
            KeyboardInteractiveAuthenticationMethod keyboardMethod = new KeyboardInteractiveAuthenticationMethod(username);
            keyboardMethod.AuthenticationPrompt += delegate(object? sender, AuthenticationPromptEventArgs e)
            {
                foreach (AuthenticationPrompt prompt in e.Prompts)
                {
                    prompt.Response = credential;
                }
            };
            info = new ConnectionInfo(host, port, username, passwordMethod, keyboardMethod);
        }

        info.RetryAttempts = retryAttempts;
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
            if (entry.Chain != null)
            {
                entry.Chain.Dispose();
            }
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
        public ClientEntry(SshClient client, SshJumpChain? chain)
        {
            this.Client = client;
            this.Chain = chain;
            this.LastUsed = DateTime.Now;
        }

        public SshClient Client { get; }

        /// <summary>目标连接经跳板建立时持有的跳板链；直连时为 null。</summary>
        public SshJumpChain? Chain { get; }

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
