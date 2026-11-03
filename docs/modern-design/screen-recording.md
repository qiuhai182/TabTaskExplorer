# 录屏现代化设计

状态：设计稿（2026-11-03）
适用：`WindowsScreenRecordingService`、`FfmpegRecordingSession`、`CaptureCoordinator`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 全屏 / 主屏 / 区域 / 活动窗口录制 | `WindowsScreenRecordingService.ResolveArea`（:73-93） | 可用 | 物理像素区域 |
| FFmpeg gdigrab 采集 + H.264/MP4 | `BuildArguments`（:96-156） | 可用 | `libx264`、`veryfast`、CRF、`yuv420p`、`+faststart` |
| 帧率 24/30/60 | `AppSettings.CaptureRecordFps`、`BuildArguments` | 可用 | 1–120 范围校验 |
| 画质档位 | `AppSettings.CaptureRecordQuality` | 可用 | CRF 18–28 |
| 鼠标指针 | `-draw_mouse` | 可用 | 全局开关 |
| 麦克风 | DirectShow `audio=` 设备（:131-137） | 可用 | 仅麦克风，无系统声音 |
| 停止收尾 | `FfmpegRecordingSession.StopAsync`（:140-180） | 可用 | 向 stdin 写 `q`，最多等 8 秒后强杀 |
| 录制计时 / 状态 | `CaptureCoordinator.OnRecordingWatchTick`（:881）、`CaptureTabViewModel.UpdateRecordingState`（:575） | 可用 | 面板内计时 |
| 暂停 / 继续 | 无 | 缺口 | 只能停止后重新开始 |
| 系统音频（扬声器回环） | 无 | 缺口 | 仅麦克风 |
| 崩溃恢复 | 无 | 缺口 | 进程被杀时 MP4 可能不可播放 |
| 摄像头画中画 | 无 | 缺口 | — |
| 录制区域跟随窗口 | 无 | 缺口 | 窗口移动后区域不跟随 |
| Linux / macOS | `LinuxScreenRecordingService`、`MacScreenRecordingService` | 可用 | 各自拼 FFmpeg 参数 |

主要问题：

