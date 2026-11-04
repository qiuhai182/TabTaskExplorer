using System;
using System.Collections.Generic;
using TabTaskExplorer.App.Services.Tasks;

namespace TabTaskExplorer.App.Services;

/// <summary>网页端服务的界面状态快照：与桌面界面同构。</summary>
public class DebugUiState
{
    /// <summary>快照生成时间。</summary>
    public string GeneratedTime { get; set; } = string.Empty;

    /// <summary>工作区标识：独立工作区各自的编号，跟随桌面时为空。</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>是否跟随桌面主窗口（跟随模式下标签与桌面完全一致）。</summary>
    public bool FollowDesktop { get; set; }

    /// <summary>服务端一次性提示（例如远程连接失败原因）；网页端读取后以浮动提示展示。</summary>
    public string Notice { get; set; } = string.Empty;

    /// <summary>待确认的主机密钥（首次连接或密钥变更）；为空表示没有待确认项。</summary>
    public DebugPendingHostKey? PendingHostKey { get; set; }

    /// <summary>标签总数（不受行数上限影响）。</summary>
    public int TabCount { get; set; }

    /// <summary>当前主题：Dark / Light（镜像页面跟随配色）。</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>是否显示隐藏条目（与桌面设置同步）。</summary>
    public bool ShowHiddenFiles { get; set; }

    /// <summary>是否显示文件扩展名（与桌面设置同步）。</summary>
    public bool ShowFileExtensions { get; set; }

    /// <summary>文件列表可见列（与桌面共用 AppSettings）。</summary>
    public string FileVisibleColumns { get; set; } = "Name,Modified,Type,Size,Attributes";

    /// <summary>文件列表列宽（含勾选框与图标列，逗号分隔像素）。</summary>
    public string FileColumnWidths { get; set; } = string.Empty;

    /// <summary>文件列表紧凑模式。</summary>
    public bool FileCompactMode { get; set; }

    /// <summary>文件列表共享排序字段。</summary>
    public string FileSortField { get; set; } = "Name";

    /// <summary>文件列表共享排序方向。</summary>
    public bool FileSortAscending { get; set; } = true;

    /// <summary>统一任务中心快照（本地任务在前，远程传输映射在后）。</summary>
    public List<FileTaskSnapshot> Tasks { get; } = new List<FileTaskSnapshot>();

    /// <summary>全部标签页（与桌面标签条同序）。</summary>
    public List<DebugTabState> Tabs { get; } = new List<DebugTabState>();

    /// <summary>最近访问的路径（与桌面地址栏"最近"列表同构，新→旧）。</summary>
    public List<string> RecentPaths { get; } = new List<string>();

    /// <summary>侧边栏：快捷访问组（与桌面导航栏同构）。</summary>
    public List<DebugNavItem> NavPinned { get; } = new List<DebugNavItem>();

    /// <summary>侧边栏：此电脑组（与桌面导航栏同构）。</summary>
    public List<DebugNavItem> NavPc { get; } = new List<DebugNavItem>();

    /// <summary>当前是否处于 IDE 模式（与桌面主窗口同构）。</summary>
    public bool IsIdeMode { get; set; }

    /// <summary>IDE 模式状态（IsIdeMode 为真时由网页端渲染：资源树 + 编辑器 + 查找面板）。</summary>
    public DebugIdeState Ide { get; set; } = new DebugIdeState();
}

/// <summary>IDE 模式镜像状态：工作区资源树、编辑器标签、查找替换与快速打开。</summary>
public class DebugIdeState
{
    /// <summary>工作区根路径显示文字（远程加连接名前缀）。</summary>
    public string WorkspaceRootText { get; set; } = string.Empty;

    /// <summary>IDE 状态栏左侧文字（加载 / 打开 / 保存等进度提示）。</summary>
    public string SolutionStatusText { get; set; } = string.Empty;

    /// <summary>工作区是否正在加载。</summary>
    public bool IsWorkspaceLoading { get; set; }

    /// <summary>是否显示底部查找面板。</summary>
    public bool ShowFindPanel { get; set; }

    /// <summary>查找关键字。</summary>
    public string FindText { get; set; } = string.Empty;

