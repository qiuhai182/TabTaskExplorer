using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.MacOS;

/// <summary>
/// macOS 屏幕录制：使用 FFmpeg 的 avfoundation 采集屏幕，H.264 编码为 MP4；
/// 首次使用时系统会要求授予屏幕录制权限。
/// </summary>
public class MacScreenRecordingService : IScreenRecordingService, IDisposable
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
                "macOS 平台暂不支持活动窗口录制，请改用区域录制"));
        }
        if (options.Target == CaptureTarget.Region && !options.Area.IsValid)
        {
            return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath,
                "区域录制的宽高必须大于零"));
        }
        string ffmpegPath = FfmpegLocator.Find(options.FfmpegPath) ?? string.Empty;
        if (ffmpegPath.Length == 0)
        {
            return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath,
                "没有找到 FFmpeg。请先安装，或在设置中心填写 FFmpeg 完整路径"));
        }
        List<string> arguments = this.BuildArguments(options);
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

    /// <summary>拼接 avfoundation 录制参数。</summary>
    private List<string> BuildArguments(ScreenRecordingOptions options)
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
        string audioDevice = string.IsNullOrWhiteSpace(options.MicrophoneDevice)
            ? "none"
            : options.MicrophoneDevice.Trim();
        List<string> arguments = new List<string>();
        arguments.Add("-hide_banner");
        arguments.Add("-loglevel");
        arguments.Add("error");
        arguments.Add("-y");
        arguments.Add("-f");
        arguments.Add("avfoundation");
        arguments.Add("-framerate");
        arguments.Add(framesPerSecond.ToString());
        arguments.Add("-capture_cursor");
        arguments.Add(options.IncludeCursor ? "1" : "0");
        arguments.Add("-i");
        arguments.Add("1:" + audioDevice);
        if (options.Target == CaptureTarget.Region)
        {
            arguments.Add("-vf");
            arguments.Add("crop=" + options.Area.Width + ":" + options.Area.Height
                + ":" + options.Area.X + ":" + options.Area.Y);
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
