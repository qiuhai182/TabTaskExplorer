namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 文件搜索选项。
/// </summary>
public class FileSearchOptions
{
    /// <summary>搜索关键字，支持 Everything 风格语法（空格与、竖线或、感叹号非、path:、case:、ext: 前缀）。</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>最多返回的结果条数，防止结果过多导致界面卡顿。</summary>
    public int MaxResults { get; set; } = 500;
}
