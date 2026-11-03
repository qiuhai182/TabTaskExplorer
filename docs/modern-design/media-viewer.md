# 媒体查看器现代化设计

状态：设计稿（2026-11-03）
适用：`MediaTabViewModel`、`MediaPlaylistViewModel`、`MediaPanel`、`VlcPlaybackEngine`
调研：联网检索（2026-11-03）+ `docs/media-viewer-design.md` + 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 媒体标签 | `MainWindowViewModel.OpenMediaAsync`（:386）、`OpenMediaTab`（:418） | 可用 | 图片/视频/音频新标签 |
| 图片查看 | `ImageViewerControl.axaml.cs` | 可用 | 滚轮锚点缩放 10%–1600%、拖拽平移、旋转、适应窗口/原尺寸、上一张/下一张 |
| 视频播放 | `MediaTabViewModel`、`VlcPlaybackEngine` | 可用 | 播放/暂停、进度、音量、倍速、全屏、音轨、外挂字幕、画面截图 |
| 音频播放 | `MediaTabViewModel`、`MediaPlaylistViewModel` | 可用 | 封面/元数据/播放控制/结束自动下一项 |
| 播放列表侧栏 | `MediaPlaylistViewModel.cs`、`MediaPanel.axaml` | 可用 | 同目录同类/全部媒体，可切换、折叠、宽度持久化 |
| 打开方式规则 | `MediaOpenPolicy`、`AppSettings.MediaOpenMode/Rules` | 可用 | 内置/系统/每次询问；按类型覆盖 |
| 解码后端 | `VlcPlaybackEngine`、`NullMediaPlaybackEngine` | 可用 | Windows 随包 libvlc；缺失降级并提示 |
| 大图降采样 | `AppSettings.MediaImageMaxDecodeSize`（默认 4096） | 可用 | 避免超大图内存 |
| 视频裁剪/转码 | 无 | 缺口 | 只播放，不编辑 |
| 远程/压缩包媒体流式播放 | 无 | 缺口 | `docs/media-viewer-design.md` §8 M4 |
| 播放进度记忆 | 无 | 缺口 | 关闭后从头开始 |
| 字幕编码/同步 | 半成品 | 半成品 | 可加载字幕，无编码选择/时间偏移 |
| 硬件解码配置 | 无 | 缺口 | 使用 VLC 默认 |
| 系统媒体键/缩略图工具栏 | 无 | 缺口 | 无播放控制集成 |

主要问题：

1. **播放列表缩略图/元数据有限**：列表以文件名为主，图片/视频缺少缩略图与时长/分辨率；大目录滚动体验一般。
2. **无播放进度记忆**：视频看到一半关闭，下次从头；无“继续播放”。
3. **字幕体验弱**：无字幕编码选择（GBK/UTF-8）、时间偏移、字体/大小设置；外挂字幕加载失败提示不具体。
4. **无硬件解码/性能选项**：高码率 4K 视频可能 CPU 占用高；无“自动/禁用硬解”开关。
5. **远程与压缩包媒体不可播放**：SFTP/FTP 标签、压缩包内图片/视频只能解压/下载后看。
6. **无系统媒体集成**：键盘媒体键、任务栏缩略图工具栏、通知控制未接入。
7. **音频无波形/均衡器**：只有封面与控制条；无频谱/波形（设计列为 M4 彩蛋）。
8. **无播放列表持久化/收藏**：关闭标签后列表丢失。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| VLC | 全格式、硬件解码、字幕、播放列表、媒体键 | 解码能力已有；补硬解/字幕/媒体键 |
| MPC-HC / mpv | 轻量、硬解、字幕同步、快捷键 | 字幕偏移/硬解开关 |
| Windows 照片 | 图片浏览、缩放、旋转、幻灯片 | 图片体验基准 |
| foobar2000 | 音频播放列表、元数据、波形 | 音频列表/元数据 |
| PotPlayer | 视频播放、字幕、截图、倍速 | 视频控制完整度 |
| LibVLCSharp | VLC 的 .NET 绑定（[LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp)） | 现有引擎 |

## 3. 推荐方案

### 必做（P0）

1. **播放列表缩略图与元数据**
   - 图片缩略图 64×64；视频首帧缩略图（限流/缓存）；显示时长、分辨率、大小；当前项高亮。
   - 接入点：`MediaPlaylistViewModel`、`MediaPanel.axaml`；新增 `MediaThumbnailCache`。
   - 注意：视频缩略图解码成本高，限制并发 2、缓存到磁盘。

