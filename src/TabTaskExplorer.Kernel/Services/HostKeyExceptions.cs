
namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 首次连接遇到未信任的主机密钥：调用方必须展示指纹并取得用户确认，
/// 确认后写入 KnownHostStore 再重试连接；协议层不得自行信任。
/// </summary>
public class HostKeyUnknownException : Exception
{
    public HostKeyUnknownException(string host, int port, string keyType, string fingerprintSha256, Exception? inner,
        int hopIndex = 0)
        : base((hopIndex > 0 ? "第 " + hopIndex + " 跳：" : string.Empty)
            + "首次连接 " + host + ":" + port + "，主机密钥尚未信任，算法=" + keyType
            + "，指纹=" + fingerprintSha256, inner)
    {
        this.Host = host;
        this.Port = port;
        this.KeyType = keyType;
        this.FingerprintSha256 = fingerprintSha256;
        this.HopIndex = hopIndex;
    }

    /// <summary>跳板序号：0 表示目标主机，1..3 表示第几跳跳板机。</summary>
    public int HopIndex { get; }

    /// <summary>主机地址。</summary>
    public string Host { get; }

    /// <summary>端口号。</summary>
    public int Port { get; }

    /// <summary>密钥算法。</summary>
    public string KeyType { get; }

    /// <summary>SHA256 指纹。</summary>
    public string FingerprintSha256 { get; }
}

/// <summary>
/// 主机密钥与已信任记录不一致：连接必须被硬阻断，只有用户显式删除旧记录后才允许重新信任。
/// </summary>
public class HostKeyMismatchException : Exception
{
    public HostKeyMismatchException(string host, int port, string keyType, string expectedFingerprint,
        string actualFingerprint, Exception? inner, int hopIndex = 0)
        : base((hopIndex > 0 ? "第 " + hopIndex + " 跳：" : string.Empty)
            + "主机密钥校验失败 " + host + ":" + port + "，算法=" + keyType
            + "，已信任指纹=" + expectedFingerprint + "，当前指纹=" + actualFingerprint, inner)
    {
        this.Host = host;
        this.Port = port;
        this.KeyType = keyType;
        this.ExpectedFingerprintSha256 = expectedFingerprint;
        this.ActualFingerprintSha256 = actualFingerprint;
        this.HopIndex = hopIndex;
    }

    /// <summary>跳板序号：0 表示目标主机，1..3 表示第几跳跳板机。</summary>
    public int HopIndex { get; }

    /// <summary>主机地址。</summary>
    public string Host { get; }

    /// <summary>端口号。</summary>
    public int Port { get; }

    /// <summary>密钥算法。</summary>
    public string KeyType { get; }

    /// <summary>已信任的 SHA256 指纹。</summary>
    public string ExpectedFingerprintSha256 { get; }

    /// <summary>本次连接实际收到的 SHA256 指纹。</summary>
    public string ActualFingerprintSha256 { get; }
}

/// <summary>
/// 跳板链某一跳建立失败：明确标注第几跳、主机、失败阶段与累计耗时，
/// 避免把跳板故障笼统显示为目标主机连接失败。
/// </summary>
public class RemoteHopException : Exception
{
    public RemoteHopException(int hopIndex, string host, int port, string stage, long elapsedMilliseconds,
        Exception? inner)
        : base("第 " + hopIndex + " 跳连接失败：" + host + ":" + port + "，阶段=" + stage
            + "，耗时=" + elapsedMilliseconds + " 毫秒，原因=" + (inner == null ? "未知错误" : inner.Message), inner)
    {
        this.HopIndex = hopIndex;
        this.Host = host;
        this.Port = port;
        this.Stage = stage;
        this.ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>跳板序号（从 1 开始）。</summary>
    public int HopIndex { get; }

    /// <summary>跳板主机地址。</summary>
    public string Host { get; }

    /// <summary>跳板端口。</summary>
    public int Port { get; }

    /// <summary>失败阶段：解析、网络、认证、主机密钥、端口转发。</summary>
    public string Stage { get; }

    /// <summary>该跳累计耗时（毫秒，含重试）。</summary>
    public long ElapsedMilliseconds { get; }
}
