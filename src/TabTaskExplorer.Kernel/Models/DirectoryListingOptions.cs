namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 目录列举选项：控制返回哪些条目。
/// </summary>
public class DirectoryListingOptions
{
    /// <summary>是否包含隐藏文件与隐藏文件夹。</summary>
    public bool ShowHiddenFiles { get; set; } = true;
}
