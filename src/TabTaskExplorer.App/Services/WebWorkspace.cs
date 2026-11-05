using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.Services.Tasks;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 网页端独立工作区：自己持有一套标签页，与桌面主窗口的标签互不影响。
/// 所有界面状态只在 UI 线程读写，其他线程通过 InvokeAsync 取快照，避免集合竞争。
/// </summary>
public class WebWorkspace
{
    private readonly IServiceProvider services;
    private readonly AppSettings settings;
    private readonly List<object> tabs = new List<object>();
    private int selectedIndex = -1;

    /// <summary>工作区自己的统一任务中心（本地复制/移动/删除）。</summary>
    private readonly FileTaskCenter fileTaskCenter = new FileTaskCenter();

    /// <summary>独立工作区自己的剪贴板：被复制或剪切的条目路径。</summary>
    private List<string>? clipboardPaths;

    /// <summary>剪贴板是否处于剪切模式（粘贴时移动而非复制）。</summary>
    private bool clipboardIsCut;

    /// <summary>独立工作区自己的 IDE 外壳（资源树 + 编辑器标签 + 查找面板）。</summary>
    private readonly IdeShellViewModel ideShell;

    /// <summary>该工作区当前是否处于 IDE 模式。</summary>
    private bool isIdeMode;

    /// <summary>服务端最近一次操作提示（连接失败等），网页端轮询快照后展示。</summary>
    private string lastNotice = string.Empty;

    /// <summary>待用户确认的主机密钥（首次连接或密钥变更），网页端据此渲染确认对话框。</summary>
    private DebugPendingHostKey? pendingHostKey;

    /// <summary>工作区标识（网页端回传时使用）。</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>最近一次访问的时间（用于清理闲置工作区）。</summary>
    public DateTime LastAccessTime { get; set; } = DateTime.Now;

    /// <summary>是否跟随桌面主窗口：跟随模式下不持有自己的标签，直接读取桌面会话。</summary>
    public bool FollowDesktop { get; set; }

    /// <summary>创建独立工作区：默认打开一个"此电脑"文件标签。</summary>
    public WebWorkspace(IServiceProvider services, AppSettings settings)
    {
        this.services = services;
        this.settings = settings;
        this.ideShell = new IdeShellViewModel(settings, services);
        this.Id = Guid.NewGuid().ToString("N");
        this.fileTaskCenter.TaskFinished += delegate
        {
            Dispatcher.UIThread.Post(delegate
            {
                foreach (object tab in this.tabs)
                {
                    FileTabViewModel? fileTab = tab as FileTabViewModel;
                    if (fileTab != null)
                    {
                        fileTab.Refresh();
                    }
                }
            });
        };
        this.fileTaskCenter.ConflictPending += delegate(FileTaskSnapshot snapshot)
        {
            Dispatcher.UIThread.Post(delegate
            {
                string text = "任务等待冲突处理：" + (string.IsNullOrEmpty(snapshot.ConflictName) ? snapshot.ConflictPath : snapshot.ConflictName)
                    + "，请在任务中心选择处理方式（不会自动跳过，可取消任务）";
                this.lastNotice = text;
                FileTabViewModel? fileTab = this.CurrentFileTab();
                if (fileTab != null)
                {
                    fileTab.ShowMessage(text);
                }
                UiLogger.Log("网页端任务", "工作区=" + this.Id + " " + text + " 任务=" + snapshot.Id);
            });
        };
        this.AddFileTab(string.Empty);
    }

    /// <summary>切换该工作区的 IDE 模式 / 资源管理器模式（与桌面切换逻辑一致）。</summary>
    public void ToggleIdeMode()
    {
        if (this.isIdeMode)
        {
            this.isIdeMode = false;
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 退出 IDE 模式：回到资源管理器模式");
            return;
        }
        this.isIdeMode = true;
        string root = this.ResolveIdeWorkspaceRoot();
        this.ideShell.SetWorkspace(
            root,
            this.services.GetRequiredService<IFileSystemProvider>(),
            this.CurrentFileTab() == null ? null : this.CurrentFileTab().ContentAccess,
            string.Empty);
        if (!string.IsNullOrEmpty(root))
        {
            this.settings.IdeWorkspacePath = root;
            new AppSettingsStore().Save(this.settings);
        }
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 切换到 IDE 模式：工作区=" + root);
    }

