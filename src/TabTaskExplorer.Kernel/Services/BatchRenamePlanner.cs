namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 批量改名选项：查找替换、前后缀、序号与扩展名。
/// </summary>
public class BatchRenameOptions
{
    /// <summary>要查找的文字（作用于主文件名，不含扩展名）。</summary>
    public string FindText { get; set; } = string.Empty;

    /// <summary>替换后的文字。</summary>
    public string ReplaceText { get; set; } = string.Empty;

    /// <summary>添加在名称开头的前缀。</summary>
    public string PrefixText { get; set; } = string.Empty;

    /// <summary>添加在名称末尾（扩展名之前）的后缀。</summary>
    public string SuffixText { get; set; } = string.Empty;

    /// <summary>是否在名称末尾追加序号，例如 名称(1)、名称(2)。</summary>
    public bool UseSequence { get; set; }

    /// <summary>序号起始值。</summary>
    public int SequenceStart { get; set; } = 1;

    /// <summary>新扩展名，例如 .jpg 或 jpg；为空表示保持原扩展名。</summary>
    public string NewExtension { get; set; } = string.Empty;
}

/// <summary>
/// 批量改名名称计算器：纯字符串逻辑，便于单元测试。
/// </summary>
public static class BatchRenamePlanner
{
    /// <summary>
    /// 按选项计算新文件名。
    /// </summary>
    /// <param name="originalName">原始文件名（含扩展名）。</param>
    /// <param name="options">改名选项。</param>
    /// <param name="sequenceNumber">当前序号。</param>
    /// <returns>新文件名（不含路径）。</returns>
    public static string BuildNewName(string originalName, BatchRenameOptions options, int sequenceNumber)
    {
        string extension = Path.GetExtension(originalName);
        string stem = originalName.Substring(0, originalName.Length - extension.Length);

        if (!string.IsNullOrEmpty(options.FindText))
        {
            stem = stem.Replace(options.FindText, options.ReplaceText);
        }

        stem = options.PrefixText + stem + options.SuffixText;

        if (options.UseSequence)
        {
            stem = stem + "(" + sequenceNumber + ")";
        }

        string newExtension = extension;
        if (!string.IsNullOrEmpty(options.NewExtension))
        {
            newExtension = options.NewExtension;
            if (!newExtension.StartsWith(".", StringComparison.Ordinal))
            {
                newExtension = "." + newExtension;
            }
        }

        string result = stem + newExtension;
        if (string.IsNullOrWhiteSpace(result))
        {
            return originalName;
        }
        return result;
    }
}
