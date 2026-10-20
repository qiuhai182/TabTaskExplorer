# WizTree 逆向分析与磁盘视图合并计划

## 一、摘要

1. 基于**已成功完成**的 REA（Ghidra 12.1.4）分析结果（`C:\Users\QiuHai\.cache\rea-wiztree\strings.json`，76KB，已验证：无 error 字段、MFT/USN/treemap 关键词 185 次命中），离线分析 WizTree64.exe 的 MFT 直读扫描、树形统计与可视化机制，输出分析报告到 `C:\Users\QiuHai\.cache\rea-wiztree\strings.WizTree64.md`（格式参照既有快照 `C:\Users\QiuHai\.cache\rea-nsarang\strings.Xshell.md`）。
2. 按项目「内核抽象层 + 平台适配层 + 应用层」架构，将磁盘空间分析功能（WizTree 式磁盘视图）合并到 TabTaskExplorer：新增内核模型与接口、Windows 平台实现（目录遍历统计版）、空实现默认实现、应用层磁盘视图标签页，并在组合根注册。

## 二、现状分析

- **REA 结果**：后台任务 `job-d22a3c1092df4dd9bc731b7cd9eab18b` 已成功（exit code 0），结果文件 `C:\Users\QiuHai\.cache\rea-wiztree\strings.json` 已通过 Grep 验证有效（无 `"error"` 字段；`MFT|MftRecord|USN|treemap|Treemap` 185 次命中）。文件 76KB 超出 Read 单次 64KB 限制，需用 Grep 分段提取或 PowerShell 只读分块读取。
- **项目现状**：项目目前无磁盘空间分析功能。内核仅有 `ISystemMetricsProvider`（单系统盘快照：DiskTotalBytes/DiskFreeBytes）。磁盘视图落点已全部预研确认：
  - 内核四目录规范：Abstractions/（接口）、Models/（模型）、Services/（默认实现，空实现参照 NullWindowCaptionHider）、Storage/（JSON 持久化）
  - Windows 平台实现目录：`src/TabTaskExplorer.Platform.Windows/`（已有 WindowsFileSystemProvider、WindowsProcessProvider 等 6 个实现）
  - 组合根 `src/TabTaskExplorer.App/CompositionRoot.cs` L57-L79 为唯一 OS 分支注册点
  - 应用层标签页模式：MainWindowViewModel `OpenTaskBoardTab()`/`OpenConnectionsTab()`（L175-L207「已存在则选中、不存在则新建」）、`CreateProcessPanel()` DI 面板工厂（L645-L651）
  - ViewModel 模式：ProcessPanelViewModel——构造注入内核接口、`Task.Run` 后台执行、`Dispatcher.UIThread.Post(new Action(...))` 更新、`ObservableCollection` 先 Clear 再 Add
  - XAML 模式：ProcessWindow.axaml 四行 Grid（`Auto,Auto,*,Auto`），但该面板用 ItemsControl 违背虚拟化约束——**磁盘视图大列表必须用 ListBox**
  - code-behind 模式：`if (this.DataContext is XxxViewModel vm) vm.StartRefresh();` + `this.Close()`

## 三、实施步骤

### 步骤 1：离线分析 REA 结果（任务 2）

- 用 Grep 按主题分组提取 strings.json 内容（MFT 扫描、USN、卷枚举、Treemap、进度/取消、命令行导出等），必要时 PowerShell 只读分块读取。
- 分析要点：WizTree 为何快（NTFS MFT 直读 + admin 权限要求）、树形统计结构、Treemap 着色/图例、扫描进度与取消、命令行导出（CSV）。
- 产出报告：`C:\Users\QiuHai\.cache\rea-wiztree\strings.WizTree64.md`（含 evidence_id、subject.digest/sha256、provider 表，格式同 strings.Xshell.md）。

### 步骤 2：内核层（TabTaskExplorer.Kernel）

- **新增** `Models/DiskEntry.cs`：磁盘条目模型（名称、完整路径、类型 Directory/File、大小字节数、父路径；UTF-8 BOM）。
- **新增** `Abstractions/IDiskUsageProvider.cs`：接口，方法签名对齐现有接口风格（同步返回或由调用方后台线程包装），例如：
  - `IReadOnlyList<DiskEntry> GetTopLevelEntries(string rootPath);`
  - `long ComputeSize(string path, CancellationToken cancellationToken);`（递归统计目录大小，支持取消）
