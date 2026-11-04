# Web 对齐 + 统一任务中心 进度

状态：待构建
卡点：本轮改动未编译（含复制/剪切/粘贴冲突策略、剪贴板探测移出 UI 线程、UiWatchdog、托盘退出看门狗）；桌面完整任务中心面板尚未实现。
下一步：stop.bat → build.bat 后，按 §7 八条验收清单做浏览器与桌面验证。

更新时间：2026-11-04

## 已完成（代码落地）

### 1. 公共预览分类器
- 新增 `Services/FilePreviewClassifier.cs`：`FilePreviewKind`、`Detect`、`IsImage`、`ReadTextPreview`、`DescribeUnsupported`。
- `Views/QuickLookWindow.cs` 改用公共方法，删除私有枚举/判定与文本读取；桌面行为不变。

### 2. Web 缩略图
- `WebServiceHost` 新增 `GET /api/thumb?path=…&size=96`。
- 走 `ThumbnailService` 96/192/384 磁盘缓存；成功 `image/png` + `Cache-Control: private, max-age=600`；失败/远程/不支持 404，页面保留类型图标。
- 前端 `views.js`：详细信息行、列表/内容、磁贴（192/384）挂 `.thumbBox[data-thumb-path]`；`IntersectionObserver` 懒加载；`fetch` 带 Bearer，按 `path|size` 缓存 blob URL；失败回退图标。

### 3. Web 列配置（共用 AppSettings）
- `AppSettings` 新增 `FileSortField` / `FileSortAscending`；`AppSettingsStore.Save` 加锁 + 临时文件替换。
- `FileTabViewModel` 新增 `ApplyColumnConfiguration` / `ApplySortConfiguration`；构造恢复共享排序；`SetSortField` 持久化。
- `IWebWorkspaceBridge` + `MainWindowViewModel` + `WebWorkspace` 新增 `GetFileColumnConfig` / `ApplyFileColumnConfig`，桌面应用到全部文件标签并保存一次。
- `WebServiceHost` 新增 `GET/POST /api/filecolumns`；POST 校验列名/列宽/排序，需修改权限。
- `DebugUiState` 增加列/宽度/紧凑/排序字段；`DebugFileRow` 增加 `Attributes`、`PreviewKind`；`TabSnapshotBuilder` 填充。
- 前端：详细信息表按共享列/宽度渲染（勾选/图标列固定，其余可拖宽并写回 7 段桌面列宽）；查看菜单列显示/隐藏、紧凑模式；排序沿用 `sort` 动作并持久化。

### 4. Web 快速预览
- `WebServiceHost` 新增 `GET /api/preview?path=…`：分类与文本读取复用 `FilePreviewClassifier`；返回 kind/name/size/modified/text/hint/local/supportsThumbnail。
- 前端：工具栏「预览」、右键「预览」、空格打开；右侧预览层支持图片（384 缩略图）、文本（512 KB 截断）、PDF 提示、音视频占位；Esc 关闭。

### 5. 统一任务中心
- 新增 `Services/Tasks/FileTaskModels.cs`、`FileTaskCenter.cs`、`LocalFileTaskExecutor.cs`、`RemoteTaskAdapter.cs`。
- 本地复制/移动/删除：串行队列、冲突策略跳过/覆盖/重命名/询问、取消、失败重试、复制临时文件与取消清理、删除优先回收站。
- 询问冲突不会自动跳过：任务保持 `waitingConflict`，Web 面板显示「等待处理（不会自动跳过）」并自动打开任务中心；桌面通过文件标签底部摘要与日志给出明确提示。
- 远程：`RemoteTransferPanelViewModel` 的 `Jobs` 映射为同一 `FileTaskSnapshot`；取消/继续转发到现有队列。
- 桥接：`IWebWorkspaceBridge` 与 `MainWindowViewModel`/`WebWorkspace` 新增任务读取/提交/取消/重试/冲突处理。
- API：`GET/POST /api/tasks`、`POST /api/tasks/{id}/cancel|retry|conflict`；提交/命令需新建与删除权限，源/目标走路径白名单。
- 前端：工具栏「任务中心」徽标、任务浮层、进度条、取消/重试、冲突四选一 + 本批统一；打开面板时 1 秒轮询，任务结束后拉一次全量状态。
- 接线：Web 复制/剪切后粘贴由 `WebWorkspace.PasteClipboard` 提交任务（询问策略）；Web 删除把勾选/单条提交任务；桌面跟随模式粘贴对本地路径提交任务（重命名策略），Web 触发的桌面删除提交任务；任务完成后刷新桌面文件标签。

