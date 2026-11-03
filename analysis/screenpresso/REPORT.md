# Screenpresso 1.8.3 静态逆向报告

本报告基于 `d:\TabTaskExplorer\analysis\screenpresso` 下的脚本与证据文件生成。分析对象是本机已安装的 Screenpresso 1.8.3.0（Learnpulse），不是公开样本。所有结论均标注为「直接证据」或「推断」。

## 1. 结论摘要

- **逆向覆盖程度**：主程序与 Capture 组件的 PE/CLR 结构、版本与 PDB 路径、.NET 类型清单/资源清单、字符串与配置键、原生视频编解码器的 Ghidra 函数级伪代码均已取得。主程序与 Capture 组件是 .NET 程序，Ghidra 按设计拒绝分析受管 PE（`managed_target_unsupported`），因此改用 PE/CLR 静态取证 + ILSpy 元数据/类型清单；真正的原生代码只存在于 `ScreenpressoCodec.dll`，已用 REA + Ghidra 12.1.4 完成分析（428 个函数、406 条已定义字符串）。
- **程序形态**：Screenpresso.exe 是 .NET Framework 4.7（`net47`）程序，PDB 路径直接暴露源码工程 `C:\git\screenpresso\Screenpresso\src`；主程序只导入 `mscoree.dll`，所有 Win32 能力通过 P/Invoke 调用。ScreenpressoCapture.exe 同样是 .NET（`C:\git\screenpresso\ScreenpressoCapture\src`），是区域选择/放大镜/摄像头/取色等捕获前台 UI。两者通过命名管道 + 序列化对象通信。
- **录屏链路**：Screenpresso 支持 Video for Windows（AVI）+ 自研编解码器 `SPV1`（`ScreenpressoCodec.dll`，导出 `DriverProc`/`SetDebugMode`/`SetSpriteMode`，内部使用 zlib 压缩）；也提供「直接录制为 MP4」模式（Media Foundation/NAudio），并可把视频转换为其它标准格式、导出动图 GIF。常规 AVI 链路是否为出厂默认尚未确认。现有项目完全走 FFmpeg `gdigrab + libx264` 生成 MP4，是单一路径。
- **主要差距**：项目已覆盖常见截图/录屏/快捷键/剪贴板/托盘/开机启动/OCR/二维码，但**缺少滚动截图、放大镜与快速查看、工作区、GIF/动图录制、系统音频、摄像头叠加、鼠标点击高亮、视频格式/画质/帧率完整参数、截图命名模板与扩展名/质量选择、图片+路径组合剪贴板、PDF/DOCX/打印导出、云分享与浏览器扩展宿主**；并且**全局热键实现方式与 Screenpresso 不同**（项目在进程内装 `WH_KEYBOARD_LL` 钩子；Screenpresso 以 `RegisterHotKey` + 辅助进程键拦截/命名管道为主）。

## 2. 目标与环境

| 项目 | 值 |
| --- | --- |
| 安装目录 | `C:\Users\QiuHai\AppData\Local\Learnpulse\Screenpresso` |
| 定位方式 | HKCU `App Paths\Screenpresso.exe` + 运行进程路径 + 开始菜单快捷方式 |
| 主程序 | `Screenpresso.exe`，18,397,280 字节，v1.8.3.0，Learnpulse，2020-07-24 |
| 捕获组件 | `ScreenpressoCapture.exe`，7,222,880 字节，v1.5.0.0，2020-06-18 |
| 原生编解码器 | `ScreenpressoCodec.dll`，167,656 字节，x64，v1.4.0.0，2016-11-14 |
| 32 位编解码器 | `SysWOW64\ScreenpressoCodec.dll`，146,664 字节 |
| 浏览器宿主 | `ScreenpressoBrowserNative.exe` + manifest（Chrome native messaging，扩展 ID `agffhkejbocomabiogfdjnbfcnpdljie`） |
| .NET 依赖 | `References\AWSSDK.Core/S3`、`GemBox.Document`、`Google.Apis.*`（含 Drive/YouTube/Translate） |
| Ghidra | `C:\tools\ghidra_12.1.4_PUBLIC`，JDK 21；REA 6.7.0 |