1. **gdigrab 性能与兼容性**：GDI 抓屏在高分辨率/高帧率下 CPU 高；对硬件加速窗口、HDR、受保护内容可能黑屏。
2. **无系统声音**：教程/演示录制通常需要“扬声器 + 麦克风”混音，目前只能录麦克风。
3. **无暂停**：录制中需要跳过隐私/等待片段时只能停掉重录，产物被拆成多个文件。
4. **崩溃不安全**：直接写 `.mp4`，FFmpeg 被强杀或断电时 `moov` 原子未写完，文件不可播放。
5. **无录制指示器**：只有面板内计时；用户切换到其他程序后不知道正在录制（托盘/悬浮指示缺失）。
6. **无分段/时长上限策略**：`DurationLimitSeconds` 存在但 UI 未暴露；长时间录制无自动分段。
7. **无丢帧/码率统计**：无法判断录制是否卡顿。
8. **活动窗口录制从本程序启动时会录到自己**：`CapturePanel` 的提示已说明，但交互上没有“倒计时后最小化”或“选择目标窗口”。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 11 截图工具 | 区域录屏、停止后可在 Clipchamp 编辑（[Microsoft 支持](https://support.microsoft.com/zh-cn/windows/apps/use-snipping-tool-to-capture-screenshots)） | 录屏应和截图共用入口与产物历史 |
| ShareX | 录屏任务、FFmpeg 参数可配置、录制后工作流（[文档](https://getsharex.com/docs/)） | 参数预设与动作链可复用截图模块设计 |
| FastStone Capture | 屏幕活动、麦克风、扬声器音频、摄像头、鼠标点击高亮（[官网](https://faststone.org.cn/)） | 系统音频、摄像头、鼠标点击高亮是差异点 |
| Screenpresso | HD 视频录制、内置编辑器、发布（[功能页](https://www.screenpresso.com/features/)） | 录制后剪辑/发布是 P1 |
| OBS Studio | 多源合成、硬件编码、场景切换 | 不做完整 OBS，但可借鉴“源/场景”模型与硬件编码 |

## 3. 推荐方案

### 必做（P0）

1. **Windows 采集后端升级**
   - 新增 `WindowsWgcRecordingBackend`：WGC 帧 + Media Foundation H.264 编码；FFmpeg 作为回退/跨平台统一后端。
   - 保留 `IScreenRecordingService` 契约，新增 `IRecordingBackend` 由平台选择。
   - 收益：降低 CPU、支持遮挡窗口、HDR 转 SDR、避免 gdigrab 黑屏。
   - 接入点：`Kernel/Capture/IScreenRecordingService.cs` 增加能力位；`CompositionRoot` 注册；`WindowsScreenRecordingService` 保留回退。

2. **系统音频 + 麦克风混音**
   - Windows 用 WASAPI loopback 采集扬声器，麦克风单独采集，混音后送编码器；设置项“系统声音 / 麦克风 / 两者”。
   - 接入点：新增 `Platform.Windows/WasapiLoopbackCapture.cs`；`ScreenRecordingOptions` 增加 `AudioSource`、`MicrophoneVolume`、`SystemAudioVolume`。
   - 注意：无音频设备/独占模式时降级为静音并在 UI 提示。

3. **崩溃安全录制**
   - 录制先写 `.mkv`（Matroska 对中断更鲁棒），停止后 `ffmpeg -c copy` 转 `.mp4`；转换失败保留 `.mkv` 并提示。
   - 启动时扫描未完成录制（`.mkv` + 锁文件），提供“恢复/删除”。
   - 接入点：`FfmpegRecordingSession.StartAsync/StopAsync`；`CapturePaths`；`CaptureCoordinator` 启动时恢复。

4. **暂停 / 继续（分段拼接）**
   - 暂停 = 结束当前段；继续 = 新开一段；停止时用 FFmpeg concat demuxer 无损拼接（同编码参数）。
   - UI 显示“已暂停”，产物仍为单个 MP4（拼接失败则保留分段并提示）。
   - 接入点：`CaptureCoordinator.ToggleRecordingAsync`（:847）；`FfmpegRecordingSession` 增加 `PauseSegment/ResumeSegment`；`ScreenRecordingOptions.SegmentIndex`。
   - 风险：拼接时间基/关键帧不一致；参数必须完全一致。

5. **录制指示器与全局状态**
   - 托盘图标变为“录制中”红色角标；主窗口标题加“● 录制中”；可选悬浮小窗（可关闭）。
   - 全局快捷键停止录制时无需切回主窗口。
   - 接入点：`App.axaml.cs` 托盘菜单/图标；`MainWindow` 标题绑定；`CaptureCoordinator` 事件。

6. **目标窗口选择与倒计时**
   - 录制前可选“窗口”，倒计时 3 秒后自动最小化本程序（若目标是本程序则提示）。
   - 接入点：复用 `WindowPickerWindow`（截图模块）；`CaptureCoordinator.StartRecordingAsync`。

### 可选（P1/P2）

- 摄像头画中画（Windows Media Foundation / OpenCV），位置与大小可调。
- 鼠标点击高亮/按键显示（教程场景）。
- 录制后剪辑：裁剪头尾、静音段、导出 GIF。
- 硬件编码优先（NVENC/QSV/AMF），失败回退 libx264。
- 自动分段（按 2GB/30 分钟），避免单文件过大。
- 直播推流（RTMP）——与产品定位较远，P2 且需单独安全设计。

### 不建议

- 自研完整非编器：成本高，先接系统播放器/剪映等外部工具。
- 默认开启系统音频：可能录到隐私内容；首次使用需明确提示。
- 用 `gdigrab` 作为唯一 Windows 后端：性能与兼容性问题会持续。

## 4. 交互与视觉

- 录屏区：目标下拉（全屏/主屏/区域/窗口/活动窗口）、帧率、画质、音频源、是否含指针、是否含点击高亮。
- 录制中：红色圆点 + 计时 + 预估文件大小 + 暂停/停止按钮；托盘同步红点。
- 暂停：按钮变“继续”，计时暂停；状态文字“已暂停（当前段 00:32）”。
- 停止后：toast“已保存 + 时长 + 大小”，提供“打开文件夹 / 播放 / 删除”。
- 崩溃恢复：启动时弹“发现未完成录制，是否恢复为 MP4？”。
- 隐私提示：首次勾选系统音频时说明“将录制扬声器输出，请避免播放敏感内容”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| WGC 后端 | 新增 `Platform.Windows/WindowsWgcRecordingBackend.cs`；`Kernel/Services/FfmpegRecordingSession.cs` 保留 | 能力位选择后端 |
| 系统音频 | 新增 `Platform.Windows/WasapiLoopbackCapture.cs`；`Kernel/Capture/IScreenRecordingService.cs` | 混音在应用层/内核服务完成 |
| 崩溃安全 | `FfmpegRecordingSession.cs`、`CapturePaths.cs`、`CaptureCoordinator.cs` | `.mkv` → `.mp4` remux |
| 暂停拼接 | `FfmpegRecordingSession.cs`、`CaptureCoordinator.cs` | concat 列表写入临时目录 |
| 指示器 | `App.axaml.cs`、`MainWindow.axaml(.cs)`、`CaptureCoordinator.cs` | 托盘图标状态切换 |
| 目标窗口 | `Views/WindowPickerWindow.axaml`、`CaptureCoordinator.cs` | 与截图共用 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| WGC + MF 编码 | WinRT/COM 生命周期、编码参数、音视频同步 | L |
| 系统音频 | 设备独占、采样率不匹配、无设备 | M |
| 暂停拼接 | 段间关键帧/时间基不一致，可能花屏 | M |
| 崩溃恢复 | remux 失败、临时文件清理 | S/M |
| 摄像头/点击高亮 | 额外采集与合成 | M |

## 7. 验收标准

1. 1080p60 录制 10 分钟，CPU 占用低于当前 gdigrab 方案（以同机对比为准），无丢帧提示。
2. 系统音频 + 麦克风可同时录制，回放两者清晰、无明显不同步。
3. 暂停 30 秒后继续，最终单个 MP4 可播放，暂停段不出现。
4. 录制中强杀进程，重启后可恢复为可播放 MP4（或保留 MKV 并提示）。
5. 托盘/标题栏在录制中持续显示红点，停止后恢复。
6. 选择被遮挡窗口录制可正确采集；选择本程序窗口时给出明确提示。
7. 录制失败（FFmpeg 缺失/设备占用）时给出中文原因，不产生 0 字节文件。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows 11 截图工具](https://support.microsoft.com/zh-cn/windows/apps/use-snipping-tool-to-capture-screenshots) | 录屏入口与后续编辑 | 2026-11-03 |
| [FastStone Capture](https://faststone.org.cn/) | 系统音频、摄像头、鼠标高亮 | 2026-11-03 |
| [Screenpresso 功能](https://www.screenpresso.com/features/) | HD 录屏与发布 | 2026-11-03 |
| [ShareX 文档](https://getsharex.com/docs/) | 录制工作流与参数 | 2026-11-03 |
| [Windows.Graphics.Capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture) | WGC 帧采集 | 2026-11-03 |

注：WASAPI loopback 与 Media Foundation 编码为本项目拟采用的 Windows 原生路径，具体 API 细节需在实现阶段以官方文档复核；本文不声称已完成实测。
