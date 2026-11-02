using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 屏幕录制：使用 FFmpeg 的 gdigrab 采集桌面画面，H.264 编码为 MP4，
/// 可选 DirectShow 麦克风混音；FFmpeg 路径由内核定位器统一查找。
/// </summary>
public class WindowsScreenRecordingService : IScreenRecordingService, IDisposable
{
    private readonly FfmpegRecordingSession session = new FfmpegRecordingSession();

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public bool IsRecording
    {
        get { return this.session.IsRunning; }
    }

    /// <inheritdoc />
    public string CurrentOutputPath
    {
        get { return this.session.OutputPath; }
    }

    /// <inheritdoc />
    public Task<ScreenRecordingResult> StartAsync(ScreenRecordingOptions options)
    {
        CaptureArea area = this.ResolveArea(options);
        if (!area.IsValid)
        {
            return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath,
                "录制区域无效：目标窗口可能已最小化，或指定的宽高小于等于零"));
        }
        string ffmpegPath = FfmpegLocator.Find(options.FfmpegPath) ?? string.Empty;
        if (ffmpegPath.Length == 0)
        {
            return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath,
                "没有找到 FFmpeg。请安装后加入系统 PATH，或在设置中心的「截图与录屏」分页填写 FFmpeg 完整路径"));
        }
        List<string> arguments = this.BuildArguments(options, area);
        return this.session.StartAsync(ffmpegPath, arguments, options.OutputPath);
    }

    /// <inheritdoc />
    public Task<ScreenRecordingResult> StopAsync()
    {
        return this.session.StopAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.session.Dispose();
    }

    /// <summary>按录制目标解析实际物理像素区域。</summary>
    private CaptureArea ResolveArea(ScreenRecordingOptions options)
    {
        CaptureArea area;
        if (options.Target == CaptureTarget.Region)
        {
            area = options.Area;
        }
        else if (options.Target == CaptureTarget.PrimaryScreen)
        {
            area = WindowsScreenGeometry.GetPrimaryScreenArea();
        }
        else if (options.Target == CaptureTarget.ActiveWindow)
        {
            area = WindowsScreenGeometry.GetActiveWindowArea();
        }
        else
        {
            area = WindowsScreenGeometry.GetVirtualScreenArea();
        }
        return WindowsScreenGeometry.ClipToVirtualScreen(area);
    }

    /// <summary>拼接完整的 FFmpeg 输入与编码参数。</summary>
    private List<string> BuildArguments(ScreenRecordingOptions options, CaptureArea area)
    {
        int framesPerSecond = options.FramesPerSecond;
        if (framesPerSecond < 1 || framesPerSecond > 120)
        {
            framesPerSecond = 30;
        }
        int quality = options.Quality;
        if (quality < 0 || quality > 51)
        {
            quality = 23;
        }
        List<string> arguments = new List<string>();
        arguments.Add("-hide_banner");
        arguments.Add("-loglevel");
        arguments.Add("error");
        arguments.Add("-y");

        // 输入参数：桌面采集范围、帧率与鼠标指针
        arguments.Add("-f");
        arguments.Add("gdigrab");
        arguments.Add("-framerate");
        arguments.Add(framesPerSecond.ToString());
        arguments.Add("-draw_mouse");
        arguments.Add(options.IncludeCursor ? "1" : "0");
        arguments.Add("-offset_x");
        arguments.Add(area.X.ToString());
        arguments.Add("-offset_y");
        arguments.Add(area.Y.ToString());
        arguments.Add("-video_size");
        arguments.Add(area.Width.ToString() + "x" + area.Height.ToString());
        arguments.Add("-i");
        arguments.Add("desktop");

        // 可选麦克风输入
        if (!string.IsNullOrWhiteSpace(options.MicrophoneDevice))
        {
            arguments.Add("-f");
            arguments.Add("dshow");
            arguments.Add("-i");
            arguments.Add("audio=" + options.MicrophoneDevice.Trim());
        }

        arguments.Add("-c:v");
        arguments.Add("libx264");
        arguments.Add("-preset");
        arguments.Add("veryfast");
        arguments.Add("-crf");
        arguments.Add(quality.ToString());
        arguments.Add("-pix_fmt");
        arguments.Add("yuv420p");
        arguments.Add("-movflags");
        arguments.Add("+faststart");
        if (options.DurationLimitSeconds > 0)
        {
            arguments.Add("-t");
            arguments.Add(options.DurationLimitSeconds.ToString());
        }
        arguments.Add(options.OutputPath);
        return arguments;
    }
}