    /// <summary>计算 IDE 工作区根路径：优先当前文件标签目录，其次最近使用的 IDE 工作区，最后用户目录。</summary>
    private string ResolveIdeWorkspaceRoot()
    {
        FileTabViewModel? tab = this.CurrentFileTab();
        if (tab != null && !string.IsNullOrEmpty(tab.CurrentPath))
        {
            return tab.CurrentPath;
        }
        if (!string.IsNullOrEmpty(this.settings.IdeWorkspacePath) && Directory.Exists(this.settings.IdeWorkspacePath))
        {
            return this.settings.IdeWorkspacePath;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>新建文件标签并打开指定路径（路径为压缩包时自动开预览标签，与桌面端一致）。</summary>
    public void AddFileTab(string path)
    {
        IArchiveService archiveService = this.services.GetRequiredService<IArchiveService>();
        IFileSystemProvider provider = this.services.GetRequiredService<IFileSystemProvider>();
        DiskScanService? scanService = this.services.GetRequiredService<DiskScanService>();
        if (ArchiveFileSystemProvider.TryCreate(archiveService, path, out ArchiveFileSystemProvider? archiveProvider))
        {
            // 压缩包：用压缩包虚拟文件系统浏览，虚拟路径不做本机目录大小回填
            provider = archiveProvider!;
            scanService = null;
        }
        this.CreateFileTab(provider, path, scanService, archiveService);
    }

    /// <summary>按给定提供者创建文件标签并加入标签条。</summary>
    private void CreateFileTab(IFileSystemProvider provider, string path,
        DiskScanService? scanService, IArchiveService archiveService)
    {
        FileTabViewModel tab = new FileTabViewModel(provider, this.settings, path, scanService, archiveService,
            new AppSettingsStore());
        tab.ArchiveOpenRequested += this.OnArchiveOpenRequested;
        this.tabs.Add(tab);
        this.selectedIndex = this.tabs.Count - 1;
        string kindText = provider is ArchiveFileSystemProvider ? "压缩包预览" : "文件";
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 新建" + kindText + "标签：路径=" + (string.IsNullOrEmpty(path) ? "此电脑" : path) + " 标签总数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>压缩包条目打开请求：在本标签所属的压缩包文件系统上新开预览子标签。</summary>
    private void OnArchiveOpenRequested(object? sender, string path)
    {
        if (sender is FileTabViewModel sourceTab && sourceTab.FileSystemProvider is ArchiveFileSystemProvider owner)
        {
            this.CreateFileTab(owner, path, null, this.services.GetRequiredService<IArchiveService>());
            return;
        }
        this.AddFileTab(path);
    }

    /// <summary>
    /// 打开路径若是压缩包则新开预览标签并返回 true（与桌面端双击压缩包一致）：
    /// 当前标签已在压缩包内且路径属于它时复用其文件系统（嵌套压缩包），
    /// 否则按真实文件魔数校验通过才开标签。
    /// </summary>
    private bool TryOpenArchiveTab(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        IArchiveService archiveService = this.services.GetRequiredService<IArchiveService>();
        if (!archiveService.IsArchivePath(path))
        {
            return false;
        }
        FileTabViewModel? current = this.CurrentFileTab();
        if (current != null && current.FileSystemProvider is ArchiveFileSystemProvider owner && owner.OwnsPath(path))
        {
            this.CreateFileTab(owner, path, null, archiveService);
            return true;
        }
        if (archiveService.Probe(path) == null)
        {
            // 伪装扩展名的普通文件或文件不存在：交回原逻辑处理
            return false;
        }
        this.AddFileTab(path);
        return true;
    }

    /// <summary>新建磁盘视图标签。</summary>
    public void AddDiskViewTab()
    {
        DiskViewPanelViewModel tab = new DiskViewPanelViewModel(
            this.services.GetRequiredService<IDiskUsageProvider>(),
            this.services.GetRequiredService<DiskScanService>(),
            this.services.GetRequiredService<IFileSystemProvider>());
        this.tabs.Add(tab);
        this.selectedIndex = this.tabs.Count - 1;
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 新建磁盘视图标签：标签总数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>新建进程监控标签。</summary>
    public void AddProcessTab()
    {
        ProcessPanelViewModel tab = new ProcessPanelViewModel(
            this.services.GetRequiredService<ProcessSampleService>(),
            this.services.GetRequiredService<IProcessProvider>(),
            this.services.GetRequiredService<IProcessDetailService>());
        this.tabs.Add(tab);
        this.selectedIndex = this.tabs.Count - 1;
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 新建进程监控标签：标签总数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>新建任务看板标签（同一工作区只保留一个）。</summary>
    public void AddTaskBoardTab()
    {
        int existingIndex = this.IndexOfTab(typeof(TaskBoardViewModel));
        if (existingIndex >= 0)
        {
            this.selectedIndex = existingIndex;
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 任务看板标签已存在：直接切换到该标签 序号=" + existingIndex);
            this.SyncTabForeground();
            return;
        }
        TaskBoardViewModel tab = new TaskBoardViewModel(this.services.GetRequiredService<ITaskStore>());
        this.tabs.Add(tab);
        this.selectedIndex = this.tabs.Count - 1;
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 新建任务看板标签：标签总数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>新建全局搜索标签（同一工作区只保留一个）。</summary>
    public void AddSearchTab()
    {
        int existingIndex = this.IndexOfTab(typeof(SearchTabViewModel));
        if (existingIndex >= 0)
        {
            this.selectedIndex = existingIndex;
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 全局搜索标签已存在：直接切换到该标签 序号=" + existingIndex);
            this.SyncTabForeground();
            return;
        }
        SearchTabViewModel tab = new SearchTabViewModel(this.services.GetRequiredService<IFileSearchService>());
        this.tabs.Add(tab);
        this.selectedIndex = this.tabs.Count - 1;
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 新建全局搜索标签：标签总数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>新建连接管理标签（同一工作区只保留一个）。</summary>
    public void AddConnectionsTab()
    {
        int existingIndex = this.IndexOfTab(typeof(ConnectionManagerViewModel));
        if (existingIndex >= 0)
        {
            this.selectedIndex = existingIndex;
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 连接管理标签已存在：直接切换到该标签 序号=" + existingIndex);
            this.SyncTabForeground();
            return;
        }
        ConnectionManagerViewModel tab = new ConnectionManagerViewModel(
            this.services.GetRequiredService<ConnectionManager>());
        this.tabs.Add(tab);
        this.selectedIndex = this.tabs.Count - 1;
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 新建连接管理标签：标签总数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>新建启动项管理标签（同一工作区只保留一个）。</summary>
    public void AddStartupTab()
    {
        int existingIndex = this.IndexOfTab(typeof(StartupManagerViewModel));
        if (existingIndex >= 0)
        {
            this.selectedIndex = existingIndex;
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 启动项管理标签已存在：直接切换到该标签 序号=" + existingIndex);
            this.SyncTabForeground();
            return;
        }
        AppSettingsStore startupSettingsStore = new AppSettingsStore();
        CustomStartupService startupService = new CustomStartupService(this.settings, startupSettingsStore);
        StartupManagerViewModel tab = new StartupManagerViewModel(
            this.services.GetRequiredService<IStartupItemProvider>(),
            this.settings, startupSettingsStore, startupService);
        this.tabs.Add(tab);
        this.selectedIndex = this.tabs.Count - 1;
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 新建启动项管理标签：标签总数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>新建比对标签（目录或文件模式由路径类型自动判断）。</summary>
    public void AddCompareTab(string leftPath, string rightPath)
    {
        CompareTabViewModel tab = new CompareTabViewModel(this.settings,
            this.services.GetRequiredService<ITextFileService>(), leftPath, rightPath);
        tab.FileCompareRequested += this.OnCompareFileRequested;
        this.tabs.Add(tab);
        this.selectedIndex = this.tabs.Count - 1;
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 新建比对标签：左侧路径=" + leftPath
            + " 右侧路径=" + rightPath + " 标签总数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>目录比对中双击文件行：新开文件比对标签。</summary>
    private void OnCompareFileRequested(object? sender, ComparePairEventArgs args)
    {
        this.AddCompareTab(args.LeftPath, args.RightPath);
    }

    /// <summary>当前工作区选中的启动项管理标签；没有时返回 null。</summary>
    private StartupManagerViewModel? CurrentStartupTab()
    {
        if (this.selectedIndex < 0 || this.selectedIndex >= this.tabs.Count)
        {
            return null;
        }
        return this.tabs[this.selectedIndex] as StartupManagerViewModel;
    }

    /// <summary>当前工作区选中的比对标签；没有时返回 null。</summary>
    private CompareTabViewModel? CurrentCompareTab()
    {
        if (this.selectedIndex < 0 || this.selectedIndex >= this.tabs.Count)
        {
            return null;
        }
        return this.tabs[this.selectedIndex] as CompareTabViewModel;
    }

    /// <summary>按标签类型打开视图：与桌面共用同一套 kind 标识。</summary>
    public void OpenView(string kind)
    {
        if (kind == TabKinds.File)
        {
            this.AddFileTab(string.Empty);
            return;
        }
        if (kind == TabKinds.Compare)
        {
            this.AddCompareTab(string.Empty, string.Empty);
            return;
        }
        if (kind == TabKinds.Board)
        {
            this.AddTaskBoardTab();
            return;
        }
        if (kind == TabKinds.Search)
        {
            this.AddSearchTab();
            return;
        }
        if (kind == TabKinds.Connections)
        {
            this.AddConnectionsTab();
            return;
        }
        if (kind == TabKinds.Disk)
        {
            this.AddDiskViewTab();
            return;
        }
        if (kind == TabKinds.Process)
        {
            this.AddProcessTab();
            return;
        }
        if (kind == TabKinds.Startup)
        {
            this.AddStartupTab();
            return;
        }
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 打开视图被忽略：无法识别的标签类型=" + kind);
    }

    /// <summary>查找指定类型标签的下标；不存在返回 -1。</summary>
    private int IndexOfTab(Type tabType)
    {
        for (int index = 0; index < this.tabs.Count; index++)
        {
            if (this.tabs[index].GetType() == tabType)
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>选中指定下标的标签；下标非法时忽略。</summary>
    public void SelectTab(int index)
    {
        if (index < 0 || index >= this.tabs.Count)
        {
            return;
        }
        this.selectedIndex = index;
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 切换标签：序号=" + index + " 标题=" + this.DescribeTitle(this.tabs[index]));
        this.SyncTabForeground();
    }

    /// <summary>关闭指定下标的标签；关闭后自动选中相邻标签。</summary>
    public void CloseTab(int index)
    {
        if (index < 0 || index >= this.tabs.Count)
        {
            return;
        }
        DiskViewPanelViewModel? closingDiskTab = this.tabs[index] as DiskViewPanelViewModel;
        if (closingDiskTab != null)
        {
            closingDiskTab.Close();
        }
        ProcessPanelViewModel? closingProcessTab = this.tabs[index] as ProcessPanelViewModel;
        if (closingProcessTab != null)
        {
            closingProcessTab.Close();
        }
        StartupManagerViewModel? closingStartupTab = this.tabs[index] as StartupManagerViewModel;
        if (closingStartupTab != null)
        {
            closingStartupTab.Close();
        }
        CompareTabViewModel? closingCompareTab = this.tabs[index] as CompareTabViewModel;
        if (closingCompareTab != null)
        {
            closingCompareTab.Close();
        }
        // 文件标签关闭时停止在途的目录大小回填
        FileTabViewModel? closingFileTab = this.tabs[index] as FileTabViewModel;
        if (closingFileTab != null)
        {
            closingFileTab.Close();
        }
        string title = this.DescribeTitle(this.tabs[index]);
        this.tabs.RemoveAt(index);
        if (this.selectedIndex >= this.tabs.Count)
        {
            this.selectedIndex = this.tabs.Count - 1;
        }
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 关闭标签：标题=" + title + " 剩余标签数=" + this.tabs.Count);
        this.SyncTabForeground();
    }

    /// <summary>回收工作区前调用：关闭全部磁盘视图与进程监控标签，退订扫描事件并停止台前探测。</summary>
    public void ReleaseTabs()
    {
        foreach (object tab in this.tabs)
        {
            if (tab is DiskViewPanelViewModel diskTab)
            {
                diskTab.Close();
            }
            if (tab is ProcessPanelViewModel processTab)
            {
                processTab.Close();
            }
            if (tab is FileTabViewModel fileTab)
            {
                fileTab.Close();
            }
            if (tab is CompareTabViewModel compareTab)
            {
                compareTab.Close();
            }
        }
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 已释放标签资源：磁盘视图、进程监控、文件标签与比对标签的在途任务全部停止");
    }

    /// <summary>台前同步：磁盘视图与进程监控只有选中的标签进入台前（台前才周期刷新，其余停在台后）。</summary>
    private void SyncTabForeground()
    {
        DiskViewPanelViewModel? currentDisk = this.CurrentDiskTab();
        ProcessPanelViewModel? currentProcess = this.CurrentProcessTab();
        for (int index = 0; index < this.tabs.Count; index++)
        {
            DiskViewPanelViewModel? diskTab = this.tabs[index] as DiskViewPanelViewModel;
            if (diskTab != null)
            {
                diskTab.SetForeground(object.ReferenceEquals(diskTab, currentDisk));
            }
            ProcessPanelViewModel? processTab = this.tabs[index] as ProcessPanelViewModel;
            if (processTab != null)
            {
                processTab.SetForeground(object.ReferenceEquals(processTab, currentProcess));
            }
        }
    }

    /// <summary>取当前选中的进程监控标签；没有时返回 null。</summary>
    private ProcessPanelViewModel? CurrentProcessTab()
    {
        ProcessPanelViewModel? processTab = this.tabs[this.selectedIndex] as ProcessPanelViewModel;
        return processTab;
    }

    /// <summary>取当前选中的全局搜索标签；没有时返回 null。</summary>
    private SearchTabViewModel? CurrentSearchTab()
    {
        if (this.selectedIndex < 0 || this.selectedIndex >= this.tabs.Count)
        {
            return null;
        }
        return this.tabs[this.selectedIndex] as SearchTabViewModel;
    }

    /// <summary>取当前选中的连接管理标签；没有时返回 null。</summary>
    private ConnectionManagerViewModel? CurrentConnectionsTab()
    {
        if (this.selectedIndex < 0 || this.selectedIndex >= this.tabs.Count)
        {
            return null;
        }
        return this.tabs[this.selectedIndex] as ConnectionManagerViewModel;
    }

    /// <summary>在当前文件标签中打开路径；没有文件标签时自动新建；压缩包路径开预览标签。</summary>
    public void OpenPath(string path)
    {
        if (this.TryOpenArchiveTab(path))
        {
            return;
        }
        FileTabViewModel? current = this.CurrentFileTab();
        if (current == null)
        {
            this.AddFileTab(path);
            return;
        }
        current.OpenPath(path);
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 打开路径：" + (string.IsNullOrEmpty(path) ? "此电脑" : path));
    }

    /// <summary>返回上一级：文件标签回到上级目录，磁盘视图标签回到上级扫描根（根视图保持不动）。</summary>
    public void NavigateUp()
    {
        FileTabViewModel? current = this.CurrentFileTab();
        if (current != null)
        {
            current.NavigateUp();
            return;
        }
        DiskViewPanelViewModel? diskTab = this.CurrentDiskTab();
        if (diskTab != null)
        {
            diskTab.NavigateUp();
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 磁盘视图返回上一级：新根目录="
                + (string.IsNullOrEmpty(diskTab.RootPathInput) ? "此电脑" : diskTab.RootPathInput));
        }
    }

    /// <summary>
    /// 磁盘视图切换展开：空路径或根路径回到根视图，其余路径在列表中原地展开或收起（不进入下一级）。
    /// 非目录路径与非磁盘视图标签直接忽略。
    /// </summary>
    public void NavigateDisk(string path)
    {
        DiskViewPanelViewModel? diskTab = this.CurrentDiskTab();
        if (diskTab == null)
        {
            return;
        }
        string target = path == null ? string.Empty : path.Trim();
        if (target.Length > 0 && !Directory.Exists(target))
        {
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 磁盘视图切换忽略：目标不是目录 目标=" + target);
            return;
        }
        diskTab.ToggleRowByPath(target);
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 磁盘视图切换展开：目标=" + (target.Length == 0 ? "根视图" : target));
    }

    /// <summary>新建文件夹（需权限方案放行增删，由调用方先判定）。</summary>
    public void CreateFolder(string folderName)
    {
        FileTabViewModel? current = this.CurrentFileTab();
        if (current != null)
        {
            current.CreateFolder(folderName);
        }
    }

    /// <summary>重命名单个条目（需权限方案放行增删，由调用方先判定）。</summary>
    public void RenameEntry(string path, string newName)
    {
        FileTabViewModel? current = this.CurrentFileTab();
        if (current == null)
        {
            return;
        }
        foreach (FileRow row in current.VisibleRows)
        {
            if (string.Equals(row.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                current.RenameRow(row, newName);
                return;
            }
        }
    }

    /// <summary>删除单个条目（需权限方案放行增删，由调用方先判定）。</summary>
    public void DeleteEntry(string path)
    {
        FileTabViewModel? current = this.CurrentFileTab();
        if (current == null)
        {
            return;
        }
        foreach (FileRow row in current.VisibleRows)
        {
            if (string.Equals(row.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                current.DeleteRow(row);
                return;
            }
        }
    }

    /// <summary>执行网页端动作（独立工作区），返回给页面的提示文字，空文本表示无提示。</summary>
    public string ExecuteAction(string action, string argument)
    {
        if (action.StartsWith("ide", StringComparison.Ordinal))
        {
            if (action == "ide")
            {
                this.ToggleIdeMode();
                return this.isIdeMode ? "已切换到 IDE 模式" : "已切换到资源管理器模式";
            }
            return this.ideShell.ExecuteIdeAction(action, argument);
        }
        FileTabViewModel? fileTab = this.CurrentFileTab();
        DiskViewPanelViewModel? diskTab = this.CurrentDiskTab();

        if (action.StartsWith("disk", StringComparison.Ordinal))
        {
            return this.RunOnUiThread<string>(delegate { return this.ExecuteDiskAction(action, argument); });
        }
        if (action == "openinmanager")
        {
            List<string> targetLines = WebWorkspace.SplitPathLines(argument);
            string path = targetLines.Count > 0 ? targetLines[0] : string.Empty;
            if (path.Length == 0)
            {
                return "在资源管理器打开失败：缺少目标路径";
            }
            bool exists = Directory.Exists(path) || File.Exists(path);
            if (!exists)
            {
                return "在资源管理器打开失败：目标不存在 " + path;
            }
            if (diskTab != null && this.FindDiskRow(diskTab, path) == null
                && !string.Equals(WebContextMenuPolicy.NormalizePath(path), WebContextMenuPolicy.NormalizePath(diskTab.RootPathInput), StringComparison.OrdinalIgnoreCase))
            {
                return "在资源管理器打开失败：目标不在当前磁盘视图内";
            }
            if (diskTab == null && fileTab != null)
            {
                bool matched = false;
                foreach (FileRow row in fileTab.VisibleRows)
                {
                    if (string.Equals(WebContextMenuPolicy.NormalizePath(row.FullPath), WebContextMenuPolicy.NormalizePath(path), StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    return "在资源管理器打开失败：目标不在当前文件列表中";
                }
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
                UiLogger.Log("网页端工作区", "在资源管理器打开：工作区=" + this.Id + " 目标=" + path + " 结果=已调用资源管理器");
                return "已在资源管理器中打开：" + path;
            }
            catch (Exception error)
            {
                UiLogger.LogError("网页端工作区", "在资源管理器打开失败：目标=" + path, error);
                return "在资源管理器打开失败：" + error.Message;
            }
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
            ProcessPanelViewModel? sortProcessTab = this.CurrentProcessTab();
            if (sortProcessTab != null)
            {
                sortProcessTab.SetSortField(argument);
                return "已按 " + argument + (sortProcessTab.SortAscending ? " 升序" : " 降序") + " 排列";
            }
            SearchTabViewModel? sortSearchTab = this.CurrentSearchTab();
            if (sortSearchTab != null)
            {
                sortSearchTab.SetSortField(argument);
                return "已按 " + argument + (sortSearchTab.SortAscending ? " 升序" : " 降序") + " 排列";
            }
            return "当前标签不支持排序";
        }
        // 表头列宽拖动：网页端回传"表格键=列宽列表"，这里识别所属视图并记录完成日志；
        // 列宽只在本次页面打开期间有效，不回写设置
        if (action == "colresize")
        {
            int separatorIndex = argument.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= argument.Length - 1)
            {
                UiLogger.Log("网页端", "表头列宽拖动结束：参数格式不正确，无法识别表格键与列宽，参数=" + argument);
                return string.Empty;
            }
            string tableKey = argument.Substring(0, separatorIndex);
            string widthsText = argument.Substring(separatorIndex + 1);
            string viewName = "未知视图";
            if (tableKey.StartsWith("file:", StringComparison.Ordinal))
            {
                viewName = "文件列表";
            }
            else if (tableKey.StartsWith("search:", StringComparison.Ordinal))
            {
                viewName = "全盘搜索";
            }
            else if (tableKey == "disk")
            {
                viewName = "磁盘视图";
            }
            else if (tableKey == "process")
            {
                viewName = "进程监控";
            }
            else if (tableKey == "startup")
            {
                viewName = "系统启动项";
            }
            else if (tableKey == "startupcustom")
            {
                viewName = "自定义启动项";
            }
            else if (tableKey == "connections")
            {
                viewName = "连接管理";
            }
            string[] parts = widthsText.Split(',');
            UiLogger.Log("网页端", "表头列宽拖动完成：视图=" + viewName + " 表格键=" + tableKey + " 列数=" + parts.Length + " 调整后列宽=" + widthsText + "像素 列宽仅本次页面打开期间有效，重新打开页面后恢复默认列宽");
            return string.Empty;
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
            SearchTabViewModel? searchTab = this.CurrentSearchTab();
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
            List<string> checkedPaths = WebWorkspace.SplitPathLines(argument);
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
            this.clipboardPaths = copyPaths;
            this.clipboardIsCut = false;
            fileTab.ShowMessage("已复制 " + copyPaths.Count + " 项，可粘贴到目标文件夹");
            return "已复制 " + copyPaths.Count + " 项";
        }
        if (action == "cut")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string> cutPaths = fileTab.GetCheckedPaths();
            if (cutPaths.Count == 0) { return "请先勾选要剪切的条目"; }
            this.clipboardPaths = cutPaths;
            this.clipboardIsCut = true;
            fileTab.ShowMessage("已剪切 " + cutPaths.Count + " 项，可粘贴到目标文件夹");
            return "已剪切 " + cutPaths.Count + " 项";
        }
        if (action == "paste")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            return this.PasteClipboard(fileTab, fileTab.CurrentPath);
        }
        if (action == "pasteto")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            if (string.IsNullOrEmpty(argument)) { return "未指定粘贴目标文件夹"; }
            return this.PasteClipboard(fileTab, argument);
        }
        if (action == "deletechecked")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string> deletePaths = fileTab.GetCheckedPaths();
            if (deletePaths.Count == 0) { return "请先勾选要删除的条目"; }
            string deleteTaskId = this.fileTaskCenter.Submit(new FileTaskRequest
            {
                Kind = FileTaskKind.Delete,
                Sources = deletePaths,
                ConflictPolicy = FileConflictPolicy.Skip,
            });
            if (string.IsNullOrEmpty(deleteTaskId))
            {
                return "删除任务提交失败";
            }
            return "已提交删除任务：共 " + deletePaths.Count + " 项，可在任务中心查看进度";
        }
        if (action == "batchrename")
        {
            if (fileTab == null) { return "当前标签不是文件列表"; }
            List<string[]> renamePlan = WebWorkspace.ParseRenamePlan(argument);
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
            List<string> encodePaths = WebWorkspace.ParseEncodePreviewRequest(argument,
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
            List<string[]> convertPlan = WebWorkspace.ParseRenamePlan(argument);
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
                changer.ChangePermissions(targetPath, octalMode);
                return "权限已修改：" + modeText;
            }
            catch (Exception error)
            {
                return "权限修改失败：" + error.Message;
            }
        }
        if (action == "togglehidden")
        {
            this.settings.ShowHiddenFiles = !this.settings.ShowHiddenFiles;
            this.SaveSettingsAndRefresh();
            return "显示隐藏条目=" + (this.settings.ShowHiddenFiles ? "开" : "关");
        }
        if (action == "toggleextensions")
        {
            this.settings.ShowFileExtensions = !this.settings.ShowFileExtensions;
            this.SaveSettingsAndRefresh();
            return "显示文件扩展名=" + (this.settings.ShowFileExtensions ? "开" : "关");
        }
        if (action == "pin" || action == "unpin")
        {
            return "独立工作区不与桌面共享书签，该操作不可用";
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
            this.AddCompareTab(compareRows[0].FullPath, compareRows[1].FullPath);
            return "已打开比对：左侧=" + compareRows[0].FullPath + " 右侧=" + compareRows[1].FullPath;
        }
        if (action.StartsWith("compare", StringComparison.Ordinal))
        {
            return this.ExecuteCompareAction(action, argument);
        }
        if (action.StartsWith("startup", StringComparison.Ordinal))
        {
            StartupManagerViewModel? startupTab = this.CurrentStartupTab();
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
        ProcessPanelViewModel? processTab = this.CurrentProcessTab();
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

    /// <summary>执行网页端比对动作：设置路径、切换选项、差异导航与进入子目录。</summary>
    private string ExecuteCompareAction(string action, string argument)
    {
        CompareTabViewModel? tab = this.CurrentCompareTab();
        if (tab == null)
        {
            return "请先打开比对标签";
        }
        if (action == "compareapply")
        {
            List<string> paths = WebWorkspace.SplitPathLines(argument);
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

    /// <summary>执行网页端连接管理动作（选择、保存、删除、测试、一键导入、打开远程文件夹）。</summary>
    private string ExecuteConnectionAction(string action, string argument)
    {
        if (action == "connhostkeytrust" || action == "connhostkeyforget" || action == "connhostkeycancel")
        {
            return this.ExecuteHostKeyAction(action);
        }
        ConnectionManagerViewModel? tab = this.CurrentConnectionsTab();
        if (tab == null)
        {
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 连接动作被忽略：当前标签不是连接管理 动作=" + action + " 参数=" + argument);
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
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 删除连接：名称=" + profile.Name + " 服务器=" + profile.Host);
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
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 测试连接：名称=" + profile.Name + " 服务器=" + profile.Host
                + " 端口=" + profile.GetEffectivePort() + " 耗时=" + result.LatencyMilliseconds + " 毫秒 结果=" + result.Message);
            return (result.Success ? "测试成功：" : "测试失败：") + result.Message;
        }
        if (action == "connimport")
        {
            XshellImportSummary summary = tab.ImportInstalled();
            string rootsText = string.Join("、", summary.RootsScanned);
            string skippedText = string.Join("、", summary.Skipped);
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 一键导入已安装配置：配置目录数=" + summary.RootsScanned.Count
                + " 配置目录=" + rootsText + " 新增=" + summary.Imported + " 更新=" + summary.Updated
                + " 跳过=" + summary.Skipped.Count + " 跳过清单=" + skippedText);
            if (summary.RootsScanned.Count == 0)
            {
                return "未发现 Xshell / Xftp 配置目录";
            }
            return "一键导入完成：新增 " + summary.Imported + " 个、更新 " + summary.Updated + " 个连接"
                + (summary.Skipped.Count > 0 ? "；跳过 " + summary.Skipped.Count + " 个：" + skippedText : string.Empty);
        }
        if (action == "connopenfolder")
        {
            RemoteConnectionProfile? profile = WebWorkspace.FindConnection(tab, argument);
            if (profile == null)
            {
                return "请先选择要打开的连接";
            }
            this.pendingHostKey = null;
            this.OpenRemoteFolder(profile);
            return "正在连接 " + profile.Host + "，远程目录标签将在连接成功后出现";
        }
        if (action == "connopenterminal")
        {
            return "网页端暂不展示终端会话，请在桌面端打开终端";
        }
        return "未知动作：" + action;
    }

    /// <summary>执行网页端主机密钥确认动作：信任首次指纹、显式删除旧记录或取消。</summary>
    private string ExecuteHostKeyAction(string action)
    {
        DebugPendingHostKey? pending = this.pendingHostKey;
        if (action == "connhostkeycancel")
        {
            this.pendingHostKey = null;
            UiLogger.Log("远程连接审计", "网页端取消主机密钥确认：工作区=" + this.Id + " 结果=未建立连接");
            return "已取消主机密钥确认";
        }
        if (pending == null)
        {
            return "当前没有待确认的主机密钥";
        }
        KnownHostStore store = this.services.GetRequiredService<KnownHostStore>();
        if (action == "connhostkeytrust")
        {
            if (pending.ErrorCode != "HOST_KEY_UNKNOWN")
            {
                return "主机密钥已变化，不能直接信任；请先删除旧记录再重新确认新指纹";
            }
            store.Trust(pending.Host, pending.Port, pending.KeyType, pending.FingerprintSha256);
            UiLogger.Log("远程连接审计", "网页端信任主机密钥：工作区=" + this.Id + " 连接=" + pending.ProfileName
                + " 主机=" + pending.Host + " 端口=" + pending.Port + " 算法=" + pending.KeyType
                + " 指纹=" + pending.FingerprintSha256 + " 结果=已写入 known_hosts 并重试连接");
            this.pendingHostKey = null;
            this.RetryPendingConnection(pending, "主机密钥已保存，正在重新连接……");
            return "已信任主机密钥并重新连接：" + pending.Host + ":" + pending.Port;
        }
        if (action == "connhostkeyforget")
        {
            if (pending.ErrorCode != "HOST_KEY_MISMATCH")
            {
                return "当前没有密钥变更记录需要删除";
            }
            int removed = store.Remove(pending.Host, pending.Port);
            UiLogger.Log("远程连接审计", "网页端删除旧主机密钥：工作区=" + this.Id + " 连接=" + pending.ProfileName
                + " 主机=" + pending.Host + " 端口=" + pending.Port + " 删除条数=" + removed
                + " 结果=已删除，重新连接需要再次确认新指纹");
            this.pendingHostKey = null;
            this.RetryPendingConnection(pending, "旧主机密钥已删除，正在重新连接，请再次确认新指纹");
            return "已删除旧主机密钥记录 " + removed + " 条；重新连接需要再次确认新指纹";
        }
        return "未知主机密钥动作：" + action;
    }

    /// <summary>确认主机密钥后按原连接重试；连接配置不存在时只记录提示。</summary>
    private void RetryPendingConnection(DebugPendingHostKey pending, string notice)
    {
        this.lastNotice = notice;
        ConnectionManager manager = this.services.GetRequiredService<ConnectionManager>();
        RemoteConnectionProfile? profile = manager.FindById(pending.ProfileId);
        if (profile == null)
        {
            this.lastNotice = "连接配置已不存在：" + pending.ProfileName;
            return;
        }
        this.OpenRemoteFolder(profile);
    }

    /// <summary>按编号查找连接管理标签中的连接配置；找不到返回 null。</summary>
    public static RemoteConnectionProfile? FindConnection(ConnectionManagerViewModel tab, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        foreach (RemoteConnectionProfile profile in tab.Connections)
        {
            if (profile.Id == id)
            {
                return profile;
            }
        }
        return null;
    }

    /// <summary>
    /// 解析网页端保存连接的参数并写入存储。参数为换行分隔：
    /// 前 11 段为编号、名称、分组、协议、主机、端口、用户名、认证方式、私钥路径、保活秒数、密码；
    /// 其后每行是一个跳板：主机|端口|用户名|认证方式|私钥路径|密码（密码留空表示保持原凭据）。
    /// </summary>
    public static string SaveConnectionFromWeb(ConnectionManagerViewModel tab, string argument)
    {
        string[] parts = argument.Split('\n');
        if (parts.Length < 10)
        {
            return "保存参数不完整，至少需要编号、名称、分组、协议、主机、端口、用户名、认证方式、私钥路径与保活秒数";
        }
        string id = parts[0].Trim();
        string name = parts[1].Trim();
        if (name.Length == 0)
        {
            return "请填写连接名称";
        }
        string host = parts[4].Trim();
        if (host.Length == 0)
        {
            return "请填写目标主机地址（当前连接）";
        }

        List<RemoteJumpHop> parsedHops = new List<RemoteJumpHop>();
        List<string> hopSecrets = new List<string>();
        for (int lineIndex = 11; lineIndex < parts.Length; lineIndex++)
        {
            string line = parts[lineIndex].Trim();
            if (line.Length == 0)
            {
                // 空行直接跳过，便于用户用空行分隔或粘贴时带尾部换行
                continue;
            }
            int lineNumber = lineIndex + 1;
            int hopNumber = parsedHops.Count + 1;
            string location = "第 " + lineNumber + " 行（第 " + hopNumber + " 跳）";
            if (hopNumber > RemoteConnectionProfile.MaxJumpHops)
            {
                return location + "：跳板机最多 " + RemoteConnectionProfile.MaxJumpHops
                    + " 跳，当前为 " + (parsedHops.Count + 1) + " 跳";
            }
            string[] fields = line.Split(new char[] { '|' }, 6);
            if (fields.Length < 5)
            {
                return location + "：字段数量不足，需要“主机|端口|用户名|认证方式|私钥路径”，密码可选";
            }
            string hopHost = fields[0].Trim();
            if (hopHost.Length == 0)
            {
                return location + "：字段“主机”为空";
            }
            string hopPortText = fields[1].Trim();
            int hopPort = 0;
            if (hopPortText.Length > 0
                && (!int.TryParse(hopPortText, out hopPort) || hopPort <= 0 || hopPort > 65535))
            {
                return location + "：字段“端口”不是有效端口：" + hopPortText;
            }
            string hopUser = fields[2].Trim();
            if (hopUser.Length == 0)
            {
                return location + "：字段“用户名”为空";
            }
            string authText = fields[3].Trim();
            RemoteAuthType hopAuthType;
            if (authText.Length == 0 || authText.Equals("Password", StringComparison.OrdinalIgnoreCase))
            {
                hopAuthType = RemoteAuthType.Password;
            }
            else if (authText.Equals("PrivateKey", StringComparison.OrdinalIgnoreCase))
            {
                hopAuthType = RemoteAuthType.PrivateKey;
            }
            else
            {
                return location + "：字段“认证方式”必须为 Password 或 PrivateKey，当前为：" + authText;
            }
            string hopKeyPath = fields[4].Trim();
            if (hopAuthType == RemoteAuthType.PrivateKey)
            {
                if (hopKeyPath.Length == 0)
                {
                    return location + "：字段“私钥路径”为空";
                }
                if (!File.Exists(hopKeyPath))
                {
                    return location + "：私钥文件不存在：" + hopKeyPath;
                }
            }
            RemoteJumpHop hop = new RemoteJumpHop();
            hop.Host = hopHost;
            hop.Port = hopPort;
            hop.Username = hopUser;
            hop.AuthType = hopAuthType;
            hop.PrivateKeyPath = hopKeyPath;
            parsedHops.Add(hop);
            hopSecrets.Add(fields.Length > 5 ? fields[5] : string.Empty);
        }

        RemoteConnectionProfile? profile = WebWorkspace.FindConnection(tab, id);
        if (profile == null)
        {
            profile = tab.CreateDraft();
        }
        // 按主机+端口+用户名沿用旧凭据，避免重排跳板后密码错位；新密码由视图模型统一加密
        List<RemoteJumpHop> oldHops = profile.JumpHops == null
            ? new List<RemoteJumpHop>()
            : new List<RemoteJumpHop>(profile.JumpHops);
        for (int index = 0; index < parsedHops.Count; index++)
        {
            RemoteJumpHop parsed = parsedHops[index];
            foreach (RemoteJumpHop oldHop in oldHops)
            {
                if (oldHop.Host == parsed.Host && oldHop.GetEffectivePort() == parsed.GetEffectivePort()
                    && oldHop.Username == parsed.Username)
                {
                    parsed.ProtectedSecret = oldHop.ProtectedSecret;
                    break;
                }
            }
        }
        profile.JumpHops = parsedHops;
        profile.Name = name;
        profile.Group = parts[2].Trim();
        profile.Protocol = parts[3].Trim() == "FTP" ? RemoteProtocol.Ftp : RemoteProtocol.Ssh;
        profile.Host = host;
        int port = 0;
        if (int.TryParse(parts[5].Trim(), out int parsedPort))
        {
            port = parsedPort;
        }
        profile.Port = port;
        profile.Username = parts[6].Trim();
        profile.AuthType = parts[7].Trim() == "PrivateKey" ? RemoteAuthType.PrivateKey : RemoteAuthType.Password;
        profile.PrivateKeyPath = parts[8].Trim();
        int keepAlive = 15;
        if (int.TryParse(parts[9].Trim(), out int parsedKeepAlive) && parsedKeepAlive >= 0)
        {
            keepAlive = parsedKeepAlive;
        }
        profile.KeepAliveSeconds = keepAlive;
        string secret = parts.Length > 10 ? parts[10] : string.Empty;
        try
        {
            tab.Save(profile, secret, hopSecrets);
        }
        catch (ArgumentException error)
        {
            return error.Message;
        }
        string hopText = profile.HasJumpHops ? "，跳板 " + profile.JumpHops.Count + " 跳" : string.Empty;
        return "已保存连接：" + profile.Name + hopText;
    }

    /// <summary>在后台线程建立远程文件系统连接，成功后回到界面线程新建远程文件夹标签。</summary>
    private void OpenRemoteFolder(RemoteConnectionProfile profile)
    {
        string secret = this.services.GetRequiredService<ConnectionManager>().GetSecret(profile);
        IRemoteSessionFactory factory = this.services.GetRequiredService<IRemoteSessionFactory>();
        WebWorkspace workspace = this;
        Task.Run(delegate
        {
            try
            {
                IFileSystemProvider provider = factory.OpenFileSystem(profile, secret);
                // 先实际读取一次根目录验证认证与 SFTP 通道；失败直接返回提示，避免生成一个空标签
                provider.ListDirectory("/");
                workspace.lastNotice = string.Empty;
                Dispatcher.UIThread.Post(delegate
                {
                    for (int index = 0; index < workspace.tabs.Count; index++)
                    {
                        FileTabViewModel? existing = workspace.tabs[index] as FileTabViewModel;
                        if (existing != null && existing.AttachedProfileId == profile.Id)
                        {
                            workspace.selectedIndex = index;
                            // 复用标签可能停留在上次失败的空列表：重新读取，触发真实重连
                            existing.Refresh();
                            UiLogger.Log("网页端工作区", "工作区=" + workspace.Id + " 远程文件夹标签已存在：重新读取并切换到该标签 连接=" + profile.Name);
                            workspace.SyncTabForeground();
                            return;
                        }
                    }
                    FileTabViewModel newTab = new FileTabViewModel(provider, workspace.settings, "/", null,
                        workspace.services.GetRequiredService<IArchiveService>(), new AppSettingsStore());
                    newTab.AttachedProfileId = profile.Id;
                    workspace.tabs.Add(newTab);
                    workspace.selectedIndex = workspace.tabs.Count - 1;
                    UiLogger.Log("网页端工作区", "工作区=" + workspace.Id + " 打开远程文件夹标签：连接=" + profile.Name
                        + " 服务器=" + profile.Host + " 端口=" + profile.GetEffectivePort() + " 标签总数=" + workspace.tabs.Count);
                    workspace.SyncTabForeground();
                });
            }
            catch (HostKeyUnknownException keyError)
            {
                workspace.pendingHostKey = new DebugPendingHostKey
                {
                    ErrorCode = "HOST_KEY_UNKNOWN",
                    ProfileId = profile.Id,
                    ProfileName = profile.Name,
                    Host = keyError.Host,
                    Port = keyError.Port,
                    KeyType = keyError.KeyType,
                    FingerprintSha256 = keyError.FingerprintSha256,
                    HopIndex = keyError.HopIndex,
                };
                workspace.lastNotice = (keyError.HopIndex > 0 ? "第 " + keyError.HopIndex + " 跳：" : string.Empty)
                    + "首次连接 " + keyError.Host + ":" + keyError.Port
                    + "，需要确认主机指纹：" + keyError.FingerprintSha256;
                UiLogger.Log("远程连接审计", "主机密钥待确认：连接=" + profile.Name + " 跳序=" + keyError.HopIndex
                    + " 主机=" + keyError.Host + " 端口=" + keyError.Port + " 算法=" + keyError.KeyType
                    + " 指纹=" + keyError.FingerprintSha256 + " 结果=已阻断，等待用户确认");
            }
            catch (HostKeyMismatchException keyError)
            {
                workspace.pendingHostKey = new DebugPendingHostKey
                {
                    ErrorCode = "HOST_KEY_MISMATCH",
                    ProfileId = profile.Id,
                    ProfileName = profile.Name,
                    Host = keyError.Host,
                    Port = keyError.Port,
                    KeyType = keyError.KeyType,
                    FingerprintSha256 = keyError.ActualFingerprintSha256,
                    ExpectedFingerprintSha256 = keyError.ExpectedFingerprintSha256,
                    HopIndex = keyError.HopIndex,
                };
                workspace.lastNotice = (keyError.HopIndex > 0 ? "第 " + keyError.HopIndex + " 跳：" : string.Empty)
                    + "主机密钥与已信任记录不一致，连接已阻断：" + keyError.Host + ":" + keyError.Port;
                UiLogger.Log("远程连接审计", "主机密钥变更被阻断：连接=" + profile.Name + " 跳序=" + keyError.HopIndex
                    + " 主机=" + keyError.Host + " 端口=" + keyError.Port + " 算法=" + keyError.KeyType
                    + " 已信任指纹=" + keyError.ExpectedFingerprintSha256
                    + " 当前指纹=" + keyError.ActualFingerprintSha256 + " 结果=硬阻断，未建立会话");
            }
            catch (RemoteHopException hopError)
            {
                workspace.lastNotice = hopError.Message;
                UiLogger.Log("远程连接审计", "跳板链连接失败：连接=" + profile.Name + " 跳序=" + hopError.HopIndex
                    + " 主机=" + hopError.Host + ":" + hopError.Port + " 阶段=" + hopError.Stage
                    + " 耗时=" + hopError.ElapsedMilliseconds + " 毫秒 结果=未建立会话，原因=" + hopError.Message);
            }
            catch (Exception error)
            {
                workspace.lastNotice = "打开远程文件夹失败：" + error.Message;
                UiLogger.LogError("网页端工作区", "打开远程文件夹失败：连接=" + profile.Name + " 服务器=" + profile.Host
                    + " 端口=" + profile.GetEffectivePort() + " 认证方式=" + profile.AuthType, error);
            }
        });
    }

    /// <summary>把剪贴板中的条目复制或移动到该标签当前目录（独立工作区自己的剪贴板）。</summary>
    private string PasteClipboard(FileTabViewModel fileTab)
    {
        return this.PasteClipboard(fileTab, fileTab.CurrentPath);
    }

    /// <summary>把剪贴板中的条目复制或移动到指定目录（独立工作区自己的剪贴板），供当前目录粘贴与「粘贴到该文件夹」共用。</summary>
    private string PasteClipboard(FileTabViewModel? fileTab, string targetDirectory)
    {
        if (this.clipboardPaths == null || this.clipboardPaths.Count == 0)
        {
            return "剪贴板为空，请先复制或剪切";
        }
        if (string.IsNullOrEmpty(targetDirectory))
        {
            return "请先进入目标文件夹";
        }
        if (fileTab != null && fileTab.FileSystemProvider is ArchiveFileSystemProvider)
        {
            string unsupported = "压缩包预览标签不支持粘贴，请在普通文件夹或远程目录中粘贴";
            fileTab.ShowMessage(unsupported);
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 粘贴：目标=" + targetDirectory
                + " 结果=压缩包预览标签不支持，已提示不可用");
            return unsupported;
        }
        string landedPathsText = string.Join("、", this.clipboardPaths);
        bool landedIsCut = this.clipboardIsCut;
        bool localTask = fileTab != null && fileTab.FileSystemProvider is DefaultFileSystemProvider;
        if (localTask)
        {
            foreach (string candidate in this.clipboardPaths)
            {
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    localTask = false;
                    break;
                }
            }
        }
        string taskId = string.Empty;
        if (localTask)
        {
            UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 粘贴开始：源数量=" + this.clipboardPaths.Count
                + " 目标=" + targetDirectory + " 冲突策略=询问（同名文件替换/改名共存，同名文件夹合并/替换/逐项处理/改名共存）");
            taskId = this.fileTaskCenter.Submit(new FileTaskRequest
            {
                Kind = landedIsCut ? FileTaskKind.Move : FileTaskKind.Copy,
                Sources = new List<string>(this.clipboardPaths),
                DestinationPath = targetDirectory,
                ConflictPolicy = FileConflictPolicy.Ask,
            });
        }
        if (string.IsNullOrEmpty(taskId))
        {
            // 远程或虚拟路径沿用原有提供者复制/移动，不进入本地任务中心
            if (fileTab != null)
            {
                int successCount = 0;
                int failedCount = 0;
                foreach (string path in this.clipboardPaths)
                {
                    try
                    {
                        if (landedIsCut) { fileTab.FileSystemProvider.Move(path, targetDirectory); }
                        else { fileTab.FileSystemProvider.Copy(path, targetDirectory); }
                        successCount++;
                    }
                    catch (Exception)
                    {
                        failedCount++;
                    }
                }
                if (landedIsCut)
                {
                    this.clipboardPaths = null;
                }
                string fallbackMessage = "粘贴完成：成功 " + successCount + " 项，失败 " + failedCount + " 项";
                fileTab.ShowMessage(fallbackMessage);
                fileTab.Refresh();
                return fallbackMessage;
            }
            return "任务提交失败：请检查源路径与目标目录";
        }
        if (this.clipboardIsCut)
        {
            this.clipboardPaths = null;
        }
        string message = "已提交" + (landedIsCut ? "移动" : "复制") + "任务：" + landedPathsText
            + " → " + targetDirectory + "，冲突时在任务中心选择处理方式（不会自动跳过）";
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 粘贴提交任务中心：目标目录=" + targetDirectory
            + " 路径列表=" + landedPathsText
            + " 方式=" + (landedIsCut ? "移动" : "复制") + " 任务=" + taskId);
        if (fileTab != null)
        {
            fileTab.ShowMessage(message);
        }
        return message;
    }

    /// <summary>刷新当前工作区内所有正好停在指定目录的文件标签（磁盘视图操作后保持文件夹视图同步）。</summary>
    public void RefreshTabsAtDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        foreach (object tab in this.tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null
                && string.Equals(WebContextMenuPolicy.NormalizePath(fileTab.CurrentPath),
                    WebContextMenuPolicy.NormalizePath(directory), StringComparison.OrdinalIgnoreCase))
            {
                fileTab.Refresh();
            }
        }
    }

    /// <summary>执行磁盘视图专属的文件操作：所有目标必须位于当前磁盘标签根目录或可见行内。</summary>
    private string ExecuteDiskAction(string action, string argument)
    {
        DiskViewPanelViewModel? diskTab = this.CurrentDiskTab();
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
            this.RefreshTabsAtDirectory(directory);
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
            this.RefreshTabsAtDirectory(parent);
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
                this.RefreshTabsAtDirectory(parent);
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
            string message = this.PasteClipboard(null, lines[0]);
            return message;
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
            this.clipboardPaths = paths;
            this.clipboardIsCut = action == "diskcutsource";
            return (this.clipboardIsCut ? "已剪切 " : "已复制 ") + paths.Count + " 项，可粘贴到目标文件夹";
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
                string parent = Path.GetDirectoryName(source) ?? string.Empty;
                this.RefreshTabsAtDirectory(parent);
            }
            this.RefreshTabsAtDirectory(target);
            return "拖放完成（" + (isMove ? "移动" : "复制") + "）：成功 " + successCount + " 项，失败 " + failedCount + " 项";
        }
        return "未知磁盘操作：" + action;
    }

    /// <summary>校验磁盘操作目录：必须等于根目录或当前可见目录行，返回空表示通过。</summary>
    private string EnsureDiskDirectory(DiskViewPanelViewModel diskTab, string directory)
    {
        string target = WebContextMenuPolicy.NormalizePath(directory);
        if (target.Length == 0)
        {
            return "此电脑视图没有具体目录，请先打开某个磁盘或目录";
        }
        // 此电脑根视图（未设置根目录）下仍允许操作已展开的目录行：
        // 行路径来自当前磁盘视图，属于可操作范围，不能因为根目录为空就整体拒绝
        foreach (DiskRow row in diskTab.Rows)
        {
            if (row.IsDirectory
                && string.Equals(WebContextMenuPolicy.NormalizePath(row.FullPath), target, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
        }
        string root = WebContextMenuPolicy.NormalizePath(diskTab.RootPathInput);
        if (string.Equals(root, target, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }
        if (root.Length == 0)
        {
            return "此电脑视图没有具体目录，请先打开某个磁盘或目录";
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

    /// <summary>保存共享设置并让本工作区全部文件标签按新设置刷新。</summary>
    private void SaveSettingsAndRefresh()
    {
        new AppSettingsStore().Save(this.settings);
        foreach (object tab in this.tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null)
            {
                fileTab.Refresh();
            }
        }
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 修改共享设置：显示隐藏条目="
            + this.settings.ShowHiddenFiles + " 显示文件扩展名=" + this.settings.ShowFileExtensions);
    }

    /// <summary>把"每行一个路径"的文本拆成路径列表（去掉空行与行尾回车）。</summary>
    public static List<string> SplitPathLines(string text)
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

    /// <summary>解析八进制权限文本（000~777）：必须全部是数字 0~7，超范围返回 false。</summary>
    public static bool TryParseOctalMode(string text, out int mode)
    {
        mode = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        foreach (char digit in text)
        {
            if (digit < '0' || digit > '7')
            {
                return false;
            }
            mode = (mode * 8) + (digit - '0');
        }
        return mode <= 511;
    }

    /// <summary>读取本工作区 IDE 指定编辑器的当前文本（返回格式：令牌 + 换行 + 文本）。</summary>
    public string ReadIdeText(int index)
    {
        return this.ideShell.ReadEditorText(index);
    }

    /// <summary>读取文件内容供网页端只读编辑器展示；路径必须是可访问的普通文件。</summary>
    public string ReadFileText(string path)
    {
        FileTabViewModel? current = this.CurrentFileTab();
        IFileContentAccess? contentAccess = current == null ? null : current.ContentAccess;
        if (contentAccess == null)
        {
            return WebWorkspace.ReadFileTextLocal(path);
        }
        return WebWorkspace.FormatFileText(contentAccess.ReadAllBytes(path));
    }
    /// <summary>按本机文件系统读取文件内容（跟随桌面模式与本地路径共用）。</summary>
    public static string ReadFileTextLocal(string path)
    {
        return WebWorkspace.FormatFileText(File.ReadAllBytes(path));
    }

    /// <summary>把文件字节转成可显示的文本；超过上限时只展示前 512 KB 并注明总字节数。</summary>
    private static string FormatFileText(byte[] bytes)
    {
        const int MaxBytes = 512 * 1024;
        int length = bytes.Length;
        if (length > MaxBytes)
        {
            length = MaxBytes;
        }
        string text = Encoding.UTF8.GetString(bytes, 0, length);
        if (bytes.Length > MaxBytes)
        {
            text = text + "\n\n（文件较大，仅显示前 512 KB，共 " + bytes.Length + " 字节）";
        }
        return text;
    }

    /// <summary>写入文件内容（需权限方案放行修改，由调用方先判定）。</summary>
    public void WriteFile(string path, byte[] content)
    {
        FileTabViewModel? current = this.CurrentFileTab();
        IFileContentAccess? contentAccess = current == null ? null : current.ContentAccess;
        if (contentAccess != null)
        {
            contentAccess.WriteAllBytes(path, content);
            return;
        }
        File.WriteAllBytes(path, content);
    }

    /// <summary>在 UI 线程构建当前工作区快照。</summary>
    public Task<DebugUiState> BuildStateAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(this.BuildState());
        }
        return Dispatcher.UIThread.InvokeAsync(new Func<DebugUiState>(this.BuildState)).GetTask();
    }

    /// <summary>构建当前工作区快照（必须在 UI 线程调用）。</summary>
    private DebugUiState BuildState()
    {
        this.LastAccessTime = DateTime.Now;
        this.ApplySharedSortToFileTabs();
        DebugUiState state = new DebugUiState();
        state.GeneratedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        state.Theme = this.settings.Theme;
        state.WorkspaceId = this.Id;
        state.FollowDesktop = this.FollowDesktop;
        state.Notice = this.lastNotice;
        state.PendingHostKey = this.pendingHostKey;
        state.TabCount = this.tabs.Count;
        int index = 0;
        foreach (object tab in this.tabs)
        {
            DebugTabState tabState = new DebugTabState();
            tabState.Index = index;
            tabState.Selected = index == this.selectedIndex;
            tabState.Title = this.DescribeTitle(tab);
            TabSnapshotBuilder.Fill(tab, tabState);
            if (tabState.Selected)
            {
                // 网页端台前心跳：页面仍在轮询时保持台前周期刷新/探测，页面关闭后自动停止
                ProcessPanelViewModel? heartbeatProcess = tab as ProcessPanelViewModel;
                if (heartbeatProcess != null)
                {
                    heartbeatProcess.NotifyVisible();
                }
                DiskViewPanelViewModel? heartbeatDisk = tab as DiskViewPanelViewModel;
                if (heartbeatDisk != null)
                {
                    heartbeatDisk.NotifyVisible();
                }
            }
            state.Tabs.Add(tabState);
            index++;
        }

        // 视图开关（独立工作区读写同一份设置，与桌面保持一致）
        state.ShowHiddenFiles = this.settings.ShowHiddenFiles;
        state.ShowFileExtensions = this.settings.ShowFileExtensions;
        state.FileVisibleColumns = this.settings.FileVisibleColumns;
        state.FileColumnWidths = this.settings.FileColumnWidths;
        state.FileCompactMode = this.settings.FileCompactMode;
        state.FileSortField = this.settings.FileSortField;
        state.FileSortAscending = this.settings.FileSortAscending;
        state.ClipboardHasFiles = this.clipboardPaths != null && this.clipboardPaths.Count > 0;
        state.ClipboardIsCut = this.clipboardIsCut;
        foreach (FileTaskSnapshot task in this.fileTaskCenter.Snapshot())
        {
            state.Tasks.Add(task);
        }
        foreach (FileTaskSnapshot task in RemoteTaskAdapter.Snapshot(this.tabs))
        {
            state.Tasks.Add(task);
        }

        // IDE 模式镜像（独立工作区自己的 IDE 外壳，与桌面 IDE 外壳同构）
        state.IsIdeMode = this.isIdeMode;
        if (this.isIdeMode)
        {
            state.Ide = this.ideShell.BuildMirrorState();
        }

        // 此电脑分组：磁盘分区（独立工作区没有桌面书签，侧边栏只给磁盘入口）
        FileTabViewModel? currentFileTab = this.CurrentFileTab();
        string currentPath = currentFileTab != null ? currentFileTab.CurrentPath : string.Empty;
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            state.NavPc.Add(new DebugNavItem
            {
                Name = "本地磁盘 (" + drive.Name.TrimEnd(Path.DirectorySeparatorChar) + ")",
                Path = drive.Name,
                IconCategory = FileIconCatalog.Drive,
                Selected = !string.IsNullOrEmpty(currentPath)
                    && currentPath.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase),
            });
        }
        return state;
    }

    /// <summary>读取工作区共享列配置（在界面线程取设置与当前标签列宽）。</summary>
    public FileColumnConfig GetFileColumnConfig()
    {
        return this.RunOnUiThread<FileColumnConfig>(delegate
        {
            FileTabViewModel? tab = this.CurrentFileTab();
            return new FileColumnConfig
            {
                VisibleColumns = this.settings.FileVisibleColumns,
                ColumnWidths = tab != null ? tab.ColumnWidths : this.settings.FileColumnWidths,
                CompactMode = this.settings.FileCompactMode,
                SortField = tab != null ? tab.SortField : this.settings.FileSortField,
                SortAscending = tab != null ? tab.SortAscending : this.settings.FileSortAscending,
            };
        });
    }

    /// <summary>应用列配置到工作区全部文件标签并保存一次设置。</summary>
    public void ApplyFileColumnConfig(FileColumnConfig config)
    {
        if (config == null)
        {
            return;
        }
        this.RunOnUiThread<bool>(delegate
        {
            this.settings.FileVisibleColumns = config.VisibleColumns;
            this.settings.FileColumnWidths = config.ColumnWidths;
            this.settings.FileCompactMode = config.CompactMode;
            this.settings.FileSortField = config.SortField;
            this.settings.FileSortAscending = config.SortAscending;
            foreach (object tab in this.tabs)
            {
                FileTabViewModel? fileTab = tab as FileTabViewModel;
                if (fileTab == null)
                {
                    continue;
                }
                fileTab.ApplyColumnConfiguration(config.VisibleColumns, config.ColumnWidths, config.CompactMode, false);
                fileTab.ApplySortConfiguration(config.SortField, config.SortAscending, false);
            }
            new AppSettingsStore().Save(this.settings);
            return true;
        });
    }

    /// <summary>把共享排序设置同步到工作区所有文件标签（仅在设置与标签不一致时重排）。</summary>
    private void ApplySharedSortToFileTabs()
    {
        string field = this.settings.FileSortField;
        if (field != "Name" && field != "Modified" && field != "Type" && field != "Size" && field != "Attributes")
        {
            field = "Name";
        }
        foreach (object tab in this.tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null && (fileTab.SortField != field || fileTab.SortAscending != this.settings.FileSortAscending))
            {
                fileTab.ApplySortConfiguration(field, this.settings.FileSortAscending, false);
            }
        }
    }

    /// <summary>读取工作区统一任务快照（本地 + 远程映射）。</summary>
    public List<FileTaskSnapshot> GetFileTasks()
    {
        return this.RunOnUiThread<List<FileTaskSnapshot>>(delegate
        {
            List<FileTaskSnapshot> result = new List<FileTaskSnapshot>(this.fileTaskCenter.Snapshot());
            result.AddRange(RemoteTaskAdapter.Snapshot(this.tabs));
            return result;
        });
    }

    /// <summary>提交本地任务。</summary>
    public string StartFileTask(FileTaskRequest request)
    {
        return this.fileTaskCenter.Submit(request);
    }

    /// <summary>取消任务：先查本地任务，再映射远程队列。</summary>
    public bool CancelFileTask(string id)
    {
        if (this.fileTaskCenter.Cancel(id))
        {
            return true;
        }
        return this.RunOnUiThread<bool>(delegate { return RemoteTaskAdapter.Cancel(this.tabs, id); });
    }

    /// <summary>重试失败任务：先查本地任务，再映射远程队列继续。</summary>
    public bool RetryFileTask(string id)
    {
        if (this.fileTaskCenter.Retry(id))
        {
            return true;
        }
        return this.RunOnUiThread<bool>(delegate { return RemoteTaskAdapter.Retry(this.tabs, id); });
    }

    /// <summary>处理本地任务冲突询问。</summary>
    public bool ResolveFileTaskConflict(string id, string action, bool applyToAll)
    {
        return this.fileTaskCenter.ResolveConflict(id, action, applyToAll);
    }

    /// <summary>当前选中的文件标签；不是文件标签时返回 null。</summary>
    private FileTabViewModel? CurrentFileTab()
    {
        if (this.selectedIndex < 0 || this.selectedIndex >= this.tabs.Count)
        {
            return null;
        }
        return this.tabs[this.selectedIndex] as FileTabViewModel;
    }

    /// <summary>当前选中的磁盘视图标签；不是磁盘视图标签时返回 null。</summary>
    private DiskViewPanelViewModel? CurrentDiskTab()
    {
        if (this.selectedIndex < 0 || this.selectedIndex >= this.tabs.Count)
        {
            return null;
        }
        return this.tabs[this.selectedIndex] as DiskViewPanelViewModel;
    }

    /// <summary>收集当前已打开的本地目录：文件标签取当前目录，磁盘视图标签取根目录；远程与压缩包标签不参与。</summary>
    public List<string> GetOpenLocalDirectories()
    {
        List<string> directories = new List<string>();
        foreach (object tab in this.tabs)
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
                // 磁盘视图里已展开的目录也是合法的右键目标父目录，否则深层条目会被误判为越权
                foreach (DiskRow row in diskTab.Rows)
                {
                    if (row.IsDirectory && !string.IsNullOrEmpty(row.FullPath))
                    {
                        directories.Add(row.FullPath);
                    }
                }
            }
        }
        return directories;
    }

