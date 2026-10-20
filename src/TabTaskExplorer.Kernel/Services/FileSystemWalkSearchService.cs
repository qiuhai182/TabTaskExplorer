using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 内核层默认搜索实现：后台线程递归遍历磁盘目录并逐条匹配关键词。
/// 速度远慢于 Everything 的索引搜索，但在 Everything 未安装或未运行时仍然可用。
/// </summary>
public class FileSystemWalkSearchService : IFileSearchService
{
    private readonly SearchQueryParser parser = new SearchQueryParser();

    /// <summary>内置遍历搜索始终可用。</summary>
    public bool IsAvailable
    {
        get { return true; }
    }

    /// <summary>搜索来源的可读名称。</summary>
    public string DisplayName
    {
        get { return "内置遍历搜索"; }
    }

    /// <summary>
    /// 按条件执行全盘搜索，遍历工作放在后台线程执行。
    /// </summary>
    /// <param name="options">搜索条件。</param>
    /// <returns>命中的结果列表。</returns>
    public Task<IReadOnlyList<FileSearchResult>> SearchAsync(FileSearchOptions options)
    {
        return Task.Run(delegate { return this.Search(options); });
    }

    /// <summary>
    /// 同步执行全盘搜索，供平台实现回退调用（调用方需自行放在后台线程）。
    /// </summary>
    /// <param name="options">搜索条件。</param>
    /// <returns>命中的结果列表。</returns>
    public IReadOnlyList<FileSearchResult> Search(FileSearchOptions options)
    {
        SearchQuery query = this.parser.Parse(options.Query);
        if (query.IsEmpty)
        {
            return Array.Empty<FileSearchResult>();
        }

        List<FileSearchResult> results = new List<FileSearchResult>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            this.WalkDirectory(drive.Name, query, options.MaxResults, results);
            if (results.Count >= options.MaxResults)
            {
                break;
            }
        }
        return results;
    }

    /// <summary>递归遍历一个目录，把命中的条目加入结果列表，目录打不开时跳过。</summary>
    private void WalkDirectory(string directoryPath, SearchQuery query, int maxResults, List<FileSearchResult> results)
    {
        if (results.Count >= maxResults)
        {
            return;
        }

        IEnumerable<FileSystemInfo> items;
        try
        {
            items = new DirectoryInfo(directoryPath).EnumerateFileSystemInfos();
        }
        catch (Exception)
        {
            // 无权限访问或目录已消失，直接跳过该目录
            return;
        }

        foreach (FileSystemInfo item in items)
        {
            if (results.Count >= maxResults)
            {
                return;
            }

            bool isDirectory = item is DirectoryInfo;
            if (this.Matches(item.FullName, item.Name, isDirectory, query))
            {
                FileSearchResult result = new FileSearchResult();
                result.FullPath = item.FullName;
                result.Name = item.Name;
                result.Directory = Path.GetDirectoryName(item.FullName) ?? string.Empty;
                result.IsDirectory = isDirectory;
                if (item is FileInfo file)
                {
                    result.LengthBytes = file.Length;
                }
                result.ModifiedTimeUtc = item.LastWriteTimeUtc;
                results.Add(result);
            }

            if (isDirectory)
            {
                this.WalkDirectory(item.FullName, query, maxResults, results);
            }
        }
    }

    /// <summary>判断一个条目是否命中条件树：任意一组全部关键词命中即命中。</summary>
    private bool Matches(string fullPath, string name, bool isDirectory, SearchQuery query)
    {
        foreach (SearchConditionGroup group in query.Groups)
        {
            bool groupHit = true;
            foreach (SearchKeyword keyword in group.Keywords)
            {
                if (!this.MatchesKeyword(fullPath, name, isDirectory, keyword))
                {
                    groupHit = false;
                    break;
                }
            }

            if (groupHit)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>判断一个条目是否命中单个关键词，取反关键词命中表示不包含该词。</summary>
    private bool MatchesKeyword(string fullPath, string name, bool isDirectory, SearchKeyword keyword)
    {
        bool hit;
        if (keyword.IsExtension)
        {
            // 扩展名条件只对文件生效，文件夹一律视为未命中
            hit = false;
            if (!isDirectory)
            {
                string extension = Path.GetExtension(name).TrimStart('.');
                hit = string.Equals(extension, keyword.Text, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            string target = keyword.MatchPath ? fullPath : name;
            StringComparison comparison = keyword.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            hit = target.IndexOf(keyword.Text, comparison) >= 0;
        }

        if (keyword.Negated)
        {
            return !hit;
        }
        return hit;
    }
}