    /// <summary>替换文字。</summary>
    public string ReplaceText { get; set; } = string.Empty;

    /// <summary>查找状态提示（找到位置 / 未找到等）。</summary>
    public string FindStatusText { get; set; } = string.Empty;

    /// <summary>查找命中起始字符位置（网页端在编辑区选中该命中）。</summary>
    public int FindSelectionStart { get; set; } = -1;

    /// <summary>查找命中长度。</summary>
    public int FindSelectionLength { get; set; }

    /// <summary>是否显示快速打开浮层（Ctrl+P）。</summary>
    public bool ShowQuickOpen { get; set; }

    /// <summary>快速打开关键字。</summary>
    public string QuickOpenText { get; set; } = string.Empty;

    /// <summary>当前激活的编辑器下标；无激活编辑器时为 -1。</summary>
    public int ActiveEditorIndex { get; set; } = -1;

    /// <summary>激活编辑器文本令牌（文本变化时随之变化，网页端据此判断是否需要重新拉取全文）。</summary>
    public string ActiveTextToken { get; set; } = string.Empty;

    /// <summary>激活编辑器是否只读（二进制 / 超大文件）。</summary>
    public bool ActiveIsReadOnly { get; set; }

    /// <summary>激活编辑器探测到的编码名称。</summary>
    public string ActiveEncoding { get; set; } = string.Empty;

    /// <summary>激活编辑器的位置显示文字。</summary>
    public string ActiveLocation { get; set; } = string.Empty;

    /// <summary>激活编辑器的总行数。</summary>
    public int ActiveLineCount { get; set; } = 1;

    /// <summary>资源树可见节点（按展开状态拍平，含缩进层级）。</summary>
    public List<DebugIdeTreeNode> Tree { get; } = new List<DebugIdeTreeNode>();

    /// <summary>已打开的编辑器标签。</summary>
    public List<DebugIdeEditor> Editors { get; } = new List<DebugIdeEditor>();

    /// <summary>快速打开候选（最多 200 条，与桌面过滤结果一致）。</summary>
    public List<DebugIdeQuickOpen> QuickOpenItems { get; } = new List<DebugIdeQuickOpen>();

    /// <summary>工作区搜索结果（与桌面查找结果面板同构）。</summary>
    public List<DebugIdeFindResult> FindResults { get; } = new List<DebugIdeFindResult>();

    /// <summary>按文本内容计算稳定令牌（FNV-1a + 长度），网页端比较后决定是否重新拉取全文。</summary>
    public static string ComputeTextToken(string text)
    {
        uint hash = 2166136261;
        for (int index = 0; index < text.Length; index++)
        {
            hash = (hash ^ text[index]) * 16777619u;
        }
        return text.Length + "-" + hash.ToString("x8");
    }
}

/// <summary>IDE 资源树可见节点（已按展开状态拍平）。</summary>
public class DebugIdeTreeNode
{
    /// <summary>缩进层级（0 为根层）。</summary>
    public int Depth { get; set; }

    /// <summary>节点显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>节点完整路径。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>是否已展开（网页端箭头方向）。</summary>
    public bool IsExpanded { get; set; }

    /// <summary>是否为占位节点（加载中 / 无法读取）。</summary>
    public bool IsPlaceholder { get; set; }

    /// <summary>类型图标分类键（见 <see cref="FileIconCatalog"/>）。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.File;

    /// <summary>类型图标颜色（十六进制）。</summary>
    public string IconColor { get; set; } = FileIconCatalog.GetAccent(FileIconCatalog.File);
}

/// <summary>IDE 编辑器标签镜像。</summary>
public class DebugIdeEditor
{
    /// <summary>标签下标（切换与关闭回传时使用）。</summary>
    public int Index { get; set; }

    /// <summary>显示标题（未保存时带圆点）。</summary>
    public string DisplayTitle { get; set; } = string.Empty;

    /// <summary>提示文字（完整路径）。</summary>
    public string ToolTipText { get; set; } = string.Empty;

    /// <summary>是否有未保存更改（关闭前确认用）。</summary>
    public bool IsDirty { get; set; }

