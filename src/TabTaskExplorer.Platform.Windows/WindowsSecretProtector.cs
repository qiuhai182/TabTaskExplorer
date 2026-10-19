using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 凭据保护实现：DPAPI 当前用户作用域，附加应用熵。
/// 同一 Windows 用户账户下可解密；换机器或换用户后无法解密，需要重新输入凭据。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsSecretProtector : ISecretProtector
{
    /// <summary>附加熵：让本应用加密的数据不能被同用户下的其他程序直接解密复用。</summary>
    private static readonly byte[] additionalEntropy = Encoding.UTF8.GetBytes("TabTaskExplorer.SecretProtector.v1");

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipherBytes = ProtectedData.Protect(plainBytes, additionalEntropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipherBytes);
    }

    public string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }
        byte[] cipherBytes = Convert.FromBase64String(cipherText);
        byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, additionalEntropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
