using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Media;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>播放列表排序方式。</summary>
public enum MediaPlaylistSort
{
    /// <summary>按文件名自然排序（默认）。</summary>
    Name = 0,

    /// <summary>按修改时间。</summary>
    Modified = 1,

    /// <summary>按文件大小。</summary>
    Size = 2,
}

/// <summary>播放列表收录范围。</summary>
public enum MediaPlaylistScope
{
    /// <summary>只收录与当前文件同类（图片标签只列图片）。</summary>
    SameKind = 0,

    /// <summary>收录同目录全部媒体。</summary>
    AllMedia = 1,
}

/// <summary>
/// 播放列表中的一个条目：包住内核 <see cref="MediaItem"/>，补充界面状态（当前项、是否已看）。
/// </summary>
public class MediaPlaylistItemViewModel : INotifyPropertyChanged
{
    private bool isCurrent;
    private bool isViewed;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>内核条目数据。</summary>
    public MediaItem Item { get; }

    /// <summary>使用内核条目创建视图模型。</summary>
    public MediaPlaylistItemViewModel(MediaItem item)
    {
        this.Item = item;
    }

    /// <summary>完整路径。</summary>
    public string FullPath
    {
        get { return this.Item.FullPath; }
    }

    /// <summary>文件名。</summary>
    public string Name
    {
        get { return this.Item.Name; }
    }

    /// <summary>媒体大类。</summary>
    public MediaKind MediaKind
    {
        get { return this.Item.Kind; }
    }

    /// <summary>图标分类键：图片 / 视频 / 音频。</summary>
    public string IconCategory
    {
        get
        {
            if (this.Item.Kind == MediaKind.Image)
            {
                return FileIconCatalog.Image;
            }
            if (this.Item.Kind == MediaKind.Video)
            {
                return FileIconCatalog.Video;
            }
            if (this.Item.Kind == MediaKind.Audio)
            {
                return FileIconCatalog.Audio;
            }
            return FileIconCatalog.File;
        }
    }

    /// <summary>图标强调色：跟随分类自动取色。</summary>
    public string IconColor
    {
        get { return FileIconCatalog.GetAccent(this.IconCategory); }
    }

    /// <summary>类型文本。</summary>
    public string TypeText
    {
        get { return this.Item.TypeText; }
    }

    /// <summary>大小文本。</summary>
    public string SizeText
    {
        get { return this.Item.SizeText; }
    }

    /// <summary>是否为当前正在查看 / 播放的条目。</summary>
    public bool IsCurrent
    {
        get { return this.isCurrent; }
        set
        {
            if (this.isCurrent != value)
            {
                this.isCurrent = value;
                this.RaisePropertyChanged(nameof(IsCurrent));
            }
        }
    }

    /// <summary>本次会话内是否已经看过（列表中以灰字提示）。</summary>
    public bool IsViewed
    {
        get { return this.isViewed; }
        set
        {
            if (this.isViewed != value)
            {
                this.isViewed = value;
                this.RaisePropertyChanged(nameof(IsViewed));
            }
        }
    }

    /// <summary>触发属性变更通知。</summary>
    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

/// <summary>
/// 播放列表视图模型：扫描当前媒体所在目录，维护同目录媒体清单与当前项。
/// 扫描在后台线程执行，切目录 / 快速连续切换时用分代令牌丢弃过期结果。
/// </summary>
public class MediaPlaylistViewModel : INotifyPropertyChanged
{
    /// <summary>单个目录最多收录的条目数，避免超大目录拖慢界面。</summary>
    public const int MaxItems = 5000;

    private readonly Dictionary<string, bool> viewedPaths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private int loadGeneration;
    private MediaPlaylistItemViewModel? currentItem;
    private string scopeText = "同类";
    private string summaryText = "正在扫描目录……";
    private bool isLoading;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>用户点击某条目请求切换播放（参数为条目）。</summary>
    public event EventHandler<MediaPlaylistItemViewModel>? ItemActivated;

    /// <summary>播放列表条目集合。</summary>
    public ObservableCollection<MediaPlaylistItemViewModel> Items { get; } = new ObservableCollection<MediaPlaylistItemViewModel>();

    /// <summary>当前条目。</summary>
    public MediaPlaylistItemViewModel? CurrentItem
    {
        get { return this.currentItem; }
        private set
        {
            this.currentItem = value;
            this.RaisePropertyChanged(nameof(CurrentItem));
        }
    }

    /// <summary>当前收录范围文本（"同类" / "全部媒体"）。</summary>
    public string ScopeText
    {
        get { return this.scopeText; }
        private set
        {
            this.scopeText = value;
            this.RaisePropertyChanged(nameof(ScopeText));
        }
    }

    /// <summary>摘要文本（"共 24 项 · 本目录"）。</summary>
    public string SummaryText
    {
        get { return this.summaryText; }
        private set
        {
            this.summaryText = value;
            this.RaisePropertyChanged(nameof(SummaryText));
        }
    }

    /// <summary>是否正在扫描。</summary>
    public bool IsLoading
    {
        get { return this.isLoading; }
        private set
        {
            this.isLoading = value;
            this.RaisePropertyChanged(nameof(IsLoading));
        }
    }

    /// <summary>请求切换播放指定条目（供界面点击调用）。</summary>
    public void Activate(MediaPlaylistItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }
        EventHandler<MediaPlaylistItemViewModel>? handler = this.ItemActivated;
        if (handler != null)
        {
            handler(this, item);
        }
    }

