using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Controls;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Capture;
using TabTaskExplorer.App.Services.Tasks;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Media;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>侧边栏中的一个可跳转位置（快捷访问、磁盘或用户文件夹）。</summary>
public class QuickAccessItem
{
    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>跳转目标路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>图标分类键（见 <see cref="FileIconCatalog"/>）。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.Folder;

    /// <summary>图标强调色：跟随分类自动取色。</summary>
    public string IconColor
    {
        get { return FileIconCatalog.GetAccent(this.IconCategory); }
    }
}

/// <summary>
/// 主窗口视图模型：混合标签页（文件浏览 / 任务看板）、侧边栏、书签栏、剪贴板与设置。
/// </summary>
public class MainWindowViewModel : INotifyPropertyChanged, IWebWorkspaceBridge
{
    private readonly IServiceProvider services;
    private readonly AppSettingsStore settingsStore = new AppSettingsStore();

    /// <summary>桌面统一任务中心（本地复制/移动/删除）。</summary>
    private readonly FileTaskCenter fileTaskCenter = new FileTaskCenter();
    private readonly CaptureCoordinator captureCoordinator;
    private readonly CustomStartupService customStartupService;
    private object? selectedTab;
    private bool showToolbar = true;
    private bool showBookmarkBar = true;
    private bool showTopWhiteStrip = false;
    private bool isIdeMode;
    private List<string>? clipboardPaths;
    private bool clipboardIsCut;
    private bool canPasteFromClipboard;
    private bool clipboardProbeRunning;
    private int clipboardProbeTickCount;
    private Avalonia.Threading.DispatcherTimer? clipboardAvailabilityTimer;
    private string clipboardAvailabilitySignature = string.Empty;
    private bool taskRefreshSubscribed;
    private WebServiceHost? debugWebHost;
    private bool webServiceRunning;
    private int webServicePort;
    private string webServiceStatus = "网页端服务：未启动";
    private readonly Dictionary<string, WebWorkspace> webWorkspaces = new Dictionary<string, WebWorkspace>();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>应用设置（所有标签页共享同一份实例）。</summary>
    public AppSettings Settings { get; private set; }

