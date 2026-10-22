using System;
using System.Text;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 归档条目文件名编解码：
/// 智能解码（严格 UTF-8 → GBK → 单字节）用于把归档内原始字节还原为可显示字符串；
/// tar 打包与解包默认按原始字节透传，仅在明确指定编码时才做转换。
/// </summary>
public static class ArchiveNameCodec
{
    private static readonly object syncRoot = new object();
    private static Encoding? gbkEncoding;

    /// <summary>GBK（CP936）编码；运行时组件不可用时回退为 null。</summary>
    public static Encoding? Gbk
    {
        get
        {
            if (gbkEncoding != null)
            {
                return gbkEncoding;
            }
            lock (syncRoot)
            {
                if (gbkEncoding == null)
                {
                    try
                    {
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                        gbkEncoding = Encoding.GetEncoding(936);
                    }
                    catch
                    {
                        gbkEncoding = Encoding.UTF8;
                    }
                }
            }
            return gbkEncoding;
        }
    }

    /// <summary>
    /// 智能解码原始字节：先按严格 UTF-8 尝试（失败说明不是 UTF-8 文件名），
    /// 再按 GBK 尝试，最后用单字节回退，保证不丢字节信息。
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }
        try
        {
            Encoding strictUtf8 = new UTF8Encoding(false, true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
        }
        Encoding? gbk = Gbk;
        if (gbk != null)
        {
            try
            {
                return gbk.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
            }
        }
        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>按显式编码名解码原始字节（"utf-8" / "gbk" 等；空则走智能解码）。</summary>
    public static string Decode(byte[] bytes, string? encodingName)
    {
        if (string.IsNullOrWhiteSpace(encodingName))
        {
            return Decode(bytes);
        }
        if (string.Equals(encodingName, "gbk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "cp936", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "936", StringComparison.Ordinal))
        {
            return (Gbk ?? Encoding.UTF8).GetString(bytes);
        }
        if (string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "utf8", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(bytes);
        }
        try
        {
            return Encoding.GetEncoding(encodingName).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Decode(bytes);
        }
    }

    /// <summary>
    /// 把字符串按指定策略编码为归档头字节：
    /// null / "utf-8" → UTF-8；"gbk" → CP936；"keep" → Latin-1 字节往返（不转码）。
    /// </summary>
    public static byte[] Encode(string name, string? encodingName)
    {
        if (string.IsNullOrEmpty(name))
        {
            return Array.Empty<byte>();
        }
        if (string.IsNullOrWhiteSpace(encodingName)
            || string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "utf8", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetBytes(name);
        }
        if (string.Equals(encodingName, "keep", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Latin1.GetBytes(name);
        }
        if (string.Equals(encodingName, "gbk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "cp936", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "936", StringComparison.Ordinal))
        {
            return (Gbk ?? Encoding.UTF8).GetBytes(name);
        }
        try
        {
            return Encoding.GetEncoding(encodingName).GetBytes(name);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetBytes(name);
        }
    }
}
