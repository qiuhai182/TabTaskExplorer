using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Controls;
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
    private bool showTopWhiteStrip = false;
    private bool isIdeMode;
    private List<string>? clipboardPaths;
    private bool clipboardIsCut;
    private DebugWebHost? debugWebHost;
    private bool webServiceRunning;
    private int webServicePort;
    private string webServiceStatus = "网页端服务：未启动";

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

    /// <summary>新建文件标签页并打开指定路径。</summary>
    public void AddTab(string path)
    {
        FileTabViewModel tab = new FileTabViewModel(
            this.services.GetRequiredService<IFileSystemProvider>(),
            this.Settings,
            path);
        tab.PathChanged += this.OnTabPathChanged;
        this.Tabs.Add(tab);
        this.SelectedTab = tab;
        UiLogger.Log("程序行为", "新建文件标签：路径=" + (string.IsNullOrEmpty(path) ? "此电脑" : path) + " 标签总数=" + this.Tabs.Count);
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
            this.services.GetRequiredService<IDiskUsageProvider>());
        this.Tabs.Add(newDiskView);
        this.SelectedTab = newDiskView;
        UiLogger.Log("程序行为", "新建磁盘视图标签：标签总数=" + this.Tabs.Count);
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
            FileTabViewModel newTab = new FileTabViewModel(provider, this.Settings, "/");
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
        UiLogger.Log("配置更改", "会话保存：记录打开的标签数量=" + this.Settings.OpenTabPaths.Count);
    }

    /// <summary>创建进程监控面板（供独立窗口使用）。</summary>
    public ProcessPanelViewModel CreateProcessPanel()
    {
        IProcessProvider processProvider = this.services.GetRequiredService<IProcessProvider>();
        ISystemMetricsProvider metricsProvider = this.services.GetRequiredService<ISystemMetricsProvider>();
        return new ProcessPanelViewModel(processProvider, metricsProvider);
    }

    /// <summary>接入网页端服务并按当前设置决定是否随程序启动。</summary>
    public void AttachDebugWebHost(DebugWebHost host)
    {
        this.debugWebHost = host;
        this.ApplyWebServiceSettings();
    }

    /// <summary>
    /// 按设置启动或停止网页端服务：开关关闭时停止，端口变化时按新端口重启，
    /// 并在状态栏显示真实监听地址或未启动原因。设置发生变化后由 SaveSettingsAndApply 调用。
    /// </summary>
    public void ApplyWebServiceSettings()
    {
        DebugWebHost? host = this.debugWebHost;
        if (host == null)
        {
            return;
        }

        if (!this.Settings.EnableWebService)
        {
            if (this.webServiceRunning)
            {
                host.Stop();
                this.webServiceRunning = false;
                UiLogger.Log("网页端服务", "停止：按设置关闭 原监听地址=" + host.ListenUrl + " 原端口=" + this.webServicePort);
            }
            else
            {
                UiLogger.Log("网页端服务", "保持停止：设置中已关闭，服务未在运行");
            }
            this.SetWebServiceStatus("网页端服务：未启动");
            return;
        }

        int port = this.Settings.WebServicePort;
        if (port < 1024 || port > 65535)
        {
            UiLogger.Log("网页端服务", "端口非法已回落默认值：配置端口=" + port + " 默认端口=" + DebugWebHost.DefaultPort);
            port = DebugWebHost.DefaultPort;
            this.Settings.WebServicePort = port;
        }
        if (this.webServiceRunning && this.webServicePort == port)
        {
            return;
        }
        if (this.webServiceRunning)
        {
            host.Stop();
            this.webServiceRunning = false;
            UiLogger.Log("网页端服务", "停止：端口变化需重启 原端口=" + this.webServicePort + " 新端口=" + port);
        }

        bool started = host.Start(
            delegate { return this.DispatchDebugSnapshotAsync(); },
            delegate(int index) { this.SelectDebugTabOnUiThread(index); },
            delegate(string theme) { this.SetThemeOnUiThread(theme); },
            delegate(string path) { this.OpenPathOnUiThread(path); },
            port);

        this.webServiceRunning = started;
        this.webServicePort = port;
        if (started)
        {
            this.SetWebServiceStatus("网页端服务：" + host.ListenUrl);
            UiLogger.Log("网页端服务", "启动成功：监听地址=" + host.ListenUrl + " 端口=" + port);
        }
        else
        {
            this.SetWebServiceStatus("网页端服务：未启动（端口 " + port + " 不可用）");
            UiLogger.Log("网页端服务", "启动失败：端口=" + port + " 不可用，可能已被其他程序占用");
        }
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
