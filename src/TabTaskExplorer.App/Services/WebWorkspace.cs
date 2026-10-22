using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Abstractions;
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

    /// <summary>独立工作区自己的剪贴板：被复制或剪切的条目路径。</summary>
    private List<string>? clipboardPaths;

    /// <summary>剪贴板是否处于剪切模式（粘贴时移动而非复制）。</summary>
    private bool clipboardIsCut;

    /// <summary>独立工作区自己的 IDE 外壳（资源树 + 编辑器标签 + 查找面板）。</summary>
    private readonly IdeShellViewModel ideShell;

    /// <summary>该工作区当前是否处于 IDE 模式。</summary>
    private bool isIdeMode;

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
        FileTabViewModel tab = new FileTabViewModel(provider, this.settings, path, scanService, archiveService);
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
            this.services.GetRequiredService<DiskScanService>());
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
        }
        UiLogger.Log("网页端工作区", "工作区=" + this.Id + " 已释放标签资源：磁盘视图、进程监控与文件标签的在途回填全部停止");
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
            return this.PasteClipboard(fileTab);
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
        if (action == "openboard" || action == "opensearch" || action == "openconnections")
        {
            return "独立工作区暂不支持该视图，请在跟随桌面模式下打开";
        }
        if (action == "opendisk")
        {
            this.AddDiskViewTab();
            return string.Empty;
        }
        if (action == "openprocess")
        {
            this.AddProcessTab();
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

    /// <summary>把剪贴板中的条目复制或移动到当前目录（独立工作区自己的剪贴板）。</summary>
    private string PasteClipboard(FileTabViewModel fileTab)
    {
        if (this.clipboardPaths == null || this.clipboardPaths.Count == 0)
        {
            return "剪贴板为空，请先复制或剪切";
        }
        if (string.IsNullOrEmpty(fileTab.CurrentPath))
        {
            return "请先进入目标文件夹";
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
                    fileSystem.Move(path, fileTab.CurrentPath);
                }
                else
                {
                    fileSystem.Copy(path, fileTab.CurrentPath);
                }
                successCount++;
            }
            catch (Exception)
            {
                failedCount++;
            }
        }
        if (this.clipboardIsCut)
        {
            this.clipboardPaths = null;
        }
        fileTab.ShowMessage("粘贴完成：成功 " + successCount + " 项，失败 " + failedCount + " 项");
        fileTab.Refresh();
        return "粘贴完成：成功 " + successCount + " 项，失败 " + failedCount + " 项";
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

    /// <summary>把走势图序列复制到快照列表（复制而非引用，避免快照与界面状态竞争）。</summary>
    private static void CopyTrend(IReadOnlyList<double> source, List<double> target)
    {
        target.Clear();
        for (int index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
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
        DebugUiState state = new DebugUiState();
        state.GeneratedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        state.Theme = this.settings.Theme;
        state.WorkspaceId = this.Id;
        state.FollowDesktop = this.FollowDesktop;
        state.TabCount = this.tabs.Count;
        int index = 0;
        foreach (object tab in this.tabs)
        {
            DebugTabState tabState = new DebugTabState();
            tabState.Index = index;
            tabState.Selected = index == this.selectedIndex;
            tabState.Title = this.DescribeTitle(tab);
            FileTabViewModel? fileTab = tab as FileTabViewModel;
            if (fileTab != null)
            {
                tabState.TabType = "file";
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
                tabState.TotalRows = fileTab.VisibleRows.Count;
                int limit = tabState.TotalRows > 1000 ? 1000 : tabState.TotalRows;
                for (int rowIndex = 0; rowIndex < limit; rowIndex++)
                {
                    FileRow row = fileTab.VisibleRows[rowIndex];
                    DebugFileRow debugRow = new DebugFileRow();
                    debugRow.Name = row.DisplayName;
                    debugRow.IsDirectory = row.IsDirectory;
                    debugRow.FullPath = row.FullPath;
                    debugRow.Type = row.TypeText;
                    debugRow.Size = row.SizeText;
                    debugRow.Modified = row.ModifiedText;
                    debugRow.IsSelected = row.IsSelected;
                    debugRow.IconGlyph = row.IconGlyph;
                    debugRow.IconColor = row.IconColor;
                    tabState.Rows.Add(debugRow);
                }
            }
            else
            {
                DiskViewPanelViewModel? diskTab = tab as DiskViewPanelViewModel;
                ProcessPanelViewModel? processTab = tab as ProcessPanelViewModel;
                if (processTab != null)
                {
                    tabState.TabType = "process";
                    tabState.ResourceText = processTab.ResourceText;
                    tabState.SummaryText = processTab.SummaryText;
                    tabState.TotalRows = processTab.Rows.Count;
                    tabState.DetailText = processTab.DetailText;
                    tabState.AffinityInput = processTab.AffinityInput;
                    tabState.PriorityIndex = processTab.SelectedPriorityIndex;
                    tabState.DetailSupported = processTab.IsDetailSupported;
                    tabState.TrendVersion = processTab.TrendUpdatedAt.Ticks;
                    tabState.SortField = processTab.SortField;
                    tabState.SortAscending = processTab.SortAscending;
                    CopyTrend(processTab.SystemCpuTrend, tabState.SystemCpuTrend);
                    CopyTrend(processTab.SystemMemoryTrend, tabState.SystemMemoryTrend);
                    CopyTrend(processTab.SelectedCpuTrend, tabState.SelectedCpuTrend);
                    if (tabState.Selected)
                    {
                        // 网页端台前心跳：页面仍在轮询时保持台前周期刷新，页面关闭后自动停止
                        processTab.NotifyVisible();
                    }
                    int processLimit = tabState.TotalRows > 1000 ? 1000 : tabState.TotalRows;
                    for (int processIndex = 0; processIndex < processLimit; processIndex++)
                    {
                        ProcessRow row = processTab.Rows[processIndex];
                        DebugProcessRow debugRow = new DebugProcessRow();
                        debugRow.ProcessId = row.ProcessId;
                        debugRow.IndentText = row.IndentText;
                        debugRow.Name = row.Name;
                        debugRow.CpuText = row.CpuText;
                        debugRow.MemoryText = row.MemoryText;
                        debugRow.WindowTitle = row.WindowTitle;
                        debugRow.IsSelected = object.ReferenceEquals(row, processTab.SelectedRow);
                        tabState.ProcessRows.Add(debugRow);
                    }
                }
                else if (diskTab != null)
                {
                    tabState.TabType = "disk";
                    tabState.CurrentPath = diskTab.RootPathInput;
                    tabState.SummaryText = diskTab.SummaryText;
                    tabState.TotalRows = diskTab.Rows.Count;
                    tabState.SortField = diskTab.SortField;
                    tabState.SortAscending = diskTab.SortAscending;
                    if (tabState.Selected)
                    {
                        // 网页端台前心跳：页面仍在轮询时保持台前探测，页面关闭后探测自动停止
                        diskTab.NotifyVisible();
                    }
                    foreach (DiskRow row in diskTab.Rows)
                    {
                        DebugFileRow debugRow = new DebugFileRow();
                        debugRow.Name = row.Name;
                        debugRow.IsDirectory = row.IsDirectory;
                        debugRow.FullPath = row.FullPath;
                        debugRow.Type = row.EntryTypeText;
                        debugRow.Size = row.SizeText;
                        debugRow.Free = row.FreeText;
                        debugRow.Usage = row.UsageText;
                        debugRow.FileSystem = row.FileSystemText;
                        debugRow.Level = row.Level;
                        debugRow.Expanded = row.IsExpanded;
                        tabState.Rows.Add(debugRow);
                    }
                }
            }
            state.Tabs.Add(tabState);
            index++;
        }

        // 视图开关（独立工作区读写同一份设置，与桌面保持一致）
        state.ShowHiddenFiles = this.settings.ShowHiddenFiles;
        state.ShowFileExtensions = this.settings.ShowFileExtensions;

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
                IconGlyph = "\uED3B",
                Selected = !string.IsNullOrEmpty(currentPath)
                    && currentPath.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase),
            });
        }
        return state;
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
}
