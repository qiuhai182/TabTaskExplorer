using System.Text;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 编码选项：一个可供选择的字符编码（显示名 + 代码页）。
/// </summary>
public class EncodingChoice
{
    /// <summary>编码显示名，例如 UTF-8、GBK。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>编码代码页编号，例如 65001、936。</summary>
    public int CodePage { get; set; }

    /// <summary>下拉框等控件的默认显示文本。</summary>
    public override string ToString()
    {
        return this.Name;
    }
}

/// <summary>
/// 一条编码转换计划：旧路径、转换后的新名称、失败或跳过原因（为空表示可执行）。
/// </summary>
public class EncodeConvertEntry
{
    /// <summary>条目的当前完整路径。</summary>
    public string OldPath { get; set; } = string.Empty;

    /// <summary>转换后的新名称（不含路径）。</summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>原因：为空表示转换成功可执行；非空表示失败或无变化。</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 文件名批量编码转换器：纯字符串逻辑（对照 convmv 语义），便于单元测试。
/// 转换公式：新名 = 目标编码.GetString(源编码.GetBytes(旧名))。
/// 只转换文件名与文件夹名，不转换文件内容。
/// </summary>
public static class FileNameEncodingConverter
{
    /// <summary>递归收集后代时的最大深度，防止异常目录结构导致过深递归。</summary>
    private const int MaxRecursiveDepth = 64;

    private static readonly IReadOnlyList<EncodingChoice> SupportedEncodings = new List<EncodingChoice>
    {
        CreateChoice("UTF-8", 65001),
        CreateChoice("UTF-16 LE", 1200),
        CreateChoice("UTF-16 BE", 1201),
        CreateChoice("GBK", 936),
        CreateChoice("GB18030", 54936),
        CreateChoice("Big5", 950),
        CreateChoice("Latin-1", 28591),
        CreateChoice("Shift_JIS", 932),
        CreateChoice("EUC-JP", 51932),
        CreateChoice("EUC-KR", 949),
        CreateChoice("KOI8-R", 20866),
        CreateChoice("Windows-1251", 1251),
        CreateChoice("Windows-1252", 1252),
        CreateChoice("ISO-8859-2", 28592),
    };

    /// <summary>取全部支持的编码选项（双向转换通用）。</summary>
    public static IReadOnlyList<EncodingChoice> GetSupportedEncodings()
    {
        return SupportedEncodings;
    }

    /// <summary>按代码页取编码显示名；未知代码页返回数字文本。</summary>
    public static string GetEncodingName(int codePage)
    {
        foreach (EncodingChoice choice in SupportedEncodings)
        {
            if (choice.CodePage == codePage)
            {
                return choice.Name;
            }
        }
        return codePage.ToString();
    }

    /// <summary>
    /// 转换一个名称的编码理解方式：
    /// 源编码 = 名字当前被误解的编码，目标编码 = 想要的正确编码。
    /// </summary>
    /// <param name="name">原始名称（不含路径）。</param>
    /// <param name="fromCodePage">源编码代码页。</param>
    /// <param name="toCodePage">目标编码代码页。</param>
    /// <param name="reason">失败原因；转换成功或无变化时为空。</param>
    /// <returns>转换后的新名称；失败时返回原名称。</returns>
    public static string ConvertName(string name, int fromCodePage, int toCodePage, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            reason = "名称为空";
            return name;
        }
        // 纯 ASCII 名称在任何单字节编码下字节一致，无需转换（对应 convmv 的智能检测）
        if (fromCodePage == toCodePage || IsAscii(name))
        {
            return name;
        }
        Encoding? fromEncoding = TextFileCodec.ResolveCodePage(fromCodePage);
        Encoding? toEncoding = TextFileCodec.ResolveCodePage(toCodePage);
        if (fromEncoding == null || toEncoding == null)
        {
            reason = "不支持的编码";
            return name;
        }

        // 源编码按严格编码器：名字里有源编码表示不了的字符即判定失败
        Encoding strictFrom = (Encoding)fromEncoding.Clone();
        strictFrom.EncoderFallback = new EncoderExceptionFallback();
        // 目标编码按严格解码器：源字节在目标编码里不是合法序列即判定失败
        Encoding strictTo = (Encoding)toEncoding.Clone();
        strictTo.DecoderFallback = new DecoderExceptionFallback();

        byte[] rawBytes;
        try
        {
            rawBytes = strictFrom.GetBytes(name);
        }
        catch (EncoderFallbackException)
        {
            reason = "原编码无法完整表示该名称中的字符";
            return name;
        }
        try
        {
            return strictTo.GetString(rawBytes);
        }
        catch (DecoderFallbackException)
        {
            reason = "目标编码无法解析原编码字节";
            return name;
        }
    }

    /// <summary>
    /// 为一批路径生成转换计划：递归时深度收集选中目录的全部后代。
    /// </summary>
    /// <param name="provider">文件系统提供者（用于递归列举目录）。</param>
    /// <param name="paths">选中的路径列表。</param>
    /// <param name="recursive">是否递归处理选中目录的全部后代。</param>
    /// <param name="fromCodePage">源编码代码页。</param>
    /// <param name="toCodePage">目标编码代码页。</param>
    /// <returns>转换计划列表（每项含旧路径、新名称、原因）。</returns>
    public static List<EncodeConvertEntry> BuildPlan(IFileSystemProvider provider,
        IReadOnlyList<string> paths, bool recursive, int fromCodePage, int toCodePage)
    {
        List<string> collected = new List<string>();
        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            collected.Add(path);
            if (recursive)
            {
                CollectDescendants(provider, path, collected, 1);
            }
        }

        List<EncodeConvertEntry> plan = new List<EncodeConvertEntry>();
        foreach (string path in collected)
        {
            string name = GetEntryName(path);
            EncodeConvertEntry entry = new EncodeConvertEntry();
            entry.OldPath = path;
            entry.NewName = ConvertName(name, fromCodePage, toCodePage, out string reason);
            entry.Reason = reason;
            plan.Add(entry);
        }
        return plan;
    }

    /// <summary>深度收集一个路径的全部后代（先父后子；执行阶段再统一自底向上排序）。</summary>
    private static void CollectDescendants(IFileSystemProvider provider, string path,
        List<string> collected, int depth)
    {
        if (depth > MaxRecursiveDepth)
        {
            return;
        }
        IReadOnlyList<FileEntry> children;
        try
        {
            children = provider.ListDirectory(path);
        }
        catch
        {
            // 是文件或无法访问的目录：没有可收集的后代
            return;
        }
        foreach (FileEntry child in children)
        {
            collected.Add(child.FullPath);
            if (child.IsDirectory)
            {
                CollectDescendants(provider, child.FullPath, collected, depth + 1);
            }
        }
    }

    /// <summary>取路径末尾的名称；处理带结尾分隔符的目录路径。</summary>
    public static string GetEntryName(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name))
        {
            return trimmed;
        }
        return name;
    }

    /// <summary>是否为纯 ASCII 名称（所有字符均小于 128）。</summary>
    private static bool IsAscii(string name)
    {
        for (int index = 0; index < name.Length; index++)
        {
            if (name[index] > 127)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>创建一个编码选项。</summary>
    private static EncodingChoice CreateChoice(string name, int codePage)
    {
        EncodingChoice choice = new EncodingChoice();
        choice.Name = name;
        choice.CodePage = codePage;
        return choice;
    }
}
