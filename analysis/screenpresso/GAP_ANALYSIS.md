# 逆向结论 vs 现有实现：差距清单

对照对象：Screenpresso 1.8.3.0（逆向证据见 `REPORT.md`）与 `src\TabTaskExplorer` 当前截图/录屏实现。

状态定义：
- **已覆盖**：核心行为一致，参数可接受。
- **参数不一致**：功能存在，但默认值、格式、目标或交互与 Screenpresso 不同。
- **缺失**：Screenpresso 有直接证据、项目没有对应能力。
- **架构差异**：实现路径不同，不一定需要改，但会影响行为/依赖/风险。

## 1. 总览

| # | 能力 | Screenpresso 逆向结论 | 现有实现 | 状态 |
| --- | --- | --- | --- | --- |
| 1 | 区域/全屏/活动窗口/上次区域截图 | `ScreenshotRegion/FullScreen/ActiveWindow/LastRegion`；全屏遮罩 + 区域选择 | `CaptureTarget` 四类 + `ScreenshotLastRegion`；`RegionSelectionWindow` | 已覆盖 |
| 2 | 截图延迟/倒计时 | `screenshotCountdown`、`ScreenshotDelay`、倒计时窗口 `CountdownCaptureForm`、延迟热键 | 延迟 0/3/5/10 秒，`CaptureDelaySeconds` | 参数不一致（无可配置倒计时/独立延迟热键） |
| 3 | 滚动截图（长截图） | `ScreenshotAutoscroll`、`PerformAutoScroll`、`autoscrollDelay`、`autoscrollScrollbarWidth`；多帧重叠拼接 | 无 | **缺失** |
| 4 | 放大镜/快速查看 | `Magnifier`、`MagnifyingGlassSettings`、`ShowQuickView`、`QuickCaptureWindowVisible` | 编辑器内有取色放大镜；无截图放大镜/快速查看 | 部分缺失 |
| 5 | 截图编辑工具 | 矩形/椭圆/箭头/自由画笔/荧光笔/多边形/大括号/文字气泡/序号/放大镜/裁剪/画布缩放/水印/效果/打印 | 11 种工具：矩形/椭圆/箭头/画笔/荧光笔/马赛克/模糊/文字/序号/放大镜/橡皮擦 | 部分覆盖（缺多边形/大括号/气泡/水印/效果/裁剪/画布缩放/打印） |
| 6 | 截图导出格式 | `screenshotDefaultExt`、`screenshotQuality`；`ScreenshotFilenameType`、`screenshot_{num}` 模板；PNG/JPG/BMP/TIFF/GIF/PDF/DOCX（GemBox） | 固定 PNG；命名 `截图_yyyyMMdd_HHmmss.png`；编辑器另存仅 PNG | **参数不一致 + 缺失**（无扩展名/质量/模板/PDF） |
| 7 | 截图后动作 | `actionAfterScreenshot` 可配（打开工作区/编辑器/剪贴板/分享） | 复制文件路径到剪贴板、打开产物文件夹、打开编辑器，三个开关 | 参数不一致（无工作区/分享，剪贴板语义不同） |
| 8 | 剪贴板落点 | 三种：仅图像、图像+文件路径、仅文件路径（`ClipboardDataType`、`CF_DIB/CF_DIBV5/CF_HDROP`） | 截图后写文件路径列表（CF_HDROP）；编辑器复制图像 | **参数不一致**（缺"图像+路径"和"仅图像"组合；截图默认不是图像） |
| 9 | 拖拽 | `DoDragDrop`、`IDataObject`、`DragDropEffects`、`CF_HDROP` | 支持文件拖放/路径拖拽（文件管理器内核层） | 部分覆盖（截图/工作区拖放语义不同） |
| 10 | OCR | `OCR`、`OCRHelper` | Windows.Media.Ocr / Tesseract；`OcrResultWindow` | 已覆盖（实现不同） |
| 11 | 二维码/条码 | `ZXing`、`BarcodeReader/Writer` | ZXing.Net，支持 QR/DataMatrix/Aztec/PDF417/一维码 | 已覆盖 |
| 12 | 工作区 | `WorkspaceSettings`、`hotKeyOpenWorkspace`、缩略图/自动关闭/排序/拖拽 | 无工作区；只有产物历史列表 | **缺失** |
| 13 | 录屏容器/编码 | AVI（VFW）+ `SPV1` 自研无损编解码器（zlib）；另有"直接录制为 MP4"（Media Foundation） | MP4（H.264/libx264，FFmpeg `gdigrab`），单一容器/编码 | **架构差异 + 缺失 AVI/SPV1/直接 MP4 选项** |
| 14 | 录屏帧率/画质/码率 | `videoFps`、`videoQuality`、`videoCodecFcc`、`videoFormat`、`VideoConvertQualityType`、`VideoResize*` | 24/30/60 fps；三档 CRF 预设；无码率/分辨率/格式选择 | 参数不一致 |
| 15 | 麦克风 + 系统音频 | `audioRecordMicrophone`、`audioRecordSystem`、`videoCaptureAudioRate` | 仅麦克风（DirectShow/PulseAudio/设备序号） | **缺失系统音频回环** |
| 16 | 摄像头画中画 | `WebcamWindow`、`videoSelectedWebCam`、`webCamPercent/webCamLocation`、`captureVideoWebcamShortcutKey` | 无 | **缺失** |
| 17 | 鼠标高亮/点击效果 | `videoCaptureCursorHightlight`、`...Click`、`...ClickWithSound`、左右键颜色 | 仅"包含鼠标指针"（FFmpeg `draw_mouse`） | **缺失高亮/点击视觉与音效** |
| 18 | 录制中缩放/快捷键 | `captureVideoZoomShortcutKey`、`captureVideoWebcamShortcutKey` | 无 | **缺失** |
| 19 | 录制延迟/自动停止 | `videoCaptureDelaySettings`、`videoCaptureStopAfterDelay`/`Value` | 无录制延迟；无自动停止（有 `DurationLimitSeconds` 内核字段但界面未暴露） | 部分缺失 |
| 20 | 动图 GIF | `GifEncoder`、`animatedGifFramerate`、`animatedGifLoopCount`、帧间延迟 UI | 无 | **缺失** |
| 21 | 视频转换/标准格式 | `VideoConverterParameters`、`IMFTranscodeProfile`、`视频马上就将被转换为选定的标准视频格式` | 无 | **缺失** |
| 22 | AVI 修复 | `VideoRecoverToolForm`、`Microsoft AVI` 修复提示 | 无 | **缺失**（低优先级） |
| 23 | 输出命名模板 | `screenshot_{num}` / `video_{num}`、`Date` 枚举 | 固定中文前缀 + 时间戳 | 参数不一致 |
| 24 | 全局热键动作 | 区域/全屏/上次区域/活动窗口/滚动窗口/延迟/OCR/二维码/取色/录屏/录屏区域/工作区/快速查看/摄像头/缩放 | 区域/全屏/主屏/活动窗口/上次区域/录屏切换/取色/贴图 | 部分覆盖（缺滚动/延迟/OCR/二维码/工作区/快速查看/摄像头/缩放） |
| 25 | 全局热键实现 | `RegisterHotKey` + `WH_KEYBOARD_LL` + `XKeyInterceptorViaHook` + `KeyInterceptorRpcServer`（辅助进程/命名管道） | 进程内 `WH_KEYBOARD_LL` 低级钩子，忽略注入键，吞掉 Win/Alt 抬起 | **架构差异** |
| 26 | Win 修饰键 | 每个 `hotKey*` 配套 `*WinModifier` | `HotkeySetting.Win` 支持 | 已覆盖 |
| 27 | 托盘/开机启动/单实例 | `systrayForm`、`autoStart`、`Mutex`、命名管道 | `TrayIcon`、`AutoStartManager`（HKCU Run）、`SingleInstanceService`（互斥量+管道） | 已覆盖 |
| 28 | 浏览器扩展宿主 | `ScreenpressoBrowserNative.exe`（Chrome native messaging） | 无 | **缺失**（低优先级） |
| 29 | 云分享/短链接 | 20+ 分享账号、S3/Drive/YouTube/FTP/脚本、短链接回写剪贴板 | 无 | **缺失**（产品范围外，建议不做） |
| 30 | 截图/录屏依赖 | 自带 VFW 编解码器 + Media Foundation，无外部 FFmpeg 依赖 | 录屏强依赖 FFmpeg（找不到则禁用录屏） | **架构差异** |

