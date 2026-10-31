# 内置媒体查看器设计方案（图片 / 视频 / 音频 + 同目录播放列表）

状态：**已实现**（M1–M3 全部落地并真机验证：图片 / 音频 / 视频三类均可打开播放；M4 增强项未做）
版本：v2（2026-10-31，含落地结果）
关联：`README.md` 路线图「文件类型彩色图标与图片缩略图」、`docs/ide-mode-design.md`（打开方式规则的既有设计）

---

## 1. 目标与范围

在 TabTaskExplorer 内新增**内置媒体标签**：双击图片 / 视频 / 音频文件时新开一个标签页就地播放，标签内提供侧边栏展示同目录的媒体播放列表，可连续切换浏览。

### 1.1 本期目标

| 编号 | 目标 | 验收口径 |
| --- | --- | --- |
| G1 | 双击图片 → 新标签页看图 | 支持缩放、平移、旋转、上一张 / 下一张 |
| G2 | 双击视频 → 新标签页播放 | 播放 / 暂停 / 进度拖拽 / 音量 / 倍速 / 全屏 / 字幕 |
| G3 | 双击音频 → 新标签页播放 | 播放控制 + 元数据（标题 / 艺术家 / 专辑 / 封面） |
| G4 | 标签内侧边栏播放列表 | 列出同目录同类媒体，点击即切换，当前项高亮，可折叠 |
| G5 | 与既有体系一致 | 多标签 / 关闭 / 拖出成新窗口 / 主题切换 / 快捷键 / 设置项全部复用 |

### 1.2 明确不做（本期）

- 媒体库管理（索引、收藏、标签、评分）、转码、录制、投屏
- 均衡器、音频可视化频谱（可作为 M4 彩蛋）
- 播放器皮肤系统、插件系统
- 远程（SFTP / FTP）与压缩包内的媒体流式播放 —— 见 §8 M4

### 1.3 设计原则

1. **零阻塞**：媒体解码与目录扫描一律在后台线程，UI 线程只做渲染与状态更新。
2. **失败可见**：不支持的格式给出明确提示（含"用系统默认程序打开"按钮），不静默失败。
3. **可降级**：图片查看器不依赖任何原生库（纯 Avalonia/Skia），即使音视频后端初始化失败，看图功能仍可用。
4. **契合现有分层**：内核放抽象与格式策略，平台层放渲染面，应用层只做 UI 与状态机。

---

## 2. 交互规格

### 2.1 打开入口

| 入口 | 行为 |
| --- | --- |
| 列表双击媒体文件 | 按「媒体打开方式」设置决定：内置标签（默认）/ 系统默认程序 / 每次询问 |
| 右键菜单「用内置播放器打开」 | 强制开内置标签，忽略设置 |
| 右键菜单「打开方式 → 系统默认程序」 | 强制走 `Process.Start` |
| 标签内播放列表点击 / 方向键 | 在同一标签内切换，**不新开标签** |
| 拖文件到已有媒体标签的显示区 | 在当前标签内切换播放 |
| 命令行参数 / 关联打开（后续） | 复用 `OpenMediaTab` |

### 2.2 标签行为

- 标题：`文件名 - 图片` / `文件名 - 视频` / `文件名 - 音频`（长名中间省略）。
- 同一路径已存在媒体标签时：**激活已有标签**，不重复打开（可通过设置「始终新开」关闭该合并行为）。
- 关闭标签时同步 `MediaPlayer.Stop()` + `Dispose()`，避免后台仍在出声（复用 `CloseTab` 的现有清理分支）。
- 拖出成新窗口：媒体标签随 `DetachTab` / `AttachTab` 迁移，播放不中断。

### 2.3 布局

```text
┌──────────────────────────────────────────────────────────────────────┐
│  标签条： [ 海边日落.jpg  ×] [ 项目演示.mp4  ×]                        │
├───────────────────────────────────────────────┬──────────────────────┤
│                                               │  播放列表      ⟨ 折叠 │
│                                               │ ┌──────────────────┐ │
│                媒体显示区                      │ │[缩略] 01 海边.jpg│ │
│      （图片 / 视频画面 / 音频封面+波形）         │ │[缩略] 02 山.png  │ │
│                                               │ │▶[缩略] 03 日落.jpg│ │
│                                               │ │[缩略] 04 夜景.jpg│ │
│                                               │ └──────────────────┘ │
├───────────────────────────────────────────────┤  共 24 项 · 本目录    │
│ ▶  ━━━━━━●━━━━━━━  01:23 / 03:45  🔊 1.0×  ⛶  │  [同类 ▾]  [排序 ▾]  │
└───────────────────────────────────────────────┴──────────────────────┘
```

- 侧边栏默认在右，宽 260px（可拖拽 180–480，宽度持久化），可整体折叠（折叠后顶部留一条 28px 的"播放列表"竖条按钮）。
- 图片标签下控制条退化为：`‹ 3 / 24 ›  1920×1080  2.4 MB  100%  ⛶`。
- 主题：显示区底色用 `ContentBrush`，视频区纯黑 `#000000`，控制条 `ChromeBrush`，文字 `TextPrimaryBrush` / `TextMutedBrush`，强调色 `AccentGlyphBrush`（全部沿用 `App.axaml` 现有资源）。

### 2.4 图片查看器交互

| 操作 | 效果 |
| --- | --- |
| 滚轮 / Ctrl+滚轮 | 以光标为中心缩放（10% – 1600%，步进 1.1×） |
| 拖拽 | 平移（缩放 > 适应窗口时才可拖） |
| 双击画面 | 适应窗口 ⇄ 100% 切换 |
| `0` / `1` | 适应窗口 / 原始像素 |
| `R` / `Shift+R` | 顺时针 / 逆时针旋转 90° |
| `←` `→` / `PgUp` `PgDn` | 上一张 / 下一张（循环，可设置边界停住） |
| `F` | 全屏（隐藏标签条与控制条，`Esc` 退出） |
| 右键 | 菜单：复制图片 / 复制路径 / 设为壁纸（Windows）/ 用系统程序打开 / 在资源管理器中显示 |
| GIF / 动态 WebP | 自动逐帧播放（播放中显示 ⏸ 按钮可暂停） |

