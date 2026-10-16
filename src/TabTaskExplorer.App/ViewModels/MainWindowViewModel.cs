using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>侧边栏中的一个可跳转位置（快捷访问、磁盘或用户文件夹）。</summary>
public class QuickAccessItem
{
    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>跳转目标路径。</summary>
    public string Path { get; set; } = string.Empty;
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

    /// <summary>侧边栏：此电脑（磁盘与常用文件夹）。</summary>
    public ObservableCollection<QuickAccessItem> ThisPcItems { get; } = new ObservableCollection<QuickAccessItem>();

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
        this.ThisPcItems.Add(new QuickAccessItem { Name = "此电脑", Path = string.Empty });

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = "本地磁盘 (" + drive.Name.TrimEnd(Path.DirectorySeparatorChar) + ")", Path = drive.Name });
        }

        this.AddUserFolder("桌面", Environment.SpecialFolder.Desktop);
        this.AddUserFolder("我的文档", Environment.SpecialFolder.MyDocuments);
        this.AddUserFolder("图片", Environment.SpecialFolder.MyPictures);
        this.AddUserFolder("音乐", Environment.SpecialFolder.MyMusic);
        this.AddUserFolder("视频", Environment.SpecialFolder.MyVideos);

        string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloadsPath))
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = "下载", Path = downloadsPath });
        }
    }

    /// <summary>添加一个系统特殊文件夹。</summary>
    private void AddUserFolder(string name, Environment.SpecialFolder folder)
    {
        string path = Environment.GetFolderPath(folder);
        if (!string.IsNullOrEmpty(path))
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = name, Path = path });
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
            this.QuickAccessItems.Add(new QuickAccessItem { Name = GetDisplayName(path), Path = path });
        }
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