## 2. 参数不一致的具体位置

| 项 | Screenpresso | 现有实现 | 影响 |
| --- | --- | --- | --- |
| 截图输出 | 扩展名/质量/文件名模板可配（`screenshotDefaultExt`、`screenshotQuality`、`screenshot_{num}`） | `CapturePaths.CreateScreenshotPath` 固定 `截图_时间.png`；`ImageFormat.Png` | 用户无法选 JPG/质量/序号命名 |
| 剪贴板 | 图像/图像+路径/路径三选一 | `CaptureCoordinator` 截图后 `WriteAsync(new[]{path}, false)` 仅文件路径；编辑器写位图 | 与"截图后可直接粘贴图片"的预期不一致 |
| 录屏画质 | 多档质量/码率/格式/分辨率 | `WindowsScreenRecordingService` 固定 `libx264`、`veryfast`、`crf`、`yuv420p`、`+faststart` | 无法输出 AVI/其它 MP4 参数 |
| 录屏目标 | 窗口/区域/全屏 + 上次区域 | 全屏/区域/活动窗口（`RecordingTargetIndex` 0–2） | 缺"上次视频区域"复用 |
| 延迟 | 倒计时窗口 + 可配延迟 + 延迟热键 | 0/3/5/10 秒下拉 | 无法精确配置/用热键触发延迟截图 |
| 热键默认值 | 安装后可自定义，支持 Win 修饰 | `PrintScreen` 区域、`Ctrl+PrintScreen` 全屏、`Ctrl+Alt+PrintScreen` 窗口、`Ctrl+Shift+PrintScreen` 上次区域、`Ctrl+Shift+R` 录屏 | 默认组合与 Screenpresso 不一定一致 |