- EXIF Orientation 自动应用（避免手机竖拍照片横躺）；旋转按钮在此基础上叠加。
- 大图保护：解码前按窗口尺寸做 `DecodePixelWidth` 降采样（默认上限 4096px，可配置），避免 1 亿像素全景图撑爆内存；`>` 512MB 文件直接提示"文件过大，请用系统程序打开"。

### 2.5 视频播放器交互

| 操作 | 效果 |
| --- | --- |
| `Space` / 单击画面 | 播放 / 暂停 |
| `←` `→` | 后退 / 前进 5 秒（`Shift` 为 1 秒） |
| `J` `L` | 后退 / 前进 10 秒 |
| `↑` `↓` | 音量 ±5%（`M` 静音） |
| `[` `]` | 倍速 0.5× – 4×（0.25 步进） |
| `F` / 双击画面 | 全屏（独立无边框窗口，`Esc` 退出） |
| `PgUp` `PgDn` | 上一个 / 下一个媒体 |
| 拖进度条 | 精确定位；拖动时显示时间气泡 |
| 右键 | 菜单：字幕（加载 / 选择轨道 / 延迟）、音轨切换、画面比例、逐帧（`,` `.`）、截图、循环模式 |

- 进度条下方显示：`已缓冲` 半透明条（LibVLC 事件驱动）。
- 播放结束：按设置「自动播放下一项」（默认开）切到播放列表下一项。
- 记住上次音量与倍速（写入设置）。

### 2.6 音频播放器交互

- 主区显示：封面（内嵌 ID3/FLAC 图片，无封面时按文件类型生成渐变占位图）+ 标题 / 艺术家 / 专辑 / 时长 + 一条静态波形装饰条。
- 控制条与视频共用（无全屏按钮，替换为"打开所在目录"）。
- 支持 `.lrc` 同名歌词滚动（M4，可选）。

### 2.7 播放列表侧边栏

| 规则 | 说明 |
| --- | --- |
| 来源目录 | 当前媒体文件所在目录（不做递归） |
| 收录范围 | 默认「同类」（图片标签只列图片、视频标签只列视频、音频标签只列音频）；下拉可切「全部媒体」 |
| 过滤 | 跳过隐藏文件、跳过 `0` 字节文件、跳过回收站/系统目录 |
| 排序 | 自然排序（`img2 < img10`，大小写不敏感），可选「按修改时间」「按大小」 |
| 上限 | 单目录最多 5000 项，超出提示"仅显示前 5000 项" |
| 缩略图 | 图片取 48×48 缩略图（异步、带 LRU 缓存）；视频取首帧（M4）；音频取类型图标 |
| 交互 | 单击选中并切换；双击同单击；`Enter` 切换；`Ctrl+C` 复制路径；右键菜单同文件列表（打开 / 打开所在目录 / 属性） |
| 状态 | 当前项左侧竖条 + 加粗；已看过的项文字变灰（会话内记忆） |

### 2.8 全局快捷键（注册进现有快捷键系统 `HotkeySetting`）

| 快捷键 | 命令 | 作用域 |
| --- | --- | --- |
| `Ctrl+Shift+M` | 打开媒体播放器（文件对话框） | 全局 |
| `Ctrl+Shift+P` | 显示 / 隐藏播放列表侧边栏 | 媒体标签 |
| `Ctrl+Shift+O` | 用系统默认程序打开当前媒体 | 媒体标签 |

---

## 3. 技术选型（关键决策）

### 3.1 现状盘点（决定了可选方案）

| 事实 | 影响 |
| --- | --- |
| `Tabs` 是 `ObservableCollection<object>`，`MainWindow.axaml` 用 `DataTemplate DataType` 按类型映射面板 | 新增媒体标签只需加一个 VM + 一个面板 + 一条 DataTemplate |
| 项目**没有任何 WebView 依赖**（终端是 `TextBox` 行模式，README 里 xterm.js 仍是待接入项） | 「WebView + HTML5 播放器」方案需要新引入 WebView 运行时（WebView2 / WKWebView），成本被低估 |
| 目标框架 `net10.0`，Avalonia **12.1.4** | 第三方媒体控件多数只对 Avalonia 11 编译，兼容性需实测 |
| `WebServiceHost` 是手写 HTTP 服务，`/api/file` **不支持 Range 请求** | 网页镜像若要播视频，必须先补 Range 支持 |
| 内核已有 `IFileSystemProvider` / `IFileContentAccess` | 远程目录将来可复用为媒体源（M4） |

### 3.2 音视频后端三方案对比

| 方案 | 格式覆盖 | 跨平台 | 体积 | 主要风险 |
| --- | --- | --- | --- | --- |
| **A. LibVLCSharp + libvlc** | 极全（MP4/MKV/AVI/FLV/TS/WebM/MOV + H.264/H.265/VP9/AV1；MP3/AAC/FLAC/APE/WAV/OGG；内嵌与外挂字幕、多音轨） | Windows / Linux / macOS 全支持 | `VideoLAN.LibVLC.Windows` ≈ 40MB 压缩、解压 ~110MB | 原生窗口叠加（airspace）、Avalonia 12 兼容性 |
| B. WebView + HTML5 `<video>` | 受宿主内核限制：MP4(H.264)/WebM/MP3/AAC/FLAC 可用；**MKV、HEVC、APE 基本不可用** | 依赖各平台 WebView 运行时 | 需 WebView2 Runtime | 新依赖 + 需给 HTTP 服务补 Range + 本地媒体暴露给 Web 层 |
| C. Flyleaf / libmpv 封装 | 全（FFmpeg / mpv 内核） | Flyleaf 实质偏 Windows（D3D11 渲染） | 中 | 跨平台弱；许可证需逐项核对（见 §7） |

