using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using TabTaskExplorer.Kernel.Media;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 视频渲染面宿主：在 Avalonia 视觉树里开一个原生子窗口，把句柄交给播放引擎由 libvlc 直接绘制。
/// libvlc 的画面不走 Avalonia 渲染管线，因此本控件所在区域内的 Avalonia 元素会被原生窗口覆盖，
/// 界面上的控制条、菜单一律放在该区域之外（见 MediaPanel 布局）。
/// </summary>
public class MediaSurfaceHost : NativeControlHost, IMediaSurface
{
    /// <summary>窗口样式：子窗口 + 可见 + 裁剪子窗口。</summary>
    private const uint WsChild = 0x40000000;

    /// <summary>窗口样式：可见。</summary>
    private const uint WsVisible = 0x10000000;

    /// <summary>窗口样式：裁剪子窗口。</summary>
    private const uint WsClipChildren = 0x02000000;

    /// <summary>窗口样式：裁剪兄弟窗口（避免闪烁）。</summary>
    private const uint WsClipSiblings = 0x04000000;

    private IntPtr nativeHandle;

    /// <inheritdoc />
    public IntPtr NativeHandle
    {
        get { return this.nativeHandle; }
    }

    /// <inheritdoc />
    public bool IsReady
    {
        get { return this.nativeHandle != IntPtr.Zero; }
    }

    /// <inheritdoc />
    public event EventHandler? HandleChanged;

    /// <summary>创建承载视频画面的原生子窗口（仅 Windows；其他平台回落默认实现）。</summary>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsWindows())
        {
            IntPtr window = CreateWindowExW(0, "static", string.Empty,
                WsChild | WsVisible | WsClipChildren | WsClipSiblings,
                0, 0, 16, 16, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (window != IntPtr.Zero)
            {
                this.nativeHandle = window;
                this.RaiseHandleChanged();
                return new PlatformHandle(window, "HWND");
            }
        }
        return base.CreateNativeControlCore(parent);
    }

    /// <summary>销毁原生子窗口。</summary>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (OperatingSystem.IsWindows() && control.Handle == this.nativeHandle && this.nativeHandle != IntPtr.Zero)
        {
            DestroyWindow(this.nativeHandle);
            this.nativeHandle = IntPtr.Zero;
            return;
        }
        base.DestroyNativeControlCore(control);
    }

    /// <summary>通知播放引擎句柄已就绪。</summary>
    private void RaiseHandleChanged()
    {
        EventHandler? handler = this.HandleChanged;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int width, int height, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);
}
