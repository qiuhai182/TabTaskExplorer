using System;

namespace TabTaskExplorer.Kernel.Media;

/// <summary>
/// 视频渲染面：由平台层实现，把原生窗口句柄暴露给播放引擎。
/// 音视频解码器不走 Avalonia 渲染管线，必须挂到真实原生窗口上（Windows HWND / Linux X11 Window / macOS NSView）。
/// </summary>
public interface IMediaSurface
{
    /// <summary>原生窗口句柄；尚未创建时为 <see cref="IntPtr.Zero"/>。</summary>
    IntPtr NativeHandle { get; }

    /// <summary>句柄是否已就绪（控件已挂载到视觉树且原生窗口创建完成）。</summary>
    bool IsReady { get; }

    /// <summary>句柄创建完成或控件尺寸变化时触发，引擎据此重新挂载渲染面。</summary>
    event EventHandler? HandleChanged;
}

/// <summary>媒体轨道（音轨 / 字幕轨）描述。</summary>
public class MediaTrack
{
    /// <summary>轨道编号（由后端分配，切换时原样回传）。</summary>
    public int Id { get; set; }

    /// <summary>界面展示名（"English" / "简体中文" / "Track 2"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>语言代码（可空）。</summary>
    public string Language { get; set; } = string.Empty;
}

/// <summary>
/// 播放引擎抽象：音频与视频共用同一套状态机，视频额外通过 <see cref="AttachSurface"/> 挂渲染面。
/// 实现方负责把后端回调线程切回调用方线程（UI 线程），事件订阅方可以安全地直接更新界面。
/// </summary>
public interface IMediaPlaybackEngine : IDisposable
{
    /// <summary>播放 / 暂停 / 停止状态变化。</summary>
    event EventHandler? StateChanged;

    /// <summary>播放进度变化（实现方负责节流，建议 200ms 一次）。</summary>
    event EventHandler<TimeSpan>? PositionChanged;

    /// <summary>总时长就绪或变化。</summary>
    event EventHandler<TimeSpan>? DurationChanged;

    /// <summary>打开或解码失败，参数为可直接展示给用户的原因。</summary>
    event EventHandler<string>? Failed;

    /// <summary>播放自然结束（用于自动播放下一条）。</summary>
    event EventHandler? EndReached;

    /// <summary>后端是否可用（原生库缺失时为 false，界面据此降级提示）。</summary>
    bool IsAvailable { get; }

    /// <summary>是否正在播放。</summary>
    bool IsPlaying { get; }

    /// <summary>当前进度。</summary>
    TimeSpan Position { get; }

    /// <summary>总时长；未知时为 <see cref="TimeSpan.Zero"/>。</summary>
    TimeSpan Duration { get; }

    /// <summary>音量（0-100）。</summary>
    int Volume { get; set; }

    /// <summary>是否静音。</summary>
    bool IsMuted { get; set; }

    /// <summary>播放倍速（0.25 - 4.0）。</summary>
    float Rate { get; set; }

    /// <summary>当前媒体的音轨清单（无音轨时为空）。</summary>
    System.Collections.Generic.IReadOnlyList<MediaTrack> AudioTracks { get; }

    /// <summary>当前媒体的字幕轨清单（含内嵌与外挂）。</summary>
    System.Collections.Generic.IReadOnlyList<MediaTrack> SubtitleTracks { get; }

    /// <summary>打开媒体文件并立即开始播放。</summary>
    void Open(string path);

    /// <summary>开始 / 继续播放。</summary>
    void Play();

    /// <summary>暂停。</summary>
    void Pause();

    /// <summary>在播放与暂停之间切换。</summary>
    void TogglePlayPause();

    /// <summary>跳转到指定进度。</summary>
    void Seek(TimeSpan position);

    /// <summary>挂载视频渲染面（音频播放无需调用）。</summary>
    void AttachSurface(IMediaSurface surface);

    /// <summary>卸载渲染面（关闭标签或切到音频时调用）。</summary>
    void DetachSurface();

    /// <summary>选择字幕轨（-1 表示关闭字幕）。</summary>
    void SelectSubtitle(int trackId);

    /// <summary>选择音轨。</summary>
    void SelectAudioTrack(int trackId);

    /// <summary>加载外挂字幕文件。</summary>
    void AddSubtitleFile(string path);

    /// <summary>截取当前画面到指定文件（仅视频，失败返回 false）。</summary>
    bool TakeSnapshot(string targetPath);
}