**结论：选 A（LibVLCSharp）作为音视频统一后端。**
理由：本地媒体文件的现实是 MKV / HEVC / APE / 多音轨 / 外挂字幕遍地都是，格式覆盖是播放器的生命线；且一个后端同时解决音频与视频，比"音频用 NAudio、视频用别的"少一套状态机。体积代价用"按需拷贝 + 裁剪 plugins"消化（§6.4）。

### 3.3 视频渲染面（A 方案的子决策）

LibVLC 的画面不走 Avalonia 渲染管线，必须挂到原生窗口上。两条路：

| 子方案 | 做法 | 优点 | 风险 |
| --- | --- | --- | --- |
| A1. 用 `LibVLCSharp.Avalonia` 现成控件 | NuGet 引用，`<vlc:VideoView MediaPlayer="{Binding ...}"/>` | 半天可跑通 | 该包按 Avalonia 11 编译；Avalonia 12 若 API 变更则不可用；且内部就是 `NativeControlHost`，airspace 问题照样存在 |
| A2. 自建渲染面抽象 | 内核定义 `IMediaSurface`，平台层各实现一个 `NativeControlHost` 子类，把 HWND / X11 Window / NSView 交给 libvlc | 完全契合本项目"平台差异收敛在平台工程"的既有哲学；可控可调 | 需写三平台各 ~100 行 |

**决策：先 spike A1（0.5 天）。** 通过则用 A1 快速交付；不通过立即转 A2，接口按 A2 设计（A1 也能被包在同一个 `IMediaSurface` 抽象后面，切换不返工）。
这与 `ide-mode-design.md` 里"先验证、不通过走兜底"的做法保持一致。

### 3.4 图片解码方案

| 方案 | 支持格式 | 许可证 | 结论 |
| --- | --- | --- | --- |
| Avalonia `Bitmap`（内部 Skia） | JPG / PNG / BMP / GIF / WEBP / ICO | MIT | **主选**，零新增依赖 |
| SkiaSharp（直接 `SKCodec`） | 同上 + 逐帧动画解码 | MIT | 用于 GIF / 动态 WebP 帧序列 |
| ImageSharp | 全（含 TGA、PBM） | **Six Labors Split License（商用有营收门槛，需付费）** | 不用，避免许可证纠纷 |
| Magick.NET | 极全（含 HEIC / AVIF / RAW） | Apache-2.0 | 可选后备（M4，用于 HEIC/AVIF） |
| Avalonia.Svg.Skia | SVG | MIT | 可选（M4） |

**决策**：M1 用 Avalonia `Bitmap` 覆盖 95% 场景，动画帧走 SkiaSharp；HEIC / AVIF / RAW 一律给出"暂不支持，用系统程序打开"的友好提示，M4 再接 Magick.NET。

### 3.5 元数据读取

| 用途 | 方案 | 许可证 |
| --- | --- | --- |
| EXIF 方向、相机型号、拍摄时间 | **MetadataExtractor** | Apache-2.0 |
| 音频 ID3v2 / FLAC / MP4 标签与封面 | **TagLib#**（`TagLibSharp`） | LGPL-2.1（动态引用，合规） |
| 视频时长 / 分辨率 | LibVLC 自身（`LengthChanged` / 轨道信息），不额外引库 | — |

两者都只是"读"，失败即静默降级（不显示元数据），绝不阻塞打开。

---

## 4. 架构设计

### 4.1 分层

```text
┌──────────────────────────── 应用层（App） ────────────────────────────┐
│ MediaTabViewModel   ← 标签状态机（Kind / 当前项 / 播放状态 / 错误）      │
│ MediaPlaylistViewModel ← 目录扫描、排序、切换、缩略图调度                │
│ MediaPanel.axaml    ← 显示区 + 控制条 + 侧边栏（纯视图，无业务）          │
│ ImageViewerControl / VideoSurfaceHost / AudioPlayerView / MediaControlBar│
└───────────────┬───────────────────────────────────────┬───────────────┘
                │ 只依赖抽象                              │ 组合根注入
┌───────────────▼──────────────┐        ┌───────────────▼───────────────┐
│ 内核 Kernel                  │        │ 平台层 Platform.*             │
│ MediaFormatCatalog（格式策略）│        │ WindowsMediaSurface（HWND）   │
│ MediaItem / MediaKind        │        │ LinuxMediaSurface（X11）      │
│ IMediaPlaybackEngine（抽象）  │        │ MacMediaSurface（NSView）     │
│ IMediaSurface（抽象）         │        │ VlcNativeLoader（libvlc 定位）│
│ MediaOpenPolicy（打开方式）   │        └───────────────────────────────┘
└──────────────────────────────┘
```

- 内核**不引用** LibVLCSharp（保持"零平台依赖、零重依赖"），只定义 `IMediaPlaybackEngine`；LibVLC 实现放在应用层的 `Services/Vlc/`（它是跨平台纯托管包装，放应用层即可，不必单开工程）。
- 若后续要把引擎下沉（例如给网页端服务复用），再抽 `TabTaskExplorer.Media` 工程，接口已就位。

### 4.2 内核抽象（新增文件）

`src/TabTaskExplorer.Kernel/Media/MediaKind.cs`

```csharp
/// <summary>媒体大类：图片 / 视频 / 音频。</summary>
public enum MediaKind
{
    Unknown = 0,
    Image = 1,
    Video = 2,
    Audio = 3,
}
```

`src/TabTaskExplorer.Kernel/Media/MediaFormatCatalog.cs`