    /// <summary>
    /// 构建网页端右键菜单的系统项：先校验目标必须落在已打开标签内，
    /// 再按标签类型判定系统外壳菜单是否可用（远程、压缩包与非 Windows 返回受限菜单）。
    /// </summary>
    /// <param name="paths">目标绝对路径列表。</param>
    /// <param name="isBackground">是否为列表空白处右键。</param>
    public WebContextMenuResult BuildContextMenu(IReadOnlyList<string> paths, bool isBackground)
    {
        ContextMenuTargetDecision decision = this.RunOnUiThread<ContextMenuTargetDecision>(
            delegate { return this.EvaluateContextMenuTargets(paths, isBackground); });
        ISystemContextMenuService? service = decision.MatchedLocalTab
            ? this.services.GetService<ISystemContextMenuService>()
            : null;
        WebContextMenuResult result = WebContextMenuBuilder.Build(service, paths, isBackground,
            decision.MatchedLocalTab ? string.Empty : decision.Note);
        UiLogger.Log("网页端右键菜单", "构建结果：工作区=" + this.Id + " 落点=" + (isBackground ? "空白处" : "条目")
            + " 目标数=" + paths.Count + " 系统项可用=" + result.SystemMenuAvailable
            + " 顶层条目数=" + result.Items.Count + " 说明=" + (result.SystemMenuNote.Length == 0 ? "无" : result.SystemMenuNote));
        return result;
    }

