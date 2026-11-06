using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 滚动长截图：复用 <see cref="IScreenCaptureService"/> 在固定区域内连续采集，
/// 由 <see cref="ScrollStitcher"/> 按重叠区拼接。
/// 采用手动滚动模式——不向目标窗口发送滚轮消息，用户自己在选区内滚动，停止滚动后自动完成。
/// 取消与普通截图一致：右键或 Esc（由调用方通过 <c>pollCancel</c> 轮询）。
/// </summary>
public class WindowsScrollCaptureService : IScrollCaptureService
{
    /// <summary>两次采集之间的最短间隔，避免把界面刷得过高负载。</summary>
    private const int MinimumIntervalMilliseconds = 150;

    private readonly IScreenCaptureService captureService;

    /// <summary>注入平台截图服务，用于逐帧采集指定区域。</summary>
    public WindowsScrollCaptureService(IScreenCaptureService captureService)
    {
        this.captureService = captureService;
    }

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public Task<ScrollCaptureResult> CaptureAsync(ScrollCaptureOptions options,
        IProgress<ScrollCaptureProgress>? progress, Func<CaptureCancelSource>? pollCancel)
    {
        return Task.Run(delegate
        {
            return this.CaptureCoreAsync(options, progress, pollCancel);
        });
    }

    /// <summary>采集与拼接主循环：首帧入图后，每帧只追加探针带下方的新增内容。</summary>
    private async Task<ScrollCaptureResult> CaptureCoreAsync(ScrollCaptureOptions options,
        IProgress<ScrollCaptureProgress>? progress, Func<CaptureCancelSource>? pollCancel)
    {
        if (options == null || !options.Area.IsValid)
        {
            return ScrollCaptureResult.CreateFailure("滚动长截图区域无效", 0);
        }
        int interval = Math.Max(MinimumIntervalMilliseconds, options.CaptureIntervalMilliseconds);
        int maxFrames = Math.Max(2, options.MaxFrames);
        int maxHeight = Math.Max(600, options.MaxHeight);
        int stableLimit = Math.Max(2, options.StableFrameCount);
        ScrollStitcher? stitcher = null;
        int frameCount = 0;
        int stableCount = 0;
        int failedCount = 0;
        bool hasProgress = false;
        string stopReason = "达到最大帧数";
        ScreenCaptureRequest request = new ScreenCaptureRequest();
        request.Target = CaptureTarget.Region;
        request.Area = options.Area;
        request.IncludeCursor = options.IncludeCursor;
        request.ToolPath = options.ToolPath;
        request.Backend = options.Backend;
        try
        {
            while (frameCount < maxFrames)
            {
                CaptureCancelSource cancel = pollCancel == null ? CaptureCancelSource.None : pollCancel();
                if (cancel != CaptureCancelSource.None)
                {
                    stopReason = cancel == CaptureCancelSource.Escape ? "用户按 Esc 取消" : "用户右键取消";
                    return ScrollCaptureResult.CreateCancelled(stopReason, frameCount);
                }
                ScreenCaptureResult shot = await this.captureService.CaptureAsync(request);
                if (!shot.Success)
                {
                    failedCount++;
                    if (stitcher == null)
                    {
                        return ScrollCaptureResult.CreateFailure("首帧采集失败：" + shot.ErrorMessage, frameCount);
                    }
                    if (failedCount >= stableLimit)
                    {
                        stopReason = "连续采集失败：" + shot.ErrorMessage;
                        break;
                    }
                    await Task.Delay(interval);
                    continue;
                }
                failedCount = 0;
                using (Bitmap frame = DecodeFrame(shot.ImageBytes))
                {
                    if (stitcher == null)
                    {
                        stitcher = new ScrollStitcher(frame.Width, maxHeight,
                            options.MinOverlapRows, options.MatchTolerance);
                        int firstAdded;
                        string firstDetail;
                        stitcher.Append(frame, out firstAdded, out firstDetail);
                        frameCount = 1;
                        progress?.Report(BuildProgress(frameCount, stitcher.Height, firstAdded, firstDetail));
                    }
                    else
                    {
                        int addedRows;
                        string detail;
                        bool matched = stitcher.Append(frame, out addedRows, out detail);
                        frameCount++;
                        if (matched && addedRows > 0)
                        {
                            hasProgress = true;
                            stableCount = 0;
                        }
                        else if (hasProgress)
                        {
                            // 已经开始滚动后，连续几帧没有新增内容即认为用户已停止滚动
                            stableCount++;
                        }
                        progress?.Report(BuildProgress(frameCount, stitcher.Height, addedRows, detail));
                    }
                    if (stitcher.Height >= maxHeight)
                    {
                        stopReason = "达到最大高度：" + maxHeight + " 像素";
                        break;
                    }
                    if (stableCount >= stableLimit)
                    {
                        stopReason = "连续 " + stableCount + " 帧没有新增内容，判定滚动结束";
                        break;
                    }
                }
                await Task.Delay(interval);
            }
            if (stitcher == null || !stitcher.HasContent)
            {
                return ScrollCaptureResult.CreateFailure("没有采集到任何画面", frameCount);
            }
            if (!hasProgress)
            {
                stopReason = stopReason + "（未检测到滚动，结果等同单帧截图）";
            }
            byte[] pngBytes = stitcher.EncodePng();
            return ScrollCaptureResult.CreateSuccess(pngBytes, stitcher.Width, stitcher.Height,
                frameCount, stopReason);
        }
        catch (Exception error)
        {
            return ScrollCaptureResult.CreateFailure("滚动长截图失败：" + error.Message, frameCount);
        }
        finally
        {
            if (stitcher != null)
            {
                stitcher.Dispose();
            }
        }
    }

    /// <summary>组装一条采集进度。</summary>
    private static ScrollCaptureProgress BuildProgress(int frameCount, int height, int addedRows, string detail)
    {
        ScrollCaptureProgress item = new ScrollCaptureProgress();
        item.FrameCount = frameCount;
        item.StitchedHeight = height;
        item.AddedRows = addedRows;
        item.StatusText = "滚动长截图：第 " + frameCount + " 帧 已拼接高度=" + height
            + " 本帧新增=" + addedRows + " " + detail;
        return item;
    }

    /// <summary>把 PNG 解码并归一化为 32bppArgb，保证签名采样与合成行为一致。</summary>
    private static Bitmap DecodeFrame(byte[] pngBytes)
    {
        using (MemoryStream stream = new MemoryStream(pngBytes))
        {
            using (Bitmap decoded = new Bitmap(stream))
            {
                Bitmap normalized = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(normalized))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImage(decoded,
                        new Rectangle(0, 0, decoded.Width, decoded.Height),
                        0, 0, decoded.Width, decoded.Height, GraphicsUnit.Pixel);
                }
                return normalized;
            }
        }
    }
}