```csharp
/// <summary>媒体格式白名单与分类：唯一的格式判定入口，桌面端 / 网页端 / 设置页共用。</summary>
public static class MediaFormatCatalog
{
    /// <summary>图片扩展名（含前导点，小写）。</summary>
    public static IReadOnlyList<string> ImageExtensions { get; }

    /// <summary>视频扩展名。</summary>
    public static IReadOnlyList<string> VideoExtensions { get; }

    /// <summary>音频扩展名。</summary>
    public static IReadOnlyList<string> AudioExtensions { get; }

    /// <summary>按扩展名判定媒体大类；非媒体返回 Unknown。</summary>
    public static MediaKind Classify(string path);

    /// <summary>是否可交给内置播放器（当前后端确实能解码的格式）。</summary>
    public static bool IsPlayable(string path);

    /// <summary>取用于界面展示的类型名（"JPEG 图片" / "MP4 视频"）。</summary>
    public static string DescribeType(string path);

    /// <summary>自然排序比较器（img2 &lt; img10），大小写不敏感。</summary>
    public static IComparer<string> NaturalComparer { get; }
}
```

`src/TabTaskExplorer.Kernel/Media/MediaItem.cs` — 播放列表条目（`FullPath` / `Name` / `Kind` / `SizeBytes` / `ModifiedAt` / `IsCurrent`）。

`src/TabTaskExplorer.Kernel/Media/IMediaPlaybackEngine.cs`

```csharp
/// <summary>播放引擎抽象：音频与视频共用一套状态机，视频额外挂渲染面。</summary>
public interface IMediaPlaybackEngine : IDisposable
{
    event EventHandler? StateChanged;              // 播放 / 暂停 / 停止
    event EventHandler<TimeSpan>? PositionChanged; // 当前进度（200ms 节流）
    event EventHandler<TimeSpan>? DurationChanged; // 总时长（媒体就绪后触发）
    event EventHandler<string>? Failed;            // 解码失败（带可读原因）

    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int Volume { get; set; }        // 0-100
    float Rate { get; set; }        // 0.25 - 4.0

    void Open(string path);
    void Play();
    void Pause();
    void TogglePlayPause();
    void Seek(TimeSpan position);
    void AttachSurface(IMediaSurface surface);   // 视频：挂原生窗口；音频：不调用
    void DetachSurface();
    IReadOnlyList<MediaTrack> SubtitleTracks { get; }
    IReadOnlyList<MediaTrack> AudioTracks { get; }
    void SelectSubtitle(int trackId);
    void SelectAudioTrack(int trackId);
    void AddSubtitleFile(string path);
}
```

`src/TabTaskExplorer.Kernel/Media/IMediaSurface.cs`

```csharp
/// <summary>视频渲染面：由平台层实现，把原生窗口句柄暴露给播放引擎。</summary>
public interface IMediaSurface
{
    /// <summary>原生窗口句柄（Windows: HWND，Linux: X11 Window，macOS: NSView 指针）。</summary>
    IntPtr NativeHandle { get; }

    /// <summary>句柄是否已就绪（控件挂载完成）。</summary>
    bool IsReady { get; }

    /// <summary>句柄就绪或尺寸变化时触发，引擎据此重新挂载。</summary>
    event EventHandler? HandleChanged;
}
```

`src/TabTaskExplorer.Kernel/Media/MediaOpenPolicy.cs` — 打开方式决策（内置 / 系统 / 询问），复用 `FileOpenPolicy` 的规则匹配思路：

```csharp
/// <summary>媒体打开方式：Builtin（内置标签）/ System（系统默认程序）/ Ask（每次询问）。</summary>
public static class MediaOpenPolicy
{
    public static string Resolve(AppSettingsLike settings, MediaKind kind); // 默认优先级 + 按类型覆盖
}
```

### 4.3 应用层（新增文件）

| 文件 | 职责 | 预估行数 |
| --- | --- | --- |
| `ViewModels/MediaTabViewModel.cs` | 标签根 VM：`Kind`、`Title`、当前项、播放状态、错误提示、`ITabView` 实现 | ~400 |
| `ViewModels/MediaPlaylistViewModel.cs` | 目录扫描（后台）、自然排序、同类/全部切换、当前项维护、切换请求事件 | ~300 |
| `ViewModels/MediaPlaylistItemViewModel.cs` | 单条目（名称 / 类型 / 大小 / 时长 / 缩略图 / 是否当前 / 是否已看） | ~80 |
| `Views/MediaPanel.axaml(.cs)` | 容器：显示区 + 控制条 + 侧边栏；拖放、快捷键、全屏切换 | ~250 |
| `Views/ImageViewerControl.axaml(.cs)` | 图片视图：缩放/平移/旋转/动画帧定时器 | ~350 |
| `Views/VideoSurfaceHost.axaml(.cs)` | `IMediaSurface` 的宿主控件（内含平台 `NativeControlHost`） | ~120 |
| `Views/AudioPlayerView.axaml(.cs)` | 音频视图：封面 + 元数据 + 装饰波形 | ~150 |
| `Views/MediaControlBar.axaml(.cs)` | 共用控制条（播放/进度/音量/倍速/全屏/字幕菜单） | ~250 |
| `Services/Media/VlcPlaybackEngine.cs` | `IMediaPlaybackEngine` 的 LibVLC 实现（单例 `LibVLC`，每标签一个 `MediaPlayer`） | ~450 |
| `Services/Media/MediaMetadataService.cs` | TagLib# / MetadataExtractor 读取，带 LRU 缓存 | ~150 |
| `Services/Media/ThumbnailService.cs` | 图片缩略图（异步 + 内存 LRU + 磁盘缓存，M4 起对文件列表也生效） | ~200 |

### 4.4 平台层（新增文件）

| 平台 | 文件 | 实现要点 |
| --- | --- | --- |
| Windows | `Platform.Windows/Media/WindowsMediaSurface.cs` | `NativeControlHost` 子类，重写 `CreateNativeControlCore` 返回承载子 HWND 的 `IWindowImpl`；取 `Handle` 交给 libvlc |
| Linux | `Platform.Linux/Media/LinuxMediaSurface.cs` | 同上，暴露 X11 Window（XID）；Wayland 会话下降级为"视频窗口独立弹出"并提示 |
| macOS | `Platform.MacOS/Media/MacMediaSurface.cs` | 同上，暴露 `NSView*` |

> 只有走 A2 路线时才需要这三个文件；A1 通过则用 `LibVLCSharp.Avalonia` 的 `VideoView`，并在其外层包一层实现 `IMediaSurface` 的适配器。

