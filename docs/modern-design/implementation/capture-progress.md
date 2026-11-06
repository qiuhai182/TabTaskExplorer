# 采集链路优化进度（P0 截图相关）

状态：待实测
卡点：1.0–1.10 + WGC 主路径已进 2026-11-06 15:07 发布包，后台采集验证通过；桌面交互项仍未实测。
下一步：等用户空闲，按文末 11 项清单集中做一次桌面验证。

> 更新规则：每完成一个子项就追加一节，只记录「做了什么 / 改了哪些文件 / 待构建项 / 下一步」。
> 构建由用户双击 `D:\TabTaskExplorer\build.bat` 执行；本会话可用 `dotnet build` 做编译校验，但不代替发布包。

## 2026-11-04 子项 0：核对上次改动

- 结论：`src` 下不存在 WGC / 滚动截图 / 动作链的半截代码；`CaptureCoordinator` 已完整接入 `ICaptureCancelInput`（
  延迟等待、截图后、保存后、写剪贴板前四段均有取消轮询），无需补全。
- 未触碰：`src/TabTaskExplorer.App/Views/FileListPanel.axaml.cs` 的右键菜单函数、`src/TabTaskExplorer.App/Services/FileDropPolicy.cs`。

## 2026-11-04 子项 1：剪贴板 PNG + DIB 双格式（已构建，待实测验证）

### 做了什么

- 新增内核契约 `IClipboardImageWriter`：一次写入 PNG 字节 + 随附文件路径，返回
  `ClipboardImageWriteResult`（成功标志 + 中文逐格式明细），由应用层负责日志。
- 新增 Windows 实现 `WindowsClipboardImageWriter`：
  - 先写注册格式 `PNG`（`RegisterClipboardFormat("PNG")`），再写 `CF_DIBV5`（124 字节 BITMAPV5HEADER，
    `BI_BITFIELDS`，R/G/B/A 掩码，`LCS_sRGB`），再写 `CF_DIB`（40 字节 BITMAPINFOHEADER，`BI_RGB`，32bpp），
    最后写 `CF_HDROP`（20 字节 DROPFILES + UTF-16 路径列表，双空字符结尾）。
  - PNG 先解码为 32bppArgb 并**合成到白色背景**（透明区域填白），再转换为自下而上的 BGRA 行序，
    避免只认 `BI_RGB` 的程序把 alpha 当成黑块。
  - `OpenClipboard` 失败时重试 6 次、间隔 60 毫秒；`EmptyClipboard` 后按格式逐个写入，
    任一格式失败只记明细不中断；三种图片格式全部失败才判定失败。
  - 全局内存用 `GlobalAlloc(GMEM_MOVEABLE|GMEM_ZEROINIT)` + `GlobalLock` 写入，
    `SetClipboardData` 成功后所有权交给系统，失败时 `GlobalFree` 自行释放。
- 新增空实现 `NullClipboardImageWriter`（非 Windows 平台），由应用层回退为仅写文件路径。
- `CaptureCoordinator` 新增 `WriteScreenshotToClipboardAsync`：优先图片双格式，失败/不支持时回退
  `ISystemClipboardService.WriteAsync`（仅 `CF_HDROP`），所有分支均写中文日志（含图片尺寸、字节数、逐格式结果）。
  普通截图与「贴图截图」两条落剪贴板的路径都已改走该方法。
- 组合根注册：Windows → `WindowsClipboardImageWriter`；其他平台 → `NullClipboardImageWriter`。
- 界面文案：截图面板与设置中心的「截图后复制到剪贴板」改为「（图片+文件路径）」，并补充说明性 ToolTip。

### 改动文件

- 新增 `src/TabTaskExplorer.Kernel/Abstractions/IClipboardImageWriter.cs`
- 新增 `src/TabTaskExplorer.Platform.Windows/WindowsClipboardImageWriter.cs`
- 修改 `src/TabTaskExplorer.App/CompositionRoot.cs`
- 修改 `src/TabTaskExplorer.App/Services/Capture/CaptureCoordinator.cs`
- 修改 `src/TabTaskExplorer.App/Views/CapturePanel.axaml`
- 修改 `src/TabTaskExplorer.App/Views/SettingsWindow.axaml`

### 待构建 / 待实测

- 构建（2026-11-04 08:50）：`dotnet publish`（Release / win-x64 / 单文件）**exit=0，0 error**，
  日志 `output/build-capture-clipboard.log`。首次两次构建失败均来自其他并行任务正在改的
  `LogViewerWindow.axaml(.cs)` / `SettingsWindow.axaml.cs` / `QuickLookWindow.cs`（并发半成品状态），
  对方修好后重试即通过；本子项代码本身无编译错误。
- 平台工程单独构建（08:48）：`TabTaskExplorer.Platform.Windows` exit=0，仅 `System.Drawing` 的 CA1416 警告。
- 运行时实测：未能完成。应用启动后（进程 54836，日志 `ui-2026-11-04.log` 正常记录热键注册与主窗口创建），
  尚未触发任何截图便自行退出，日志无截图记录，因此 `WindowsClipboardImageWriter` 未被实际调用。