    /// <summary>校验并执行一条系统外壳命令编号：只允许当前已打开标签内的路径，越权直接抛异常。</summary>
    /// <param name="commandId">外壳菜单命令编号。</param>
    /// <param name="paths">目标绝对路径列表（必须与枚举时一致）。</param>
    /// <param name="isBackground">是否为列表空白处右键。</param>
    public bool ExecuteContextMenuCommand(int commandId, IReadOnlyList<string> paths, bool isBackground)
    {
        this.RunOnUiThread<bool>(delegate
        {
            this.EvaluateContextMenuTargets(paths, isBackground);
            return true;
        });
        ISystemContextMenuService? service = this.services.GetService<ISystemContextMenuService>();
        if (service == null || !service.IsSupported)
        {
            UiLogger.Log("网页端右键菜单", "拒绝执行：当前平台不支持系统外壳命令 工作区=" + this.Id + " 命令编号=" + commandId);
            return false;
        }
        EnumerationState state = new EnumerationState();
        Thread thread = new Thread(delegate ()
        {
            try
            {
                state.Success = service.ExecuteShellMenuCommand(commandId, paths, isBackground);
            }
            catch (Exception error)
            {
                state.Error = error;
            }
            finally
            {
                state.Completed.Set();
            }
        });
        thread.IsBackground = true;
        thread.Name = "网页端右键命令执行";
        if (OperatingSystem.IsWindows())
        {
            try
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            catch (Exception)
            {
                // 运行时不允许设置套间时保持默认
            }
        }
        thread.Start();
        if (!state.Completed.Wait(30000))
        {
            UiLogger.Log("网页端右键菜单", "执行系统外壳命令超时：工作区=" + this.Id + " 命令编号=" + commandId
                + " 目标=" + string.Join("、", paths) + " 结果=已放弃等待，扩展可能仍在运行");
            return false;
        }
        if (state.Error != null)
        {
            UiLogger.LogError("网页端右键菜单", "执行系统外壳命令失败：工作区=" + this.Id + " 命令编号=" + commandId
                + " 目标=" + string.Join("、", paths), state.Error);
            return false;
        }
        UiLogger.Log("网页端右键菜单", "执行系统外壳命令：工作区=" + this.Id + " 命令编号=" + commandId
            + " 落点=" + (isBackground ? "空白处" : "条目") + " 目标=" + string.Join("、", paths)
            + " 结果=" + (state.Success ? "成功，已交给外壳扩展执行" : "失败，外壳扩展未接受该命令"));
        return state.Success;
    }

