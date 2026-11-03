
using System.Text.Json;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>已信任主机的 SSH 主机密钥记录。</summary>
public class KnownHostEntry
{
    /// <summary>主机地址（统一小写保存）。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>端口号。</summary>
    public int Port { get; set; }

    /// <summary>密钥算法（如 ssh-ed25519、ssh-rsa）。</summary>
    public string KeyType { get; set; } = string.Empty;

    /// <summary>SHA256 指纹（Base64、无 SHA256: 前缀）。</summary>
    public string FingerprintSha256 { get; set; } = string.Empty;

    /// <summary>首次信任时间。</summary>
    public DateTime TrustedTime { get; set; }
}

/// <summary>
/// 已知主机密钥存储：按 host:port + 密钥算法记录 SHA256 指纹，首次信任与后续校验共用。
/// 文件位于用户数据目录的 known_hosts.json，写入采用临时文件替换，避免半写入损坏。
/// </summary>
public class KnownHostStore
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private readonly string filePath;
    private readonly object gate = new object();

    /// <summary>使用默认用户数据目录创建存储。</summary>
    public KnownHostStore()
    {
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabTaskExplorer");
        this.filePath = Path.Combine(dataDirectory, "known_hosts.json");
    }

    /// <summary>使用指定文件路径创建存储，供测试与便携模式使用。</summary>
    public KnownHostStore(string filePath)
    {
        this.filePath = filePath;
    }

    /// <summary>读取全部已信任记录。</summary>
    public IReadOnlyList<KnownHostEntry> GetAll()
    {
        lock (this.gate)
        {
            return this.Load();
        }
    }

    /// <summary>查找指定主机、端口与算法对应的记录；不存在返回 null。</summary>
    public KnownHostEntry? Find(string host, int port, string keyType)
    {
        string normalizedHost = NormalizeHost(host);
        string normalizedKeyType = NormalizeKeyType(keyType);
        lock (this.gate)
        {
            foreach (KnownHostEntry entry in this.Load())
            {
                if (entry.Host == normalizedHost && entry.Port == port && entry.KeyType == normalizedKeyType)
                {
                    return entry;
                }
            }
        }
        return null;
    }

    /// <summary>校验指纹是否与已信任记录一致；无记录或算法不一致返回 false。</summary>
    public bool Matches(string host, int port, string keyType, string fingerprintSha256)
    {
        KnownHostEntry? entry = this.Find(host, port, keyType);
        if (entry == null)
        {
            return false;
        }
        return string.Equals(entry.FingerprintSha256, NormalizeFingerprint(fingerprintSha256),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>信任并保存主机密钥；同主机同算法已存在时更新指纹。</summary>
    public void Trust(string host, int port, string keyType, string fingerprintSha256)
    {
        string normalizedHost = NormalizeHost(host);
        string normalizedKeyType = NormalizeKeyType(keyType);
        string normalizedFingerprint = NormalizeFingerprint(fingerprintSha256);
        lock (this.gate)
        {
            List<KnownHostEntry> entries = this.Load();
            KnownHostEntry? existing = null;
            foreach (KnownHostEntry entry in entries)
            {
                if (entry.Host == normalizedHost && entry.Port == port && entry.KeyType == normalizedKeyType)
                {
                    existing = entry;
                    break;
                }
            }
            if (existing == null)
            {
                existing = new KnownHostEntry();
                existing.Host = normalizedHost;
                existing.Port = port;
                existing.KeyType = normalizedKeyType;
                entries.Add(existing);
            }
            existing.FingerprintSha256 = normalizedFingerprint;
            existing.TrustedTime = DateTime.Now;
            this.Save(entries);
        }
    }

    /// <summary>删除某主机的全部已信任密钥（忽略算法），返回删除条数。</summary>
    public int Remove(string host, int port)
    {
        string normalizedHost = NormalizeHost(host);
        lock (this.gate)
        {
            List<KnownHostEntry> entries = this.Load();
            List<KnownHostEntry> remaining = new List<KnownHostEntry>();
            int removed = 0;
            foreach (KnownHostEntry entry in entries)
            {
                if (entry.Host == normalizedHost && entry.Port == port)
                {
                    removed++;
                    continue;
                }
                remaining.Add(entry);
            }
            if (removed > 0)
            {
                this.Save(remaining);
            }
            return removed;
        }
    }

    /// <summary>从磁盘加载记录；文件损坏时按空列表处理并让后续信任重建文件。</summary>
    private List<KnownHostEntry> Load()
    {
        if (!File.Exists(this.filePath))
        {
            return new List<KnownHostEntry>();
        }
        try
        {
            string json = File.ReadAllText(this.filePath);
            List<KnownHostEntry>? entries = JsonSerializer.Deserialize<List<KnownHostEntry>>(json, jsonOptions);
            return entries == null ? new List<KnownHostEntry>() : entries;
        }
        catch (Exception)
        {
            return new List<KnownHostEntry>();
        }
    }

    /// <summary>先写临时文件再替换正式文件，避免写入中断留下半个 JSON。</summary>
    private void Save(List<KnownHostEntry> entries)
    {
        string? directory = Path.GetDirectoryName(this.filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        string json = JsonSerializer.Serialize(entries, jsonOptions);
        string tempPath = this.filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, this.filePath, true);
    }

    private static string NormalizeHost(string host)
    {
        return (host ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizeKeyType(string keyType)
    {
        return (keyType ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>去掉 OpenSSH 风格前缀与空白，统一比较口径。</summary>
    private static string NormalizeFingerprint(string fingerprint)
    {
        string text = (fingerprint ?? string.Empty).Trim();
        if (text.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring("SHA256:".Length);
        }
        return text;
    }
}
