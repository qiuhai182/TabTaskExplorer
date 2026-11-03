# 截图现代化设计

状态：设计稿（2026-11-03）
适用：`WindowsScreenCaptureService`、`CaptureCoordinator`、`RegionSelectionWindow`、`CapturePanel`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 全屏 / 主屏 / 区域 / 活动窗口截图 | `WindowsScreenCaptureService.CaptureCore`（:76-129） | 可用 | GDI `CopyFromScreen`，逐显示器裁剪拼接（:135-166） |
| 多显示器 / 混合 DPI 基础支持 | `CapturePixels`（:135）、`WindowsScreenGeometry` | 可用 | 物理坐标裁剪；负坐标与混合 DPI 有专门处理 |
| 区域框选 + 窗口/子视图预选 | `RegionSelectionWindow.axaml.cs:77-534`、`WindowsScreenRegionDetector` | 可用 | 鼠标悬停预选、Tab/滚轮切换层级、Enter/单击确认、Esc/右键取消 |
| 延迟截图（0/3/5/10 秒） | `CaptureCoordinator.WaitForCancelablePhaseAsync`（:1238） | 可用 | 延迟期间可取消 |
| 上次区域复用 | `CaptureLastRegionAsync`（:553）、`RememberLastRegion`（:1392） | 可用 | 保存物理坐标 |
| 截图后保存 PNG / 复制文件到剪贴板 / 打开产物目录 / 打开标注编辑器 | `CaptureInternalAsync`（:573-699） | 可用 | 流程串联完整 |
| 贴图截图 | `CapturePinRegionAsync`（:381-550） | 可用 | 选区后保存并贴图 |
| 屏幕取色 | `OpenScreenColorPickerAsync`（:1536） | 可用 | 冻结画面、复制十六进制 |
| 取消（Esc/右键） | `ICaptureCancelInput`、`PollCancelRequest` | 可用 | 覆盖延迟、遮罩、保存、贴图各阶段 |
| 截图产物历史 | `CaptureCoordinator.ListCaptureFiles`（:1752）、`CapturePanel.axaml:77-101` | 可用 | 文件列表，无元数据索引 |
| 滚动长截图 | 无 | 缺口 | 见 `scrolling-long-screenshot.md` |

主要问题：

