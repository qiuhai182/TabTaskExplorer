using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 屏幕截图：通过 GDI 直接读取屏幕像素，不依赖外部程序；
/// 支持全部屏幕、主屏、指定区域与活动窗口，并可把鼠标指针画进截图。
/// </summary>
public class WindowsScreenCaptureService : IScreenCaptureService
{
    private const int DrawIconNormal = 0x0003;

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr deviceContext, int x, int y, IntPtr iconHandle,
        int width, int height, int animationStep, IntPtr flickerFreeBrush, int flags);

    /// <inheritdoc />
    public ScreenCaptureCapabilities Capabilities { get; } = CreateCapabilities();

    private static ScreenCaptureCapabilities CreateCapabilities()
    {
        ScreenCaptureCapabilities capabilities = new ScreenCaptureCapabilities();
        capabilities.FullScreen = true;
        capabilities.PrimaryScreen = true;
        capabilities.Region = true;
        capabilities.ActiveWindow = true;
        return capabilities;
    }

    /// <inheritdoc />
    public Task<ScreenCaptureResult> CaptureAsync(ScreenCaptureRequest request)
    {
        return Task.Run(() => this.CaptureCore(request));
    }

    /// <summary>在后台线程执行实际截图，避免阻塞界面。</summary>
    private ScreenCaptureResult CaptureCore(ScreenCaptureRequest request)
    {
        CaptureArea area;
        if (request.Target == CaptureTarget.Region)
        {
            area = request.Area;
        }
        else if (request.Target == CaptureTarget.PrimaryScreen)
        {
            area = WindowsScreenGeometry.GetPrimaryScreenArea();
        }
        else if (request.Target == CaptureTarget.ActiveWindow)
        {
            area = WindowsScreenGeometry.GetActiveWindowArea();
        }
        else
        {
            area = WindowsScreenGeometry.GetVirtualScreenArea();
        }
        if (!area.IsValid)
        {
            return ScreenCaptureResult.CreateFailure("截图区域无效：宽高必须大于零");
        }
        area = WindowsScreenGeometry.ClipToVirtualScreen(area);
        if (!area.IsValid)
        {
            return ScreenCaptureResult.CreateFailure("截图区域完全落在屏幕之外，无法截取");
        }

        try
        {
            using (Bitmap bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CopyFromScreen(area.X, area.Y, 0, 0, new Size(area.Width, area.Height),
                        CopyPixelOperation.SourceCopy);
                    if (request.IncludeCursor)
                    {
                        DrawCursor(graphics, area);
                    }
                }
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return ScreenCaptureResult.CreateSuccess(stream.ToArray(), area.Width, area.Height, area);
                }
            }
        }
        catch (Exception error)
        {
            return ScreenCaptureResult.CreateFailure("Windows 屏幕截图失败：" + error.Message);
        }
    }

    /// <summary>把当前鼠标指针绘制到截图中的正确位置（含指针热点偏移）。</summary>
    private static void DrawCursor(Graphics graphics, CaptureArea area)
    {
        if (!WindowsScreenGeometry.IsCursorVisible())
        {
            return;
        }
        WindowsScreenGeometry.CursorInformation cursorInformation;
        if (!WindowsScreenGeometry.GetCursorInformation(out cursorInformation))
        {
            return;
        }
        if (cursorInformation.CursorHandle == IntPtr.Zero)
        {
            return;
        }
        WindowsScreenGeometry.IconInformation iconInformation;
        if (!WindowsScreenGeometry.GetIconInfo(cursorInformation.CursorHandle, out iconInformation))
        {
            return;
        }
        try
        {
            int x = cursorInformation.ScreenPosition.X - area.X - iconInformation.HotspotX;
            int y = cursorInformation.ScreenPosition.Y - area.Y - iconInformation.HotspotY;
            IntPtr deviceContext = graphics.GetHdc();
            try
            {
                DrawIconEx(deviceContext, x, y, cursorInformation.CursorHandle, 0, 0, 0, IntPtr.Zero, DrawIconNormal);
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }
        finally
        {
            if (iconInformation.MaskBitmap != IntPtr.Zero)
            {
                WindowsScreenGeometry.DeleteObject(iconInformation.MaskBitmap);
            }
            if (iconInformation.ColorBitmap != IntPtr.Zero)
            {
                WindowsScreenGeometry.DeleteObject(iconInformation.ColorBitmap);
            }
        }
    }
}
