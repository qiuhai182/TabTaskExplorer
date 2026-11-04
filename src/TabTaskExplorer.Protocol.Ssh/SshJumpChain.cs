using System.Diagnostics;
using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Protocol.Ssh;

/// <summary>
/// 一跳跳板的凭据：节点配置与已解密的明文凭据分开传递，避免模型本身持有明文。
/// </summary>
public sealed class SshJumpHopCredential
{
    public SshJumpHopCredential(RemoteJumpHop hop, string secret)
    {
        this.Hop = hop;
        this.Secret = secret ?? string.Empty;
    }

    /// <summary>跳板节点配置。</summary>
    public RemoteJumpHop Hop { get; }

    /// <summary>已解密的明文凭据（密码或私钥口令）。</summary>
    public string Secret { get; }
}

/// <summary>
/// ProxyJump 跳板链：按顺序建立各跳 SSH 连接，并用 ForwardedPortLocal 逐跳串联，
/// 最后一跳把目标主机的 host:port 转发到本机回环端口，目标会话从该端口接入。
/// 链对象持有全部跳板连接与转发端口，关闭时必须一并释放。
/// </summary>
public sealed class SshJumpChain : IDisposable
{
    /// <summary>网络类失败的最大尝试次数（含首次）。</summary>
    private const int MaxConnectAttempts = 2;

    private readonly List<SshClient> clients = new List<SshClient>();
    private readonly List<ForwardedPortLocal> forwards = new List<ForwardedPortLocal>();
    private bool disposed;

    private SshJumpChain(int hopCount, string targetHost, int targetPort, ConnectionInfo targetInfo)
    {
        this.HopCount = hopCount;
        this.TargetHost = targetHost;
        this.TargetPort = targetPort;
        this.TargetInfo = targetInfo;
    }

    /// <summary>目标会话实际使用的连接信息：主机为 127.0.0.1，端口为最后一跳的本地转发端口。</summary>
    public ConnectionInfo TargetInfo { get; }

    /// <summary>目标主机原始地址。</summary>
    public string TargetHost { get; }

    /// <summary>目标主机原始端口。</summary>
    public int TargetPort { get; }

    /// <summary>跳板数量。</summary>
    public int HopCount { get; }

    /// <summary>本地回环转发端口（0 表示未建立）。</summary>
    public int LocalTargetPort
    {
        get { return this.TargetInfo.Port; }
    }

    /// <summary>
    /// 建立完整跳板链。失败时释放已建立的连接，并抛出 <see cref="RemoteHopException"/> 或主机密钥专用异常。
    /// </summary>
    /// <param name="profile">目标连接配置。</param>
    /// <param name="targetCredential">已解密的目标主机凭据。</param>
    /// <param name="hops">按顺序排列的跳板凭据；不能为空。</param>
    /// <param name="knownHosts">已知主机存储；为空时跳过校验（仅测试或降级场景）。</param>
    /// <param name="log">审计日志回调；只记录主机、端口、跳序与耗时，不记录密码。</param>
    public static SshJumpChain Connect(RemoteConnectionProfile profile, string targetCredential,
        IReadOnlyList<SshJumpHopCredential> hops, KnownHostStore? knownHosts, Action<string>? log)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        if (hops == null || hops.Count == 0)
        {
            throw new ArgumentException("跳板链至少需要一跳", nameof(hops));
        }
        if (hops.Count > RemoteConnectionProfile.MaxJumpHops)
        {
            throw new ArgumentException("跳板机最多 " + RemoteConnectionProfile.MaxJumpHops + " 跳", nameof(hops));
        }
        string? validationError = profile.ValidateJumpChain();
        if (validationError != null)
        {
            throw new ArgumentException(validationError, nameof(profile));
        }