- **新增** `Services/DefaultDiskUsageProvider.cs`：跨平台默认实现，基于 `System.IO.Directory`/`DirectoryInfo` 递归遍历统计（对照 NullWindowCaptionHider 空实现模式——非 Windows 平台功能齐备但走通用 API）。
- 所有新文件 UTF-8 BOM 编码（Write 新建中文文件后需补 BOM，脚本显式按 UTF-8 读入）。

### 步骤 3：平台适配层（TabTaskExplorer.Platform.Windows）

- **新增** `WindowsDiskUsageProvider.cs`：继承/实现 `IDiskUsageProvider`。按用户代码风格：简单直观、不用 lambda、尽量少改动。
- 参照 REA 分析结论说明设计取舍：**不采用** WizTree 的 MFT 直读方案（需管理员权限 + 驱动级解析，超出本项目范围），Windows 实现走 `DriveInfo` + 目录遍历，与内核默认实现保持一致的对外行为；报告中记录 WizTree MFT 机制作为后续可选优化方向。

### 步骤 4：应用层（TabTaskExplorer.App）

- **新增** `ViewModels/DiskViewPanelViewModel.cs`（复用 ProcessPanelViewModel 模式）：
  - 构造注入 `IDiskUsageProvider`；`StartRefresh()` 用 `Task.Run` 后台扫描；`Dispatcher.UIThread.Post(new Action(...))` 更新 `ObservableCollection<DiskRow> Rows`（先 Clear 再 Add）；SummaryText 显示总大小/耗时。
  - 日志/提示文案：纯中文、参数写全、不用缩写。
- **新增** `Views/DiskViewWindow.axaml`：四行 Grid 布局（刷新按钮 → SummaryText 绑定 → 表头 + ScrollViewer 包 **ListBox**（虚拟化必需，禁用 ItemsControl）→ 关闭按钮）。
- **新增** `Views/DiskViewWindow.axaml.cs`：code-behind 复用 `DataContext is DiskViewPanelViewModel` + StartRefresh/Close 模式。
- **修改** `ViewModels/MainWindowViewModel.cs`：新增 `OpenDiskViewTab()`，复用「已存在则选中、不存在则新建」标签模式（参照 OpenConnectionsTab L175-L207）；新增 `CreateDiskViewPanel()` DI 工厂（参照 CreateProcessPanel L645-L651）；在工具栏/菜单挂接入口按钮（位置与进程面板入口一致）。
- **修改** `CompositionRoot.cs`：Windows 分支注册 `IDiskUsageProvider → WindowsDiskUsageProvider`；else 分支注册 `IDiskUsageProvider → DefaultDiskUsageProvider`。

### 步骤 5：测试与构建验证（任务 4）

- **新增** `TabTaskExplorer.Tests` 单元测试：DefaultDiskUsageProvider 对临时目录的递归统计正确性（子目录、深层文件、取消令牌）。
- 构建前先关闭所有托盘常驻程序实例（普通权限运行 stop.bat），再跑 build.bat；构建失败若为 MSB4166 瞬时崩溃直接重试。
- 验证项：35+ 新增测试全过、构建成功、start.bat 启动后磁盘视图标签页可打开、扫描后台执行不卡 UI、列表滚动流畅。

## 四、假设与决策

- **决策 1**：磁盘视图实现采用「目录遍历统计」而非 WizTree 的 MFT 直读——MFT 直读需管理员权限与设备级 API（DeviceIoControl + MFT 记录解析），复杂度与权限要求高；REA 报告中完整记录其机制供后续演进。
- **决策 2**：磁盘视图以独立标签页呈现（与任务看板/连接管理同级），非弹出窗口独占。
- **决策 3**：扫描数据不做 JSON 持久化（首次实现不缓存，符合最小实现原则；Storage 目录暂不涉及）。
- **假设**：`IDiskUsageProvider` 接口为同步签名，由 ViewModel 用 Task.Run 包装为异步（与 IProcessProvider 现有模式一致）。

## 五、验证清单

1. REA 报告文件生成且含 subject 摘要、机制分析、证据引用。
2. 内核新文件 4 个（模型/接口/默认实现/如有辅助）均 UTF-8 BOM。
3. CompositionRoot 双分支均注册，应用层零平台引用、零 #if。
4. 磁盘视图列表为 ListBox，扫描在后台线程。
5. 全部单元测试通过；build.bat 构建成功；start.bat 启动验证功能正常。
