using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 全局快捷键：使用 WH_KEYBOARD_LL 低级键盘钩子在系统级拦截配置过的组合键，
/// 再通过后台消息循环分派动作。相比 RegisterHotKey，低级钩子可以覆盖其他程序注册的快捷键；
/// Ctrl+Alt+Del、Win+L 等由系统安全桌面处理的组合无法覆盖。
/// </summary>
public class WindowsGlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private const int HookKeyboardLowLevel = 13;

    private const int MessageKeyDown = 0x0100;

    private const int MessageKeyUp = 0x0101;

    private const int MessageSystemKeyDown = 0x0104;

    private const int MessageSystemKeyUp = 0x0105;

    private const int MessageQuit = 0x0012;

    private const uint HookFlagInjected = 0x10;

    private const int VirtualKeyShift = 0x10;

    private const int VirtualKeyControl = 0x11;

    private const int VirtualKeyAlt = 0x12;

    private const int VirtualKeyLeftShift = 0xA0;

    private const int VirtualKeyRightShift = 0xA1;

    private const int VirtualKeyLeftControl = 0xA2;

    private const int VirtualKeyRightControl = 0xA3;

    private const int VirtualKeyLeftAlt = 0xA4;

    private const int VirtualKeyRightAlt = 0xA5;

    private const int VirtualKeyLeftWin = 0x5B;

    private const int VirtualKeyRightWin = 0x5C;

    /// <summary>一条已经注册的全局快捷键。</summary>
    private sealed class Registration
    {
        public string ActionName = string.Empty;
        public uint VirtualKey;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public bool Win;
        public Action? Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public IntPtr ParameterW;
        public IntPtr ParameterL;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr parameterW, IntPtr parameterL);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int hookIdentifier, LowLevelKeyboardProc callback,
        IntPtr moduleHandle, uint threadIdentifier);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr parameterW, IntPtr parameterL);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out NativeMessage message, IntPtr windowHandle,
        uint filterMinimum, uint filterMaximum);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessageW(uint threadIdentifier, uint message, IntPtr parameterW, IntPtr parameterL);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    private readonly List<Registration> registrations = new List<Registration>();
    private readonly object registrationLock = new object();
    private readonly Dictionary<int, Registration> suppressedKeys = new Dictionary<int, Registration>();
    private readonly HashSet<int> pressedKeys = new HashSet<int>();
    private readonly LowLevelKeyboardProc hookCallback;
    private readonly ManualResetEventSlim readyEvent = new ManualResetEventSlim(false);
    private readonly object startLock = new object();
    private Thread? hookThread;
    private IntPtr hookHandle = IntPtr.Zero;
    private uint hookThreadIdentifier;
    private volatile bool running;
    private volatile bool disposed;
    private volatile bool disabled;
    private bool ctrlDown;
    private bool altDown;
    private bool shiftDown;
    private bool winDown;
    private bool winUsedInCombination;

    private bool altUsedInCombination;

    public WindowsGlobalHotkeyService()
    {
        this.hookCallback = this.OnLowLevelKeyboardMessage;
    }

    /// <inheritdoc />
    public bool IsSupported
    {
        get
        {
            if (this.disposed || this.disabled)
            {
                return false;
            }
            this.EnsureHookThread();
            return this.hookHandle != IntPtr.Zero;
        }
    }

    /// <inheritdoc />
    public bool Register(string actionName, string keyText, bool ctrl, bool alt, bool shift, bool win, Action callback)
    {
        if (this.disposed || this.disabled)
        {
            return false;
        }
        uint virtualKey = ToVirtualKey(keyText);
        if (virtualKey == 0)
        {
            return false;
        }
        if (!this.IsSupported)
        {
            return false;
        }
        Registration registration = new Registration();
        registration.ActionName = actionName;
        registration.VirtualKey = virtualKey;
        registration.Ctrl = ctrl;
        registration.Alt = alt;
        registration.Shift = shift;
        registration.Win = win;
        registration.Callback = callback;
        lock (this.registrationLock)
        {
            foreach (Registration existing in this.registrations)
            {
                if (existing.VirtualKey == virtualKey
                    && existing.Ctrl == ctrl
                    && existing.Alt == alt
                    && existing.Shift == shift
                    && existing.Win == win)
                {
                    // 同一个按键组合只允许绑定一个动作，避免一次按键触发多个功能
                    return false;
                }
            }
            this.registrations.Add(registration);
        }
        return true;
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        lock (this.registrationLock)
        {
            this.registrations.Clear();
        }
    }

    /// <summary>
    /// 完全停用全局快捷键：先清空注册项，再停止钩子线程并通过 UnhookWindowsHookEx 摘除低级键盘钩子，
    /// 最后兜底按句柄卸载一次，保证进程内不再残留任何全局按键拦截。
    /// </summary>
    public void DisableAll()
    {
        this.disabled = true;
        lock (this.registrationLock)
        {
            this.registrations.Clear();
        }
        this.running = false;
        if (this.hookThreadIdentifier != 0)
        {
            PostThreadMessageW(this.hookThreadIdentifier, (uint)MessageQuit, IntPtr.Zero, IntPtr.Zero);
        }
        Thread? thread = this.hookThread;
        if (thread != null)
        {
            if (!thread.Join(1500))
            {
                // 消息队列可能刚建立，补投一次退出消息，确保钩子线程能回到卸载钩子的收尾逻辑
                if (this.hookThreadIdentifier != 0)
                {
                    PostThreadMessageW(this.hookThreadIdentifier, (uint)MessageQuit, IntPtr.Zero, IntPtr.Zero);
                }
                thread.Join(500);
            }
            this.hookThread = null;
        }
        if (this.hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(this.hookHandle);
            this.hookHandle = IntPtr.Zero;
        }
        this.hookThreadIdentifier = 0;
        this.pressedKeys.Clear();
        this.suppressedKeys.Clear();
        this.ctrlDown = false;
        this.altDown = false;
        this.shiftDown = false;
        this.winDown = false;
        this.winUsedInCombination = false;
        this.altUsedInCombination = false;
    }

    /// <summary>重新启用全局快捷键：清理停用标记，下次注册或查询能力时重新安装键盘钩子。</summary>
    public void EnableAll()
    {
        this.disabled = false;
    }

    /// <summary>确保低级键盘钩子线程已经启动。</summary>
    private void EnsureHookThread()
    {
        if (this.hookThread != null)
        {
            // 钩子线程可能仍在安装中，等待一次就绪信号，避免并发读取到零句柄
            this.readyEvent.Wait(2000);
            return;
        }
        lock (this.startLock)
        {
            if (this.hookThread != null)
            {
                this.readyEvent.Wait(2000);
                return;
            }
            this.readyEvent.Reset();
            this.hookThread = new Thread(new ThreadStart(this.RunHookThread));
            this.hookThread.IsBackground = true;
            this.hookThread.Name = "全局快捷键键盘钩子线程";
            this.hookThread.Start();
            this.readyEvent.Wait(2000);
        }
    }

    /// <summary>钩子线程：安装低级键盘钩子并运行消息循环。</summary>
    private void RunHookThread()
    {
        this.hookThreadIdentifier = GetCurrentThreadId();
        this.hookHandle = SetWindowsHookExW(HookKeyboardLowLevel, this.hookCallback,
            GetModuleHandleW(null), 0);
        this.readyEvent.Set();
        if (this.hookHandle == IntPtr.Zero)
        {
            return;
        }
        this.running = true;
        NativeMessage message;
        while (this.running && GetMessageW(out message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
        this.running = false;
        if (this.hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(this.hookHandle);
            this.hookHandle = IntPtr.Zero;
        }
    }

    /// <summary>低级键盘回调：匹配配置过的组合键，命中时返回 1 阻止按键继续传递给其他程序。</summary>
    private IntPtr OnLowLevelKeyboardMessage(int code, IntPtr parameterW, IntPtr parameterL)
    {
        if (code < 0)
        {
            return CallNextHookEx(this.hookHandle, code, parameterW, parameterL);
        }
        KeyboardHookData data = Marshal.PtrToStructure<KeyboardHookData>(parameterL);
        if ((data.Flags & HookFlagInjected) != 0u)
        {
            // 忽略程序模拟注入的按键，避免与自动化脚本互相触发
            return CallNextHookEx(this.hookHandle, code, parameterW, parameterL);
        }
        int message = parameterW.ToInt32();
        bool isDown = message == MessageKeyDown || message == MessageSystemKeyDown;
        bool isUp = message == MessageKeyUp || message == MessageSystemKeyUp;
        if (!isDown && !isUp)
        {
            return CallNextHookEx(this.hookHandle, code, parameterW, parameterL);
        }
        int virtualKey = (int)data.VirtualKeyCode;
        this.UpdateModifierState(virtualKey, isDown);
        if (IsModifierKey(virtualKey))
        {
            if (isUp && IsWinKey(virtualKey) && this.winUsedInCombination)
            {
                // 组合键使用过 Win 键时吞掉 Win 抬起事件，避免同时弹出开始菜单
                this.winUsedInCombination = false;
                return (IntPtr)1;
            }
            if (isUp && IsAltKey(virtualKey) && this.altUsedInCombination)
            {
                // 组合键使用过 Alt 键时吞掉 Alt 抬起事件，避免激活窗口菜单栏
                this.altUsedInCombination = false;
                return (IntPtr)1;
            }
            return CallNextHookEx(this.hookHandle, code, parameterW, parameterL);
        }

        if (isDown)
        {
            if (this.pressedKeys.Contains(virtualKey))
            {
                if (this.suppressedKeys.ContainsKey(virtualKey))
                {
                    return (IntPtr)1;
                }
                return CallNextHookEx(this.hookHandle, code, parameterW, parameterL);
            }
            this.pressedKeys.Add(virtualKey);
            Registration? matched = this.FindMatch(virtualKey);
            if (matched == null)
            {
                return CallNextHookEx(this.hookHandle, code, parameterW, parameterL);
            }
            this.suppressedKeys[virtualKey] = matched;
            if (matched.Win)
            {
                this.winUsedInCombination = true;
            }
            if (matched.Alt)
            {
                this.altUsedInCombination = true;
            }
            Action? callback = matched.Callback;
            if (callback != null)
            {
                ThreadPool.QueueUserWorkItem(delegate (object? state)
                {
                    try
                    {
                        callback();
                    }
                    catch
                    {
                        // 回调异常不能终止键盘钩子线程
                    }
                });
            }
            return (IntPtr)1;
        }

        this.pressedKeys.Remove(virtualKey);
        if (this.suppressedKeys.Remove(virtualKey))
        {
            return (IntPtr)1;
        }
        return CallNextHookEx(this.hookHandle, code, parameterW, parameterL);
    }

    /// <summary>按当前修饰键状态在已注册组合中查找匹配项。</summary>
    private Registration? FindMatch(int virtualKey)
    {
        lock (this.registrationLock)
        {
            foreach (Registration registration in this.registrations)
            {
                if ((int)registration.VirtualKey != virtualKey)
                {
                    continue;
                }
                if (registration.Ctrl != this.ctrlDown
                    || registration.Alt != this.altDown
                    || registration.Shift != this.shiftDown
                    || registration.Win != this.winDown)
                {
                    continue;
                }
                return registration;
            }
        }
        return null;
    }

    /// <summary>更新修饰键按下状态；低级键盘钩子线程独占访问。</summary>
    private void UpdateModifierState(int virtualKey, bool isDown)
    {
        if (virtualKey == VirtualKeyLeftControl || virtualKey == VirtualKeyRightControl || virtualKey == VirtualKeyControl)
        {
            this.ctrlDown = isDown;
        }
        else if (virtualKey == VirtualKeyLeftAlt || virtualKey == VirtualKeyRightAlt || virtualKey == VirtualKeyAlt)
        {
            this.altDown = isDown;
        }
        else if (virtualKey == VirtualKeyLeftShift || virtualKey == VirtualKeyRightShift || virtualKey == VirtualKeyShift)
        {
            this.shiftDown = isDown;
        }
        else if (virtualKey == VirtualKeyLeftWin || virtualKey == VirtualKeyRightWin)
        {
            this.winDown = isDown;
        }
    }

    private static bool IsModifierKey(int virtualKey)
    {
        return virtualKey == VirtualKeyShift
            || virtualKey == VirtualKeyControl
            || virtualKey == VirtualKeyAlt
            || virtualKey == VirtualKeyLeftShift
            || virtualKey == VirtualKeyRightShift
            || virtualKey == VirtualKeyLeftControl
            || virtualKey == VirtualKeyRightControl
            || virtualKey == VirtualKeyLeftAlt
            || virtualKey == VirtualKeyRightAlt
            || virtualKey == VirtualKeyLeftWin
            || virtualKey == VirtualKeyRightWin;
    }

    private static bool IsWinKey(int virtualKey)
    {
        return virtualKey == VirtualKeyLeftWin || virtualKey == VirtualKeyRightWin;
    }

    private static bool IsAltKey(int virtualKey)
    {
        return virtualKey == VirtualKeyAlt
            || virtualKey == VirtualKeyLeftAlt
            || virtualKey == VirtualKeyRightAlt;
    }

    /// <summary>把 Avalonia 按键名称转换为 Windows 虚拟键码；无法识别时返回零。</summary>
    private static uint ToVirtualKey(string keyText)
    {
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return 0;
        }
        string text = keyText.Trim().ToUpperInvariant();
        if (text.Length == 1)
        {
            char letter = text[0];
            if (letter >= 'A' && letter <= 'Z')
            {
                return (uint)(0x41 + (letter - 'A'));
            }
            if (letter >= '0' && letter <= '9')
            {
                return (uint)(0x30 + (letter - '0'));
            }
        }
        if (text.Length == 2 && text[0] == 'D' && text[1] >= '0' && text[1] <= '9')
        {
            return (uint)(0x30 + (text[1] - '0'));
        }
        if (text.Length >= 2 && text[0] == 'F')
        {
            int functionNumber = 0;
            bool parsed = int.TryParse(text.Substring(1), out functionNumber);
            if (parsed && functionNumber >= 1 && functionNumber <= 24)
            {
                return (uint)(0x70 + functionNumber - 1);
            }
        }
        switch (text)
        {
            case "TAB":
                return 0x09;
            case "ENTER":
                return 0x0D;
            case "RETURN":
                return 0x0D;
            case "SPACE":
                return 0x20;
            case "DELETE":
                return 0x2E;
            case "INSERT":
                return 0x2D;
            case "BACKSPACE":
                return 0x08;
            case "HOME":
                return 0x24;
            case "END":
                return 0x23;
            case "PAGEUP":
                return 0x21;
            case "PAGEDOWN":
                return 0x22;
            case "ESCAPE":
                return 0x1B;
            case "ESC":
                return 0x1B;
            case "PRINTSCREEN":
                return 0x2C;
            case "SNAPSHOT":
                return 0x2C;
            case "PAUSE":
                return 0x13;
            case "SCROLLLOCK":
                return 0x91;
            case "NUMLOCK":
                return 0x90;
            case "CAPSLOCK":
                return 0x14;
            case "LEFT":
                return 0x25;
            case "RIGHT":
                return 0x27;
            case "UP":
                return 0x26;
            case "DOWN":
                return 0x28;
            default:
                return 0;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.disposed = true;
        this.running = false;
        if (this.hookThreadIdentifier != 0)
        {
            PostThreadMessageW(this.hookThreadIdentifier, (uint)MessageQuit, IntPtr.Zero, IntPtr.Zero);
        }
        Thread? thread = this.hookThread;
        if (thread != null)
        {
            if (!thread.Join(1000) && this.hookThreadIdentifier != 0)
            {
                // 线程可能刚创建、消息队列尚未建立，再次投递退出消息
                PostThreadMessageW(this.hookThreadIdentifier, (uint)MessageQuit, IntPtr.Zero, IntPtr.Zero);
                thread.Join(500);
            }
            this.hookThread = null;
        }
        this.readyEvent.Dispose();
        this.UnregisterAll();
    }
}