- 实测方法备忘：Computer Use 的合成按键不会触发本程序的全局热键（按下 PrintScreen 反而拉起了
  Windows 自带 `ScreenClippingHost.exe` 截图浮层），后续实测应在应用内「截图与录屏」页点击「全屏截图」按钮。
- 待实测：微信 / QQ / 飞书粘贴应得到图片；资源管理器粘贴应得到文件；带透明区域的图片不应出现黑块。
- 已知边界：非 Windows 平台仍是「仅文件路径」，没有图片剪贴板能力（接口存在，实现为空）。

### 下一步

子项 2：Windows 截图主路径改 Windows.Graphics.Capture（WGC），保留 GDI `CopyFromScreen` 作为回退，
并提供可强制的后端切换以便对比。

## 2026-11-04 验证纪律（后续全部按此执行）

1. 能走浏览器的一律走浏览器（`http://127.0.0.1:15919/`），桌面只做必要对照。
2. 不抢焦点、不在用户主窗口点按钮或键入、不弹全屏遮罩、不切换用户正在看的标签；测完立即退出进程。
3. 桌面专属项（剪贴板粘贴 / WGC 采集 / 滚动拼接）集中成一次验证，开始前说明占用时长，做完停手。
4. 每条验证记录必须写明「用了浏览器还是桌面、是否影响用户」。

### 本轮 Browser 侧验证记录（2026-11-04 08:56–08:58）
- 方式：**浏览器**（复用用户已打开的 Chrome 页面 `http://127.0.0.1:15919/`，用 `evaluate_script` 发同源 `fetch`，
  未导航、未新开标签）。是否影响用户：**否**（仅在页面内执行只读请求）。
- `/api/version`：200，`version=p0-1-web-modules-68ed9a74ef`，
  `features.session/csrf/events/staticAssets` 均为 true。
- `/api/diagnostics`：200，返回空数组（无异常项）。
- `/api/state`：200，返回 `tabCount`、`tabs=[{title:"此电脑"}]`、`theme`、`isIdeMode` 等键。
- `/api/ctxmenu`（POST，无 CSRF 令牌）：403 且日志记录「缺少或错误的 CSRF 令牌」——预期行为，说明写接口有 CSRF 保护。
- **截图历史 / 产物落盘路径 / 截图设置开关：无法经 Web 验证**。Web 工作区的 `ExecuteAction` 只覆盖文件、IDE、磁盘动作
  （`WebWorkspace.cs:606` 起），没有截图相关动作，`/api/state` 也不包含截图产物与截图设置字段。
  这三项只能在桌面窗口验证，已并入下一次集中桌面验证。
- 桌面侧：本轮仅为验证 Web 服务启动过应用（`launch_app` 直接拉起 `output\TabTaskExplorer.exe`，未点击、未键入）。
  应用随后按设置「关闭最后一个窗口→最小化到托盘」退出窗口，未触发任何截图；已用 `taskkill` 结束残留进程，
  验证结束无进程残留。

## 2026-11-04 子项 1.5：Web 截图接口（已构建，待浏览器实测）

### `GET /api/capture/state` 返回字段

| 字段 | 说明 |
| --- | --- |
| `outputDirectory` | 当前产物保存目录（`CapturePaths.ResolveOutputDirectory`） |
| `fileNameTemplate` | 命名模板 `截图_yyyyMMdd_HHmmss.png` |
| `format` | 当前固定 `png` |
| `pinSave` | 「贴图截图保存产物」开关 |
| `copyToClipboard` / `openFolderAfter` / `openEditorAfter` | 截图后动作开关 |
| `count` | 历史条目数量 |
| `items[]` | `fileName` / `fullPath` / `sizeBytes` / `sizeText` / `modifiedTime` / `thumbnailUrl` |

- `thumbnailUrl` 形如 `/api/thumb?path=<URL 编码后的完整路径>`，复用现有缩略图接口。

### `POST /api/capture/action`

| 参数 | 说明 |
| --- | --- |
| `a=fullscreen` | 全屏截图（后台线程提交，立即返回 `ok=true`） |
| `a=region` | 区域截图（**会在桌面弹出全屏遮罩**，仅本地使用） |
| `a=pin` | 贴图截图（同区域截图，会弹遮罩） |
| `a=pinSave&arg=on/off`（留空取反） | 切换「贴图截图保存产物」，写入设置 |
| `a=delete&arg=<完整路径>` | 删除历史产物，仅允许产物目录内的截图/录屏文件 |

- 权限：`capturefull/captureregion/capturepin/capturedelete` 走「新建与删除」权限；`capturepinsave` 走「修改」权限，
  沿用现有 CSRF 校验与审计日志。
- 改动文件：`Services/WebServiceHost.cs`（路由 + 两个处理器 + 权限映射）、`Services/WebWorkspace.cs`（接口两个方法）、
  `ViewModels/MainWindowViewModel.cs`（状态 JSON 组装、动作分派、删除校验、后台截图执行）。

### 构建与验证状态