工作副本位于 `C:\Users\QiuHai\.cache\rea-screenpresso-target`，避免 Ghidra 对安装目录/可变路径的准入限制。

## 3. 程序信息与模块划分

### 3.1 PE / CLR 直接证据

| 项 | Screenpresso.exe | ScreenpressoCapture.exe | ScreenpressoCodec.dll |
| --- | --- | --- | --- |
| 架构 | PE32 x86 | PE32 x86 | PE32+ x64 |
| 子系统 | GUI | GUI | GUI (DLL) |
| 链接器 | 48.0 | 48.0 | 12.0 |
| 编译时间戳 | 2020-07-24 08:56:28 UTC | 2020-06-18 09:08:33 UTC | 2016-11-14 19:54:14 UTC |
| CLR 头 | 有，运行时 2.5 / 元数据 v4.0.30319 | 有，同左 | 无（原生） |
| 元数据大小 | 4,109,548 字节 | 2,160,948 字节 | — |
| 文件版本 | 1.8.3.0 | 1.5.0.0 | 1.4.0.0 |
| 入口令牌 | `0x06000A21` | `0x060002EA` | 入口 RVA `0x00008F50` |
| PDB 路径 | `C:\git\screenpresso\Screenpresso\src\obj\Release\net47\Screenpresso.pdb` | `C:\git\screenpresso\ScreenpressoCapture\src\obj\Release\net47\ScreenpressoCapture.pdb` | 无 |
| 导入 | 仅 `mscoree.dll!_CorExeMain` | 仅 `mscoree.dll!_CorExeMain` | `WINMM/KERNEL32/USER32/ADVAPI32/SHELL32` |
| 签名 | 存在证书表（6,240 字节，未验证签名链） | 存在证书表（6,240 字节） | 存在证书表（12,520 字节） |
| 清单 | `asInvoker`、`dpiAware=true/pm`、Common Controls v6 | `asInvoker` | `asInvoker` |
| 节区 | `.text/.rsrc/.reloc` | `.text/.rsrc/.reloc` | `.text/.rdata/.data/.pdata/.rsrc/.reloc` |
| 类数量（ILSpy） | 2,326 | 1,725 | — |

**结论（直接证据）**：主程序与 Capture 组件是 .NET Framework 4.7 的混合 WPF/WinForms 应用，不是 Qt/Delphi；Ghidra 对它们返回 `managed_target_unsupported` 是正确判定，不应再尝试反汇编 IL。

### 3.2 模块职责（直接证据 + 推断）

- `Screenpresso.exe`：工作区/编辑器/设置/分享/历史/托盘/热键注册，内嵌 `LearnPulse.Screenpresso` 与部分 `LearnPulse.ScreenpressoCapture` 类型。
- `ScreenpressoCapture.exe`：捕获前台 UI，类型清单包含 `RegionWindow`、`RegionWindowViewModel`、`SelectionArea`、`Magnifier`、`GrayScreenWindow`、`VideoTargetWindow`、`VideoTargetManagerViewModel`、`WebcamWindow`、`CapturePopup`、`CountdownCaptureForm`。
- `ScreenpressoCodec.dll`：Video for Windows 编解码器，导出 `DriverProc`、`SetDebugMode`、`SetSpriteMode`；`DriverProc` 分派 `DRV_*` 与 `ICM_COMPRESS_* / ICM_DECOMPRESS_*` 消息（证据：`rea-decompile-20261103-102103.log`）。
- `ScreenpressoBrowserNative.exe`：Chrome native messaging 宿主，manifest 声明 `com.screenpresso.api`，用于浏览器扩展与运行中的 Screenpresso.exe 通信。
- `References\*.dll`：PDF/DOCX（GemBox.Document）、云存储（AWS S3/Google Drive）、YouTube 上传/翻译等 .NET 依赖。

**通信方式（直接证据 + 推断）**：主程序字符串包含 `NamedPipeClientStream`、`NamedPipeServerStream`、`XNamedPipeClient`、`XNamedPipeServer`、`commandsPipeName`、`notificationsPipeName`、`KeyInterceptorRpcServer`、`CaptureParameters`、`CaptureResult`、`ScreenpressoCaptureSerializer`、`StartScreenpressoCapture`、`BeginStartScreenpressoCapture`、`ProcessStartInfo`。**强烈推断**：Capture 组件由主程序以子进程启动，通过命令/通知管道交换捕获参数与结果；键拦截也通过 RPC 服务器转发。具体协议字段与命令行格式仍未证实。

