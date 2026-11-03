using System;
using System.Collections.Generic;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInformation
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitorHandle, IntPtr deviceContext,
        IntPtr monitorRectangle, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr deviceContext, int x, int y, IntPtr iconHandle,
        int width, int height, int animationStep, IntPtr flickerFreeBrush, int flags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRectangle,
        MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInformation information);

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
        return Task.Run(delegate
        {
            return this.CaptureCore(request);
        });
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
                    CapturePixels(graphics, area);
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

    /// <summary>
    /// 按显示器分别采集像素再按虚拟坐标拼接：跨屏选区在各屏物理坐标下裁剪，
    /// 避免混合 DPI 或负坐标布局时错位、半屏空白或重复区域。
    /// </summary>
    private static void CapturePixels(Graphics graphics, CaptureArea area)
    {
        List<NativeRect> monitors = EnumerateMonitors();
        if (monitors.Count == 0)
        {
            graphics.CopyFromScreen(area.X, area.Y, 0, 0, new Size(area.Width, area.Height),
                CopyPixelOperation.SourceCopy);
            return;
        }
        bool capturedAny = false;
        foreach (NativeRect monitor in monitors)
        {
            int left = Math.Max(area.X, monitor.Left);
            int top = Math.Max(area.Y, monitor.Top);
            int right = Math.Min(area.X + area.Width, monitor.Right);
            int bottom = Math.Min(area.Y + area.Height, monitor.Bottom);
            if (right <= left || bottom <= top)
            {
                continue;
            }
            int destinationX = left - area.X;
            int destinationY = top - area.Y;
            graphics.CopyFromScreen(left, top, destinationX, destinationY,
                new Size(right - left, bottom - top), CopyPixelOperation.SourceCopy);
            capturedAny = true;
        }
        if (!capturedAny)
        {
            graphics.CopyFromScreen(area.X, area.Y, 0, 0, new Size(area.Width, area.Height),
                CopyPixelOperation.SourceCopy);
        }
    }

    /// <summary>枚举全部显示器物理矩形；失败时返回空列表。</summary>
    private static List<NativeRect> EnumerateMonitors()
    {
        List<NativeRect> monitors = new List<NativeRect>();
        MonitorEnumProc callback = delegate(IntPtr monitorHandle, IntPtr deviceContext,
            IntPtr monitorRectangle, IntPtr data)
        {
            MonitorInformation information = new MonitorInformation();
            information.Size = Marshal.SizeOf<MonitorInformation>();
            if (GetMonitorInfo(monitorHandle, ref information))
            {
                monitors.Add(information.Monitor);
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return monitors;
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
