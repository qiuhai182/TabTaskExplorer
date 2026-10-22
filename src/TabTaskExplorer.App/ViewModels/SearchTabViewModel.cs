using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 全局搜索标签页：跨全盘文件名搜索。
/// Windows 上优先使用 Everything 进程间通信搜索，Everything 不可用时自动回退内置遍历搜索。
/// 搜索在后台线程执行，结果回到界面线程后再更新列表，避免大量结果冻结界面。
/// </summary>
public class SearchTabViewModel : INotifyPropertyChanged
{
    private readonly IFileSearchService searchService;

    private string queryText = string.Empty;
    private string statusText = "输入关键字后按回车或点击搜索按钮。";
    private string sortField = string.Empty;
    private bool sortAscending = true;
    private bool isSearching;

    // 分代令牌：新搜索开始后，旧搜索的迟到结果会被丢弃
    private int searchGeneration;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SearchTabViewModel(IFileSearchService searchService)
    {
        this.searchService = searchService;
    }

    /// <summary>标签标题。</summary>
    public string Title
    {
        get { return "全局搜索"; }
    }

    /// <summary>搜索关键字。</summary>
    public string QueryText
    {
        get { return this.queryText; }
        set
        {
            this.queryText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueryText)));
        }
    }

    /// <summary>状态栏文字：搜索引擎名称、搜索进度与结果数量。</summary>
    public string StatusText
    {
        get { return this.statusText; }
        private set
        {
            this.statusText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    /// <summary>是否正在搜索（搜索中禁用搜索按钮）。</summary>
    public bool IsSearching
    {
        get { return this.isSearching; }
        private set
        {
            this.isSearching = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSearching)));
        }
    }

    /// <summary>搜索结果列表（整表替换思路，逐项加入即可，ListBox 自带虚拟化）。</summary>
    public ObservableCollection<FileRow> Results { get; } = new ObservableCollection<FileRow>();

    /// <summary>当前排序字段：空为搜索引擎返回顺序，Name / Modified / Size。</summary>
    public string SortField
    {
        get { return this.sortField; }
    }

    /// <summary>排序方向是否为升序（降序时表头显示向下箭头）。</summary>
    public bool SortAscending
    {
        get { return this.sortAscending; }
    }

    /// <summary>点击表头排序：重复点击同一字段切换升序降序，点击新字段按升序排列。</summary>
    /// <param name="field">排序字段：Name / Modified / Size。</param>
    public void SetSortField(string field)
    {
        if (this.sortField == field)
        {
            this.sortAscending = !this.sortAscending;
        }
        else
        {
            this.sortField = field;
            this.sortAscending = true;
        }
        this.ReapplySort();
        UiLogger.Log("全局搜索", "点击表头排序：排序字段=" + field
            + " 排序方向=" + (this.sortAscending ? "升序" : "降序")
            + " 结果行数=" + this.Results.Count);
    }

    /// <summary>按当前排序字段把结果列表整体重排（平铺列表，直接整体排序）。</summary>
    private void ReapplySort()
    {
        if (this.Results.Count <= 1)
        {
            return;
        }
        List<FileRow> sorted = new List<FileRow>(this.Results);
        sorted.Sort(this.CompareResults);
        this.Results.Clear();
        foreach (FileRow row in sorted)
        {
            this.Results.Add(row);
        }
    }

    /// <summary>搜索结果行比较器：目录始终在前（与文件列表一致），再按当前排序字段比较；
    /// 未排序时保持搜索引擎返回顺序。</summary>
    /// <param name="left">左行。</param>
    /// <param name="right">右行。</param>
    /// <returns>小于 0 / 等于 0 / 大于 0。</returns>
    private int CompareResults(FileRow left, FileRow right)
    {
        int directoryCompare = right.IsDirectory.CompareTo(left.IsDirectory);
        if (directoryCompare != 0)
        {
            return directoryCompare;
        }
        int result;
        if (this.sortField == "Modified")
        {
            result = left.ModifiedTimeUtc.CompareTo(right.ModifiedTimeUtc);
        }
        else if (this.sortField == "Size")
        {
            result = left.LengthBytes.CompareTo(right.LengthBytes);
        }
        else
        {
            result = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        }
        if (!this.sortAscending)
        {
            result = -result;
        }
        return result;
    }

    /// <summary>执行搜索：后台线程调用内核搜索服务，完成后回到界面线程更新列表。</summary>
    public void ExecuteSearch()
    {
        if (this.isSearching)
        {
            return;
        }
        string query = this.queryText.Trim();
        if (query.Length == 0)
        {
            this.StatusText = "请输入搜索关键字。";
            return;
        }

        this.IsSearching = true;
        this.StatusText = "正在使用「" + this.searchService.DisplayName + "」搜索……";
        this.searchGeneration = this.searchGeneration + 1;
        int generation = this.searchGeneration;

        Task.Run(delegate
        {
            try
            {
                FileSearchOptions options = new FileSearchOptions();
                options.Query = query;
                List<FileSearchResult> found = new List<FileSearchResult>(this.searchService.SearchAsync(options).GetAwaiter().GetResult());
                this.ApplyResults(generation, query, found);
            }
            catch (Exception error)
            {
                this.ApplyError(generation, error.Message);
            }
        });
    }

    /// <summary>把搜索结果发布到界面线程（过期分代的结果直接丢弃）。</summary>
    private void ApplyResults(int generation, string query, List<FileSearchResult> results)
    {
        Dispatcher.UIThread.Post(delegate
        {
            if (generation != this.searchGeneration)
            {
                return;
            }
            this.IsSearching = false;
            this.Results.Clear();
            List<FileRow> rows = new List<FileRow>();
            foreach (FileSearchResult result in results)
            {
                rows.Add(FileRow.FromSearchResult(result, true));
            }
            if (this.sortField.Length > 0)
            {
                rows.Sort(this.CompareResults);
            }
            foreach (FileRow row in rows)
            {
                this.Results.Add(row);
            }
            this.StatusText = "搜索「" + query + "」完成：共 " + results.Count.ToString() + " 项（" + this.searchService.DisplayName + "）";
        });
    }

    /// <summary>把搜索失败信息发布到界面线程。</summary>
    private void ApplyError(int generation, string message)
    {
        Dispatcher.UIThread.Post(delegate
        {
            if (generation != this.searchGeneration)
            {
                return;
            }
            this.IsSearching = false;
            this.StatusText = "搜索失败：" + message;
        });
    }
}