1. **GDI 采集已到瓶颈**：`CopyFromScreen` 对硬件加速窗口、受保护内容（DRM）、HDR 画面、部分全屏游戏/浏览器合成层可能得到黑屏或偏色；遮挡窗口只能截取屏幕可见像素。
2. **“活动窗口”只是前台矩形**：无法选择任意窗口，也不能稳定截取被遮挡窗口；`GetActiveWindowArea` 只取前台窗口区域。
3. **剪贴板只写文件列表**：`ISystemClipboardService.WriteAsync(paths)` 写的是文件路径，聊天工具/网页输入框粘贴时得到“文件”而非图片。多数截图工具同时提供 `image/png` 与文件列表。
4. **产物无索引**：历史列表靠目录枚举（`ListCaptureFiles`），没有截图时间、区域、目标窗口、OCR 状态等元数据；无法做“按窗口/日期筛选”。
5. **输出格式单一**：固定 PNG，无 JPG/WebP、质量、尺寸上限设置。
6. **命名规则固定**：`CapturePaths.CreateScreenshotPath` 仅按时间戳；不支持 `{窗口标题}`、`{日期}`、`{序号}` 模板。
7. **缺少截图后的可配置动作链**：保存/复制/打开编辑器/贴图是硬编码顺序，用户不能自定义“截图后上传/复制路径/运行命令”。
8. **窗口选择器缺失**：区域框选有预选，但没有独立“窗口列表 + 缩略图 + 搜索”的截图入口。
9. **无 HDR/宽色域说明**：未检测 HDR 显示器，也未做 SDR 色调映射说明。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 11 截图工具 | 区域/窗口/全屏 + 录屏 + Text Actions（本地 OCR）+ 快速标注（[Microsoft 支持](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots)） | OCR 与录屏已具备，下一步是“同一入口、同一编辑器” |
| Snipaste | 自动检测界面元素、像素级控制、贴图、丰富标注（[官网](https://www.snipaste.com/)） | 窗口/子视图预选已对齐；差异在贴图管理和标注细节 |
| ShareX | 截图后工作流（上传/复制/打开编辑器/自定义命令）、滚动截图（[Scrolling screenshot](https://getsharex.com/docs/scrolling-screenshot)） | “动作链”是生产力核心，应可配置 |
| FastStone Capture | 窗口/对象/菜单/滚动窗口/网页、内置编辑器、录屏（[官网](https://faststone.org.cn/)） | 窗口对象与滚动截图是明确缺口 |
| Greenshot | 内置编辑器、外部命令插件、导出目标（[帮助](https://getgreenshot.org/help/zh-cn/)） | 外部命令可作为 P1 的“截图后动作” |
| Screenpresso | 内置矢量编辑器、滚动截图手动拼接（[手册](https://www.screenpresso.com/download/docs/ScreenpressoHelp.pdf)） | 长截图失败时的手动模式可借鉴 |

## 3. 推荐方案

### 必做（P0）

1. **采集后端升级为 Windows.Graphics.Capture（WGC）**
   - 新增 `WindowsGraphicsCaptureService` 实现 `IScreenCaptureService`；窗口截图用 `GraphicsCaptureItem`，屏幕/区域用 `GraphicsCapturePicker` 或显示项。
   - 保留 GDI 实现作为回退（WGC 不可用、旧系统、特殊窗口）。
   - 能力位：`Capabilities` 增加 `WindowPicker`、`HdrAware`、`OccludedWindow`；UI 根据能力位启用按钮。
   - 接入点：`Kernel/Capture/IScreenCaptureService.cs` 增加能力字段；`CompositionRoot.cs` 注册选择逻辑；`WindowsScreenCaptureService` 保留。
   - 注意：WGC 需要窗口句柄；截图自身遮罩要排除（`IsOwnProcessWindow` 已有）。

2. **剪贴板双格式**
   - `ISystemClipboardService` 增加 `WriteImageAsync(byte[] pngBytes, string? filePath)`；Windows 写 `CF_DIB`/PNG + `CF_HDROP`；其他平台写图片 + 文件 URI。
   - 设置项：`CaptureClipboardMode = ImageAndFile / FileOnly / ImageOnly`。
   - 接入点：`AvaloniaSystemClipboardService.cs`、`CaptureCoordinator` 保存后调用、`SettingsWindow` 增加下拉。

3. **截图元数据索引**
   - 产物目录写 `captures.index.jsonl`（追加式，启动时加载最近 1000 条）：路径、时间、目标、区域、显示器、窗口标题/进程、是否 OCR、标注次数。
   - `CaptureHistoryItem` 从索引读取，不再每次全目录枚举；删除时同步移除索引。
   - 接入点：`CaptureCoordinator.ListCaptureFiles`、`CaptureTabViewModel.RefreshHistory`（:395）、`CapturePaths`。

4. **窗口选择器**
   - 新入口“选择窗口截图”：列出可见顶层窗口缩略图 + 标题 + 进程名，支持搜索与刷新；点击即截取该窗口（WGC 可截取被遮挡窗口）。
   - 接入点：复用 `WindowsScreenRegionDetector.Detect` 的窗口枚举；新增 `WindowPickerWindow.axaml`；`CapturePanel` 增加按钮。

5. **动作链可配置**
   - 截图完成后按顺序执行：保存 → 复制（图片/文件/路径）→ 打开标注 → 贴图 → 打开产物目录 → 运行外部命令（可选）。
   - 设置页用可勾选列表 + 上移/下移；外部命令支持 `%f` 占位符。
   - 接入点：`CaptureCoordinator.CaptureInternalAsync` 抽出 `CapturePostActionPipeline`；`AppSettings` 新增 `CapturePostActions`。

6. **输出格式与命名模板**
   - 支持 PNG/JPG/WebP、JPG 质量、最大边长；文件名模板 `{date}/{time}/{counter}/{window}/{process}`。
   - 接入点：`CapturePaths`、`AppSettings`、`CapturePanel` 设置区。

### 可选（P1/P2）

- HDR 检测与 SDR 映射：读取显示器色彩空间，WGC 输出 SDR；在设置中说明“HDR 截图将按 SDR 保存”。
- 截图后 OCR 自动执行并写入索引（配合 `ocr.md`）。
- 定时/间隔截图（每隔 N 秒）。
- 截图水印/边框/阴影（标注编辑器已有部分能力）。
- 上传目标（FTP/SFTP/HTTP）：复用已有协议层，作为动作链的一步。

### 不建议

- 继续把 GDI 作为唯一后端：HDR/遮挡/合成层问题会持续。
- 在截图主流程里直接依赖外部程序（如 PowerShell）做图像处理：延迟与兼容性差。
- 自建云上传服务：先做通用 HTTP/FTP 动作，不做账号体系。

## 4. 交互与视觉

- 截图面板顶部：全屏 / 主屏 / 区域 / 窗口选择 / 活动窗口 / 上次区域 / 贴图截图 / 屏幕取色。
- 区域遮罩：候选窗口用强调色边框，子视图用虚线；顶部提示“Tab/滚轮切换层级，Enter/单击确认，Esc/右键取消”。
- 延迟截图：屏幕中央 3-2-1 倒计时，可 Esc 取消。
- 截图后：右下角 toast“已保存 + 已复制”，提供“打开标注 / 打开文件夹 / 贴图”三个快捷按钮。
- 窗口选择器：网格卡片 240×160，标题过长省略，右上角进程名；顶部搜索框。
- 历史列表：缩略图 + 时间 + 尺寸 + 窗口；支持按日期分组。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| WGC | 新增 `Platform.Windows/WindowsGraphicsCaptureService.cs`；`Kernel/Capture/IScreenCaptureService.cs` | 能力位驱动 UI；失败回退 GDI |
| 剪贴板图片 | `Kernel/Abstractions/ISystemClipboardService.cs`、`Services/AvaloniaSystemClipboardService.cs` | Windows 同时写 DIB/PNG + HDROP |
| 元数据索引 | 新增 `Services/CaptureHistoryStore.cs`；`CaptureCoordinator.cs`、`CaptureTabViewModel.cs` | JSONL 追加，损坏行跳过 |
| 窗口选择器 | 新增 `Views/WindowPickerWindow.axaml(.cs)`；`CapturePanel.axaml` | 复用窗口枚举与缩略图 |
| 动作链 | `CaptureCoordinator.cs`、`AppSettingsStore.cs`、`SettingsWindow.axaml(.cs)` | 每步独立日志与失败不阻断后续 |
| 命名/格式 | `CapturePaths.cs`、`AppSettingsStore.cs` | 模板非法字符需清洗 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| WGC | WinRT 互操作、窗口句柄生命周期、权限/系统版本 | M/L |
| 剪贴板图片 | 各平台格式差异；大图内存 | S/M |
| 元数据索引 | 并发写、删除同步、历史损坏 | S |
| 窗口选择器 | 缩略图枚举性能、UWP/ cloaked 窗口 | M |
| 动作链 | 外部命令安全与失败处理 | S/M |

## 7. 验收标准

1. 被其他窗口遮挡的目标窗口可通过“窗口选择”正确截取（WGC 路径），内容非黑屏。
2. 截图后剪贴板同时提供图片与文件：聊天工具粘贴得到图片，资源管理器粘贴得到文件。
3. 截图历史 1000 条加载 < 300ms；按窗口标题/日期筛选可用。
4. 动作链顺序可配置，任一步失败不影响后续，日志写明失败原因。
5. JPG/WebP 输出质量设置生效；命名模板生成合法文件名。
6. HDR 显示器截图不出严重偏色；设置页有明确说明。
7. 区域截图取消后无残留遮罩、无临时文件、前台窗口恢复。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows.Graphics.Capture 截屏文档](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture) | WGC 会话、Picker、帧池 | 2026-11-03 |
| [Desktop Duplication API](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/desktop-duplication-api) | 桌面复制与高性能采集的边界 | 2026-11-03 |
| [Windows 11 截图工具](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots) | 录屏、Text Actions、统一入口 | 2026-11-03 |
| [Snipaste](https://www.snipaste.com/) | 贴图、标注、元素检测 | 2026-11-03 |
| [ShareX 滚动截图](https://getsharex.com/docs/scrolling-screenshot) | 截图后工作流与滚动拼接思路 | 2026-11-03 |
| [FastStone Capture](https://faststone.org.cn/) | 窗口/对象/滚动截图能力 | 2026-11-03 |
| [Greenshot 帮助](https://getgreenshot.org/help/zh-cn/) | 编辑器与外部命令 | 2026-11-03 |
| [Screenpresso 手册](https://www.screenpresso.com/download/docs/ScreenpressoHelp.pdf) | 手动滚动拼接与编辑器 | 2026-11-03 |
