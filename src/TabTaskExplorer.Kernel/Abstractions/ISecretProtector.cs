namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 凭据保护器：把明文凭据转换为可持久化存储的密文，或还原回来。
/// Windows 实现基于 DPAPI（当前用户作用域）；其他平台的实现留空降级为明文。
/// </summary>
public interface ISecretProtector
{
    /// <summary>把明文凭据转换为可存储的密文。</summary>
    /// <param name="plaintext">明文凭据。</param>
    /// <returns>可持久化的密文字符串；输入为空时返回空字符串。</returns>
    string Protect(string plaintext);

    /// <summary>把存储的密文还原为明文凭据。</summary>
    /// <param name="cipherText">存储的密文字符串。</param>
    /// <returns>明文凭据；输入为空时返回空字符串。</returns>
    string Unprotect(string cipherText);
}
