# Web 对齐 + 统一任务中心 进度

状态：待构建
卡点：`WebPageHtml.cs` 内联回退页未同步侧边栏右键与书签数据；桌面冲突对话框、桌面远程标签粘贴未实测；桌面完整任务中心面板未实现。
下一步：补齐回退页 → 重新发布 → 做桌面冲突对话框与远程标签粘贴验证（完整任务中心面板另行排期）。

更新时间：2026-11-06

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
- Windows 探测与读取已改为后台 OLE（各自 200/500 毫秒上限），不再进 UI 线程；写入仍走 Avalonia UI 线程（API 约束），极端占用时仍可能卡住复制/剪切动作，但不会再卡住轮询与粘贴读取。
- 托盘菜单回调若完全无法被派发（UI 线程尚未恢复），看门狗线程只能在回调进入后生效；本次通过移除周期性阻塞源解决。
- 上述探测/读取改动已在本轮构建后由 §9 的浏览器实测覆盖；托盘退出看门狗与桌面入口仍未桌面实测。

### 9. 复制 / 剪切 / 粘贴浏览器实测（2026-11-05）

构建：`stop.bat` → `build.bat` 退出码 0（多次修复后重新构建均通过）。

**结果：28 项中 21 项通过（含 4 项修复后复测通过），7 项未实测/受限。**

| # | 项目 | 结果 | 证据/说明 |
| --- | --- | --- | --- |
| 1 | 剪贴板为空 → 粘贴禁用 | 通过 | Web 独立工作区 `clipboardHasFiles=false`，按钮 `btn disabled`，提示「剪贴板为空或没有文件」 |
| 2 | 纯文本 → 不可用 | 通过 | 日志 `粘贴可用性=不可用 剪贴板格式=非文件（文本或图片）` |
| 3 | 图片 → 不可用 | 通过 | 同上（图片与文本共用非文件分类） |
| 4 | 文件 → 可用 | 通过 | 日志 `粘贴可用性=可用 剪贴板格式=文件列表（CF_HDROP），文件数=3 来源=系统剪贴板` |
| 5 | 多文件 + 文件夹混合 → 计数正确 | 通过 | 同上 `文件数=3`（两文件 + 一文件夹） |
| 6 | 标签空白处粘贴 | 通过 | Web 复制 3 项 → dst 粘贴任务 `completed 成功 3 项`，列表刷新 |
| 7 | 文件夹行粘贴 | 未实测 | 早期尝试被压缩包提供者残留状态干扰，修复后未重跑该落点 |
| 8 | 磁盘视图粘贴 | 未实测 | — |
| 9 | 导航/此电脑条目粘贴 | 未实测 | — |
| 10 | 书签栏右键粘贴 | 未实测 | — |
| 11 | 压缩包标签 → 提示不支持 | 通过（修复后） | 修复前静默失败；现返回「压缩包预览标签不支持粘贴，请在普通文件夹或远程目录中粘贴」 |
| 12 | 远程标签 | 未实测 | 本机无可用远程连接 |
| 13 | 同名文件替换 | 通过 | 覆盖后内容 `AAA`/`BBB`；替换前目标走回收站（未逐项验证回收站条目） |
| 14 | 同名文件改名共存 | 通过 | 目标出现 `file-a (1).txt`、`file-b (1).txt`，原名保留 |
| 15 | 同名文件取消 | 通过 | 任务 `canceled`，目标内容保持 `OLD-A`，源保留 |
| 16 | 同名文件夹合并 | 通过 | `keep.txt` 保留，`child.txt` 被替换，`nested/deep.txt` 正常复制 |
| 17 | 同名文件夹替换 | 通过 | 目标独有 `keep.txt` 消失，源内容完整写入 |
| 18 | 同名文件夹逐项处理 | 通过 | 依次询问 `child.txt`、`nested`，逐个处理后任务完成 |
| 19 | 同名文件夹改名共存 | 通过 | 生成 `folder1 (1)`，原 `folder1` 保留 |
| 20 | 本批统一 | 通过 | 文件级勾选后第二个文件不再询问；文件/文件夹记忆分开实现 |
| 21 | 冲突不自动跳过 / 取消后源保留 | 通过 | `waitingConflict` 持续等待；取消后目标不变、源保留 |
| 22 | 剪切成功删源 | 通过 | `move` 任务完成，源 `file-b.txt` 不存在，目标内容 `BBB` |
| 23 | 剪切失败/跳过不删源 | 通过（修复后） | 锁定目标覆盖失败，任务失败且源 `BBB2` 仍在；修复前回收站删除把任务线程挂起 3 分钟以上 |
| 24 | 跨设备剪切 | 未实测 | 无 U 盘/网络盘 |
| 25 | 粘贴中取消 | 通过 | 512MB 复制取消，任务 `canceled`，目标无残留、源保留 |
| 26 | 大文件/多文件 UI 不冻结 | 通过 | 512MB 复制期间 `/api/state` 延迟 4–20ms；512MB 单文件进度跳 100% |
| 27 | 连续快速粘贴两次 | 通过（修复后） | 修复前产生 2 个任务；现 `FileTaskCenter` 复用未结束的相同任务，只产生 1 个 |
| 28 | 只读/无权限目标 | 未实测 | — |

