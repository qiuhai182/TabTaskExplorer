using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>文件视图模式：详细信息、大图标、中图标。</summary>
public enum FileViewMode
{
    Details = 0,
    LargeIcons = 1,
    MediumIcons = 2,
}

/// <summary>侧边栏中的一个可跳转位置（快捷访问、磁盘或用户文件夹）。</summary>
public class QuickAccessItem
{
    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>跳转目标路径。</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>主窗口视图模型：动态标签页、侧边栏、书签栏与设置。</summary>
public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IServiceProvider services;
    private readonly AppSettingsStore settingsStore = new AppSettingsStore();
    private FileTabViewModel? selectedTab;
    private bool showToolbar = true;
    private bool showBookmarkBar = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>应用设置（所有标签页共享同一份实例）。</summary>
    public AppSettings Settings { get; private set; }

    /// <summary>全部打开的标签页。</summary>
    public ObservableCollection<FileTabViewModel> Tabs { get; } = new ObservableCollection<FileTabViewModel>();

    /// <summary>侧边栏：快捷访问（用户固定）。</summary>
    public ObservableCollection<QuickAccessItem> QuickAccessItems { get; } = new ObservableCollection<QuickAccessItem>();

    /// <summary>侧边栏：此电脑（磁盘与常用文件夹）。</summary>
    public ObservableCollection<QuickAccessItem> ThisPcItems { get; } = new ObservableCollection<QuickAccessItem>();

    /// <summary>当前选中的标签页。</summary>
    public FileTabViewModel? SelectedTab
    {
        get { return this.selectedTab; }
        set
        {
            this.selectedTab = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTab)));
        }
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

    /// <summary>新建标签页并打开指定路径。</summary>
    public void AddTab(string path)
    {
        FileTabViewModel tab = new FileTabViewModel(
            this.services.GetRequiredService<IFileSystemProvider>(),
            this.Settings,
            path);
        this.Tabs.Add(tab);
        this.SelectedTab = tab;
    }

    /// <summary>关闭标签页并选中相邻标签。</summary>
    public void CloseTab(FileTabViewModel tab)
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

    /// <summary>把当前标签页路径固定到快捷访问。</summary>
    public void PinCurrentTab()
    {
        string? path = this.SelectedTab?.CurrentPath;
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

    /// <summary>保存设置并让全部标签页按新设置刷新。</summary>
    public void SaveSettingsAndApply()
    {
        this.settingsStore.Save(this.Settings);
        this.SyncViewSettings();
        foreach (FileTabViewModel tab in this.Tabs)
        {
            tab.Refresh();
        }
    }

    /// <summary>保存当前打开的标签页路径（窗口关闭时调用）。</summary>
    public void SaveOpenTabs()
    {
        this.Settings.OpenTabPaths.Clear();
        foreach (FileTabViewModel tab in this.Tabs)
        {
            this.Settings.OpenTabPaths.Add(tab.CurrentPath);
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