- `TabTaskExplorer.Platform.Windows`：08:48 单独构建 exit=0。
- 整个 App：**本轮未通过构建**。原因 1：另一条并行任务正在修 `ConnectionPanel.axaml`（AVLN2000/AVLN3000）；
  原因 2：本机当前有 AweSun 远程会话反复抢占前台焦点，Computer Use 的点击/键入无法落到目标窗口，
  无法用 IDLE/PowerShell 代跑 `dotnet publish`（已停止桌面尝试，避免打扰用户）。
- 浏览器实测：**未执行**（需要有包含本接口的新构建产物才能启动服务）。
- 待补：构建通过后，在浏览器依次验证 `state` 初始值 → `action fullscreen` 后 `count+1` 且 `items[0].thumbnailUrl` 可加载 →
  `pinSave` 切换后 `state.pinSave` 变化且落盘到设置 → `delete` 后文件真实消失、`count-1`。

## 2026-11-04 子项 1.6：截图不再默认复制到剪贴板（已构建，待实测）

### 默认值与迁移

| 位置 | 改动 |
| --- | --- |
| `AppSettings.CaptureCopyToClipboard` | 默认值 `true` → `false` |
| `AppSettings.CaptureClipboardDefaultMigrated` | 新增一次性迁移标记 |
| `AppSettingsStore.EnsureCaptureClipboardDefault` | 首次加载时把旧值（含 `true`）统一改为 `false` 并写日志「配置迁移：截图剪贴板默认值迁移…」 |
| 新安装 / 重置 / 设置文件损坏回落 | 构造 `AppSettings` 即 `false`，并标记已迁移 |

### 之前会自动写剪贴板的调用点（已全部移除）

| # | 调用点 | 处理 |
| --- | --- | --- |
| 1 | `CaptureCoordinator.CaptureInternalAsync`（全屏 / 主屏 / 区域 / 活动窗口 / 上次区域截图） | 删除自动写入块，仅记「未自动写剪贴板」 |
| 2 | `CaptureCoordinator.CapturePinRegionAsync`（贴图截图保存产物分支） | 删除自动写入块，日志改为「自动复制到剪贴板=否」 |
| 3 | 网页端 `a=fullscreen/region/pin` | 走同一协调器，天然不再自动复制 |
| 4 | 滚动长截图 | 尚未实现；实现时必须同样不写剪贴板 |

### 显式复制入口（新增）

| 入口 | 说明 |
| --- | --- |
| 截图与录屏页工具栏「复制选中到剪贴板」 | 支持 Ctrl / Shift 多选，批量写入全部文件路径 + 第一张图片的 PNG/CF_DIBV5/CF_DIB |
| 截图与录屏页工具栏「复制最近一张」 | 复制历史列表第一条（完成提示的显式按钮） |
| 产物历史右键「复制到剪贴板（图片+文件）」 | 多选时复制全部选中，否则复制右键命中条目 |
| `POST /api/capture/action?a=copy` | `arg` 可空（复制最近一条）；支持换行 / 竖线 / 分号分隔的多路径 |
| 标注编辑器 / 贴图窗口的「复制」 | 原有显式动作，保持不变 |

- 统一实现：`CaptureCoordinator.CopyCaptureFilesToClipboardAsync`，沿用 `WindowsClipboardImageWriter`（PNG + DIBV5 + DIB + CF_HDROP）。
- `GET /api/capture/state` 的 `copyToClipboard` 直接读设置，迁移后恒为 `false`。
- 贴图流程不依赖剪贴板：`CapturePinRegionAsync` 使用截图字节生成位图；`PinClipboardImageAsync` 是用户主动点击的「贴图（剪贴板）」，保持不变。

### 构建与验证状态

- **未构建**：App 仍被并行任务的 `ConnectionPanel.axaml` 编译问题阻塞，本轮无法产出新二进制。
- 待构建后验证：启动应用不改设置直接截图 → 剪贴板应保持为空；点「复制最近一张」→ 剪贴板出现图片与文件；
  多选两条 → 资源管理器粘贴得到两个文件；网页端 `a=copy` 返回 `ok=true`。

## 2026-11-04 子项 1.7：截图不抢焦点 / 主窗口避让（已构建，待实测）

### 改动点

| # | 改动 | 位置 |
| --- | --- | --- |
| 1 | 新增设置「截图时隐藏主窗口」（默认开启）、「贴图避开原截图区域」（默认开启）、上次贴图坐标 | `AppSettingsStore` |
| 2 | 截图前快照主窗口可见/窗口状态 → `Hide()`；截取完成立即按原状态恢复，恢复时 `ShowActivated=false`，原为最小化则保持最小化 | `CaptureCoordinator.HideMainWindowForCapture / RestoreMainWindowAfterCapture`，覆盖全屏/主屏/活动窗口/上次区域/区域/贴图截图 |
| 3 | 区域遮罩 `ShowActivated="False"`，移除两处 `Activate()/Focus()`，不改变前台窗口 | `RegionSelectionWindow.axaml(.cs)` |
| 4 | 截图完成自动打开产物文件夹时不再 `Activate`（新增 `activateWindow` 参数，截图流程传 false） | `CaptureCoordinator.OpenOutputFolderTab` |
| 5 | 贴图位置：优先选区右侧 → 下方，放不下翻转，并记录上次坐标；关闭「避开原截图区域」时复用上次位置 | `CaptureCoordinator.ResolvePinPlacement` |
| 6 | 截图与录屏页新增两个设置勾选项 | `CapturePanel.axaml` |