**本轮修复（均复测）**
1. `CF_HDROP` 常量误写为 14（实际 15）→ 文件剪贴板始终识别为非文件；已改为 15。
2. 跟随桌面模式下 `PasteFromClipboardAsync(...).GetAwaiter().GetResult()` 在 UI 线程同步等待异步粘贴，续体需要 UI 线程 → 自锁；改为投递异步任务并立即返回。
3. `ReadAsync` 在 UI 线程走 Avalonia 剪贴板读取，剪贴板被占用时会长时间阻塞 UI；Windows 改为后台 STA 线程 OLE 读取（最多 500ms）。
4. 回收站删除对锁定文件可无限挂起任务线程；改为独立 STA 线程 + 3 秒超时，失败回落永久删除并让任务失败。
5. 快速连续粘贴重复提交；`FileTaskCenter.Submit` 对未结束的同源同目标任务去重。
6. 压缩包粘贴无明确提示；Web 工作区新增「不支持粘贴」提示。
7. 失败任务摘要缺原因；补齐「最近失败：…」。

**关键日志（原样）**
- `粘贴可用性=可用 剪贴板格式=文件列表（CF_HDROP），文件数=3 来源=系统剪贴板`
- `粘贴可用性=不可用 剪贴板格式=非文件（文本或图片） 来源=系统剪贴板`
- `冲突处理：任务=ft-655ff775c754 选择=Rename 本批统一=False 结果=已写回`
- `剪切合并未删除源…` / `部分失败：成功 0 项，跳过 0 项，失败 1 项；最近失败：file-b.txt：The process cannot access the file ...`

### 10. “连接异常”截图排查（2026-11-05）

**结论：不是主机密钥或 SFTP 层，而是网络可达性 / 认证层；代码已补可读失败原因，未验证成功连接。**

- 截图操作按“连接管理 → 打开远程文件夹”复现；`/api/action?a=connopenfolder` 返回 **HTTP 200**，正文「正在连接 192.168.1.134，远程目录标签将在连接成功后出现」；真正的失败在后台异步回调里。
- 失败前 `pendingHostKey=null`，未进入 `HOST_KEY_UNKNOWN` / `HOST_KEY_MISMATCH`。
- 本轮 `Test-NetConnection 192.168.1.134:22`：`TcpTestSucceeded=False`、`PingSucceeded=False`（早前为 TCP 不可达）；随后复测时目标可达并进入认证层，134-nk8500 返回 `SshAuthenticationException: Permission denied (password).`
- 134-uospro 另有 `SshOperationTimeoutException: Session operation has timed out`（会话/认证阶段超时）。
- 截图中的空白「本地磁盘」标签未复现：本地磁盘 (D:) 正常加载 174 项；打开远程文件夹失败前会先 `ListDirectory("/")` 校验，失败不创建空标签。
- **修复**：`WebWorkspace.OpenRemoteFolder` 的通用异常改为中文可读原因（网络 TCP 超时 / 认证失败 / SFTP 子系统 / Socket），并在日志增加 `失败层次=网络层|认证层|SFTP 子系统`。
- **复测**：重启最新产物后再次执行打开远程文件夹，提示变为「打开远程文件夹失败：认证失败：用户名/密码或私钥被拒绝（Permission denied (password).）。请检查凭据后重试。」；`pendingHostKey` 仍为 null。
- **未实测**：正确凭据下的成功连接、主机密钥首次确认、SFTP 目录读取。

