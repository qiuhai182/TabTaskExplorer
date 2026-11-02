using System.Threading.Tasks;

namespace TabTaskExplorer.Kernel.Capture;

/// <summary>一次录屏请求。</summary>
public class ScreenRecordingOptions
{
    /// <summary>录像目标的显示区域；目标为活动窗口时由平台实现自行解析。</summary>
    public CaptureTarget Target { get; set; } = CaptureTarget.FullScreen;

    /// <summary>目标为区域时的物理像素区域。</summary>
    public CaptureArea Area { get; set; } = CaptureArea.Empty;

    /// <summary>输出文件完整路径（MP4）。</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>帧率（每秒采集的屏幕画面数量）。</summary>
    public int FramesPerSecond { get; set; } = 30;

    /// <summary>H.264 恒定质量参数：数值越小画质越高、文件越大（常用 18 到 28）。</summary>
    public int Quality { get; set; } = 23;

    /// <summary>是否把鼠标指针录制进画面。</summary>
    public bool IncludeCursor { get; set; } = true;

    /// <summary>麦克风设备名；留空表示不录制音频。Windows 使用 DirectShow 设备名，Linux 使用 PulseAudio 设备名，macOS 使用设备序号。</summary>
    public string MicrophoneDevice { get; set; } = string.Empty;

    /// <summary>最长录制秒数；0 表示不限制。</summary>
    public int DurationLimitSeconds { get; set; } = 0;

    /// <summary>FFmpeg 可执行文件完整路径；留空表示由平台实现自动查找。</summary>
    public string FfmpegPath { get; set; } = string.Empty;
}

/// <summary>录屏结果：开始时表示是否已成功拉起编码进程，停止时表示文件是否可用。</summary>
public class ScreenRecordingResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>输出文件路径。</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>失败原因（中文，可直接展示给用户）。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>构造成功结果。</summary>
    public static ScreenRecordingResult CreateSuccess(string outputPath)
    {
        ScreenRecordingResult result = new ScreenRecordingResult();
        result.Success = true;
        result.OutputPath = outputPath;
        return result;
    }

    /// <summary>构造失败结果。</summary>
    public static ScreenRecordingResult CreateFailure(string outputPath, string errorMessage)
    {
        ScreenRecordingResult result = new ScreenRecordingResult();
        result.Success = false;
        result.OutputPath = outputPath;
        result.ErrorMessage = errorMessage;
        return result;
    }
}

/// <summary>
/// 屏幕录制服务：内核统一契约，三平台都通过 FFmpeg 采集屏幕画面，失败时返回中文原因而不抛出异常。
/// </summary>
public interface IScreenRecordingService
{
    /// <summary>当前平台是否具备录屏能力（通常取决于 FFmpeg 是否可用）。</summary>
    bool IsSupported { get; }

    /// <summary>当前是否正在录制。</summary>
    bool IsRecording { get; }

    /// <summary>当前录制输出文件路径；未录制时为空文本。</summary>
    string CurrentOutputPath { get; }

    /// <summary>开始录制：编码进程成功启动才返回成功。</summary>
    Task<ScreenRecordingResult> StartAsync(ScreenRecordingOptions options);

    /// <summary>停止录制：等待编码进程正常收尾并确认文件可用。</summary>
    Task<ScreenRecordingResult> StopAsync();
}
