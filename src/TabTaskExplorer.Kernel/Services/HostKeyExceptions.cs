
namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 首次连接遇到未信任的主机密钥：调用方必须展示指纹并取得用户确认，
/// 确认后写入 KnownHostStore 再重试连接；协议层不得自行信任。
/// </summary>
public class HostKeyUnknownException : Exception
{
    public HostKeyUnknownException(string host, int port, string keyType, string fingerprintSha256, Exception? inner)
        : base("首次连接 " + host + ":" + port + "，主机密钥尚未信任，算法=" + keyType
            + "，指纹=" + fingerprintSha256, inner)
    {
        this.Host = host;
        this.Port = port;
        this.KeyType = keyType;
        this.FingerprintSha256 = fingerprintSha256;
    }

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
        string actualFingerprint, Exception? inner)
        : base("主机密钥校验失败 " + host + ":" + port + "，算法=" + keyType
            + "，已信任指纹=" + expectedFingerprint + "，当前指纹=" + actualFingerprint, inner)
    {
        this.Host = host;
        this.Port = port;
        this.KeyType = keyType;
        this.ExpectedFingerprintSha256 = expectedFingerprint;
        this.ActualFingerprintSha256 = actualFingerprint;
    }

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