    /// <summary>是否为预览标签（网页端以斜体显示，可被下一次预览替换）。</summary>
    public bool IsPreview { get; set; }

    /// <summary>标签图标分类键。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.File;

    /// <summary>标签图标强调色。</summary>
    public string IconColor { get; set; } = FileIconCatalog.GetAccent(FileIconCatalog.File);
}

/// <summary>IDE 快速打开候选项。</summary>
public class DebugIdeQuickOpen
{
    /// <summary>文件名。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件完整路径。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>所在目录显示文字。</summary>
    public string DirectoryText { get; set; } = string.Empty;
}

/// <summary>IDE 工作区搜索结果条目。</summary>
public class DebugIdeFindResult
{
    /// <summary>结果摘要（文件名 + 行号）。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>该行文本。</summary>
    public string LineText { get; set; } = string.Empty;

    /// <summary>文件完整路径。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>行号（1 基）。</summary>
    public int LineNumber { get; set; }
}

/// <summary>侧边栏导航镜像项。</summary>
public class DebugNavItem
{
    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>目标路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>是否命中当前路径（选中高亮）。</summary>
    public bool Selected { get; set; }

    /// <summary>类型图标分类键（见 <see cref="FileIconCatalog"/>），网页端按同一分类绘制矢量图标。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.Folder;

    /// <summary>图标强调色：跟随分类自动取色。</summary>
    public string IconColor
    {
        get { return FileIconCatalog.GetAccent(this.IconCategory); }
    }
}

/// <summary>单个标签页的镜像状态。</summary>
public class DebugTabState
{
    /// <summary>标签下标（回传选中时使用）。</summary>
    public int Index { get; set; }

    /// <summary>标签类型：file / board / connections。</summary>
    public string TabType { get; set; } = string.Empty;

    /// <summary>标签标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>是否为当前选中标签。</summary>
    public bool Selected { get; set; }

    /// <summary>文件标签：当前路径。</summary>
    public string CurrentPath { get; set; } = string.Empty;

    /// <summary>当前标签的条目总数（行列表可能被上限截断）。</summary>
    public int TotalRows { get; set; }

    /// <summary>当前排序字段：Name / Modified / Type / Size。</summary>
    public string SortField { get; set; } = "Name";

    /// <summary>排序是否为升序（降序时网页端列头显示向下箭头）。</summary>
    public bool SortAscending { get; set; } = true;

    /// <summary>当前搜索关键字（网页端搜索框回填）。</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>视图模式：Details / List / ExtraLargeIcons / LargeIcons / MediumIcons / SmallIcons / Tile / Content。</summary>
    public string ViewMode { get; set; } = "Details";

    /// <summary>是否显示右侧详细信息窗格。</summary>
    public bool ShowInfoPane { get; set; }

    /// <summary>是否可以后退（地址栏后退按钮灰化）。</summary>
    public bool CanGoBack { get; set; }

    /// <summary>是否可以前进（地址栏前进按钮灰化）。</summary>
    public bool CanGoForward { get; set; }

    /// <summary>已勾选的条目数量（状态栏显示）。</summary>
    public int CheckedCount { get; set; }

    /// <summary>标签底部摘要文字（操作结果提示，例如"粘贴完成：成功 3 项"）。</summary>
    public string SummaryText { get; set; } = string.Empty;

    /// <summary>进程监控：顶部资源摘要（处理器占用、内存占用与进程数量，其余标签为空）。</summary>
    public string ResourceText { get; set; } = string.Empty;

    /// <summary>进程监控：选中进程的详情文本（可执行文件、命令行、启动时间等，其余标签为空）。</summary>
    public string DetailText { get; set; } = string.Empty;

    /// <summary>进程监控：CPU 亲和性输入框内容（如"0,1,2"，其余标签为空）。</summary>
    public string AffinityInput { get; set; } = string.Empty;

    /// <summary>进程监控：优先级下拉框当前索引（0 到 5，-1 表示未选中，其余标签为 2）。</summary>
    public int PriorityIndex { get; set; } = 2;

    /// <summary>进程监控：优先级与亲和性调节是否可用（Windows 平台为真，其余平台为假并整体灰化）。</summary>
    public bool DetailSupported { get; set; }

