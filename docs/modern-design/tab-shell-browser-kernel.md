# 标签容器与浏览器内核现代化设计

状态：设计稿（2026-11-03）
适用：`TabTaskExplorer.App` 标签条、`WebServiceHost` 浏览器镜像
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 多标签新建 / 关闭 / 切换 / 循环 | `MainWindowViewModel.AddTab`（`MainWindowViewModel.cs:346`）、`CloseTab`（:1125）、`CycleTab`（:1245） | 可用 | 统一 `ObservableCollection<object> Tabs`，File/Search/Compare/Connections/Startup/Process/Disk/Capture/Media/Terminal/IDE/Board 均可入列 |
| 标签拖动排序、拖出为新窗口 | `MainWindow.axaml.cs:899-1095`、`DetachTab` / `AttachTab`（`MainWindowViewModel.cs:1189/1228`） | 可用 | 仅水平标签条；拖出创建新窗口 |
| 标签条嵌入 36px 自绘标题栏 | `MainWindow.axaml:18-109` | 可用 | 标签最大宽度 246，新建按钮跟随末尾 |
| 标签右键菜单 | `MainWindow.axaml:52-63` | 可用 | 新建、复制、书签、关闭、关闭其他、固定、粘贴 |
| 会话恢复 | `SaveOpenTabs`（`MainWindowViewModel.cs:1681`） | 半成品 | 只保存文件路径，不恢复搜索/比对/连接/IDE 等特殊标签 |
| 浏览器镜像（Web UI） | `WebServiceHost.cs`、`WebWorkspace.cs`、`WebPageHtml.cs` | 半成品 | 桌面状态 1.5 秒轮询镜像；`WebPageHtml.cs:3370` `setInterval(refresh, 1500)` |
| 标签类型契约 | `TabContract.cs:24-99` | 半成品 | 仅 `file/disk/process/search/board/connections/startup/compare` 可渲染；`media/capture/terminal/editor/transfer` 归为 `unknown` |
| 内嵌浏览器标签 | 无 | 缺口 | `App.csproj` 无 WebView/CEF 依赖；当前“浏览器内核”仅指本机 Web 镜像，不是可导航网页的浏览器标签 |

主要问题：