### 6. 构建前收尾（不依赖运行产物）

- **冲突不再自动跳过**：`FileTaskCenter.RequestResolutionAsync` 仅通过取消令牌退出，无超时/自动跳过；`waitingConflict` 期间 Web 自动打开任务中心并 flash「等待处理（不会自动跳过）」，桌面弹出冲突对话框（四选一 + 本批统一），不选择则任务一直等待。
- **内联回退页健壮化**：`WebPageHtml.cs` 启动链补 `.catch`，静态资源缺失或初始化失败时只在页面底部显示一条黄色提示，不再产生未处理异常；回退页仍可用旧功能。
- **失败项重试入口**：`FileTaskSnapshot.CanRetry` 在整任务失败（无逐项失败记录）时也返回 true；`FileTaskCenter.Retry` 对无逐项记录的失败任务重试全部源，并拒绝重试非终态任务。Web 任务面板显示「重试失败项」。
- **桌面冲突对话框（本轮补齐桌面入口）**：`MessageDialog` 新增冲突模式 `AskTaskConflictAsync`（跳过 / 覆盖 / 重命名 / 取消任务 + 「本批统一按此处理」）；`MainWindowViewModel.ShowTaskConflictNotice` 弹出对话框并把选择写回 `FileTaskCenter.ResolveConflict`；关闭窗口按「取消任务」处理，不会静默跳过。
- **桌面重试按钮**：文件标签摘要栏新增「重试失败项」按钮（`FileListPanel.axaml` + `FileTabViewModel.CanRetryFailedTask/SetFailedTask/RetryFailedTask`）；任务失败时 `MainWindowViewModel.SetFailedTask` 点亮按钮，点击经 `RetryTaskRequested` 回调 `MainWindowViewModel.RetryFileTask` 重新提交。
- **待后续**：桌面完整任务中心面板（任务列表、进度、远程队列统一展示）尚未实现；当前桌面仅有「冲突对话框 + 重试按钮」两个最小入口。

### 7. 文件操作冲突增强（复制 / 剪切 / 粘贴）

- **工具栏**：复制 / 剪切 / 粘贴与 Ctrl+C/X/V、右键菜单共用 `CopyPathsToClipboard` / `CutPathsToClipboard` / `PasteFromClipboardAsync`。粘贴按钮绑定 `CanPasteFromClipboard`，仅当剪贴板含文件或文件夹时启用。
- **剪贴板判定**：`ISystemClipboardService.ProbeAsync` 轻量读取文件列表（CF_HDROP）与我方状态标记，不读内容；纯文本 / 图片返回 `HasFileData=false`。`MainWindowViewModel` 以 1.2 秒 `DispatcherTimer` 轮询，只在可用性变化时刷新并记日志：`粘贴可用性=可用/不可用 剪贴板格式=… 来源=…`（内部剪贴板优先）。
- **粘贴落点**：文件标签空白处、文件夹行右键、磁盘视图空白/行、导航栏与「此电脑」条目、书签栏条目（新增右键「粘贴到该文件夹」，非文件夹书签明确提示）、网页端当前目录。压缩包预览标签直接提示「不支持粘贴」；远程标签沿用提供者复制/移动规则。
- **冲突模型**：`FileConflictPolicy` 新增 `Merge` / `PerItem`；`FileConflictAction` 新增 `Merge` / `PerItem`；快照新增 `ConflictIsDirectory`。文件与文件夹的「本批统一」分别记忆（`applyToAllFile` / `applyToAllDirectory`），避免文件夹的合并选择影响后续同名文件询问。
- **同名文件**：替换 / 改名共存两项；替换先删除目标（Windows 走回收站）再写入源；默认聚焦「改名共存」。
- **同名文件夹**：合并 / 替换 / 逐项处理 / 改名共存四项；替换=清空目标后写入源内容（删除走回收站）；合并递归下钻，嵌套同名文件与文件夹继续按上述规则询问；逐项处理与合并共用递归询问路径，逐文件检查取消并更新进度消息（`正在处理：…（已复制 N 个文件）`）。
- **剪切语义**：复制成功后才删除源；任一失败不删源；合并过程中有同名项被跳过时保留源目录并记录日志。
- **日志**：`粘贴开始 源数量=… 目标=… 冲突策略=询问…`、每项冲突 `冲突判定：路径=… 类型=文件/文件夹 策略=询问/本批统一 结果=…`、`冲突处理：任务=… 选择=… 本批统一=… 结果=已写回`、剪切合并保留源日志。
- **两端一致性**：桌面复用 `MessageDialog.AskTaskConflictAsync(isDirectory)`；Web 任务中心按 `conflictIsDirectory` 渲染四选一/二选一，二者动作名相同（merge/overwrite/perItem/rename）。