    /// <summary>进程监控：系统处理器占用走势图（最近 60 次采样，0 到 100，其余标签为空）。</summary>
    public List<double> SystemCpuTrend { get; } = new List<double>();

    /// <summary>进程监控：系统内存占用走势图（最近 60 次采样，0 到 100，其余标签为空）。</summary>
    public List<double> SystemMemoryTrend { get; } = new List<double>();

    /// <summary>进程监控：选中进程处理器占用走势图（最近 60 次采样，0 到 100，其余标签为空）。</summary>
    public List<double> SelectedCpuTrend { get; } = new List<double>();

    /// <summary>进程监控：走势图数据更新令牌（每次采样变化时更新，网页端据此判断是否重绘）。</summary>
    public long TrendVersion { get; set; }

    /// <summary>文件标签：可见条目（与桌面列表同构）。</summary>
    public List<DebugFileRow> Rows { get; } = new List<DebugFileRow>();

    /// <summary>比对：模式（directory / file）。</summary>
    public string CompareMode { get; set; } = string.Empty;

    /// <summary>比对：左侧路径。</summary>
    public string LeftPath { get; set; } = string.Empty;

    /// <summary>比对：右侧路径。</summary>
    public string RightPath { get; set; } = string.Empty;

    /// <summary>比对：左侧路径输入框内容。</summary>
    public string LeftPathInput { get; set; } = string.Empty;

    /// <summary>比对：右侧路径输入框内容。</summary>
    public string RightPathInput { get; set; } = string.Empty;

    /// <summary>比对：是否只显示差异。</summary>
    public bool CompareShowOnlyDifferences { get; set; }

    /// <summary>比对：目录模式是否包含子目录。</summary>
    public bool CompareIncludeSubDirectories { get; set; } = true;

    /// <summary>比对：目录模式是否按内容比较。</summary>
    public bool CompareFileContent { get; set; }

    /// <summary>比对：差异位置文字。</summary>
    public string CompareDifferencePosition { get; set; } = string.Empty;

    /// <summary>比对：左侧元信息（文件模式为编码与行数）。</summary>
    public string LeftMetaText { get; set; } = string.Empty;

    /// <summary>比对：右侧元信息（文件模式为编码与行数）。</summary>
    public string RightMetaText { get; set; } = string.Empty;

    /// <summary>比对：可见结果行。</summary>
    public List<DebugCompareRow> CompareRows { get; } = new List<DebugCompareRow>();

    /// <summary>进程监控：进程列表（与桌面进程面板同构）。</summary>
    public List<DebugProcessRow> ProcessRows { get; } = new List<DebugProcessRow>();

    /// <summary>任务看板：待办标题。</summary>
    public List<string> Todo { get; } = new List<string>();

    /// <summary>任务看板：进行中标题。</summary>
    public List<string> InProgress { get; } = new List<string>();

    /// <summary>任务看板：已完成标题。</summary>
    public List<string> Done { get; } = new List<string>();

    /// <summary>连接管理：全部连接（含可编辑字段，网页端据此渲染编辑表单）。</summary>
    public List<DebugConnection> Connections { get; } = new List<DebugConnection>();

    /// <summary>连接管理：当前选中的连接名称。</summary>
    public string SelectedConnection { get; set; } = string.Empty;

    /// <summary>连接管理：当前选中的连接编号（网页端保存、测试、删除与打开时回传）。</summary>
    public string SelectedConnectionId { get; set; } = string.Empty;

    /// <summary>启动项管理：系统启动项列表（与桌面启动项面板同构）。</summary>
    public List<DebugStartupRow> StartupRows { get; } = new List<DebugStartupRow>();

    /// <summary>启动项管理：自定义启动项列表。</summary>
    public List<DebugCustomStartupRow> CustomStartupRows { get; } = new List<DebugCustomStartupRow>();

    /// <summary>启动项管理：平台能力、本次开机标识与自动拉起说明。</summary>
    public string StartupEnvironmentText { get; set; } = string.Empty;
}

