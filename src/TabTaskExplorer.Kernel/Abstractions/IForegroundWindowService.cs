using System;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 前台窗口记录与恢复：截图遮罩关闭后把焦点还给取消前的程序，避免退出截图时把前台切到本程序。
/// </summary>
public interface IForegroundWindowService
{
    /// <summary>记录当前前台窗口句柄；不支持时返回零。</summary>
    IntPtr CaptureForegroundWindow();

    /// <summary>把指定窗口恢复到前台；失败或句柄无效时返回 false。</summary>
    bool RestoreForegroundWindow(IntPtr windowHandle);
}

/// <summary>不支持前台窗口恢复的平台使用：记录返回零，恢复永远失败。</summary>
public class NullForegroundWindowService : IForegroundWindowService
{
    /// <inheritdoc />
    public IntPtr CaptureForegroundWindow()
    {
        return IntPtr.Zero;
    }

    /// <inheritdoc />
    public bool RestoreForegroundWindow(IntPtr windowHandle)
    {
        return false;
    }
}