### 4.5 数据流

```text
双击 row ──► FileTabViewModel.OpenEntry
                │ MediaFormatCatalog.Classify + MediaOpenPolicy.Resolve
                ▼
        MediaOpenRequested 事件 ──► MainWindowViewModel.OpenMediaTab(path, kind)
                │ 同路径已开 → 激活；否则新建
                ▼
        MediaTabViewModel.Load(path)
                ├─► MediaPlaylistViewModel.LoadDirectoryAsync(dir, kind)   // 后台扫描
                ├─► VlcPlaybackEngine.Open(path) → Play()
                └─► MediaMetadataService.ReadAsync(path)                  // 元数据
                ▼
        引擎事件（Playing / TimeChanged / EndReached / Failed）
                │ Dispatcher.UIThread.Post 切回 UI 线程
                ▼
        控制条 / 进度 / 播放列表状态刷新
```

### 4.6 接入现有标签体系（改动清单）

| 文件 | 改动 |
| --- | --- |
| `Services/TabContract.cs` | 新增 `TabKinds.Media = "media"`；**不加入 `All`**（网页镜像暂不渲染，走 `Unknown` 分支）；新增 `FillMedia` 快照（当前文件名、类型、播放状态、进度、播放列表条数） |
| `ViewModels/MainWindowViewModel.cs` | 新增 `OpenMediaTab(string path, MediaKind kind)`；`CreateFileTab` 里订阅 `MediaOpenRequested`；`CloseTab` 增加 `MediaTabViewModel` 释放分支；`DescribeTabTitle` 已能反射 `Title`，无需改 |
| `ViewModels/FileTabViewModel.cs` | `OpenEntry` 在"系统程序打开"之前插入媒体分支（见下）；新增 `event EventHandler<MediaOpenRequest>` |
| `Views/MainWindow.axaml` | `ContentControl.DataTemplates` 增加 `<DataTemplate DataType="vm:MediaTabViewModel"><views:MediaPanel/></DataTemplate>` |
| `App.axaml.cs` / `CompositionRoot.cs` | 注册 `IMediaPlaybackEngine`（单例工厂）、`MediaMetadataService`、`ThumbnailService`；`Core.Initialize()` 只调一次，失败时降级并记日志 |
| `Services/AppSettingsStore.cs` | 新增媒体设置项（§5） |
| `Views/SettingsWindow.axaml(.cs)` | 新增「媒体」页：打开方式、播放列表默认范围、自动下一项、大图解码上限、是否记住音量、字幕字体与大小 |
| `TabTaskExplorer.App.csproj` | `LibVLCSharp`、`VideoLAN.LibVLC.Windows`（条件 `RuntimeIdentifier`）、`SkiaSharp`、`TagLibSharp`、`MetadataExtractor` |
| `build.bat` | 提示媒体原生库体积；`full` 模式自动拷贝 `libvlc/` 目录 |

`FileTabViewModel.OpenEntry` 的关键改动（插在压缩包判断之后、系统程序打开之前）：

```csharp
// 媒体文件：按设置交给内置媒体标签（图片 / 视频 / 音频），否则继续走系统默认程序
if (MediaFormatCatalog.IsPlayable(row.FullPath))
{
    MediaKind kind = MediaFormatCatalog.Classify(row.FullPath);
    if (MediaOpenPolicy.ShouldUseBuiltin(this.Settings, kind))
    {
        this.RaiseMediaOpenRequested(new MediaOpenRequest(row.FullPath, kind));
        return;
    }
}
```

### 4.7 边界（重要）

- **IDE 模式**下媒体文件**不走内置**：IDE 有独立的标签容器（`IdeShellViewModel`），把媒体标签塞进 `MainWindow.Tabs` 会导致"看不见的标签"。本期 IDE 模式双击媒体仍交系统程序，M4 再讨论是否在 IDE 里开媒体标签。
- **压缩包标签**内媒体：继续解压到临时文件后交系统程序（压缩包虚拟文件系统不支持随机 Seek，视频无法拖动进度）。
- **远程（SFTP/FTP）媒体**：M4 用 `IFileContentAccess` + 本地缓存文件实现"下载后播放"，不做边下边播。

---

## 5. 设置项（`AppSettings` 新增）

```csharp
/// <summary>媒体打开方式：Builtin（默认，内置标签）/ System（系统默认程序）/ Ask（每次询问）。</summary>
public string MediaOpenMode { get; set; } = MediaOpenModeValues.Builtin;

/// <summary>按媒体大类覆盖打开方式（图片/视频/音频可各自设置），空表示跟随默认。</summary>
public List<MediaOpenRule> MediaOpenRules { get; set; } = new List<MediaOpenRule>();

/// <summary>是否显示播放列表侧边栏。</summary>
public bool MediaShowPlaylist { get; set; } = true;

/// <summary>播放列表宽度（像素，默认 260）。</summary>
public double MediaPlaylistWidth { get; set; } = 260;

/// <summary>播放列表范围：SameKind（同类，默认）/ AllMedia（全部媒体）。</summary>
public string MediaPlaylistScope { get; set; } = "SameKind";

/// <summary>播放结束后是否自动播放下一项。</summary>
public bool MediaAutoPlayNext { get; set; } = true;

/// <summary>图片解码最大边长（像素，默认 4096，0 表示不限制）。</summary>
public int MediaImageMaxDecodeSize { get; set; } = 4096;

/// <summary>上次音量（0-100）。</summary>
public int MediaVolume { get; set; } = 80;

/// <summary>上次播放倍速（0.25-4.0）。</summary>
public double MediaRate { get; set; } = 1.0;

/// <summary>字幕字体与字号（LibVLC 渲染）。</summary>
public string MediaSubtitleFontFamily { get; set; } = "Microsoft YaHei, Noto Sans CJK SC, sans-serif";
public int MediaSubtitleFontSize { get; set; } = 22;
```

---

## 6. 关键实现要点与坑