## 4. 关键功能点

### 4.1 截图模式与编辑（直接证据）

字符串/类型证据：

- `ScreenshotRegion`（0x4AB4F7）、`ScreenshotActiveWindow`（0x4AB508）、`ScreenshotLastRegion`（0x4AB51F）、`ScreenshotFullScreen`（0x4AB534）。
- `ScreenshotAutoscroll`（0x4AB549）、`ScreenshotAutoscrollHelp`（0x4AB55E）、`autoscrollDelay`（0x4AE228）、`autoscrollDelayForIE`（0x4AE238）、`autoscrollScrollbarWidth`（0x4AE3F0）、`PerformAutoScroll`（0x4F0E63）。
- 中文 UI：`捕获一个滚动区域`（UTF16 0x56CCDA）、`反复松开鼠标左键，同时使用滚动条选定区域以抓取图像，图片将覆盖前一张`（UTF16 0x56B52A）、`延迟截图`（UTF16 0x6184A8）。
- `screenshotCountdown`（0x4AD99A）、`screenshotDisableMagnifier`（0x4AD97F）、`ShowQuickView`（0x4AB5D8）、`QuickCaptureWindowVisible`（settings.xml）。
- 编辑工具设置类（0x6D35DC–0x6D368F）：`RectSettings`、`PolygonSettings`、`BraceSettings`、`EllSettings`、`FreeHandSettings`、`LineSettings`、`NumSettings`、`MagnifyingGlassSettings`、`TextSettings`、`TextBubbleSettings`、`HighlighterSettings`。
- 后处理：`EffectShadow`、`WatermarkImageContainer`、`WatermarkTextContainer`、`ImageEffects`、`CropContainer`、`CanvasResizeType`、`ResizeSettings`、`PrintSettings`、`GifEncoder`、`AnimatedGifResources`、`GemBox.Document`。
- OCR/条码：`OCR`、`OCRHelper`（0x49BCCA）、`ZXing`、`BarcodeReader/Writer`（0x49DBA9 起）。
- 文件名/扩展名/质量：`screenshotDefaultExt`（0x4AD428）、`screenshotQuality`（0x4AD43D）、`actionAfterScreenshot`（0x4AD7A9）、`ScreenshotFilenameType`（0x6D380C）、`screenshot_{num}`（0x6D3855）、`video_{num}`（0x6D386B）、`.png`（0x6D377A）。
- 截图动作枚举：`OpenWorkspace`（0x6D37C5）、`ClipboardDataType`（0x6D37D7）、值 `Image`（0x6D3802）；`ScreenshotFilenameType` 值 `Date`（0x6D384B）。

**可确认能力**：区域/活动窗口/上次区域/全屏/滚动区域截图；倒计时/延迟；放大镜（可禁用）；快速查看窗口；滚动截图按重叠区拼接；编辑工具含矩形/椭圆/箭头/自由画笔/荧光笔/多边形/大括号/文字气泡/序号/放大镜；效果、水印、裁剪、画布缩放、打印；OCR 与二维码/条码；PNG 等扩展名与质量可配置；支持"截图后打开工作区"。

### 4.2 录屏、编码与格式（直接证据）

配置键（0x4AD2F6–0x4AE166）：

