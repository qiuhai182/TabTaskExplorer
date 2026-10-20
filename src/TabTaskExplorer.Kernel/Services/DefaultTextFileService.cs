using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 本机文本文件服务默认实现：System.IO 读写 + TextFileCodec 编码探测。
/// 跨平台可用，不依赖任何平台专用 API。
/// </summary>
public class DefaultTextFileService : ITextFileService
{
    /// <inheritdoc />
    public TextFileContent Read(string filePath)
    {
        FileInfo info = new FileInfo(filePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("文件不存在：" + filePath, filePath);
        }
        if (info.Length > TextFileCodec.MaxTextFileBytes)
        {
            TextFileContent tooLarge = new TextFileContent();
            tooLarge.IsTooLarge = true;
            tooLarge.LengthBytes = info.Length;
            return tooLarge;
        }
        byte[] bytes = File.ReadAllBytes(filePath);
        return TextFileCodec.Decode(bytes);
    }

    /// <inheritdoc />
    public void Write(string filePath, string text, string encodingName)
    {
        byte[] bytes = TextFileCodec.Encode(text, encodingName);
        File.WriteAllBytes(filePath, bytes);
    }
}
