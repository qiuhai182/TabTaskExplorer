using System;
using System.Runtime.InteropServices;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 屏幕几何查询：虚拟桌面、主屏、活动窗口与鼠标指针的物理像素坐标。
/// 截图与录屏共用同一套坐标解析，保证两者截取范围完全一致。
/// </summary>
internal static class WindowsScreenGeometry
{
    private const int VirtualScreenLeft = 76;

    private const int VirtualScreenTop = 77;

    private const int VirtualScreenWidth = 78;

    private const int VirtualScreenHeight = 79;

    private const int ScreenWidth = 0;

    private const int ScreenHeight = 1;

    private const int DwmExtendedFrameBounds = 9;

    private const uint CursorShowing = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CursorInformation
    {
        public int Size;
        public int Flags;
        public NativePoint ScreenPosition;
        public IntPtr CursorHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IconInformation
    {
        public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr windowHandle, int attribute, out NativeRect rectangle, int rectangleSize);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfoNative(out CursorInformation information);

    /// <summary>读取当前鼠标指针信息，并按系统要求先填写结构体大小。</summary>
    internal static bool GetCursorInformation(out CursorInformation information)
    {
        information = new CursorInformation();
        information.Size = Marshal.SizeOf<CursorInformation>();
        return GetCursorInfoNative(out information);
    }

    [DllImport("user32.dll")]
    internal static extern bool GetIconInfo(IntPtr iconHandle, out IconInformation information);

    [DllImport("user32.dll")]
    internal static extern bool DeleteObject(IntPtr objectHandle);

    /// <summary>全部屏幕组成的虚拟桌面区域。</summary>
    public static CaptureArea GetVirtualScreenArea()
    {
        int x = GetSystemMetrics(VirtualScreenLeft);
        int y = GetSystemMetrics(VirtualScreenTop);
        int width = GetSystemMetrics(VirtualScreenWidth);
        int height = GetSystemMetrics(VirtualScreenHeight);
        return new CaptureArea(x, y, width, height);
    }

    /// <summary>主显示器区域。</summary>
    public static CaptureArea GetPrimaryScreenArea()
    {
        int width = GetSystemMetrics(ScreenWidth);
        int height = GetSystemMetrics(ScreenHeight);
        return new CaptureArea(0, 0, width, height);
    }

    /// <summary>当前活动窗口的可视外框区域；窗口最小化或没有前台窗口时返回空区域。</summary>
    public static CaptureArea GetActiveWindowArea()
    {
        IntPtr windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
        {
            return CaptureArea.Empty;
        }
        if (IsIconic(windowHandle))
        {
            return CaptureArea.Empty;
        }
        NativeRect rectangle = new NativeRect();
        int result = DwmGetWindowAttribute(windowHandle, DwmExtendedFrameBounds, out rectangle, Marshal.SizeOf<NativeRect>());
        if (result != 0)
        {
            if (!GetWindowRect(windowHandle, out rectangle))
            {
                return CaptureArea.Empty;
            }
        }
        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        return new CaptureArea(rectangle.Left, rectangle.Top, width, height);
    }

    /// <summary>把区域裁剪到虚拟桌面范围内；完全落在桌面外时返回空区域。</summary>
    public static CaptureArea ClipToVirtualScreen(CaptureArea area)
    {
        CaptureArea screen = GetVirtualScreenArea();
        int left = Math.Max(area.X, screen.X);
        int top = Math.Max(area.Y, screen.Y);
        int right = Math.Min(area.X + area.Width, screen.X + screen.Width);
        int bottom = Math.Min(area.Y + area.Height, screen.Y + screen.Height);
        if (right <= left || bottom <= top)
        {
            return CaptureArea.Empty;
        }
        return new CaptureArea(left, top, right - left, bottom - top);
    }

    /// <summary>当前鼠标指针是否处于显示状态；隐藏时截图不绘制指针。</summary>
    public static bool IsCursorVisible()
    {
        CursorInformation information;
        if (!GetCursorInformation(out information))
        {
            return false;
        }
        return (information.Flags & (int)CursorShowing) != 0;
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr windowHandle);
}
