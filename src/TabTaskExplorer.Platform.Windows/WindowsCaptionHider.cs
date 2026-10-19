using System;
using System.Runtime.InteropServices;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 实现：去除窗口的 WS_CAPTION 样式位，隐藏系统绘制的窗口图标与程序名。
/// 保留 WS_THICKFRAME / WS_MINIMIZEBOX / WS_MAXIMIZEBOX，窗口仍可拖拽缩放与最小化最大化。
/// </summary>
public class WindowsCaptionHider : IWindowCaptionHider
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long KEEP_MASK = WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public void HideCaption(IntPtr nativeHandle)
    {
        if (nativeHandle == IntPtr.Zero)
        {
            return;
        }
        int style = GetWindowLong(nativeHandle, GWL_STYLE);
        int newStyle = (int)((style & ~WS_CAPTION) | KEEP_MASK);
        if (newStyle != style)
        {
            SetWindowLong(nativeHandle, GWL_STYLE, newStyle);
        }
    }
}