### 日志字段

- 隐藏：`截图前隐藏主窗口：原可见=True 原窗口状态=<Normal/Maximized/Minimized> 结果=已隐藏`
- 跳过：`截图前不隐藏主窗口：设置项=关闭/主窗口不存在/当前不可见`
- 恢复：`截图后恢复主窗口：原可见=<bool> 原窗口状态=<state> 激活=False 结果=已恢复`
- 贴图：`贴图位置计算：原区域=... 图像宽度=... 结果横坐标=... 结果纵坐标=... 避开原区域=<bool>`

### 验收项（待用户构建后执行）

1. 让「数据维护工具」在前台，用全局热键触发全屏截图：全过程主窗口不得出现/闪烁。
2. 查看产物：目标窗口内容完整进图，没有被文件列表遮挡。
3. 截图结束后：主窗口回到触发前状态（可见性/最大化/最小化一致），前台仍是原程序。
4. 区域截图：遮罩出现后前台窗口标题不变（不进入 TabTaskExplorer）。
5. 贴图截图：贴图出现在选区右侧（放不下则下方/翻转），不覆盖原选区内容。
6. 关闭「截图时隐藏主窗口」后重复第 1 项：允许遮挡，用于对照。

### 仍可能把主窗口带到前台的路径（未在本轮改动）

- `CaptureCoordinator.OpenCaptureTab`：用户主动点击「截图与录屏」或热键时激活（用户意图）。
- `CaptureEditorWindow.Open`：需用户显式打开「自动打开标注编辑器」开关（现默认关闭）才会激活编辑器窗口。
- `ExecuteGlobalAction` 非截图动作分支、`App.axaml.cs` 单实例 FOCUS：均为用户/外部主动唤起。

## 2026-11-04 子项 1.8：失败提示三档 + 编辑器默认关闭（已构建，待实测）

| # | 改动 | 位置 |
| --- | --- | --- |
| 1 | `ShowFailure` 改为三档：托盘气泡（默认，无焦点浮层，不激活）/ 仅日志 / 弹窗（会抢焦点）；日志分别记 `截图失败提示方式=托盘气泡`、`=仅日志`、`=弹窗（会抢焦点）` | `CaptureCoordinator.ShowFailure` |
| 2 | 新增无焦点浮层 `FailureToastWindow`：右下角置顶小窗、`ShowActivated=false`、不占任务栏、4 秒自动关闭 | 新文件 `Services/Capture/FailureToastWindow.cs` |
| 3 | 新增设置 `CaptureFailureNotice`（`balloon` 默认 / `log` / `dialog`）与下拉索引 `CaptureFailureNoticeIndex` | `AppSettingsStore`、`SettingsWindow.axaml` |
| 4 | 「截图后自动打开标注编辑器」默认 `true → false`，界面文案「（默认关闭）」 | `AppSettingsStore`、`CapturePanel.axaml`、`SettingsWindow.axaml` |

- 取舍：Avalonia `TrayIcon` 没有跨平台气泡接口，因此「托盘气泡」用无焦点置顶浮层实现；若后续要系统级 NotifyIcon balloon，需在平台层新增接口。
- 失败提示不再默认抢焦点；「弹窗」档保留给需要强提示的用户，并在下拉项里标注会抢焦点。

### 失败气泡复核（2026-11-04 追加）

- 已确认走 `WindowHostStyler.MakeNoActivate`：`ShowInTaskbar=false` + `WS_EX_TOOLWINDOW`（移除 `WS_EX_APPWINDOW`）+ `WS_EX_NOACTIVATE`，
  点击气泡不夺焦点、不激活主窗口；多显示器按鼠标所在屏的工作区定位；4 秒后 `Close()` 并释放实例，日志记「失败气泡已关闭」。
- 平台接口 `IWindowHostService.ApplyNoActivateWindow` 已补齐：Windows 实现 Win32 样式，其余平台为空实现。

## 2026-11-04 子项 1.9：按目标判定隐藏 + 完成后激活截图页（已构建，待实测）

### 目标判定逻辑（`PrepareMainWindowForCapture`）

| 目标类型 | 判定 | 处理 |
| --- | --- | --- |
| 区域 | 选区与主窗口矩形（物理像素，`Position + Bounds×RenderScaling`）是否有交集 | 有交集→临时隐藏；无交集→保持 |
| 窗口 | 交集 + 前台窗口是否为本程序主窗口 | 本程序窗口→保持（可正常截自己）；否则有交集才隐藏 |
| 全屏 | 一律不隐藏 | 按用户所见画面截取 |

- 日志字段：`目标类型=窗口/区域/全屏 与主窗口相交=是/否 本程序窗口目标=是/否 处理=保持/临时隐藏 设置项=开启/关闭 恢复=待恢复/无需恢复`；
  恢复日志追加 `恢复=已恢复` 与原可见性/窗口状态。