2. **播放进度记忆**
   - 每个媒体路径记录 `PositionSeconds`（最多 200 条），下次打开提示“继续播放/从头开始”；完成度 > 95% 清除。
   - 接入点：`AppSettings.MediaPositions`；`MediaTabViewModel` 加载/保存；`VlcPlaybackEngine` 事件。

3. **字幕增强**
   - 字幕编码选择（自动/UTF-8/GBK/UTF-16）、时间偏移 ±10 秒、字体/大小/颜色、字幕列表多选。
   - 接入点：`MediaTabViewModel`、`MediaPanel.axaml`、`VlcPlaybackEngine`（字幕轨道/延迟 API）。

4. **硬件解码开关**
   - 设置“自动/优先硬解/禁用硬解”；失败自动回退软解并提示；显示当前解码器。
   - 接入点：`VlcPlaybackEngine` 初始化参数；`AppSettings.MediaHardwareDecode`。

5. **远程/压缩包媒体（M4）**
   - 对 SFTP/FTP/压缩包条目，先下载/解压到临时文件再播放；播放结束清理；大文件提示“先下载”。
   - 接入点：`MainWindowViewModel.OpenMediaAsync`、`IFileContentAccess`/`ArchiveService`；`MediaTabViewModel` 支持临时源。

### 可选（P1/P2）

- 系统媒体键/任务栏缩略图工具栏（播放/暂停/上一首/下一首）。
- 音频波形/频谱、均衡器、声道选择。
- 视频裁剪/转码/导出 GIF（调用 FFmpeg）。
- 幻灯片播放、图片 EXIF 信息面板。
- 播放列表收藏/持久化、随机/循环模式。
- HDR/色彩管理说明与开关。

### 不建议

- 自研解码器：继续用 LibVLC/系统解码。
- 内置完整视频编辑器：与“查看器”定位冲突。
- 默认把所有远程媒体自动下载到磁盘：大文件风险，必须提示。

## 4. 交互与视觉

- 媒体区背景：图片用 `ContentBrush`，视频纯黑；控制条 `ChromeBrush`。
- 控制条：播放/暂停、进度、时间、音量、倍速、字幕、全屏、截图。
- 播放列表：缩略图 + 名称 + 时长；当前项强调色；顶部“同类/全部”切换。
- 字幕：右键“字幕”→ 加载/编码/偏移/样式。
- 继续播放：打开时底部提示条“上次看到 12:34，继续/从头”。
- 远程媒体：标题显示“远程/压缩包”，播放前显示下载进度。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 缩略图 | `MediaPlaylistViewModel.cs`、新增 `Services/MediaThumbnailCache.cs` | 视频首帧限流 |
| 进度记忆 | `AppSettings`、`MediaTabViewModel.cs` | 路径为键 |
| 字幕 | `MediaTabViewModel.cs`、`VlcPlaybackEngine.cs` | 编码/延迟/样式 |
| 硬解 | `VlcPlaybackEngine.cs`、`AppSettings` | 失败回退 |
| 远程媒体 | `MainWindowViewModel.OpenMediaAsync`、`ArchiveService` | 临时文件清理 |
| 媒体键 | 新增 `Platform.Windows/WindowsMediaKeyService.cs` | 可选 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 视频缩略图 | 解码开销/缓存膨胀 | M |
| 字幕 | 编码误判/时间偏移 | S/M |
| 硬解 | 不同 GPU/驱动兼容 | M |
| 远程媒体 | 临时文件/大文件/清理 | M |
| 媒体键 | 全局钩子/冲突 | S/M |

## 7. 验收标准

1. 播放列表显示图片/视频缩略图与时长，滚动不卡顿。
2. 视频关闭后再次打开提示继续播放；完成度 >95% 不提示。
3. GBK/UTF-8 字幕正确显示；时间偏移 ±10 秒生效。
4. 硬解开关生效，失败回退软解并提示当前解码器。
5. SFTP/FTP/压缩包内图片可预览，视频提示下载后播放；临时文件清理。
6. 媒体键可控制播放/暂停/上一首/下一首（P1）。
7. 4K 视频播放 CPU 占用低于软解（以同机对比为准）。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) | VLC .NET 绑定/解码 | 2026-11-03 |
| [VLC](https://www.videolan.org/vlc/) | 硬解/字幕/播放列表 | 2026-11-03 |
| [mpv](https://mpv.io/) | 字幕同步/硬解 | 2026-11-03 |
| Windows 照片（Windows 11 内置） | 图片浏览体验 | 2026-11-03（官方内置功能，无单独文档链接） |
| [foobar2000](https://www.foobar2000.org/) | 音频播放列表/元数据 | 2026-11-03 |
| [PotPlayer](https://potplayer.daum.net/) | 视频控制/字幕 | 2026-11-03 |