### 11. 134 认证对照定论（2026-11-06）

**前置：主机原本是关机状态。** `vmrun list` 为 `Total running VMs: 0`，`arp -a` 无 192.168.1.134 条目，ping 100% 丢包，系统 `ssh` 报 `connect to address 192.168.1.134 port 22: Connection timed out`。已用 `vmrun -T ws start "D:\VMSystems\134 - Uos Pro\134 - Uos Pro.vmx" nogui` 无界面启动，启动后 0 秒内 TCP 22 就绪。

**系统 OpenSSH 对照（`ssh -vv -o BatchMode=yes`，仅尝试 publickey）**

| 账号 | 服务器版本 | 协商 | 服务端可用认证 | 结果 |
| --- | --- | --- | --- | --- |
| nk8500 | OpenSSH_7.9p1 Uos-deepin1 UOS_Desktop_104* | kex curve25519-sha256 / hostkey ssh-ed25519 | `publickey,password` | `Permission denied (publickey,password).` |
| root | 同上 | 同上 | `publickey,password` | `Permission denied (publickey,password).` |

两账号服务端均接受 `password`，协商算法完全一致，排除“认证方法不被允许”。

**SSH.NET 独立控制台（不经过 App / Web / 本项目配置）**

| 账号 + 密码 | Password 模式 | HostKeyReceived | 说明 |
| --- | --- | --- | --- |
| root + root123 | **成功** `IsConnected=True` | 触发，ssh-ed25519，`cIhG+LXadOVuJKVOkwCSIb9g9utSV0DraVoQhaN8wlA` | SFTP 可用，根目录 30 项 |
| root + root123（Password+KeyboardInteractive） | **成功** | 同上 | — |
| nk8500 + 已保存密码（DPAPI 解出 11 位、全可打印） | 失败 `SshOperationTimeoutException: Session operation has timed out` | 触发，指纹同上 | 不是拒绝，是认证阶段挂起 |
| nk8500 + root123 | 失败，同样超时 | 触发 | 与密码内容无关 |
| nosuchuser + 任意密码 | 失败 `SshAuthenticationException: Permission denied (password).` | 触发 | 错误口令会立即被拒 |

**结论**

1. **不是 SSH.NET 库/算法兼容问题**：同一份 SSH.NET 用 `root/root123` 可以连通并列出目录，协商、主机密钥、SFTP 子系统全部正常。
2. **不是主机密钥问题**：指纹与 known_hosts 记录一致（`cIhG+LXadOVuJKVOkwCSIb9g9utSV0DraVoQhaN8wlA`），从未进入 mismatch。
3. **不是密码内容问题**：同一账号换任何密码都是“超时”；而不存在的账号会立刻 `Permission denied`，说明 `nk8500` 是服务端该账号的认证/PAM 挂起（账号被锁定、shell/PAM 配置或家目录异常），只能在服务端修。
4. **可用凭据是 `root/root123`**（主题账号），此前“被拒”是因为把它填进了 `nk8500` 用户名下。

### 12. App 全链路实测（root/root123，网页通道）