### 截图完成后激活（`FinishCapturePresentation`）

- 设置 `CaptureActivateAfterCapture` 默认 `true`；开启时截图完成后激活主窗口并 `OpenCaptureTab()` 切到「截图与录屏」页（最新产物在列表首位）。
- 关闭时日志记 `完成后不激活主窗口：因截图流程而抑制激活 设置项=关闭`，保持当前前台窗口。
- 标注编辑器在同一流程中排在主窗口激活之后，避免两个窗口争前台。

### 截图期间局部抑制激活的点位（无全局抑制）

| 调用点 | 抑制方式 |
| --- | --- |
| 产物目录标签 | `OpenOutputFolderTab(..., activateWindow: false)` |
| 失败提示 | 托盘气泡档：无焦点浮层；`log` 档仅写日志；`dialog` 档才激活（用户可选） |
| 区域遮罩 | `ShowActivated=false`，已移除 `Activate/Focus` |
| 主窗口恢复 | 临时 `ShowActivated=false` 后 `Show()` |
| 贴图窗口 | `ShowActivated=false` + `WS_EX_NOACTIVATE`（既有实现） |

- **未新增任何全局抑制标志**；托盘点击、任务栏点击、热键打开面板、单实例 FOCUS、用户主动打开产物文件夹等路径保持原有激活行为。

## 2026-11-06 构建覆盖核对（以产物与代码现状为准）

- `output\TabTaskExplorer.exe` 最后写入时间 `2026-11-06 10:27:33`；同批源码时间戳 `10:27:10`（`build.bat` 第 0 步编码归一化先写文件、再发布），说明这次发布发生在本轮改动之后。
- 在该 exe 中按 UTF-16 检索到以下新增字符串，确认 1.5 / 1.6 / 1.8 / 1.9 均已编进产物：
  `失败气泡已关闭`、`主窗口避让判定`、`完成后不激活主窗口`、`截图剪贴板默认值迁移`、`网页端截图完成`、`截图完成后切到截图页`。
- 运行时日志 `%APPDATA%\TabTaskExplorer\logs\ui-2026-11-06.log` 显示 10:26:39 启动的是 `src\...\bin\Debug` 的调试版，且 6 秒后退出、没有任何截图动作；
  因此**发布包本身还没有做过运行时验证**，之前所有「待构建」项已全部被构建覆盖，剩下的都是「待实测」。
- 本轮新增的滚动长截图代码**尚未发布**：`dotnet build -c Debug` 已 0 错误，仍需再跑一次 `build.bat`。

## 2026-11-06 子项 1.10：滚动长截图（手动滚动 + 自动拼接，代码完成，待构建）

### 形态与取舍

- 独立入口：截图面板新增「滚动长截图」按钮 + 动作标识 `ScreenshotScroll`（快捷键目录可配，默认 `Ctrl+Alt+Shift+PrintScreen`，仅给既有配置补一次）。
- **手动滚动模式**：选区后由用户自己在目标程序里滚轮滚动，工具只按 400ms 间隔采集同一区域，不向目标窗口发送任何滚轮消息
  （遵守「不要驱动桌面窗口做自动化」的约束）。停止滚动约 3 帧（约 1.2 秒）后自动完成。
- 取消沿用同一个入口：`ICaptureCancelInput` 轮询右键 / Esc，取消后不保存产物、不写剪贴板。
- 结果按普通截图规则保存进产物历史（PNG），不自动写剪贴板；完成后按 1.9 的规则激活主窗口并切到截图页。

### 拼接算法（`ScrollStitcher`）

- 长图初始为首帧；之后每帧：
  1. 取长图底部一段 180 行作为「探针带」（避开最底部 8 行，防止固定底栏把探针带钉死）；
  2. 用逐行 64 列亮度签名在长图中自下而上搜索探针带在新帧中的位置 `matchedY`；
  3. 归一化平均像素差 ≤ `0.04` 视为匹配；新增起点 = `matchedY + (长图当前高度 - 探针带顶部)`，只追加该行以下的内容；
  4. 因为只追加探针带下方内容，**固定页眉不会重复出现**；探针带匹配到底部但无新增时，改用更高的备用探针带重试。
- 保护：最大高度 16000 像素、最大 90 帧（约 36 秒）；超过后按已有内容完成。
- 已知限制：选区内若有**固定底栏 / 悬浮工具条**，追加时会把该条带一起拼进去，可能出现重复条带；建议选区避开。横向滚动未支持。

### 涉及文件

