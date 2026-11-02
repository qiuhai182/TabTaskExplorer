using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 区域预选：在框选窗口显示前枚举当前屏幕上的可见顶层窗口与 Win32 子窗口，
/// 供框选窗口按鼠标位置自动预选窗口、窗口内子视图，并在桌面空白处回落为全屏。
/// </summary>
public class WindowsScreenRegionDetector : IScreenRegionDetector
{
    private const int MinimumCandidateSize = 32;
    private const int MaximumCandidates = 1500;
    private const int MaximumChildrenPerWindow = 200;
    private const int MaximumWindowTitleLength = 260;
    private const int DwmExtendedFrameBounds = 9;
    private const int DwmCloaked = 14;
    private const int WindowExtendedStyleIndex = -20;
    private const int WindowExtendedStyleTransparent = 0x00000020;

    // 桌面、任务栏等外壳窗口不作为可截取窗口，鼠标位于这些区域时统一回落为全屏
    private static readonly string[] ShellWindowClasses =
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
        "TaskListThumbnailWnd",
    };

    private delegate bool EnumWindowProc(IntPtr windowHandle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parentHandle, EnumWindowProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr windowHandle, int attribute, out NativeRect rectangle,
        int rectangleSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeCloaked(IntPtr windowHandle, int attribute, out int cloakedState,
        int valueSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScreenRegionCandidate> Detect(CaptureArea screenArea)
    {
        List<ScreenRegionCandidate> candidates = new List<ScreenRegionCandidate>();
        if (!screenArea.IsValid)
        {
            return candidates;
        }
        ScreenRegionCandidate fullScreen = new ScreenRegionCandidate();
        fullScreen.Area = screenArea;
        fullScreen.Kind = ScreenRegionKind.FullScreen;
        fullScreen.Name = "全屏（当前显示器）";
        fullScreen.Depth = 0;
        fullScreen.ZOrder = -1;
        candidates.Add(fullScreen);

        List<ScreenRegionCandidate> windows = new List<ScreenRegionCandidate>();
        int zOrder = 0;
        EnumWindows(delegate(IntPtr windowHandle, IntPtr parameter)
        {
            if (!IsWindowVisible(windowHandle) || IsIconic(windowHandle))
            {
                return true;
            }
            string className = ReadClassName(windowHandle);
            if (IsShellWindowClass(className))
            {
                return true;
            }
            if (IsClickThroughWindow(windowHandle) || IsCloakedWindow(windowHandle))
            {
                return true;
            }
            CaptureArea area;
            if (!TryReadWindowArea(windowHandle, true, out area))
            {
                return true;
            }
            if (!Intersects(area, screenArea) || area.Width < MinimumCandidateSize || area.Height < MinimumCandidateSize)
            {
                return true;
            }
            ScreenRegionCandidate window = new ScreenRegionCandidate();
            window.Area = area;
            window.Kind = ScreenRegionKind.Window;
            window.Name = ReadWindowName(windowHandle, className);
            window.NativeHandle = windowHandle.ToInt64();
            window.Depth = 1;
            window.ZOrder = zOrder;
            zOrder++;
            windows.Add(window);
            return true;
        }, IntPtr.Zero);

        candidates.AddRange(windows);
        foreach (ScreenRegionCandidate window in windows)
        {
            AppendChildViews(candidates, window, screenArea);
            if (candidates.Count >= MaximumCandidates)
            {
                break;
            }
        }
        return candidates;
    }

    /// <inheritdoc />
    public bool TryGetPointerPosition(out int x, out int y)
    {
        NativePoint point;
        if (GetCursorPos(out point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }
        x = 0;
        y = 0;
        return false;
    }

    /// <summary>枚举一个顶层窗口内的可见子窗口，作为鼠标可预选的子视图。</summary>
    private static void AppendChildViews(List<ScreenRegionCandidate> candidates, ScreenRegionCandidate parent,
        CaptureArea screenArea)
    {
        int addedCount = 0;
        IntPtr parentHandle = new IntPtr(parent.NativeHandle);
        HashSet<string> seenAreas = new HashSet<string>();
        EnumChildWindows(parentHandle, delegate(IntPtr childHandle, IntPtr parameter)
        {
            if (addedCount >= MaximumChildrenPerWindow || candidates.Count >= MaximumCandidates)
            {
                return false;
            }
            if (!IsWindowVisible(childHandle))
            {
                return true;
            }
            CaptureArea area;
            if (!TryReadWindowArea(childHandle, false, out area))
            {
                return true;
            }
            if (!Intersects(area, screenArea) || area.Width < MinimumCandidateSize || area.Height < MinimumCandidateSize)
            {
                return true;
            }
            // 子窗口与所属窗口范围完全一致时没有额外信息，跳过以免与窗口候选重复
            if (area.X == parent.Area.X && area.Y == parent.Area.Y
                && area.Width == parent.Area.Width && area.Height == parent.Area.Height)
            {
                return true;
            }
            string areaKey = area.X + "," + area.Y + "," + area.Width + "," + area.Height;
            if (!seenAreas.Add(areaKey))
            {
                return true;
            }
            string className = ReadClassName(childHandle);
            ScreenRegionCandidate child = new ScreenRegionCandidate();
            child.Area = area;
            child.Kind = ScreenRegionKind.ChildView;
            child.Name = "子视图：" + ReadWindowName(childHandle, className);
            child.NativeHandle = childHandle.ToInt64();
            child.ParentHandle = parent.NativeHandle;
            child.Depth = ResolveDepth(childHandle, parentHandle);
            child.ZOrder = parent.ZOrder;
            candidates.Add(child);
            addedCount++;
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>
    /// 读取窗口物理像素外框；顶层窗口优先使用 DWM 可见外框，避免把不可见的窗口阴影边框算进预选区域。
    /// </summary>
    private static bool TryReadWindowArea(IntPtr windowHandle, bool preferExtendedFrame, out CaptureArea area)
    {
        area = CaptureArea.Empty;
        NativeRect rectangle;
        bool readSuccess = false;
        if (preferExtendedFrame)
        {
            int result = DwmGetWindowAttribute(windowHandle, DwmExtendedFrameBounds, out rectangle,
                Marshal.SizeOf<NativeRect>());
            readSuccess = result == 0;
        }
        if (!readSuccess && !GetWindowRect(windowHandle, out rectangle))
        {
            return false;
        }
        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }
        area = new CaptureArea(rectangle.Left, rectangle.Top, width, height);
        return true;
    }

    /// <summary>计算子窗口相对顶层窗口的层级深度，最多统计 8 层，避免异常父子关系导致死循环。</summary>
    private static int ResolveDepth(IntPtr childHandle, IntPtr topLevelHandle)
    {
        int depth = 2;
        IntPtr current = GetParent(childHandle);
        while (current != IntPtr.Zero && current != topLevelHandle && depth < 8)
        {
            depth++;
            IntPtr next = GetParent(current);
            if (next == current)
            {
                break;
            }
            current = next;
        }
        return depth;
    }

    /// <summary>读取窗口标题；没有标题时退回窗口类名，确保界面始终有可读名称。</summary>
    private static string ReadWindowName(IntPtr windowHandle, string className)
    {
        StringBuilder title = new StringBuilder(MaximumWindowTitleLength);
        int length = GetWindowText(windowHandle, title, title.Capacity);
        if (length > 0)
        {
            string text = title.ToString(0, length).Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 80)
            {
                text = text.Substring(0, 80) + "…";
            }
            if (text.Length > 0)
            {
                return text;
            }
        }
        if (className.Length > 0)
        {
            return className;
        }
        return "未命名窗口";
    }

    /// <summary>读取窗口类名。</summary>
    private static string ReadClassName(IntPtr windowHandle)
    {
        StringBuilder className = new StringBuilder(MaximumWindowTitleLength);
        int length = GetClassName(windowHandle, className, className.Capacity);
        if (length <= 0)
        {
            return string.Empty;
        }
        return className.ToString(0, length);
    }

    /// <summary>判断窗口是否为鼠标穿透的透明覆盖层，避免把不可见覆盖层当成可截取窗口。</summary>
    private static bool IsClickThroughWindow(IntPtr windowHandle)
    {
        int extendedStyle = GetWindowLong(windowHandle, WindowExtendedStyleIndex);
        return (extendedStyle & WindowExtendedStyleTransparent) != 0;
    }

    /// <summary>判断窗口是否被 DWM 隐藏（例如位于其他虚拟桌面），隐藏窗口不参与预选。</summary>
    private static bool IsCloakedWindow(IntPtr windowHandle)
    {
        int cloakedState = 0;
        int result = DwmGetWindowAttributeCloaked(windowHandle, DwmCloaked, out cloakedState, sizeof(int));
        return result == 0 && cloakedState != 0;
    }

    /// <summary>判断窗口类名是否属于桌面或任务栏等外壳窗口。</summary>
    private static bool IsShellWindowClass(string className)
    {
        foreach (string shellClass in ShellWindowClasses)
        {
            if (string.Equals(className, shellClass, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>判断区域是否与指定屏幕区域相交。</summary>
    private static bool Intersects(CaptureArea area, CaptureArea screenArea)
    {
        int left = Math.Max(area.X, screenArea.X);
        int top = Math.Max(area.Y, screenArea.Y);
        int right = Math.Min(area.X + area.Width, screenArea.X + screenArea.Width);
        int bottom = Math.Min(area.Y + area.Height, screenArea.Y + screenArea.Height);
        return right > left && bottom > top;
    }
}
