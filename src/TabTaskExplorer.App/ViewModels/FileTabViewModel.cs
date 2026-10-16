using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 文件视图模式：详细信息、大图标、中图标。
/// </summary>
public enum FileViewMode
{
    Details = 0,
    LargeIcons = 1,
    MediumIcons = 2,
}

/// <summary>
/// 文件标签页：一个标签对应一个独立的目录浏览上下文（当前路径、历史、排序、搜索、视图模式）。
/// 路径为空字符串时表示"此电脑"根视图，展示全部磁盘分区。
/// 目录枚举在后台线程执行，避免大文件夹冻结界面。
/// </summary>
public class FileTabViewModel : INotifyPropertyChanged
{
    private readonly IFileSystemProvider fileSystemProvider;
    private readonly AppSettings appSettings;

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

    // 后台加载的状态：分代令牌用于丢弃过期的加载结果
    private int loadGeneration;
    private string fetchPath = string.Empty;
    private int fetchGeneration = -1;
    private List<FileRow> fetchedRows = new List<FileRow>();
    private int fetchedGeneration = -1;

    public event PropertyChangedEventHandler? PropertyChanged;

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
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileWidth)));
        }
    }

    /// <summary>是否为详细信息布局。</summary>
    public bool IsDetailsLayout
    {
        get { return this.viewMode == FileViewMode.Details; }
    }

    /// <summary>是否为图标布局（大图标或中图标）。</summary>
    public bool IsIconsLayout
    {
        get { return this.viewMode != FileViewMode.Details; }
    }

    /// <summary>图标模式的磁贴宽度。</summary>
    public double TileWidth
    {
        get { return this.viewMode == FileViewMode.LargeIcons ? 150 : 110; }
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

    public FileTabViewModel(IFileSystemProvider fileSystemProvider, AppSettings appSettings, string openPath)
    {
        this.fileSystemProvider = fileSystemProvider;
        this.appSettings = appSettings;
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

    /// <summary>返回上一层目录；已在磁盘根目录时回到此电脑视图。</summary>
    public void NavigateUp()
    {
        if (string.IsNullOrEmpty(this.currentPath))
        {
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

    /// <summary>切换视图模式：详细信息 → 大图标 → 中图标 → 详细信息。</summary>
    public void CycleViewMode()
    {
        if (this.viewMode == FileViewMode.Details)
        {
            this.ViewMode = FileViewMode.LargeIcons;
        }
        else if (this.viewMode == FileViewMode.LargeIcons)
        {
            this.ViewMode = FileViewMode.MediumIcons;
        }
        else
        {
            this.ViewMode = FileViewMode.Details;
        }
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

    /// <summary>打开一个条目：文件夹进入目录，文件用系统默认程序打开。</summary>
    public void OpenEntry(FileRow row)
    {
        if (row.IsDirectory)
        {
            this.OpenPath(row.FullPath);
            return;
        }
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

        this.fetchPath = path;
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
            catch
            {
                // 读取失败时展示空列表，界面线程上给出提示
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
        this.ApplyFilter();

        if (this.allRows.Count == 0 && !string.IsNullOrEmpty(this.currentPath))
        {
            this.SummaryText = "目录为空，或无法读取该目录";
        }
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
}

/// <summary>
/// 文件列表中的一行显示数据。
/// </summary>
public class FileRow
{
    /// <summary>显示名称（受"显示扩展名"设置影响）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>完整路径。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>类型文字：文件夹或扩展名描述。</summary>
    public string TypeText { get; set; } = string.Empty;

    /// <summary>大小显示文字。</summary>
    public string SizeText { get; set; } = string.Empty;

    /// <summary>修改时间显示文字（本地时区）。</summary>
    public string ModifiedText { get; set; } = string.Empty;

    /// <summary>是否被勾选。</summary>
    public bool IsSelected { get; set; }

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
        return row;
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
