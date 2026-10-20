using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
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
            foreach (FileSearchResult result in results)
            {
                this.Results.Add(FileRow.FromSearchResult(result, true));
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
