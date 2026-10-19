using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 非 Windows 平台的留空实现：凭据不做加密、按明文保存。
/// 仅 Windows 提供系统级凭据加密（DPAPI），其他平台此能力留空，连接管理功能本身保持可用。
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
