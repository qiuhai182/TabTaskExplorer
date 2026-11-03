using System;
using System.Runtime.InteropServices;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 截图取消输入：直接读取鼠标右键与 Esc 的全局按下状态。
/// 使用 GetAsyncKeyState 而不是键盘钩子，避免干扰其他程序的正常输入，
/// 同时可以在遮罩窗口没有拿到焦点时兜住 Esc。
/// </summary>
public class WindowsCaptureCancelInput : ICaptureCancelInput
{
    private const int VirtualKeyRightButton = 0x02;
    private const int VirtualKeyEscape = 0x1B;
    private const int KeyStateDown = 0x8000;
    private const int KeyStatePressedSinceLastCall = 0x0001;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private bool lastRightButtonDown;
    private bool lastEscapeDown;

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public void Reset()
    {
        // 先读一次并丢弃，清除“自上次调用后按下”标记，避免触发截图的那次右键/菜单操作被当成取消
        int rightState = GetAsyncKeyState(VirtualKeyRightButton);
        int escapeState = GetAsyncKeyState(VirtualKeyEscape);
        this.lastRightButtonDown = (rightState & KeyStateDown) != 0;
        this.lastEscapeDown = (escapeState & KeyStateDown) != 0;
    }

    /// <inheritdoc />
    public CaptureCancelSource PollCancelRequest()
    {
        int rightState = GetAsyncKeyState(VirtualKeyRightButton);
        int escapeState = GetAsyncKeyState(VirtualKeyEscape);
        bool rightDown = (rightState & KeyStateDown) != 0;
        bool escapeDown = (escapeState & KeyStateDown) != 0;
        bool rightPressed = (rightState & KeyStatePressedSinceLastCall) != 0;
        bool escapePressed = (escapeState & KeyStatePressedSinceLastCall) != 0;
        CaptureCancelSource source = CaptureCancelSource.None;
        if (rightPressed || (rightDown && !this.lastRightButtonDown))
        {
            source = CaptureCancelSource.RightButton;
        }
        else if (escapePressed || (escapeDown && !this.lastEscapeDown))
        {
            source = CaptureCancelSource.Escape;
        }
        this.lastRightButtonDown = rightDown;
        this.lastEscapeDown = escapeDown;
        return source;
    }
}
