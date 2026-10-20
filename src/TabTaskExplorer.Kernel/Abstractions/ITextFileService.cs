using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 本机文本文件读写能力：负责编码探测、二进制识别与大小保护。
/// 实现方应在后台线程被调用，避免大文件读写阻塞界面。
/// </summary>
public interface ITextFileService
{
    /// <summary>
    /// 读取指定路径的文本文件，返回解码文本、探测编码与二进制 / 超大标记。
    /// </summary>
    /// <param name="filePath">本机文件完整路径。</param>
    /// <returns>文本内容结果。</returns>
    TextFileContent Read(string filePath);

    /// <summary>
    /// 按指定编码把文本写回指定路径（覆盖原文件）。
    /// </summary>
    /// <param name="filePath">本机文件完整路径。</param>
    /// <param name="text">要写入的文本。</param>
    /// <param name="encodingName">编码名称（保存时沿用读取时探测到的编码）。</param>
    void Write(string filePath, string text, string encodingName);
}