- `videoCodecFcc`、`videoQuality`、`videoFormat`、`videoFps`、`videoCaptureMouse`、`audioRecordMicrophone`、`audioRecordSystem`、`videoCaptureAudioRate`、`videoCaptureDelaySettings`、`videoCaptureStopAfterDelay`、`videoCaptureStopAfterDelayValue`。
- 叠加/特效：`videoCaptureCursorHightlight`、`videoCaptureCursorHighlightColor`、`videoCaptureCursorHightlightClick`、`videoCaptureCursorHightlightClickWithSound`、`videoCaptureCursorHighlightColorLeftClick/RightClick`、`videoSelectedWebCam`、`webCamPercent`、`webCamLocation`、`captureVideoWebcamShortcutKey`、`captureVideoZoomShortcutKey`。
- 动图：`animatedGifFramerate`（0x4AE44A）、`animatedGifLoopCount`（0x4AE45F）、`GifEncoder`、`AnimatedGifResources`；中文 UI：`帧间延迟(只适用GIF文件)`（UTF16 0x61EB3C）、`Loop (animated GIF only)`（UTF16 0x54A19C）。
- 容器/编码证据：`VideoRecorderType` 值 `VideoForWindows`（0x6D2F5E / 0x4AB1C3）；`ScreenpressoCodec`（0x49BD05）；`avifil32.dll`、`AVIFileOpen/CreateStream/Write`（0x531BE2 起）；`MediaFoundationRecorderHelper`（0x49BDFE）、`IMFSinkWriter`（0x49EF2E）、`MFVideoFormat`（0x4A16F0）、`MFTranscodeContainerType`；`WMV1/WMV2/WMV3`（UTF16 0x68C1FC 起）。
- 中文 UI：`直接录制为MP4`（UTF16 0x629DBE）、`直接记录到一个MP4文件。需要更多的CPU和禁止更改输出文件格式`（UTF16 0x62A274）、`视频马上就将被转换为选定的标准视频格式`（UTF16 0x5EB758）、`录制完成后显示预览`（UTF16 0x585590）、`Microsoft AVI` 修复提示（UTF16 0x5F8F8E）。

**可确认能力**：录制目标为窗口/区域/全屏（`VideoRegion`、`LastVideoRegion`）；支持 Video for Windows AVI 录制，使用自研 `SPV1` 编解码器；另有"直接录制为 MP4"模式（Media Foundation，CPU 更高、格式固定）；支持录制后转换/转码为标准视频格式、动图 GIF；支持麦克风与系统回环音频；支持鼠标高亮/点击高亮/点击音效；支持摄像头画中画、录制中缩放快捷键；支持延迟录制、按延迟自动停止、录制预览；带 AVI 修复工具。出厂默认是否 AVI 未证实。

### 4.3 编解码器 `SPV1`（Ghidra 直接证据）

- `ScreenpressoCodec.dll` 是 Video for Windows (VFW/ICM) 编解码器，导出函数与地址：
  - `DriverProc` = `0x1800067E0`（导出 RVA `0x000067E0`）
  - `SetDebugMode` = `0x180006610`
  - `SetSpriteMode` = `0x180006EC0`
  - `SysWOW64\ScreenpressoCodec.dll` 为 32 位镜像（x86，基址 `0x10000000`），导出同一组 `DriverProc/SetDebugMode/SetSpriteMode`（RVA `0x6490/0x6A70/0x77B0`），编译时间与 x64 版相差 6 秒，说明发行时同时提供 32/64 位编解码器。
- `DriverProc` 伪代码分派 `DRV_LOAD/ENABLE/OPEN/CLOSE/DISABLE/FREE/CONFIGURE/QUERYCONFIGURE/INSTALL/REMOVE`、`ICM_GETINFO/CONFIGURE/ABOUT/GETDEFAULTQUALITY/GETSTATE`、`ICM_COMPRESS_GET_FORMAT/GET_SIZE/QUERY/BEGIN/COMPRESS/END`、`ICM_DECOMPRESS_*`（见 `ghidra/rea-decompile-20261103-102103.log`）。
- `FUN_1800074d0`（`ICM_COMPRESS_GET_FORMAT` 处理）直接比较 `biCompression` 常量 `0x31565053`，即 ASCII `SPV1`；接受 `BI_RGB (0)` 或 `SPV1` 输入，输出强制 `biCompression = 0x31565053`，并根据输入位深设置输出 `biBitCount` 为 `0x10/0x18/0x20`（见 `ghidra/rea-decompile-20261103-102306.log`）。
- 字符串证据：`vidc`（0x6650）、`SPV1`（0x6657）、`SCREENPRESSOCODEC.dll`（0x20796）、`DriverProc`（0x207AC）、`SetSpriteMode`（0x207C4）、`Screenpresso codec v1.4`（0x23A48）、zlib 错误串 `unknown compression method`/`incorrect header check`/`oversubscribed literal/length tree`（0x1EB40 起）。
- 注册表键 `SOFTWARE\LearnPulse\ScreenpressoCodec`（UTF16 0x1F290）。