/// <summary>文件列表列配置：网页端读写，桌面端应用后共享同一份 AppSettings。</summary>
public class FileColumnConfig
{
    /// <summary>可见列标识（Name/Modified/Type/Size/Attributes）。</summary>
    public string VisibleColumns { get; set; } = "Name,Modified,Type,Size,Attributes";

    /// <summary>列宽（逗号分隔像素，含勾选框与图标列）。</summary>
    public string ColumnWidths { get; set; } = string.Empty;

    /// <summary>紧凑模式。</summary>
    public bool CompactMode { get; set; }

    /// <summary>排序字段。</summary>
    public string SortField { get; set; } = "Name";

    /// <summary>排序是否升序。</summary>
    public bool SortAscending { get; set; } = true;
}

/// <summary>待用户确认的主机密钥信息（网页端据此展示确认对话框）。</summary>
public class DebugPendingHostKey
{
    /// <summary>结构化错误码：HOST_KEY_UNKNOWN（首次连接）或 HOST_KEY_MISMATCH（密钥变更）。</summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>发起连接的连接编号。</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>连接名称。</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>主机地址。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>端口号。</summary>
    public int Port { get; set; }

    /// <summary>本次连接的主机密钥算法。</summary>
    public string KeyType { get; set; } = string.Empty;

    /// <summary>本次连接的 SHA256 指纹。</summary>
    public string FingerprintSha256 { get; set; } = string.Empty;

    /// <summary>密钥变更时已信任的旧指纹；首次连接为空。</summary>
    public string ExpectedFingerprintSha256 { get; set; } = string.Empty;

    /// <summary>可执行的确认动作名称。</summary>
    public string TrustAction { get; set; } = "connhostkeytrust";

    /// <summary>跳板序号：0 表示目标主机，1..3 表示第几跳跳板机。</summary>
    public int HopIndex { get; set; }
}

/// <summary>系统启动项镜像行。</summary>
public class DebugStartupRow
{
    /// <summary>启动项编号（启停时回传）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>类型文字。</summary>
    public string KindText { get; set; } = string.Empty;

    /// <summary>来源文字。</summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>作用范围文字。</summary>
    public string ScopeText { get; set; } = string.Empty;

    /// <summary>启动目标显示文字。</summary>
    public string CommandText { get; set; } = string.Empty;

    /// <summary>状态文字。</summary>
    public string StateText { get; set; } = string.Empty;

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; }

    /// <summary>是否允许修改启停状态。</summary>
    public bool CanToggle { get; set; }

    /// <summary>不可修改时的原因。</summary>
    public string ToggleHint { get; set; } = string.Empty;

    /// <summary>是否为本程序自身的开机自启项（网页端据此在提示中强调会影响「开机启动」设置）。</summary>
    public bool IsSelfAutoStart { get; set; }
}

/// <summary>自定义启动项镜像行。</summary>
public class DebugCustomStartupRow
{
    /// <summary>条目编号。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>类型文字。</summary>
    public string KindText { get; set; } = string.Empty;

    /// <summary>类型标识（网页端编辑表单回填下拉框）。</summary>
    public string ItemType { get; set; } = "Program";

    /// <summary>启动目标原文（网页端编辑表单回填）。</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>启动参数原文（网页端编辑表单回填）。</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>延迟秒数（网页端编辑表单回填）。</summary>
    public int DelaySeconds { get; set; }

    /// <summary>启动目标显示文字。</summary>
    public string TargetText { get; set; } = string.Empty;

    /// <summary>延迟文字。</summary>
    public string DelayText { get; set; } = string.Empty;

    /// <summary>启用状态文字。</summary>
    public string StateText { get; set; } = string.Empty;

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; }

    /// <summary>本次开机执行状态文字。</summary>
    public string BootStateText { get; set; } = string.Empty;

    /// <summary>最后一次拉起结果。</summary>
    public string ResultText { get; set; } = string.Empty;
}

/// <summary>连接管理镜像项。</summary>
public class DebugConnection
{
    /// <summary>连接编号。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>连接名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>连接分组。</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>连接协议：SSH / FTP。</summary>
    public string Protocol { get; set; } = "SSH";

    /// <summary>主机地址。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>端口号；0 表示使用协议默认端口。</summary>
    public int Port { get; set; }