#### 构建后验收清单（待执行）

1. 剪贴板无文件时粘贴按钮置灰：复制纯文本 / 截图后按钮不可用，复制文件后恢复可用；日志出现 `粘贴可用性=不可用/可用`。
2. 粘贴到任意目录：标签空白处、文件夹行右键、磁盘视图行、导航条目、书签文件夹、网页端当前目录均能落地。
3. 同名文件两种处理：替换（目标进回收站、源写入）、改名共存（生成 `name (1)`），桌面与 Web 行为一致。
4. 同名文件夹四种处理：合并（两边独有文件保留）、替换（清空目标后写入源）、逐项处理（逐个询问）、改名共存（新目录名）。
5. 合并递归冲突：嵌套同名文件弹二选一、嵌套同名文件夹弹四选一；勾选「本批统一」后同类冲突不再询问。
6. 剪切失败不删源：对只读 / 被占用目标粘贴剪切，任务失败但源文件仍在。
7. 批量统一：文件级与文件夹级分别记忆，互不串用。
8. 远程 / 压缩包标签：压缩包预览粘贴提示「不支持粘贴」；远程标签按提供者规则执行，不可用时给出提示。

### 8. UI 线程阻塞事故排查与修复（2026-11-04）

**证据（`%APPDATA%\TabTaskExplorer\logs\ui-2026-11-04.log`）**
- `15:40:55.614` 是事故前最后一条业务日志：`写入剪贴板… 结果=成功`；之后到 `15:42:59.247` 托盘退出前没有任何日志，界面线程约 2 分钟无心跳。
- `13:59` 段有密集 `读取剪贴板` 记录（同一秒多次），证明粘贴路径会频繁访问剪贴板；`15:40:55` 之后 1.2 秒一次的探测不再产生可见日志（探测成功本就不写日志），与"探测卡在 `OpenClipboard`"吻合。

**根因判断**
- `MainWindowViewModel` 的 1.2 秒 `DispatcherTimer` 调用 `ISystemClipboardService.ProbeAsync()`；
- 原实现直接 `Dispatcher.UIThread.InvokeAsync(ProbeOnUIThreadAsync)`，在 UI 线程执行 Avalonia `IClipboard.TryGetDataAsync()`；
- Windows 上其他进程持有剪贴板时 `OpenClipboard` 会重试等待，UI 线程被阻塞，托盘菜单回调也无法处理，直到剪贴板释放才恢复。与日志中的 2 分钟空窗一致。

**修复**
- `AvaloniaSystemClipboardService.ProbeAsync`：Windows 改为后台线程 `Task.Run(ProbeOnWindowsWorker)`，用 Win32 `OpenClipboard` 最多重试 200 毫秒、读取 `CF_HDROP` 与我方状态标记；失败即返回"剪贴板被其他进程占用"，不重试、不进 UI 线程。非 Windows 保留 UI 实现但加 250 毫秒超时。
- `ReadAsync` / `WriteAsync` / `WriteTextAsync` / `ClearAsync`：Windows 先做同一后台 200 毫秒探测，占用时直接放弃并记日志，不再进 UI 线程等待 `OpenClipboard`；实际 Avalonia 读写仍在 UI 线程（API 线程约束），但已消除最常见的长阻塞入口。
- `RefreshClipboardAvailabilityAsync`：加 `clipboardProbeRunning` 重入保护，上一次未完成跳过本次；仍然只在变化时写 `粘贴可用性` 日志。
- `UiWatchdog`（新增）：UI 线程 200 毫秒心跳 + 后台 500 毫秒检查，超过 2.5 秒未心跳写 `UI 阻塞疑似 阶段=… 耗时=…`；已在剪贴板探测、剪贴板读取、任务提交、冲突对话框、Shell 菜单枚举、Shell 原生菜单埋点。
- `MessageDialog.Show/ConfirmSaveAsync/ConfirmAsync/AskTaskConflictAsync`：加入 UI 线程检查，后台线程调用时先投递到 UI 线程再创建对话框。
- 托盘退出 `OnTrayExitClick`：启动独立线程看门狗，1.5 秒内未完成退出直接 `Environment.Exit(0)`，不依赖 UI 线程空闲（托盘菜单回调本身仍由 UI 线程派发，先移除阻塞源才是根本）。
- 未改截图、`DoDragDrop` 与拖拽语义、`FileDropPolicy`。