### 6.1 空域问题（airspace）—— 最大的 UI 陷阱

`NativeControlHost` 里的原生窗口**永远盖在 Avalonia 绘制之上**，任何浮在视频区上方的 Avalonia 元素（下拉菜单、ToolTip、进度条气泡、对话框）都会被视频画面遮住。对策：

1. 控制条**不叠加**在画面上，放在视频区**下方**独立一行（这也是 Windows 照片/电影和电视的默认布局）。
2. 全屏时用**独立无边框 `Window`**（`WindowState=FullScreen`），控制条在该窗口底部单独占一行，鼠标静止 2 秒自动隐藏（隐藏时用 `Opacity=0` + 禁用命中测试，而不是 `IsVisible=false`，避免布局跳动）。
3. 右键菜单、字幕菜单、设置对话框一律用 `Flyout` / 独立 `Window`（Avalonia 的 `Flyout` 走 `OverlayLayer`，同属 Avalonia 绘制，仍会被遮挡）→ **媒体标签内的所有弹出层必须用独立 Window 或 `Popup`（`Popup` 在 Avalonia 12 是独立顶层窗口，可用）**。
4. 显示区四角/中央的提示文字（"正在加载…"）必须在挂载渲染面**之前**显示，挂载后立即隐藏。

### 6.2 LibVLC 生命周期

- `Core.Initialize()` 全进程**只调一次**，放在 `Program.Main` 早期；失败（找不到 libvlc）则置 `IsAvailable=false`，媒体标签显示"音视频引擎不可用，点击用系统程序打开"，**图片功能不受影响**。
- 一个 `LibVLC` 实例全局共享，`MediaPlayer` 每标签一个，`Media` 每次 `Open` 新建并在切歌后 `Dispose`。
- 所有 LibVLC 事件回调都在 **VLC 线程**，必须 `Dispatcher.UIThread.Post` 回 UI；事件里**不要**直接读 `Position`（有竞态），改为在 UI 线程用 200ms 定时器轮询 `Position`（更稳，且天然节流）。
- 标签关闭顺序：`Stop()` → `DetachSurface()` → `MediaPlayer.Dispose()`，在 UI 线程执行，避免原生窗口已销毁还在回调。

### 6.3 图片性能

- 解码在 `Task.Run` 中进行，先出一张 1/8 缩略图立即上屏（`DecodePixelWidth` 小值），再异步替换为原尺寸（"渐进加载"），大图不再白屏。
- 切换图片时 `Bitmap` 必须显式 `Dispose`，否则 100 张 4K 图会吃掉 3GB。
- 缩放用 `RenderTransform`（`ScaleTransform` + `TranslateTransform`）而非重新解码，保证 60fps。

### 6.4 体积与发布

- `VideoLAN.LibVLC.Windows` 解压约 110MB，`build.bat` 的框架依赖单文件会从 ~20MB 涨到 ~130MB。
- 裁剪：只保留 `libvlc.dll` / `libvlccore.dll` / `plugins/{codec,demux,access,video_output,audio_output,text_renderer}`，砍掉 `gui`、`lua`、`visualization` 等，可压到 ~60MB。裁剪脚本进 `build.bat`，并在 README 说明。
- Linux/macOS：依赖系统 `libvlc`（`apt install libvlc-dev` / `brew install --cask vlc`），不随包分发，避免 LGPL 静态链接争议。

### 6.5 日志埋点（沿用 `UiLogger`）

统一前缀「媒体」：`新建媒体标签：类型=视频 路径=… 播放列表条数=24`、`切换媒体：从=a.mp4 到=b.mp4 结果=成功`、`解码失败：路径=… 原因=…`。便于用户反馈问题时定位。

### 6.6 调试镜像（`WebServiceHost` / `WebPageHtml`）

- 本期媒体标签在网页端走 `TabKinds.Unknown`，镜像页显示"桌面专用标签（媒体）"占位卡片，点击可切回桌面。
- M4 增强：`/api/file` 补 `Range` 支持（206 分段响应），镜像页用 `<video>` 只读预览 —— 这一步同时为"网页端播放"打基础，但**不是**本期交付项。

---

## 7. 开源参考与许可证合规

本项目是 **MIT**，合并吸收代码时必须守住许可证红线。**GPL 代码一律只借鉴交互与算法思路，不复制源码、不链接**。

