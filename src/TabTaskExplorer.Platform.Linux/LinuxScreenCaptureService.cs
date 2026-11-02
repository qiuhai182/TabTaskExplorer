using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.Linux;

/// <summary>
/// Linux 屏幕截图：优先使用 FFmpeg 的 x11grab 单帧采集，缺失时回退到 ImageMagick 的 import 工具。
/// 活动窗口截图在 Linux 上暂不提供，能力清单会明确关闭该入口。
/// </summary>
public class LinuxScreenCaptureService : IScreenCaptureService
{
    /// <inheritdoc />
    public ScreenCaptureCapabilities Capabilities
    {
        get
        {
            ScreenCaptureCapabilities capabilities = new ScreenCaptureCapabilities();
            bool hasFfmpeg = FfmpegLocator.Find(null) != null;
            bool hasImport = CaptureToolRunner.FindExecutableOnPath("import").Length > 0;
            capabilities.FullScreen = hasFfmpeg || hasImport;
            capabilities.PrimaryScreen = capabilities.FullScreen;
            capabilities.Region = capabilities.FullScreen;
            capabilities.ActiveWindow = false;
            return capabilities;
        }
    }

    /// <inheritdoc />
    public Task<ScreenCaptureResult> CaptureAsync(ScreenCaptureRequest request)
    {
        if (request.Target == CaptureTarget.ActiveWindow)
        {
            return Task.FromResult(ScreenCaptureResult.CreateFailure(
                "Linux 平台暂不支持活动窗口截图，请改用区域截图"));
        }
        if (request.Target == CaptureTarget.Region && !request.Area.IsValid)
        {
            return Task.FromResult(ScreenCaptureResult.CreateFailure("区域截图的宽高必须大于零"));
        }

        string tempFile = Path.Combine(Path.GetTempPath(),
            "TabTaskExplorer-截图-" + Guid.NewGuid().ToString("N") + ".png");
        string ffmpegPath = FfmpegLocator.Find(request.ToolPath) ?? string.Empty;
        if (ffmpegPath.Length > 0)
        {
            return CaptureToolRunner.RunAsync(ffmpegPath, BuildFfmpegArguments(request, tempFile),
                tempFile, "Linux 屏幕截图失败");
        }
        string importPath = CaptureToolRunner.FindExecutableOnPath("import");
        if (importPath.Length > 0)
        {
            return CaptureToolRunner.RunAsync(importPath, BuildImportArguments(request, tempFile),
                tempFile, "Linux 屏幕截图失败");
        }
        return Task.FromResult(ScreenCaptureResult.CreateFailure(
            "Linux 平台没有找到可用的截图工具，请安装 FFmpeg 或 ImageMagick"));
    }

    /// <summary>拼接 FFmpeg x11grab 单帧采集参数。</summary>
    private static List<string> BuildFfmpegArguments(ScreenCaptureRequest request, string outputFile)
    {
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
        arguments.Add("1");
        arguments.Add("-draw_mouse");
        arguments.Add(request.IncludeCursor ? "1" : "0");
        if (request.Target == CaptureTarget.Region)
        {
            arguments.Add("-video_size");
            arguments.Add(request.Area.Width.ToString() + "x" + request.Area.Height.ToString());
            arguments.Add("-i");
            arguments.Add(display + "+" + request.Area.X + "," + request.Area.Y);
        }
        else
        {
            arguments.Add("-i");
            arguments.Add(display);
        }
        arguments.Add("-frames:v");
        arguments.Add("1");
        arguments.Add(outputFile);
        return arguments;
    }

    /// <summary>拼接 ImageMagick import 截图参数（不支持鼠标指针绘制）。</summary>
    private static List<string> BuildImportArguments(ScreenCaptureRequest request, string outputFile)
    {
        List<string> arguments = new List<string>();
        arguments.Add("-window");
        arguments.Add("root");
        if (request.Target == CaptureTarget.Region)
        {
            arguments.Add("-crop");
            arguments.Add(request.Area.Width + "x" + request.Area.Height + "+" + request.Area.X + "+" + request.Area.Y);
        }
        arguments.Add(outputFile);
        return arguments;
    }
}