        List<SshClient> openedClients = new List<SshClient>();
        List<ForwardedPortLocal> openedForwards = new List<ForwardedPortLocal>();
        string connectHost = hops[0].Hop.Host;
        int connectPort = hops[0].Hop.GetEffectivePort();
        try
        {
            for (int index = 0; index < hops.Count; index++)
            {
                SshJumpHopCredential credential = hops[index];
                RemoteJumpHop hop = credential.Hop;
                int hopIndex = index + 1;
                SshClient client = ConnectHop(hop, credential.Secret, connectHost, connectPort, knownHosts, hopIndex, log);
                openedClients.Add(client);

                if (index == hops.Count - 1)
                {
                    ForwardedPortLocal targetForward = StartForward(client, profile.Host, profile.GetEffectivePort(),
                        hop, hopIndex, log);
                    openedForwards.Add(targetForward);
                    ConnectionInfo targetInfo = SshSessionFactory.BuildConnectionInfo(
                        "127.0.0.1", (int)targetForward.BoundPort, profile.Username, profile.AuthType,
                        targetCredential, profile.PrivateKeyPath);
                    SshJumpChain chain = new SshJumpChain(hops.Count, profile.Host, profile.GetEffectivePort(),
                        targetInfo);
                    chain.clients.AddRange(openedClients);
                    chain.forwards.AddRange(openedForwards);
                    return chain;
                }

                RemoteJumpHop nextHop = hops[index + 1].Hop;
                ForwardedPortLocal nextForward = StartForward(client, nextHop.Host, nextHop.GetEffectivePort(),
                    hop, hopIndex, log);
                openedForwards.Add(nextForward);
                connectHost = "127.0.0.1";
                connectPort = (int)nextForward.BoundPort;
            }
            throw new InvalidOperationException("跳板链建立失败：未生成目标转发端口");
        }
        catch
        {
            StopForwards(openedForwards);
            DisposeClients(openedClients);
            throw;
        }
    }

    /// <summary>按跳序建立单个跳板连接，网络类失败重试一次，认证与主机密钥失败不重试。</summary>
    private static SshClient ConnectHop(RemoteJumpHop hop, string secret, string connectHost, int connectPort,
        KnownHostStore? knownHosts, int hopIndex, Action<string>? log)
    {
        Exception? lastError = null;
        long totalElapsed = 0;
        for (int attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string keyType = string.Empty;
            string actualFingerprint = string.Empty;
            string expectedFingerprint = string.Empty;
            bool unknown = false;
            bool mismatch = false;
            SshClient client = new SshClient(SshSessionFactory.BuildConnectionInfo(connectHost, connectPort,
                hop.Username, hop.AuthType, secret, hop.PrivateKeyPath));
            client.KeepAliveInterval = TimeSpan.FromSeconds(15);
            if (knownHosts != null)
            {
                // 每跳独立校验：即使中间人是上一跳转发过来的，也必须匹配该跳自己的 known_hosts 记录
                client.HostKeyReceived += delegate(object? sender, HostKeyEventArgs e)
                {
                    keyType = string.IsNullOrEmpty(e.HostKeyName) ? "unknown" : e.HostKeyName;
                    actualFingerprint = e.FingerPrintSHA256;
                    KnownHostEntry? known = knownHosts.Find(hop.Host, hop.GetEffectivePort(), keyType);
                    if (known == null)
                    {
                        unknown = true;
                        e.CanTrust = false;
                        return;
                    }
                    if (!knownHosts.Matches(hop.Host, hop.GetEffectivePort(), keyType, actualFingerprint))
                    {
                        mismatch = true;
                        expectedFingerprint = known.FingerprintSha256;
                        e.CanTrust = false;
                        return;
                    }
                    e.CanTrust = true;
                };
            }

            try
            {
                client.Connect();
                if (!client.IsConnected)
                {
                    throw new InvalidOperationException("连接后状态为未连接");
                }
                stopwatch.Stop();
                Log(log, "第 " + hopIndex + " 跳连接成功：主机=" + hop.Host + ":" + hop.GetEffectivePort()
                    + " 尝试=" + attempt + " 耗时=" + stopwatch.ElapsedMilliseconds + " 毫秒");
                return client;
            }
            catch (Exception error)
            {
                stopwatch.Stop();
                totalElapsed += stopwatch.ElapsedMilliseconds;
                TryDispose(client);
                if (unknown)
                {
                    throw new HostKeyUnknownException(hop.Host, hop.GetEffectivePort(), keyType, actualFingerprint,
                        error, hopIndex);
                }
                if (mismatch)
                {
                    throw new HostKeyMismatchException(hop.Host, hop.GetEffectivePort(), keyType, expectedFingerprint,
                        actualFingerprint, error, hopIndex);
                }
                lastError = error;
                bool retryable = IsRetryable(error);
                Log(log, "第 " + hopIndex + " 跳连接失败：主机=" + hop.Host + ":" + hop.GetEffectivePort()
                    + " 尝试=" + attempt + "/" + MaxConnectAttempts + " 耗时=" + stopwatch.ElapsedMilliseconds
                    + " 毫秒 原因=" + error.Message + " 可重试=" + (retryable ? "是" : "否"));
                if (!retryable || attempt == MaxConnectAttempts)
                {
                    break;
                }
                Thread.Sleep(300);
            }
        }

        throw new RemoteHopException(hopIndex, hop.Host, hop.GetEffectivePort(),
            ClassifyStage(lastError, false), totalElapsed, lastError);
    }

    /// <summary>在指定跳板上启动本地端口转发；失败按“第 N 跳 + 端口转发”映射。</summary>
    private static ForwardedPortLocal StartForward(SshClient client, string host, int port, RemoteJumpHop hop,
        int hopIndex, Action<string>? log)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ForwardedPortLocal forward = new ForwardedPortLocal("127.0.0.1", 0u, host, (uint)port);
        try
        {
            client.AddForwardedPort(forward);
            forward.Start();
            stopwatch.Stop();
            Log(log, "第 " + hopIndex + " 跳端口转发已启动：目标=" + host + ":" + port
                + " 本地端口=" + forward.BoundPort + " 耗时=" + stopwatch.ElapsedMilliseconds + " 毫秒");
            return forward;
        }
        catch (Exception error)
        {
            stopwatch.Stop();
            try
            {
                forward.Dispose();
            }
            catch
            {
                // 转发未建立，忽略释放异常
            }
            throw new RemoteHopException(hopIndex, hop.Host, hop.GetEffectivePort(), "端口转发",
                stopwatch.ElapsedMilliseconds, error);
        }
    }

    /// <summary>把连接异常映射为失败阶段文字（认证 / 超时 / 网络 / 连接），供界面与测试共用。</summary>
    public static string MapFailureStage(Exception? error)
    {
        return ClassifyStage(error, false);
    }

    /// <summary>判断异常是否适合重试：认证失败与主机密钥失败不重试。</summary>
    private static bool IsRetryable(Exception error)
    {
        if (error is SshAuthenticationException)
        {
            return false;
        }
        if (error is SshOperationTimeoutException || error is TimeoutException)
        {
            return true;
        }
        if (error is SocketException)
        {
            return true;
        }
        if (error is SshConnectionException)
        {
            return true;
        }
        return error.InnerException is SocketException || error.InnerException is TimeoutException;
    }

    /// <summary>按异常类型归类失败阶段。</summary>
    private static string ClassifyStage(Exception? error, bool duringForward)
    {
        if (duringForward)
        {
            return "端口转发";
        }
        if (error is SshAuthenticationException)
        {
            return "认证";
        }
        if (error is SshOperationTimeoutException || error is TimeoutException)
        {
            return "超时";
        }
        if (error is SocketException || (error != null && error.InnerException is SocketException))
        {
            return "网络";
        }
        return "连接";
    }

    private static void Log(Action<string>? log, string message)
    {
        if (log != null)
        {
            log(message);
        }
    }

    private static void StopForwards(List<ForwardedPortLocal> ports)
    {
        for (int index = ports.Count - 1; index >= 0; index--)
        {
            try
            {
                ports[index].Stop();
            }
            catch
            {
                // 跳板已断开时转发可能已自动停止
            }
            try
            {
                ports[index].Dispose();
            }
            catch
            {
                // 忽略释放异常
            }
        }
        ports.Clear();
    }

    private static void DisposeClients(List<SshClient> items)
    {
        for (int index = items.Count - 1; index >= 0; index--)
        {
            try
            {
                items[index].Disconnect();
            }
            catch
            {
                // 连接可能已断开
            }
            TryDispose(items[index]);
        }
        items.Clear();
    }

    private static void TryDispose(SshClient client)
    {
        try
        {
            client.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }
        this.disposed = true;
        StopForwards(this.forwards);
        DisposeClients(this.clients);
    }
}