| 项目 | 许可证 | 能否合并 | 建议吸收的内容 |
| --- | --- | --- | --- |
| [LibVLCSharp](https://github.com/videolan/libvlcsharp) | LGPL-2.1 | ✅ NuGet 动态引用 | 播放引擎（`MediaPlayer` 事件模型、字幕/音轨 API 用法） |
| libvlc (VLC) | LGPL-2.1+ | ✅ 动态库分发 | 解码后端 |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | MIT | ✅ | GIF/WebP 逐帧解码 |
| [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) | Apache-2.0 | ✅ | EXIF 方向、相机信息 |
| [TagLib#](https://github.com/mono/taglib-sharp) | LGPL-2.1 | ✅ 动态引用 | ID3 / FLAC / MP4 标签与封面 |
| [ImageGlass](https://github.com/d2phap/ImageGlass) | GPL-3.0 | ❌ 仅借鉴 | 看图器交互：滚轮缩放锚点算法、拖拽平移手感、EXIF 面板信息密度、GIF 逐帧控制条 |
| [Files](https://github.com/files-community/Files) | MIT | ✅ 可参考 | 预览面板布局、播放列表侧边栏的视觉层级 |
| [qView](https://github.com/jurplel/qView) / nomacs | GPL | ❌ 仅借鉴 | 极简看图交互（单击切换、隐藏全部 chrome） |
| [Flyleaf](https://github.com/SuRGeoNix/Flyleaf) | GPL-3.0（商用需商业授权，需核对） | ❌ | FFmpeg 播放器的 seek 精度处理、硬解回退策略 |
| mpv / libmpv | GPL | ❌ | 不采用 |
| [ImageSharp](https://github.com/SixLabors/ImageSharp) | Six Labors Split（商用收费） | ⚠️ 不用 | — |
| NAudio / ManagedBass | MIT / 商业授权 | ⚠️ 不用 | 音频后端已由 LibVLC 统一覆盖 |

> 「拉取开源项目合并吸收」的正确姿势：**能用 NuGet 包解决的一律用包**（自动满足许可证义务），**只把 MIT/Apache 项目的代码片段吸收进来**，GPL 项目只写"交互对照表"进设计文档，代码自己实现。

---

## 8. 里程碑

| 阶段 | 内容 | 交付物 | 预估 |
| --- | --- | --- | --- |
| **M0 探针** | ① `LibVLCSharp.Avalonia` 与 Avalonia 12.1.4 兼容性 spike；② 单窗口内嵌视频 HWND 最小验证（能播能拖进度） | 结论写回本文档「落地结果」 | 0.5–1 天 |
| **M1 图片查看器** | 内核 `MediaFormatCatalog` / `MediaItem`；`MediaTabViewModel` + `MediaPanel` 骨架；`ImageViewerControl`（缩放/平移/旋转/上一张下一张/EXIF）；`OpenEntry` 媒体分支；标签接入 + 设置页「媒体」；播放列表侧边栏（仅图片，无缩略图） | 双击 jpg/png/gif 开标签看图，侧边栏可切图 | 2–3 天 |
| **M2 音频播放器** | `VlcPlaybackEngine`（无渲染面）；`MediaControlBar`；`AudioPlayerView`（封面 + 元数据）；播放结束自动下一项；音量/倍速持久化 | 双击 mp3/flac/m4a 播放，侧边栏连续切歌 | 2–3 天 |
| **M3 视频播放器** | 渲染面（A1 或 A2）；视频布局；全屏独立窗口；字幕加载与轨道切换；音轨切换；截图 | 双击 mp4/mkv 播放，可全屏、可挂字幕 | 3–4 天 |
| **M4 增强** | 播放列表缩略图（图片 + 视频首帧）；GIF 逐帧控制条；`/api/file` Range + 镜像页只读预览；远程媒体（下载后播）；HEIC/AVIF（Magick.NET）；文件列表缩略图视图联动 | 体验补齐 | 3–5 天 |

每个里程碑结束跑一次 `dotnet test`，并为 `MediaFormatCatalog` / `MediaOpenPolicy` / 自然排序比较器补单元测试（纯逻辑，无需 UI）。

---

## 9. 风险与对策

| 风险 | 影响 | 概率 | 对策 |
| --- | --- | --- | --- |
| `LibVLCSharp.Avalonia` 与 Avalonia 12 不兼容 | 视频无法显示 | 中 | M0 探针先验；不通过走 A2 自建渲染面（接口已按此设计） |
| 原生窗口遮挡 Avalonia 弹出层 | 菜单/对话框看不见 | 高（必现） | §6.1 四条对策，布局上不叠加控件 |
| 发布体积从 20MB 涨到 130MB | 用户体验 | 高（必现） | plugins 裁剪 + README 说明；保留"无媒体后端"构建开关 |
| libvlc 在部分 Linux 发行版缺失 | 音视频不可用 | 中 | 启动探测 + 降级提示 + 引导安装命令 |
| 大图/长视频内存暴涨 | 崩溃 | 中 | 解码尺寸上限、`Bitmap` 及时释放、播放列表缩略图 LRU |
| 远程/压缩包媒体 Seek 失败 | 拖动进度无效 | 高 | 本期明确不支持并提示（§4.7） |
| 许可证污染（误引 GPL 代码） | 法律风险 | 低 | §7 清单 + 代码评审检查项 |
| 与「文件列表缩略图」路线图重复建设 | 返工 | 中 | 缩略图服务一开始就设计成可被文件列表复用（`ThumbnailService` 独立于媒体标签） |

---

## 10. 待确认决策（需要拍板）

1. **打开方式默认值**：双击媒体默认走内置标签，还是保持现状（系统程序）由用户在设置里主动开启？
   *建议：默认内置（否则功能等于藏起来），设置里可一键切回"系统默认程序"。*
2. **同文件重复打开**：同一文件已开标签时，是激活已有标签，还是每次双击都新开？
   *建议：激活已有（符合浏览器与资源管理器习惯），设置里可改为"始终新开"。*
3. **后端体积**：接受发布包涨到 ~60–130MB 换取"MKV/HEVC/APE 全格式"，还是先只做图片 + 音频（体积小），视频继续交系统程序？
   *建议：接受，格式覆盖是播放器的核心价值。*
4. **播放列表范围**：默认只列同类（图片标签只列图片），还是列"同目录所有媒体"混合？
   *建议：默认同类，下拉可切全部。*

---

## 11. 落地结果（2026-10-31 实测）

### 11.1 M0 探针结论：走 A2（自建渲染面），不用 `LibVLCSharp.Avalonia`

没有引入 `LibVLCSharp.Avalonia` 包，直接自建 `MediaSurfaceHost`（`NativeControlHost` 子类 + Win32 `CreateWindowExW("static")` 子窗口），
在 **Avalonia 12.1.4** 下工作正常。理由：现成包按 Avalonia 11 编译，且其内部同样是 `NativeControlHost`，
自建反而少一层兼容风险，也符合项目「平台差异收敛」的既有风格。

### 11.2 两个必须记住的坑（都已修复）

| 坑 | 现象 | 修复 |
| --- | --- | --- |
| `NativeControlHost` 一挂载就创建原生窗口，与 `IsVisible` 无关 | 图片标签也去建原生子窗口，抛 `InvalidOperationException: Unable to create child window for native control host` | 渲染面改为**按需创建**：XAML 只放一个空 `Panel`，视频标签时才 `new MediaSurfaceHost()` 加进去 |
| 缺少应用清单的 `supportedOS` 声明 | 同一个异常，即使只在视频时创建也会抛 | 新增 `src/TabTaskExplorer.App/app.manifest`（声明 Win7–Win11 + PerMonitorV2），csproj 加 `<ApplicationManifest>` |

### 11.3 真机验证记录

| 场景 | 结果 |
| --- | --- |
| 双击 PNG（483B） | 标签标题 `sample01.png - 图片`；播放列表扫描到同目录 2 个 PNG，当前项高亮正确 |
| 双击 WAV（3 秒 440Hz） | 引擎可用=True；`解码就绪：时长=00:03 音轨数=2`；无异常 |
| 双击 MP4（10 秒 H.264 1MB） | 引擎可用=True；`解码就绪：时长=00:10`；视频渲染面创建成功，日志无异常 |
| 目录扫描 | 自然排序、同类过滤、隐藏/零字节文件跳过均按设计工作 |

### 11.4 实现期发现并修复的两个逻辑缺陷

1. **路径分隔符不一致**：传入路径可能带正斜杠（`C:/Users/...`），而 `Directory.EnumerateFiles` 返回反斜杠，
   `OrdinalIgnoreCase` 比较不相等 → 播放列表当前项永远不高亮。修复：`MediaTabViewModel.NormalizePath`（`Path.GetFullPath`）统一规整，
   构造、切换、复用判断三处都过一遍。
2. **播放列表选中反向触发切换**：`ListBox.SelectedItem` 由程序同步时会触发 `SelectionChanged`，导致刚打开的图片被"自动切走"。
   修复：`OnPlaylistSelectionChanged` 增加守卫——选中项路径与当前文件相同则直接返回。

### 11.5 体积实测

`VideoLAN.LibVLC.Windows` 默认把 x64 / x86 / arm64 三套原生库全部拷出：**303MB**。
csproj 加 `TrimLibVlcArchitectures` target 后只保留目标架构：**108MB**（`libvlc/win-x64`）。
后续若需进一步压缩，可裁剪 `plugins/` 中不用的 `gui`、`lua`、`visualization` 模块（预计再省 30–40MB）。

### 11.6 与方案的偏差

| 方案条目 | 实际 | 原因 |
| --- | --- | --- |
| 元数据用 MetadataExtractor + TagLib# | **未引入** | v1 只显示分辨率/大小/类型，音频标签信息未做；两者留待 M4，避免过早增加依赖 |
| 图片动画帧走 SkiaSharp | **未引入** | v1 用 Avalonia `Bitmap` 只显示首帧；动图逐帧播放留待 M4 |
| 播放列表缩略图 | **未做** | 留待 M4，与「文件列表缩略图」一起做（`ThumbnailService` 未创建） |
| 字幕 / 音轨菜单 UI | 引擎已支持（`AddSubtitleFile` / `SelectAudioTrack` / `TakeSnapshot`），**界面入口未做** | 留待 M4 补右键菜单 |
| 网页镜像播放 | 走 `TabKinds.Media` + `Unknown` 回退，镜像页显示占位 | 按方案 §6.6，`/api/file` 的 Range 支持留待 M4 |

### 11.7 测试

新增 `MediaFormatCatalogTests`（分类 / 可播放 / 动图识别 / 类型文本 / 自然排序）与 `MediaOpenPolicyTests`（规则优先级 / 规整 / 下标往返），
全仓 `dotnet test`：**190 通过，0 失败**。

### 11.8 新增 / 改动文件清单

新增：
- `src/TabTaskExplorer.Kernel/Media/`：`MediaKind.cs`、`MediaFormatCatalog.cs`（含 `NaturalStringComparer`）、`MediaItem.cs`、`MediaOpenPolicy.cs`、`IMediaPlaybackEngine.cs`（含 `IMediaSurface` / `MediaTrack`）
- `src/TabTaskExplorer.App/ViewModels/MediaTabViewModel.cs`、`MediaPlaylistViewModel.cs`（含条目视图模型）
- `src/TabTaskExplorer.App/Views/MediaPanel.axaml(.cs)`、`ImageViewerControl.axaml(.cs)`、`MediaSurfaceHost.cs`
- `src/TabTaskExplorer.App/Services/Media/VlcPlaybackEngine.cs`、`NullMediaPlaybackEngine.cs`
- `src/TabTaskExplorer.App/app.manifest`
- `src/TabTaskExplorer.Tests/MediaFormatCatalogTests.cs`、`MediaOpenPolicyTests.cs`

改动：
- `FileTabViewModel.OpenEntry`：系统程序打开之前插入媒体分支 + `MediaOpenRequested` 事件
- `MainWindowViewModel`：`OpenMediaAsync` / `OpenMediaTab` / `CreateMediaEngine` / `CloseTab` 释放
- `MainWindow.axaml`：`MediaTabViewModel → MediaPanel` 数据模板
- `Services/TabContract.cs`：`TabKinds.Media` + `FillMedia` 快照
- `Services/AppSettingsStore.cs`：媒体设置项（打开方式、播放列表范围、自动下一项、复用标签、解码上限、音量、倍速）
- `Views/SettingsWindow.axaml(.cs)`：新增「媒体」分页
- `Views/MessageDialog.axaml.cs`：新增通用 `ConfirmAsync`（"每次询问"用）
- `CompositionRoot.cs`：注册 `IMediaPlaybackEngine → VlcPlaybackEngine`
- `TabTaskExplorer.App.csproj`：LibVLCSharp + libvlc 原生包（仅 Windows）+ manifest + 架构裁剪
- `README.md`：功能列表与路线图

### 11.9 后续（M4 待办）

1. 播放列表缩略图（图片缩略图 + 视频首帧），并与文件列表缩略图视图共用服务
2. 字幕 / 音轨 / 截图 / 逐帧的右键菜单入口
3. 音频元数据与封面（TagLib#）、EXIF 方向（MetadataExtractor）
4. GIF / 动态 WebP 逐帧播放（SkiaSharp）
5. `/api/file` 补 Range 支持，镜像页用 `<video>` 只读预览
6. 远程（SFTP）媒体"下载后播放"
7. `plugins/` 裁剪脚本（build.bat）