- 在网页「连接管理」新建连接 `134-root`（SSH / 192.168.1.134 / 22 / root / Password），保存成功。
- 点「打开远程文件夹」→ 标签出现并列出根目录 **28 个条目**（`boot`、`data`、`dev`、`etc`、`home`、`lost+found` …），证明 App 侧 DPAPI 解密 → `ConnectionInfo` → SFTP 全链路可用。
- **主机密钥未弹确认**：该主机的 `ssh-ed25519` 记录已在 known_hosts 中且指纹一致，走“已信任”分支；不是被跳过。
- **二次连接不提示**：再次点「打开远程文件夹」，日志为
  `关闭旧的远程文件夹标签：连接=134-root 结果=已移除，准备按当前凭据重建` → `打开远程文件夹标签：连接=134-root 标签总数=3`，标签数仍为 3，无重复、无空标签、无密码/指纹提示。
- **发现并修复的显示缺陷**：远程根路径 `/` 的标签标题曾显示为 `本地磁盘 ()`（沿用本地盘符逻辑）。已改为远程提供者根路径使用连接名（新增 `FileTabViewModel.RemoteRootTitle`，桌面与 Web 创建标签时传入 `profile.Name`），回退文案为「远程根目录」。新构建复测：标签显示 `134-root`，列表 28 项。

### 13. 本地 → 远程粘贴（矩阵 #12，已修并实测）

- 原实现：远程目录标签里粘贴本地文件时，直接把本地路径交给**远程提供者**的 `Copy`，必然失败；且只回一句「粘贴完成：成功 0 项，失败 1 项」，没有原因。
- 修复：`WebWorkspace.UploadLocalPathsToRemote` —— 目标为远程标签且源全是本地路径时，改走 `IRemoteTransferService.EnqueueUpload` 上传队列；连接缓存复用；剪切模式在上传成功后删除本地源、失败保留；失败把 `job.ErrorMessage + 远端路径` 写进提示与日志；远程来源 → 远程目标给出明确“请先下载再上传”的提示。
- 实测（`/tmp`）：复制 `D:\ttx-paste\src\a.txt` → 远程标签 `/tmp` → 粘贴，提示
  `已提交上传任务：本地 1 项 → /tmp（可在任务中心查看进度）`；刷新后 `/tmp` 出现 `a.txt 14 B`。

### 14. 本地粘贴矩阵：文件夹行（#7 通过）/ 磁盘视图（#8 修完待复测）

**#7 文件夹行右键「粘贴到该文件夹」：通过。**
- 操作：勾选 `D:\ttx-paste\src\a.txt` → 复制 → 在 `D:\ttx-paste` 列表中对 `dst` 文件夹行右键 →「粘贴到该文件夹」。
- 前端链路：`runOwnContextAction('pasteto')` → `doAction('pasteto', 'D:\\ttx-paste\\dst')` → `WebWorkspace.PasteClipboard(fileTab, path)`。
- 结果：提示「已提交复制任务…」，落地 `D:\ttx-paste\dst\a.txt`（14 B，内容 `A-CONTENT`）。

**#8 磁盘视图粘贴：发现真实缺陷，已改代码，待重建复测。**
- 现象：磁盘视图（`diskpasteinto`）里粘贴，返回「任务提交失败：请检查源路径与目标目录」，无任何文件落地，也没有可读原因。
- 根因：`diskpasteinto` 调用 `PasteClipboard(null, lines[0])`，而粘贴入口的分流条件是
  `bool localTask = fileTab != null && fileTab.FileSystemProvider is DefaultFileSystemProvider;`
  `fileTab == null` 时该值为 false，本地任务中心与远程上传两条路都不进，直接在末尾返回失败提示。
- 修复（`WebWorkspace.cs` 粘贴入口）：
  `bool localTask = fileTab == null || fileTab.FileSystemProvider is DefaultFileSystemProvider;`
  磁盘视图没有文件标签但目标仍是本地目录，必须走本地任务中心。
- 复测结果：见 §15（通过）。

### 15. 矩阵复测：磁盘视图（#8）、导航/此电脑（#9）、书签栏（#10）、跨设备剪切（#24）、只读目录（#28）——全部通过

