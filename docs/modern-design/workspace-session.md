# 工作区与会话恢复现代化设计

状态：设计稿（2026-11-03）
适用：`MainWindowViewModel.SaveOpenTabs`、`WindowRegistry`、`SingleInstanceService`、`WebWorkspace`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 保存打开标签 | `MainWindowViewModel.SaveOpenTabs`（:1887-1905） | 可用 | 跨全部窗口收集文件标签路径 |
| 恢复上次标签 | `MainWindowViewModel` 构造函数（:233-235）、`AppSettings.RestoreLastTabs` | 可用 | 仅恢复文件标签路径 |
| 多窗口注册表 | `Services/WindowRegistry.cs` | 可用 | 主窗口生命周期提升、窗口列表 |
| 单实例 | `Services/SingleInstanceService.cs:17-118` | 可用 | 命名互斥量 + 命名管道；转发 `OPEN`/`FOCUS` |
| 标签拖出/拖入 | `MainWindowViewModel.DetachTab`（:1189）、`AttachTab`（:1228） | 可用 | 拖出成新窗口；拖入已有窗口 |
| 系统唤起去重 | `MainWindowViewModel.OpenPathFromSystem`（:2822-2932） | 可用 | 跨窗口查找已有标签，命中则激活 |
| Web 工作区 | `WebWorkspace`、`MainWindowViewModel.CreateWorkspace`（:2525） | 可用 | 每个窗口一个浏览器镜像工作区 |
| 命名工作区 | 无 | 缺口 | 只有“上次会话” |
| 特殊标签恢复 | 无 | 缺口 | 搜索/比对/连接/IDE/媒体/终端不恢复 |
| 窗口布局恢复 | 无 | 缺口 | 不保存窗口位置/大小/显示器 |
| 崩溃恢复 | 无 | 缺口 | 异常退出后无会话快照 |
| 工作区模板 | 无 | 缺口 | 不能保存“项目 A：目录 + 终端 + 比对” |

主要问题：

1. **会话恢复只覆盖文件标签**：`SaveOpenTabs` 仅收集 `FileTabViewModel.CurrentPath`；搜索关键词、比对左右路径、连接 Id、IDE 工作区、媒体播放项、终端配置全部丢失。
2. **不保存窗口布局**：多窗口位置/大小/所在显示器不恢复，重启后窗口重叠。
3. **无命名工作区**：用户不能保存“工作组合”并按需切换；VS Code Workspace 式能力缺失。
4. **崩溃后无快照**：正常关闭才保存；崩溃/强杀后丢失会话。
5. **单实例命令协议简单**：只有 `OPEN`/`FOCUS`，无法“在已有窗口新建标签/打开终端/执行动作”。
6. **跨窗口去重逻辑分散**：`OpenPathFromSystem` 手写遍历所有窗口标签，缺少统一 `TabLocator`。
7. **Web 工作区与桌面窗口生命周期耦合**：窗口关闭后工作区清理依赖 `CleanupWebWorkspaces`，异常退出可能残留。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| VS Code Workspace | 文件夹/多根工作区、`.code-workspace`、窗口恢复、最近工作区 | 命名工作区 + 结构化描述 |
| Windows App SDK App Lifecycle | 单实例、激活、重启恢复（[App lifecycle](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing)） | 单实例激活协议 |
| Edge/Chrome 会话恢复 | 恢复标签、固定标签、最近关闭 | 特殊标签也应恢复 |
| Total Commander | 保存窗口布局/标签/历史 | 窗口几何与标签一起保存 |
| Directory Opus | Layouts/Profiles：保存窗口、标签、工具栏布局 | 命名布局 |
| tmux/screen | 会话持久化、断线恢复 | 终端会话独立持久化（P2） |

## 3. 推荐方案

### 必做（P0）

1. **结构化会话描述**
   - 新增 `SessionDescriptor`：窗口列表（几何、显示器、激活标签）、每窗口标签列表（Kind + 重建参数）。
   - 各标签实现 `ITabSessionState`：File(path)、Search(query/filters)、Compare(left/right)、Connections(profileId)、Startup(none)、Disk(root)、IDE(workspace/editors)、Media(path/playlist)、Terminal(profileId/local)。
   - 接入点：`TabContract.cs` 增加 `ITabSessionState`；`MainWindowViewModel.SaveOpenTabs/恢复`；`AppSettings.OpenTabs` 兼容旧 `OpenTabPaths`。
   - 验收：重启后搜索标签恢复到原关键词，比对标签恢复左右路径。

2. **窗口几何恢复**
   - 保存窗口 X/Y/Width/Height/WindowState/所在显示器 Id；恢复时校验显示器存在，否则居中。
   - 接入点：`WindowRegistry`、`MainWindow`、`AppSettings.WindowLayouts`。

