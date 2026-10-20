namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 单个搜索关键词条件。
/// </summary>
public class SearchKeyword
{
    /// <summary>关键词文本。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>是否取反（感叹号开头的关键词，表示排除包含该词的结果）。</summary>
    public bool Negated { get; set; }

    /// <summary>是否区分大小写（case: 前缀）。</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>是否匹配完整路径而不是仅匹配名称（path: 前缀）。</summary>
    public bool MatchPath { get; set; }

    /// <summary>是否为扩展名条件（ext: 前缀，仅对文件生效）。</summary>
    public bool IsExtension { get; set; }
}

/// <summary>
/// 一组与条件：组内全部关键词同时命中才算命中。
/// </summary>
public class SearchConditionGroup
{
    /// <summary>本组的关键词列表，全部命中时本组命中。</summary>
    public List<SearchKeyword> Keywords { get; } = new List<SearchKeyword>();
}

/// <summary>
/// 解析后的搜索条件：多组之间是或关系，任意一组命中即命中。
/// </summary>
public class SearchQuery
{
    /// <summary>与条件分组列表，组与组之间是或关系。</summary>
    public List<SearchConditionGroup> Groups { get; } = new List<SearchConditionGroup>();

    /// <summary>是否为空条件（没有任何关键词）。</summary>
    public bool IsEmpty
    {
        get { return this.Groups.Count == 0; }
    }
}