**#8 磁盘视图粘贴（重建后复测）：通过。**
- 重建产物 `2026-11-06 09:11:35`，网页通道打开磁盘视图标签，等快照加载完（约 3 秒，423861 条）后 `diskNav("D:\")` 展开；
  日志：`展开目标不在当前列表中，已忽略：路径=D:\`（首次调用太早）→ 第二次 `磁盘视图切换展开：目标=D:\`。
- `doAction('diskpasteinto','D:\ttx-paste')` → 日志 `粘贴开始：源数量=1 目标=D:\ttx-paste` → `已提交复制任务…任务=ft-43422b40f869`；
  落地 `D:\ttx-paste\a.txt`（14 B），任务状态 completed。

**#9 导航/此电脑、#10 书签栏：此前完全没有右键入口，已补。**
- 原状：`views.js` 的 `navitem`（此电脑各行）与 `bmItem`（书签）只有 `onclick`，没有任何 `oncontextmenu`，矩阵两项无法执行。
- 新增前端：
  - `views.js` 两处模板加 `oncontextmenu="return sidebarMenu(event, path, name)"`；
  - `actions.js` 新增 `sidebarMenu(event, path, name)`：按当前标签/权限/剪贴板算出不可粘贴原因，渲染「打开」「粘贴到该文件夹」两项；
  - `actions.js` 的 `runOwnContextAction` 增加 `sidebaropen`（`openPath`）与 `sidebarpaste`（`doAction('pasteto', 目标路径)`）分支。
- 实测（旧构建，仅菜单可弹）：右键「本地磁盘 (D:)」弹出 `目标文件夹：本地磁盘 (D:)`，菜单 `[打开, 粘贴到该文件夹]`。
  点击粘贴无请求——**发现 JS 缺陷**：`runOwnContextAction(action, arg, state)` 的形参 `state` 遮蔽全局 `state`，
  在分支里读 `state.clipboardHasFiles` 恒为空，被误判为“剪贴板里没有可粘贴的文件…”而直接返回。
  修复：把前置条件在 `sidebarMenu` 构造菜单时算好，存入 `ctxMenuState.pasteReason`，分支只读该字段。
  直接调用 `doAction('pasteto','D:\')` 已证明后端可用（`D:\a.txt` 14 B，任务 `ft-6f31b6bd325e` completed）。
- **#9 复测（重建后）：通过。** 网页通道复制 `D:\ttx-paste\src\a.txt` → 右键侧边栏「本地磁盘 (D:)」→ 菜单 `[打开, 粘贴到该文件夹]`
  → 点「粘贴到该文件夹」，落地 `D:\a.txt`（14 B），任务中心 completed。
- **#10 书签栏：发现独立工作区根本没有书签数据。** `WebWorkspace.BuildState` 只填 `NavPc`（磁盘），从不填 `NavPinned`；
  且 `pin/unpin` 直接返回「独立工作区不与桌面共享书签」。因此网页端的书签栏永远是空白提示，右键入口无对象可测。
  已补：`BuildState` 按 `settings.PinnedPaths` 生成 `NavPinned`（名称取目录名，文件夹图标，当前路径命中则高亮）；
  `pin/unpin` 改为直接增删 `settings.PinnedPaths` 并保存，与桌面共用同一份书签。
- **#10 复测（重建后）：通过。** 书签栏出现既有书签 `output`（`D:\TabTaskExplorer\output`），左侧「快捷访问」也同步列出；
  网页通道复制 `D:\ttx-paste\src\a.txt` → 右键书签 `output` → 菜单 `[打开, 粘贴到该文件夹]`，标题 `目标文件夹：output`
  → 点粘贴，落地 `D:\TabTaskExplorer\output\a.txt`（任务 `ft-6c2080517590` completed）。测试文件已清理。
- 备注：当前 15919 端口方案 `AllowModify=false`，`pin/unpin` 会在动作鉴权层被拒（`该端口未放行修改权限`），
  属于权限方案既有约束，不是缺陷；书签增删需在放行修改的端口或桌面端操作。

**#24 跨设备剪切（C: → D:）：通过。**
- `C:\ttx-paste\cut.txt` 剪切 → 粘贴到 `D:\ttx-paste\dst`。
- 提示 `已提交移动任务：C:\ttx-paste\cut.txt → D:\ttx-paste\dst，冲突时在任务中心选择处理方式（不会自动跳过）`，任务 `ft-b516adcc77fb` completed；
  校验 `C:\ttx-paste\cut.txt` 已不存在、`D:\ttx-paste\dst\cut.txt` 存在。

**#28 只读目录（`D:\ttx-paste\ro`，已 deny WD/AD）：通过，但原文混英文。**
- 粘贴后任务 `ft-1cea4b222663` 状态 `failed`，`canRetry=true`，没有静默跳过；
  原文 `部分失败：成功 0 项，跳过 0 项，失败 1 项；最近失败：a.txt：Access to the path '…ttex-tmp-…' is denied.`
- 改进：`LocalFileTaskExecutor.DescribeError` 把 `UnauthorizedAccessException` / Win32 错误码 5 统一转成
  `拒绝访问：目标目录不可写（只读、被占用或权限不足）`，不再直接暴露英文系统文本。
- 新文案复测（重建后）：`部分失败：成功 0 项，跳过 0 项，失败 1 项；最近失败：a.txt：拒绝访问：目标目录不可写（只读、被占用或权限不足）`，
  任务 `ft-07f457ec80af` 状态 `failed`、`canRetry=true`，全中文、无静默跳过。

### 16. 桌面冲突对话框与网页策略对齐（2026-11-06）

**逐项对比（`MessageDialog.AskTaskConflictAsync` ↔ 网页任务中心 `renderTasks`）**

| 维度 | 网页 | 桌面 | 结论 |
|---|---|---|---|
| 同名文件夹选项 | 合并 / 替换 / 逐项处理 / 改名共存（顺序一致） | 同左（`okAction/saveAction/dontSaveAction/cancelAction`） | 一致，未改 |
| 同名文件选项 | 替换 / 改名共存 | 同左，另有「取消任务」按钮（等价网页任务行上的「取消」） | 集合一致，保留取消入口 |
| 本批统一 | 复选框，默认不勾选 | `ApplyAllCheck`，默认不勾选 | 一致 |
| 默认焦点 | 无模态焦点（行内按钮） | 文件夹=`CancelChoiceButton`、文件=`SaveButton`，都是「改名共存」 | 安全默认一致 |

**文案改动（两边改为同一句，回收站语义保留在里）**

- 同名文件夹
  - 旧（桌面）：`请选择：合并（保留两边内容）、替换（清空目标后写入源内容，删除走回收站）、逐项处理文件夹内同名项，或改名共存。关闭窗口=取消任务，不会自动跳过。`
  - 新（桌面/网页一致）：`目标已存在同名文件夹：X。替换=清空目标后写入源内容（删除走回收站）；逐项处理会逐个询问文件夹内同名项；合并会保留两边内容；改名共存让两边都保留。关闭窗口=取消任务，不会自动跳过。`
- 同名文件
  - 旧（桌面）：`请选择替换（删除目标文件后写入源文件，删除走回收站）或改名共存（两边都保留）。关闭窗口=取消任务，不会自动跳过。`
  - 新（桌面/网页一致）：`目标已存在同名文件：X。替换会删除目标文件（走回收站）后写入源文件；改名共存让两边都保留。关闭窗口=取消任务，不会自动跳过。`
- 网页端 `actions.js`：文件夹提示补上「合并会保留两边内容；改名共存让两边都保留。」，文件提示由「改名共存保留两边内容」改为「改名共存让两边都保留」。

**没变的默认值**：两个对话框仍默认聚焦「改名共存」，「本批统一」仍默认不勾选；`MessageDialog.AskTaskConflictAsync` 的选项与动作映射未改动，只改提示文案。本轮另把桌面复选框文案 `本批统一按此处理` 改为 `本批统一`，与网页逐字一致。

**验证方式**：桌面模态对话框需要抢前台点击，按本轮约束未做桌面交互实测；网页侧文案已实测（见 §17）。

### 17. 两项收边验证（2026-11-06）

**构建**：`2026-11-06 09:39:00` 产物（含 `FileTaskCenter` 冲突消息、`MessageDialog` 复选框文案、远程粘贴分流、`DescribeTransfer`）。

**网页任务中心冲突文案实测（DOM 原文）**
- 同名文件：hint = `替换会删除目标文件（走回收站）后写入源文件；改名共存让两边都保留。`
- 同名文件夹：hint = `替换=清空目标后写入源内容（删除走回收站）；逐项处理会逐个询问文件夹内同名项；合并会保留两边内容；改名共存让两边都保留。`
- 任务行 meta 同步为：`目标已存在同名文件：a.txt。请选择替换（删除走回收站）或改名共存，不会自动跳过` / `目标已存在同名文件夹：folder1。请选择合并 / 替换（删除走回收站）/ 逐项处理 / 改名共存，不会自动跳过`（与桌面提示同源同句）。
- 任务编号：`ft-10cc2bcbaf2f`（文件）、`ft-efe4516eda6f`（文件夹），均 `waitingConflict` → 手动 `cancel`，未自动跳过；夹具已恢复。

**远程标签下的任务/冲突路径**
- 分流：`MainWindowViewModel.PasteFromClipboardAsync` 在进入任务中心前判断「远程标签（`AttachedProfileId` 非空）且落点不是已存在的本地目录」→ `UploadLocalPathsToRemoteAsync` 后直接返回。条件由原来的「仅当前目录粘贴」放宽为「落点非本地目录」，覆盖远程标签下右键文件夹行「粘贴到该文件夹」；远程标签下粘贴到本地书签/导航目录仍按本地任务中心处理（`Directory.Exists(target)` 判定）。网页端 `WebWorkspace.PasteClipboard` 的 `localTask` 判定与上传分支保持一致。
- 冲突对话框不会误触发：桌面对话框唯一来源是 `FileTaskCenter.ConflictPending`；远程粘贴只调 `IRemoteTransferService.EnqueueUpload`，不提交 `FileTaskRequest`，因此不会产生 `waitingConflict`，远端同名直接覆盖（SFTP 写同名路径）。网页端同样在 `localTask=false` 时先返回上传队列。
- 失败原因中文分层：新增 `RemoteFailureText.DescribeTransfer(string?)`（路径不存在 / 权限不足 / 空间不足 / 超时 / 连接中断 / 兜底「传输失败：…」），三处共用：桌面 `MainWindowViewModel.OnUploadJobChanged`、网页 `WebWorkspace.OnUploadJobChanged`、远程任务快照 `RemoteTaskAdapter`（网页任务中心显示同一条中文原因）。提交阶段异常仍用 `RemoteFailureText.Describe` + `Layer`（网络 / 主机密钥 / 认证 / SFTP / 其他）。
- 日志：`远程粘贴提交上传：连接=… 目标=… 路径总数=… 方式=… 冲突策略=远端同名直接覆盖，不弹本地冲突对话框`；失败 `上传失败：任务=… 本地=… 远端=… 失败层次=传输层 原因=…`。
- 实测范围：网页端远程上传链路已在 §13 实测（`/tmp` 落地、提示「已提交上传任务」）；本轮桌面远程标签分支为代码级确认，**未实测**（按约束不驱动桌面窗口）；桌面提示语 `已提交上传任务：N 项 → 目标（可在传输列表与任务中心查看进度）`。

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

- 构建：已通过（最近产物 `2026-11-06 09:39:00`，`dotnet publish -c Release -r win-x64` 退出码 0，仅既有警告）。
- 浏览器验证：冲突文案与任务快照已实测（见 §17）；§9 的 21 项粘贴矩阵为前序实测结果。
- 桌面集中验证：**未实测**（缩略图滚动、空格预览、列菜单勾选、冲突对话框四选一 + 本批统一、失败任务「重试失败项」按钮；按约束不驱动桌面窗口）。
- 文件操作冲突增强验收：网页侧已覆盖，桌面交互项**未实测**，清单见 §7「构建后验收清单」。
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
