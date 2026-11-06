using System;
using System.Threading.Tasks;

namespace TabTaskExplorer.Kernel.Capture;

/// <summary>截图目标：全部屏幕（虚拟桌面）、主屏、指定区域或当前活动窗口。</summary>
public enum CaptureTarget
{
    /// <summary>全部屏幕组成的虚拟桌面。</summary>
    FullScreen,

    /// <summary>主显示器。</summary>
    PrimaryScreen,

    /// <summary>调用方指定的物理像素区域。</summary>
    Region,

    /// <summary>当前活动窗口。</summary>
    ActiveWindow,
}

/// <summary>物理像素坐标系中的矩形区域（与界面缩放无关）。</summary>
public struct CaptureArea
{
    public CaptureArea(int x, int y, int width, int height)
    {
        this.X = x;
        this.Y = y;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>左上角横坐标（物理像素，可为负值以支持多显示器）。</summary>
    public int X { get; set; }

    /// <summary>左上角纵坐标（物理像素，可为负值以支持多显示器）。</summary>
    public int Y { get; set; }

    /// <summary>宽度（物理像素）。</summary>
    public int Width { get; set; }

    /// <summary>高度（物理像素）。</summary>
    public int Height { get; set; }

    /// <summary>区域是否有效（宽高均为正数）。</summary>
    public bool IsValid
    {
        get { return this.Width > 0 && this.Height > 0; }
    }

    /// <summary>空区域。</summary>
    public static CaptureArea Empty
    {
        get { return new CaptureArea(0, 0, 0, 0); }
    }

    /// <summary>便于日志阅读的完整描述。</summary>
    public override string ToString()
    {
        return "左上角横坐标=" + this.X + " 左上角纵坐标=" + this.Y
            + " 宽度=" + this.Width + " 高度=" + this.Height;
    }
}

/// <summary>
/// 截图采集后端：默认自动（优先现代采集，失败回退 GDI），也可强制指定以便对比与排查。
/// </summary>
public enum CaptureBackend
{
    /// <summary>平台自动选择：优先 Windows Graphics Capture，不可用或失败时回退 GDI。</summary>
    Auto,

    /// <summary>优先/强制使用 Windows Graphics Capture；仍失败时回退 GDI，并在结果中标注原因。</summary>
    WindowsGraphicsCapture,

    /// <summary>强制使用 GDI（CopyFromScreen），用于对照与绕过采集兼容问题。</summary>
    Gdi,
}

/// <summary>截图能力清单：界面据此禁用当前平台不支持的目标，避免用户点击后才失败。</summary>
public class ScreenCaptureCapabilities
{
    /// <summary>是否支持全部屏幕截图。</summary>
    public bool FullScreen { get; set; }

    /// <summary>是否支持主屏截图。</summary>
    public bool PrimaryScreen { get; set; }

    /// <summary>是否支持区域截图。</summary>
    public bool Region { get; set; }

    /// <summary>是否支持活动窗口截图。</summary>
    public bool ActiveWindow { get; set; }

    /// <summary>是否支持独立窗口选择（可截取被遮挡窗口）。</summary>
    public bool WindowPicker { get; set; }

    /// <summary>采集链路是否具备 HDR / 宽色域感知（按系统合成结果输出 SDR 像素）。</summary>
    public bool HdrAware { get; set; }

    /// <summary>是否可截取被其他窗口遮挡的内容。</summary>
    public bool OccludedWindow { get; set; }

    /// <summary>当前后端名称（中文，用于状态展示与日志）。</summary>
    public string BackendName { get; set; } = string.Empty;
}

/// <summary>一次截图请求。</summary>
public class ScreenCaptureRequest
{
    /// <summary>截图目标。</summary>
    public CaptureTarget Target { get; set; } = CaptureTarget.FullScreen;

    /// <summary>目标为区域时使用的物理像素区域。</summary>
    public CaptureArea Area { get; set; } = CaptureArea.Empty;

    /// <summary>是否把鼠标指针绘制到截图中。</summary>
    public bool IncludeCursor { get; set; } = true;

    /// <summary>
    /// 可选的外部工具路径（Linux / macOS 使用系统截图工具或 FFmpeg 时使用）；
    /// 留空表示由平台实现自行查找。
    /// </summary>
    public string ToolPath { get; set; } = string.Empty;

    /// <summary>采集后端偏好；平台不支持时按自动策略回退。</summary>
    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;
}

/// <summary>一次截图结果：成功时携带 PNG 字节与像素尺寸。</summary>
public class ScreenCaptureResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>PNG 图像字节（仅在成功时有值）。</summary>
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

    /// <summary>实际截取的物理像素宽度。</summary>
    public int PixelWidth { get; set; }

    /// <summary>实际截取的物理像素高度。</summary>
    public int PixelHeight { get; set; }

    /// <summary>实际截取的物理像素区域。</summary>
    public CaptureArea Area { get; set; } = CaptureArea.Empty;

    /// <summary>失败原因（中文，可直接展示给用户）。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>实际使用的采集后端（中文，含回退原因），用于日志排查。</summary>
    public string BackendName { get; set; } = string.Empty;

    /// <summary>构造成功结果。</summary>
    public static ScreenCaptureResult CreateSuccess(byte[] imageBytes, int pixelWidth, int pixelHeight, CaptureArea area)
    {
        ScreenCaptureResult result = new ScreenCaptureResult();
        result.Success = true;
        result.ImageBytes = imageBytes;
        result.PixelWidth = pixelWidth;
        result.PixelHeight = pixelHeight;
        result.Area = area;
        return result;
    }

    /// <summary>构造失败结果。</summary>
    public static ScreenCaptureResult CreateFailure(string errorMessage)
    {
        ScreenCaptureResult result = new ScreenCaptureResult();
        result.Success = false;
        result.ErrorMessage = errorMessage;
        return result;
    }
}

/// <summary>
/// 屏幕截图服务：内核统一契约，Windows 使用 GDI 原生截图，Linux / macOS 使用系统截图工具或 FFmpeg。
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>当前平台的截图能力。</summary>
    ScreenCaptureCapabilities Capabilities { get; }

    /// <summary>执行一次截图；失败时在结果中返回中文原因，不抛出异常。</summary>
    Task<ScreenCaptureResult> CaptureAsync(ScreenCaptureRequest request);
}
