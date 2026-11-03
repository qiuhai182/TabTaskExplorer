using System;
using System.Runtime.InteropServices;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 窗口宿主实现：通过扩展样式把辅助窗口变成工具窗口，
/// 并用 Shell API 设置进程级 AppUserModelID，避免任务栏图标被拆成多个应用分组。
/// </summary>
public class WindowsWindowHostService : IWindowHostService
{
    private const int ExtendedStyleIndex = -20;
    private const long ExtendedStyleToolWindow = 0x00000080;
    private const long ExtendedStyleAppWindow = 0x00040000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrNative(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrNative(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appUserModelId);

    /// <summary>在进程创建任何窗口前设置 AUMID：入口点和应用初始化阶段各调用一次。</summary>
    public static bool TrySetProcessAppUserModelId(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return false;
        }
        return SetCurrentProcessExplicitAppUserModelID(appUserModelId) >= 0;
    }

    /// <inheritdoc />
    public void ApplyToolWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }
        long style = GetWindowLongPtrNative(windowHandle, ExtendedStyleIndex).ToInt64();
        style = (style | ExtendedStyleToolWindow) & ~ExtendedStyleAppWindow;
        SetWindowLongPtrNative(windowHandle, ExtendedStyleIndex, new IntPtr(style));
        // 仅改扩展样式位不会让资源管理器立即重算任务栏/Alt+Tab 条目；
        // 必须发送 SWP_FRAMECHANGED，让已存在的旧任务栏按钮被移除。
        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder | SwpFrameChanged);
    }

    /// <inheritdoc />
    public void SetAppUserModelId(string appUserModelId)
    {
        if (!TrySetProcessAppUserModelId(appUserModelId))
        {
            throw new InvalidOperationException("设置进程 AppUserModelID 失败：取值=" + appUserModelId);
        }
    }
}
