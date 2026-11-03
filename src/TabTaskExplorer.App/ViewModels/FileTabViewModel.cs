using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Media;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 文件视图模式：详细信息、大图标、中图标。
/// </summary>
public enum FileViewMode
{
    Details = 0,         // 详细信息
    List = 1,            // 列表
    ExtraLargeIcons = 2, // 超大图标
    LargeIcons = 3,      // 大图标
    MediumIcons = 4,     // 中图标
    SmallIcons = 5,      // 小图标
    Tile = 6,            // 平铺
    Content = 7,         // 内容
}

/// <summary>
/// 文件标签页：一个标签对应一个独立的目录浏览上下文（当前路径、历史、排序、搜索、视图模式）。
/// 路径为空字符串时表示"此电脑"根视图，展示全部磁盘分区。
/// 目录枚举在后台线程执行，避免大文件夹冻结界面。
/// </summary>
public class FileTabViewModel : INotifyPropertyChanged, IColumnWidthViewModel, ITabView
{
    /// <summary>标签类型标识：文件列表。</summary>
    public string Kind
    {
        get { return TabKinds.File; }
    }

    /// <summary>把文件列表状态写入网页端快照（桌面与网页共用）。</summary>
    public void FillSnapshot(DebugTabState tabState)
    {
        TabSnapshotFiller.FillFile(this, tabState);
    }

    private readonly IFileSystemProvider fileSystemProvider;
    private readonly AppSettings appSettings;

    /// <summary>压缩包服务：识别压缩包文件并触发预览标签打开请求（与提供者无关，始终可用）。</summary>
    private readonly IArchiveService archiveService;

    /// <summary>磁盘扫描服务：提供目录大小的单飞共享计算与快照复用；远程标签为 null，不做本机回填。</summary>
    private readonly DiskScanService? scanService;

    private readonly List<string> backStack = new List<string>();
    private readonly List<string> forwardStack = new List<string>();
    private List<FileRow> allRows = new List<FileRow>();

    private string currentPath = string.Empty;
    private string title = "此电脑";
    private string searchText = string.Empty;
    private string sortField = "Name";
    private bool sortAscending = true;
    private bool canGoBack;
    private bool canGoForward;
    private string summaryText = "正在加载……";
    private FileViewMode viewMode = FileViewMode.Details;
    private bool showInfoPane;
    private IReadOnlyList<FileRow> visibleRows = new List<FileRow>();

    /// <summary>表头列宽：仅本次运行内有效，重启后恢复界面文件里的默认列宽。</summary>
    private string columnWidths = string.Empty;

    // 后台加载的状态：分代令牌用于丢弃过期的加载结果
    private int loadGeneration;
    private string fetchPath = string.Empty;
    private int fetchGeneration = -1;
    private List<FileRow> fetchedRows = new List<FileRow>();
    private int fetchedGeneration = -1;

    // 已经在界面线程应用到 allRows 的加载分代：必须与 fetchedGeneration 区分，
    // 后者由后台线程先写入、界面行数还没填充，用它判断会误认为“列表已就绪”
    private int appliedGeneration = -1;

    // 系统唤起“在文件夹中显示”挂起的待选中条目：列表加载完成后自动应用
    private string? pendingSelectPath;

    // 目录大小回填：切路径、刷新或关闭标签时取消，只让本订阅者退出共享计算
    private CancellationTokenSource? sizeBackfillCancellation;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前目录变化事件（供主窗口记录"最近访问"）。</summary>
    public event EventHandler<string>? PathChanged;

