using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 仅用于单元测试与历史兼容的明文保护器，生产环境不得注册使用。
/// 正式平台凭据保护：Windows 使用 DPAPI；macOS / Linux 使用 AesGcmFileSecretProtector。
/// </summary>
public class PlaintextSecretProtector : ISecretProtector
{
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }
        return plaintext;
    }

    public string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }
        return cipherText;
    }
}