    /// <summary>
    /// 扫描指定目录并重建播放列表；<paramref name="currentPath"/> 为当前打开的文件，扫描完成后自动标记为当前项。
    /// </summary>
    public async Task LoadAsync(string directory, MediaKind kind, MediaPlaylistScope scope,
        MediaPlaylistSort sort, string currentPath)
    {
        int generation = ++this.loadGeneration;
        this.IsLoading = true;
        this.ScopeText = scope == MediaPlaylistScope.AllMedia ? "全部媒体" : "同类";
        List<MediaItem> scanned = new List<MediaItem>();
        try
        {
            scanned = await Task.Run(delegate
            {
                return ScanDirectory(directory, kind, scope);
            });
        }
        catch (Exception)
        {
            scanned = new List<MediaItem>();
        }
        if (generation != this.loadGeneration)
        {
            // 已有更新的扫描请求，丢弃本次结果
            return;
        }
        SortItems(scanned, sort);
        this.Items.Clear();
        MediaPlaylistItemViewModel? target = null;
        for (int index = 0; index < scanned.Count; index++)
        {
            MediaPlaylistItemViewModel itemViewModel = new MediaPlaylistItemViewModel(scanned[index]);
            if (string.Equals(scanned[index].FullPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                target = itemViewModel;
            }
            if (this.viewedPaths.ContainsKey(scanned[index].FullPath))
            {
                itemViewModel.IsViewed = true;
            }
            this.Items.Add(itemViewModel);
        }
        this.SetCurrent(target);
        this.IsLoading = false;
        string limitText = scanned.Count >= MaxItems ? "（已达上限 " + MaxItems + " 项）" : string.Empty;
        this.SummaryText = "共 " + this.Items.Count + " 项 · 本目录" + limitText;
    }

    /// <summary>把指定路径标记为当前项（路径不在列表内时清空当前项）。</summary>
    public void SetCurrentByPath(string fullPath)
    {
        MediaPlaylistItemViewModel? target = null;
        for (int index = 0; index < this.Items.Count; index++)
        {
            if (string.Equals(this.Items[index].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                target = this.Items[index];
                break;
            }
        }
        this.SetCurrent(target);
    }

    /// <summary>取当前项在列表中的下标（未找到返回 -1）。</summary>
    public int IndexOfCurrent()
    {
        if (this.currentItem == null)
        {
            return -1;
        }
        for (int index = 0; index < this.Items.Count; index++)
        {
            if (object.ReferenceEquals(this.Items[index], this.currentItem))
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>取相对当前项偏移若干位的条目（循环滚动，列表为空时返回 null）。</summary>
    public MediaPlaylistItemViewModel? GetNeighbor(int offset)
    {
        if (this.Items.Count == 0)
        {
            return null;
        }
        int index = this.IndexOfCurrent();
        if (index < 0)
        {
            return this.Items[0];
        }
        int target = index + offset;
        if (target < 0)
        {
            target = this.Items.Count - 1;
        }
        if (target >= this.Items.Count)
        {
            target = 0;
        }
        return this.Items[target];
    }

    /// <summary>清空列表（关闭标签时调用）。</summary>
    public void Clear()
    {
        this.loadGeneration++;
        this.Items.Clear();
        this.CurrentItem = null;
        this.SummaryText = "无可用媒体";
    }

    /// <summary>设置当前项：同步高亮与已看标记。</summary>
    private void SetCurrent(MediaPlaylistItemViewModel? target)
    {
        if (this.currentItem != null && !object.ReferenceEquals(this.currentItem, target))
        {
            this.currentItem.IsCurrent = false;
        }
        this.CurrentItem = target;
        if (target != null)
        {
            target.IsCurrent = true;
            target.IsViewed = true;
            this.viewedPaths[target.FullPath] = true;
        }
    }

    /// <summary>后台扫描目录：只收录可播放的媒体，跳过隐藏 / 系统文件与零字节文件。</summary>
    private static List<MediaItem> ScanDirectory(string directory, MediaKind kind, MediaPlaylistScope scope)
    {
        List<MediaItem> items = new List<MediaItem>();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return items;
        }
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory);
        }
        catch (Exception)
        {
            return items;
        }
        foreach (string file in files)
        {
            if (items.Count >= MaxItems)
            {
                break;
            }
            if (!MediaFormatCatalog.IsPlayable(file))
            {
                continue;
            }
            MediaKind fileKind = MediaFormatCatalog.Classify(file);
            if (scope == MediaPlaylistScope.SameKind && fileKind != kind)
            {
                continue;
            }
            if (IsHiddenOrSystem(file))
            {
                continue;
            }
            MediaItem item = MediaItem.FromPath(file, fileKind);
            item.FillFileInfo();
            if (item.SizeBytes <= 0)
            {
                // 零字节文件多半是未完成的下载或占位文件，播放必然失败，直接跳过
                continue;
            }
            items.Add(item);
        }
        return items;
    }

    /// <summary>判断文件是否带隐藏或系统属性。</summary>
    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Hidden) == FileAttributes.Hidden
                || (attributes & FileAttributes.System) == FileAttributes.System;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>按指定方式排序（文件名用自然排序，保证 img2 在 img10 之前）。</summary>
    private static void SortItems(List<MediaItem> items, MediaPlaylistSort sort)
    {
        if (sort == MediaPlaylistSort.Modified)
        {
            items.Sort(delegate (MediaItem left, MediaItem right)
            {
                return right.ModifiedAt.CompareTo(left.ModifiedAt);
            });
            return;
        }
        if (sort == MediaPlaylistSort.Size)
        {
            items.Sort(delegate (MediaItem left, MediaItem right)
            {
                return right.SizeBytes.CompareTo(left.SizeBytes);
            });
            return;
        }
        items.Sort(delegate (MediaItem left, MediaItem right)
        {
            return MediaFormatCatalog.NaturalComparer.Compare(left.Name, right.Name);
        });
    }

    /// <summary>触发属性变更通知。</summary>
    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
