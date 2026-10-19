using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>侧边栏中的一个可跳转位置（快捷访问、磁盘或用户文件夹）。</summary>
public class QuickAccessItem
{
    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>跳转目标路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>图标字符（Segoe MDL2）。</summary>
    public string IconGlyph { get; set; } = "\uE8B7";
}

/// <summary>
/// 主窗口视图模型：混合标签页（文件浏览 / 任务看板）、侧边栏、书签栏、剪贴板与设置。
/// </summary>
public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IServiceProvider services;
    private readonly AppSettingsStore settingsStore = new AppSettingsStore();
    private object? selectedTab;
    private bool showToolbar = true;
    private bool showBookmarkBar = true;
    private List<string>? clipboardPaths;
    private bool clipboardIsCut;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>应用设置（所有标签页共享同一份实例）。</summary>
    public AppSettings Settings { get; private set; }

    /// <summary>全部打开的标签页（可能是文件标签或任务看板）。</summary>
    public ObservableCollection<object> Tabs { get; } = new ObservableCollection<object>();

    /// <summary>侧边栏：快捷访问（用户固定）。</summary>
    public ObservableCollection<QuickAccessItem> QuickAccessItems { get; } = new ObservableCollection<QuickAccessItem>();

    /// <summary>侧边栏：此电脑（磁盘与用户文件夹）。</summary>
    public ObservableCollection<QuickAccessItem> ThisPcItems { get; } = new ObservableCollection<QuickAccessItem>();

    private bool hasNoBookmarks = true;

    /// <summary>书签栏是否为空（为空时显示引导提示文案）。</summary>
    public bool HasNoBookmarks
    {
        get { return this.hasNoBookmarks; }
        private set
        {
            this.hasNoBookmarks = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoBookmarks)));
        }
    }

    /// <summary>当前选中的标签页。</summary>
    public object? SelectedTab
    {
        get { return this.selectedTab; }
        set
        {
            this.selectedTab = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTab)));
        }
    }

    /// <summary>当前选中的文件标签页（不是文件标签时为 null）。</summary>
    public FileTabViewModel? SelectedFileTab
    {
        get { return this.selectedTab as FileTabViewModel; }
    }

    /// <summary>是否显示文件面板导航工具栏。</summary>
    public bool ShowToolbar
    {
        get { return this.showToolbar; }
        private set
        {
            this.showToolbar = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowToolbar)));
        }
    }

    /// <summary>是否显示顶部书签栏。</summary>
    public bool ShowBookmarkBar
    {
        get { return this.showBookmarkBar; }
        private set
        {
            this.showBookmarkBar = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowBookmarkBar)));
        }
    }

    public MainWindowViewModel(IServiceProvider services)
    {
        this.services = services;
        this.Settings = this.settingsStore.Load();
        this.FillThisPcItems();
        this.FillQuickAccessItems();
        this.SyncViewSettings();
        this.ApplySavedTheme();

        // 恢复上次关闭的标签页（需在设置中开启）
        if (this.Settings.RestoreLastTabs && this.Settings.OpenTabPaths.Count > 0)
        {
            foreach (string path in this.Settings.OpenTabPaths)
            {
                this.AddTab(path);
            }
        }
        else
        {
            this.AddTab(string.Empty);
        }
    }

    /// <summary>新建文件标签页并打开指定路径。</summary>
    public void AddTab(string path)
    {
        FileTabViewModel tab = new FileTabViewModel(
            this.services.GetRequiredService<IFileSystemProvider>(),
            this.Settings,
            path);
        this.Tabs.Add(tab);
        this.SelectedTab = tab;
    }

    /// <summary>打开任务看板标签页（已存在时直接选中）。</summary>
    public void OpenTaskBoardTab()
    {
        foreach (object tab in this.Tabs)
        {
            if (tab is TaskBoardViewModel board)
            {
                this.SelectedTab = board;
                return;
            }
        }
        TaskBoardViewModel newBoard = new TaskBoardViewModel(this.services.GetRequiredService<ITaskStore>());
        this.Tabs.Add(newBoard);
        this.SelectedTab = newBoard;
    }

    /// <summary>打开连接管理标签页（已存在时直接选中）。</summary>
    public void OpenConnectionsTab()
    {
        foreach (object tab in this.Tabs)
        {
            if (tab is ConnectionManagerViewModel connections)
            {
                this.SelectedTab = connections;
                return;
            }
        }
        ConnectionManagerViewModel newManager = new ConnectionManagerViewModel(
            this.services.GetRequiredService<Kernel.Services.ConnectionManager>());
        this.Tabs.Add(newManager);
        this.SelectedTab = newManager;
    }

    /// <summary>关闭标签页并选中相邻标签。</summary>
    public void CloseTab(object tab)
    {
        int index = this.Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }
        this.Tabs.Remove(tab);
        if (this.Tabs.Count == 0)
        {
            this.SelectedTab = null;
            return;
        }
        if (index >= this.Tabs.Count)
        {
            index = this.Tabs.Count - 1;
        }
        this.SelectedTab = this.Tabs[index];
    }

    /// <summary>按方向切换标签：1 向后，-1 向前。</summary>
    public void CycleTab(int direction)
    {
        if (this.Tabs.Count == 0 || this.SelectedTab == null)
        {
            return;
        }
        int index = this.Tabs.IndexOf(this.SelectedTab) + direction;
        if (index < 0)
        {
            index = this.Tabs.Count - 1;
        }
        if (index >= this.Tabs.Count)
        {
            index = 0;
        }
        this.SelectedTab = this.Tabs[index];
    }

    /// <summary>关闭全部文件标签页（任务看板保留）。</summary>
    public void CloseAllTabs()
    {
        List<object> snapshot = new List<object>(this.Tabs);
        foreach (object tab in snapshot)
        {
            if (tab is FileTabViewModel)
            {
                this.CloseTab(tab);
            }
        }
        if (this.Tabs.Count == 0)
        {
            this.SelectedTab = null;
        }
    }

    /// <summary>把当前文件标签页路径固定到快捷访问。</summary>
    public void PinCurrentTab()
    {
        string? path = this.SelectedFileTab?.CurrentPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        foreach (string pinned in this.Settings.PinnedPaths)
        {
            if (pinned == path)
            {
                return;
            }
        }
        this.Settings.PinnedPaths.Add(path);
        this.settingsStore.Save(this.Settings);
        this.FillQuickAccessItems();
    }

    /// <summary>从快捷访问取消固定。</summary>
    public void Unpin(string path)
    {
        this.Settings.PinnedPaths.Remove(path);
        this.settingsStore.Save(this.Settings);
        this.FillQuickAccessItems();
    }

    /// <summary>把勾选的条目放入剪贴板（复制模式）。</summary>
    public void CopyCheckedToClipboard()
    {
        this.SetClipboard(false);
    }

    /// <summary>把勾选的条目放入剪贴板（剪切模式）。</summary>
    public void CutCheckedToClipboard()
    {
        this.SetClipboard(true);
    }

    /// <summary>把指定路径列表放入剪贴板（复制模式，供右键菜单使用）。</summary>
    public void CopyPathsToClipboard(IReadOnlyList<string> paths)
    {
        this.SetClipboardPaths(paths, false);
    }

    /// <summary>把指定路径列表放入剪贴板（剪切模式，供右键菜单使用）。</summary>
    public void CutPathsToClipboard(IReadOnlyList<string> paths)
    {
        this.SetClipboardPaths(paths, true);
    }

    /// <summary>写入内部剪贴板状态。</summary>
    private void SetClipboardPaths(IReadOnlyList<string> paths, bool isCut)
    {
        if (paths.Count == 0)
        {
            return;
        }
        this.clipboardPaths = new List<string>(paths);
        this.clipboardIsCut = isCut;
        FileTabViewModel? tab = this.SelectedFileTab;
        if (tab != null)
        {
            tab.ShowMessage((isCut ? "已剪切 " : "已复制 ") + paths.Count + " 项，按 Ctrl+V 粘贴到目标文件夹");
        }
    }

    /// <summary>把剪贴板中的条目复制或移动到当前目录。</summary>
    public void PasteFromClipboard()
    {
        FileTabViewModel? tab = this.SelectedFileTab;
        if (tab == null)
        {
            return;
        }
        if (this.clipboardPaths == null || this.clipboardPaths.Count == 0)
        {
            tab.ShowMessage("剪贴板为空，请先复制或剪切");
            return;
        }
        if (string.IsNullOrEmpty(tab.CurrentPath))
        {
            tab.ShowMessage("请先进入目标文件夹");
            return;
        }

        IFileSystemProvider fileSystem = this.services.GetRequiredService<IFileSystemProvider>();
        int successCount = 0;
        int failedCount = 0;
        foreach (string path in this.clipboardPaths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            try
            {
                if (this.clipboardIsCut)
                {
                    fileSystem.Move(path, tab.CurrentPath);
                }
                else
                {
                    fileSystem.Copy(path, tab.CurrentPath);
                }
                successCount++;
            }
            catch
            {
                failedCount++;
            }
        }

        if (this.clipboardIsCut)
        {
            this.clipboardPaths = null;
        }
        tab.ShowMessage("粘贴完成：成功 " + successCount + " 项，失败 " + failedCount + " 项");
        tab.Refresh();
    }

    /// <summary>保存设置并让全部文件标签页按新设置刷新。</summary>
    public void SaveSettingsAndApply()
    {
        this.settingsStore.Save(this.Settings);
        this.SyncViewSettings();
        foreach (object tab in this.Tabs)
        {
            if (tab is FileTabViewModel fileTab)
            {
                fileTab.Refresh();
            }
        }
    }

    /// <summary>保存当前打开的标签页路径（窗口关闭时调用）。</summary>
    public void SaveOpenTabs()
    {
        this.Settings.OpenTabPaths.Clear();
        foreach (object tab in this.Tabs)
        {
            if (tab is FileTabViewModel fileTab)
            {
                this.Settings.OpenTabPaths.Add(fileTab.CurrentPath);
            }
        }
        this.settingsStore.Save(this.Settings);
    }

    /// <summary>创建进程监控面板（供独立窗口使用）。</summary>
    public ProcessPanelViewModel CreateProcessPanel()
    {
        IProcessProvider processProvider = this.services.GetRequiredService<IProcessProvider>();
        ISystemMetricsProvider metricsProvider = this.services.GetRequiredService<ISystemMetricsProvider>();
        return new ProcessPanelViewModel(processProvider, metricsProvider);
    }

    /// <summary>启动浏览器调试镜像服务：页面与程序界面实时同构，便于调试。</summary>
    public void StartDebugWebHost(DebugWebHost host)
    {
        host.Start(
            delegate { return this.DispatchDebugSnapshotAsync(); },
            delegate(int index) { this.SelectDebugTabOnUiThread(index); },
            delegate(string theme) { this.SetThemeOnUiThread(theme); });
    }

    /// <summary>按保存的设置应用主题（程序启动时调用）。</summary>
    public void ApplySavedTheme()
    {
        this.ApplyThemeValue(this.Settings.Theme);
    }

    /// <summary>切换深浅色主题并持久化（供标题栏按钮调用）。</summary>
    public void ToggleTheme()
    {
        this.SetThemeOnUiThread(this.Settings.Theme == "Light" ? "Dark" : "Light");
    }

    /// <summary>浏览器镜像设置主题：跨线程安全。</summary>
    public void SetThemeOnUiThread(string theme)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            this.ApplyThemeValue(theme);
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(delegate { this.ApplyThemeValue(theme); });
    }

    /// <summary>应用主题值并持久化（必须在 UI 线程）。</summary>
    private void ApplyThemeValue(string theme)
    {
        this.Settings.Theme = theme == "Light" ? "Light" : "Dark";
        Avalonia.Application.Current!.RequestedThemeVariant = this.Settings.Theme == "Light"
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
        this.settingsStore.Save(this.Settings);
    }

    /// <summary>构建界面状态快照（必须在 UI 线程调用）。</summary>
    public DebugUiState BuildDebugState()
    {
        DebugUiState state = new DebugUiState();
        state.GeneratedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        state.Theme = this.Settings.Theme;
        int index = 0;
        foreach (object tab in this.Tabs)
        {
            DebugTabState tabState = new DebugTabState();
            tabState.Index = index;
            tabState.Selected = object.ReferenceEquals(tab, this.SelectedTab);
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            TaskBoardViewModel? board = tab as TaskBoardViewModel;
            ConnectionManagerViewModel? connections = tab as ConnectionManagerViewModel;
            if (fileTab != null)
            {
                tabState.TabType = "file";
                tabState.Title = fileTab.Title;
                tabState.CurrentPath = fileTab.CurrentPath;
                foreach (FileRow row in fileTab.VisibleRows)
                {
                    DebugFileRow mirror = new DebugFileRow();
                    mirror.Name = row.DisplayName;
                    mirror.Type = row.TypeText;
                    mirror.Size = row.SizeText;
                    mirror.Modified = row.ModifiedText;
                    tabState.Rows.Add(mirror);
                }
            }
            else if (board != null)
            {
                tabState.TabType = "board";
                tabState.Title = board.Title;
                foreach (TaskItem item in board.TodoItems)
                {
                    tabState.Todo.Add(item.Title);
                }
                foreach (TaskItem item in board.InProgressItems)
                {
                    tabState.InProgress.Add(item.Title);
                }
                foreach (TaskItem item in board.DoneItems)
                {
                    tabState.Done.Add(item.Title);
                }
            }
            else if (connections != null)
            {
                tabState.TabType = "connections";
                tabState.Title = connections.Title;
                foreach (RemoteConnectionProfile profile in connections.Connections)
                {
                    tabState.Connections.Add(new string[] { profile.Name, profile.Summary });
                }
                tabState.SelectedConnection = connections.SelectedConnection != null ? connections.SelectedConnection.Name : string.Empty;
            }
            else
            {
                tabState.TabType = "unknown";
                tabState.Title = "标签页";
            }
            state.Tabs.Add(tabState);
            index++;
        }

        // 侧边栏导航镜像（与桌面导航栏同构，含选中高亮）
        QuickAccessItem? navMatch = this.MatchNavByPath(this.SelectedFileTab?.CurrentPath);
        foreach (QuickAccessItem item in this.QuickAccessItems)
        {
            state.NavPinned.Add(new DebugNavItem
            {
                Name = item.Name,
                Path = item.Path,
                Selected = object.ReferenceEquals(item, navMatch),
            });
        }
        foreach (QuickAccessItem item in this.ThisPcItems)
        {
            state.NavPc.Add(new DebugNavItem
            {
                Name = item.Name,
                Path = item.Path,
                Selected = object.ReferenceEquals(item, navMatch),
            });
        }
        return state;
    }

    /// <summary>浏览器镜像请求快照：已在 UI 线程则直接构建，否则调度到 UI 线程。</summary>
    private Task<DebugUiState> DispatchDebugSnapshotAsync()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(this.BuildDebugState());
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(new Func<DebugUiState>(this.BuildDebugState)).GetTask();
    }

    /// <summary>浏览器镜像切换标签：调度到 UI 线程执行。</summary>
    private void SelectDebugTabOnUiThread(int index)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            this.SelectDebugTab(index);
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(delegate { this.SelectDebugTab(index); });
    }

    /// <summary>选中指定下标的标签页（供浏览器镜像回传）。</summary>
    public void SelectDebugTab(int index)
    {
        if (index >= 0 && index < this.Tabs.Count)
        {
            this.SelectedTab = this.Tabs[index];
        }
    }

    /// <summary>把勾选条目路径放入内部剪贴板。</summary>
    private void SetClipboard(bool isCut)
    {
        FileTabViewModel? tab = this.SelectedFileTab;
        if (tab == null)
        {
            return;
        }
        List<string> paths = tab.GetCheckedPaths();
        if (paths.Count == 0)
        {
            tab.ShowMessage(isCut ? "请先勾选要剪切的条目" : "请先勾选要复制的条目");
            return;
        }
        this.clipboardPaths = paths;
        this.clipboardIsCut = isCut;
        tab.ShowMessage((isCut ? "已剪切 " : "已复制 ") + paths.Count + " 项，按 Ctrl+V 粘贴到目标文件夹");
    }

    /// <summary>同步界面可见性设置。</summary>
    private void SyncViewSettings()
    {
        this.ShowToolbar = this.Settings.ShowToolbar;
        this.ShowBookmarkBar = this.Settings.ShowBookmarkBar;
    }

    /// <summary>填充此电脑分组：此电脑入口、磁盘分区与常用文件夹。</summary>
    private void FillThisPcItems()
    {
        this.ThisPcItems.Clear();
        this.ThisPcItems.Add(new QuickAccessItem { Name = "此电脑", Path = string.Empty, IconGlyph = "\uE7F8" });

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = "本地磁盘 (" + drive.Name.TrimEnd(Path.DirectorySeparatorChar) + ")", Path = drive.Name, IconGlyph = "\uED3B" });
        }

        this.AddUserFolder("桌面", Environment.SpecialFolder.Desktop, "\uE7F8");
        this.AddUserFolder("我的文档", Environment.SpecialFolder.MyDocuments, "\uE8A5");
        this.AddUserFolder("图片", Environment.SpecialFolder.MyPictures, "\uEB9F");
        this.AddUserFolder("音乐", Environment.SpecialFolder.MyMusic, "\uEC4F");
        this.AddUserFolder("视频", Environment.SpecialFolder.MyVideos, "\uE714");

        string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloadsPath))
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = "下载", Path = downloadsPath, IconGlyph = "\uE896" });
        }
    }

    /// <summary>添加一个系统特殊文件夹。</summary>
    private void AddUserFolder(string name, Environment.SpecialFolder folder, string glyph)
    {
        string path = Environment.GetFolderPath(folder);
        if (!string.IsNullOrEmpty(path))
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = name, Path = path, IconGlyph = glyph });
        }
    }

    /// <summary>按设置重建快捷访问列表。</summary>
    private void FillQuickAccessItems()
    {
        this.QuickAccessItems.Clear();
        foreach (string path in this.Settings.PinnedPaths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            this.QuickAccessItems.Add(new QuickAccessItem { Name = GetDisplayName(path), Path = path, IconGlyph = "\uE8B7" });
        }
        this.HasNoBookmarks = this.QuickAccessItems.Count == 0;
    }

    /// <summary>按当前路径匹配侧边栏导航项：精确或前缀匹配（最长者胜，用于选中高亮联动）。</summary>
    public QuickAccessItem? MatchNavByPath(string? path)
    {
        if (path == null)
        {
            return null;
        }
        QuickAccessItem? best = null;
        int bestLength = -1;
        foreach (QuickAccessItem item in this.ThisPcItems)
        {
            if (MatchesNavPath(item.Path, path) && item.Path.Length > bestLength)
            {
                best = item;
                bestLength = item.Path.Length;
            }
        }
        foreach (QuickAccessItem item in this.QuickAccessItems)
        {
            if (MatchesNavPath(item.Path, path) && item.Path.Length > bestLength)
            {
                best = item;
                bestLength = item.Path.Length;
            }
        }
        return best;
    }

    /// <summary>判断导航项路径是否命中当前路径。</summary>
    private static bool MatchesNavPath(string navPath, string currentPath)
    {
        if (navPath == string.Empty)
        {
            return currentPath == string.Empty;
        }
        if (string.Equals(currentPath, navPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (currentPath.StartsWith(navPath, StringComparison.OrdinalIgnoreCase))
        {
            if (navPath.EndsWith("\\", StringComparison.Ordinal))
            {
                return true;
            }
            return currentPath.Length > navPath.Length && currentPath[navPath.Length] == '\\';
        }
        return false;
    }

    /// <summary>取路径的末级名称用于显示。</summary>
    private static string GetDisplayName(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name))
        {
            return path;
        }
        return name;
    }
}
