using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.Linux;

/// <summary>
/// Linux 屏幕录制：使用 FFmpeg 的 x11grab 采集 X11 桌面，H.264 编码为 MP4，可选 PulseAudio 麦克风混音。
/// 活动窗口录制暂不提供，界面会依据能力清单禁用该项。
/// </summary>
public class LinuxScreenRecordingService : IScreenRecordingService, IDisposable
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
        if (options.Target == CaptureTarget.ActiveWindow)
        {
            return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath,
                "Linux 平台暂不支持活动窗口录制，请改用区域录制"));
        }
        if (options.Target == CaptureTarget.Region && !options.Area.IsValid)
        {
            return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath,
                "区域录制的宽高必须大于零"));
        }
        CaptureArea evenArea;
        if (!CaptureGeometry.TryMakeEven(options.Area.IsValid ? options.Area : new CaptureArea(0, 0, 2, 2), out evenArea))
        {
            return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath,
                "录制区域过小：H.264 编码至少需要两个像素的宽度与高度"));
        }
        string ffmpegPath = FfmpegLocator.Find(options.FfmpegPath) ?? string.Empty;
        if (ffmpegPath.Length == 0)
        {
            return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath,
                "没有找到 FFmpeg。请先安装，或在设置中心填写 FFmpeg 完整路径"));
        }
        List<string> arguments = this.BuildArguments(options, evenArea);
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

    /// <summary>拼接 x11grab 录制参数。</summary>
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
        string display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrWhiteSpace(display))
        {
            display = ":0.0";
        }
        else if (!display.Contains(".", StringComparison.Ordinal))
        {
            display = display + ".0";
        }
        List<string> arguments = new List<string>();
        arguments.Add("-hide_banner");
        arguments.Add("-loglevel");
        arguments.Add("error");
        arguments.Add("-y");
        arguments.Add("-f");
        arguments.Add("x11grab");
        arguments.Add("-framerate");
        arguments.Add(framesPerSecond.ToString());
        arguments.Add("-draw_mouse");
        arguments.Add(options.IncludeCursor ? "1" : "0");
        if (options.Target == CaptureTarget.Region)
        {
            arguments.Add("-video_size");
            arguments.Add(area.Width.ToString() + "x" + area.Height.ToString());
            arguments.Add("-i");
            arguments.Add(display + "+" + area.X + "," + area.Y);
        }
        else
        {
            arguments.Add("-i");
            arguments.Add(display);
        }
        if (!string.IsNullOrWhiteSpace(options.MicrophoneDevice))
        {
            arguments.Add("-f");
            arguments.Add("pulse");
            arguments.Add("-i");
            arguments.Add(options.MicrophoneDevice.Trim());
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
