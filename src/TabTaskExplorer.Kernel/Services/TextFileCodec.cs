using System.Text;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 文本编码编解码工具：本机文件与远程文件共用同一套探测规则。
/// 探测顺序为 BOM 优先 → 严格 UTF-8 校验 → GBK 回退，避免中文文本被误判为乱码。
/// </summary>
public static class TextFileCodec
{
    /// <summary>文本编辑大小上限（5MB），超过则拒绝按文本打开。</summary>
    public const long MaxTextFileBytes = 5L * 1024 * 1024;

    /// <summary>无 BOM 的 UTF-8 编码名。</summary>
    public const string Utf8Name = "UTF-8";

    /// <summary>带 BOM 的 UTF-8 编码名。</summary>
    public const string Utf8BomName = "UTF-8 BOM";

    /// <summary>UTF-16 小端编码名。</summary>
    public const string Utf16LeName = "UTF-16 LE";

    /// <summary>UTF-16 大端编码名。</summary>
    public const string Utf16BeName = "UTF-16 BE";

    /// <summary>UTF-32 小端编码名。</summary>
    public const string Utf32LeName = "UTF-32 LE";

    /// <summary>UTF-32 大端编码名。</summary>
    public const string Utf32BeName = "UTF-32 BE";

    /// <summary>GBK（Windows 936 代码页）编码名。</summary>
    public const string GbkName = "GBK";

    /// <summary>兜底编码名（Latin-1，逐字节无损回退，仅保证不丢字节）。</summary>
    public const string Latin1Name = "Latin-1";

    private static bool providerRegistered;

    /// <summary>
    /// 注册代码页编码提供程序，使 GBK（936）等旧编码可用。
    /// </summary>
    private static void EnsureProviderRegistered()
    {
        if (providerRegistered)
        {
            return;
        }
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // 提供程序不可用时 GBK 回退将退化为 Latin-1
        }
        providerRegistered = true;
    }

    /// <summary>是否包含空字节，据此判定为二进制文件。</summary>
    public static bool LooksBinary(byte[] bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 把字节数组解码为文本内容：先按 BOM 判定，再做二进制检查，最后按 UTF-8 严格校验或 GBK 回退。
    /// </summary>
    public static TextFileContent Decode(byte[] bytes)
    {
        TextFileContent result = new TextFileContent();
        result.LengthBytes = bytes.LongLength;
        if (bytes.LongLength > MaxTextFileBytes)
        {
            result.IsTooLarge = true;
            return result;
        }

        // 一、BOM 优先：带 BOM 的编码即使含空字节也按文本处理
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            result.EncodingName = Utf8BomName;
            result.Text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            return result;
        }
        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            result.EncodingName = Utf32LeName;
            result.Text = new UTF32Encoding(false, false).GetString(bytes, 4, bytes.Length - 4);
            return result;
        }
        if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            result.EncodingName = Utf32BeName;
            result.Text = new UTF32Encoding(true, false).GetString(bytes, 4, bytes.Length - 4);
            return result;
        }
        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            result.EncodingName = Utf16LeName;
            result.Text = new UnicodeEncoding(false, false).GetString(bytes, 2, bytes.Length - 2);
            return result;
        }
        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            result.EncodingName = Utf16BeName;
            result.Text = new UnicodeEncoding(true, false).GetString(bytes, 2, bytes.Length - 2);
            return result;
        }

        // 二、无 BOM：含空字节判定为二进制
        if (LooksBinary(bytes))
        {
            result.IsBinary = true;
            return result;
        }

        // 三、严格 UTF-8 校验：非法序列抛异常则回退 GBK
        UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
        try
        {
            result.Text = strictUtf8.GetString(bytes);
            result.EncodingName = Utf8Name;
            return result;
        }
        catch (DecoderFallbackException)
        {
            // 继续尝试 GBK
        }

        EnsureProviderRegistered();
        try
        {
            Encoding gbk = Encoding.GetEncoding(936);
            result.Text = gbk.GetString(bytes);
            result.EncodingName = GbkName;
            return result;
        }
        catch
        {
            result.Text = Encoding.Latin1.GetString(bytes);
            result.EncodingName = Latin1Name;
            return result;
        }
    }

    /// <summary>按编码名称解析出编码对象（用于保存时编回字节）。</summary>
    public static Encoding ResolveEncoding(string encodingName)
    {
        switch (encodingName)
        {
            case Utf8BomName:
                return new UTF8Encoding(true);
            case Utf16LeName:
                return new UnicodeEncoding(false, true);
            case Utf16BeName:
                return new UnicodeEncoding(true, true);
            case Utf32LeName:
                return new UTF32Encoding(false, true);
            case Utf32BeName:
                return new UTF32Encoding(true, true);
            case GbkName:
                EnsureProviderRegistered();
                try
                {
                    return Encoding.GetEncoding(936);
                }
                catch
                {
                    return Encoding.Latin1;
                }
            case Latin1Name:
                return Encoding.Latin1;
            default:
                return new UTF8Encoding(false);
        }
    }

    /// <summary>把文本按指定编码编码成字节数组；带 BOM 的编码会写出前导标记。</summary>
    public static byte[] Encode(string text, string encodingName)
    {
        Encoding encoding = ResolveEncoding(encodingName);
        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes(text ?? string.Empty);
        if (preamble.Length == 0)
        {
            return body;
        }
        byte[] combined = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, combined, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, combined, preamble.Length, body.Length);
        return combined;
    }

    /// <summary>判断字节数组是否以给定前缀开头。</summary>
    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }
        for (int index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }
        return true;
    }
}