    /// <summary>登录用户名。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>认证方式：Password / PrivateKey。</summary>
    public string AuthType { get; set; } = "Password";

    /// <summary>私钥文件完整路径（认证方式为私钥时使用）。</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>SSH 保活间隔（秒）；0 表示关闭保活。</summary>
    public int KeepAliveSeconds { get; set; } = 15;

    /// <summary>列表展示摘要：协议 用户名@主机:端口。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>跳板链摘要：跳板1 → 跳板2 → 目标；直连为空字符串。</summary>
    public string JumpSummary { get; set; } = string.Empty;

    /// <summary>跳板链（仅包含非敏感字段与“是否已保存凭据”，供网页端编辑表单回填）。</summary>
    public List<DebugJumpHop> JumpHops { get; } = new List<DebugJumpHop>();

    /// <summary>是否为当前选中的连接。</summary>
    public bool Selected { get; set; }
}

/// <summary>跳板节点镜像：不含明文或密文凭据，只暴露是否已保存密码。</summary>
public class DebugJumpHop
{
    /// <summary>跳板主机地址。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>跳板端口；0 表示 22。</summary>
    public int Port { get; set; }

    /// <summary>跳板登录用户名。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>认证方式：Password / PrivateKey。</summary>
    public string AuthType { get; set; } = "Password";

    /// <summary>私钥路径。</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>是否已保存凭据（网页端据此提示“留空保持”）。</summary>
    public bool HasSecret { get; set; }

    /// <summary>跳序展示文字，例如“第 1 跳”。</summary>
    public string IndexText { get; set; } = string.Empty;
}

/// <summary>进程监控镜像行。</summary>
public class DebugProcessRow
{
    /// <summary>进程编号。</summary>
    public int ProcessId { get; set; }

    /// <summary>进程树缩进前缀（全角空格，层级越多越长，根层为空字符串）。</summary>
    public string IndentText { get; set; } = string.Empty;

    /// <summary>进程名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>本进程处理器占用的显示文字（如"3.2%"，首轮采样为"—"）。</summary>
    public string CpuText { get; set; } = "—";

    /// <summary>内存占用的显示文字。</summary>
    public string MemoryText { get; set; } = string.Empty;

    /// <summary>主窗口标题，无窗口的进程为空字符串。</summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>进程所属用户的显示文字（如"SYSTEM"，读取不到时为"—"）。</summary>
    public string UserNameText { get; set; } = "—";

    /// <summary>线程数的显示文字（读取不到时为"—"）。</summary>
    public string ThreadText { get; set; } = "—";

    /// <summary>句柄数的显示文字（读取不到时为"—"）。</summary>
    public string HandleText { get; set; } = "—";

    /// <summary>优先级的显示文字（如"正常"，读取不到时为"—"）。</summary>
    public string PriorityText { get; set; } = "—";

    /// <summary>读写流量（IO）的显示文字（读取不到时为"—"）。</summary>
    public string IoText { get; set; } = "—";

    /// <summary>进程启动时间的显示文字（读取不到时为"—"）。</summary>
    public string StartTimeText { get; set; } = "—";

    /// <summary>进程命令行的显示文字（读取不到时为"—"）。</summary>
    public string CommandLineText { get; set; } = "—";

    /// <summary>是否被选中（与桌面单击选中同步）。</summary>
    public bool IsSelected { get; set; }
}

/// <summary>比对镜像行：目录模式的状态行与文件模式的对齐行共用同一结构。</summary>
public class DebugCompareRow
{
    /// <summary>目录模式：相对路径；文件模式：左右行号摘要。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否为目录。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>状态文字（相同 / 不同 / 仅左侧 / 修改等）。</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>状态样式键（same / different / leftonly / rightonly / modified / leftnewer / rightnewer / type）。</summary>
    public string StatusKey { get; set; } = "same";

    /// <summary>状态强调色。</summary>
    public string StatusColor { get; set; } = "#4CAF50";

    /// <summary>目录模式：左侧信息文字。</summary>
    public string LeftInfo { get; set; } = string.Empty;

    /// <summary>目录模式：右侧信息文字。</summary>
    public string RightInfo { get; set; } = string.Empty;

