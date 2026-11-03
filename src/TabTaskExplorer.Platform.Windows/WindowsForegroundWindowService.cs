using System;
using System.Runtime.InteropServices;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>Windows 前台窗口记录与恢复。</summary>
public class WindowsForegroundWindowService : IForegroundWindowService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr windowHandle);

    /// <inheritdoc />
    public IntPtr CaptureForegroundWindow()
    {
        return GetForegroundWindow();
    }

    /// <inheritdoc />
    public bool RestoreForegroundWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
        {
            return false;
        }
        return SetForegroundWindow(windowHandle);
    }
}