## 3. 建议的后续集成顺序（仅供参考，本轮未改源码）

### P0：低成本、直接补齐行为差异

1. **截图剪贴板模式**：增加"仅图像 / 图像+路径 / 仅路径"三选一，复用 `ISystemClipboardService`；默认改为"图像+路径"更贴近 Screenpresso。
2. **截图格式与命名**：`CapturePaths` 增加扩展名/质量/模板参数（`screenshot_{num}` 与日期两种），编辑器另存支持 JPG/BMP/TIFF。
3. **录屏参数暴露**：把 `DurationLimitSeconds`、分辨率/码率、容器选项接到设置页；补"上次视频区域"。
4. **录制延迟/自动停止**：复用现有延迟配置和 `DurationLimitSeconds`，界面增加开关。

### P1：中等成本、用户价值高

5. **系统音频回环**：Windows 用 WASAPI loopback（或 FFmpeg `dshow`/`wasapi` 虚拟设备），与麦克风混音；Linux/macOS 用 PulseAudio/CoreAudio 对应实现。
6. **GIF 导出**：优先做"录屏后转 GIF"（FFmpeg 调色板 + 帧率/循环参数），避免新增录制编码器。
7. **鼠标点击高亮**：录制时叠加点击圆圈/音效；可先用 FFmpeg `drawbox`/后期合成，或屏幕采集时自绘。
8. **编辑器补齐**：水印、裁剪、画布缩放、文字气泡、多边形/大括号；PDF 导出可评估引入 PDF 库（注意跨平台与体积）。
9. **滚动截图**：独立功能，需实现滚动消息注入、重叠区匹配与拼接；跨平台复杂，建议单独立项。

### P2：高成本或产品范围外

10. **摄像头画中画 / 录制中缩放快捷键**：需要视频合成管线。
11. **SPV1/AVI 原生编解码器**：需要自行实现 VFW 编解码器或改为内置采集编码；与现有 FFmpeg 路线冲突，收益主要是"零外部依赖 + 无损中间格式"，不建议短期做。
12. **云分享/浏览器扩展宿主/工作区**：产品定位不同，建议不做或单独立项。

## 4. 已覆盖但实现方式不同的点（无需按原样复刻）

- **截图采集**：Screenpresso 用 `GetWindowDC/BitBlt/GetDIBits/CAPTUREBLT + DwmGetWindowAttribute`；项目用 GDI+ `CopyFromScreen` + `DrawIconEx`。功能等价，注意分层窗口/DPI 场景的差异。
- **热键**：Screenpresso 的 `RegisterHotKey` 不会拦截按键；项目的 `WH_KEYBOARD_LL` 会吞掉按键。若后续要与 Screenpresso 行为一致，应把"注册型热键"作为默认，低级钩子仅作为可选增强。
- **OCR**：Screenpresso `OCRHelper`（具体引擎未证实）；项目用 Windows.Media.Ocr/Tesseract，跨平台策略更明确。
- **单实例/托盘/开机启动**：两边都有，项目实现已满足功能。

## 5. 证据引用

- 截图模式与滚动：`evidence/strings-ascii.txt` 0x4AB4F7–0x4AB55E、0x4AE228/0x4AE3F0；`evidence/strings-utf16.txt` 0x56CCDA/0x56B52A。
- 录屏参数：`evidence/strings-ascii.txt` 0x4AD2F6–0x4AE166；`evidence/strings-utf16.txt` 0x629DBE/0x62A274/0x5EB758。
- SPV1 编解码器：`ghidra/rea-decompile-20261103-102103.log`（DriverProc）、`ghidra/rea-decompile-20261103-102306.log`（格式处理）、`evidence/codec-dll/exports.txt`、`evidence/codec-dll/strings-ascii.txt` 0x6650/0x6657。
- 热键：`evidence/strings-ascii.txt` 0x4ADC79–0x4AE00A、0x4E934D/0x4ED28E/0x4B2302。
- 剪贴板：`evidence/strings-ascii.txt` 0x4B34DE–0x4B34FD、0x4ECBFA/0x4ECC2F；中文 UI `evidence/strings-utf16.txt` 0x570B76–0x5723AA。
- 进程/IPC：`evidence/strings-ascii.txt` 0x49A943/0x49A974/0x49B023/0x4B0C79；`evidence/strings-utf16.txt` 0x6683B4。
- 现有实现：`src/TabTaskExplorer.Platform.Windows/WindowsScreenRecordingService.cs`、`WindowsScreenCaptureService.cs`、`WindowsGlobalHotkeyService.cs`、`src/TabTaskExplorer.App/Services/Capture/CaptureCoordinator.cs`、`CapturePaths.cs`、`src/TabTaskExplorer.Kernel/Capture/*.cs`、`src/TabTaskExplorer.App/Services/HotkeySetting.cs`。
