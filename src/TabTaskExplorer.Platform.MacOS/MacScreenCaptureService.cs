using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.MacOS;

/// <summary>
/// macOS 屏幕截图：使用系统自带的 screencapture 命令，无需额外安装工具；
/// 支持全部屏幕（主显示器）、主屏与指定区域，活动窗口截图暂不提供。
/// </summary>
public class MacScreenCaptureService : IScreenCaptureService
{
    /// <inheritdoc />
    public ScreenCaptureCapabilities Capabilities
    {
        get
        {
            ScreenCaptureCapabilities capabilities = new ScreenCaptureCapabilities();
            bool available = FindScreencapture().Length > 0;
            capabilities.FullScreen = available;
            capabilities.PrimaryScreen = available;
            capabilities.Region = available;
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
                "macOS 平台暂不支持活动窗口截图，请改用区域截图"));
        }
        if (request.Target == CaptureTarget.Region && !request.Area.IsValid)
        {
            return Task.FromResult(ScreenCaptureResult.CreateFailure("区域截图的宽高必须大于零"));
        }
        string toolPath = FindScreencapture();
        if (toolPath.Length == 0)
        {
            return Task.FromResult(ScreenCaptureResult.CreateFailure("macOS 没有找到系统截图命令 screencapture"));
        }
        string tempFile = Path.Combine(Path.GetTempPath(),
            "TabTaskExplorer-截图-" + Guid.NewGuid().ToString("N") + ".png");
        List<string> arguments = BuildArguments(request, tempFile);
        return CaptureToolRunner.RunAsync(toolPath, arguments, tempFile, "macOS 屏幕截图失败");
    }

    /// <summary>拼接 screencapture 参数。</summary>
    private static List<string> BuildArguments(ScreenCaptureRequest request, string outputFile)
    {
        List<string> arguments = new List<string>();
        arguments.Add("-x");
        if (request.IncludeCursor)
        {
            arguments.Add("-C");
        }
        if (request.Target == CaptureTarget.Region)
        {
            arguments.Add("-R");
            arguments.Add(request.Area.X + "," + request.Area.Y + "," + request.Area.Width + "," + request.Area.Height);
        }
        arguments.Add(outputFile);
        return arguments;
    }

    /// <summary>查找系统 screencapture 命令的完整路径。</summary>
    private static string FindScreencapture()
    {
        string[] candidates = new string[]
        {
            "/usr/sbin/screencapture",
            "/usr/bin/screencapture",
        };
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return CaptureToolRunner.FindExecutableOnPath("screencapture");
    }
}
