using System.Text;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// Everything 风格搜索语法解析器。
/// 语法规则：空格表示与；竖线 | 表示或（整句按竖线拆分为多个与组）；感叹号 ! 开头表示排除；
/// 引号包裹的内容作为一个整体关键词；path: 表示匹配完整路径，case: 表示区分大小写，ext: 表示按扩展名匹配。
/// </summary>
public class SearchQueryParser
{
    private const string PathPrefix = "path:";
    private const string CasePrefix = "case:";
    private const string ExtPrefix = "ext:";

    /// <summary>
    /// 把搜索语句解析为条件树。
    /// </summary>
    /// <param name="query">用户输入的搜索语句。</param>
    /// <returns>解析后的搜索条件，输入为空白时返回空条件。</returns>
    public SearchQuery Parse(string query)
    {
        SearchQuery result = new SearchQuery();
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        string[] groupTexts = query.Split('|');
        foreach (string groupText in groupTexts)
        {
            SearchConditionGroup group = new SearchConditionGroup();
            List<string> tokens = this.SplitTokens(groupText);
            foreach (string token in tokens)
            {
                SearchKeyword? keyword = this.ParseKeyword(token);
                if (keyword != null)
                {
                    group.Keywords.Add(keyword);
                }
            }

            if (group.Keywords.Count > 0)
            {
                result.Groups.Add(group);
            }
        }
        return result;
    }

    /// <summary>按空格拆分关键词，引号内的空格不参与拆分。</summary>
    private List<string> SplitTokens(string text)
    {
        List<string> tokens = new List<string>();
        StringBuilder current = new StringBuilder();
        bool inQuote = false;
        foreach (char ch in text)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && (ch == ' ' || ch == '\t'))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }
        return tokens;
    }

    /// <summary>解析单个关键词的前缀与取反标记，仅剩前缀没有内容时返回空。</summary>
    private SearchKeyword? ParseKeyword(string token)
    {
        if (token.Length == 0)
        {
            return null;
        }

        SearchKeyword keyword = new SearchKeyword();
        string text = token;
        if (text.StartsWith("!"))
        {
            keyword.Negated = true;
            text = text.Substring(1);
        }

        if (text.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            keyword.MatchPath = true;
            text = text.Substring(PathPrefix.Length);
        }
        else if (text.StartsWith(CasePrefix, StringComparison.OrdinalIgnoreCase))
        {
            keyword.CaseSensitive = true;
            text = text.Substring(CasePrefix.Length);
        }
        else if (text.StartsWith(ExtPrefix, StringComparison.OrdinalIgnoreCase))
        {
            keyword.IsExtension = true;
            text = text.Substring(ExtPrefix.Length);
        }

        if (text.Length == 0)
        {
            return null;
        }

        keyword.Text = text;
        return keyword;
    }
}
