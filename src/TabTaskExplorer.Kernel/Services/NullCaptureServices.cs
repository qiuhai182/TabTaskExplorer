using System;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 屏幕截图空实现：用于尚未提供平台适配的操作系统，能力清单全部为 false，调用时返回明确的中文失败原因。
/// </summary>
public class NullScreenCaptureService : IScreenCaptureService
{
    /// <inheritdoc />
    public ScreenCaptureCapabilities Capabilities { get; } = new ScreenCaptureCapabilities();

    /// <inheritdoc />
    public Task<ScreenCaptureResult> CaptureAsync(ScreenCaptureRequest request)
    {
        return Task.FromResult(ScreenCaptureResult.CreateFailure("当前操作系统暂不支持屏幕截图"));
    }
}

/// <summary>屏幕录制空实现：不具备录制能力，开始与停止均返回明确的中文失败原因。</summary>
public class NullScreenRecordingService : IScreenRecordingService
{
    /// <inheritdoc />
    public bool IsSupported
    {
        get { return false; }
    }

    /// <inheritdoc />
    public bool IsRecording
    {
        get { return false; }
    }

    /// <inheritdoc />
    public string CurrentOutputPath
    {
        get { return string.Empty; }
    }

    /// <inheritdoc />
    public Task<ScreenRecordingResult> StartAsync(ScreenRecordingOptions options)
    {
        return Task.FromResult(ScreenRecordingResult.CreateFailure(options.OutputPath, "当前操作系统暂不支持屏幕录制"));
    }

    /// <inheritdoc />
    public Task<ScreenRecordingResult> StopAsync()
    {
        return Task.FromResult(ScreenRecordingResult.CreateFailure(string.Empty, "当前没有正在进行的录制"));
    }
}

/// <summary>全局快捷键空实现：用于不提供全局热键的操作系统，注册一律失败并保持应用内快捷键可用。</summary>
public class NullGlobalHotkeyService : IGlobalHotkeyService
{
    /// <inheritdoc />
    public bool IsSupported
    {
        get { return false; }
    }

    /// <inheritdoc />
    public bool Register(string actionName, string keyText, bool ctrl, bool alt, bool shift, Action callback)
    {
        return false;
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
    }
}
