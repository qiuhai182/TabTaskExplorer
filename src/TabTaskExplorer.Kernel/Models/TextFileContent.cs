namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 文本文件读取结果：解码后的文本、探测到的编码，以及二进制与超大文件标记。
/// 本机文件与远程文件共用同一份结构，便于编辑器统一处理。
/// </summary>
public class TextFileContent
{
    /// <summary>解码后的文本内容；二进制或超大文件时为空文本。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>探测到的编码名称（UTF-8、UTF-8 BOM、UTF-16 LE、UTF-16 BE、GBK 等）。</summary>
    public string EncodingName { get; set; } = "UTF-8";

    /// <summary>是否为二进制文件（含空字节，无法按文本编辑）。</summary>
    public bool IsBinary { get; set; }

    /// <summary>是否超过文本编辑大小上限（默认 5MB）。</summary>
    public bool IsTooLarge { get; set; }

    /// <summary>原始字节数。</summary>
    public long LengthBytes { get; set; }

    /// <summary>是否可编辑：非二进制且未超过大小上限。</summary>
    public bool IsEditable
    {
        get { return !this.IsBinary && !this.IsTooLarge; }
    }
}