1. **镜像传输模型落后**：每次刷新拉取整份 `DebugUiState`（含文件行、进程行、启动项），1.5 秒固定轮询；标签切换、文件操作反馈有明显延迟，大目录时 JSON 体积膨胀。
2. **前端不可维护**：`WebPageHtml.cs` 单文件 3119 行，HTML/CSS/JS 全内联，桌面与网页的视图逻辑靠字符串拼接维持“同构”。
3. **标签能力没有注册表**：`TabKinds.All` 是硬编码数组，新增标签必须同时改桌面、`WebWorkspace`、`WebPageHtml` 三处，容易漏。
4. **标签管理缺现代能力**：无分组、无垂直标签、无标签搜索、无“最近关闭恢复”、无休眠/挂起；固定标签只是书签式固定。
5. **“浏览器内核”定位不清**：用户若期望在内嵌标签打开网页，目前没有实现；若只做本机镜像，则不应继续称“浏览器内核”。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Microsoft Edge 垂直标签 + 标签组 + 休眠标签 | 垂直标签释放横向空间；标签组按颜色/名称聚合；休眠标签灰显并释放资源（[Edge 垂直标签](https://www.microsoft.com/en-us/edge/features/vertical-tabs)、[Sleeping Tabs](https://www.microsoft.com/en-us/edge/features/sleeping-tabs)） | 标签多时提供垂直模式；媒体/终端等重标签可“休眠”释放解码器与轮询 |
| Chrome 标签组 | 右键“添加到新组”，组可命名、着色、折叠；支持键盘操作（[Chrome 帮助](https://support.google.com/chrome/answer/10483214)） | 分组应落到 `MainWindowViewModel` 的标签模型，而不是只改 XAML |
| VS Code 快速打开 / 预览标签 | `Ctrl+P` 模糊搜索文件；单击预览、双击固定（[VS Code Tips](https://code.visualstudio.com/docs/editing/tips-and-tricks)） | 标签搜索与“预览标签”可复用到文件视图与 IDE |
| Zed 多缓冲区 | 搜索结果/诊断以可编辑 multibuffer 呈现（[Zed Multibuffers](https://zed.dev/docs/multibuffers)） | Web 镜像不应把每个视图都做成独立页面，可做“统一工作区事件流” |
| WebView2 | Windows 原生嵌入 Chromium，支持 JS↔Native 互操作（[WebView2 文档](https://learn.microsoft.com/en-us/microsoft-edge/WebView2/)） | 仅当确需内嵌网页时采用；跨平台需 WKWebView/WebKitGTK，成本 L，不建议本期做 |

## 3. 推荐方案

### 必做（P0）

1. **标签类型注册表**
   - 把 `TabKinds` 扩成“类型描述表”：`Kind / 桌面视图工厂 / 网页渲染能力 / 是否需要保活 / 是否可休眠 / 默认标题 / 图标`。
   - 接入点：`TabContract.cs` 新增 `TabKindDescriptor`，`MainWindowViewModel.AddTab` 与 `WebWorkspace.OpenView` 改为查表；`WebPageHtml.renderContent` 不再写 `if/else` 长链。
   - 每个标签实现 `ITabView.FillSnapshot` 时声明 `WebRenderMode`（full / summary / unsupported），网页端对 unsupported 显示统一占位卡而不是 `unknown`。

2. **镜像改为“首帧快照 + 增量事件推送”**
   - `WebServiceHost` 增加 `GET /api/events`（SSE，首选）或 WebSocket；事件包含 `stateVersion / tabPatch / rowsPatch / statusPatch`。
   - 保留 `GET /api/state` 作为首次全量快照与断线重连兜底；`WebPageHtml` 的 1.5 秒轮询降为 10 秒心跳校验。
   - 接入点：`WebServiceHost.cs:286-679` 增加路由；`MainWindowViewModel.BuildDebugState` 前后加版本号；`TabSnapshotFiller` 生成行级 diff；`WebWorkspace` 向 `WebPageHtml` 推送。
   - 验收：标签切换在网页端 < 200ms 可见；空闲 CPU 明显下降。

3. **前端拆分为可维护资源**
   - 新建 `src/TabTaskExplorer.App/Assets/web/`：`index.html`、`app.css`、`app.js`、`views/file.js`、`views/disk.js`、`views/process.js`、`views/ide.js`、`views/connections.js`、`views/startup.js`。
   - `WebServiceHost` 按路径提供静态资源（嵌入资源或 `AvaloniaResource`），`WebPageHtml` 只保留模板入口与启动参数注入。
   - 接入点：`WebServiceHost.cs` 的 HTML 响应分支；`WebPageHtml.cs` 删除内联 JS/CSS（分阶段迁移，先搬 CSS 再搬视图）。
   - 注意：不要引入 npm 构建链，保持“无构建、可直接改”的轻量约束。

4. **标签管理补齐**
   - 标签组：`TabGroup` 模型（Id/Name/Color/Collapsed），`MainWindowViewModel.Tabs` 改为分组容器或旁挂映射；`MainWindow.axaml` 标签条渲染组色条与折叠。
   - 最近关闭恢复：`ClosedTabHistory` 栈（最多 10 条），`Ctrl+Shift+T`；特殊标签保存重建参数（搜索关键词、比对左右路径、连接 Id）。
   - 标签搜索：`Ctrl+Shift+A` 弹出命令面板，复用 `IdeShellViewModel` 的模糊匹配工具。
   - 垂直标签：设置项 `TabStripOrientation`，`MainWindow.axaml` 用 `DockPanel` 切换；仅在标签数 > 8 时建议。
   - 休眠：对 `MediaTabViewModel` 释放 `IMediaPlaybackEngine`、对 `TerminalTabViewModel` 暂停输出刷新、对 `ProcessPanelViewModel` 停止定时器；切回时恢复。

### 可选（P1/P2）

- 标签预览缩略图（文件/媒体标签截屏缓存，200×120）。
- 工作区（Workspace）：把一组标签 + 窗口布局保存为命名工作区，参考 VS Code Workspace。
- 内嵌网页标签：仅 Windows 用 WebView2，macOS/Linux 用系统浏览器打开；定位为“网页预览”而非完整浏览器，避免跨平台不一致。

### 不建议

- 引入 CEF/Chromium 自建浏览器内核：体积、更新、安全成本高，且与“跨平台一致”约束冲突。
- 把 Web 镜像做成第二套完整产品：桌面端仍是主实现，网页端只做“同构只读 + 有限操作”。

## 4. 交互与视觉

- 标签高度 28，左右内边距 8/4；关闭按钮仅悬停/选中显示，避免拥挤。
- 固定标签用图钉图标 + 最左固定区；分组用 3px 左侧色条 + 组名 chip。
- 垂直标签栏宽 220，支持折叠为 44px 图标条；与左侧导航不共存（IDE 模式优先资源树）。
- 标签搜索面板：顶部 36px 输入框，结果按“最近使用”排序，回车切换。
- 休眠标签降低不透明度到 60%，图标加“zzz”角标；点击即唤醒。
- 网页端：标签条与桌面同构，但需显示“桌面专用”徽标（媒体/终端/截图）。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 标签类型注册表 | `Services/TabContract.cs`、`MainWindowViewModel.cs`、`Services/WebWorkspace.cs` | 单一事实来源，桌面与网页共用 |
| 事件推送 | `Services/WebServiceHost.cs`、`Services/WebWorkspace.cs`、`Assets/web/app.js` | SSE 优先；每 100ms 合并事件，避免高频抖动 |
| 前端拆分 | `Services/WebPageHtml.cs` → `Assets/web/*` | 先拆 CSS，再拆每个 `renderXxx` |
| 标签组/休眠/恢复 | `MainWindowViewModel.cs`、`MainWindow.axaml`、各标签 ViewModel | `ITabLifecycle.Suspend/Resume` 新接口 |
| 会话恢复 | `AppSettings.OpenTabPaths` → `OpenTabDescriptors` | 兼容旧字段，新增结构化描述 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| SSE/增量推送 | 事件顺序与断线重连；多端口多工作区隔离 | M |
| 前端拆分 | 迁移期双实现，容易漏视图 | M |
| 标签组/休眠 | 影响所有标签生命周期，需逐类回归 | M |
| 内嵌网页 | 跨平台能力不一致 | L（不建议本期） |

## 7. 验收标准

1. 网页端首屏加载后，标签切换、目录切换、状态栏变化在 200ms 内可见；无 1.5 秒固定全量轮询。
2. 10000 行目录下，网页端单次增量消息 < 200KB；连续操作 10 分钟无内存持续增长。
3. 新增一种标签类型只需改 `TabContract.cs` + 一个视图文件，不改 `WebPageHtml` 主流程。
4. 标签组可创建、改名、着色、折叠，重启后恢复。
5. `Ctrl+Shift+T` 能恢复最近关闭的文件/搜索/比对标签，且不产生重复标签。
6. 媒体/终端标签休眠后释放对应引擎或定时器，唤醒后状态正确。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Microsoft Edge 垂直标签](https://www.microsoft.com/en-us/edge/features/vertical-tabs) | 垂直标签、标签组入口 | 2026-11-03 |
| [Microsoft Edge Sleeping Tabs](https://www.microsoft.com/en-us/edge/features/sleeping-tabs) | 休眠标签的资源收益与设置 | 2026-11-03 |
| [Chrome 标签页组帮助](https://support.google.com/chrome/answer/10483214) | 标签组命名/着色/键盘操作 | 2026-11-03 |
| [VS Code Tips and Tricks](https://code.visualstudio.com/docs/editing/tips-and-tricks) | Quick Open、预览标签交互 | 2026-11-03 |
| [Zed Multibuffers](https://zed.dev/docs/multibuffers) | 多文件统一编辑面 | 2026-11-03 |
| [WebView2 介绍](https://learn.microsoft.com/en-us/microsoft-edge/WebView2/) | 内嵌浏览器的可行性与边界 | 2026-11-03 |