**结论（直接证据）**：`SPV1` 是 Screenpresso 自研的 VFW 视频编码格式，支持 16/24/32 位 RGB 输入，内部使用 zlib/deflate 压缩；这是与项目 FFmpeg/H.264 完全不同的编码路线。

### 4.4 快捷键与全局热键（直接证据）

- P/Invoke：`RegisterHotKey`（0x4E934D）、`UnregisterHotKey`（0x4E935C）、`SetWindowsHookEx`（0x4ED28E）、`WH_KEYBOARD_LL`（0x4B2302）、`WM_HOTKEY`（0x4B200B）。
- 键拦截与 IPC：`XKeyInterceptorViaHook`（capture 类型）、`KeyInterceptorRpcServer`（UTF16 0x6683B4）、`commandsPipeName`/`notificationsPipeName`（UTF16 0x10891EA/0x108920C）、`XKeyInterceptorViaHook`、`HotKeyControl`。
- 配置键（0x4ADC79–0x4AE00A）：`captureShortcutKey`、`captureShortcutKeyWinModifier`、`hotKeyScreenshotActiveWindow`、`hotKeyScreenshotLastRegion`、`hotKeyScreenshotFullScreen`、`hotKeyScreenshotScrollingWnd`、`hotKeyScreenshotDelay`、`hotKeyReconizeText`、`hotKeyReconizeQRCode`、`hotKeyPtTimer`、`hotKeyScreenshotColorPicker`、`hotKeyCaptureVideo`、`hotKeyCaptureVideoRegion`、`hotKeyAndroidCenter`、`hotKeyOpenWorkspace`、`hotKeyShowQuickView`、`captureVideoWebcamShortcutKey`、`captureVideoZoomShortcutKey`；每个动作都有 `*WinModifier` 布尔项。
- 默认/持久化样例：`settings.xml` 中 `CaptureShortcutKey = Back ShiftKey FinalMode H P X Alt`，各 `HotKey*` 为 `None`。

**可确认能力**：多动作全局热键（区域/全屏/上次区域/活动窗口/滚动窗口/延迟/OCR/二维码/取色/录屏/录屏区域/工作区/快速查看/摄像头/缩放），支持 Win 修饰键；同时存在 `RegisterHotKey` 与低级键盘钩子，且键拦截可通过独立进程 RPC 转发。具体哪个动作走哪条路径为推断。

### 4.5 剪贴板与拖拽（直接证据）

- 格式常量：`CF_DIB`（0x4B34DE）、`CF_DIBV5`（0x4B34FD）、`CF_HDROP`（0x4B34F4）。
- API：`OpenClipboard`（0x4ECBFA）、`SetClipboardData`（0x4ECC2F）、`IDataObject`（0x498B9C/0x4EE081）、`DoDragDrop`（0x523A52）、`DragDropEffects`（0x4997AE）。
- 中文 UI：`复制到剪贴板`（UTF16 0x570B76）、`将图像复制到剪贴板`（0x571776）、`将图像及文件路径复制到剪贴板`（0x571DD8）、`将文件路径复制到剪贴板`（0x5723AA）。
- 枚举：`ClipboardDataType`（0x6D37D7），可见值 `Image`（0x6D3802）。

**可确认能力**：三种剪贴板落点（仅图像、图像+文件路径、仅文件路径），支持 DIB/DIBV5/HDROP，并支持拖放（工作区接收截图文件）。

### 4.6 系统集成（直接证据）