    /// <summary>工具栏「粘贴」是否可用：只有剪贴板含文件 / 文件夹时启用，纯文本或图片为 false。</summary>
    public bool CanPasteFromClipboard
    {
        get { return this.canPasteFromClipboard; }
        private set
        {
            if (this.canPasteFromClipboard == value)
            {
                return;
            }
            this.canPasteFromClipboard = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPasteFromClipboard)));
        }
    }

    /// <summary>自定义启动项服务：本次开机首次启动检查与启动项管理标签共用同一实例。</summary>
    public CustomStartupService CustomStartup
    {
        get { return this.customStartupService; }
    }

    /// <summary>网页端服务的状态文字：真实监听地址，或未启动原因（状态栏展示）。</summary>
    public string WebServiceStatus
    {
        get { return this.webServiceStatus; }
    }

    /// <summary>IDE 模式外壳视图模型（资源树 + 编辑器标签 + 查找面板）。</summary>
    public IdeShellViewModel IdeShell { get; private set; }

    /// <summary>当前是否处于 IDE 模式。</summary>
    public bool IsIdeMode
    {
        get { return this.isIdeMode; }
        private set
        {
            this.isIdeMode = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.IsIdeMode)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExplorerMode)));
        }
    }

    /// <summary>当前是否处于资源管理器模式（与 IDE 模式互斥）。</summary>
    public bool IsExplorerMode
    {
        get { return !this.isIdeMode; }
    }

    /// <summary>全部打开的标签页（可能是文件标签或任务看板）。</summary>
    public ObservableCollection<object> Tabs { get; } = new ObservableCollection<object>();

    /// <summary>主窗口引用：供错误提示弹窗作为所有者使用。</summary>
    public Window? OwnerWindow { get; set; }

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
            object? previous = this.selectedTab;
            this.selectedTab = value;
            if (!object.ReferenceEquals(previous, value))
            {
                // 磁盘视图只有在台前才扫描：切走立即停止，切入立即刷新为最新状态
                if (previous is DiskViewPanelViewModel previousDiskView)
                {
                    previousDiskView.SetForeground(false);
                }
                if (value is DiskViewPanelViewModel newDiskView)
                {
                    newDiskView.SetForeground(true);
                }
                // 进程监控同理：只有台前标签才周期刷新
                if (previous is ProcessPanelViewModel previousProcess)
                {
                    previousProcess.SetForeground(false);
                }
                if (value is ProcessPanelViewModel newProcess)
                {
                    newProcess.SetForeground(true);
                }
                // 截图录屏标签切入台前时刷新产物历史与当前录制状态
                if (value is CaptureTabViewModel newCaptureTab)
                {
                    newCaptureTab.OnActivated();
                }
                // 启动项管理标签切入台前时重新读取系统启动项与本次开机执行状态
                if (value is StartupManagerViewModel newStartupTab)
                {
                    newStartupTab.OnActivated();
                }
            }
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

    /// <summary>是否在窗口顶部显示白色边框条（关闭时窗口由框架绘制为无边框外观）。</summary>
    public bool ShowTopWhiteStrip
    {
        get { return this.showTopWhiteStrip; }
        private set
        {
            this.showTopWhiteStrip = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTopWhiteStrip)));
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

    /// <summary>
    /// 系统右键菜单服务：供设置页与文件列表枚举、启停、唤起系统真实右键菜单项；
    /// 服务容器未注册时回退为空实现（非 Windows 平台）。
    /// </summary>
    public ISystemContextMenuService SystemContextMenus
    {
        get
        {
            return this.services.GetService(typeof(ISystemContextMenuService)) as ISystemContextMenuService
                ?? new NullSystemContextMenuService();
        }
    }

    public MainWindowViewModel(IServiceProvider services)
    {
        this.services = services;
        this.Settings = this.settingsStore.Load();
        ThumbnailService.Configure(this.Settings.FfmpegPath);
        UiLogger.SetMinimumLevel(UiLogger.ParseLevel(this.Settings.LogLevel, LogLevel.Info));
        this.captureCoordinator = services.GetRequiredService<CaptureCoordinator>();
        this.customStartupService = new CustomStartupService(this.Settings, this.settingsStore);
        this.IdeShell = new IdeShellViewModel(this.Settings, services);
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

        // 按「默认打开模式」决定启动时进入 IDE 模式还是资源管理器模式
        if (string.Equals(this.Settings.DefaultOpenMode, "Ide", StringComparison.OrdinalIgnoreCase))
        {
            this.EnterIdeMode();
        }
        this.StartClipboardAvailabilityWatcher();
    }

    /// <summary>
    /// 拖出标签创建的新窗口所用构造：与主窗口共享同一份设置对象（避免多份配置互相覆盖保存），
    /// 不恢复会话标签（标签由拖出操作移入），固定以资源管理器模式打开。
    /// </summary>
    public MainWindowViewModel(IServiceProvider services, AppSettings sharedSettings)
    {
        this.services = services;
        this.Settings = sharedSettings;
        ThumbnailService.Configure(this.Settings.FfmpegPath);
        UiLogger.SetMinimumLevel(UiLogger.ParseLevel(this.Settings.LogLevel, LogLevel.Info));
        this.captureCoordinator = services.GetRequiredService<CaptureCoordinator>();
        this.customStartupService = new CustomStartupService(this.Settings, this.settingsStore);
        this.IdeShell = new IdeShellViewModel(this.Settings, services);
        this.FillThisPcItems();
        this.FillQuickAccessItems();
        this.SyncViewSettings();
        this.ApplySavedTheme();
        this.StartClipboardAvailabilityWatcher();
        UiLogger.Log("程序行为", "新窗口视图模型创建：共享设置对象、不恢复会话标签、资源管理器模式");
    }

    /// <summary>两种模式一键切换。</summary>
    public void ToggleIdeMode()
    {
        if (this.IsIdeMode)
        {
            this.ExitIdeMode();
        }
        else
        {
            this.EnterIdeMode();
        }
    }

    /// <summary>
    /// 进入 IDE 模式：以当前文件标签所在目录（本机或远程）作为工作区，保留资源管理器标签会话。
    /// </summary>
    public void EnterIdeMode()
    {
        this.IsIdeMode = true;
        string root = this.ResolveIdeWorkspaceRoot(out IFileSystemProvider provider, out IFileContentAccess? contentAccess, out string connectionName);
        this.IdeShell.SetWorkspace(root, provider, contentAccess, connectionName);
        UiLogger.Log("程序行为", "切换到 IDE 模式：工作区=" + (string.IsNullOrEmpty(root) ? "无" : root) + " 连接=" + (string.IsNullOrEmpty(connectionName) ? "本机" : connectionName));
        if (!string.IsNullOrEmpty(root))
        {
            this.Settings.IdeWorkspacePath = contentAccess == null ? root : string.Empty;
            this.settingsStore.Save(this.Settings);
        }
    }

    /// <summary>退出 IDE 模式，回到资源管理器模式（标签会话保持不变）。</summary>
    public void ExitIdeMode()
    {
        this.IsIdeMode = false;
        UiLogger.Log("程序行为", "退出 IDE 模式：回到资源管理器模式");
    }

    /// <summary>
    /// 计算 IDE 工作区根路径与对应提供者：优先当前文件标签目录，其次最近使用的 IDE 工作区，最后用户目录。
    /// </summary>
    private string ResolveIdeWorkspaceRoot(out IFileSystemProvider provider, out IFileContentAccess? contentAccess, out string connectionName)
    {
        FileTabViewModel? tab = this.SelectedFileTab;
        if (tab != null && !string.IsNullOrEmpty(tab.CurrentPath))
        {
            provider = tab.FileSystemProvider;
            contentAccess = tab.ContentAccess;
            connectionName = this.ResolveConnectionName(tab.AttachedProfileId);
            return tab.CurrentPath;
        }

        provider = this.services.GetRequiredService<IFileSystemProvider>();
        contentAccess = null;
        connectionName = string.Empty;
        if (!string.IsNullOrEmpty(this.Settings.IdeWorkspacePath) && Directory.Exists(this.Settings.IdeWorkspacePath))
        {
            return this.Settings.IdeWorkspacePath;
        }
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fallback;
    }

    /// <summary>按连接编号解析连接名称（IDE 工作区标题与远程编辑显示用）。</summary>
    private string ResolveConnectionName(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return string.Empty;
        }
        RemoteConnectionProfile? profile = this.services.GetRequiredService<Kernel.Services.ConnectionManager>().FindById(profileId);
        if (profile == null)
        {
            return "远程";
        }
        return profile.Name;
    }

    /// <summary>新建文件标签页并打开指定路径（路径为压缩包时自动开预览标签）。</summary>
    public void AddTab(string path)
    {
        IArchiveService archiveService = this.services.GetRequiredService<IArchiveService>();
        IFileSystemProvider provider;
        DiskScanService? scanService;
        if (ArchiveFileSystemProvider.TryCreate(archiveService, path, out ArchiveFileSystemProvider? archiveProvider))
        {
            // 压缩包：用压缩包虚拟文件系统浏览，虚拟路径不做本机目录大小回填
            provider = archiveProvider!;
            scanService = null;
        }
        else
        {
            provider = this.services.GetRequiredService<IFileSystemProvider>();
            scanService = this.services.GetRequiredService<DiskScanService>();
        }
        this.CreateFileTab(provider, path, scanService, archiveService);
    }

    /// <summary>压缩包条目打开请求：在本标签所属的压缩包文件系统上新开预览子标签。</summary>
    private void OnArchiveOpenRequested(object? sender, string path)
    {
        ArchiveFileSystemProvider? owner = sender is FileTabViewModel sourceTab
            ? sourceTab.FileSystemProvider as ArchiveFileSystemProvider
            : null;
        if (owner != null)
        {
            this.CreateFileTab(owner, path, null, this.services.GetRequiredService<IArchiveService>());
            return;
        }
        this.AddTab(path);
    }

    /// <summary>媒体文件打开请求（来自文件标签双击）：按设置开内置标签、弹窗询问或交系统程序。</summary>
    private async void OnMediaOpenRequested(object? sender, string path)
    {
        await this.OpenMediaAsync(path);
    }

    /// <summary>按「媒体打开方式」设置打开一个媒体文件。</summary>
    public async Task OpenMediaAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        MediaKind kind = MediaFormatCatalog.Classify(path);
        if (kind == MediaKind.Unknown)
        {
            return;
        }
        string mode = MediaOpenPolicy.ResolveMode(this.Settings.MediaOpenRules, this.Settings.MediaOpenMode, kind);
        if (mode == MediaOpenModeValues.Ask)
        {
            bool useBuiltin = await Views.MessageDialog.ConfirmAsync(this.OwnerWindow, "打开方式",
                "「" + Path.GetFileName(path) + "」\n\n用内置媒体查看器打开吗？\n选择「系统默认程序」将交给系统关联的应用。",
                "内置查看器", "系统默认程序");
            if (!useBuiltin)
            {
                this.OpenWithSystemProgram(path);
                return;
            }
        }
        else if (mode == MediaOpenModeValues.System)
        {
            this.OpenWithSystemProgram(path);
            return;
        }
        this.OpenMediaTab(path, kind);
    }

    /// <summary>新开（或复用）媒体标签：同一文件已打开时按设置激活已有标签。</summary>
    public void OpenMediaTab(string path, MediaKind kind)
    {
        path = MediaTabViewModel.NormalizePath(path);
        if (this.Settings.MediaReuseTab)
        {
            foreach (object tab in this.Tabs)
            {
                MediaTabViewModel? existing = tab as MediaTabViewModel;
                if (existing != null && string.Equals(existing.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    this.SelectedTab = existing;
                    UiLogger.Log("媒体", "复用已打开的媒体标签：路径=" + path);
                    return;
                }
            }
        }
        MediaTabViewModel mediaTab = new MediaTabViewModel(this.Settings, this.CreateMediaEngine, path, kind);
        mediaTab.CloseRequested += this.OnMediaTabCloseRequested;
        this.Tabs.Add(mediaTab);
        this.SelectedTab = mediaTab;
        _ = mediaTab.LoadAsync();
        UiLogger.Log("媒体", "新建媒体标签：类型=" + MediaFormatCatalog.DescribeKind(kind) + " 路径=" + path
            + " 标签总数=" + this.Tabs.Count);
    }

    /// <summary>创建播放引擎实例（每个媒体标签一个，由容器按需构造）。</summary>
    private IMediaPlaybackEngine? CreateMediaEngine()
    {
        return this.services.GetService<IMediaPlaybackEngine>();
    }

    /// <summary>媒体标签请求关闭自身（例如提示中的「关闭标签」）。</summary>
    private void OnMediaTabCloseRequested(object? sender, EventArgs e)
    {
        MediaTabViewModel? mediaTab = sender as MediaTabViewModel;
        if (mediaTab != null)
        {
            this.CloseTab(mediaTab);
        }
    }

    /// <summary>用系统默认程序打开文件（媒体打开方式设为「系统」或用户在询问中选择了系统程序时使用）。</summary>
    private void OpenWithSystemProgram(string path)
    {
        try
        {
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.FileName = path;
            startInfo.UseShellExecute = true;
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception error)
        {
            Views.MessageDialog.Show(this.OwnerWindow, "打开失败", "无法用系统默认程序打开：" + error.Message);
        }
    }

    /// <summary>按给定提供者创建文件标签并加入标签条。</summary>
    private void CreateFileTab(IFileSystemProvider provider, string path,
        DiskScanService? scanService, IArchiveService archiveService)
    {
        FileTabViewModel tab = new FileTabViewModel(provider, this.Settings, path, scanService, archiveService, this.settingsStore);
        tab.PathChanged += this.OnTabPathChanged;
        tab.ArchiveOpenRequested += this.OnArchiveOpenRequested;
        tab.MediaOpenRequested += this.OnMediaOpenRequested;
        tab.RetryTaskRequested = (workspaceId, taskId) => this.RetryFileTask(workspaceId, taskId);
        this.Tabs.Add(tab);
        this.SelectedTab = tab;
        string locationText = string.IsNullOrEmpty(path) ? "此电脑" : path;
        string kindText = provider is ArchiveFileSystemProvider ? "压缩包预览" : "文件";
        UiLogger.Log("程序行为", "新建" + kindText + "标签：路径=" + locationText + " 标签总数=" + this.Tabs.Count);
    }

    /// <summary>最近访问的路径（供地址栏"最近"下拉列表展示）。</summary>
    public ObservableCollection<string> RecentPaths { get; } = new ObservableCollection<string>();

    /// <summary>记录一条最近访问路径：去重后移到最前，最多保留 20 条。</summary>
    private void RecordRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        for (int i = 0; i < this.RecentPaths.Count; i++)
        {
            if (string.Equals(this.RecentPaths[i], path, StringComparison.OrdinalIgnoreCase))
            {
                this.RecentPaths.RemoveAt(i);
                break;
            }
        }
        this.RecentPaths.Insert(0, path);
        while (this.RecentPaths.Count > 20)
        {
            this.RecentPaths.RemoveAt(this.RecentPaths.Count - 1);
        }
    }

    private void OnTabPathChanged(object? sender, string path)
    {
        this.RecordRecent(path);
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
        UiLogger.Log("程序行为", "新建任务看板标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>打开全局搜索标签页（已存在时直接选中）。</summary>
    public void OpenSearchTab()
    {
        foreach (object tab in this.Tabs)
        {
            if (tab is SearchTabViewModel search)
            {
                this.SelectedTab = search;
                return;
            }
        }
        SearchTabViewModel newSearch = new SearchTabViewModel(this.services.GetRequiredService<IFileSearchService>());
        this.Tabs.Add(newSearch);
        this.SelectedTab = newSearch;
        UiLogger.Log("程序行为", "新建全局搜索标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>打开比对标签页：存在目录时进入目录比对，否则进入文件文本比对；同一路径不新建重复标签。</summary>
    public CompareTabViewModel OpenCompareTab(string leftPath, string rightPath)
    {
        foreach (object tab in this.Tabs)
        {
            CompareTabViewModel? existing = tab as CompareTabViewModel;
            if (existing != null
                && string.Equals(existing.LeftPathInput, leftPath ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.RightPathInput, rightPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                this.SelectedTab = existing;
                UiLogger.Log("比对", "复用已打开的比对标签：左侧路径=" + leftPath + " 右侧路径=" + rightPath);
                return existing;
            }
        }
        CompareTabViewModel compareTab = new CompareTabViewModel(this.Settings,
            this.services.GetRequiredService<ITextFileService>(), leftPath ?? string.Empty, rightPath ?? string.Empty);
        compareTab.FileCompareRequested += this.OnCompareFileCompareRequested;
        this.Tabs.Add(compareTab);
        this.SelectedTab = compareTab;
        UiLogger.Log("程序行为", "新建比对标签：左侧路径=" + (leftPath ?? string.Empty)
            + " 右侧路径=" + (rightPath ?? string.Empty) + " 标签总数=" + this.Tabs.Count);
        return compareTab;
    }

    /// <summary>目录比对中双击文件行：新开文件比对标签。</summary>
    private void OnCompareFileCompareRequested(object? sender, ComparePairEventArgs args)
    {
        this.OpenCompareTab(args.LeftPath, args.RightPath);
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
        newManager.Owner = this;
        this.Tabs.Add(newManager);
        this.SelectedTab = newManager;
        UiLogger.Log("程序行为", "新建连接管理标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>打开启动项管理标签页（已存在时直接选中）。</summary>
    public void OpenStartupManagerTab()
    {
        foreach (object tab in this.Tabs)
        {
            if (tab is StartupManagerViewModel startupTab)
            {
                this.SelectedTab = startupTab;
                startupTab.OnActivated();
                UiLogger.Log("启动项", "切换到已打开的启动项管理标签：标签总数=" + this.Tabs.Count);
                return;
            }
        }
        StartupManagerViewModel newStartupTab = new StartupManagerViewModel(
            this.services.GetRequiredService<IStartupItemProvider>(),
            this.Settings, this.settingsStore, this.customStartupService);
        this.Tabs.Add(newStartupTab);
        this.SelectedTab = newStartupTab;
        UiLogger.Log("程序行为", "新建启动项管理标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>
    /// 程序启动时检查自定义启动项：按开机标识只拉起本次开机尚未执行过的条目。
    /// 由 App 在界面线程调用，内部按延迟顺序异步执行，不阻塞窗口显示。
    /// </summary>
    public void RunCustomStartupItemsOnBoot()
    {
        this.customStartupService.RunBootItemsIfNeeded();
    }

    /// <summary>打开进程监控标签页（已存在时直接选中）。</summary>
    public void OpenProcessMonitorTab()
    {
        foreach (object tab in this.Tabs)
        {
            if (tab is ProcessPanelViewModel processPanel)
            {
                this.SelectedTab = processPanel;
                UiLogger.Log("程序行为", "切换到进程监控标签：标签总数=" + this.Tabs.Count);
                return;
            }
        }
        ProcessPanelViewModel newProcessPanel = new ProcessPanelViewModel(
            this.services.GetRequiredService<ProcessSampleService>(),
            this.services.GetRequiredService<IProcessProvider>(),
            this.services.GetRequiredService<IProcessDetailService>());
        this.Tabs.Add(newProcessPanel);
        this.SelectedTab = newProcessPanel;
        UiLogger.Log("程序行为", "新建进程监控标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>打开磁盘视图标签页（已存在时直接选中）。</summary>
    public void OpenDiskViewTab()
    {
        foreach (object tab in this.Tabs)
        {
            if (tab is DiskViewPanelViewModel diskView)
            {
                this.SelectedTab = diskView;
                return;
            }
        }

        DiskViewPanelViewModel newDiskView = new DiskViewPanelViewModel(
            this.services.GetRequiredService<IDiskUsageProvider>(),
            this.services.GetRequiredService<DiskScanService>(),
            this.services.GetRequiredService<IFileSystemProvider>());
        this.Tabs.Add(newDiskView);
        this.SelectedTab = newDiskView;
        UiLogger.Log("程序行为", "新建磁盘视图标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>打开截图与录屏标签页（已存在时直接选中）。</summary>
    public void OpenCaptureTab()
    {
        if (this.IsIdeMode)
        {
            UiLogger.Log("截图录屏", "打开截图录屏标签前退出 IDE 模式，保证标签在资源管理器区域可见");
            this.ExitIdeMode();
        }
        foreach (object tab in this.Tabs)
        {
            if (tab is CaptureTabViewModel captureTab)
            {
                this.SelectedTab = captureTab;
                UiLogger.Log("截图录屏", "切换到已打开的截图录屏标签：标签总数=" + this.Tabs.Count);
                return;
            }
        }
        CaptureTabViewModel newCaptureTab = new CaptureTabViewModel(this.Settings, this.captureCoordinator);
        this.Tabs.Add(newCaptureTab);
        this.SelectedTab = newCaptureTab;
        UiLogger.Log("截图录屏", "新建截图录屏标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>
    /// 打开产物文件夹标签：同一路径的文件标签已存在时直接选中并刷新，
    /// 否则新建文件标签；截图与录屏完成后由协调器调用，满足「自动弹出产物文件夹视图」。
    /// </summary>
    public void OpenFolderTab(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            UiLogger.Log("截图录屏", "打开产物文件夹标签被跳过：路径为空");
            return;
        }
        if (this.IsIdeMode)
        {
            UiLogger.Log("截图录屏", "打开产物文件夹标签前退出 IDE 模式，保证文件夹标签在资源管理器区域可见");
            this.ExitIdeMode();
        }
        string normalizedPath = Path.GetFullPath(path);
        foreach (object tab in this.Tabs)
        {
            FileTabViewModel? existing = tab as FileTabViewModel;
            if (existing != null && string.Equals(existing.CurrentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                this.SelectedTab = existing;
                existing.Refresh();
                UiLogger.Log("截图录屏", "复用已打开的产物文件夹标签：路径=" + normalizedPath
                    + " 标签总数=" + this.Tabs.Count);
                return;
            }
        }
        this.AddTab(normalizedPath);
        UiLogger.Log("截图录屏", "新建产物文件夹标签：路径=" + normalizedPath
            + " 标签总数=" + this.Tabs.Count);
    }

    /// <summary>截图录屏环境摘要（平台能力、FFmpeg、全局快捷键状态）：设置窗口展示用。</summary>
    public string CaptureEnvironmentText
    {
        get { return this.captureCoordinator.DescribeEnvironment(); }
    }

    /// <summary>当前平台是否支持系统全局快捷键（用于把全局配置降级为应用内触发）。</summary>
    public bool IsGlobalHotkeySupported
    {
        get { return this.captureCoordinator.IsGlobalHotkeySupported; }
    }

    /// <summary>执行截图 / 录屏类快捷键动作（应用内快捷键与全局快捷键共用）。</summary>
    public void ExecuteCaptureShortcut(string actionName)
    {
        int delaySeconds = this.Settings.CaptureDelaySeconds;
        if (string.Equals(actionName, "ScreenshotRegion", StringComparison.Ordinal))
        {
            UiLogger.Log("截图录屏", "快捷键触发区域截图：延迟秒数=" + delaySeconds);
            _ = this.captureCoordinator.CaptureRegionAsync(delaySeconds);
            return;
        }
        if (string.Equals(actionName, "ScreenshotFull", StringComparison.Ordinal))
        {
            UiLogger.Log("截图录屏", "快捷键触发全屏截图：延迟秒数=" + delaySeconds);
            _ = this.captureCoordinator.CaptureAsync(CaptureTarget.FullScreen, delaySeconds);
            return;
        }
        if (string.Equals(actionName, "ScreenshotPrimary", StringComparison.Ordinal))
        {
            UiLogger.Log("截图录屏", "快捷键触发主屏截图：延迟秒数=" + delaySeconds);
            _ = this.captureCoordinator.CaptureAsync(CaptureTarget.PrimaryScreen, delaySeconds);
            return;
        }
        if (string.Equals(actionName, "ScreenshotWindow", StringComparison.Ordinal))
        {
            UiLogger.Log("截图录屏", "快捷键触发活动窗口截图：延迟秒数=" + delaySeconds);
            _ = this.captureCoordinator.CaptureAsync(CaptureTarget.ActiveWindow, delaySeconds);
            return;
        }
        if (string.Equals(actionName, "ScreenshotLastRegion", StringComparison.Ordinal))
        {
            UiLogger.Log("截图录屏", "快捷键触发上次区域截图：延迟秒数=" + delaySeconds);
            _ = this.captureCoordinator.CaptureLastRegionAsync(delaySeconds);
            return;
        }
        if (string.Equals(actionName, "ScreenshotPin", StringComparison.Ordinal))
        {
            UiLogger.Log("截图录屏", "快捷键触发贴图截图：延迟秒数=" + delaySeconds
                + " 保存产物=" + this.Settings.CapturePinShotSaveFile);
            _ = this.captureCoordinator.CapturePinRegionAsync(delaySeconds);
            return;
        }
        if (string.Equals(actionName, "ScreenshotScroll", StringComparison.Ordinal))
        {
            UiLogger.Log("截图录屏", "快捷键触发滚动长截图：延迟秒数=" + delaySeconds
                + " 取消方式=右键或 Esc");
            _ = this.captureCoordinator.CaptureScrollRegionAsync(delaySeconds);
            return;
        }
        if (string.Equals(actionName, "RecordToggle", StringComparison.Ordinal))
        {
            int targetIndex = this.captureCoordinator.ResolveRecordingTargetIndex(this.Settings.CaptureRecordTarget);
            UiLogger.Log("截图录屏", "快捷键触发开始或停止录屏：录制目标索引=" + targetIndex);
            _ = this.captureCoordinator.ToggleRecordingAsync(targetIndex);
            return;
        }
        UiLogger.Log("截图录屏", "截图录屏快捷键动作被忽略：无法识别的动作标识=" + actionName);
    }

    /// <summary>按最新设置重新注册全部全局快捷键；设置窗口关闭后与设置内即时停用时调用。</summary>
    public void ReloadGlobalHotkeys()
    {
        this.captureCoordinator.ReloadGlobalHotkeys();
        UiLogger.Log("全局热键", "按最新设置重新注册全局快捷键：状态=" + this.captureCoordinator.HotkeyStatusText);
    }

    /// <summary>只把当前设置写入磁盘，不触发界面整体刷新；用于设置窗口内「一键清空」等需要立即持久化的操作。</summary>
    public void PersistSettings()
    {
        this.settingsStore.Save(this.Settings);
        int shortcutCount = this.Settings.Shortcuts == null ? 0 : this.Settings.Shortcuts.Count;
        UiLogger.Log("配置更改", "即时保存设置：快捷键是否全部停用=" + (this.Settings.ShortcutsDisabled ? "是" : "否")
            + " 快捷键条数=" + shortcutCount + " 结果=成功");
    }

    /// <summary>跟随桌面：网页端请求打开桌面磁盘视图标签。</summary>
    public void OpenDesktopDiskView()
    {
        this.OpenDiskViewTab();
        UiLogger.Log("网页端服务", "跟随桌面打开磁盘视图标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>按标签类型打开视图：网页端与桌面端共用同一套 kind 标识，新增视图只在此登记。</summary>
    public void OpenView(string kind)
    {
        if (kind == TabKinds.File)
        {
            this.AddTab(string.Empty);
            return;
        }
        if (kind == TabKinds.Compare)
        {
            this.OpenCompareTab(string.Empty, string.Empty);
            return;
        }
        if (kind == TabKinds.Board)
        {
            this.OpenTaskBoardTab();
            return;
        }
        if (kind == TabKinds.Search)
        {
            this.OpenSearchTab();
            return;
        }
        if (kind == TabKinds.Connections)
        {
            this.OpenConnectionsTab();
            return;
        }
        if (kind == TabKinds.Startup)
        {
            this.OpenStartupManagerTab();
            return;
        }
        if (kind == TabKinds.Disk)
        {
            this.OpenDiskViewTab();
            return;
        }
        if (kind == TabKinds.Process)
        {
            this.OpenProcessMonitorTab();
            return;
        }
        if (kind == TabKinds.Capture)
        {
            this.OpenCaptureTab();
            return;
        }
        UiLogger.Log("程序行为", "打开视图被忽略：无法识别的标签类型=" + kind + " 结果=未打开任何标签");
    }

    /// <summary>跟随桌面：网页端请求操作桌面磁盘视图；空路径回到根视图，其余路径原地展开或收起。</summary>
    public void NavigateDesktopDisk(string path)
    {
        DiskViewPanelViewModel? diskView = this.SelectedTab as DiskViewPanelViewModel;
        if (diskView == null)
        {
            UiLogger.Log("网页端服务", "跟随桌面磁盘视图操作忽略：当前选中的不是磁盘视图标签 当前标签="
                + (this.SelectedTab == null ? "无" : this.SelectedTab.GetType().Name));
            return;
        }
        string target = path == null ? string.Empty : path.Trim();
        if (target.Length > 0 && !Directory.Exists(target))
        {
            UiLogger.Log("网页端服务", "跟随桌面磁盘视图操作忽略：目标不是目录 目标=" + target);
            return;
        }
        diskView.ToggleRowByPath(target);
        UiLogger.Log("网页端服务", "跟随桌面磁盘视图切换展开：目标=" + (target.Length == 0 ? "根视图" : target));
    }

    /// <summary>跟随桌面：当前标签返回上一级；文件标签与磁盘视图标签各自处理，其它标签忽略。</summary>
    public void NavigateDesktopUp()
    {
        FileTabViewModel? fileTab = this.SelectedTab as FileTabViewModel;
        if (fileTab != null)
        {
            fileTab.NavigateUp();
            UiLogger.Log("网页端服务", "跟随桌面返回上一级：文件标签=" + fileTab.Title + " 新路径=" + fileTab.CurrentPath);
            return;
        }
        DiskViewPanelViewModel? diskView = this.SelectedTab as DiskViewPanelViewModel;
        if (diskView != null)
        {
            diskView.NavigateUp();
            UiLogger.Log("网页端服务", "跟随桌面返回上一级：磁盘视图新根目录="
                + (string.IsNullOrEmpty(diskView.RootPathInput) ? "此电脑" : diskView.RootPathInput));
            return;
        }
        UiLogger.Log("网页端服务", "跟随桌面返回上一级忽略：当前标签不支持上一级导航 当前标签="
            + (this.SelectedTab == null ? "无" : this.SelectedTab.GetType().Name));
    }

    /// <summary>各连接的传输服务缓存：同一连接复用同一个传输队列。</summary>
    private readonly Dictionary<string, IRemoteTransferService> transferServices = new Dictionary<string, IRemoteTransferService>();

    /// <summary>剪切上传的任务编号 → 本地源路径：上传成功后才删除源，失败保留。</summary>
    private readonly Dictionary<Guid, string> cutUploadSources = new Dictionary<Guid, string>();

    /// <summary>已挂过上传完成/失败监听的连接：同一传输服务只订阅一次。</summary>
    private readonly HashSet<string> uploadNoticeProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>解密连接凭据；解密失败返回空文本。</summary>
    private string ResolveSecret(RemoteConnectionProfile profile)
    {
        Kernel.Services.ConnectionManager manager = this.services.GetRequiredService<Kernel.Services.ConnectionManager>();
        try
        {
            return manager.GetSecret(profile);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 连接前确认密码：SSH 密码认证连接缺少可用密码时弹窗要求现场输入并加密保存，
    /// 返回 null 表示用户取消。全程只记录密码长度，不写明文。
    /// </summary>
    private async Task<string?> EnsureSecretAsync(RemoteConnectionProfile profile)
    {
        string secret = this.ResolveSecret(profile);
        string? missing = RemoteCredentialCheck.DescribeMissing(profile, secret);
        if (missing == null)
        {
            return secret;
        }
        bool mainMissing = profile.Protocol == RemoteProtocol.Ssh && profile.AuthType == RemoteAuthType.Password
            && string.IsNullOrEmpty(secret);
        if (!mainMissing)
        {
            // 缺口在跳板：提示到连接管理中补填跳板密码，不把输入错误地写进主连接
            Views.MessageDialog.Show(this.OwnerWindow, "需要补填跳板密码", missing);
            UiLogger.Log("远程连接", "连接前密码检查：连接=" + profile.Name + " 服务器=" + profile.Host
                + " 结果=跳板凭据缺失，未建立连接 原因=" + missing);
            return null;
        }
        string? input = await Views.MessageDialog.PromptPasswordAsync(this.OwnerWindow, "需要输入密码", missing);
        if (input == null || input.Length == 0)
        {
            UiLogger.Log("远程连接", "连接前密码检查：连接=" + profile.Name + " 服务器=" + profile.Host
                + " 结果=用户取消或未输入密码，未建立连接");
            return null;
        }
        Kernel.Services.ConnectionManager manager = this.services.GetRequiredService<Kernel.Services.ConnectionManager>();
        manager.SetSecret(profile, input);
        UiLogger.Log("远程连接", "连接前密码检查：连接=" + profile.Name + " 服务器=" + profile.Host
            + " 密码长度=" + input.Length + " 结果=已加密保存，继续建立连接");
        return input;
    }

    /// <summary>打开远程终端标签页（同一连接只保留一个，连接在后台线程建立）。</summary>
    public async void OpenTerminalTab(RemoteConnectionProfile profile)
    {
        if (profile == null)
        {
            return;
        }
        foreach (object tab in this.Tabs)
        {
            if (tab is TerminalTabViewModel terminal && terminal.AttachedProfileId == profile.Id)
            {
                this.SelectedTab = terminal;
                return;
            }
        }

        try
        {
            string? secret = await this.EnsureSecretAsync(profile);
            if (secret == null)
            {
                return;
            }
            IRemoteSessionFactory factory = this.services.GetRequiredService<IRemoteSessionFactory>();
            ITerminalSession session = await Task.Run(delegate
            {
                return factory.OpenTerminal(profile, secret, 120, 32);
            });
            TerminalTabViewModel newTerminal = new TerminalTabViewModel(session, profile.Name + " - 终端", 120, 32);
            newTerminal.AttachedProfileId = profile.Id;
            newTerminal.ReconnectRequested += this.OnTerminalReconnectRequested;
            foreach (string command in this.Settings.RemoteQuickCommands)
            {
                newTerminal.QuickCommands.Add(command);
            }
            this.Tabs.Add(newTerminal);
            this.SelectedTab = newTerminal;
        }
        catch (HostKeyUnknownException keyError)
        {
            bool trusted = await this.ConfirmUnknownHostKeyAsync(keyError);
            if (trusted)
            {
                this.OpenTerminalTab(profile);
            }
        }
        catch (HostKeyMismatchException keyError)
        {
            bool forgot = await this.ConfirmHostKeyMismatchAsync(keyError);
            if (forgot)
            {
                this.OpenTerminalTab(profile);
            }
        }
        catch (RemoteHopException hopError)
        {
            UiLogger.Log("远程连接审计", "跳板链连接失败：连接=" + profile.Name + " 跳序=" + hopError.HopIndex
                + " 主机=" + hopError.Host + ":" + hopError.Port + " 阶段=" + hopError.Stage
                + " 耗时=" + hopError.ElapsedMilliseconds + " 毫秒 结果=未建立终端，原因=" + hopError.Message);
            Views.MessageDialog.Show(this.OwnerWindow, "跳板连接失败", hopError.Message);
        }
        catch (Exception ex)
        {
            string reason = RemoteFailureText.Describe(ex, profile);
            UiLogger.LogError("远程连接", "打开终端失败：连接名称=" + profile.Name + " 服务器=" + profile.Host
                + " 端口=" + profile.GetEffectivePort() + " 认证方式=" + profile.AuthType
                + " 失败层次=" + RemoteFailureText.Layer(ex) + " 原因=" + reason, ex);
            Views.MessageDialog.Show(this.OwnerWindow, "连接失败", "无法连接 " + profile.Host + "：" + reason);
        }
    }

    /// <summary>把远程目录作为文件标签打开（复用本地文件标签的全部能力）。</summary>
    public async void OpenRemoteFolderTab(RemoteConnectionProfile profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            string? secret = await this.EnsureSecretAsync(profile);
            if (secret == null)
            {
                return;
            }
            // 先关闭同一连接的旧远程标签：旧标签可能停在失败的空列表，且持有旧凭据的 provider，
            // 保存新密码后继续复用会导致仍用旧凭据连接（二次连接永远失败）
            for (int index = this.Tabs.Count - 1; index >= 0; index--)
            {
                if (this.Tabs[index] is FileTabViewModel oldTab && oldTab.AttachedProfileId == profile.Id)
                {
                    oldTab.PathChanged -= this.OnTabPathChanged;
                    this.Tabs.RemoveAt(index);
                    UiLogger.Log("远程连接", "关闭旧的远程文件夹标签：连接=" + profile.Name + " 路径="
                        + oldTab.CurrentPath + " 结果=已移除，准备按当前凭据重新连接");
                }
            }
            IRemoteSessionFactory factory = this.services.GetRequiredService<IRemoteSessionFactory>();
            IFileSystemProvider provider = await Task.Run(delegate
            {
                IFileSystemProvider opened = factory.OpenFileSystem(profile, secret);
                // 先实际读取根目录，认证或 SFTP 不可用时直接抛错，不留下空标签
                opened.ListDirectory("/");
                return opened;
            });
            // 远程标签传空扫描服务：禁止对本机磁盘做目录大小回填
            FileTabViewModel newTab = new FileTabViewModel(provider, this.Settings, "/", null,
                this.services.GetRequiredService<IArchiveService>(), this.settingsStore);
            newTab.AttachedProfileId = profile.Id;
            newTab.RemoteRootTitle = profile.Name;
            newTab.PathChanged += this.OnTabPathChanged;
            newTab.RemoteFileOpenRequested += this.OnRemoteFileOpenRequested;
            newTab.RetryTaskRequested = (workspaceId, taskId) => this.RetryFileTask(workspaceId, taskId);
            this.Tabs.Add(newTab);
            this.SelectedTab = newTab;
            UiLogger.Log("远程连接", "打开远程文件夹成功：连接=" + profile.Name + " 服务器=" + profile.Host
                + " 端口=" + profile.GetEffectivePort() + " 认证=成功 SFTP根目录=可读 结果=已创建远程文件夹标签");
        }
        catch (HostKeyUnknownException keyError)
        {
            bool trusted = await this.ConfirmUnknownHostKeyAsync(keyError);
            if (trusted)
            {
                this.OpenRemoteFolderTab(profile);
            }
        }
        catch (HostKeyMismatchException keyError)
        {
            bool forgot = await this.ConfirmHostKeyMismatchAsync(keyError);
            if (forgot)
            {
                this.OpenRemoteFolderTab(profile);
            }
        }
        catch (RemoteHopException hopError)
        {
            UiLogger.Log("远程连接审计", "跳板链连接失败：连接=" + profile.Name + " 跳序=" + hopError.HopIndex
                + " 主机=" + hopError.Host + ":" + hopError.Port + " 阶段=" + hopError.Stage
                + " 耗时=" + hopError.ElapsedMilliseconds + " 毫秒 结果=未建立远程文件夹，原因=" + hopError.Message);
            Views.MessageDialog.Show(this.OwnerWindow, "跳板连接失败", hopError.Message);
        }
        catch (Exception ex)
        {
            string reason = RemoteFailureText.Describe(ex, profile);
            UiLogger.LogError("远程连接", "打开远程文件夹失败：连接名称=" + profile.Name + " 服务器=" + profile.Host
                + " 端口=" + profile.GetEffectivePort() + " 认证方式=" + profile.AuthType
                + " 失败层次=" + RemoteFailureText.Layer(ex) + " 原因=" + reason, ex);
            Views.MessageDialog.Show(this.OwnerWindow, "连接失败", "无法打开远程文件夹：" + reason);
        }
    }

    /// <summary>首次连接确认主机指纹；用户确认后写入 known_hosts 并返回 true。</summary>
    private async Task<bool> ConfirmUnknownHostKeyAsync(HostKeyUnknownException keyError)
    {
        string message = (keyError.HopIndex > 0 ? "第 " + keyError.HopIndex + " 跳（跳板机）" : "目标主机") + "\n"
            + "首次连接 " + keyError.Host + ":" + keyError.Port + "\n"
            + "主机密钥算法：" + keyError.KeyType + "\n"
            + "SHA256 指纹：" + keyError.FingerprintSha256 + "\n\n"
            + "请核对该指纹与服务器管理员提供的一致；确认后将保存到 known_hosts。";
        bool trust = await Views.MessageDialog.ConfirmAsync(this.OwnerWindow, "确认主机指纹", message,
            "信任并保存", "取消");
        if (!trust)
        {
            UiLogger.Log("远程连接审计", "主机密钥确认被取消：主机=" + keyError.Host + " 端口=" + keyError.Port
                + " 算法=" + keyError.KeyType + " 指纹=" + keyError.FingerprintSha256 + " 结果=未建立连接");
            return false;
        }
        KnownHostStore store = this.services.GetRequiredService<KnownHostStore>();
        store.Trust(keyError.Host, keyError.Port, keyError.KeyType, keyError.FingerprintSha256);
        UiLogger.Log("远程连接审计", "主机密钥已信任：主机=" + keyError.Host + " 端口=" + keyError.Port
            + " 算法=" + keyError.KeyType + " 指纹=" + keyError.FingerprintSha256 + " 结果=已写入 known_hosts 并重试连接");
        return true;
    }

    /// <summary>主机密钥变更警告：默认阻断；用户显式确认后才删除旧记录，随后必须重新确认新指纹。</summary>
    private async Task<bool> ConfirmHostKeyMismatchAsync(HostKeyMismatchException keyError)
    {
        string message = "主机密钥与已信任记录不一致，连接已阻断。\n"
            + (keyError.HopIndex > 0 ? "位置：第 " + keyError.HopIndex + " 跳（跳板机）\n" : "位置：目标主机\n")
            + "主机：" + keyError.Host + ":" + keyError.Port + "\n"
            + "密钥算法：" + keyError.KeyType + "\n"
            + "已信任指纹：" + keyError.ExpectedFingerprintSha256 + "\n"
            + "当前指纹：" + keyError.ActualFingerprintSha256 + "\n\n"
            + "这可能是服务器更换密钥，也可能是中间人攻击。只有确认变更来源可信时，才删除旧记录；删除后重新连接仍需再次确认新指纹。";
        bool forget = await Views.MessageDialog.ConfirmAsync(this.OwnerWindow, "主机密钥不匹配（已阻断）", message,
            "删除旧记录并重新连接", "取消");
        if (!forget)
        {
            UiLogger.Log("远程连接审计", "主机密钥变更被用户取消：主机=" + keyError.Host + " 端口=" + keyError.Port
                + " 算法=" + keyError.KeyType + " 已信任指纹=" + keyError.ExpectedFingerprintSha256
                + " 当前指纹=" + keyError.ActualFingerprintSha256 + " 结果=保持阻断");
            return false;
        }
        KnownHostStore store = this.services.GetRequiredService<KnownHostStore>();
        int removed = store.Remove(keyError.Host, keyError.Port);
        UiLogger.Log("远程连接审计", "用户删除旧主机密钥：主机=" + keyError.Host + " 端口=" + keyError.Port
            + " 删除条数=" + removed + " 结果=已删除，重新连接需要再次确认新指纹");
        return true;
    }

    /// <summary>最小主机密钥管理入口：查看并删除某连接主机的全部已信任密钥。</summary>
    public async void ManageKnownHosts(RemoteConnectionProfile profile)
    {
        if (profile == null)
        {
            return;
        }
        KnownHostStore store = this.services.GetRequiredService<KnownHostStore>();
        IReadOnlyList<KnownHostEntry> all = store.GetAll();
        List<KnownHostEntry> matches = new List<KnownHostEntry>();
        foreach (KnownHostEntry entry in all)
        {
            if (entry.Host == profile.Host.Trim().ToLowerInvariant() && entry.Port == profile.GetEffectivePort())
            {
                matches.Add(entry);
            }
        }
        if (matches.Count == 0)
        {
            Views.MessageDialog.Show(this.OwnerWindow, "主机密钥",
                profile.Host + ":" + profile.GetEffectivePort() + " 尚未保存任何已信任的主机密钥。");
            return;
        }
        string message = "主机 " + profile.Host + ":" + profile.GetEffectivePort() + " 的已信任密钥：\n";
        foreach (KnownHostEntry entry in matches)
        {
            message = message + "\n算法：" + entry.KeyType + "\nSHA256：" + entry.FingerprintSha256
                + "\n首次信任：" + entry.TrustedTime.ToString("yyyy-MM-dd HH:mm:ss") + "\n";
        }
        bool remove = await Views.MessageDialog.ConfirmAsync(this.OwnerWindow, "已信任的主机密钥",
            message + "\n是否删除该主机的全部已信任密钥？删除后下次连接需要重新确认指纹。", "删除", "关闭");
        if (!remove)
        {
            return;
        }
        int removed = store.Remove(profile.Host, profile.GetEffectivePort());
        UiLogger.Log("远程连接审计", "删除已信任主机密钥：连接=" + profile.Name + " 主机=" + profile.Host
            + " 端口=" + profile.GetEffectivePort() + " 删除条数=" + removed + " 结果=成功");
    }

    /// <summary>
    /// 以指定连接的远程根目录为工作区进入 IDE 模式：资源树直接浏览远端，
    /// 打开文本文件后保存会经 SFTP 回传。
    /// </summary>
    public async void OpenRemoteIdeRoot(RemoteConnectionProfile profile)
    {
        if (profile == null)
        {
            return;
        }
        try
        {
            string? secret = await this.EnsureSecretAsync(profile);
            if (secret == null)
            {
                return;
            }
            IRemoteSessionFactory factory = this.services.GetRequiredService<IRemoteSessionFactory>();
            IFileSystemProvider provider = await Task.Run(delegate
            {
                return factory.OpenFileSystem(profile, secret);
            });
            IFileContentAccess? contentAccess = provider as IFileContentAccess;
            if (contentAccess == null)
            {
                Views.MessageDialog.Show(this.OwnerWindow, "不支持", "该连接不支持远程文件内容读写，无法在 IDE 中编辑。");
                return;
            }
            this.IsIdeMode = true;
            this.IdeShell.SetWorkspace("/", provider, contentAccess, profile.Name);
        }
        catch (Exception ex)
        {
            string reason = RemoteFailureText.Describe(ex, profile);
            UiLogger.LogError("远程连接", "打开远程 IDE 工作区失败：连接名称=" + profile.Name + " 服务器=" + profile.Host
                + " 端口=" + profile.GetEffectivePort() + " 失败层次=" + RemoteFailureText.Layer(ex) + " 原因=" + reason, ex);
            Views.MessageDialog.Show(this.OwnerWindow, "连接失败", "无法打开远程 IDE 工作区：" + reason);
        }
    }

    /// <summary>打开远程传输队列标签页（同一连接复用同一传输服务）。</summary>
    public async void OpenTransfersTab(RemoteConnectionProfile profile)
    {
        if (profile == null)
        {
            return;
        }
        foreach (object tab in this.Tabs)
        {
            if (tab is RemoteTransferPanelViewModel transfers && transfers.AttachedProfileId == profile.Id)
            {
                this.SelectedTab = transfers;
                return;
            }
        }

        try
        {
            IRemoteTransferService service;
            if (!this.transferServices.TryGetValue(profile.Id, out service))
            {
                string? secret = await this.EnsureSecretAsync(profile);
                if (secret == null)
                {
                    return;
                }
                IRemoteSessionFactory factory = this.services.GetRequiredService<IRemoteSessionFactory>();
                service = await Task.Run(delegate
                {
                    return factory.OpenTransferService(profile, secret);
                });
                this.transferServices[profile.Id] = service;
            }
            RemoteTransferPanelViewModel newPanel = new RemoteTransferPanelViewModel(profile.Name, service);
            newPanel.AttachedProfileId = profile.Id;
            this.Tabs.Add(newPanel);
            this.SelectedTab = newPanel;
        }
        catch (Exception ex)
        {
            string reason = RemoteFailureText.Describe(ex, profile);
            UiLogger.LogError("远程连接", "建立传输服务失败：连接名称=" + profile.Name + " 服务器=" + profile.Host
                + " 端口=" + profile.GetEffectivePort() + " 失败层次=" + RemoteFailureText.Layer(ex) + " 原因=" + reason, ex);
            Views.MessageDialog.Show(this.OwnerWindow, "连接失败", "无法建立传输服务：" + reason);
        }
    }

    /// <summary>
    /// 本地条目粘贴到远程目录：必须走传输队列上传（远程提供者直接 Copy 读不到本地路径）。
    /// 远端同名直接覆盖，不触发桌面冲突对话框；失败按传输层给中文原因。
    /// </summary>
    private async Task UploadLocalPathsToRemoteAsync(FileTabViewModel tab, string targetDirectory, IReadOnlyList<string> paths, bool isCut)
    {
        foreach (string candidate in paths)
        {
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                tab.ShowMessage("远程目录暂不支持从远程来源直接复制或移动，请先下载到本地再上传");
                UiLogger.Log("程序行为", "远程粘贴：来源不是本地路径 来源=" + candidate + " 结果=未提交，已提示先下载到本地");
                return;
            }
        }
        RemoteConnectionProfile? profile = this.services.GetRequiredService<Kernel.Services.ConnectionManager>().FindById(tab.AttachedProfileId);
        if (profile == null)
        {
            tab.ShowMessage("找不到该远程标签对应的连接配置，请重新打开远程文件夹");
            UiLogger.Log("程序行为", "远程粘贴：连接配置=未找到 连接编号=" + tab.AttachedProfileId + " 结果=未提交");
            return;
        }
        try
        {
            IRemoteTransferService? service;
            if (!this.transferServices.TryGetValue(profile.Id, out service))
            {
                string? secret = await this.EnsureSecretAsync(profile);
                if (secret == null)
                {
                    return;
                }
                IRemoteSessionFactory factory = this.services.GetRequiredService<IRemoteSessionFactory>();
                IRemoteTransferService created = await Task.Run(delegate
                {
                    return factory.OpenTransferService(profile, secret);
                });
                this.transferServices[profile.Id] = created;
                service = created;
            }
            if (this.uploadNoticeProfiles.Add(profile.Id))
            {
                service.JobChanged += this.OnUploadJobChanged;
            }
            int submitted = 0;
            string trimmedTarget = targetDirectory.TrimEnd('/');
            foreach (string source in paths)
            {
                string remotePath = trimmedTarget + "/" + Path.GetFileName(source);
                RemoteTransferJob job = service.EnqueueUpload(source, remotePath);
                if (isCut)
                {
                    this.cutUploadSources[job.Id] = source;
                }
                submitted++;
            }
            if (isCut)
            {
                this.clipboardPaths = null;
                _ = this.RefreshClipboardAvailabilityAsync(true);
            }
            tab.ShowMessage("已提交上传任务：" + submitted + " 项 → " + targetDirectory + "（可在传输列表与任务中心查看进度）");
            UiLogger.Log("程序行为", "远程粘贴提交上传：连接=" + profile.Name + " 目标=" + targetDirectory
                + " 路径总数=" + submitted + " 方式=" + (isCut ? "移动（上传成功后删除源）" : "复制")
                + " 冲突策略=远端同名直接覆盖，不弹本地冲突对话框");
        }
        catch (Exception error)
        {
            string reason = RemoteFailureText.Describe(error, profile);
            tab.ShowMessage("上传提交失败：" + reason);
            UiLogger.LogError("远程传输", "提交上传失败：连接=" + profile.Name + " 目标=" + targetDirectory
                + " 失败层次=" + RemoteFailureText.Layer(error) + " 原因=" + reason, error);
        }
    }

    /// <summary>上传完成/失败：成功的剪切删除本地源；失败给中文原因并保留源文件。</summary>
    private void OnUploadJobChanged(RemoteTransferJob job)
    {
        if (job.Status == RemoteTransferStatus.Completed)
        {
            string? source;
            if (this.cutUploadSources.TryGetValue(job.Id, out source))
            {
                this.cutUploadSources.Remove(job.Id);
                try
                {
                    if (Directory.Exists(source)) { Directory.Delete(source, true); }
                    else if (File.Exists(source)) { File.Delete(source); }
                }
                catch (Exception error)
                {
                    UiLogger.LogError("远程传输", "上传成功但删除本地源失败：源=" + source + " 远端=" + job.RemotePath, error);
                }
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(delegate
            {
                foreach (object item in this.Tabs)
                {
                    if (item is FileTabViewModel fileTab
                        && fileTab.FileSystemProvider is not TabTaskExplorer.Kernel.Services.DefaultFileSystemProvider)
                    {
                        fileTab.Refresh();
                    }
                }
            });
            return;
        }
        if (job.Status == RemoteTransferStatus.Failed)
        {
            this.cutUploadSources.Remove(job.Id);
            string reason = RemoteFailureText.DescribeTransfer(job.ErrorMessage);
            Avalonia.Threading.Dispatcher.UIThread.Post(delegate
            {
                if (this.SelectedFileTab != null)
                {
                    this.SelectedFileTab.ShowMessage("上传失败：" + reason + "（远端路径：" + job.RemotePath + "）");
                }
            });
            UiLogger.LogError("远程传输", "上传失败", "任务=" + job.Id + " 本地=" + job.LocalPath
                + " 远端=" + job.RemotePath + " 失败层次=传输层 原因=" + reason);
        }
    }

    /// <summary>终端断线重连请求：关闭旧终端标签并按连接配置重建。</summary>
    private void OnTerminalReconnectRequested(object? sender, string profileId)
    {
        RemoteConnectionProfile? profile = this.services.GetRequiredService<Kernel.Services.ConnectionManager>().FindById(profileId);
        if (sender is TerminalTabViewModel oldTerminal)
        {
            this.Tabs.Remove(oldTerminal);
            oldTerminal.Dispose();
        }
        if (profile != null)
        {
            this.OpenTerminalTab(profile);
        }
    }

    /// <summary>远程文件内容打开请求：在后台读取内容并打开编辑器标签。</summary>
    private async void OnRemoteFileOpenRequested(object? sender, string remotePath)
    {
        FileTabViewModel? fileTab = sender as FileTabViewModel;
        if (fileTab == null || fileTab.ContentAccess == null)
        {
            return;
        }
        IFileContentAccess contentAccess = fileTab.ContentAccess;
        try
        {
            byte[] bytes = await Task.Run(delegate
            {
                return contentAccess.ReadAllBytes(remotePath);
            });
            if (bytes.Length > 2 * 1024 * 1024)
            {
                Views.MessageDialog.Show(this.OwnerWindow, "文件过大", "在线编辑仅支持 2MB 以内的文本文件，请使用传输队列下载后编辑。");
                return;
            }
            string text = System.Text.Encoding.UTF8.GetString(bytes);
            EditorTabViewModel editor = new EditorTabViewModel(contentAccess, remotePath, text, fileTab.AttachedProfileId);
            editor.Saved += this.OnRemoteEditorSaved;
            this.Tabs.Add(editor);
            this.SelectedTab = editor;
        }
        catch (Exception ex)
        {
            Views.MessageDialog.Show(this.OwnerWindow, "打开失败", "无法读取远程文件：" + ex.Message);
        }
    }

    /// <summary>远程文件保存完成提示。</summary>
    private void OnRemoteEditorSaved(object? sender, string remotePath)
    {
        // 保存后由编辑器标签自行提示状态，无需额外处理
    }

    /// <summary>关闭标签页并选中相邻标签。</summary>
    public void CloseTab(object tab)
    {
        // 终端标签页关闭时同步断开远程会话
        if (tab is TerminalTabViewModel closingTerminal)
        {
            closingTerminal.Dispose();
        }
        // 磁盘视图标签关闭时停止台前探测并释放扫描订阅
        if (tab is DiskViewPanelViewModel closingDiskView)
        {
            closingDiskView.Close();
        }
        // 进程监控标签关闭时停止台前周期刷新
        if (tab is ProcessPanelViewModel closingProcessPanel)
        {
            closingProcessPanel.Close();
        }
        // 文件标签关闭时停止在途的目录大小回填
        if (tab is FileTabViewModel closingFileTab)
        {
            closingFileTab.Close();
        }
        // 媒体标签关闭时停止播放并释放解码器与位图
        if (tab is MediaTabViewModel closingMediaTab)
        {
            closingMediaTab.Dispose();
        }
        // 截图录屏标签关闭时取消事件订阅与状态计时器
        if (tab is CaptureTabViewModel closingCaptureTab)
        {
            closingCaptureTab.Detach();
        }
        // 启动项管理标签关闭时停止后续枚举回调
        if (tab is StartupManagerViewModel closingStartupTab)
        {
            closingStartupTab.Close();
        }
        // 比对标签关闭时丢弃在途比对结果
        if (tab is CompareTabViewModel closingCompareTab)
        {
            closingCompareTab.Close();
        }
        int index = this.Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }
        this.Tabs.Remove(tab);
        UiLogger.Log("程序行为", "关闭标签：序号=" + index + " 剩余标签数=" + this.Tabs.Count);
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

    /// <summary>
    /// 把标签移出本窗口（拖出成新窗口用）：只解除事件订阅与集合归属，不调用标签的关闭 / 释放逻辑。
    /// </summary>
    public void DetachTab(object tab)
    {
        int index = this.Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }
        string locationText = "非文件标签";
        FileTabViewModel? fileTab = tab as FileTabViewModel;
        if (fileTab != null)
        {
            fileTab.PathChanged -= this.OnTabPathChanged;
            fileTab.ArchiveOpenRequested -= this.OnArchiveOpenRequested;
            fileTab.MediaOpenRequested -= this.OnMediaOpenRequested;
            locationText = fileTab.CurrentPath;
        }
        this.Tabs.Remove(tab);
        // 修正选中标签（与关闭标签相同的邻接选择逻辑，但标签本身继续存活）
        if (object.ReferenceEquals(this.selectedTab, tab))
        {
            if (this.Tabs.Count == 0)
            {
                this.SelectedTab = null;
            }
            else
            {
                if (index >= this.Tabs.Count)
                {
                    index = this.Tabs.Count - 1;
                }
                this.SelectedTab = this.Tabs[index];
            }
        }
        UiLogger.Log("程序行为", "标签移出窗口：路径=" + locationText + " 源窗口剩余标签数=" + this.Tabs.Count);
    }

    /// <summary>
    /// 把标签移入本窗口（拖出创建的新窗口接收用）：重新订阅事件并选中该标签，标签全程不被关闭。
    /// </summary>
    public void AttachTab(object tab)
    {
        string locationText = "非文件标签";
        FileTabViewModel? fileTab = tab as FileTabViewModel;
        if (fileTab != null)
        {
            fileTab.PathChanged += this.OnTabPathChanged;
            fileTab.ArchiveOpenRequested += this.OnArchiveOpenRequested;
            fileTab.MediaOpenRequested += this.OnMediaOpenRequested;
            locationText = fileTab.CurrentPath;
        }
        this.Tabs.Add(tab);
        this.SelectedTab = tab;
        UiLogger.Log("程序行为", "标签移入本窗口：路径=" + locationText + " 本窗口标签总数=" + this.Tabs.Count);
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

    /// <summary>关闭除指定标签外的全部文件标签页。</summary>
    public void CloseOtherTabs(FileTabViewModel keep)
    {
        List<object> snapshot = new List<object>(this.Tabs);
        foreach (object tab in snapshot)
        {
            if (tab is FileTabViewModel file && !object.ReferenceEquals(file, keep))
            {
                this.CloseTab(tab);
            }
        }
        this.SelectedTab = keep;
    }

    /// <summary>切换标签固定状态：固定后移动到标签条最前。</summary>
    public void TogglePinTab(FileTabViewModel tab)
    {
        tab.IsPinned = !tab.IsPinned;
        int index = this.Tabs.IndexOf(tab);
        if (index >= 0)
        {
            if (tab.IsPinned)
            {
                this.Tabs.Move(index, 0);
            }
        }
    }

    /// <summary>把当前文件标签页路径固定到快捷访问。</summary>
    public void PinCurrentTab()
    {
        string? path = this.SelectedFileTab?.CurrentPath;
        if (!string.IsNullOrEmpty(path))
        {
            this.PinPath(path);
        }
    }

    /// <summary>把指定路径固定到快捷访问（已存在时忽略）。</summary>
    public void PinPath(string path)
    {
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
        UiLogger.Log("配置更改", "快捷访问：新增固定路径=" + path + " 当前固定数量=" + this.Settings.PinnedPaths.Count);
        this.FillQuickAccessItems();
    }

    /// <summary>从快捷访问取消固定。</summary>
    public void Unpin(string path)
    {
        this.Settings.PinnedPaths.Remove(path);
        this.settingsStore.Save(this.Settings);
        UiLogger.Log("配置更改", "快捷访问：取消固定路径=" + path + " 当前固定数量=" + this.Settings.PinnedPaths.Count);
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

    /// <summary>把路径文本写入系统剪贴板（右键「复制完整路径」使用，多选时每行一条）。</summary>
    public async Task<bool> CopyPathTextAsync(IReadOnlyList<string> paths)
    {
        if (paths == null || paths.Count == 0)
        {
            UiLogger.Log("程序行为", "复制完整路径：路径列表=空 结果=失败，未写入系统剪贴板");
            return false;
        }
        ISystemClipboardService? systemClipboard = this.services.GetService<ISystemClipboardService>();
        if (systemClipboard == null)
        {
            UiLogger.Log("程序行为", "复制完整路径：系统剪贴板服务=未注册 结果=失败，未写入系统剪贴板");
            return false;
        }
        string text = string.Join(Environment.NewLine, paths);
        bool success = await systemClipboard.WriteTextAsync(text);
        UiLogger.Log("程序行为", "复制完整路径：路径总数=" + paths.Count + " 路径列表=" + string.Join("、", paths)
            + " 结果=" + (success ? "成功，已写入系统剪贴板" : "失败，系统剪贴板写入未成功"));
        return success;
    }

    /// <summary>写入内部剪贴板状态，并把同样的内容写进系统剪贴板供外部程序粘贴。</summary>
    private void SetClipboardPaths(IReadOnlyList<string> paths, bool isCut)
    {
        if (paths.Count == 0)
        {
            UiLogger.Log("程序行为", "写入剪贴板：路径列表=空 结果=失败，未写入任何剪贴板");
            return;
        }
        this.clipboardPaths = new List<string>(paths);
        this.clipboardIsCut = isCut;
        this.WriteToSystemClipboard(paths, isCut);
        FileTabViewModel? tab = this.SelectedFileTab;
        if (tab != null)
        {
            tab.ShowMessage((isCut ? "已剪切 " : "已复制 ") + paths.Count + " 项，按 Ctrl+V 粘贴到目标文件夹");
        }
        _ = this.RefreshClipboardAvailabilityAsync(true);
    }

    /// <summary>启动剪贴板监听（1.2 秒轮询，仅在可用性变化时刷新并记日志，避免日志抖动）。</summary>
    private void StartClipboardAvailabilityWatcher()
    {
        if (this.clipboardAvailabilityTimer != null)
        {
            return;
        }
        this.clipboardAvailabilityTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200),
        };
        this.clipboardAvailabilityTimer.Tick += async delegate
        {
            try
            {
                await this.RefreshClipboardAvailabilityAsync(false);
                this.clipboardProbeTickCount++;
                if (this.clipboardProbeTickCount % 20 == 0)
                {
                    UiLogger.Log("程序行为", "剪贴板监听心跳：已探测 " + this.clipboardProbeTickCount
                        + " 次 当前可用性=" + (this.canPasteFromClipboard ? "可用" : "不可用")
                        + " 格式=" + this.clipboardAvailabilitySignature);
                }
            }
            catch (Exception error)
            {
                UiLogger.LogError("程序行为", "刷新剪贴板粘贴可用性失败", error);
            }
        };
        this.clipboardAvailabilityTimer.Start();
        _ = this.RefreshClipboardAvailabilityAsync(true);
    }

    /// <summary>
    /// 刷新「粘贴」可用性：内部剪贴板优先，其次轻量探测系统剪贴板（CF_HDROP / 文件列表）。
    /// 只在状态变化或强制时写日志：粘贴可用性=可用/不可用 剪贴板格式=… 来源=…。
    /// </summary>
    public async Task RefreshClipboardAvailabilityAsync(bool forceLog)
    {
        // 防止重入：上一次探测尚未返回时跳过本次，避免后台探测任务堆积
        if (this.clipboardProbeRunning)
        {
            return;
        }
        this.clipboardProbeRunning = true;
        try
        {
            bool hasFiles;
            string format;
            string source;
            if (this.clipboardPaths != null && this.clipboardPaths.Count > 0)
            {
                hasFiles = true;
                format = "内部剪贴板文件列表";
                source = "内部剪贴板";
            }
            else
            {
                ISystemClipboardService? systemClipboard = this.services.GetService<ISystemClipboardService>();
                if (systemClipboard == null)
                {
                    hasFiles = false;
                    format = "系统剪贴板服务未注册";
                    source = "无服务";
                }
                else
                {
                    ClipboardPresence presence;
                    UiWatchdog.EnterStage("剪贴板探测");
                    try
                    {
                        presence = await systemClipboard.ProbeAsync();
                    }
                    finally
                    {
                        UiWatchdog.LeaveStage("剪贴板探测");
                    }
                    hasFiles = presence.HasFileData;
                    format = presence.FormatDescription
                        + (presence.FileCount > 0 ? "，文件数=" + presence.FileCount : string.Empty);
                    source = "系统剪贴板";
                }
            }
            string signature = (hasFiles ? "1" : "0") + "|" + format;
            if (!forceLog && signature == this.clipboardAvailabilitySignature)
            {
                return;
            }
            this.clipboardAvailabilitySignature = signature;
            this.CanPasteFromClipboard = hasFiles;
            UiLogger.Log("程序行为", "粘贴可用性=" + (hasFiles ? "可用" : "不可用")
                + " 剪贴板格式=" + format + " 来源=" + source);
        }
        finally
        {
            this.clipboardProbeRunning = false;
        }
    }

    /// <summary>把复制 / 剪切的路径写进系统剪贴板，让资源管理器等外部程序也能粘贴。</summary>
    private void WriteToSystemClipboard(IReadOnlyList<string> paths, bool isCut)
    {
        ISystemClipboardService? systemClipboard = this.services.GetService<ISystemClipboardService>();
        if (systemClipboard == null)
        {
            UiLogger.Log("程序行为", "写入系统剪贴板：系统剪贴板服务=未注册 结果=失败，仅内部剪贴板生效");
            return;
        }
        try
        {
            _ = systemClipboard.WriteAsync(paths, isCut);
            UiLogger.Log("程序行为", "写入系统剪贴板：路径总数=" + paths.Count + " 路径列表=" + string.Join("、", paths)
                + " 方式=" + (isCut ? "剪切" : "复制") + " 结果=已提交写入，具体结果见系统剪贴板服务日志");
        }
        catch (Exception error)
        {
            UiLogger.LogError("程序行为", "写入系统剪贴板：路径总数=" + paths.Count
                + " 方式=" + (isCut ? "剪切" : "复制") + " 结果=失败，仅内部剪贴板生效", error);
        }
    }

    /// <summary>
    /// 把剪贴板中的条目复制或移动到目标目录：先读系统剪贴板（含外部程序复制的内容），
    /// 按「我方状态标记 → 外部文件列表 → 内部剪贴板」的顺序判定粘贴内容与移动 / 复制方式（判定逻辑在内核层，桌面与网页共用）。
    /// 剪切落地成功后清掉剪切状态，与资源管理器行为一致。
    /// </summary>
    /// <param name="targetDirectory">目标目录；为空时用当前选中标签的当前目录。</param>
    public async Task PasteFromClipboardAsync(string? targetDirectory = null)
    {
        FileTabViewModel? tab = this.SelectedFileTab;
        string target = string.IsNullOrEmpty(targetDirectory)
            ? (tab == null ? string.Empty : tab.CurrentPath)
            : targetDirectory;
        if (string.IsNullOrEmpty(target))
        {
            UiLogger.Log("程序行为", "粘贴：选中标签=无且未指定目标目录 结果=失败，未执行粘贴");
            return;
        }
        if (tab != null && tab.FileSystemProvider is ArchiveFileSystemProvider && string.IsNullOrEmpty(targetDirectory))
        {
            tab.ShowMessage("压缩包预览标签不支持粘贴，请在普通文件夹或远程目录中粘贴");
            UiLogger.Log("程序行为", "粘贴：目标=压缩包预览标签 结果=不支持，已提示不可用");
            return;
        }

        ClipboardContent? content = null;
        ISystemClipboardService? systemClipboard = this.services.GetService<ISystemClipboardService>();
        if (systemClipboard == null)
        {
            UiLogger.Log("程序行为", "粘贴：系统剪贴板服务=未注册 结果=按内部剪贴板判定");
        }
        else
        {
            UiWatchdog.EnterStage("剪贴板读取");
            try
            {
                content = await systemClipboard.ReadAsync();
            }
            catch (Exception error)
            {
                UiLogger.LogError("程序行为", "粘贴：读取系统剪贴板 结果=失败，按内部剪贴板判定", error);
            }
            finally
            {
                UiWatchdog.LeaveStage("剪贴板读取");
            }
        }

        ClipboardPasteDecision decision = ClipboardPastePolicy.Decide(content, this.clipboardPaths, this.clipboardIsCut);
        string categoryDescription;
        if (decision.Category == ClipboardPasteCategory.StateFormat) { categoryDescription = "我方状态标记"; }
        else if (decision.Category == ClipboardPasteCategory.SystemFiles) { categoryDescription = "外部程序的文件列表"; }
        else if (decision.Category == ClipboardPasteCategory.InternalFallback) { categoryDescription = "内部剪贴板"; }
        else if (decision.Category == ClipboardPasteCategory.NoFileData) { categoryDescription = "剪贴板有数据但没有文件"; }
        else { categoryDescription = "剪贴板为空"; }
        UiLogger.Log("程序行为", "粘贴判定：目标目录=" + (target.Length == 0 ? "（未进入任何目录）" : target)
            + " 判定分类=" + categoryDescription + " 待粘贴路径数=" + decision.Paths.Count
            + " 待粘贴路径=" + (decision.Paths.Count == 0 ? "（无）" : string.Join("、", decision.Paths))
            + " 方式=" + (decision.IsCut ? "移动" : "复制"));

        if (decision.Category == ClipboardPasteCategory.NoFileData)
        {
            if (tab != null) { tab.ShowMessage("剪贴板里没有可粘贴的文件"); }
            UiLogger.Log("程序行为", "粘贴：判定分类=剪贴板有数据但没有文件 结果=失败，未执行粘贴");
            return;
        }
        if (decision.Category == ClipboardPasteCategory.Empty)
        {
            if (tab != null) { tab.ShowMessage("剪贴板为空，请先复制或剪切"); }
            UiLogger.Log("程序行为", "粘贴：判定分类=剪贴板为空 结果=失败，未执行粘贴");
            return;
        }
        if (string.IsNullOrEmpty(target))
        {
            if (tab != null) { tab.ShowMessage("请先进入目标文件夹"); }
            UiLogger.Log("程序行为", "粘贴：目标目录=未取到 结果=失败，未执行粘贴");
            return;
        }

        int successCount = 0;
        int failedCount = 0;
        bool isCut = decision.IsCut;
        IReadOnlyList<string> paths = decision.Paths;
        bool usedStateFormat = decision.Category == ClipboardPasteCategory.StateFormat;
        // 远程标签且目标是远端路径（不是已存在的本地目录）：走上传队列；远端没有本地冲突概念，不弹冲突对话框。
        // 用 Directory.Exists 区分落点：远程标签下粘贴到本地书签/导航目录时仍按本地任务中心处理。
        if (tab != null && !string.IsNullOrEmpty(tab.AttachedProfileId) && !Directory.Exists(target))
        {
            await this.UploadLocalPathsToRemoteAsync(tab, target, paths, isCut);
            return;
        }
        // 目标是否为真实本地目录与当前标签类型无关：导航 / 书签 / 磁盘视图落点到本地目录时也应进入本地任务中心
        bool localTask = paths.Count > 0 && Directory.Exists(target);
        if (localTask)
        {
            foreach (string candidate in paths)
            {
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    localTask = false;
                    break;
                }
            }
        }
        if (localTask)
        {
            this.EnsureTaskRefreshSubscription();
            UiLogger.Log("程序行为", "粘贴开始：源数量=" + paths.Count + " 目标=" + target
                + " 冲突策略=询问（同名文件替换/改名共存，同名文件夹合并/替换/逐项处理/改名共存）");
            string taskId;
            UiWatchdog.EnterStage("任务提交");
            try
            {
                taskId = this.fileTaskCenter.Submit(new TabTaskExplorer.App.Services.Tasks.FileTaskRequest
                {
                    Kind = isCut ? TabTaskExplorer.App.Services.Tasks.FileTaskKind.Move : TabTaskExplorer.App.Services.Tasks.FileTaskKind.Copy,
                    Sources = new List<string>(paths),
                    DestinationPath = target,
                    ConflictPolicy = TabTaskExplorer.App.Services.Tasks.FileConflictPolicy.Ask,
                });
            }
            finally
            {
                UiWatchdog.LeaveStage("任务提交");
            }
            if (!string.IsNullOrEmpty(taskId))
            {
                if (isCut)
                {
                    this.clipboardPaths = null;
                    _ = this.RefreshClipboardAvailabilityAsync(true);
                }
                if (tab != null)
                {
                    tab.ShowMessage("已提交" + (isCut ? "移动" : "复制") + "任务：" + paths.Count + " 项，冲突时等待你选择处理方式");
                }
                UiLogger.Log("程序行为", "粘贴提交任务中心：目标目录=" + target + " 路径总数=" + paths.Count
                    + " 方式=" + (isCut ? "移动" : "复制") + " 任务=" + taskId);
                if (isCut && systemClipboard != null && usedStateFormat)
                {
                    try
                    {
                        await systemClipboard.ClearAsync();
                    }
                    catch (Exception error)
                    {
                        UiLogger.LogError("程序行为", "粘贴提交任务后清理系统剪贴板失败", error);
                    }
                }
                return;
            }
        }
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            successCount = this.TransferPathsToDirectory(tab, target, paths, isCut, out failedCount);
            if (isCut)
            {
                this.clipboardPaths = null;
                _ = this.RefreshClipboardAvailabilityAsync(true);
            }
            if (tab != null) { tab.ShowMessage("粘贴完成：成功 " + successCount + " 项，失败 " + failedCount + " 项"); }
            UiLogger.Log("程序行为", "粘贴落地：目标目录=" + target + " 路径总数=" + paths.Count
                + " 方式=" + (isCut ? "移动" : "复制") + " 成功条数=" + successCount + " 失败条数=" + failedCount
                + " 结果=" + (failedCount == 0 ? "全部成功" : "部分或全部失败"));
            this.RefreshTabsAtPath(target, tab);
        });

        // 剪切落地后清掉剪贴板里的剪切状态，与资源管理器一致：粘贴走的剪切文件不再留在剪贴板
        if (isCut && systemClipboard != null && usedStateFormat)
        {
            try
            {
                await systemClipboard.ClearAsync();
                UiLogger.Log("程序行为", "粘贴后清理剪贴板：判定分类=我方状态标记 方式=移动 结果=已清空系统剪贴板");
            }
            catch (Exception error)
            {
                UiLogger.LogError("程序行为", "粘贴后清理剪贴板：方式=移动 结果=失败", error);
            }
        }
    }

    /// <summary>粘贴或磁盘操作落地后刷新目标目录所在的标签页；没有标签正好停在该目录时刷新回退标签（没有回退标签则只刷新磁盘视图）。</summary>
    public void RefreshTabsAtPath(string targetDirectory, FileTabViewModel? fallbackTab)
    {
        bool refreshedAny = false;
        foreach (object item in this.Tabs)
        {
            if (item is FileTabViewModel fileTab
                && string.Equals(fileTab.CurrentPath, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                fileTab.Refresh();
                refreshedAny = true;
            }
        }
        if (!refreshedAny && fallbackTab != null)
        {
            fallbackTab.Refresh();
        }
    }

    /// <summary>执行磁盘视图专属的文件操作（桌面跟随模式）：目标必须位于当前磁盘标签根目录或可见行内。</summary>
    private string ExecuteDiskAction(string action, string argument, DiskViewPanelViewModel? diskTab)
    {
        if (diskTab == null)
        {
            return "当前标签不是磁盘视图";
        }
        List<string> lines = WebWorkspace.SplitPathLines(argument);
        if (action == "disknewfolder" || action == "disknewfile")
        {
            if (lines.Count < 2)
            {
                return "参数不足：需要目录与新名称";
            }
            string directory = lines[0];
            string name = lines[1];
            string guard = this.EnsureDiskDirectory(diskTab, directory);
            if (guard.Length > 0)
            {
                return guard;
            }
            string message = action == "disknewfolder"
                ? diskTab.CreateFolder(directory, name)
                : diskTab.CreateFile(directory, name);
            this.RefreshTabsAtPath(directory, null);
            return message;
        }
        if (action == "diskrename")
        {
            if (lines.Count < 2)
            {
                return "参数不足：需要原路径与新名称";
            }
            DiskRow? row = this.FindDiskRow(diskTab, lines[0]);
            if (row == null)
            {
                return "重命名失败：目标不在当前磁盘视图内";
            }
            string parent = Path.GetDirectoryName(row.FullPath) ?? string.Empty;
            string guard = this.EnsureDiskDirectory(diskTab, parent);
            if (guard.Length > 0)
            {
                return guard;
            }
            string message = diskTab.Rename(row, lines[1]);
            this.RefreshTabsAtPath(parent, null);
            return message;
        }
        if (action == "diskdelete")
        {
            bool permanent = lines.Count > 0 && lines[0] == "1";
            int startIndex = permanent ? 1 : 0;
            int deletedCount = 0;
            string lastMessage = string.Empty;
            for (int index = startIndex; index < lines.Count; index++)
            {
                DiskRow? row = this.FindDiskRow(diskTab, lines[index]);
                if (row == null)
                {
                    continue;
                }
                string parent = Path.GetDirectoryName(row.FullPath) ?? string.Empty;
                string guard = this.EnsureDiskDirectory(diskTab, parent);
                if (guard.Length > 0)
                {
                    lastMessage = guard;
                    continue;
                }
                lastMessage = diskTab.Delete(row, permanent);
                deletedCount++;
                this.RefreshTabsAtPath(parent, null);
            }
            return deletedCount == 0 ? "删除失败：目标不在当前磁盘视图内" : lastMessage;
        }
        if (action == "diskpasteinto")
        {
            if (lines.Count == 0)
            {
                return "粘贴失败：缺少目标目录";
            }
            string guard = this.EnsureDiskDirectory(diskTab, lines[0]);
            if (guard.Length > 0)
            {
                return guard;
            }
            if (this.clipboardPaths == null || this.clipboardPaths.Count == 0)
            {
                return "剪贴板为空，请先复制或剪切";
            }
            int successCount = this.TransferPathsToDirectory(null, lines[0], this.clipboardPaths,
                this.clipboardIsCut, out int failedCount);
            if (this.clipboardIsCut)
            {
                this.clipboardPaths = null;
            }
            this.RefreshTabsAtPath(lines[0], null);
            diskTab.RefreshAfterChange(lines[0]);
            return "粘贴完成：成功 " + successCount + " 项，失败 " + failedCount + " 项";
        }
        if (action == "diskcopysource" || action == "diskcutsource")
        {
            List<string> paths = new List<string>();
            foreach (string line in lines)
            {
                DiskRow? row = this.FindDiskRow(diskTab, line);
                if (row != null)
                {
                    paths.Add(row.FullPath);
                }
            }
            if (paths.Count == 0)
            {
                return "请先选择要复制或剪切的条目";
            }
            this.SetClipboardPaths(paths, action == "diskcutsource");
            return (action == "diskcutsource" ? "已剪切 " : "已复制 ") + paths.Count + " 项，可粘贴到目标文件夹";
        }
        if (action == "diskcopyto" || action == "diskmoveto")
        {
            if (lines.Count < 2)
            {
                return "拖放失败：需要目标目录与源条目";
            }
            string target = lines[0];
            string guard = this.EnsureDiskDirectory(diskTab, target);
            if (guard.Length > 0)
            {
                return guard;
            }
            List<string> sources = new List<string>();
            for (int index = 1; index < lines.Count; index++)
            {
                DiskRow? row = this.FindDiskRow(diskTab, lines[index]);
                if (row != null)
                {
                    sources.Add(row.FullPath);
                }
            }
            if (sources.Count == 0)
            {
                return "拖放失败：源条目不在当前磁盘视图内";
            }
            bool isMove = action == "diskmoveto";
            int successCount = diskTab.TransferPaths(sources, target, isMove, out int failedCount);
            foreach (string source in sources)
            {
                this.RefreshTabsAtPath(Path.GetDirectoryName(source) ?? string.Empty, null);
            }
            this.RefreshTabsAtPath(target, null);
            return "拖放完成（" + (isMove ? "移动" : "复制") + "）：成功 " + successCount + " 项，失败 " + failedCount + " 项";
        }
        return "未知磁盘操作：" + action;
    }

    /// <summary>校验磁盘操作目录：必须等于根目录或当前可见目录行，返回空表示通过。</summary>
    private string EnsureDiskDirectory(DiskViewPanelViewModel diskTab, string directory)
    {
        string root = WebContextMenuPolicy.NormalizePath(diskTab.RootPathInput);
        string target = WebContextMenuPolicy.NormalizePath(directory);
        if (root.Length == 0)
        {
            return "此电脑视图没有具体目录，请先打开某个磁盘或目录";
        }
        if (string.Equals(root, target, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }
        foreach (DiskRow row in diskTab.Rows)
        {
            if (row.IsDirectory
                && string.Equals(WebContextMenuPolicy.NormalizePath(row.FullPath), target, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
        }
        return "目标目录不在当前磁盘视图内：" + directory;
    }

    /// <summary>在当前磁盘视图的可见行里按完整路径查找一行；找不到返回空。</summary>
    private DiskRow? FindDiskRow(DiskViewPanelViewModel diskTab, string path)
    {
        string target = WebContextMenuPolicy.NormalizePath(path);
        if (target.Length == 0)
        {
            return null;
        }
        foreach (DiskRow row in diskTab.Rows)
        {
            if (string.Equals(WebContextMenuPolicy.NormalizePath(row.FullPath), target, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }
        return null;
    }

    /// <summary>
    /// 把一批路径统一移动或复制到指定目录：Ctrl+V 粘贴、列表内拖放、窗口级拖放、磁盘视图拖放共用的落地入口。
    /// 逐条按源路径解析实际提供者（压缩包内 / 包外各自处理），单条失败不影响其余条目，
    /// 返回成功条数，并通过 out 参数返回失败条数。
    /// </summary>
    /// <param name="providerTab">发起落地的目标标签；磁盘视图等没有文件标签的场景为 null（按默认文件系统处理）。</param>
    /// <param name="targetDirectory">目标目录。</param>
    /// <param name="paths">要移动或复制的源路径列表。</param>
    /// <param name="isMove">true 为移动，false 为复制。</param>
    /// <param name="failedCount">落地失败的条目数。</param>
    /// <returns>落地成功的条目数。</returns>
    public int TransferPathsToDirectory(FileTabViewModel? providerTab, string targetDirectory,
        IReadOnlyList<string> paths, bool isMove, out int failedCount)
    {
        int successCount = 0;
        failedCount = 0;
        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            try
            {
                // 按源 / 目标是否在压缩包内解析实际提供者：包内粘贴走追加，包内复制出走解压
                IFileSystemProvider fileSystem = this.ResolvePasteProvider(providerTab, path);
                if (isMove)
                {
                    fileSystem.Move(path, targetDirectory);
                }
                else
                {
                    fileSystem.Copy(path, targetDirectory);
                }
                successCount++;
            }
            catch
            {
                failedCount++;
            }
        }
        return successCount;
    }

    /// <summary>
    /// 解析单条粘贴路径应使用的文件系统提供者：
    /// 目标标签在压缩包内时一律用目标压缩包提供者（包外源 → 追加进包）；
    /// 否则源在压缩包内时用源压缩包提供者（包内源 → 解压到目标目录）；
    /// 两者都不是时用目标标签自己的提供者；目标标签为空（磁盘视图）时用默认文件系统提供者。
    /// </summary>
    private IFileSystemProvider ResolvePasteProvider(FileTabViewModel? tab, string path)
    {
        ArchiveFileSystemProvider? targetArchive = tab == null ? null : tab.FileSystemProvider as ArchiveFileSystemProvider;
        if (targetArchive != null)
        {
            return targetArchive;
        }
        IArchiveService archiveService = this.services.GetRequiredService<IArchiveService>();
        if (ArchiveFileSystemProvider.TryCreate(archiveService, path, out ArchiveFileSystemProvider? sourceArchive)
            && sourceArchive!.OwnsPath(path))
        {
            return sourceArchive;
        }
        if (tab == null)
        {
            return this.services.GetRequiredService<IFileSystemProvider>();
        }
        return tab.FileSystemProvider;
    }

    /// <summary>保存设置并让全部文件标签页按新设置刷新；网页端服务的开关与端口在此重新应用。</summary>
    public void SaveSettingsAndApply()
    {
        this.settingsStore.Save(this.Settings);
        UiLogger.SetMinimumLevel(UiLogger.ParseLevel(this.Settings.LogLevel, LogLevel.Info));
        int globalShortcutCount = 0;
        int shortcutCount = 0;
        if (this.Settings.Shortcuts != null)
        {
            shortcutCount = this.Settings.Shortcuts.Count;
            foreach (HotkeySetting shortcut in this.Settings.Shortcuts)
            {
                if (shortcut != null && shortcut.IsGlobal)
                {
                    globalShortcutCount++;
                }
            }
        }
        UiLogger.Log("配置更改", "保存设置并应用：隐藏文件=" + this.Settings.ShowHiddenFiles
            + " 显示扩展名=" + this.Settings.ShowFileExtensions
            + " 双击标签关闭=" + this.Settings.DoubleClickTabClose
            + " 关闭到托盘=" + this.Settings.CloseToTray
            + " 显示工具栏=" + this.Settings.ShowToolbar
            + " 显示书签栏=" + this.Settings.ShowBookmarkBar
            + " 顶部白色条=" + this.Settings.ShowTopWhiteStrip
            + " 界面主题=" + this.Settings.Theme
            + " 开机自启=" + this.Settings.AutoStart
            + " 网页端服务开关=" + this.Settings.EnableWebService
            + " 网页端服务端口=" + this.Settings.WebServicePort
            + " 恢复上次标签=" + this.Settings.RestoreLastTabs
            + " 默认打开模式=" + this.Settings.DefaultOpenMode
            + " 默认文件管理器=" + this.Settings.DefaultFileManager
            + " 编辑器字号=" + this.Settings.EditorFontSize
            + " 编辑器Tab宽度=" + this.Settings.EditorTabWidth
            + " 快捷访问固定数=" + this.Settings.PinnedPaths.Count
            + " 打开方式规则数=" + (this.Settings.FileOpenRules != null ? this.Settings.FileOpenRules.Count : 0)
            + " 快捷键条数=" + shortcutCount
            + " 全局快捷键条数=" + globalShortcutCount
            + " 截图保存目录=" + CapturePaths.ResolveOutputDirectory(this.Settings)
            + " 截图复制到剪贴板=" + this.Settings.CaptureCopyToClipboard
            + " 完成后打开产物文件夹=" + this.Settings.CaptureOpenFolderAfter
            + " 截图包含指针=" + this.Settings.CaptureIncludeCursor
            + " 截图延迟秒数=" + this.Settings.CaptureDelaySeconds
            + " 录屏帧率=" + this.Settings.CaptureRecordFps
            + " 录屏画质=" + this.Settings.CaptureRecordQuality
            + " FFmpeg路径=" + (string.IsNullOrEmpty(this.Settings.FfmpegPath) ? "自动查找" : this.Settings.FfmpegPath));
        this.SyncViewSettings();
        this.ApplyWebServiceSettings();
        foreach (object tab in this.Tabs)
        {
            if (tab is FileTabViewModel fileTab)
            {
                fileTab.Refresh();
            }
            else if (tab is CaptureTabViewModel captureTab)
            {
                // 设置窗口可能修改了保存目录、FFmpeg 路径或产物选项，立即刷新截图录屏标签页
                captureTab.RefreshEnvironment();
                captureTab.RefreshHistory();
            }
        }
    }

    /// <summary>保存全部窗口打开的标签页路径（任一窗口关闭时调用，跨窗口取并集避免丢标签）。</summary>
    public void SaveOpenTabs()
    {
        this.Settings.OpenTabPaths.Clear();
        foreach (Views.MainWindow window in WindowRegistry.GetAllWindows())
        {
            if (!(window.DataContext is MainWindowViewModel viewModel))
            {
                continue;
            }
            foreach (object tab in viewModel.Tabs)
            {
                if (tab is FileTabViewModel fileTab)
                {
                    this.Settings.OpenTabPaths.Add(fileTab.CurrentPath);
                }
            }
        }
        this.settingsStore.Save(this.Settings);
        UiLogger.Log("配置更改", "会话保存：记录打开的标签数量=" + this.Settings.OpenTabPaths.Count + " 参与保存的窗口数量=" + WindowRegistry.GetAllWindows().Count);
    }

    /// <summary>接入网页端服务并按当前设置决定是否随程序启动。</summary>
    public void AttachDebugWebHost(WebServiceHost host)
    {
        this.debugWebHost = host;
        this.ApplyWebServiceSettings();
    }

    /// <summary>
    /// 按设置启动或停止网页端服务：逐个端口解析权限方案并监听，任意一个端口失败都不影响其他端口。
    /// 状态栏显示全部监听地址，失败原因写入日志。设置变化后由 SaveSettingsAndApply 调用。
    /// </summary>
    public void ApplyWebServiceSettings()
    {
        WebServiceHost? host = this.debugWebHost;
        if (host == null)
        {
            return;
        }

        List<WebPortRuntime> runtimes = new List<WebPortRuntime>();
        this.CleanupWebWorkspaces();
        foreach (WebPortSetting portSetting in this.Settings.WebPorts)
        {
            if (!portSetting.Enabled)
            {
                continue;
            }
            if (portSetting.Port < 1024 || portSetting.Port > 65535)
            {
                UiLogger.Log("网页端服务", "端口非法已跳过：配置端口=" + portSetting.Port + " 方案=" + portSetting.ProfileName);
                continue;
            }
            WebAccessProfile? profile = AppSettingsStore.FindProfile(this.Settings, portSetting.ProfileName);
            if (profile == null)
            {
                UiLogger.Log("网页端服务", "端口缺少可用权限方案已跳过：端口=" + portSetting.Port + " 方案=" + portSetting.ProfileName);
                continue;
            }
            WebPortRuntime runtime = new WebPortRuntime();
            runtime.Port = portSetting.Port;
            runtime.BindScope = portSetting.BindAddress;
            runtime.Profile = profile;
            runtimes.Add(runtime);
        }

        if (runtimes.Count == 0)
        {
            if (this.webServiceRunning)
            {
                host.Stop();
                this.webServiceRunning = false;
            }
            UiLogger.Log("网页端服务", "保持停止：没有启用的端口配置");
            this.SetWebServiceStatus("网页端服务：未启动");
            return;
        }

        host.Start(runtimes, this, this.Settings);
        this.webServiceRunning = host.ListenUrls.Count > 0;
        this.webServicePort = runtimes[0].Port;

        foreach (string failure in host.Failures)
        {
            UiLogger.Log("网页端服务", "启动失败：" + failure);
        }
        foreach (WebPortRuntime runtime in runtimes)
        {
            UiLogger.Log("网页端服务", "端口配置：端口=" + runtime.Port + " 监听范围=" + runtime.BindScope
                + " 权限方案=" + runtime.Profile.Name + " 允许修改=" + runtime.Profile.AllowModify
                + " 允许增删=" + runtime.Profile.AllowCreateDelete + " 需要密码=" + runtime.Profile.RequirePassword
                + " 来源白名单=" + this.DescribeList(runtime.Profile.AllowedAddresses)
                + " 路径白名单=" + this.DescribeList(runtime.Profile.AllowedPaths));
        }

        if (host.ListenUrls.Count > 0)
        {
            string joined = string.Join(" ", host.ListenUrls);
            this.SetWebServiceStatus("网页端服务：" + joined);
            UiLogger.Log("网页端服务", "启动成功：监听地址=" + joined + " 端口数量=" + host.ListenUrls.Count
                + " 失败端口数=" + host.Failures.Count);
        }
        else
        {
            this.SetWebServiceStatus("网页端服务：未启动（端口不可用）");
            UiLogger.Log("网页端服务", "启动失败：全部端口均不可用 端口数量=" + runtimes.Count);
        }
    }

    /// <summary>把清单拼成日志文本；空清单写明“不限制”。</summary>
    private string DescribeList(List<string> items)
    {
        if (items == null || items.Count == 0)
        {
            return "不限制";
        }
        return string.Join(",", items);
    }

    /// <summary>清理超过 30 分钟没有访问的独立工作区，避免长期占用内存。</summary>
    private void CleanupWebWorkspaces()
    {
        DateTime deadline = DateTime.Now.AddMinutes(-30);
        List<string> expired = new List<string>();
        foreach (KeyValuePair<string, WebWorkspace> pair in this.webWorkspaces)
        {
            if (pair.Value.LastAccessTime < deadline)
            {
                expired.Add(pair.Key);
            }
        }
        foreach (string id in expired)
        {
            // 回收前关闭工作区内的磁盘视图标签：退订扫描事件并停止台前探测
            if (this.webWorkspaces.TryGetValue(id, out WebWorkspace? expiredWorkspace))
            {
                expiredWorkspace.ReleaseTabs();
            }
            this.webWorkspaces.Remove(id);
            UiLogger.Log("网页端工作区", "回收闲置工作区：工作区=" + id);
        }
    }

    /// <summary>读取跟随桌面模式的状态快照（跨线程安全）。</summary>
    public Task<DebugUiState> GetDesktopStateAsync()
    {
        return this.DispatchDebugSnapshotAsync();
    }

    /// <summary>读取文件列表列配置：工作区编号为空时取桌面共享设置（跨线程安全）。</summary>
    public FileColumnConfig GetFileColumnConfig(string workspaceId)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            WebWorkspace? workspace;
            if (this.webWorkspaces.TryGetValue(workspaceId, out workspace))
            {
                return workspace.GetFileColumnConfig();
            }
            return new FileColumnConfig();
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return this.BuildDesktopFileColumnConfig();
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            new Func<FileColumnConfig>(this.BuildDesktopFileColumnConfig)).GetTask().GetAwaiter().GetResult();
    }

    private FileColumnConfig BuildDesktopFileColumnConfig()
    {
        FileTabViewModel? tab = this.SelectedFileTab;
        return new FileColumnConfig
        {
            VisibleColumns = this.Settings.FileVisibleColumns,
            ColumnWidths = tab != null ? tab.ColumnWidths : this.Settings.FileColumnWidths,
            CompactMode = this.Settings.FileCompactMode,
            SortField = tab != null ? tab.SortField : this.Settings.FileSortField,
            SortAscending = tab != null ? tab.SortAscending : this.Settings.FileSortAscending,
        };
    }

    /// <summary>应用文件列表列配置：工作区编号为空时应用到桌面全部文件标签并保存一次（跨线程安全）。</summary>
    public void ApplyFileColumnConfig(string workspaceId, FileColumnConfig config)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            WebWorkspace? workspace;
            if (this.webWorkspaces.TryGetValue(workspaceId, out workspace))
            {
                workspace.ApplyFileColumnConfig(config);
            }
            return;
        }
        if (config == null)
        {
            return;
        }
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                new Action(delegate { this.ApplyDesktopFileColumnConfig(config); })).GetTask().GetAwaiter().GetResult();
            return;
        }
        this.ApplyDesktopFileColumnConfig(config);
    }

    private void ApplyDesktopFileColumnConfig(FileColumnConfig config)
    {
        this.Settings.FileVisibleColumns = config.VisibleColumns;
        this.Settings.FileColumnWidths = config.ColumnWidths;
        this.Settings.FileCompactMode = config.CompactMode;
        this.Settings.FileSortField = config.SortField;
        this.Settings.FileSortAscending = config.SortAscending;
        foreach (object tab in this.Tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab == null)
            {
                continue;
            }
            fileTab.ApplyColumnConfiguration(config.VisibleColumns, config.ColumnWidths, config.CompactMode, false);
            fileTab.ApplySortConfiguration(config.SortField, config.SortAscending, false);
        }
        this.settingsStore.Save(this.Settings);
        UiLogger.Log("文件列表", "应用网页端列配置：可见列=" + config.VisibleColumns + " 列宽=" + config.ColumnWidths
            + " 紧凑=" + (config.CompactMode ? "开" : "关") + " 排序=" + config.SortField
            + (config.SortAscending ? " 升序" : " 降序"));
    }

    /// <summary>读取统一任务快照：工作区编号为空时取桌面本地任务与远程传输映射（跨线程安全）。</summary>
    public List<FileTaskSnapshot> GetFileTasks(string workspaceId)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            WebWorkspace? workspace;
            if (this.webWorkspaces.TryGetValue(workspaceId, out workspace))
            {
                return workspace.GetFileTasks();
            }
            return new List<FileTaskSnapshot>();
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return this.BuildDesktopFileTasks();
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            new Func<List<FileTaskSnapshot>>(this.BuildDesktopFileTasks)).GetTask().GetAwaiter().GetResult();
    }

    private List<FileTaskSnapshot> BuildDesktopFileTasks()
    {
        List<FileTaskSnapshot> result = new List<FileTaskSnapshot>(this.fileTaskCenter.Snapshot());
        result.AddRange(RemoteTaskAdapter.Snapshot(this.Tabs));
        return result;
    }

    /// <summary>本地任务完成后刷新桌面文件标签，避免任务中心异步落地后列表仍是旧内容。</summary>
    private void EnsureTaskRefreshSubscription()
    {
        if (this.taskRefreshSubscribed)
        {
            return;
        }
        this.taskRefreshSubscribed = true;
        this.fileTaskCenter.TaskFinished += delegate(FileTaskSnapshot snapshot)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(delegate
            {
                this.RefreshFileTabsAfterTask();
                this.ShowTaskFinishedNotice(snapshot);
            });
        };
        this.fileTaskCenter.ConflictPending += delegate(FileTaskSnapshot snapshot)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(delegate { this.ShowTaskConflictNotice(snapshot); });
        };
    }

    /// <summary>任务等待冲突选择时弹出桌面对话框（同名文件二选一 / 同名文件夹四选一，可本批统一）。</summary>
    private async void ShowTaskConflictNotice(FileTaskSnapshot snapshot)
    {
        string name = string.IsNullOrEmpty(snapshot.ConflictName) ? snapshot.ConflictPath : snapshot.ConflictName;
        string text = snapshot.ConflictIsDirectory
            ? "目标已存在同名文件夹：" + name
                + "。替换=清空目标后写入源内容（删除走回收站）；逐项处理会逐个询问文件夹内同名项；"
                + "合并会保留两边内容；改名共存让两边都保留。关闭窗口=取消任务，不会自动跳过。"
            : "目标已存在同名文件：" + name
                + "。替换会删除目标文件（走回收站）后写入源文件；改名共存让两边都保留。"
                + "关闭窗口=取消任务，不会自动跳过。";
        FileTabViewModel? tab = this.SelectedFileTab;
        if (tab != null)
        {
            tab.ShowMessage(text);
        }
        UiLogger.Log("任务中心", text + " 任务=" + snapshot.Id);
        (FileConflictAction Action, bool ApplyToAll) result;
        UiWatchdog.EnterStage("冲突对话框");
        try
        {
            result = await Views.MessageDialog.AskTaskConflictAsync(this.OwnerWindow, "任务冲突", text,
                snapshot.ConflictIsDirectory);
        }
        finally
        {
            UiWatchdog.LeaveStage("冲突对话框");
        }
        bool resolved = this.fileTaskCenter.ResolveConflict(snapshot.Id, result.Action.ToString(), result.ApplyToAll);
        UiLogger.Log("任务中心", "冲突选择写回：任务=" + snapshot.Id + " 选择=" + result.Action
            + " 本批统一=" + result.ApplyToAll + " 结果=" + resolved);
    }

    /// <summary>任务失败时在底部摘要旁暴露「重试失败项」，避免用户以为已经处理完成。</summary>
    private void ShowTaskFinishedNotice(FileTaskSnapshot snapshot)
    {
        if (snapshot.Failed <= 0)
        {
            return;
        }
        string text = "任务失败 " + snapshot.Failed + " 项：" + snapshot.Message + "；可点右侧「重试失败项」重新提交。";
        FileTabViewModel? tab = this.SelectedFileTab;
        if (tab != null)
        {
            tab.ShowMessage(text);
            tab.SetFailedTask(string.Empty, snapshot.Id);
        }
        UiLogger.Log("任务中心", text + " 任务=" + snapshot.Id);
    }

    private void RefreshFileTabsAfterTask()
    {
        foreach (object tab in this.Tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null)
            {
                fileTab.Refresh();
            }
        }
    }

    /// <summary>把共享排序设置同步到所有桌面文件标签（仅在设置与标签不一致时重排）。</summary>
    private void ApplySharedSortToFileTabs()
    {
        string field = this.Settings.FileSortField;
        if (field != "Name" && field != "Modified" && field != "Type" && field != "Size" && field != "Attributes")
        {
            field = "Name";
        }
        foreach (object tab in this.Tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null && (fileTab.SortField != field || fileTab.SortAscending != this.Settings.FileSortAscending))
            {
                fileTab.ApplySortConfiguration(field, this.Settings.FileSortAscending, false);
            }
        }
    }

    /// <summary>提交本地任务；工作区编号为空时进入桌面任务中心。</summary>
    public string StartFileTask(string workspaceId, FileTaskRequest request)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            WebWorkspace? workspace;
            if (this.webWorkspaces.TryGetValue(workspaceId, out workspace))
            {
                return workspace.StartFileTask(request);
            }
            return string.Empty;
        }
        this.EnsureTaskRefreshSubscription();
        return this.fileTaskCenter.Submit(request);
    }

    /// <summary>取消任务：本地任务优先，其次映射远程队列。</summary>
    public bool CancelFileTask(string workspaceId, string id)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            WebWorkspace? workspace;
            return this.webWorkspaces.TryGetValue(workspaceId, out workspace) && workspace.CancelFileTask(id);
        }
        if (this.fileTaskCenter.Cancel(id))
        {
            return true;
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return RemoteTaskAdapter.Cancel(this.Tabs, id);
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            new Func<bool>(delegate { return RemoteTaskAdapter.Cancel(this.Tabs, id); })).GetTask().GetAwaiter().GetResult();
    }

    /// <summary>重试失败任务：本地任务新建重试任务，远程任务转发继续。</summary>
    public bool RetryFileTask(string workspaceId, string id)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            WebWorkspace? workspace;
            return this.webWorkspaces.TryGetValue(workspaceId, out workspace) && workspace.RetryFileTask(id);
        }
        if (this.fileTaskCenter.Retry(id))
        {
            return true;
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return RemoteTaskAdapter.Retry(this.Tabs, id);
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            new Func<bool>(delegate { return RemoteTaskAdapter.Retry(this.Tabs, id); })).GetTask().GetAwaiter().GetResult();
    }

    /// <summary>处理冲突询问。</summary>
    public bool ResolveFileTaskConflict(string workspaceId, string id, string action, bool applyToAll)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            WebWorkspace? workspace;
            return this.webWorkspaces.TryGetValue(workspaceId, out workspace)
                && workspace.ResolveFileTaskConflict(id, action, applyToAll);
        }
        return this.fileTaskCenter.ResolveConflict(id, action, applyToAll);
    }

    /// <summary>读取桌面 IDE 指定编辑器的当前文本（跨线程安全；返回格式：令牌 + 换行 + 文本）。</summary>
    public string ReadDesktopIdeText(int index)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return this.IdeShell.ReadEditorText(index);
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            new Func<string>(delegate { return this.IdeShell.ReadEditorText(index); }))
            .GetTask().GetAwaiter().GetResult();
    }

    /// <summary>在桌面切换标签（跨线程安全）。</summary>
    public void SelectDesktopTab(int index)
    {
        this.SelectDebugTabOnUiThread(index);
    }

    /// <summary>在桌面的文件标签中打开路径（跨线程安全）。</summary>
    public void OpenDesktopPath(string path)
    {
        this.OpenPathOnUiThread(path);
    }

    /// <summary>应用界面主题（跨线程安全）。</summary>
    public void ApplyTheme(string theme)
    {
        this.SetThemeOnUiThread(theme);
    }

    /// <summary>执行网页端动作（跨线程安全）：调度到 UI 线程执行，返回给页面的提示文字。</summary>
    public string ExecuteDesktopAction(string action, string argument)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return this.ExecuteWebActionOnUiThread(action, argument);
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            new Func<string>(delegate { return this.ExecuteWebActionOnUiThread(action, argument); }))
            .GetTask().GetAwaiter().GetResult();
    }

    /// <summary>网页端读取截图状态：保存目录、命名模板、格式、开关与产物历史（JSON 文本）。</summary>
    public string GetCaptureStateJson()
    {
        AppSettings settings = this.Settings;
        string outputDirectory = CapturePaths.ResolveOutputDirectory(settings);
        string[] files = this.captureCoordinator.ListCaptureFiles();
        List<object> items = new List<object>();
        foreach (string file in files)
        {
            try
            {
                FileInfo info = new FileInfo(file);
                if (!info.Exists)
                {
                    continue;
                }
                items.Add(new
                {
                    fileName = info.Name,
                    fullPath = info.FullName,
                    sizeBytes = info.Length,
                    sizeText = CapturePaths.DescribeSize(info.Length),
                    modifiedTime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    thumbnailUrl = "/api/thumb?path=" + Uri.EscapeDataString(info.FullName),
                });
            }
            catch (Exception error)
            {
                UiLogger.LogError("网页端截图", "读取产物信息失败：路径=" + file, error);
            }
        }
        object payload = new
        {
            outputDirectory = outputDirectory,
            fileNameTemplate = CapturePaths.ScreenshotPrefix + "yyyyMMdd_HHmmss.png",
            format = "png",
            pinSave = settings.CapturePinShotSaveFile,
            copyToClipboard = settings.CaptureCopyToClipboard,
            openFolderAfter = settings.CaptureOpenFolderAfter,
            openEditorAfter = settings.CaptureOpenEditorAfter,
            count = items.Count,
            items = items,
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    /// <summary>网页端截图动作：fullscreen / region / pin 提交截图，pinSave 切换保存开关，delete 删除产物文件。</summary>
    public string ExecuteCaptureAction(string action, string argument)
    {
        if (string.Equals(action, "fullscreen", StringComparison.Ordinal)
            || string.Equals(action, "region", StringComparison.Ordinal)
            || string.Equals(action, "pin", StringComparison.Ordinal))
        {
            UiLogger.Log("网页端截图", "网页端提交截图任务：动作=" + action
                + " 延迟秒数=" + this.Settings.CaptureDelaySeconds);
            Avalonia.Threading.Dispatcher.UIThread.Post(delegate
            {
                _ = this.RunWebCaptureAsync(action);
            });
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = true,
                message = "截图任务已提交，请刷新状态查看新产物",
            });
        }
        if (string.Equals(action, "pinSave", StringComparison.Ordinal))
        {
            return this.SetCapturePinSaveFromWeb(argument);
        }
        if (string.Equals(action, "delete", StringComparison.Ordinal))
        {
            return this.DeleteCaptureProductFromWeb(argument);
        }
        if (string.Equals(action, "copy", StringComparison.Ordinal))
        {
            return this.CopyCaptureProductsFromWeb(argument);
        }
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = false,
            message = "未知截图动作：" + action,
        });
    }

    /// <summary>网页端切换「贴图截图保存产物」开关：参数 on / off 指定值，其余按当前值取反。</summary>
    private string SetCapturePinSaveFromWeb(string argument)
    {
        bool newValue = !this.Settings.CapturePinShotSaveFile;
        string mode = argument == null ? string.Empty : argument.Trim().ToLowerInvariant();
        if (mode == "on" || mode == "true" || mode == "1")
        {
            newValue = true;
        }
        else if (mode == "off" || mode == "false" || mode == "0")
        {
            newValue = false;
        }
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(new Action(delegate
        {
            this.Settings.CapturePinShotSaveFile = newValue;
            this.captureCoordinator.PersistSettings("网页端切换「贴图截图保存产物」开关");
        })).GetTask().GetAwaiter().GetResult();
        UiLogger.Log("网页端截图", "切换贴图截图保存开关：新值=" + (newValue ? "开启" : "关闭")
            + " 参数=" + mode + " 结果=已保存到设置");
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = true,
            pinSave = newValue,
            message = "贴图截图保存产物=" + (newValue ? "开启" : "关闭"),
        });
    }

    /// <summary>网页端删除一条产物：只允许删除产物目录内的截图 / 录屏文件。</summary>
    private string DeleteCaptureProductFromWeb(string argument)
    {
        string targetPath;
        try
        {
            targetPath = Path.GetFullPath(argument == null ? string.Empty : argument.Trim());
        }
        catch (Exception error)
        {
            UiLogger.LogError("网页端截图", "删除产物失败：路径非法 参数=" + argument, error);
            return System.Text.Json.JsonSerializer.Serialize(new { ok = false, message = "路径非法：" + argument });
        }
        string outputDirectory = Path.GetFullPath(CapturePaths.ResolveOutputDirectory(this.Settings));
        if (!targetPath.StartsWith(outputDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            UiLogger.Log("网页端截图", "拒绝删除：目标不在产物目录内 目标=" + targetPath + " 产物目录=" + outputDirectory);
            return System.Text.Json.JsonSerializer.Serialize(new { ok = false, message = "只允许删除产物目录内的文件" });
        }
        if (!CapturePaths.IsCaptureFile(targetPath))
        {
            UiLogger.Log("网页端截图", "拒绝删除：目标不是截图或录屏产物 目标=" + targetPath);
            return System.Text.Json.JsonSerializer.Serialize(new { ok = false, message = "目标不是截图或录屏产物" });
        }
        if (!File.Exists(targetPath))
        {
            UiLogger.Log("网页端截图", "删除产物失败：文件不存在 目标=" + targetPath);
            return System.Text.Json.JsonSerializer.Serialize(new { ok = false, message = "产物文件不存在" });
        }
        try
        {
            File.Delete(targetPath);
            UiLogger.Log("网页端截图", "删除产物成功：目标=" + targetPath);
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = true,
                message = "已删除：" + Path.GetFileName(targetPath),
            });
        }
        catch (Exception error)
        {
            UiLogger.LogError("网页端截图", "删除产物失败：目标=" + targetPath, error);
            return System.Text.Json.JsonSerializer.Serialize(new { ok = false, message = "删除失败：" + error.Message });
        }
    }

    /// <summary>
    /// 网页端显式复制：参数为可选文件路径列表（换行 / 竖线 / 分号分隔），留空复制最近一条产物；
    /// 沿用 PNG + CF_DIBV5 + CF_DIB + 文件列表的双格式写入，不会由截图流程自动触发。
    /// </summary>
    private string CopyCaptureProductsFromWeb(string argument)
    {
        List<string> paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(argument))
        {
            string[] separators = new string[] { "\n", "|", ";" };
            foreach (string line in argument.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string path = line.Trim();
                if (path.Length > 0)
                {
                    paths.Add(path);
                }
            }
        }
        if (paths.Count == 0)
        {
            string[] recentFiles = this.captureCoordinator.ListCaptureFiles();
            if (recentFiles.Length > 0)
            {
                paths.Add(recentFiles[0]);
            }
        }
        if (paths.Count == 0)
        {
            UiLogger.Log("网页端截图", "网页端显式复制被拒绝：没有可复制的产物");
            return System.Text.Json.JsonSerializer.Serialize(new { ok = false, message = "没有可复制的产物" });
        }
        UiLogger.Log("网页端截图", "网页端显式复制：文件数量=" + paths.Count + " 文件列表=" + string.Join("、", paths));
        CaptureOutcome outcome = this.captureCoordinator
            .CopyCaptureFilesToClipboardAsync(paths, "网页端显式复制").GetAwaiter().GetResult();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = outcome.Success,
            count = paths.Count,
            message = outcome.Message,
        });
    }

    /// <summary>在界面线程执行网页端提交的截图任务，完成后写日志供网页端刷新状态时对照。</summary>
    private async Task RunWebCaptureAsync(string action)
    {
        CaptureOutcome outcome;
        try
        {
            if (string.Equals(action, "region", StringComparison.Ordinal))
            {
                outcome = await this.captureCoordinator.CaptureRegionAsync(this.Settings.CaptureDelaySeconds);
            }
            else if (string.Equals(action, "pin", StringComparison.Ordinal))
            {
                outcome = await this.captureCoordinator.CapturePinRegionAsync(this.Settings.CaptureDelaySeconds);
            }
            else
            {
                outcome = await this.captureCoordinator.CaptureAsync(CaptureTarget.FullScreen,
                    this.Settings.CaptureDelaySeconds);
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("网页端截图", "网页端截图异常：动作=" + action, error);
            return;
        }
        UiLogger.Log("网页端截图", "网页端截图完成：动作=" + action
            + " 成功=" + outcome.Success
            + " 已取消=" + outcome.Cancelled
            + " 产物=" + (outcome.FilePath.Length == 0 ? "（无）" : outcome.FilePath)
            + " 说明=" + outcome.Message);
    }

    /// <summary>网页端读取设置：白名单取值 + 可编辑键 + 仅桌面分组的中文原因（JSON 文本）。</summary>
    public string GetSettingsJson()
    {
        AppSettings settings = this.Settings;
        Dictionary<string, object?> values = new Dictionary<string, object?>();
        values["theme"] = settings.Theme;
        values["showFileExtensions"] = settings.ShowFileExtensions;
        values["showHiddenFiles"] = settings.ShowHiddenFiles;
        values["fileCompactMode"] = settings.FileCompactMode;
        values["closeToTray"] = settings.CloseToTray;
        values["doubleClickTabClose"] = settings.DoubleClickTabClose;
        values["showToolbar"] = settings.ShowToolbar;
        values["showBookmarkBar"] = settings.ShowBookmarkBar;
        values["logLevel"] = settings.LogLevel;
        values["restoreLastTabs"] = settings.RestoreLastTabs;
        values["defaultOpenMode"] = settings.DefaultOpenMode;
        values["editorFontFamily"] = settings.EditorFontFamily;
        values["editorFontSize"] = settings.EditorFontSize;
        values["editorTabWidth"] = settings.EditorTabWidth;
        values["editorInsertSpaces"] = settings.EditorInsertSpaces;
        values["editorWordWrap"] = settings.EditorWordWrap;
        values["editorShowWhitespace"] = settings.EditorShowWhitespace;
        values["capturePinShotSaveFile"] = settings.CapturePinShotSaveFile;
        values["captureOpenFolderAfter"] = settings.CaptureOpenFolderAfter;
        values["captureOpenEditorAfter"] = settings.CaptureOpenEditorAfter;
        values["captureIncludeCursor"] = settings.CaptureIncludeCursor;
        values["captureDelaySeconds"] = settings.CaptureDelaySeconds;
        values["captureBackend"] = settings.CaptureBackend;

        Dictionary<string, string> locked = new Dictionary<string, string>();
        locked["shortcuts"] = "快捷键在桌面端配置，网页端只读展示";
        locked["web"] = "网页服务地址与端口需在桌面端修改并重启服务";
        locked["startup"] = "启动项需要写入系统注册表 / 启动目录，仅桌面端可用";
        locked["openwith"] = "默认打开方式需要系统文件关联，仅桌面端可用";
        locked["media"] = "播放器设置由桌面内置播放器读取，网页端不使用";
        locked["shellmenu"] = "系统右键菜单项仅桌面端可枚举与启停";

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            values = values,
            editable = new List<string>(values.Keys),
            locked = locked,
        });
    }

    /// <summary>网页端应用设置：逐键白名单校验，通过项在 UI 线程赋值并保存，返回 applied / rejected。</summary>
    public string ApplySettingsJson(string json)
    {
        List<string> applied = new List<string>();
        List<object> rejected = new List<object>();
        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
            System.Text.Json.JsonElement root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    ok = false,
                    message = "设置格式不正确",
                    applied = applied,
                    rejected = rejected,
                });
            }
            System.Text.Json.JsonElement valuesElement = root;
            if (root.TryGetProperty("values", out System.Text.Json.JsonElement nested)
                && nested.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                valuesElement = nested;
            }

            Action apply = delegate
            {
                this.ApplySettingsValues(valuesElement, applied, rejected);
            };
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                apply();
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(apply).GetTask().GetAwaiter().GetResult();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = false,
                message = "设置格式不正确",
                applied = applied,
                rejected = rejected,
            });
        }

        bool ok = rejected.Count == 0 || applied.Count > 0;
        string message = applied.Count == 0 && rejected.Count == 0
            ? "没有可应用的设置项"
            : "已应用 " + applied.Count + " 项设置"
                + (rejected.Count > 0 ? "，忽略 " + rejected.Count + " 项" : string.Empty);
        UiLogger.Log("配置更改", "网页端应用设置：成功项=" + string.Join("、", applied)
            + " 忽略项=" + rejected.Count);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = ok,
            message = message,
            applied = applied,
            rejected = rejected,
        });
    }

    /// <summary>在 UI 线程按白名单把网页提交的设置写入 AppSettings（必须在 UI 线程调用）。</summary>
    private void ApplySettingsValues(System.Text.Json.JsonElement values, List<string> applied, List<object> rejected)
    {
        AppSettings settings = this.Settings;
        bool themeChanged = false;
        foreach (System.Text.Json.JsonProperty property in values.EnumerateObject())
        {
            string key = property.Name;
            System.Text.Json.JsonElement value = property.Value;
            switch (key)
            {
                case "theme":
                    {
                        string? theme = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
                        if (theme == "Dark" || theme == "Light")
                        {
                            settings.Theme = theme;
                            themeChanged = true;
                            applied.Add(key);
                        }
                        else
                        {
                            rejected.Add(new { key = key, reason = "主题只支持 Dark 或 Light" });
                        }
                        break;
                    }
                case "showFileExtensions":
                case "showHiddenFiles":
                case "fileCompactMode":
                case "closeToTray":
                case "doubleClickTabClose":
                case "showToolbar":
                case "showBookmarkBar":
                case "restoreLastTabs":
                case "editorInsertSpaces":
                case "editorWordWrap":
                case "editorShowWhitespace":
                case "capturePinShotSaveFile":
                case "captureOpenFolderAfter":
                case "captureOpenEditorAfter":
                case "captureIncludeCursor":
                    {
                        if (value.ValueKind != System.Text.Json.JsonValueKind.True
                            && value.ValueKind != System.Text.Json.JsonValueKind.False)
                        {
                            rejected.Add(new { key = key, reason = "需要布尔值 true / false" });
                            break;
                        }
                        bool flag = value.GetBoolean();
                        if (key == "showFileExtensions") { settings.ShowFileExtensions = flag; }
                        else if (key == "showHiddenFiles") { settings.ShowHiddenFiles = flag; }
                        else if (key == "fileCompactMode") { settings.FileCompactMode = flag; }
                        else if (key == "closeToTray") { settings.CloseToTray = flag; }
                        else if (key == "doubleClickTabClose") { settings.DoubleClickTabClose = flag; }
                        else if (key == "showToolbar") { settings.ShowToolbar = flag; }
                        else if (key == "showBookmarkBar") { settings.ShowBookmarkBar = flag; }
                        else if (key == "restoreLastTabs") { settings.RestoreLastTabs = flag; }
                        else if (key == "editorInsertSpaces") { settings.EditorInsertSpaces = flag; }
                        else if (key == "editorWordWrap") { settings.EditorWordWrap = flag; }
                        else if (key == "editorShowWhitespace") { settings.EditorShowWhitespace = flag; }
                        else if (key == "capturePinShotSaveFile") { settings.CapturePinShotSaveFile = flag; }
                        else if (key == "captureOpenFolderAfter") { settings.CaptureOpenFolderAfter = flag; }
                        else if (key == "captureOpenEditorAfter") { settings.CaptureOpenEditorAfter = flag; }
                        else { settings.CaptureIncludeCursor = flag; }
                        applied.Add(key);
                        break;
                    }
                case "logLevel":
                    {
                        string? level = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
                        if (level == "Debug" || level == "Info" || level == "Warn" || level == "Error")
                        {
                            settings.LogLevel = level;
                            applied.Add(key);
                        }
                        else
                        {
                            rejected.Add(new { key = key, reason = "日志级别只支持 Debug / Info / Warn / Error" });
                        }
                        break;
                    }
                case "defaultOpenMode":
                    {
                        string? mode = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
                        if (mode == "Explorer" || mode == "Ide")
                        {
                            settings.DefaultOpenMode = mode;
                            applied.Add(key);
                        }
                        else
                        {
                            rejected.Add(new { key = key, reason = "默认打开模式只支持 Explorer 或 Ide" });
                        }
                        break;
                    }
                case "editorFontFamily":
                    {
                        string? family = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(family) && family.Length <= 200)
                        {
                            settings.EditorFontFamily = family;
                            applied.Add(key);
                        }
                        else
                        {
                            rejected.Add(new { key = key, reason = "字体名称不能为空且不超过 200 个字符" });
                        }
                        break;
                    }
                case "editorFontSize":
                    {
                        if (value.ValueKind != System.Text.Json.JsonValueKind.Number
                            || !value.TryGetDouble(out double fontSize)
                            || fontSize < 8
                            || fontSize > 48)
                        {
                            rejected.Add(new { key = key, reason = "编辑器字号需在 8 到 48 之间" });
                            break;
                        }
                        settings.EditorFontSize = fontSize;
                        applied.Add(key);
                        break;
                    }
                case "editorTabWidth":
                    {
                        if (value.ValueKind != System.Text.Json.JsonValueKind.Number
                            || !value.TryGetInt32(out int tabWidth)
                            || tabWidth < 1
                            || tabWidth > 8)
                        {
                            rejected.Add(new { key = key, reason = "制表宽度需在 1 到 8 之间" });
                            break;
                        }
                        settings.EditorTabWidth = tabWidth;
                        applied.Add(key);
                        break;
                    }
                case "captureDelaySeconds":
                    {
                        if (value.ValueKind != System.Text.Json.JsonValueKind.Number
                            || !value.TryGetInt32(out int delay)
                            || delay < 0
                            || delay > 30)
                        {
                            rejected.Add(new { key = key, reason = "截图延迟需在 0 到 30 秒之间" });
                            break;
                        }
                        settings.CaptureDelaySeconds = delay;
                        applied.Add(key);
                        break;
                    }
                case "captureBackend":
                    {
                        string? backend = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
                        if (backend == "auto" || backend == "wgc" || backend == "gdi")
                        {
                            settings.CaptureBackend = backend;
                            applied.Add(key);
                        }
                        else
                        {
                            rejected.Add(new { key = key, reason = "采集后端只支持 auto / wgc / gdi" });
                        }
                        break;
                    }
                default:
                    rejected.Add(new { key = key, reason = "未知设置项（网页端不开放）" });
                    break;
            }
        }

        if (themeChanged)
        {
            this.ApplyThemeValue(settings.Theme);
        }
        if (applied.Count > 0)
        {
            this.SaveSettingsAndApply();
        }
    }

    /// <summary>构建桌面当前标签的系统右键菜单层级（跨线程安全，网页端请求线程调用）。</summary>
    public WebContextMenuResult BuildDesktopContextMenu(IReadOnlyList<string> paths, bool isBackground)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return this.BuildDesktopContextMenuOnUiThread(paths, isBackground);
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            new Func<WebContextMenuResult>(delegate { return this.BuildDesktopContextMenuOnUiThread(paths, isBackground); }))
            .GetTask().GetAwaiter().GetResult();
    }

    /// <summary>执行桌面当前标签的一条系统外壳命令编号（跨线程安全，网页端请求线程调用）。</summary>
    public bool ExecuteDesktopContextMenuCommand(int commandId, IReadOnlyList<string> paths, bool isBackground)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return this.ExecuteDesktopContextMenuCommandOnUiThread(commandId, paths, isBackground);
        }
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            new Func<bool>(delegate { return this.ExecuteDesktopContextMenuCommandOnUiThread(commandId, paths, isBackground); }))
            .GetTask().GetAwaiter().GetResult();
    }

    /// <summary>在 UI 线程构建桌面右键菜单：校验目标必须落在桌面已打开标签内，远程与压缩包标签返回受限菜单。</summary>
    private WebContextMenuResult BuildDesktopContextMenuOnUiThread(IReadOnlyList<string> paths, bool isBackground)
    {
        List<string> directories = new List<string>();
        foreach (object tab in this.Tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null)
            {
                if (fileTab.FileSystemProvider is DefaultFileSystemProvider && !string.IsNullOrEmpty(fileTab.CurrentPath))
                {
                    directories.Add(fileTab.CurrentPath);
                }
                continue;
            }
            DiskViewPanelViewModel? diskTab = tab as DiskViewPanelViewModel;
            if (diskTab != null)
            {
                if (!string.IsNullOrEmpty(diskTab.RootPathInput))
                {
                    directories.Add(diskTab.RootPathInput);
                }
                foreach (DiskRow row in diskTab.Rows)
                {
                    if (row.IsDirectory && !string.IsNullOrEmpty(row.FullPath))
                    {
                        directories.Add(row.FullPath);
                    }
                }
            }
        }
        List<string> visibleItems = new List<string>();
        FileTabViewModel? selectedFileTab = this.SelectedFileTab;
        if (selectedFileTab != null)
        {
            foreach (FileRow row in selectedFileTab.VisibleRows)
            {
                visibleItems.Add(row.FullPath);
            }
        }
        DiskViewPanelViewModel? selectedDiskTab = this.SelectedTab as DiskViewPanelViewModel;
        if (selectedDiskTab != null)
        {
            foreach (DiskRow row in selectedDiskTab.Rows)
            {
                visibleItems.Add(row.FullPath);
            }
        }
        WebContextMenuPolicy.Validate(directories, visibleItems, paths, isBackground);
        bool matchedAnyTab = false;
        bool matchedLocalTab = false;
        string remoteOrArchiveNote = "当前标签是远程连接、压缩包或非本机位置，系统右键项不可用，仅保留本程序命令";
        foreach (object tab in this.Tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null && this.MatchesTabTarget(fileTab.CurrentPath, paths, isBackground))
            {
                matchedAnyTab = true;
                if (fileTab.FileSystemProvider is DefaultFileSystemProvider && fileTab.ContentAccess == null)
                {
                    matchedLocalTab = true;
                }
            }
            DiskViewPanelViewModel? diskTab = tab as DiskViewPanelViewModel;
            if (diskTab != null && this.MatchesDiskTabTarget(diskTab, paths, isBackground))
            {
                matchedAnyTab = true;
                matchedLocalTab = true;
            }
        }
        if (!matchedAnyTab)
        {
            UiLogger.Log("网页端右键菜单", "拒绝：桌面右键目标不在任何已打开标签内 目标=" + string.Join("、", paths)
                + " 结果=未枚举也未执行任何外壳命令");
            throw new UnauthorizedAccessException("右键目标不在当前已打开的标签内");
        }
        WebContextMenuResult result = WebContextMenuBuilder.Build(
            matchedLocalTab ? this.SystemContextMenus : null, paths, isBackground,
            matchedLocalTab ? string.Empty : remoteOrArchiveNote);
        UiLogger.Log("网页端右键菜单", "构建桌面右键菜单：目标数=" + paths.Count
            + " 落点=" + (isBackground ? "空白处" : "条目") + " 系统项可用=" + result.SystemMenuAvailable
            + " 顶层条目数=" + result.Items.Count + " 说明=" + (result.SystemMenuNote.Length == 0 ? "无" : result.SystemMenuNote));
        return result;
    }

    /// <summary>在 UI 线程校验并执行桌面系统外壳命令编号。</summary>
    private bool ExecuteDesktopContextMenuCommandOnUiThread(int commandId, IReadOnlyList<string> paths, bool isBackground)
    {
        List<string> directories = new List<string>();
        foreach (object tab in this.Tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null && fileTab.FileSystemProvider is DefaultFileSystemProvider && !string.IsNullOrEmpty(fileTab.CurrentPath))
            {
                directories.Add(fileTab.CurrentPath);
            }
            DiskViewPanelViewModel? diskTab = tab as DiskViewPanelViewModel;
            if (diskTab != null)
            {
                if (!string.IsNullOrEmpty(diskTab.RootPathInput))
                {
                    directories.Add(diskTab.RootPathInput);
                }
                foreach (DiskRow row in diskTab.Rows)
                {
                    if (row.IsDirectory && !string.IsNullOrEmpty(row.FullPath))
                    {
                        directories.Add(row.FullPath);
                    }
                }
            }
        }
        List<string> visibleItems = new List<string>();
        FileTabViewModel? selectedFileTab = this.SelectedFileTab;
        if (selectedFileTab != null)
        {
            foreach (FileRow row in selectedFileTab.VisibleRows)
            {
                visibleItems.Add(row.FullPath);
            }
        }
        DiskViewPanelViewModel? selectedDiskTab = this.SelectedTab as DiskViewPanelViewModel;
        if (selectedDiskTab != null)
        {
            foreach (DiskRow row in selectedDiskTab.Rows)
            {
                visibleItems.Add(row.FullPath);
            }
        }
        WebContextMenuPolicy.Validate(directories, visibleItems, paths, isBackground);
        bool success = this.SystemContextMenus.ExecuteShellMenuCommand(commandId, paths, isBackground);
        UiLogger.Log("网页端右键菜单", "执行桌面系统外壳命令：命令编号=" + commandId
            + " 落点=" + (isBackground ? "空白处" : "条目") + " 目标=" + string.Join("、", paths)
            + " 结果=" + (success ? "成功，已交给外壳扩展执行" : "失败，外壳扩展未接受该命令"));
        return success;
    }

    /// <summary>判断目标是否属于某个桌面文件标签目录。</summary>
    private bool MatchesTabTarget(string currentPath, IReadOnlyList<string> paths, bool isBackground)
    {
        string current = WebContextMenuPolicy.NormalizePath(currentPath);
        if (current.Length == 0)
        {
            return false;
        }
        foreach (string path in paths)
        {
            string target = WebContextMenuPolicy.NormalizePath(path);
            if (target.Length == 0)
            {
                continue;
            }
            if (isBackground)
            {
                if (string.Equals(target, current, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                continue;
            }
            string parent = WebContextMenuPolicy.NormalizePath(Path.GetDirectoryName(target) ?? string.Empty);
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>判断目标是否属于某个桌面磁盘视图标签的根目录或可见行。</summary>
    private bool MatchesDiskTabTarget(DiskViewPanelViewModel tab, IReadOnlyList<string> paths, bool isBackground)
    {
        string root = WebContextMenuPolicy.NormalizePath(tab.RootPathInput);
        foreach (string path in paths)
        {
            string target = WebContextMenuPolicy.NormalizePath(path);
            if (target.Length == 0)
            {
                continue;
            }
            if (isBackground)
            {
                if (root.Length > 0 && string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                continue;
            }
            foreach (DiskRow row in tab.Rows)
            {
                if (string.Equals(WebContextMenuPolicy.NormalizePath(row.FullPath), target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>在桌面当前文件标签中新建文件夹（跨线程安全，网页端请求线程调用）。</summary>
    public void CreateDesktopFolder(string folderName)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(delegate
        {
            FileTabViewModel? fileTab = this.SelectedFileTab;
            if (fileTab != null)
            {
                fileTab.CreateFolder(folderName);
            }
        });
    }

    /// <summary>重命名桌面当前文件标签中指定路径的条目（跨线程安全，网页端请求线程调用）。</summary>
    public void RenameDesktopEntry(string path, string newName)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(delegate
        {
            FileTabViewModel? fileTab = this.SelectedFileTab;
            if (fileTab == null)
            {
                return;
            }
            foreach (FileRow row in fileTab.VisibleRows)
            {
                if (row.FullPath == path)
                {
                    fileTab.RenameRow(row, newName);
                    return;
                }
            }
        });
    }

    /// <summary>删除桌面当前文件标签中指定路径的条目（移入回收站，跨线程安全，网页端请求线程调用）。</summary>
    public void DeleteDesktopEntry(string path)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(delegate
        {
            FileTabViewModel? fileTab = this.SelectedFileTab;
            if (fileTab == null)
            {
                return;
            }
            foreach (FileRow row in fileTab.VisibleRows)
            {
                if (row.FullPath == path)
                {
                    fileTab.DeleteRow(row);
                    return;
                }
            }
        });
    }

    /// <summary>把网页端编辑器内容写入指定文件（跨线程安全，网页端请求线程调用）。</summary>
    public void WriteDesktopFile(string path, byte[] content)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(delegate
        {
            try
            {
                FileTabViewModel? fileTab = this.SelectedFileTab;
                IFileContentAccess? contentAccess = fileTab == null ? null : fileTab.ContentAccess;
                if (contentAccess != null)
                {
                    contentAccess.WriteAllBytes(path, content);
                }
                else
                {
                    File.WriteAllBytes(path, content);
                }
                UiLogger.Log("网页端访问", "修改文件：模式=跟随桌面 目标=" + path + " 字节数=" + content.Length);
            }
            catch (Exception error)
            {
                UiLogger.Log("网页端访问", "修改文件失败：模式=跟随桌面 目标=" + path + " 原因=" + error.Message);
            }
        });
    }

    /// <summary>执行网页端比对动作（跟随桌面模式）：设置路径、切换选项、差异导航与进入子目录。</summary>
    private string ExecuteCompareAction(string action, string argument)
    {
        CompareTabViewModel? tab = this.SelectedTab as CompareTabViewModel;
        if (tab == null)
        {
            return "当前标签不是比对视图";
        }
        if (action == "compareapply")
        {
            List<string> paths = MainWindowViewModel.SplitPathLines(argument);
            string left = paths.Count > 0 ? paths[0] : string.Empty;
            string right = paths.Count > 1 ? paths[1] : string.Empty;
            tab.LeftPathInput = left;
            tab.RightPathInput = right;
            tab.Reload();
            return "已开始比对：左侧=" + left + " 右侧=" + right;
        }
        if (action == "comparetoggle")
        {
            if (argument == "onlydiff")
            {
                tab.ShowOnlyDifferences = !tab.ShowOnlyDifferences;
                return "仅显示差异=" + (tab.ShowOnlyDifferences ? "开" : "关");
            }
            if (argument == "subdirs")
            {
                tab.IncludeSubDirectories = !tab.IncludeSubDirectories;
                return "包含子目录=" + (tab.IncludeSubDirectories ? "开" : "关");
            }
            if (argument == "content")
            {
                tab.CompareFileContent = !tab.CompareFileContent;
                return "按内容比较=" + (tab.CompareFileContent ? "开" : "关");
            }
            return "无法识别的比对选项：" + argument;
        }
        if (action == "comparenext")
        {
            tab.SelectNextDifference();
            return tab.DifferencePositionText;
        }
        if (action == "compareprev")
        {
            tab.SelectPreviousDifference();
            return tab.DifferencePositionText;
        }
        if (action == "compareswap")
        {
            tab.SwapSides();
            return "已交换左右两侧并重新比对";
        }
        if (action == "compareup")
        {
            tab.NavigateUp();
            return "已返回上一层";
        }
        if (action == "compareopen")
        {
            int index;
            if (!int.TryParse(argument, out index))
            {
                return "缺少要打开的行号";
            }
            if (index < 0 || index >= tab.DirectoryRows.Count)
            {
                return "行号超出范围";
            }
            tab.OpenRow(tab.DirectoryRows[index]);
            return string.Empty;
        }
        return "未知比对动作：" + action;
    }

    /// <summary>执行网页端连接管理动作（跟随桌面模式）：选择、保存、删除、测试、一键导入、打开远程目录或终端。</summary>
    private string ExecuteConnectionAction(string action, string argument)
    {
        ConnectionManagerViewModel? tab = this.FindConnectionsTab();
        if (tab == null)
        {
            UiLogger.Log("网页端访问", "连接动作被忽略：桌面端没有连接管理标签 动作=" + action + " 参数=" + argument);
            return "请先在左侧导航中打开连接管理";
        }
        if (action == "connselect")
        {
            RemoteConnectionProfile? profile = WebWorkspace.FindConnection(tab, argument);
            if (profile == null)
            {
                return "没有找到该连接，可能已被删除";
            }
            tab.SelectedConnection = profile;
            return "已选择连接：" + profile.Name;
        }
        if (action == "connsave")
        {
            return WebWorkspace.SaveConnectionFromWeb(tab, argument);
        }
        if (action == "conndelete")
        {
            RemoteConnectionProfile? profile = WebWorkspace.FindConnection(tab, argument);
            if (profile == null)
            {
                return "没有找到要删除的连接";
            }
            tab.Delete(profile.Id);
            UiLogger.Log("网页端访问", "删除连接：名称=" + profile.Name + " 服务器=" + profile.Host);
            return "已删除连接：" + profile.Name;
        }
        if (action == "conntest")
        {
            RemoteConnectionProfile? profile = WebWorkspace.FindConnection(tab, argument);
            if (profile == null)
            {
                return "请先选择要测试的连接";
            }
            ConnectionTestResult result = tab.TestAsync(profile, TcpConnectivityTester.DefaultTimeoutMilliseconds)
                .GetAwaiter().GetResult();
            UiLogger.Log("网页端访问", "测试连接：名称=" + profile.Name + " 服务器=" + profile.Host
                + " 端口=" + profile.GetEffectivePort() + " 耗时=" + result.LatencyMilliseconds + " 毫秒 结果=" + result.Message);
            return (result.Success ? "测试成功：" : "测试失败：") + result.Message;
        }
        if (action == "connimport")
        {
            XshellImportSummary summary = tab.ImportInstalled();
            UiLogger.Log("网页端访问", "一键导入已安装配置：配置目录数=" + summary.RootsScanned.Count
                + " 新增=" + summary.Imported + " 更新=" + summary.Updated + " 跳过=" + summary.Skipped.Count
                + " 需补密码=" + summary.NeedsPassword.Count);
            if (summary.RootsScanned.Count == 0)
            {
                return "未发现 Xshell / Xftp 配置目录";
            }
            return "一键导入完成：新增 " + summary.Imported + " 个、更新 " + summary.Updated + " 个连接"
                + (summary.Skipped.Count > 0 ? "；跳过 " + summary.Skipped.Count + " 个" : string.Empty)
                + (summary.NeedsPassword.Count > 0
                    ? "；其中 " + summary.NeedsPassword.Count + " 个连接未包含密码（Xftp 密码为自有加密格式），请在连接管理中重新输入并保存"
                    : string.Empty);
        }
        if (action == "connopenfolder")
        {
            RemoteConnectionProfile? profile = WebWorkspace.FindConnection(tab, argument);
            if (profile == null)
            {
                return "请先选择要打开的连接";
            }
            this.OpenRemoteFolderTab(profile);
            return "已请求在桌面端打开远程文件夹标签：" + profile.Name;
        }
        if (action == "connopenterminal")
        {
            RemoteConnectionProfile? profile = WebWorkspace.FindConnection(tab, argument);
            if (profile == null)
            {
                return "请先选择要打开的连接";
            }
            this.OpenTerminalTab(profile);
            return "已请求在桌面端打开终端标签：" + profile.Name;
        }
        return "未知动作：" + action;
    }

    /// <summary>查找桌面端已有的连接管理标签；没有返回 null。</summary>
    private ConnectionManagerViewModel? FindConnectionsTab()
    {
        foreach (object tab in this.Tabs)
        {
            if (tab is ConnectionManagerViewModel connections)
            {
                return connections;
            }
        }
        return null;
    }

    /// <summary>在 UI 线程执行网页端动作（必须在 UI 线程调用），返回给页面的提示文字，空文本表示无提示。</summary>
    private string ExecuteWebActionOnUiThread(string action, string argument)
    {
        FileTabViewModel? fileTab = this.SelectedFileTab;
        DiskViewPanelViewModel? diskTab = this.SelectedTab as DiskViewPanelViewModel;

        if (action.StartsWith("disk", StringComparison.Ordinal))
        {
            return this.ExecuteDiskAction(action, argument, diskTab);
        }
        if (action == "openinmanager")
        {
            List<string> targetLines = WebWorkspace.SplitPathLines(argument);
            string path = targetLines.Count > 0 ? targetLines[0] : string.Empty;
            if (path.Length == 0)
            {
                return "在资源管理器打开失败：缺少目标路径";
            }
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return "在资源管理器打开失败：目标不存在 " + path;
            }
            bool allowed = false;
            if (diskTab != null)
            {
                if (string.Equals(WebContextMenuPolicy.NormalizePath(path),
                    WebContextMenuPolicy.NormalizePath(diskTab.RootPathInput), StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                }
                foreach (DiskRow row in diskTab.Rows)
                {
                    if (string.Equals(WebContextMenuPolicy.NormalizePath(row.FullPath),
                        WebContextMenuPolicy.NormalizePath(path), StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }
            }
            if (fileTab != null)
            {
                foreach (FileRow row in fileTab.VisibleRows)
                {
                    if (string.Equals(WebContextMenuPolicy.NormalizePath(row.FullPath),
                        WebContextMenuPolicy.NormalizePath(path), StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }
            }
            if (!allowed)
            {
                return "在资源管理器打开失败：目标不在当前标签内";
            }
            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + path + "\"", UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
                }
                UiLogger.Log("网页端访问", "在资源管理器打开：目标=" + path + " 结果=已调用资源管理器");
                return "已在资源管理器中打开：" + path;
            }
            catch (Exception error)
            {
                UiLogger.LogError("网页端访问", "在资源管理器打开失败：目标=" + path, error);
                return "在资源管理器打开失败：" + error.Message;
            }
        }



        if (action.StartsWith("ide", StringComparison.Ordinal))
        {
            if (action == "ide")
            {
                this.ToggleIdeMode();
                return this.IsIdeMode ? "已切换到 IDE 模式" : "已切换到资源管理器模式";
            }
            return this.IdeShell.ExecuteIdeAction(action, argument);
        }
        if (action == "sort")
        {
            if (fileTab != null)
            {
                fileTab.SetSortField(argument);
                return "已按 " + argument + (fileTab.SortAscending ? " 升序" : " 降序") + " 排列";
            }
            if (diskTab != null)
            {
                diskTab.SetSortField(argument);
                return "已按 " + argument + (diskTab.SortAscending ? " 升序" : " 降序") + " 排列";
            }
            ProcessPanelViewModel? sortProcessTab = this.SelectedTab as ProcessPanelViewModel;
            if (sortProcessTab != null)
            {
                sortProcessTab.SetSortField(argument);
                return "已按 " + argument + (sortProcessTab.SortAscending ? " 升序" : " 降序") + " 排列";
            }
            SearchTabViewModel? sortSearchTab = this.SelectedTab as SearchTabViewModel;
            if (sortSearchTab != null)
            {
                sortSearchTab.SetSortField(argument);
                return "已按 " + argument + (sortSearchTab.SortAscending ? " 升序" : " 降序") + " 排列";
            }
            return "当前标签不支持排序";
        }
        if (action == "back")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            fileTab.GoBack();
            return string.Empty;
        }
        if (action == "forward")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            fileTab.GoForward();
            return string.Empty;
        }
        if (action == "refresh")
        {
            if (fileTab != null)
            {
                fileTab.Refresh();
                return "已刷新：" + (string.IsNullOrEmpty(fileTab.CurrentPath) ? "此电脑" : fileTab.CurrentPath);
            }
            if (diskTab != null)
            {
                diskTab.StartFullRefresh();
                return "已完全重扫磁盘视图";
            }
            return "当前标签不支持刷新";
        }
        if (action == "search")
        {
            SearchTabViewModel? searchTab = this.SelectedTab as SearchTabViewModel;
            if (searchTab != null)
            {
                searchTab.QueryText = argument;
                searchTab.ExecuteSearch();
                return "全局搜索关键字=" + argument;
            }
            if (fileTab == null) { return "当前标签不是文件列表"; }
            fileTab.SearchText = argument;
            return string.IsNullOrEmpty(argument) ? "已清空搜索" : "搜索关键字=" + argument;
        }
        if (action == "viewmode")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            FileViewMode mode;
            if (!Enum.TryParse(argument, out mode))
            {
                return "无法识别的视图模式：" + argument;
            }
            fileTab.SetViewMode(mode);
            return "视图模式=" + argument;
        }
        if (action == "infopane")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            fileTab.ShowInfoPane = argument == "1";
            return string.Empty;
        }
        if (action == "checkset")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string> checkedPaths = MainWindowViewModel.SplitPathLines(argument);
            fileTab.SetCheckedPaths(checkedPaths);
            return "已勾选 " + checkedPaths.Count + " 项";
        }
        if (action == "checkall")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            fileTab.SelectAll();
            return "已全选 " + fileTab.VisibleRows.Count + " 项";
        }
        if (action == "checkclear")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            fileTab.ClearChecked();
            return "已取消全部勾选";
        }
        if (action == "copy")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string> copyPaths = fileTab.GetCheckedPaths();
            if (copyPaths.Count == 0) { return "请先勾选要复制的条目"; }
            this.CopyPathsToClipboard(copyPaths);
            return "已复制 " + copyPaths.Count + " 项";
        }
        if (action == "cut")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string> cutPaths = fileTab.GetCheckedPaths();
            if (cutPaths.Count == 0) { return "请先勾选要剪切的条目"; }
            this.CutPathsToClipboard(cutPaths);
            return "已剪切 " + cutPaths.Count + " 项";
        }
        if (action == "paste")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            // 这里已在界面线程：不能同步等待异步粘贴（后续续体需要回到界面线程，会自锁死）
            _ = this.PasteFromClipboardAsync(null);
            return "已请求粘贴，完成后自动刷新";
        }
        if (action == "pasteto")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            if (string.IsNullOrEmpty(argument)) { return "未指定粘贴目标文件夹"; }
            // 同上：异步粘贴不能在界面线程同步等待
            _ = this.PasteFromClipboardAsync(argument);
            return "已请求粘贴到指定目录，完成后自动刷新";
        }
        if (action == "deletechecked")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string> deletePaths = fileTab.GetCheckedPaths();
            if (deletePaths.Count == 0) { return "请先勾选要删除的条目"; }
            this.EnsureTaskRefreshSubscription();
            string deleteTaskId = this.fileTaskCenter.Submit(new TabTaskExplorer.App.Services.Tasks.FileTaskRequest
            {
                Kind = TabTaskExplorer.App.Services.Tasks.FileTaskKind.Delete,
                Sources = deletePaths,
                ConflictPolicy = TabTaskExplorer.App.Services.Tasks.FileConflictPolicy.Skip,
            });
            if (string.IsNullOrEmpty(deleteTaskId))
            {
                fileTab.DeleteChecked();
                return "已把勾选条目移入回收站";
            }
            return "已提交删除任务：共 " + deletePaths.Count + " 项，可在任务中心查看进度";
        }
        if (action == "batchrename")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string[]> renamePlan = MainWindowViewModel.ParseRenamePlan(argument);
            if (renamePlan.Count == 0) { return "没有需要改名的条目"; }
            fileTab.ApplyBatchRenames(renamePlan);
            return "批量改名已提交，计划条目数=" + renamePlan.Count;
        }
        if (action == "encodepreview")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            int fromCodePage;
            int toCodePage;
            bool recursive;
            List<string> encodePaths = MainWindowViewModel.ParseEncodePreviewRequest(argument,
                out fromCodePage, out toCodePage, out recursive);
            if (encodePaths.Count == 0) { return "没有要转换的路径"; }
            List<EncodeConvertEntry> entries = fileTab.PreviewEncodeConvert(
                encodePaths, recursive, fromCodePage, toCodePage);
            StringBuilder previewText = new StringBuilder();
            foreach (EncodeConvertEntry entry in entries)
            {
                previewText.Append(entry.OldPath).Append('\t')
                    .Append(entry.NewName).Append('\t').Append(entry.Reason).Append('\n');
            }
            return previewText.ToString();
        }
        if (action == "encodeapply")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string[]> convertPlan = MainWindowViewModel.ParseRenamePlan(argument);
            if (convertPlan.Count == 0) { return "没有需要转换的条目"; }
            fileTab.ApplyBatchRenames(convertPlan);
            return "编码转换已提交，计划条目数=" + convertPlan.Count;
        }
        if (action == "chmod")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            int lineSplit = argument.IndexOf('\n');
            if (lineSplit <= 0 || lineSplit >= argument.Length - 1) { return "缺少目标路径或权限值"; }
            string targetPath = argument.Substring(0, lineSplit);
            string modeText = argument.Substring(lineSplit + 1).Trim();
            IRemotePermissionChanger? changer = fileTab.PermissionChanger;
            if (changer == null) { return "当前标签不是远程连接，无法修改权限"; }
            int octalMode;
            if (!WebWorkspace.TryParseOctalMode(modeText, out octalMode)) { return "权限格式无效（0~777 八进制）"; }
            try
            {
                Task.Run(delegate { changer.ChangePermissions(targetPath, octalMode); }).GetAwaiter().GetResult();
                return "权限已修改：" + modeText;
            }
            catch (Exception error)
            {
                return "权限修改失败：" + error.Message;
            }
        }
        if (action == "togglehidden")
        {
            this.Settings.ShowHiddenFiles = !this.Settings.ShowHiddenFiles;
            this.SaveSettingsAndApply();
            return "显示隐藏条目=" + (this.Settings.ShowHiddenFiles ? "开" : "关");
        }
        if (action == "toggleextensions")
        {
            this.Settings.ShowFileExtensions = !this.Settings.ShowFileExtensions;
            this.SaveSettingsAndApply();
            return "显示文件扩展名=" + (this.Settings.ShowFileExtensions ? "开" : "关");
        }
        if (action == "pin")
        {
            if (string.IsNullOrEmpty(argument))
            {
                this.PinCurrentTab();
            }
            else
            {
                this.PinPath(argument);
            }
            return "已加入书签";
        }
        if (action == "unpin")
        {
            this.Unpin(argument);
            return "已从书签移除";
        }
        if (action == "comparechecked")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            if (fileTab.ContentAccess != null || fileTab.FileSystemProvider is ArchiveFileSystemProvider)
            {
                return "远程连接或压缩包内的条目暂不支持比对";
            }
            List<FileRow> compareRows = fileTab.GetCheckedRows();
            if (compareRows.Count != 2)
            {
                return "请先勾选恰好两项（两个文件夹或两个文件）再开始比对";
            }
            this.OpenCompareTab(compareRows[0].FullPath, compareRows[1].FullPath);
            return "已打开比对：左侧=" + compareRows[0].FullPath + " 右侧=" + compareRows[1].FullPath;
        }
        if (action.StartsWith("compare", StringComparison.Ordinal))
        {
            return this.ExecuteCompareAction(action, argument);
        }
        if (action.StartsWith("startup", StringComparison.Ordinal))
        {
            StartupManagerViewModel? startupTab = this.SelectedTab as StartupManagerViewModel;
            if (startupTab == null)
            {
                return "当前标签不是启动项管理";
            }
            return startupTab.ExecuteAction(action, argument);
        }
        if (action.StartsWith("conn", StringComparison.Ordinal))
        {
            return this.ExecuteConnectionAction(action, argument);
        }
        string? viewKind = TabKinds.FromOpenAction(action);
        if (viewKind != null)
        {
            this.OpenView(viewKind);
            return string.Empty;
        }
        ProcessPanelViewModel? processTab = this.SelectedTab as ProcessPanelViewModel;
        if (action == "processrefresh")
        {
            if (processTab == null) { return "当前标签不是进程监控"; }
            processTab.StartRefresh();
            return string.Empty;
        }
        if (action == "processselect")
        {
            if (processTab == null) { return "当前标签不是进程监控"; }
            int selectedProcessId;
            if (int.TryParse(argument, out selectedProcessId))
            {
                processTab.SelectProcessById(selectedProcessId);
            }
            return string.Empty;
        }
        if (action == "processkill")
        {
            if (processTab == null) { return "当前标签不是进程监控"; }
            int killProcessId;
            if (!int.TryParse(argument, out killProcessId))
            {
                return "缺少要结束的进程编号";
            }
            processTab.TerminateByProcessId(killProcessId);
            return string.Empty;
        }
        if (action == "processpriority")
        {
            if (processTab == null) { return "当前标签不是进程监控"; }
            string[] priorityParts = argument.Split(',');
            int priorityProcessId;
            int priorityIndex;
            if (priorityParts.Length != 2
                || !int.TryParse(priorityParts[0].Trim(), out priorityProcessId)
                || !int.TryParse(priorityParts[1].Trim(), out priorityIndex))
            {
                return "优先级参数格式不正确，应为进程编号,优先级索引";
            }
            processTab.ApplyPriorityByProcessId(priorityProcessId, priorityIndex);
            return string.Empty;
        }
        if (action == "processaffinity")
        {
            if (processTab == null) { return "当前标签不是进程监控"; }
            string[] affinityParts = argument.Split(new char[] { ',' }, 2);
            int affinityProcessId;
            if (affinityParts.Length != 2 || !int.TryParse(affinityParts[0].Trim(), out affinityProcessId))
            {
                return "亲和性参数格式不正确，应为进程编号,核心编号列表";
            }
            processTab.ApplyAffinityByProcessId(affinityProcessId, affinityParts[1]);
            return string.Empty;
        }
        return "未知动作：" + action;
    }

    /// <summary>把"每行一个路径"的文本拆成路径列表（去掉空行与行尾回车）。</summary>
    private static List<string> SplitPathLines(string text)
    {
        List<string> paths = new List<string>();
        foreach (string line in text.Split('\n'))
        {
            string path = line.TrimEnd('\r').Trim();
            if (path.Length > 0)
            {
                paths.Add(path);
            }
        }
        return paths;
    }

    /// <summary>解析批量改名计划：每行"旧路径[TAB]新名称"。</summary>
    private static List<string[]> ParseRenamePlan(string text)
    {
        List<string[]> plan = new List<string[]>();
        foreach (string line in text.Split('\n'))
        {
            string cleanLine = line.TrimEnd('\r');
            int tab = cleanLine.IndexOf('\t');
            if (tab <= 0 || tab >= cleanLine.Length - 1)
            {
                continue;
            }
            plan.Add(new string[] { cleanLine.Substring(0, tab), cleanLine.Substring(tab + 1) });
        }
        return plan;
    }

    /// <summary>解析编码转换预览请求：首行为 源代码页 \t 目标代码页 \t 递归标志（1 为递归），其余行为要转换的路径。</summary>
    private static List<string> ParseEncodePreviewRequest(string text,
        out int fromCodePage, out int toCodePage, out bool recursive)
    {
        fromCodePage = 0;
        toCodePage = 0;
        recursive = false;
        List<string> paths = new List<string>();
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (index == 0)
            {
                string[] header = line.Split('\t');
                if (header.Length < 3)
                {
                    return paths;
                }
                if (!int.TryParse(header[0], out fromCodePage)
                    || !int.TryParse(header[1], out toCodePage))
                {
                    return paths;
                }
                recursive = header[2] == "1";
                continue;
            }
            if (line.Length > 0)
            {
                paths.Add(line);
            }
        }
        return paths;
    }

    /// <summary>创建一个新的网页端独立工作区。</summary>
    public WebWorkspace CreateWorkspace()
    {
        WebWorkspace workspace = new WebWorkspace(this.services, this.Settings);
        this.webWorkspaces[workspace.Id] = workspace;
        return workspace;
    }

    /// <summary>按编号查找独立工作区；不存在时返回 null。</summary>
    public WebWorkspace? FindWorkspace(string id)
    {
        WebWorkspace? workspace;
        if (this.webWorkspaces.TryGetValue(id, out workspace))
        {
            workspace.LastAccessTime = DateTime.Now;
            return workspace;
        }
        return null;
    }

    /// <summary>销毁指定独立工作区。</summary>
    public void RemoveWorkspace(string id)
    {
        this.webWorkspaces.Remove(id);
    }

    /// <summary>更新网页端服务的状态文字（状态栏展示）。</summary>
    private void SetWebServiceStatus(string status)
    {
        this.webServiceStatus = status;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.WebServiceStatus)));
    }

    /// <summary>浏览器镜像打开路径：跨线程安全，无文件标签时自动新建。</summary>
    public void OpenPathOnUiThread(string path)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            this.OpenInFileTab(path);
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(delegate { this.OpenInFileTab(path); });
    }

    /// <summary>在文件标签中打开路径：当前没有文件标签时自动新建一个。</summary>
    public void OpenInFileTab(string path)
    {
        if (this.SelectedFileTab == null)
        {
            this.AddTab(path);
            return;
        }
        this.SelectedFileTab.OpenPath(path);
    }

    /// <summary>
    /// 规范化路径用于跨窗口去重比较：取绝对路径、去掉尾部分隔符（盘根除外），
    /// 比较统一按忽略大小写进行。
    /// </summary>
    private static string NormalizePath(string path)
    {
        string text = path.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        try
        {
            text = Path.GetFullPath(text);
        }
        catch
        {
            // 非法路径字符串：保持原样参与比较
        }
        string root = Path.GetPathRoot(text) ?? string.Empty;
        while (text.Length > root.Length
            && (text[text.Length - 1] == Path.DirectorySeparatorChar || text[text.Length - 1] == Path.AltDirectorySeparatorChar))
        {
            text = text.Substring(0, text.Length - 1);
        }
        return text;
    }

    /// <summary>
    /// 系统唤起打开路径（文件夹接管 / 单实例转发 / 程序启动参数走这里）：
    /// 跨全部窗口按规范化路径查找既有文件标签，命中则激活所在窗口并跳转（不重复开新标签），
    /// 未命中则在主窗口新建标签。手动打开（新建标签按钮、书签、地址栏、网页端）不走去重逻辑。
    /// 每一步都写日志：唤起路径、规范化结果、程序选择（哪个窗口哪个标签承接）、最终行为效果。
    /// </summary>
    /// <param name="path">系统唤起的原始路径（目录或文件）。</param>
    /// <param name="source">唤起来源（程序启动参数 / 单实例命令管道等），仅用于日志追溯。</param>
    public void OpenPathFromSystem(string path, string source)
    {
        UiLogger.Log("程序行为", "系统唤起打开路径：来源=" + source + " 原始路径=" + path);
        string target = NormalizePath(path);
        string? selectFile = null;
        if (string.IsNullOrEmpty(target))
        {
            UiLogger.Log("程序行为", "系统唤起路径规范化后为空，本次唤起不打开任何标签：来源=" + source + " 原始路径=" + path);
            return;
        }
        if (!Directory.Exists(target))
        {
            if (File.Exists(target))
            {
                // 唤起对象是文件时打开其所在目录，并在加载完成后选中该文件（微信“在文件夹中显示”的期望效果）
                selectFile = target;
                string directory = NormalizePath(Path.GetDirectoryName(target) ?? target);
                UiLogger.Log("程序行为", "系统唤起对象是文件，程序选择改为打开其所在目录并选中该文件：来源=" + source
                    + " 文件路径=" + target + " 所在目录=" + directory);
                target = directory;
            }
            else
            {
                UiLogger.Log("程序行为", "系统唤起路径不存在，本次唤起不打开任何标签：来源=" + source + " 路径=" + target);
                return;
            }
        }
        UiLogger.Log("程序行为", "系统唤起路径校验通过：来源=" + source
            + " 原始路径=" + path + " 规范化路径=" + target + " 路径类型=目录");

        // 跨全部窗口查找既有文件标签（压缩包虚拟标签不参与匹配）
        IReadOnlyList<Views.MainWindow> windows = WindowRegistry.GetAllWindows();
        int scannedWindowCount = 0;
        int scannedTabCount = 0;
        foreach (Views.MainWindow window in windows)
        {
            scannedWindowCount++;
            if (!(window.DataContext is MainWindowViewModel viewModel))
            {
                continue;
            }
            foreach (object tab in viewModel.Tabs)
            {
                scannedTabCount++;
                FileTabViewModel? fileTab = tab as FileTabViewModel;
                if (fileTab == null || fileTab.FileSystemProvider is ArchiveFileSystemProvider)
                {
                    continue;
                }
                if (!string.Equals(NormalizePath(fileTab.CurrentPath), target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // 命中既有标签：激活所在窗口并跳转选中，不再开新标签
                int matchedTabIndex = viewModel.Tabs.IndexOf(fileTab);
                bool windowWasVisible = window.IsVisible;
                window.Show();
                window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Activate();
                viewModel.SelectedTab = fileTab;
                if (selectFile != null)
                {
                    fileTab.SelectEntry(selectFile);
                }
                UiLogger.Log("程序行为", "系统唤起最终行为：命中既有标签，激活所在窗口并跳转，未新建标签"
                    + " 来源=" + source + " 路径=" + target
                    + " 命中窗口序号=" + WindowRegistry.GetWindowIndex(window)
                    + " 命中窗口唤起前可见=" + windowWasVisible
                    + " 命中窗口唤起后可见=" + window.IsVisible
                    + " 命中窗口唤起后状态=" + window.WindowState
                    + " 命中标签序号=" + matchedTabIndex
                    + " 该窗口标签总数=" + viewModel.Tabs.Count
                    + " 本次扫描窗口数=" + scannedWindowCount
                    + " 本次扫描标签数=" + scannedTabCount);
                return;
            }
        }

        // 未命中：默认在既有主窗口内新建标签
        Views.MainWindow? primaryWindow = WindowRegistry.Primary;
        MainWindowViewModel? mainViewModel = primaryWindow?.DataContext as MainWindowViewModel;
        if (mainViewModel == null)
        {
            mainViewModel = this;
        }
        int previousTabCount = mainViewModel.Tabs.Count;
        // 承接窗口若被最小化/隐藏，标签建了也看不见（系统唤起后窗口仍最小化的根因）
        bool primaryWasVisible = primaryWindow?.IsVisible ?? false;
        Avalonia.Controls.WindowState primaryPreviousState = primaryWindow?.WindowState ?? Avalonia.Controls.WindowState.Normal;
        if (primaryWindow != null)
        {
            primaryWindow.Show();
            primaryWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            primaryWindow.Activate();
        }
        UiLogger.Log("程序行为", "系统唤起最终行为：未命中既有标签，在主窗口新建标签"
            + " 来源=" + source + " 路径=" + target
            + " 新建目标窗口序号=" + (primaryWindow != null ? WindowRegistry.GetWindowIndex(primaryWindow) : -1)
            + " 新建前该窗口标签数=" + previousTabCount
            + " 承接窗口唤起前可见=" + primaryWasVisible
            + " 承接窗口唤起前状态=" + primaryPreviousState
            + " 承接窗口唤起后可见=" + (primaryWindow?.IsVisible ?? false)
            + " 承接窗口唤起后状态=" + (primaryWindow?.WindowState.ToString() ?? "无窗口")
            + " 本次扫描窗口数=" + scannedWindowCount
            + " 本次扫描标签数=" + scannedTabCount);
        mainViewModel.AddTab(target);
        if (selectFile != null && mainViewModel.Tabs.Count > previousTabCount
            && mainViewModel.Tabs[mainViewModel.Tabs.Count - 1] is FileTabViewModel newFileTab)
        {
            newFileTab.SelectEntry(selectFile);
        }
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
        string previous = this.Settings.Theme;
        this.Settings.Theme = theme == "Light" ? "Light" : "Dark";
        Avalonia.Application.Current!.RequestedThemeVariant = this.Settings.Theme == "Light"
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
        this.settingsStore.Save(this.Settings);
        if (previous != this.Settings.Theme)
        {
            UiLogger.Log("配置更改", "界面主题：原值=" + previous + " 新值=" + this.Settings.Theme);
        }
    }

    /// <summary>取标签标题：各标签视图模型都公开 Title属性，取不到时回退为"标签页"。</summary>
    private static string DescribeTabTitle(object tab)
    {
        System.Reflection.PropertyInfo? property = tab.GetType().GetProperty("Title");
        if (property != null && property.PropertyType == typeof(string))
        {
            string? title = property.GetValue(tab) as string;
            if (!string.IsNullOrEmpty(title))
            {
                return title;
            }
        }
        return "标签页";
    }

    /// <summary>构建界面状态快照（必须在 UI 线程调用）。</summary>
    public DebugUiState BuildDebugState()
    {
        this.ApplySharedSortToFileTabs();
        DebugUiState state = new DebugUiState();
        state.GeneratedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        state.Theme = this.Settings.Theme;
        state.TabCount = this.Tabs.Count;
        int index = 0;
        foreach (object tab in this.Tabs)
        {
            DebugTabState tabState = new DebugTabState();
            tabState.Index = index;
            tabState.Selected = object.ReferenceEquals(tab, this.SelectedTab);
            tabState.Title = MainWindowViewModel.DescribeTabTitle(tab);
            TabSnapshotBuilder.Fill(tab, tabState);
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
                IconCategory = item.IconCategory,
            });
        }
        foreach (QuickAccessItem item in this.ThisPcItems)
        {
            state.NavPc.Add(new DebugNavItem
            {
                Name = item.Name,
                Path = item.Path,
                Selected = object.ReferenceEquals(item, navMatch),
                IconCategory = item.IconCategory,
            });
        }

        // 视图开关与最近访问（与桌面设置、地址栏"最近"列表同构）
        state.ShowHiddenFiles = this.Settings.ShowHiddenFiles;
        state.ShowFileExtensions = this.Settings.ShowFileExtensions;
        state.FileVisibleColumns = this.Settings.FileVisibleColumns;
        state.FileColumnWidths = this.Settings.FileColumnWidths;
        state.FileCompactMode = this.Settings.FileCompactMode;
        state.FileSortField = this.Settings.FileSortField;
        state.FileSortAscending = this.Settings.FileSortAscending;
        state.ClipboardHasFiles = this.canPasteFromClipboard;
        state.ClipboardIsCut = this.clipboardIsCut;
        foreach (FileTaskSnapshot task in this.fileTaskCenter.Snapshot())
        {
            state.Tasks.Add(task);
        }
        foreach (FileTaskSnapshot task in RemoteTaskAdapter.Snapshot(this.Tabs))
        {
            state.Tasks.Add(task);
        }
        foreach (string recentPath in this.RecentPaths)
        {
            state.RecentPaths.Add(recentPath);
        }

        // IDE 模式镜像（与桌面 IDE 外壳同构：资源树 + 编辑器标签 + 查找面板）
        state.IsIdeMode = this.IsIdeMode;
        if (this.IsIdeMode)
        {
            state.Ide = this.IdeShell.BuildMirrorState();
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

    /// <summary>把勾选条目路径放入剪贴板（内部剪贴板 + 系统剪贴板）。</summary>
    private void SetClipboard(bool isCut)
    {
        FileTabViewModel? tab = this.SelectedFileTab;
        if (tab == null)
        {
            UiLogger.Log("程序行为", "写入剪贴板：选中标签=无 结果=失败，未写入任何剪贴板");
            return;
        }
        List<string> paths = tab.GetCheckedPaths();
        if (paths.Count == 0)
        {
            tab.ShowMessage(isCut ? "请先勾选要剪切的条目" : "请先勾选要复制的条目");
            UiLogger.Log("程序行为", "写入剪贴板：勾选路径数=0 方式=" + (isCut ? "剪切" : "复制") + " 结果=失败，未写入任何剪贴板");
            return;
        }
        this.SetClipboardPaths(paths, isCut);
    }

    /// <summary>同步界面可见性设置。</summary>
    private void SyncViewSettings()
    {
        this.ShowToolbar = this.Settings.ShowToolbar;
        this.ShowBookmarkBar = this.Settings.ShowBookmarkBar;
        this.ShowTopWhiteStrip = this.Settings.ShowTopWhiteStrip;
    }

    /// <summary>填充此电脑分组：磁盘分区与常用文件夹。</summary>
    private void FillThisPcItems()
    {
        this.ThisPcItems.Clear();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = "本地磁盘 (" + drive.Name.TrimEnd(Path.DirectorySeparatorChar) + ")", Path = drive.Name, IconCategory = FileIconCatalog.Drive });
        }

        this.AddUserFolder("桌面", Environment.SpecialFolder.Desktop, FileIconCatalog.Folder);
        this.AddUserFolder("我的文档", Environment.SpecialFolder.MyDocuments, FileIconCatalog.Document);
        this.AddUserFolder("图片", Environment.SpecialFolder.MyPictures, FileIconCatalog.Image);
        this.AddUserFolder("音乐", Environment.SpecialFolder.MyMusic, FileIconCatalog.Audio);
        this.AddUserFolder("视频", Environment.SpecialFolder.MyVideos, FileIconCatalog.Video);

        string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloadsPath))
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = "下载", Path = downloadsPath, IconCategory = FileIconCatalog.Folder });
        }
    }

    /// <summary>添加一个系统特殊文件夹。</summary>
    private void AddUserFolder(string name, Environment.SpecialFolder folder, string category)
    {
        string path = Environment.GetFolderPath(folder);
        if (!string.IsNullOrEmpty(path))
        {
            this.ThisPcItems.Add(new QuickAccessItem { Name = name, Path = path, IconCategory = category });
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
            this.QuickAccessItems.Add(new QuickAccessItem { Name = GetDisplayName(path), Path = path, IconCategory = FileIconCatalog.Folder });
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
