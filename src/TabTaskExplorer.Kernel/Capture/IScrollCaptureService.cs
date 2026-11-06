using System;
using System.Threading.Tasks;

namespace TabTaskExplorer.Kernel.Capture;

/// <summary>
/// 滚动长截图参数：在固定屏幕区域内连续采集画面，按重叠区自动拼接成一张长图。
/// 采用手动滚动模式：由用户自己在选区内滚动目标内容，工具只负责采集、对齐与拼接，
/// 不向目标窗口发送任何滚轮消息，避免驱动用户桌面。
/// </summary>
public class ScrollCaptureOptions
{
    /// <summary>采集区域（物理像素），必须有效。</summary>
    public CaptureArea Area { get; set; } = CaptureArea.Empty;

    /// <summary>是否把鼠标指针画进每一帧；拼接时通常关闭。</summary>
    public bool IncludeCursor { get; set; }

    /// <summary>两帧之间的采集间隔（毫秒）。</summary>
    public int CaptureIntervalMilliseconds { get; set; } = 400;

    /// <summary>最大采集帧数，达到后按已有内容完成。</summary>
    public int MaxFrames { get; set; } = 60;

    /// <summary>拼接结果的最大高度（像素），超过后停止追加，避免位图内存过大。</summary>
    public int MaxHeight { get; set; } = 16000;

    /// <summary>判定重叠所需的最小行数；太小容易误匹配。</summary>
    public int MinOverlapRows { get; set; } = 40;

    /// <summary>匹配容差（归一化平均像素差，0 到 1）；越大越宽松。</summary>
    public double MatchTolerance { get; set; } = 0.04;

    /// <summary>连续多少帧没有新增内容就判定滚动结束。</summary>
    public int StableFrameCount { get; set; } = 3;

    /// <summary>平台截图工具路径（非 Windows 平台使用）。</summary>
    public string ToolPath { get; set; } = string.Empty;

    /// <summary>逐帧采集使用的后端偏好，与普通截图共享同一设置。</summary>
    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;
}

/// <summary>滚动长截图的采集进度。</summary>
public class ScrollCaptureProgress
{
    /// <summary>已采集帧数（含首帧）。</summary>
    public int FrameCount { get; set; }

    /// <summary>当前拼接结果的高度（像素）。</summary>
    public int StitchedHeight { get; set; }

    /// <summary>最近一帧新增的行数；0 表示本帧没有新内容。</summary>
    public int AddedRows { get; set; }

    /// <summary>可直接展示的中文状态。</summary>
    public string StatusText { get; set; } = string.Empty;
}

/// <summary>滚动长截图结果。</summary>
public class ScrollCaptureResult
{
    /// <summary>是否成功得到拼接图。</summary>
    public bool Success { get; set; }

    /// <summary>是否被用户取消。</summary>
    public bool Cancelled { get; set; }

    /// <summary>失败原因（中文）。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>拼接图的 PNG 字节。</summary>
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

    /// <summary>拼接图宽度（像素）。</summary>
    public int PixelWidth { get; set; }

    /// <summary>拼接图高度（像素）。</summary>
    public int PixelHeight { get; set; }

    /// <summary>采集到的帧数。</summary>
    public int FrameCount { get; set; }

    /// <summary>结束原因（中文），写入日志便于排查。</summary>
    public string StopReason { get; set; } = string.Empty;

    /// <summary>构造成功结果。</summary>
    public static ScrollCaptureResult CreateSuccess(byte[] imageBytes, int pixelWidth, int pixelHeight,
        int frameCount, string stopReason)
    {
        ScrollCaptureResult result = new ScrollCaptureResult();
        result.Success = true;
        result.ImageBytes = imageBytes;
        result.PixelWidth = pixelWidth;
        result.PixelHeight = pixelHeight;
        result.FrameCount = frameCount;
        result.StopReason = stopReason;
        return result;
    }

    /// <summary>构造失败结果。</summary>
    public static ScrollCaptureResult CreateFailure(string errorMessage, int frameCount)
    {
        ScrollCaptureResult result = new ScrollCaptureResult();
        result.Success = false;
        result.ErrorMessage = errorMessage;
        result.FrameCount = frameCount;
        result.StopReason = "失败";
        return result;
    }

    /// <summary>构造取消结果。</summary>
    public static ScrollCaptureResult CreateCancelled(string stopReason, int frameCount)
    {
        ScrollCaptureResult result = new ScrollCaptureResult();
        result.Success = false;
        result.Cancelled = true;
        result.StopReason = stopReason;
        result.FrameCount = frameCount;
        return result;
    }
}

/// <summary>
/// 滚动长截图服务：固定区域内连续采集并拼接。Windows 使用 GDI 采集 + 重叠匹配拼接，
/// 其他平台返回不支持。取消由调用方通过 <c>pollCancel</c> 回调决定（右键 / Esc，与普通截图一致）。
/// </summary>
public interface IScrollCaptureService
{
    /// <summary>当前平台是否支持滚动长截图。</summary>
    bool IsSupported { get; }

    /// <summary>执行一次滚动长截图；失败时在结果中返回中文原因，不抛出异常。</summary>
    /// <param name="options">采集与拼接参数。</param>
    /// <param name="progress">进度回调，可为空。</param>
    /// <param name="pollCancel">每帧前调用一次，返回非 None 时立即取消。</param>
    Task<ScrollCaptureResult> CaptureAsync(ScrollCaptureOptions options,
        IProgress<ScrollCaptureProgress>? progress, Func<CaptureCancelSource>? pollCancel);
}

/// <summary>不支持滚动长截图的平台使用：立即返回失败原因。</summary>
public class NullScrollCaptureService : IScrollCaptureService
{
    /// <inheritdoc />
    public bool IsSupported
    {
        get { return false; }
    }

    /// <inheritdoc />
    public Task<ScrollCaptureResult> CaptureAsync(ScrollCaptureOptions options,
        IProgress<ScrollCaptureProgress>? progress, Func<CaptureCancelSource>? pollCancel)
    {
        return Task.FromResult(ScrollCaptureResult.CreateFailure("当前平台不支持滚动长截图", 0));
    }
}