- 托盘/单实例：`systrayForm`（0x511229）、`sysTrayForm`（0x512D7F）、`SystrayIconRunningCount`（0x4AE7CB）、`editorSingleInstance`（0x4AE1AE）、`DisableSingleInstance`（0x4E8E81）、`Mutex`（0x496E2F）、`ReleaseMutex`（0x520DB5）、`NamedPipeServerStream`。
- 开机启动：`autoStart`（0x4AD279）、`AutoStart`（0x52A00E）、`startupKey`（0x511A22）、`Install0OnYourHardDriveAddAShortcutToStartupMenuAndAuToStartWithWindows`、`FailedToSetAutostart`。
- 浏览器集成：`ScreenpressoBrowserNative.manifest`（`com.screenpresso.api`，Chrome 扩展 ID）。
- 设置持久化：`%AppData%\Learnpulse\Screenpresso\settings.xml`（版本 12，`AutoStart=true`、`HistoryCount=10000`、工作区、分享账号、热键等）；`fum.bin` 为首次运行/许可标记（推断）。
- 工作区：`defaultWorkspace`、`workspaceCount`、`workspaceSaveLocation`、`workspaceCloseOnCapture`、`showWorkspaceWhenImageEditorCloses`、`hotKeyOpenWorkspace`、`WorkspaceSettingsCollection`。

**可确认能力**：托盘常驻、开机启动、单实例（互斥量+命名管道）、工作区（可保存/自动关闭/缩略图/排序）、浏览器扩展宿主、历史记录（默认上限 10000，删除时保留文件）。

### 4.7 分享与云（直接证据）

- 分享账号类型（0x6C53D6–0x6C578F）：`BoxAccount`、`TwitterAccount`、`EvernoteAccount`、`FacebookAccount`、`FlickrAccount`、`GmailAccount`、`EMailAccount`、`FtpAccount`、`ScriptAccount`、`MinusAccount`、`DropboxAccount`、`ImgurAccount`、`ImageShackAccount`、`JiraAccount`、`SpCloudAccount`、`YouSendItAccount`、`DriveAccount`、`YouTubeAccount`、`AmazonAccount`、`FileCopyAccount`、`OneNoteAccount`、`OneDriveAccount`。
- 依赖：`AWSSDK.S3`、`Google.Apis.Drive.v3`、`Google.Apis.YouTube.v3`、`GemBox.Document`。
- 中文 UI：`短网址（在剪贴板）`（UTF16 0x58A926/0x58B2C8）、`ScreenpressoCloud` 说明（UTF16 0x600870）。

**可确认能力**：多目标分享（社交、云盘、FTP、脚本、邮件、Jira、YouTube、S3 等），支持短链接回写剪贴板，支持 PDF/DOCX 文档导出（GemBox）。

## 5. 代表性函数/字符串证据索引