| 文件 | 改动 |
| --- | --- |
| `Kernel/Capture/IScrollCaptureService.cs`（新） | 参数 / 进度 / 结果契约 + 空实现 |
| `Platform.Windows/ScrollStitcher.cs`（新） | 探针带匹配、追加、PNG 编码 |
| `Platform.Windows/WindowsScrollCaptureService.cs`（新） | 复用 `IScreenCaptureService` 逐帧采集 + 循环 / 取消 / 结束判定 |
| `App/CompositionRoot.cs` | Windows 注册真实实现，其余平台注册空实现 |
| `App/Services/Capture/CaptureCoordinator.cs` | 注入服务；新增 `CaptureScrollRegionAsync`、`IsScrollCaptureAvailable`、热键分支 |
| `App/ViewModels/CaptureTabViewModel.cs` | `CanCaptureScroll`、`CaptureScrollRegionAsync` |
| `App/Views/CapturePanel.axaml(.cs)` | 「滚动长截图」按钮与点击日志 |
| `App/Services/HotkeySetting.cs`、`AppSettingsStore.cs`、`ShortcutActionCatalog.cs`、`MainWindowViewModel.cs` | 默认快捷键 + 一次性补入 + 动作目录 + 快捷键分发 |

### 日志字段（便于实测核对）

- 开始：`开始滚动长截图：延迟秒数=… 模式=手动滚动（不发送滚轮消息） 取消方式=右键或 Esc 结束方式=停止滚动后自动拼接`
- 参数：`区域=… 采集间隔毫秒=400 最大帧数=90 最大高度=16000 最小重叠行数=40 匹配容差=0.04 结束判定=连续3帧无新增内容`
- 每帧：`滚动长截图进度：滚动长截图：第 N 帧 已拼接高度=… 本帧新增=… …`
- 保存：`滚动长截图已保存：路径=… 图像宽度=… 图像高度=… 帧数=… 字节数=… 结束原因=… 自动复制到剪贴板=否`
- 取消：`滚动长截图已取消：用户右键取消/用户按 Esc 取消 已采集帧数=… 结果=未保存产物`

## 2026-11-06 非交互验证记录

| 项目 | 方式 | 结果 |
| --- | --- | --- |
| 整包编译 | `dotnet build d:\TabTaskExplorer\TabTaskExplorer.sln -c Debug` | 0 错误（190 个既有 CA1416 警告） |
| 拼接算法离线测试 | 临时控制台工程反射调用 `ScrollStitcher`，合成 320×2400 长图、700 高帧、步进 200/100 像素，含一帧重复 | 长图 320×2400，逐像素与源图完全一致；重复帧判定「已匹配但没有新增内容」 |
| 测试中发现的缺陷 | 首版探针带避让 8 行导致追加起点少算 8 行，整图出现 8 行重复 | 已改为按「长图当前底部」换算追加起点，复测像素一致 |

- 测试工程只存在于系统临时目录，未写入仓库。

## 2026-11-06 发布与后台验证（滚动长截图已进发布包）

- `stop.bat`：返回「TabTaskExplorer 未在运行」（旧实例 `TabXExplorer64` pid 23248 不是本程序、也未占用 `output`，未做任何处理）。
- `build.bat`：退出码 0，`output\TabTaskExplorer.exe` 于 **14:53:40** 重新发布（361,789,552 字节）。
- 发布包字符串核对（UTF-16 检索）：`开始滚动长截图`、`滚动长截图已保存`、`追加新增内容`、`ScreenshotScroll`、
  `滚动长截图（选区后手动滚动并拼接）` 全部存在 → 本轮滚动长截图（含追加起点修正）已在包内。
- 启动冒烟（最小化启动，不抢前台）：进程正常创建、主窗口创建完成、Web 服务监听 15919、UI 看门狗启动；
  全局热键注册 **7/7 成功**，其中包含 `ScreenshotScroll`（Ctrl+Alt+Shift+PrintScreen）。
- `GET /api/capture/state` → 200：`copyToClipboard=false`、`pinSave=true`、`items=14`、
  `format=png`、模板 `截图_yyyyMMdd_HHmmss.png`、缩略图地址 `/api/thumb?path=…`。
- `GET /api/thumb?path=…` → 200，`Content-Type=image/png`，3193 字节。
- 启动至结束期间日志无异常/未处理异常；结束后 `stop.bat` 正常停止。
- 说明：首次从工具终端启动的实例在终端命令结束时被连带结束（无优雅退出日志），改用「后台保持宿主进程」的方式复测后正常。

## 2026-11-06 待用户空闲的桌面验证清单（一次跑完）