**残留风险**
- Avalonia 的剪贴板读写 API 只能在 UI 线程调用；后台探测只保证"探测绝不阻塞 UI"，粘贴时的实际读取仍有极小的 `OpenClipboard` 竞态窗口（探测通过后到 Avalonia 读取之间被其他进程抢占）。
- 托盘菜单回调若完全无法被派发（UI 线程尚未恢复），看门狗线程只能在回调进入后生效；本次通过移除周期性阻塞源解决。
- 以上修复**未构建、未实测**（当前会话 Shell 仍不可用）。

## 接口清单

| 接口 | 说明 |
| --- | --- |
| `GET /api/thumb?path=&size=96` | PNG 缩略图，`Cache-Control: private, max-age=600`；失败 404 |
| `GET /api/preview?path=` | 预览分类、文本、提示、本地标记 |
| `GET/POST /api/filecolumns` | 共享列配置读写 |
| `GET /api/tasks` | 统一任务快照（本地 + 远程） |
| `POST /api/tasks` | `{kind,sources,destination,policy}` 提交任务 |
| `POST /api/tasks/{id}/cancel` | 取消 |
| `POST /api/tasks/{id}/retry` | 重试失败项 |
| `POST /api/tasks/{id}/conflict` | `{action,applyToAll}` 处理冲突 |

## 构建与验证

- 构建：**待用户手动执行**（本会话 Shell / RunCommand 均报 `sandbox backend failed`，不再重试）。构建顺序：`stop.bat` → `build.bat`；若仍报 `ConnectionPanel.axaml` 的 AVLN2000/AVLN3000，需贴出完整错误与行号（该文件本轮未改动）。
- 浏览器验证：**未执行**（依赖可运行产物）。
- 桌面集中验证：**未执行**（缩略图滚动、空格预览、列菜单勾选、冲突对话框四选一 + 本批统一、失败任务「重试失败项」按钮）。
- 文件操作冲突增强验收：**未执行**，清单见 §7「构建后验收清单」。
- 构建后补跑日志测试：`src/TabTaskExplorer.Tests/UiLoggerRotationTests.cs`
  - 1MB 裁剪：`Log_ShouldTrimOldestAndKeepWholeLinesWhenExceedingLimit`（`UiLogger.MaxFileBytesOverride = 1024L * 1024`）；
  - 跨天切换：`Log_ShouldCreateNewFileNextDayAndKeepOldFile`（`UiLogger.NowProvider` 模拟 23:59 → 次日 00:01）；
  - 命令：`dotnet test --filter FullyQualifiedName~UiLoggerRotationTests`。

## 静态风险 / 未验证项

1. `WebPageHtml.cs` 内联回退页未同步新功能（静态资源正常时不受影响）。
2. `Microsoft.VisualBasic.FileIO` 回收站删除需编译确认（Windows 运行时回落永久删除）。
3. 桌面 UI 的删除入口仍走原有回收站逻辑（未改 `FileListPanel` 硬约束区域）；Web 触发的删除已进入任务中心。
4. 远程任务取消/重试取决于现有 `RemoteTransferPanelViewModel` 的 `CanCancel/CanResume`。
5. 列宽拖动只持久化详细信息视图；列表/内容/搜索仍用页面内列宽缓存。
6. Web 任务冲突为默认询问；不再自动跳过，任务会一直等待用户选择或取消。
7. 桌面没有完整任务中心面板：运行中的合并任务在桌面只能通过冲突对话框关闭取消，或到网页端任务中心点「取消」；「逐项处理」的进度消息需在网页端任务面板查看。
8. 剪贴板可用性为 1.2 秒轮询而非 `AddClipboardFormatListener`，实时性有约 1 秒延迟；已在变化时去重日志。
9. 文件夹「合并」与「逐项处理」共用递归询问实现，差异仅在语义入口；两者都会逐个询问嵌套冲突。