| 证据 ID | 地址/偏移 | 内容 | 位置 | 可靠性 |
| --- | --- | --- | --- | --- |
| E01 | PE 调试目录 | PDB `...\net47\Screenpresso.pdb` | `evidence/debug-directory.txt` | 直接 |
| E02 | CLR 头 `0x00002008` | 元数据 4,109,548 字节 / v4.0.30319 / 入口令牌 `0x06000A21` | `evidence/clr-header.txt` | 直接 |
| E03 | 导入表 | 仅 `mscoree.dll!_CorExeMain` | `evidence/imports.txt` | 直接 |
| E04 | 0x4AB4F7–0x4AB55E | `ScreenshotRegion/ActiveWindow/LastRegion/FullScreen/Autoscroll` | `evidence/strings-ascii.txt` | 直接 |
| E05 | 0x4AE228 / 0x4AE238 / 0x4AE3F0 | `autoscrollDelay/autoscrollDelayForIE/autoscrollScrollbarWidth` | 同上 | 直接 |
| E06 | 0x4AD2F6–0x4AE166 | `videoCodecFcc/videoQuality/videoFps/audioRecordSystem/...` | 同上 | 直接 |
| E07 | 0x4ADC79–0x4AE00A | 全部 `hotKey*` 配置键 + `*WinModifier` | 同上 | 直接 |
| E08 | 0x4B34DE–0x4B34FD | `CF_DIB/CF_DIBV5/CF_HDROP` | 同上 | 直接 |
| E09 | 0x4E934D / 0x4ED28E / 0x4B2302 | `RegisterHotKey/SetWindowsHookEx/WH_KEYBOARD_LL` | 同上 | 直接 |
| E10 | 0x6D2F2C / 0x6D2F5E / 0x6D36BF | `VideoRecorderType/VideoForWindows/SPV1` | 同上 | 直接 |
| E11 | UTF16 0x629DBE / 0x62A274 | `直接录制为MP4` / 固定格式说明 | `evidence/strings-utf16.txt` | 直接 |
| E12 | 0x1800067E0 | `DriverProc` 伪代码（DRV/ICM 分派） | `ghidra/rea-decompile-20261103-102103.log` | 直接 |
| E13 | 0x1800074D0 | 格式查询：接受 `BI_RGB`/`SPV1`，输出 `SPV1`，16/24/32bpp | `ghidra/rea-decompile-20261103-102306.log` | 直接 |
| E14 | codec 0x6650/0x6657 | `vidc` / `SPV1` 常量 | `evidence/codec-dll/strings-ascii.txt` | 直接 |
| E15 | codec 0x1EB40 起 | zlib inflate 错误串 | 同上 | 直接 |
| E16 | codec 导出表 | `DriverProc/SetDebugMode/SetSpriteMode`（x64 与 SysWOW64 x86 镜像一致） | `evidence/codec-dll/exports.txt`、`evidence/codec-dll32/exports.txt` | 直接 |
| E17 | 0x49A943/0x49A974/0x49B023 | `NamedPipeClientStream/NamedPipeServerStream/ScreenpressoCaptureSerializer` | `evidence/strings-ascii.txt` | 直接 |
| E18 | ILSpy 类型清单 | `RegionWindow/SelectionArea/Magnifier/WebcamWindow/VideoTargetWindow` | `evidence/ilspy-types-capture.txt` | 直接 |
| E19 | ILSpy 资源清单 | `Screenpresso.g.resources/*.baml`、`VideoSettingsUc.resources` | `evidence/ilspy-resources-main.txt` | 直接 |
| E20 | settings.xml | `AutoStart=true`、`HistoryCount=10000`、`LastScreenshotRegion`、热键字段 | 安装目录 `%AppData%\Learnpulse\Screenpresso\settings.xml` | 直接 |
| E21 | Ghidra 拒绝记录 | `capability_unavailable / unsupported_provider`，`ghidra managed_target_unsupported` | `ghidra/rea-analyze-20261103-100030.log` | 直接 |

## 6. 可靠性分级

### A. 直接证据（文件结构/字符串/反编译代码/配置）

- .NET 4.7 属性、PDB 源码路径、模块列表、版本、签名、清单、导入导出。
- 截图模式、滚动截图、延迟、放大镜、快速查看、编辑工具集合、水印/效果/裁剪/打印、OCR/二维码、分享账号。
- 录屏配置键、AVI/VFW、`SPV1` 四字符码、zlib 压缩、MP4 直接录制、GIF 帧率/循环、麦克风/系统音频、摄像头、鼠标点击高亮。
- 热键 API 与配置键、Win 修饰键、键拦截 RPC、命名管道。
- 剪贴板三种落点、DIB/DIBV5/HDROP、拖放。
- 托盘、单实例、开机启动、工作区、历史记录。

### B. 推断（有类型/字符串支持，但未跟踪完整调用链）

- 主程序通过 `StartScreenpressoCapture` 启动 Capture 子进程，并用 `CaptureParameters/CaptureResult` + 命名管道回传结果。
- MP4 直接录制使用 Media Foundation `IMFSinkWriter`（类型与字符串存在，未定位具体调用点）。
- 视频转换使用 Media Foundation Transcode API（`IMFTranscodeProfile` 等类型存在）。
- GIF 由录制结果或视频转换导出（存在 `GifEncoder` 与帧延迟/循环 UI，未确认触发路径）。
- 滚动截图采用"多帧重叠拼接"，具体拼接算法未反编译。
- 工作区自动关闭/缩略图/排序等行为由配置键推断。
- `fum.bin` 为首次运行/许可状态文件（未解析内容）。

### C. 未证实（需要动态验证或更深反编译）

- 各热键的默认组合（settings.xml 中为 `None`/序列化串，不能代表安装默认值）。
- `videoQuality`/`videoFps`/`bitrate` 的默认数值与映射曲线。
- `SPV1` 的帧内压缩细节（关键帧间隔、delta 帧、sprite 模式的具体用途）。
- ScreenpressoCloud/各分享服务的 API 端点与认证流程。
- Capture 子进程命令行的具体参数格式。