| # | 验证项 | 前置条件 | 预期观察点 |
| --- | --- | --- | --- |
| 1 | 截图不自动写剪贴板 | 默认设置（`CaptureCopyToClipboard=false`） | 日志「截图未自动写剪贴板」；粘贴到画图无内容 |
| 2 | 显式复制四格式 | 产物历史选中一张 →「复制选中到剪贴板」 | 日志写入成功；`Dump-Clipboard.ps1` 显示 `PNG`/`CF_DIBV5`/`CF_DIB`/`CF_HDROP`；资源管理器/微信/画图均可粘贴 |
| 3 | 失败气泡不抢焦点 | 设置「失败提示=托盘气泡」；制造一次失败截图（产物目录改为不可写路径） | 鼠标所在屏右下角出现 4 秒气泡；当前前台窗口不变；任务栏/Alt+Tab 无新条目；日志「失败气泡已关闭」 |
| 4 | 截图不抢焦点 + 完成后激活 | 默认设置；目标程序放前台 | 触发瞬间目标程序不被遮挡；完成后主窗口激活并停在「截图与录屏」页；日志含 `主窗口避让判定` 与 `完成后激活主窗口并切到截图与录屏页` |
| 5 | 本程序窗口可被截 | 截图目标选「活动窗口」且前台是本程序 | 不隐藏、正常截到自身；日志 `本程序窗口目标=是 处理=保持` |
| 6 | 滚动长截图（纵向） | 打开一个可滚动的长网页/文档；点「滚动长截图」→ 框选内容区 → 滚轮向下滚 3~5 屏 → 停手 | 约 1.2 秒后自动完成并保存；产物高度明显大于选区；固定页眉只出现一次、接缝无重复/错位 |
| 7 | 滚动长截图取消 | 同 6，采集过程中按 Esc 或右键 | 立即停止；不产生新产物；日志「滚动长截图已取消…结果=未保存产物」 |
| 8 | WGC 主路径生效 | 「截图采集后端=自动（推荐）」 | 每次截图日志含 `采集后端=WGC`；主屏截图与 GDI 对照画面一致、无黑屏/偏色 |
| 9 | 被遮挡窗口可截 | 用记事本/浏览器窗口，先用别的窗口遮住一半，再对它能成为「活动窗口」的状态截图（或用窗口选择入口，若已启用） | 遮住的部分在产物中仍是完整内容；日志 `采集后端=WGC` |
| 10 | 强制 GDI 对照 | 设置改「强制 GDI」后重复一次全屏截图 | 日志 `采集后端=GDI（已强制 GDI）`；画面与自动模式基本一致 |
| 11 | WGC 不可用回退 | 若系统/驱动导致 WGC 失败（或人为在特殊窗口如任务栏、隐藏窗口上触发） | 截图仍成功；日志 `采集后端：WGC 采集失败（…），本帧回退 GDI`，界面不报错 |

## 2026-11-06 子项 2：WGC 主路径 + GDI 回退 + 强制切换（已编译，待发布实测）

### 做了什么

| # | 内容 | 位置 |
| --- | --- | --- |
| 1 | 内核增加采集后端偏好 `CaptureBackend`（Auto / WindowsGraphicsCapture / Gdi），放入 `ScreenCaptureRequest` 与 `ScrollCaptureOptions` | `Kernel/Capture/IScreenCaptureService.cs`、`IScrollCaptureService.cs` |
| 2 | 能力位扩充：`WindowPicker`、`HdrAware`、`OccludedWindow`、`BackendName`；结果增加 `BackendName`（实际后端 + 回退原因） | 同上 |
| 3 | 新增 WGC 主路径实现：显示器项 / 窗口项 → 帧池 → 会话 → 纹理 → 暂存纹理读回 → 裁剪 → PNG | `Platform.Windows/WindowsGraphicsCaptureService.cs`（新） |
| 4 | 失败分级回退：强制 GDI 直接走 GDI；自动/强制 WGC 时先查 `GraphicsCaptureSession.IsSupported`，再逐帧尝试 WGC，任一环节失败回退 GDI 并写明原因 | 同上 |
| 5 | 组合根改为注册 WGC 主路径，并把回退原因接到界面日志；滚动长截图共享同一后端设置 | `App/CompositionRoot.cs`、`App/Services/Capture/CaptureCoordinator.cs` |
| 6 | 设置项「截图采集后端：自动 / 强制 WGC / 强制 GDI」，截图页与设置中心各一个下拉，改动即时落盘 | `AppSettingsStore.cs`、`CapturePanel.axaml(.cs)`、`SettingsWindow.axaml` |

### 互操作依据（不凭记忆写 IID / vtable）

- 接口 IID 与顺序来自本机 Windows SDK 10.0.26100.0：
  `Windows.Graphics.Capture.Interop.h`（`IGraphicsCaptureItemInterop` = `3628E81B-…`，`CreateForWindow` 槽 3、`CreateForMonitor` 槽 4），
  `Windows.Foundation.UniversalApiContract.winmd` 用 `winmdidl` 导出的 IDL（`IDirect3D11CaptureFramePool`、`IGraphicsCaptureSession`、
  `IDirect3D11CaptureFrame`、`IGraphicsCaptureSession2/3`、`IDirect3D11CaptureFramePoolStatics/2`、`IGraphicsCaptureSessionStatics`），
  `d3d11.h`（`ID3D11Device.CreateTexture2D` 槽 5、`ID3D11DeviceContext.Map/Unmap/CopyResource` 槽 14/15/47）与 `dxgi.h`（`IDXGIDevice`）。
- 所有 WinRT 接口继承 `IInspectable`，方法从 vtable 槽 6 开始；原生 COM 接口从槽 3 开始。
- 工程 TFM 仍为 `net10.0`（无 Windows 目标、无 WinRT 投影），未改 TFM，全部用手写 COM 互操作。

### 后端判定与日志

- `Auto`：`GraphicsCaptureSession.IsSupported` 为真才走 WGC；区域/全屏取「选区中心所在显示器」的显示器项，按物理像素偏移裁剪。
- 以下情况主动回退 GDI（日志写明原因）：非 Windows、系统低于 1903、WGC 不支持、选区跨显示器、全屏跨显示器、
  活动窗口最小化或没有活动窗口、取不到首帧（1.5 秒超时）、`CreateForWindow` 返回失败（任务栏/隐藏窗口等系统窗口）。