3. **命名工作区**
   - “保存当前为工作区…”命名；工作区列表（侧边栏/开始页）；打开工作区时按描述重建窗口与标签。
   - 文件格式：`workspaces.json` 或 `.ttxworkspace`；支持导出/导入。
   - 接入点：新增 `Services/WorkspaceStore.cs`、`Views/WorkspaceManagerWindow.axaml`；`MainWindowViewModel.ApplyWorkspace`。

4. **崩溃恢复**
   - 每次标签/窗口变化后 2 秒防抖写 `session.autosave.json`；正常退出写正式会话并删除 autosave；启动时若发现 autosave 且上次异常，提示“恢复未保存会话”。
   - 接入点：`MainWindowViewModel` 变更事件、`App.axaml.cs` 启动流程、`UiLogger` 异常钩子。

5. **单实例命令协议扩展**
   - 管道消息从 `OPEN/FOCUS` 扩展为 JSON：`{action:"open", paths:[...]}`、`{action:"new-tab", kind:"terminal", profileId}`、`{action:"run", command}`（白名单）。
   - 接入点：`SingleInstanceService.BuildMessage`、`MainWindowViewModel.OpenPathFromSystem`；新增 `Services/InstanceCommand.cs`。

### 可选（P1/P2）

- 工作区模板/默认工作区（启动时打开）。
- 每窗口独立会话（当前是全局扁平保存）。
- 终端会话持久化（tmux 式，P2）。
- 工作区同步（用户目录/WebDAV）。
- 最近工作区列表与快速切换。

### 不建议

- 只保存路径字符串：特殊标签无法恢复。
- 崩溃后静默丢弃会话：至少保留 autosave 并提示。
- 在单实例协议中执行任意命令：只允许白名单动作。

## 4. 交互与视觉

- 启动：若检测到异常退出，顶部横幅“上次未正常关闭，是否恢复会话？”（恢复/丢弃）。
- 工作区管理器：列表 + 预览（窗口数/标签数/路径）+ 打开/重命名/删除/导出。
- 保存工作区：输入名称，可选“包含窗口布局/包含终端连接”。
- 恢复失败：逐项提示“搜索标签恢复失败：关键词为空”等，不整体失败。
- 多窗口：任务栏/托盘可列出窗口；工作区打开时按几何恢复。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 会话模型 | `Services/TabContract.cs`、新增 `Kernel/Models/SessionDescriptor.cs` | 各标签实现状态接口 |
| 保存/恢复 | `MainWindowViewModel.cs:1887`、构造函数 | 兼容旧 `OpenTabPaths` |
| 窗口几何 | `WindowRegistry.cs`、`AppSettings` | 显示器校验 |
| 工作区 | 新增 `Services/WorkspaceStore.cs`、`Views/WorkspaceManagerWindow.axaml` | JSON 文件 |
| 崩溃恢复 | `App.axaml.cs`、`MainWindowViewModel` 防抖保存 | autosave 生命周期 |
| 单实例协议 | `SingleInstanceService.cs`、`MainWindowViewModel` | JSON 消息 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 特殊标签恢复 | 连接/终端恢复涉及凭据与网络 | M/L |
| 窗口几何 | 多显示器拔插、DPI | S/M |
| 工作区 | 文件格式版本与兼容 | M |
| 崩溃恢复 | 防抖写盘频率、损坏文件 | S/M |
| 单实例协议 | 旧版本兼容 | S |

## 7. 验收标准

1. 重启后文件、搜索、比对、连接、IDE、媒体标签按原参数恢复。
2. 多窗口位置/大小/显示器恢复；显示器不存在时窗口可见。
3. 命名工作区可保存/打开/重命名/删除/导出，打开后窗口与标签与保存时一致。
4. 强杀进程后启动提示恢复 autosave；选择恢复后标签基本完整。
5. `tabtaskexplorer.exe D:\path` 在已有实例中打开并去重，不新建重复标签。
6. 单实例协议支持“新建终端标签”等白名单动作，非法动作被拒绝并记录。
7. 旧 `OpenTabPaths` 配置可平滑迁移。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [VS Code Workspaces](https://code.visualstudio.com/docs/editing/workspaces/workspaces) | 命名工作区/多根/窗口恢复 | 2026-11-03 |
| [Windows App SDK App Lifecycle](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing) | 单实例与激活 | 2026-11-03 |
| [Chrome 会话恢复](https://support.google.com/chrome/answer/95759) | 标签/会话恢复 | 2026-11-03 |
| [Directory Opus Layouts](https://docs.dopus.com/) | 布局/配置保存 | 2026-11-03 |
| [Windows 文件资源管理器](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows) | 多窗口/恢复习惯 | 2026-11-03 |