> 说明：`evidence/keyword-hits.md` 是二进制全量正则统计，含随机字节误报；本报告只采用经定向检索复核的条目。

## 7. 方法与复现

### 7.1 终端限制与稳定启动方式

- Trae Shell 的沙箱执行层会间歇性返回 `sandbox backend failed`（连 `Get-Date` 也会失败），且前台 Shell 的 `Start-Process` 子进程会被作业对象回收。
- 本轮验证出的稳定方式：**后台 Shell + `Start-Process` + 父进程保活**，或 **WMI `Win32_Process.Create`**。多个 WMI 调用并行会互相阻塞，必须串行。
- Ghidra 启动时限从默认 330 秒提升到 3,600,000 毫秒，堆上限 4G；实际编解码器分析 1 分钟内完成，未触发超时。

### 7.2 可复用脚本

| 脚本 | 用途 |
| --- | --- |
| `scripts\Invoke-StaticProbe.ps1` | 纯 PowerShell 读取 PE/CLR/资源/导入导出/调试目录/Rich 头/字符串/关键字；支持 `-SkipStrings` |
| `scripts\Run-Rea-Screenpresso.ps1` | Screenpresso 专用 REA 封装，独立锁与日志目录，1 小时超时 |
| `scripts\Start-Worker.ps1` | WMI 拉起游离 PowerShell，避免 Shell 作业对象回收 |
| `scripts\Invoke-All.ps1` | 一键入口（probe / rea-analyze / rea-search / rea-doctor） |

### 7.3 复现命令（PowerShell）

```powershell
# 静态取证（主程序）
powershell -NoProfile -ExecutionPolicy Bypass -File analysis\screenpresso\scripts\Invoke-StaticProbe.ps1 `
  -TargetPath C:\Users\QiuHai\.cache\rea-screenpresso-target\screenpresso.exe `
  -OutDir analysis\screenpresso\evidence

# 仅 PE/CLR（快速）
powershell -NoProfile -ExecutionPolicy Bypass -File analysis\screenpresso\scripts\Invoke-StaticProbe.ps1 `
  -TargetPath C:\Users\QiuHai\.cache\rea-screenpresso-target\screenpresso.exe `
  -OutDir analysis\screenpresso\evidence -SkipStrings

# Ghidra 分析原生编解码器
powershell -NoProfile -ExecutionPolicy Bypass -File analysis\screenpresso\scripts\Run-Rea-Screenpresso.ps1 `
  -Command analyze -TargetPath C:\Users\QiuHai\.cache\rea-screenpresso-target\screenpresso_codec.dll `
  -SnapshotPath analysis\screenpresso\ghidra\screenpresso_codec.snapshot
```

> 在上述命令前加 `Start-Process ... -WindowStyle Hidden` 并保持父进程存活，或改用 `Start-Worker.ps1`，即可绕开 Shell 子进程回收。

## 8. 仍待确认

1. Screenpresso 默认热键组合（需读取安装后默认配置或动态触发）。
2. `SPV1` 帧结构、关键帧策略、sprite 模式用途（需继续反编译 `ICM_COMPRESS_BEGIN/ICM_COMPRESS` 路径）。
3. 直接 MP4 录制的具体 MF 拓扑与编码参数（H.264 profile/bitrate/容器）。
4. GIF 是"录制时直接生成"还是"录制后转换"。
5. 滚动截图的拼接算法与重叠检测方式。
6. 云分享各服务的 API 端点、认证与短链接服务。
7. Capture 子进程命令行参数与管道消息 schema（可进一步反编译 `ScreenpressoCaptureSerializer` 与 `StartScreenpressoCapture`）。

## 9. 产出清单

- 证据：`analysis\screenpresso\evidence\`（主程序、`capture-exe\`、`codec-dll\`、`metadata\`）。
- Ghidra：`analysis\screenpresso\ghidra\`（`screenpresso_codec.snapshot`、分析/搜索/反编译日志）。
- 差距清单：`analysis\screenpresso\GAP_ANALYSIS.md`。
- 环境与方法笔记：`analysis\screenpresso\NOTES.md`。
