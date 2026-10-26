using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Controls;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
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

    /// <summary>图标字符（Segoe MDL2）。</summary>
    public string IconGlyph { get; set; } = "\uE8B7";
}

/// <summary>
/// 主窗口视图模型：混合标签页（文件浏览 / 任务看板）、侧边栏、书签栏、剪贴板与设置。
/// </summary>
public class MainWindowViewModel : INotifyPropertyChanged, IWebWorkspaceBridge
{
    private readonly IServiceProvider services;
    private readonly AppSettingsStore settingsStore = new AppSettingsStore();
    private object? selectedTab;
    private bool showToolbar = true;
    private bool showBookmarkBar = true;
    private bool showTopWhiteStrip = false;
    private bool isIdeMode;
    private List<string>? clipboardPaths;
    private bool clipboardIsCut;
    private WebServiceHost? debugWebHost;
    private bool webServiceRunning;
    private int webServicePort;
    private string webServiceStatus = "网页端服务：未启动";
    private readonly Dictionary<string, WebWorkspace> webWorkspaces = new Dictionary<string, WebWorkspace>();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>应用设置（所有标签页共享同一份实例）。</summary>
    public AppSettings Settings { get; private set; }

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
    }

    /// <summary>
    /// 拖出标签创建的新窗口所用构造：与主窗口共享同一份设置对象（避免多份配置互相覆盖保存），
    /// 不恢复会话标签（标签由拖出操作移入），固定以资源管理器模式打开。
    /// </summary>
    public MainWindowViewModel(IServiceProvider services, AppSettings sharedSettings)
    {
        this.services = services;
        this.Settings = sharedSettings;
        this.IdeShell = new IdeShellViewModel(this.Settings, services);
        this.FillThisPcItems();
        this.FillQuickAccessItems();
        this.SyncViewSettings();
        this.ApplySavedTheme();
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

    /// <summary>按给定提供者创建文件标签并加入标签条。</summary>
    private void CreateFileTab(IFileSystemProvider provider, string path,
        DiskScanService? scanService, IArchiveService archiveService)
    {
        FileTabViewModel tab = new FileTabViewModel(provider, this.Settings, path, scanService, archiveService);
        tab.PathChanged += this.OnTabPathChanged;
        tab.ArchiveOpenRequested += this.OnArchiveOpenRequested;
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
            this.services.GetRequiredService<DiskScanService>());
        this.Tabs.Add(newDiskView);
        this.SelectedTab = newDiskView;
        UiLogger.Log("程序行为", "新建磁盘视图标签：标签总数=" + this.Tabs.Count);
    }

    /// <summary>跟随桌面：网页端请求打开桌面磁盘视图标签。</summary>
    public void OpenDesktopDiskView()
    {
        this.OpenDiskViewTab();
        UiLogger.Log("网页端服务", "跟随桌面打开磁盘视图标签：标签总数=" + this.Tabs.Count);
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
            string secret = this.ResolveSecret(profile);
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
        catch (Exception ex)
        {
            Views.MessageDialog.Show(this.OwnerWindow, "连接失败", "无法连接 " + profile.Host + "：" + ex.Message);
        }
    }

    /// <summary>把远程目录作为文件标签打开（复用本地文件标签的全部能力）。</summary>
    public async void OpenRemoteFolderTab(RemoteConnectionProfile profile)
    {
        if (profile == null)
        {
            return;
        }
        foreach (object tab in this.Tabs)
        {
            if (tab is FileTabViewModel file && file.AttachedProfileId == profile.Id)
            {
                this.SelectedTab = file;
                return;
            }
        }

        try
        {
            string secret = this.ResolveSecret(profile);
            IRemoteSessionFactory factory = this.services.GetRequiredService<IRemoteSessionFactory>();
            IFileSystemProvider provider = await Task.Run(delegate
            {
                return factory.OpenFileSystem(profile, secret);
            });
            // 远程标签传空扫描服务：禁止对本机磁盘做目录大小回填
            FileTabViewModel newTab = new FileTabViewModel(provider, this.Settings, "/", null,
                this.services.GetRequiredService<IArchiveService>());
            newTab.AttachedProfileId = profile.Id;
            newTab.PathChanged += this.OnTabPathChanged;
            newTab.RemoteFileOpenRequested += this.OnRemoteFileOpenRequested;
            this.Tabs.Add(newTab);
            this.SelectedTab = newTab;
        }
        catch (Exception ex)
        {
            Views.MessageDialog.Show(this.OwnerWindow, "连接失败", "无法打开远程文件夹：" + ex.Message);
        }
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
            string secret = this.ResolveSecret(profile);
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
            Views.MessageDialog.Show(this.OwnerWindow, "连接失败", "无法打开远程 IDE 工作区：" + ex.Message);
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
                string secret = this.ResolveSecret(profile);
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
            Views.MessageDialog.Show(this.OwnerWindow, "连接失败", "无法建立传输服务：" + ex.Message);
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

        int successCount = this.TransferPathsToDirectory(tab, tab.CurrentPath, this.clipboardPaths,
            this.clipboardIsCut, out int failedCount);
        if (this.clipboardIsCut)
        {
            this.clipboardPaths = null;
        }
        tab.ShowMessage("粘贴完成：成功 " + successCount + " 项，失败 " + failedCount + " 项");
        tab.Refresh();
    }

    /// <summary>
    /// 把一批路径统一移动或复制到指定目录：Ctrl+V 粘贴、列表内拖放、窗口级拖放共用的落地入口。
    /// 逐条按源路径解析实际提供者（压缩包内 / 包外各自处理），单条失败不影响其余条目，
    /// 返回成功条数，并通过 out 参数返回失败条数。
    /// </summary>
    /// <param name="providerTab">发起落地的目标标签（用于判断目标是否在压缩包内）。</param>
    /// <param name="targetDirectory">目标目录。</param>
    /// <param name="paths">要移动或复制的源路径列表。</param>
    /// <param name="isMove">true 为移动，false 为复制。</param>
    /// <param name="failedCount">落地失败的条目数。</param>
    /// <returns>落地成功的条目数。</returns>
    public int TransferPathsToDirectory(FileTabViewModel providerTab, string targetDirectory,
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
    /// 两者都不是时用目标标签自己的提供者。
    /// </summary>
    private IFileSystemProvider ResolvePasteProvider(FileTabViewModel tab, string path)
    {
        ArchiveFileSystemProvider? targetArchive = tab.FileSystemProvider as ArchiveFileSystemProvider;
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
        return tab.FileSystemProvider;
    }

    /// <summary>保存设置并让全部文件标签页按新设置刷新；网页端服务的开关与端口在此重新应用。</summary>
    public void SaveSettingsAndApply()
    {
        this.settingsStore.Save(this.Settings);
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
            + " 快捷键条数=" + this.Settings.Hotkeys.Count);
        this.SyncViewSettings();
        this.ApplyWebServiceSettings();
        foreach (object tab in this.Tabs)
        {
            if (tab is FileTabViewModel fileTab)
            {
                fileTab.Refresh();
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

    /// <summary>在 UI 线程执行网页端动作（必须在 UI 线程调用），返回给页面的提示文字，空文本表示无提示。</summary>
    private string ExecuteWebActionOnUiThread(string action, string argument)
    {
        FileTabViewModel? fileTab = this.SelectedFileTab;
        DiskViewPanelViewModel? diskTab = this.SelectedTab as DiskViewPanelViewModel;

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
            this.PasteFromClipboard();
            return string.Empty;
        }
        if (action == "deletechecked")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            if (fileTab.GetCheckedRows().Count == 0) { return "请先勾选要删除的条目"; }
            fileTab.DeleteChecked();
            return "已把勾选条目移入回收站";
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
        if (action == "openboard")
        {
            this.OpenTaskBoardTab();
            return string.Empty;
        }
        if (action == "opensearch")
        {
            this.OpenSearchTab();
            return string.Empty;
        }
        if (action == "openconnections")
        {
            this.OpenConnectionsTab();
            return string.Empty;
        }
        if (action == "opendisk")
        {
            this.OpenDiskViewTab();
            return string.Empty;
        }
        if (action == "openprocess")
        {
            this.OpenProcessMonitorTab();
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

    /// <summary>把走势图序列复制到快照列表（复制而非引用，避免快照与界面状态竞争）。</summary>
    private static void CopyTrendList(IReadOnlyList<double> source, List<double> target)
    {
        target.Clear();
        for (int index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
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
        if (string.IsNullOrEmpty(target))
        {
            UiLogger.Log("程序行为", "系统唤起路径规范化后为空，本次唤起不打开任何标签：来源=" + source + " 原始路径=" + path);
            return;
        }
        if (!Directory.Exists(target))
        {
            if (File.Exists(target))
            {
                // 唤起对象是文件时打开其所在目录
                string directory = NormalizePath(Path.GetDirectoryName(target) ?? target);
                UiLogger.Log("程序行为", "系统唤起对象是文件，程序选择改为打开其所在目录：来源=" + source
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
        UiLogger.Log("程序行为", "系统唤起最终行为：未命中既有标签，在主窗口新建标签"
            + " 来源=" + source + " 路径=" + target
            + " 新建目标窗口序号=" + (primaryWindow != null ? WindowRegistry.GetWindowIndex(primaryWindow) : -1)
            + " 新建前该窗口标签数=" + previousTabCount
            + " 本次扫描窗口数=" + scannedWindowCount
            + " 本次扫描标签数=" + scannedTabCount);
        mainViewModel.AddTab(target);
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
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            TaskBoardViewModel? board = tab as TaskBoardViewModel;
            ConnectionManagerViewModel? connections = tab as ConnectionManagerViewModel;
            DiskViewPanelViewModel? diskViewTab = tab as DiskViewPanelViewModel;
            ProcessPanelViewModel? processPanelTab = tab as ProcessPanelViewModel;
            SearchTabViewModel? searchTab = tab as SearchTabViewModel;
            if (fileTab != null)
            {
                tabState.TabType = "file";
                tabState.Title = fileTab.Title;
                tabState.CurrentPath = fileTab.CurrentPath;
                tabState.SortField = fileTab.SortField;
                tabState.SortAscending = fileTab.SortAscending;
                tabState.SearchText = fileTab.SearchText;
                tabState.ViewMode = fileTab.ViewMode.ToString();
                tabState.ShowInfoPane = fileTab.ShowInfoPane;
                tabState.CanGoBack = fileTab.CanGoBack;
                tabState.CanGoForward = fileTab.CanGoForward;
                tabState.CheckedCount = fileTab.GetCheckedRows().Count;
                tabState.SummaryText = fileTab.SummaryText;
                foreach (FileRow row in fileTab.VisibleRows)
                {
                    DebugFileRow mirror = new DebugFileRow();
                    mirror.Name = row.DisplayName;
                    mirror.IsDirectory = row.IsDirectory;
                    mirror.FullPath = row.FullPath;
                    mirror.Type = row.TypeText;
                    mirror.Size = row.SizeText;
                    mirror.Modified = row.ModifiedText;
                    mirror.IsSelected = row.IsSelected;
                    mirror.IconGlyph = row.IconGlyph;
                    mirror.IconColor = row.IconColor;
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
            else if (diskViewTab != null)
            {
                tabState.TabType = "disk";
                tabState.Title = diskViewTab.Title;
                tabState.CurrentPath = diskViewTab.RootPathInput;
                tabState.SummaryText = diskViewTab.SummaryText;
                tabState.TotalRows = diskViewTab.Rows.Count;
                tabState.SortField = diskViewTab.SortField;
                tabState.SortAscending = diskViewTab.SortAscending;
                foreach (DiskRow row in diskViewTab.Rows)
                {
                    DebugFileRow mirror = new DebugFileRow();
                    mirror.Name = row.Name;
                    mirror.IsDirectory = row.IsDirectory;
                    mirror.FullPath = row.FullPath;
                    mirror.Type = row.EntryTypeText;
                    mirror.Size = row.SizeText;
                    mirror.Free = row.FreeText;
                    mirror.Usage = row.UsageText;
                    mirror.FileSystem = row.FileSystemText;
                    mirror.Level = row.Level;
                    mirror.Expanded = row.IsExpanded;
                    tabState.Rows.Add(mirror);
                }
            }
            else if (processPanelTab != null)
            {
                // 进程监控标签：摘要、详情、调节项、走势图与进程行镜像，网页端按同一套界面展示
                tabState.TabType = "process";
                tabState.Title = processPanelTab.Title;
                tabState.ResourceText = processPanelTab.ResourceText;
                tabState.SummaryText = processPanelTab.SummaryText;
                tabState.TotalRows = processPanelTab.Rows.Count;
                tabState.DetailText = processPanelTab.DetailText;
                tabState.AffinityInput = processPanelTab.AffinityInput;
                tabState.PriorityIndex = processPanelTab.SelectedPriorityIndex;
                tabState.DetailSupported = processPanelTab.IsDetailSupported;
                tabState.TrendVersion = processPanelTab.TrendUpdatedAt.Ticks;
                tabState.SortField = processPanelTab.SortField;
                tabState.SortAscending = processPanelTab.SortAscending;
                CopyTrendList(processPanelTab.SystemCpuTrend, tabState.SystemCpuTrend);
                CopyTrendList(processPanelTab.SystemMemoryTrend, tabState.SystemMemoryTrend);
                CopyTrendList(processPanelTab.SelectedCpuTrend, tabState.SelectedCpuTrend);
                int processLimit = tabState.TotalRows > 1000 ? 1000 : tabState.TotalRows;
                for (int processIndex = 0; processIndex < processLimit; processIndex++)
                {
                    ProcessRow row = processPanelTab.Rows[processIndex];
                    DebugProcessRow mirror = new DebugProcessRow();
                    mirror.ProcessId = row.ProcessId;
                    mirror.IndentText = row.IndentText;
                    mirror.Name = row.Name;
                    mirror.CpuText = row.CpuText;
                    mirror.MemoryText = row.MemoryText;
                    mirror.WindowTitle = row.WindowTitle;
                    mirror.UserNameText = row.UserNameText;
                    mirror.ThreadText = row.ThreadText;
                    mirror.HandleText = row.HandleText;
                    mirror.PriorityText = row.PriorityText;
                    mirror.IoText = row.IoText;
                    mirror.StartTimeText = row.StartTimeText;
                    mirror.CommandLineText = row.CommandLineText;
                    mirror.IsSelected = object.ReferenceEquals(row, processPanelTab.SelectedRow);
                    tabState.ProcessRows.Add(mirror);
                }
            }
            else if (searchTab != null)
            {
                // 全局搜索标签：结果行与文件列表同构，网页端按同一套表格展示
                tabState.TabType = "search";
                tabState.Title = searchTab.Title;
                tabState.SearchText = searchTab.QueryText;
                tabState.SummaryText = searchTab.StatusText;
                tabState.TotalRows = searchTab.Results.Count;
                tabState.SortField = searchTab.SortField;
                tabState.SortAscending = searchTab.SortAscending;
                int searchLimit = tabState.TotalRows > 1000 ? 1000 : tabState.TotalRows;
                for (int resultIndex = 0; resultIndex < searchLimit; resultIndex++)
                {
                    FileRow row = searchTab.Results[resultIndex];
                    DebugFileRow mirror = new DebugFileRow();
                    mirror.Name = row.DisplayName;
                    mirror.IsDirectory = row.IsDirectory;
                    mirror.FullPath = row.FullPath;
                    mirror.Type = row.TypeText;
                    mirror.Size = row.SizeText;
                    mirror.Modified = row.ModifiedText;
                    mirror.IsSelected = row.IsSelected;
                    mirror.IconGlyph = row.IconGlyph;
                    mirror.IconColor = row.IconColor;
                    tabState.Rows.Add(mirror);
                }
            }
            else
            {
                // 终端、传输、编辑器等标签：网页端不渲染内容，但标题要与桌面标签条一致
                tabState.TabType = "unknown";
                tabState.Title = MainWindowViewModel.DescribeTabTitle(tab);
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
                IconGlyph = item.IconGlyph,
            });
        }
        foreach (QuickAccessItem item in this.ThisPcItems)
        {
            state.NavPc.Add(new DebugNavItem
            {
                Name = item.Name,
                Path = item.Path,
                Selected = object.ReferenceEquals(item, navMatch),
                IconGlyph = item.IconGlyph,
            });
        }

        // 视图开关与最近访问（与桌面设置、地址栏"最近"列表同构）
        state.ShowHiddenFiles = this.Settings.ShowHiddenFiles;
        state.ShowFileExtensions = this.Settings.ShowFileExtensions;
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
        this.ShowTopWhiteStrip = this.Settings.ShowTopWhiteStrip;
    }

    /// <summary>填充此电脑分组：磁盘分区与常用文件夹。</summary>
    private void FillThisPcItems()
    {
        this.ThisPcItems.Clear();

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