- 日志：`采集后端：WGC 采集失败（…），本帧回退 GDI`、`截图成功：… 采集后端=WGC/GDI（原因）`。
- 光标：WGC 用会话的 `IsCursorCaptureEnabled`，GDI 用 `DrawIconEx`，行为一致；同时关闭 WGC 黄色采集边框。

### 本次后台验证（未抢前台、未驱动桌面窗口）

| 项目 | 方式 | 结果 |
| --- | --- | --- |
| 整包编译 | `dotnet build TabTaskExplorer.sln -c Debug` | 0 错误 |
| WGC 区域采集 | 临时控制台（仓库外）调 `WindowsGraphicsCaptureService` 抓 320×200 | 成功，`采集后端=WGC`，24486 字节 |
| 与 GDI 像素一致性 | 同区域再走强制 GDI，逐像素抽样 16000 点 | 平均通道差 **0.00**，最大差 4（渲染时序），说明裁剪偏移、通道顺序、上下方向均正确 |
| WGC 全屏 | 主屏采集 | 成功，1920×1080，`采集后端=WGC` |
| WGC 窗口项 | 前台为普通应用窗口 | 成功，1920×1040，画面非黑；同一调用对任务栏/`Progman`/隐藏控制台窗口返回 `E_INVALIDARG`（系统窗口不可采集），已验证会回退 GDI 并成功出图 |
| 强制 GDI | `Backend=Gdi` | 成功，`采集后端=GDI（已强制 GDI）` |

- 说明：临时验证工程位于系统临时目录，未写入仓库；全程未激活任何窗口、未弹遮罩。

### 已知边界

1. 全屏/区域跨多个显示器时 WGC 单次只能采集一个显示器，自动回退 GDI（拼接多显示器采集留待后续）。
2. 窗口项暂未做独立「窗口选择器」入口，`WindowPicker` 能力位仍为 false；活动窗口截图已能利用 WGC 截取被遮挡内容。
3. HDR 未做显式色调映射：WGC 输出 `B8G8R8A8`，颜色跟随系统合成结果；`HdrAware=true` 仅表示链路走合成器而非 GDI 桌面 DC。
4. WGC 首帧超时 1.5 秒；极端卡顿场景会回退 GDI（GDI 结果为当前可见像素）。

### 涉及文件

| 文件 | 改动 |
| --- | --- |
| `Kernel/Capture/IScreenCaptureService.cs` | `CaptureBackend` 枚举、请求后端字段、能力位、结果后端名 |
| `Kernel/Capture/IScrollCaptureService.cs` | `ScrollCaptureOptions.Backend` |
| `Platform.Windows/WindowsGraphicsCaptureService.cs`（新） | WGC + D3D11 读回 + 裁剪编码 + 回退策略 |
| `Platform.Windows/WindowsScreenCaptureService.cs` | 成功结果标注 `BackendName="GDI"` |
| `Platform.Windows/WindowsScrollCaptureService.cs` | 透传后端设置 |
| `App/CompositionRoot.cs` | 注册 WGC 主路径并接日志 |
| `App/Services/Capture/CaptureCoordinator.cs` | 6 处请求写入后端设置；`ResolveCaptureBackend`；成功日志追加实际后端 |
| `App/Services/AppSettingsStore.cs` | `CaptureBackend` + `CaptureBackendIndex` |
| `App/Views/CapturePanel.axaml(.cs)`、`SettingsWindow.axaml` | 后端下拉与即时保存 |

### 发布与后台冒烟（2026-11-06 15:07）

- `stop.bat`：返回「TabTaskExplorer 未在运行」（无实例占用 `output`）。
- `build.bat`：退出码 0，`output\TabTaskExplorer.exe` 于 **15:07:33** 重新发布（361,810,032 字节）。
- 发布包字符串核对：`WindowsGraphicsCaptureService`、`RoGetActivationFactory` 命中（UTF-8）；
  `截图采集后端` 命中（XAML 资源）→ 本轮 WGC 主路径与后端下拉已在包内。
- 最小化启动冒烟（未点击、未键入）：进程 42820 正常启动，`GET /api/capture/state` → 200（5969 字节）；
  日志 `ui-2026-11-06.log` 15:09:44 起记录完整启动（.NET 10.0.12 / Windows 10.0.19045），
  主窗口创建、UI 看门狗、网页端服务正常，**无异常/未处理异常**；随后进程随启动它的终端会话结束（无残留）。
- 说明：CompositionRoot 的 WGC 服务工厂在启动阶段即被 `CaptureCoordinator` 解析，冒烟已覆盖注册与构造路径。

### 下一步

1. 等用户空闲，按文末 11 项清单一次跑完桌面验证（8–11 为 WGC 项）。
2. 后续可选：窗口选择器（`WindowPicker` 能力位 + 缩略图网格）、多显示器 WGC 拼接、HDR 色调映射说明。