    /// <summary>界面绑定的条目列表（已经过排序与搜索过滤）。整表替换而非逐项增删，配合虚拟化保证大目录流畅。</summary>
    public IReadOnlyList<FileRow> VisibleRows
    {
        get { return this.visibleRows; }
        private set
        {
            this.visibleRows = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleRows)));
        }
    }

    /// <summary>关联的连接配置编号；本地标签为空文本。</summary>
    public string AttachedProfileId { get; set; } = string.Empty;

    /// <summary>表头列宽（逗号分隔的像素值）：同时通知表头与行模板两处网格刷新。</summary>
    public string ColumnWidths
    {
        get { return this.columnWidths; }
        set
        {
            this.columnWidths = value ?? string.Empty;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColumnWidths)));
        }
    }

    /// <summary>当前目录完整路径，空字符串表示此电脑根视图。</summary>
    public string CurrentPath
    {
        get { return this.currentPath; }
        private set
        {
            this.currentPath = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPath)));
        }
    }

    /// <summary>标签标题：此电脑、本地磁盘或文件夹名称。</summary>
    public string Title
    {
        get { return this.title; }
        private set
        {
            this.title = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    /// <summary>搜索关键字，输入时即时过滤当前列表。</summary>
    public string SearchText
    {
        get { return this.searchText; }
        set
        {
            this.searchText = value;
            this.ApplyFilter();
        }
    }

    /// <summary>当前排序字段：Name / Modified / Type / Size（供网页端快照与列头高亮）。</summary>
    public string SortField
    {
        get { return this.sortField; }
    }

    /// <summary>排序是否为升序（供网页端快照与列头箭头方向）。</summary>
    public bool SortAscending
    {
        get { return this.sortAscending; }
    }

    /// <summary>是否可以后退。</summary>
    public bool CanGoBack
    {
        get { return this.canGoBack; }
        private set
        {
            this.canGoBack = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
        }
    }

    /// <summary>是否可以前进。</summary>
    public bool CanGoForward
    {
        get { return this.canGoForward; }
        private set
        {
            this.canGoForward = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoForward)));
        }
    }

    /// <summary>底部摘要文字：项目数量或提示信息。</summary>
    public string SummaryText
    {
        get { return this.summaryText; }
        private set
        {
            this.summaryText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryText)));
        }
    }

    /// <summary>当前视图模式。</summary>
    public FileViewMode ViewMode
    {
        get { return this.viewMode; }
        private set
        {
            this.viewMode = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewMode)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDetailsLayout)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIconsLayout)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsListLayout)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTileLayout)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsContentLayout)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRowStyleLayout)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileWidth)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileIconSize)));
        }
    }

    /// <summary>是否为详细信息布局。</summary>
    public bool IsDetailsLayout
    {
        get { return this.viewMode == FileViewMode.Details; }
    }

    /// <summary>是否为图标布局（超大 / 大 / 中 / 小图标）。</summary>
    public bool IsIconsLayout
    {
        get
        {
            return this.viewMode == FileViewMode.ExtraLargeIcons
                || this.viewMode == FileViewMode.LargeIcons
                || this.viewMode == FileViewMode.MediumIcons
                || this.viewMode == FileViewMode.SmallIcons;
        }
    }

    /// <summary>是否为列表布局（仅名称列）。</summary>
    public bool IsListLayout
    {
        get { return this.viewMode == FileViewMode.List; }
    }

    /// <summary>是否为平铺布局（图标 + 名称 + 类型两行）。</summary>
    public bool IsTileLayout
    {
        get { return this.viewMode == FileViewMode.Tile; }
    }

    /// <summary>是否为内容布局（名称 + 类型行式展示）。</summary>
    public bool IsContentLayout
    {
        get { return this.viewMode == FileViewMode.Content; }
    }

    /// <summary>是否为行式布局（详细信息 / 列表 / 内容 共用列表控件）。</summary>
    public bool IsRowStyleLayout
    {
        get
        {
            return this.viewMode == FileViewMode.Details
                || this.viewMode == FileViewMode.List
                || this.viewMode == FileViewMode.Content;
        }
    }

    /// <summary>图标模式的磁贴宽度。</summary>
    public double TileWidth
    {
        get
        {
            if (this.viewMode == FileViewMode.ExtraLargeIcons)
            {
                return 210;
            }
            if (this.viewMode == FileViewMode.LargeIcons)
            {
                return 130;
            }
            if (this.viewMode == FileViewMode.SmallIcons)
            {
                return 64;
            }
            return 96;
        }
    }

    /// <summary>图标模式下文件图标的显示边长：随视图模式放大或缩小。</summary>
    public double TileIconSize
    {
        get
        {
            if (this.viewMode == FileViewMode.ExtraLargeIcons)
            {
                return 64;
            }
            if (this.viewMode == FileViewMode.LargeIcons)
            {
                return 48;
            }
            if (this.viewMode == FileViewMode.MediumIcons)
            {
                return 36;
            }
            if (this.viewMode == FileViewMode.SmallIcons)
            {
                return 24;
            }
            if (this.viewMode == FileViewMode.Tile)
            {
                return 40;
            }
            return 32;
        }
    }

    private bool isPinned;

    /// <summary>标签是否被固定（固定后排在标签条最前）。</summary>
    public bool IsPinned
    {
        get { return this.isPinned; }
        set
        {
            this.isPinned = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
        }
    }

    /// <summary>是否显示右侧信息窗格。</summary>
    public bool ShowInfoPane
    {
        get { return this.showInfoPane; }
        set
        {
            this.showInfoPane = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowInfoPane)));
        }
    }

    /// <summary>把拖动得到的全像素列宽应用到界面（表头与所有行同时生效）。</summary>
    /// <param name="widths">按列序号排列的列宽，单位像素。</param>
    public void ApplyColumnWidths(double[] widths)
    {
        this.ColumnWidths = ColumnResize.FormatWidths(widths);
    }

    /// <summary>拖动结束后记录一条包含视图名与全部列宽的纯中文日志。</summary>
    public void LogColumnWidths()
    {
        if (this.columnWidths.Length == 0)
        {
            UiLogger.Log("文件列表", "表头列宽拖动结束：本次没有产生列宽变化，仍为默认列宽");
            return;
        }
        string[] parts = this.columnWidths.Split(',');
        UiLogger.Log("文件列表", "表头列宽拖动完成：列数=" + parts.Length
            + " 调整后列宽=" + this.columnWidths + "像素 列宽仅本次运行有效，重启后恢复默认列宽");
    }

    public FileTabViewModel(IFileSystemProvider fileSystemProvider, AppSettings appSettings,
        string openPath, DiskScanService? diskScanService, IArchiveService archiveService)
    {
        this.fileSystemProvider = fileSystemProvider;
        this.appSettings = appSettings;
        this.scanService = diskScanService;
        this.archiveService = archiveService;
        this.LoadCore(openPath, true);
    }

    /// <summary>跳转到指定路径并记录历史。</summary>
    public void OpenPath(string path)
    {
        if (path == this.currentPath)
        {
            this.Refresh();
            return;
        }
        this.LoadCore(path, true);
    }

    /// <summary>后退到上一个路径。</summary>
    public void GoBack()
    {
        if (this.backStack.Count == 0)
        {
            return;
        }
        int lastIndex = this.backStack.Count - 1;
        string target = this.backStack[lastIndex];
        this.backStack.RemoveAt(lastIndex);
        this.forwardStack.Add(this.currentPath);
        this.LoadCore(target, false);
    }

    /// <summary>前进到下一个路径。</summary>
    public void GoForward()
    {
        if (this.forwardStack.Count == 0)
        {
            return;
        }
        int lastIndex = this.forwardStack.Count - 1;
        string target = this.forwardStack[lastIndex];
        this.forwardStack.RemoveAt(lastIndex);
        this.backStack.Add(this.currentPath);
        this.LoadCore(target, false);
    }

    /// <summary>返回上一层目录；已在磁盘根目录时回到此电脑视图，已在压缩包根时停住。</summary>
    public void NavigateUp()
    {
        if (string.IsNullOrEmpty(this.currentPath))
        {
            return;
        }
        ArchiveFileSystemProvider? archiveProvider = this.fileSystemProvider as ArchiveFileSystemProvider;
        if (archiveProvider != null && archiveProvider.IsRoot(this.currentPath))
        {
            // 已在压缩包根：再向上会离开压缩包，虚拟路径无法对应真实目录，停住不动
            return;
        }
        DirectoryInfo? parent = Directory.GetParent(this.currentPath);
        if (parent == null)
        {
            this.OpenPath(string.Empty);
        }
        else
        {
            this.OpenPath(parent.FullName);
        }
    }

    /// <summary>按当前设置重新加载当前目录。</summary>
    public void Refresh()
    {
        this.LoadCore(this.currentPath, false);
    }

    /// <summary>设置视图模式（供查看菜单调用）。</summary>
    public void SetViewMode(FileViewMode mode)
    {
        this.ViewMode = mode;
    }

    /// <summary>切换视图模式：按超大 → 大 → 中 → 小 → 列表 → 平铺 → 内容 → 详细信息 循环。</summary>
    public void CycleViewMode()
    {
        FileViewMode next;
        if (this.viewMode == FileViewMode.Details)
        {
            next = FileViewMode.ExtraLargeIcons;
        }
        else if (this.viewMode == FileViewMode.ExtraLargeIcons)
        {
            next = FileViewMode.LargeIcons;
        }
        else if (this.viewMode == FileViewMode.LargeIcons)
        {
            next = FileViewMode.MediumIcons;
        }
        else if (this.viewMode == FileViewMode.MediumIcons)
        {
            next = FileViewMode.SmallIcons;
        }
        else if (this.viewMode == FileViewMode.SmallIcons)
        {
            next = FileViewMode.List;
        }
        else if (this.viewMode == FileViewMode.List)
        {
            next = FileViewMode.Tile;
        }
        else if (this.viewMode == FileViewMode.Tile)
        {
            next = FileViewMode.Content;
        }
        else
        {
            next = FileViewMode.Details;
        }
        this.ViewMode = next;
    }

    /// <summary>设置排序字段；重复点击同一字段时切换升序降序。</summary>
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
        this.allRows.Sort(new Comparison<FileRow>(this.CompareRows));
        this.ApplyFilter();
    }

    /// <summary>在当前目录下创建新文件夹。</summary>
    public void CreateFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrEmpty(this.currentPath))
        {
            return;
        }
        try
        {
            this.fileSystemProvider.CreateDirectory(Path.Combine(this.currentPath, folderName));
            this.Refresh();
        }
        catch (Exception error)
        {
            this.SummaryText = "创建文件夹失败：" + error.Message;
        }
    }

    /// <summary>重命名唯一勾选的条目。</summary>
    public void RenameChecked(string newName)
    {
        FileRow? row = this.GetSingleCheckedRow();
        if (row == null || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }
        this.RenameRow(row, newName);
    }

    /// <summary>重命名指定条目。</summary>
    public void RenameRow(FileRow row, string newName)
    {
        try
        {
            this.fileSystemProvider.Rename(row.FullPath, newName);
            this.Refresh();
        }
        catch (Exception error)
        {
            this.SummaryText = "重命名失败：" + error.Message;
        }
    }

    /// <summary>删除全部勾选的条目（Windows 移入回收站）。</summary>
    public void DeleteChecked()
    {
        int deletedCount = 0;
        int failedCount = 0;
        foreach (FileRow row in this.allRows)
        {
            if (!row.IsSelected)
            {
                continue;
            }
            try
            {
                this.fileSystemProvider.Delete(row.FullPath);
                deletedCount++;
            }
            catch
            {
                failedCount++;
            }
        }
        if (failedCount > 0)
        {
            this.SummaryText = "已删除 " + deletedCount + " 项，" + failedCount + " 项删除失败";
        }
        this.Refresh();
    }

    /// <summary>删除单个条目（Windows 移入回收站）。</summary>
    public void DeleteRow(FileRow row)
    {
        try
        {
            this.fileSystemProvider.Delete(row.FullPath);
            this.Refresh();
        }
        catch (Exception error)
        {
            this.SummaryText = "删除失败：" + error.Message;
        }
    }

    /// <summary>执行批量改名计划：entry[0] 为旧路径，entry[1] 为新名称。</summary>
    public void ApplyBatchRenames(List<string[]> renamePlan)
    {
        // 自底向上执行：路径更深（分隔符更多）的先改，避免先改父目录后子路径失效
        renamePlan.Sort(CompareRenameDepth);
        int successCount = 0;
        int failedCount = 0;
        foreach (string[] entry in renamePlan)
        {
            if (entry.Length < 2)
            {
                continue;
            }
            string originalName = Path.GetFileName(entry[0]);
            if (originalName == entry[1])
            {
                continue;
            }
            try
            {
                this.fileSystemProvider.Rename(entry[0], entry[1]);
                successCount++;
            }
            catch
            {
                failedCount++;
            }
        }
        this.SummaryText = "批量改名完成：成功 " + successCount + " 项，失败 " + failedCount + " 项";
        this.Refresh();
    }

    /// <summary>比较两条改名计划的路径深度：分隔符多（更深）的排在前面，实现自底向上执行。</summary>
    private static int CompareRenameDepth(string[] left, string[] right)
    {
        int leftDepth = CountSeparators(left.Length > 0 ? left[0] : string.Empty);
        int rightDepth = CountSeparators(right.Length > 0 ? right[0] : string.Empty);
        return rightDepth.CompareTo(leftDepth);
    }

    /// <summary>统计路径里的分隔符个数，作为路径深度的近似。</summary>
    private static int CountSeparators(string path)
    {
        int count = 0;
        for (int index = 0; index < path.Length; index++)
        {
            if (path[index] == '/' || path[index] == '\\')
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 为选中路径生成编码转换计划（递归时收集全部后代），
    /// 供桌面对话框预览与网页端预览共用。
    /// </summary>
    /// <param name="paths">选中的完整路径列表。</param>
    /// <param name="recursive">是否递归处理选中目录的全部后代。</param>
    /// <param name="fromCodePage">源编码代码页。</param>
    /// <param name="toCodePage">目标编码代码页。</param>
    /// <returns>转换计划列表（旧路径、新名称、原因）。</returns>
    public List<EncodeConvertEntry> PreviewEncodeConvert(List<string> paths, bool recursive,
        int fromCodePage, int toCodePage)
    {
        List<EncodeConvertEntry> plan = FileNameEncodingConverter.BuildPlan(
            this.fileSystemProvider, paths, recursive, fromCodePage, toCodePage);
        int executableCount = 0;
        foreach (EncodeConvertEntry entry in plan)
        {
            if (entry.Reason.Length == 0
                && entry.NewName != FileNameEncodingConverter.GetEntryName(entry.OldPath))
            {
                executableCount++;
            }
        }
        UiLogger.Log("编码转换", "预览编码转换：路径数=" + paths.Count
            + " 源编码=" + FileNameEncodingConverter.GetEncodingName(fromCodePage)
            + " 目标编码=" + FileNameEncodingConverter.GetEncodingName(toCodePage)
            + " 递归=" + (recursive ? "是" : "否")
            + " 计划条数=" + plan.Count + " 可执行条数=" + executableCount);
        return plan;
    }

    /// <summary>远程文件内容打开请求（仅当提供者支持内容读写时触发），参数为文件完整路径。</summary>
    public event EventHandler<string>? RemoteFileOpenRequested;

    /// <summary>压缩包打开请求：主窗口据此在对应压缩包文件系统上新开预览标签，参数为压缩包完整路径。</summary>
    public event EventHandler<string>? ArchiveOpenRequested;

    /// <summary>
    /// 媒体文件打开请求：主窗口据此新开（或复用）媒体标签，参数为媒体文件完整路径。
    /// 仅当「媒体打开方式」不是「系统默认程序」时触发；图片 / 视频 / 音频统一走这里。
    /// </summary>
    public event EventHandler<string>? MediaOpenRequested;

    /// <summary>远程文件内容访问能力；本地提供者不支持时为 null。</summary>
    public IFileContentAccess? ContentAccess
    {
        get { return this.fileSystemProvider as IFileContentAccess; }
    }

    /// <summary>当前文件系统提供者（IDE 模式据此把当前目录作为工作区，本机 / 远程通用）。</summary>
    public IFileSystemProvider FileSystemProvider
    {
        get { return this.fileSystemProvider; }
    }

    /// <summary>远程权限修改能力；本地提供者不支持时为 null。</summary>
    public IRemotePermissionChanger? PermissionChanger
    {
        get { return this.fileSystemProvider as IRemotePermissionChanger; }
    }

    /// <summary>打开一个条目：文件夹进入目录，远程文件在线编辑，压缩包开预览标签，本地文件用系统默认程序打开。</summary>
    public void OpenEntry(FileRow row)
    {
        if (row.IsDirectory)
        {
            this.OpenPath(row.FullPath);
            return;
        }
        // 远程文件（提供者支持内容读写）双击后进入在线编辑标签
        if (this.ContentAccess != null)
        {
            EventHandler<string>? handler = this.RemoteFileOpenRequested;
            if (handler != null)
            {
                handler(this, row.FullPath);
            }
            return;
        }
        ArchiveFileSystemProvider? archiveProvider = this.fileSystemProvider as ArchiveFileSystemProvider;
        if (archiveProvider != null)
        {
            // 压缩包标签内：压缩包条目再开一层预览标签，其余文件解出临时文件后交给系统程序打开
            if (this.archiveService.IsArchivePath(row.FullPath))
            {
                this.RaiseArchiveOpenRequested(row.FullPath);
                return;
            }
            try
            {
                string tempFile = archiveProvider.ExtractFileToTemp(row.FullPath);
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = tempFile;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception error)
            {
                this.SummaryText = "无法打开该文件：" + error.Message;
            }
            return;
        }
        // 普通标签：压缩包文件开预览标签（经魔数校验，防止伪装扩展名的普通文件被误判）
        if (this.archiveService.IsArchivePath(row.FullPath) && this.archiveService.Probe(row.FullPath) != null)
        {
            this.RaiseArchiveOpenRequested(row.FullPath);
            return;
        }
        // 媒体文件：按「媒体打开方式」设置交给内置媒体标签（图片 / 视频 / 音频）；
        // 设置为「系统默认程序」时不触发事件，继续走下方系统打开分支
        MediaKind mediaKind = MediaFormatCatalog.Classify(row.FullPath);
        if (mediaKind != MediaKind.Unknown && MediaFormatCatalog.IsPlayable(row.FullPath))
        {
            string mediaMode = MediaOpenPolicy.ResolveMode(
                this.appSettings.MediaOpenRules, this.appSettings.MediaOpenMode, mediaKind);
            if (mediaMode != MediaOpenModeValues.System)
            {
                EventHandler<string>? mediaHandler = this.MediaOpenRequested;
                if (mediaHandler != null)
                {
                    mediaHandler(this, row.FullPath);
                    return;
                }
            }
        }
        // 本机文件一律交给系统默认程序打开（IDE 模式下的打开方式由 IDE 侧设置决定）
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = row.FullPath;
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }
        catch (Exception error)
        {
            this.SummaryText = "无法打开该文件：" + error.Message;
        }
    }
    /// <summary>发出压缩包打开请求（无订阅方时静默忽略）。</summary>
    private void RaiseArchiveOpenRequested(string path)
    {
        EventHandler<string>? handler = this.ArchiveOpenRequested;
        if (handler != null)
        {
            handler(this, path);
        }
    }

    /// <summary>右键"解压到"前置判断：压缩包标签内任意条目可解出，普通标签仅压缩包条目可解。</summary>
    public bool IsExtractableRow(FileRow row)
    {
        if (this.fileSystemProvider is ArchiveFileSystemProvider)
        {
            return true;
        }
        return this.archiveService.IsArchivePath(row.FullPath) && this.archiveService.Probe(row.FullPath) != null;
    }

    /// <summary>
    /// 把指定条目压缩为新压缩包（后台执行，进度显示在摘要栏）；
    /// 目标格式按扩展名自动识别（zip / 7z / tar / tar.gz 等）。
    /// </summary>
    public void CompressSelection(List<FileRow> rows, string archivePath)
    {
        if (rows == null || rows.Count == 0)
        {
            return;
        }
        if (this.fileSystemProvider is ArchiveFileSystemProvider)
        {
            this.SummaryText = "压缩包内条目暂不支持直接压缩，请先解压到普通文件夹再压缩";
            return;
        }
        List<string> sources = new List<string>();
        foreach (FileRow row in rows)
        {
            sources.Add(row.FullPath);
        }

        Progress<ArchiveProgress> progress = new Progress<ArchiveProgress>(delegate (ArchiveProgress report)
        {
            this.SummaryText = report.EntriesTotal > 0
                ? "正在压缩：" + report.EntriesDone + "/" + report.EntriesTotal + " " + report.CurrentEntryName
                : "正在压缩：" + report.CurrentEntryName;
        });
        this.SummaryText = "正在压缩 " + sources.Count + " 个条目……";
        UiLogger.Log("压缩", "开始压缩：目标=" + archivePath + " 源条目数=" + sources.Count);
        Task.Run(delegate
        {
            try
            {
                ArchiveCreateOptions options = new ArchiveCreateOptions();
                this.archiveService.Create(archivePath, sources, options, progress);
                UiLogger.Log("压缩", "压缩完成：目标=" + archivePath);
                Dispatcher.UIThread.Post(delegate
                {
                    this.SummaryText = "已创建压缩包：" + Path.GetFileName(archivePath);
                    this.Refresh();
                });
            }
            catch (Exception error)
            {
                UiLogger.Log("压缩", "压缩失败：目标=" + archivePath + " 原因=" + error.Message);
                Dispatcher.UIThread.Post(delegate
                {
                    this.SummaryText = "压缩失败：" + error.Message;
                });
            }
        });
    }

    /// <summary>
    /// 把选中条目解压到指定真实目录（后台执行，进度显示在摘要栏）：
    /// 普通标签解压选中的压缩包本体；压缩包标签逐条把包内条目解出到目标目录。
    /// </summary>
    public void ExtractSelectionTo(List<FileRow> rows, string targetDirectory)
    {
        if (rows == null || rows.Count == 0)
        {
            return;
        }
        ArchiveFileSystemProvider? archiveProvider = this.fileSystemProvider as ArchiveFileSystemProvider;
        List<string> targets = new List<string>();
        foreach (FileRow row in rows)
        {
            if (archiveProvider != null
                || (this.archiveService.IsArchivePath(row.FullPath) && this.archiveService.Probe(row.FullPath) != null))
            {
                targets.Add(row.FullPath);
            }
        }
        if (targets.Count == 0)
        {
            this.SummaryText = "选中的条目里没有可解压的压缩包";
            return;
        }

        Progress<ArchiveProgress> progress = new Progress<ArchiveProgress>(delegate (ArchiveProgress report)
        {
            this.SummaryText = report.EntriesTotal > 0
                ? "正在解压：" + report.EntriesDone + "/" + report.EntriesTotal + " " + report.CurrentEntryName
                : "正在解压：" + report.CurrentEntryName;
        });
        this.SummaryText = "正在解压到：" + targetDirectory;
        UiLogger.Log("解压", "开始解压：目标目录=" + targetDirectory + " 条目数=" + targets.Count);
        Task.Run(delegate
        {
            int successCount = 0;
            int failedCount = 0;
            string firstError = string.Empty;
            for (int i = 0; i < targets.Count; i++)
            {
                string target = targets[i];
                int displayIndex = i;
                if (archiveProvider == null)
                {
                    // 普通标签：解压整个压缩包到目标目录
                    try
                    {
                        ArchiveExtractOptions options = new ArchiveExtractOptions();
                        options.TargetDirectory = targetDirectory;
                        options.Overwrite = true;
                        this.archiveService.Extract(target, options, progress);
                        successCount++;
                    }
                    catch (Exception error)
                    {
                        failedCount++;
                        if (firstError.Length == 0)
                        {
                            firstError = error.Message;
                        }
                    }
                }
                else
                {
                    // 压缩包标签：包内条目跨界解压到目标目录（保留原名称，同名目录合并）
                    Dispatcher.UIThread.Post(delegate
                    {
                        this.SummaryText = "正在解压：" + (displayIndex + 1) + "/" + targets.Count + " " + Path.GetFileName(target);
                    });
                    try
                    {
                        archiveProvider.Copy(target, targetDirectory);
                        successCount++;
                    }
                    catch (Exception error)
                    {
                        failedCount++;
                        if (firstError.Length == 0)
                        {
                            firstError = error.Message;
                        }
                    }
                }
            }
            UiLogger.Log("解压", "解压结束：目标目录=" + targetDirectory
                + " 成功=" + successCount + " 失败=" + failedCount
                + (firstError.Length > 0 ? " 首个错误=" + firstError : string.Empty));
            Dispatcher.UIThread.Post(delegate
            {
                this.SummaryText = failedCount > 0
                    ? "解压完成：成功 " + successCount + " 项，失败 " + failedCount + " 项（" + firstError + "）"
                    : "解压完成：成功 " + successCount + " 项 → " + targetDirectory;
            });
        });
    }

    /// <summary>返回唯一勾选的条目；没有勾选或勾选多项时返回 null 并提示。</summary>
    public FileRow? GetSingleCheckedRow()
    {
        FileRow? found = this.FindSingleCheckedRow();
        if (found == null)
        {
            int checkedCount = this.GetCheckedRows().Count;
            this.SummaryText = checkedCount == 0 ? "请先勾选一个条目" : "一次只能操作一个条目";
        }
        return found;
    }

    /// <summary>查找唯一勾选的条目，无副作用。</summary>
    public FileRow? FindSingleCheckedRow()
    {
        FileRow? found = null;
        int checkedCount = 0;
        foreach (FileRow row in this.allRows)
        {
            if (row.IsSelected)
            {
                checkedCount++;
                found = row;
            }
        }
        if (checkedCount == 1)
        {
            return found;
        }
        return null;
    }

    /// <summary>获取全部勾选的条目。</summary>
    public List<FileRow> GetCheckedRows()
    {
        List<FileRow> checkedRows = new List<FileRow>();
        foreach (FileRow row in this.allRows)
        {
            if (row.IsSelected)
            {
                checkedRows.Add(row);
            }
        }
        return checkedRows;
    }

    /// <summary>获取全部勾选条目的完整路径（供复制剪切粘贴使用）。</summary>
    public List<string> GetCheckedPaths()
    {
        List<string> paths = new List<string>();
        foreach (FileRow row in this.allRows)
        {
            if (row.IsSelected)
            {
                paths.Add(row.FullPath);
            }
        }
        return paths;
    }

    /// <summary>直接设置底部提示文字。</summary>
    public void ShowMessage(string message)
    {
        this.SummaryText = message;
    }

    /// <summary>清空全部勾选。</summary>
    public void ClearChecked()
    {
        foreach (FileRow row in this.allRows)
        {
            row.IsSelected = false;
        }
    }

    /// <summary>把勾选状态设置为指定路径集合（网页端多选回传用，路径比较不区分大小写）。</summary>
    public void SetCheckedPaths(IReadOnlyCollection<string> paths)
    {
        HashSet<string> lookup = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        foreach (FileRow row in this.allRows)
        {
            row.IsSelected = lookup.Contains(row.FullPath);
        }
    }

    /// <summary>
    /// 系统唤起“在文件夹中显示”：选中指定条目。
    /// 目录列表尚未加载完成时先挂起，待本次加载结果应用后再勾选。
    /// </summary>
    public void SelectEntry(string fullPath)
    {
        this.pendingSelectPath = fullPath;
        bool listIsCurrent = this.appliedGeneration == this.loadGeneration;
        UiLogger.Log("文件标签", "系统唤起选中条目登记：路径=" + fullPath + " 标签路径=" + this.currentPath
            + " 列表已是最新结果=" + listIsCurrent + " 当前行数=" + this.allRows.Count);
        this.TryApplyPendingSelect();
    }

    /// <summary>尝试应用挂起的条目选中：列表已是当前路径的最新结果时立即生效或清除。</summary>
    private void TryApplyPendingSelect()
    {
        string? want = this.pendingSelectPath;
        if (want == null)
        {
            return;
        }
        bool listIsCurrent = this.appliedGeneration == this.loadGeneration;
        foreach (FileRow row in this.allRows)
        {
            if (!string.Equals(row.FullPath, want, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            this.pendingSelectPath = null;
            foreach (FileRow other in this.allRows)
            {
                other.IsSelected = object.ReferenceEquals(other, row);
            }
            this.SummaryText = "已选中 " + row.DisplayName;
            UiLogger.Log("文件标签", "系统唤起选中条目：路径=" + want + " 标签路径=" + this.currentPath);
            return;
        }
        if (listIsCurrent)
        {
            // 列表已是最新结果但没有该条目（文件已不在目录中）：清除挂起，避免污染下次加载
            this.pendingSelectPath = null;
            UiLogger.Log("文件标签", "系统唤起选中条目未找到，本次不选中：路径=" + want
                + " 标签路径=" + this.currentPath + " 当前行数=" + this.allRows.Count
                + " 前三行=" + string.Join(" ; ", SamplePaths(3)));
        }
    }

    /// <summary>取前 N 行路径样本，供选中失败时的诊断日志定位路径不一致问题。</summary>
    private string SamplePaths(int count)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < this.allRows.Count && i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(" ; ");
            }
            builder.Append(this.allRows[i].FullPath);
        }
        return builder.ToString();
    }

    /// <summary>全选当前目录全部条目。</summary>
    public void SelectAll()
    {
        foreach (FileRow row in this.allRows)
        {
            row.IsSelected = true;
        }
        if (this.allRows.Count > 0)
        {
            this.SummaryText = "已全选 " + this.allRows.Count + " 个项目";
        }
    }

    /// <summary>同步更新历史与标题，随后把目录枚举派发到后台线程。</summary>
    private void LoadCore(string path, bool recordHistory)
    {
        if (recordHistory && path != this.currentPath)
        {
            this.backStack.Add(this.currentPath);
            this.forwardStack.Clear();
        }

        this.currentPath = path;
        this.title = GetTitleFor(path);
        this.CanGoBack = this.backStack.Count > 0;
        this.CanGoForward = this.forwardStack.Count > 0;
        this.SummaryText = "正在加载……";
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPath)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        this.PathChanged?.Invoke(this, path);

        this.fetchPath = path;
        this.CancelSizeBackfill();
        this.fetchGeneration = ++this.loadGeneration;
        Task.Run(new Action(this.FetchInBackground));
    }

    /// <summary>后台线程：枚举目录并排序，完成后回界面线程应用。</summary>
    private void FetchInBackground()
    {
        int generation = this.fetchGeneration;
        string path = this.fetchPath;

        List<FileRow> rows = new List<FileRow>();
        if (string.IsNullOrEmpty(path))
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                rows.Add(FileRow.ForDrive(drive));
            }
        }
        else
        {
            try
            {
                DirectoryListingOptions options = new DirectoryListingOptions();
                options.ShowHiddenFiles = this.appSettings.ShowHiddenFiles;
                foreach (FileEntry entry in this.fileSystemProvider.ListDirectory(path, options))
                {
                    rows.Add(FileRow.FromEntry(entry, this.appSettings.ShowFileExtensions));
                }
            }
            catch (Exception error)
            {
                // 读取失败时展示空列表，界面线程上给出提示；同时把异常写入日志，避免认证/网络失败被静默吞掉
                UiLogger.LogError("文件标签", "读取目录失败：标签标题=" + this.title + " 路径=" + path
                    + " 文件系统提供者=" + this.fileSystemProvider.GetType().Name, error);
            }
        }

        rows.Sort(new Comparison<FileRow>(this.CompareRows));

        this.fetchedRows = rows;
        this.fetchedGeneration = generation;
        Dispatcher.UIThread.Post(new Action(this.ApplyFetchedRows));
    }

    /// <summary>在界面线程上应用后台加载结果；过期分代直接丢弃。</summary>
    private void ApplyFetchedRows()
    {
        if (this.fetchedGeneration != this.loadGeneration)
        {
            return;
        }

        this.allRows = new List<FileRow>(this.fetchedRows);
        this.appliedGeneration = this.loadGeneration;
        this.ApplyFilter();
        this.TryApplyPendingSelect();
        this.StartSizeBackfill();

        if (this.allRows.Count == 0 && !string.IsNullOrEmpty(this.currentPath))
        {
            this.SummaryText = "目录为空，或无法读取该目录";
        }
    }

    /// <summary>取消进行中的目录大小回填批次（切路径、刷新与关闭标签时调用）。</summary>
    private void CancelSizeBackfill()
    {
        CancellationTokenSource? source = this.sizeBackfillCancellation;
        if (source == null)
        {
            return;
        }
        this.sizeBackfillCancellation = null;
        // 只取消不释放：与磁盘视图一致，避免在途计算读取消令牌时撞上已释放的取消源
        source.Cancel();
    }

    /// <summary>
    /// 为当前可见的目录行启动大小回填：经扫描服务单飞共享计算，
    /// 同路径的多标签页与多视图只实际扫描一次，结果写回共享快照供复用。
    /// </summary>
    private void StartSizeBackfill()
    {
        if (this.scanService == null)
        {
            // 远程标签不做本机磁盘回填
            return;
        }
        if (string.IsNullOrEmpty(this.currentPath))
        {
            // 此电脑视图的驱动器行不回填，避免整盘递归统计
            return;
        }

        List<FileRow> targets = new List<FileRow>();
        foreach (FileRow row in this.visibleRows)
        {
            if (row.IsDirectory && string.IsNullOrEmpty(row.SizeText))
            {
                targets.Add(row);
            }
        }
        if (targets.Count == 0)
        {
            return;
        }

        this.CancelSizeBackfill();
        CancellationTokenSource source = new CancellationTokenSource();
        this.sizeBackfillCancellation = source;
        int generation = this.loadGeneration;
        SizeBackfillBatch batch = new SizeBackfillBatch(this, targets, generation, source);
        UiLogger.Log("文件标签", "开始目录大小回填：路径=" + this.currentPath
            + " 目录行数=" + targets.Count);
        Task.Run(batch.Execute);
    }

    /// <summary>关闭标签钩子：停止在途的目录大小回填，释放共享计算订阅。</summary>
    public void Close()
    {
        this.CancelSizeBackfill();
        UiLogger.Log("文件标签", "关闭文件标签：路径=" + (string.IsNullOrEmpty(this.currentPath) ? "此电脑" : this.currentPath)
            + " 已停止目录大小回填");
    }

    /// <summary>排序比较器：文件夹始终在前。</summary>
    private int CompareRows(FileRow x, FileRow y)
    {
        int directoryCompare = y.IsDirectory.CompareTo(x.IsDirectory);
        if (directoryCompare != 0)
        {
            return directoryCompare;
        }

        int result = 0;
        if (this.sortField == "Size")
        {
            result = x.LengthBytes.CompareTo(y.LengthBytes);
        }
        else if (this.sortField == "Modified")
        {
            result = x.ModifiedTimeUtc.CompareTo(y.ModifiedTimeUtc);
        }
        else if (this.sortField == "Type")
        {
            result = string.Compare(x.TypeText, y.TypeText, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            result = string.Compare(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        if (!this.sortAscending)
        {
            result = -result;
        }
        return result;
    }

    /// <summary>按搜索关键字重建可见列表（整表替换，一次绑定更新）。</summary>
    private void ApplyFilter()
    {
        List<FileRow> visible = new List<FileRow>();
        foreach (FileRow row in this.allRows)
        {
            if (this.MatchesSearch(row))
            {
                visible.Add(row);
            }
        }
        this.VisibleRows = visible;
        this.SummaryText = visible.Count + " 个项目";
    }

    /// <summary>判断条目是否命中搜索关键字。</summary>
    private bool MatchesSearch(FileRow row)
    {
        if (string.IsNullOrEmpty(this.searchText))
        {
            return true;
        }
        return row.DisplayName.IndexOf(this.searchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>根据路径计算标签标题。</summary>
    private static string GetTitleFor(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "此电脑";
        }
        string? name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
        {
            return "本地磁盘 (" + path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ")";
        }
        return name;
    }

    /// <summary>
    /// 一次目录大小回填批次：并行经共享单飞计算各可见目录行的大小，
    /// 结果回界面线程写回行数据；分代不符的过期结果直接丢弃。
    /// </summary>
    private class SizeBackfillBatch
    {
        private readonly FileTabViewModel owner;
        private readonly List<FileRow> rows;
        private readonly int generation;
        private readonly CancellationTokenSource source;
        private int successCount;
        private int failureCount;

        /// <summary>创建回填批次。</summary>
        /// <param name="owner">所属文件标签。</param>
        /// <param name="rows">需要统计大小的可见目录行。</param>
        /// <param name="generation">启动时的加载分代，用于丢弃过期结果。</param>
        /// <param name="source">本批次的取消源：取消只让本订阅者退出共享计算。</param>
        public SizeBackfillBatch(FileTabViewModel owner, List<FileRow> rows, int generation, CancellationTokenSource source)
        {
            this.owner = owner;
            this.rows = rows;
            this.generation = generation;
            this.source = source;
        }

        /// <summary>批次入口：后台并行统计，全部结束后回界面线程收尾。</summary>
        public void Execute()
        {
            try
            {
                ParallelOptions options = new ParallelOptions();
                options.MaxDegreeOfParallelism = Environment.ProcessorCount;
                options.CancellationToken = this.source.Token;
                Parallel.ForEach(this.rows, options, this.ComputeOne);
            }
            catch (OperationCanceledException)
            {
                UiLogger.Log("文件标签", "目录大小回填已取消：路径=" + this.owner.currentPath
                    + " 目录行数=" + this.rows.Count);
                return;
            }
            catch (Exception exception)
            {
                UiLogger.LogError("文件标签", "目录大小回填批次失败：路径=" + this.owner.currentPath, exception);
                return;
            }
            Dispatcher.UIThread.Post(new Action(this.OnBatchFinished));
        }

        /// <summary>统计单个目录行的大小：经共享单飞计算，完成后回界面线程写回。</summary>
        /// <param name="row">待统计的目录行。</param>
        private void ComputeOne(FileRow row)
        {
            try
            {
                DiskScanService service = this.owner.scanService!;
                long sizeBytes = service.ComputeSizeShared(row.FullPath, this.source.Token);
                Interlocked.Increment(ref this.successCount);
                Dispatcher.UIThread.Post(delegate { this.ApplyOne(row, sizeBytes); });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref this.failureCount);
                UiLogger.LogError("文件标签", "目录大小回填失败：目录=" + row.FullPath, exception);
            }
        }

        /// <summary>在界面线程把统计结果写回行；分代已变（切路径或刷新）则丢弃。</summary>
        /// <param name="row">统计完成的目录行。</param>
        /// <param name="sizeBytes">目录总大小（字节）。</param>
        private void ApplyOne(FileRow row, long sizeBytes)
        {
            if (this.generation != this.owner.loadGeneration)
            {
                return;
            }
            row.UpdateSize(sizeBytes);
            if (this.owner.sortField == "Size")
            {
                // 按大小排序时新数据改变了次序，重新排一次
                this.owner.ApplyFilter();
            }
        }

        /// <summary>批次收尾（界面线程）：释放取消源、持久化快照并记录完成日志。</summary>
        private void OnBatchFinished()
        {
            if (this.generation != this.owner.loadGeneration)
            {
                return;
            }
            if (object.ReferenceEquals(this.owner.sizeBackfillCancellation, this.source))
            {
                this.owner.sizeBackfillCancellation = null;
            }
            this.owner.scanService!.SaveSnapshot();
            UiLogger.Log("文件标签", "目录大小回填完成：路径=" + this.owner.currentPath
                + " 成功行数=" + this.successCount + " 失败行数=" + this.failureCount);
        }
    }
}

/// <summary>
/// 文件列表中的一行显示数据。
/// </summary>
public class FileRow : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>显示名称（受"显示扩展名"设置影响）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>完整路径。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>类型文字：文件夹或扩展名描述。</summary>
    public string TypeText { get; set; } = string.Empty;

    private string sizeText = string.Empty;

    /// <summary>大小显示文字；目录行由回填任务异步补上，改动需通知界面。</summary>
    public string SizeText
    {
        get { return this.sizeText; }
        set
        {
            this.sizeText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        }
    }

    /// <summary>修改时间显示文字（本地时区）。</summary>
    public string ModifiedText { get; set; } = string.Empty;

    private bool isSelected;

    /// <summary>是否被勾选。</summary>
    public bool IsSelected
    {
        get { return this.isSelected; }
        set
        {
            this.isSelected = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    /// <summary>类型图标分类键（见 <see cref="FileIconCatalog"/>）。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.File;

    /// <summary>类型图标颜色（十六进制）。</summary>
    public string IconColor { get; set; } = FileIconCatalog.GetAccent(FileIconCatalog.File);

    /// <summary>原始长度（字节），供排序使用。</summary>
    public long LengthBytes { get; set; }

    /// <summary>原始修改时间，供排序使用。</summary>
    public DateTime ModifiedTimeUtc { get; set; }

    /// <summary>从内核条目构建行数据。</summary>
    public static FileRow FromEntry(FileEntry entry, bool showFileExtensions)
    {
        FileRow row = new FileRow();
        row.DisplayName = entry.Name;
        row.FullPath = entry.FullPath;
        row.IsDirectory = entry.IsDirectory;
        row.LengthBytes = entry.LengthBytes;
        row.ModifiedTimeUtc = entry.ModifiedTimeUtc;
        row.ModifiedText = entry.ModifiedTimeUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

        if (entry.IsDirectory)
        {
            row.TypeText = "文件夹";
            row.IconCategory = FileIconCatalog.Folder;
            row.IconColor = FileIconCatalog.GetAccent(FileIconCatalog.Folder);
        }
        else
        {
            string extension = Path.GetExtension(entry.Name);
            if (!showFileExtensions)
            {
                row.DisplayName = Path.GetFileNameWithoutExtension(entry.Name);
            }
            row.TypeText = extension.TrimStart('.').ToUpperInvariant() + " 文件";
            if (extension.Length == 0)
            {
                row.TypeText = "文件";
            }
            row.SizeText = FormatSize(entry.LengthBytes);
            string category = FileIconCatalog.GetCategoryForName(entry.Name);
            row.IconCategory = category;
            row.IconColor = FileIconCatalog.GetAccent(category);
        }
        return row;
    }

    /// <summary>从全局搜索结果构建行数据。</summary>
    public static FileRow FromSearchResult(FileSearchResult result, bool showFileExtensions)
    {
        FileRow row = new FileRow();
        row.DisplayName = result.Name;
        row.FullPath = result.FullPath;
        row.IsDirectory = result.IsDirectory;
        row.LengthBytes = result.LengthBytes;
        row.ModifiedTimeUtc = result.ModifiedTimeUtc;
        row.ModifiedText = result.ModifiedTimeUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

        if (result.IsDirectory)
        {
            row.TypeText = "文件夹";
            row.IconCategory = FileIconCatalog.Folder;
            row.IconColor = FileIconCatalog.GetAccent(FileIconCatalog.Folder);
        }
        else
        {
            string extension = Path.GetExtension(result.Name);
            if (!showFileExtensions)
            {
                row.DisplayName = Path.GetFileNameWithoutExtension(result.Name);
            }
            row.TypeText = extension.TrimStart('.').ToUpperInvariant() + " 文件";
            if (extension.Length == 0)
            {
                row.TypeText = "文件";
            }
            row.SizeText = FormatSize(result.LengthBytes);
            string category = FileIconCatalog.GetCategoryForName(result.Name);
            row.IconCategory = category;
            row.IconColor = FileIconCatalog.GetAccent(category);
        }
        return row;
    }

    /// <summary>从磁盘分区构建行数据（用于此电脑视图）。</summary>
    public static FileRow ForDrive(DriveInfo drive)
    {
        FileRow row = new FileRow();
        row.DisplayName = drive.Name;
        row.FullPath = drive.Name;
        row.IsDirectory = true;
        row.TypeText = "本地磁盘";
        row.ModifiedText = string.Empty;
        row.IconCategory = FileIconCatalog.Drive;
        row.IconColor = FileIconCatalog.GetAccent(FileIconCatalog.Drive);
        return row;
    }

    /// <summary>回填目录统计结果：同时更新显示文字与排序用字节数。</summary>
    /// <param name="sizeBytes">目录总大小（字节）。</param>
    public void UpdateSize(long sizeBytes)
    {
        this.LengthBytes = sizeBytes;
        this.SizeText = FormatSize(sizeBytes);
    }

    /// <summary>把字节数格式化成易读文字。</summary>
    private static string FormatSize(long bytes)
    {
        double value = bytes;
        if (value < 1024)
        {
            return value + " B";
        }
        value = value / 1024;
        if (value < 1024)
        {
            return value.ToString("F1") + " KB";
        }
        value = value / 1024;
        if (value < 1024)
        {
            return value.ToString("F1") + " MB";
        }
        value = value / 1024;
        return value.ToString("F2") + " GB";
    }
}
