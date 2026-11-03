using System.Security.Cryptography;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 非 Windows 平台的凭据保护实现：AES-256-GCM + 当前用户主密钥文件。
/// 主密钥在首次使用时随机生成，Unix 平台写入时收紧为 600 权限；密文带版本前缀，禁止明文落盘。
/// </summary>
public class AesGcmFileSecretProtector : ISecretProtector
{
    /// <summary>密文版本前缀：用于识别新格式并支撑一次性迁移。</summary>
    public const string FormatPrefix = "aesgcm:v1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly string keyFilePath;
    private readonly object gate = new object();
    private byte[]? key;

    /// <summary>使用默认用户数据目录下的 secret.key 创建保护器。</summary>
    public AesGcmFileSecretProtector()
    {
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabTaskExplorer");
        this.keyFilePath = Path.Combine(dataDirectory, "secret.key");
    }

    /// <summary>使用指定主密钥文件路径创建保护器，供测试与便携模式使用。</summary>
    public AesGcmFileSecretProtector(string keyFilePath)
    {
        this.keyFilePath = keyFilePath;
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipherBytes = new byte[plainBytes.Length];
        using (AesGcm aes = new AesGcm(this.GetKey(), TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }
        byte[] payload = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, payload, NonceSize + TagSize, cipherBytes.Length);
        return FormatPrefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }
        if (!cipherText.StartsWith(FormatPrefix, StringComparison.Ordinal))
        {
            // 旧格式（历史明文）：迁移阶段会被重新加密；读取时原样返回以保持可用
            return cipherText;
        }
        byte[] payload = Convert.FromBase64String(cipherText.Substring(FormatPrefix.Length));
        if (payload.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("凭据密文长度不足，无法解密");
        }
        byte[] nonce = new byte[NonceSize];
        byte[] tag = new byte[TagSize];
        byte[] cipherBytes = new byte[payload.Length - NonceSize - TagSize];
        Buffer.BlockCopy(payload, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(payload, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(payload, NonceSize + TagSize, cipherBytes, 0, cipherBytes.Length);
        byte[] plainBytes = new byte[cipherBytes.Length];
        using (AesGcm aes = new AesGcm(this.GetKey(), TagSize))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>读取主密钥；不存在时随机生成并按平台能力收紧文件权限。</summary>
    private byte[] GetKey()
    {
        lock (this.gate)
        {
            if (this.key != null)
            {
                return this.key;
            }
            string? directory = Path.GetDirectoryName(this.keyFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(this.keyFilePath))
            {
                byte[] existing = File.ReadAllBytes(this.keyFilePath);
                if (existing.Length != KeySize)
                {
                    throw new InvalidOperationException("主密钥文件长度不正确，无法解密已有凭据");
                }
                this.key = existing;
                return existing;
            }
            byte[] created = RandomNumberGenerator.GetBytes(KeySize);
            File.WriteAllBytes(this.keyFilePath, created);
            TryRestrictPermissions(this.keyFilePath);
            this.key = created;
            return created;
        }
    }

    /// <summary>Unix 平台把主密钥文件权限收紧到仅当前用户可读写；Windows 依赖用户数据目录权限。</summary>
    private static void TryRestrictPermissions(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // 权限收紧失败不阻断加密；文件仍位于当前用户数据目录
        }
    }
}
