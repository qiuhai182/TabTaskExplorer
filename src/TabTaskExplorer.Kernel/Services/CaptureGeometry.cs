using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 屏幕采集几何工具：录像使用的 H.264 yuv420p 要求宽高为偶数，
/// 这里统一把录制区域裁剪到最近的偶数尺寸，三平台共用同一规则。
/// </summary>
public static class CaptureGeometry
{
    /// <summary>把区域宽高调整为偶数；宽高不足两个像素时返回 false。</summary>
    public static bool TryMakeEven(CaptureArea area, out CaptureArea evenArea)
    {
        int width = area.Width;
        int height = area.Height;
        if (width % 2 != 0)
        {
            width--;
        }
        if (height % 2 != 0)
        {
            height--;
        }
        evenArea = new CaptureArea(area.X, area.Y, width, height);
        return width > 0 && height > 0;
    }
}