    /// <summary>在界面线程上评估右键目标：校验越权并判定系统外壳菜单是否可用。</summary>
    private ContextMenuTargetDecision EvaluateContextMenuTargets(IReadOnlyList<string> paths, bool isBackground)
    {
        WebContextMenuPolicy.Validate(this.GetOpenLocalDirectories(), this.CollectVisibleItemsForContextMenu(),
            paths, isBackground);
        ContextMenuTargetDecision decision = new ContextMenuTargetDecision();
        decision.Note = "当前标签是远程连接、压缩包或非本机位置，系统右键项不可用，仅保留本程序命令";
        foreach (object tab in this.tabs)
        {
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null && this.MatchesFileTab(fileTab, paths, isBackground))
            {
                decision.MatchedAnyTab = true;
                if (fileTab.FileSystemProvider is DefaultFileSystemProvider && fileTab.ContentAccess == null)
                {
                    decision.MatchedLocalTab = true;
                }
            }
            DiskViewPanelViewModel? diskTab = tab as DiskViewPanelViewModel;
            if (diskTab != null && this.MatchesDiskTab(diskTab, paths, isBackground))
            {
                decision.MatchedAnyTab = true;
                decision.MatchedLocalTab = true;
            }
        }
        if (!decision.MatchedAnyTab)
        {
            UiLogger.Log("网页端右键菜单", "拒绝：右键目标不在任何已打开标签内 工作区=" + this.Id
                + " 落点=" + (isBackground ? "空白处" : "条目") + " 目标=" + string.Join("、", paths)
                + " 结果=未枚举也未执行任何外壳命令");
            throw new UnauthorizedAccessException("右键目标不在当前已打开的标签内");
        }
        return decision;
    }

    /// <summary>在界面线程执行并等待结果；已处于界面线程时直接执行。</summary>
    private T RunOnUiThread<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }
        return Dispatcher.UIThread.InvokeAsync(action).GetTask().GetAwaiter().GetResult();
    }

    /// <summary>一次右键目标评估结果。</summary>
    private sealed class ContextMenuTargetDecision
    {
        public bool MatchedAnyTab { get; set; }

        public bool MatchedLocalTab { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>收集当前标签的可见条目路径（磁盘视图用可见行，文件标签用可见行）；无当前标签时为空。</summary>
    private List<string> CollectVisibleItemsForContextMenu()
    {
        List<string> items = new List<string>();
        FileTabViewModel? fileTab = this.CurrentFileTab();
        if (fileTab != null)
        {
            foreach (FileRow row in fileTab.VisibleRows)
            {
                items.Add(row.FullPath);
            }
            return items;
        }
        DiskViewPanelViewModel? diskTab = this.CurrentDiskTab();
        if (diskTab != null)
        {
            foreach (DiskRow row in diskTab.Rows)
            {
                items.Add(row.FullPath);
            }
        }
        return items;
    }

    /// <summary>判断目标是否属于某个文件标签：空白处等于当前目录，条目与当前目录同父。</summary>
    private bool MatchesFileTab(FileTabViewModel tab, IReadOnlyList<string> paths, bool isBackground)
    {
        string current = WebContextMenuPolicy.NormalizePath(tab.CurrentPath);
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

    /// <summary>判断目标是否属于某个磁盘视图标签：空白处等于根目录，条目必须出现在可见行内。</summary>
    private bool MatchesDiskTab(DiskViewPanelViewModel tab, IReadOnlyList<string> paths, bool isBackground)
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

    /// <summary>右键枚举线程的状态容器。</summary>
    private sealed class EnumerationState
    {
        public bool Success { get; set; }

        public Exception? Error { get; set; }

        public ManualResetEventSlim Completed { get; } = new ManualResetEventSlim(false);
    }

    /// <summary>取标签标题，用于日志与网页标签条。</summary>
    private string DescribeTitle(object tab)
    {
        FileTabViewModel? fileTab = tab as FileTabViewModel;
        if (fileTab != null)
        {
            return fileTab.Title;
        }
        DiskViewPanelViewModel? diskTab = tab as DiskViewPanelViewModel;
        if (diskTab != null)
        {
            return diskTab.Title;
        }
        ProcessPanelViewModel? processTab = tab as ProcessPanelViewModel;
        if (processTab != null)
        {
            return processTab.Title;
        }
        SearchTabViewModel? searchTab = tab as SearchTabViewModel;
        if (searchTab != null)
        {
            return searchTab.Title;
        }
        ConnectionManagerViewModel? connectionsTab = tab as ConnectionManagerViewModel;
        if (connectionsTab != null)
        {
            return connectionsTab.Title;
        }
        TaskBoardViewModel? boardTab = tab as TaskBoardViewModel;
        if (boardTab != null)
        {
            return boardTab.Title;
        }
        CompareTabViewModel? compareTab = tab as CompareTabViewModel;
        if (compareTab != null)
        {
            return compareTab.Title;
        }
        StartupManagerViewModel? startupTab = tab as StartupManagerViewModel;
        if (startupTab != null)
        {
            return startupTab.Title;
        }
        return tab.GetType().Name;
    }
}

/// <summary>
/// 网页端工作区桥接：把跟随桌面模式与独立工作区两种来源统一成同一组操作。
/// 由应用层实现，网页端服务只依赖该接口，不直接触碰桌面窗口。
/// </summary>
public interface IWebWorkspaceBridge
{
    /// <summary>读取跟随桌面模式的界面快照。</summary>
    Task<DebugUiState> GetDesktopStateAsync();

    /// <summary>读取桌面 IDE 指定编辑器的当前文本（返回格式：令牌 + 换行 + 文本）。</summary>
    string ReadDesktopIdeText(int index);

    /// <summary>在桌面上切换标签。</summary>
    void SelectDesktopTab(int index);

    /// <summary>在桌面的文件标签中打开路径。</summary>
    void OpenDesktopPath(string path);

    /// <summary>在桌面上打开磁盘视图标签（已存在时直接选中）。</summary>
    void OpenDesktopDiskView();

    /// <summary>桌面当前磁盘视图标签切换展开状态；空路径回到根视图，其余路径原地展开或收起（不进入下一级）。</summary>
    void NavigateDesktopDisk(string path);

    /// <summary>桌面当前标签返回上一级：文件标签回上级目录，磁盘视图标签回上级扫描根。</summary>
    void NavigateDesktopUp();

    /// <summary>应用界面主题（深浅两套）。</summary>
    void ApplyTheme(string theme);

    /// <summary>执行跟随桌面模式的界面动作（排序、历史、搜索、勾选、剪贴板、视图、开关等），返回给页面的提示文字。</summary>
    string ExecuteDesktopAction(string action, string argument);

    /// <summary>读取截图录屏状态与产物历史（JSON 文本：保存目录、命名模板、格式、开关、历史条目）。</summary>
    string GetCaptureStateJson();

    /// <summary>执行截图录屏动作（fullscreen / region / pin / pinSave / delete），返回 JSON 文本。</summary>
    string ExecuteCaptureAction(string action, string argument);

    /// <summary>构建桌面当前标签的系统右键菜单层级（校验目标必须位于桌面已打开标签内）。</summary>
    WebContextMenuResult BuildDesktopContextMenu(IReadOnlyList<string> paths, bool isBackground);

    /// <summary>执行桌面当前标签的一条系统外壳命令编号（编号来自 <see cref="BuildDesktopContextMenu"/>）。</summary>
    bool ExecuteDesktopContextMenuCommand(int commandId, IReadOnlyList<string> paths, bool isBackground);

    /// <summary>在桌面当前文件标签中新建文件夹。</summary>
    void CreateDesktopFolder(string folderName);

    /// <summary>重命名桌面当前文件标签中指定路径的条目。</summary>
    void RenameDesktopEntry(string path, string newName);

    /// <summary>删除桌面当前文件标签中指定路径的条目（移入回收站）。</summary>
    void DeleteDesktopEntry(string path);

    /// <summary>把网页端编辑器内容写入指定文件。</summary>
    void WriteDesktopFile(string path, byte[] content);

    /// <summary>创建一个新的独立工作区。</summary>
    WebWorkspace CreateWorkspace();

    /// <summary>按编号查找独立工作区；不存在或已过期返回 null。</summary>
    WebWorkspace? FindWorkspace(string id);

    /// <summary>销毁指定独立工作区。</summary>
    void RemoveWorkspace(string id);

    /// <summary>读取文件列表列配置：工作区编号为空时取桌面共享设置。</summary>
    FileColumnConfig GetFileColumnConfig(string workspaceId);

    /// <summary>应用文件列表列配置：工作区编号为空时应用到桌面全部文件标签。</summary>
    void ApplyFileColumnConfig(string workspaceId, FileColumnConfig config);

    /// <summary>读取统一任务快照：工作区编号为空时取桌面任务与远程传输映射。</summary>
    List<FileTaskSnapshot> GetFileTasks(string workspaceId);

    /// <summary>提交本地任务，返回任务编号。</summary>
    string StartFileTask(string workspaceId, FileTaskRequest request);

    /// <summary>取消任务（本地或远程）。</summary>
    bool CancelFileTask(string workspaceId, string id);

    /// <summary>重试失败任务（本地或远程继续）。</summary>
    bool RetryFileTask(string workspaceId, string id);

    /// <summary>处理冲突询问。</summary>
    bool ResolveFileTaskConflict(string workspaceId, string id, string action, bool applyToAll);
}