    /// <summary>目录模式：左侧完整路径。</summary>
    public string LeftPath { get; set; } = string.Empty;

    /// <summary>目录模式：右侧完整路径。</summary>
    public string RightPath { get; set; } = string.Empty;

    /// <summary>目录模式：左侧是否存在。</summary>
    public bool LeftExists { get; set; }

    /// <summary>目录模式：右侧是否存在。</summary>
    public bool RightExists { get; set; }

    /// <summary>目录模式：类型图标分类键。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.File;

    /// <summary>目录模式：类型图标颜色。</summary>
    public string IconColor { get; set; } = FileIconCatalog.GetAccent(FileIconCatalog.File);

    /// <summary>文件模式：左侧行号显示文字。</summary>
    public string LeftNumber { get; set; } = string.Empty;

    /// <summary>文件模式：右侧行号显示文字。</summary>
    public string RightNumber { get; set; } = string.Empty;

    /// <summary>文件模式：左侧完整文本。</summary>
    public string LeftText { get; set; } = string.Empty;

    /// <summary>文件模式：右侧完整文本。</summary>
    public string RightText { get; set; } = string.Empty;

    /// <summary>文件模式：左侧差异前前缀。</summary>
    public string LeftPrefix { get; set; } = string.Empty;

    /// <summary>文件模式：左侧差异片段。</summary>
    public string LeftChanged { get; set; } = string.Empty;

    /// <summary>文件模式：左侧差异后后缀。</summary>
    public string LeftSuffix { get; set; } = string.Empty;

    /// <summary>文件模式：右侧差异前前缀。</summary>
    public string RightPrefix { get; set; } = string.Empty;

    /// <summary>文件模式：右侧差异片段。</summary>
    public string RightChanged { get; set; } = string.Empty;

    /// <summary>文件模式：右侧差异后后缀。</summary>
    public string RightSuffix { get; set; } = string.Empty;

    /// <summary>行类型样式键（same / modified / leftonly / rightonly）。</summary>
    public string KindKey { get; set; } = "same";

    /// <summary>是否为差异行（目录模式与文件模式共用）。</summary>
    public bool IsDifferent { get; set; }
}

/// <summary>文件列表镜像行。</summary>
public class DebugFileRow
{
    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否为目录（网页端据此决定点击是进入还是打开只读编辑器）。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>完整路径（网页端导航与打开文件时回传）。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>类型。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>大小。</summary>
    public string Size { get; set; } = string.Empty;

    /// <summary>修改日期。</summary>
    public string Modified { get; set; } = string.Empty;

    /// <summary>属性列文字（只读/隐藏/系统/存档，逗号分隔）。</summary>
    public string Attributes { get; set; } = string.Empty;

    /// <summary>预览分类：image/text/pdf/audio/video/other（桌面与网页共用判定）。</summary>
    public string PreviewKind { get; set; } = "other";

    /// <summary>是否被勾选（与桌面复选框同构）。</summary>
    public bool IsSelected { get; set; }

    /// <summary>类型图标分类键（见 <see cref="FileIconCatalog"/>），网页端按同一分类绘制矢量图标。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.File;

    /// <summary>类型图标颜色（十六进制）。</summary>
    public string IconColor { get; set; } = FileIconCatalog.GetAccent(FileIconCatalog.File);

    /// <summary>磁盘视图：驱动器剩余可用空间（非驱动器行为空）。</summary>
    public string Free { get; set; } = string.Empty;

    /// <summary>磁盘视图：驱动器已用空间占比（非驱动器行为空）。</summary>
    public string Usage { get; set; } = string.Empty;

    /// <summary>磁盘视图：驱动器文件系统格式（非驱动器行为空）。</summary>
    public string FileSystem { get; set; } = string.Empty;

    /// <summary>磁盘视图：树形层级（0 为根层，网页端据此缩进；其他镜像行恒为 0）。</summary>
    public int Level { get; set; }

    /// <summary>磁盘视图：文件夹是否已展开（网页端据此渲染展开箭头；其他镜像行恒为否）。</summary>
    public bool Expanded { get; set; }
}
