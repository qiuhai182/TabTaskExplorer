using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 全局快捷键：在独立线程上注册线程级热键并自建消息循环接收 WM_HOTKEY。
/// 注册动作通过线程队列同步执行，避免跨线程调用 RegisterHotKey 导致热键绑定到错误线程。
/// </summary>
public class WindowsGlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private const uint ModifierAlt = 0x0001;

    private const uint ModifierControl = 0x0002;

    private const uint ModifierShift = 0x0004;

    private const uint ModifierNoRepeat = 0x4000;

    private const uint WindowMessageHotkey = 0x0312;

    private const uint PeekMessageRemove = 0x0001;

    private const int MaximumHotkeyIdentifier = 0xBFFF;

    /// <summary>一次注册或注销请求，Done 用于让调用线程等待执行结果。</summary>
    private sealed class HotkeyCommand
    {
        /// <summary>是否注销全部热键。</summary>
        public bool UnregisterAll;

        /// <summary>热键编号。</summary>
        public int Identifier;

        /// <summary>动作标识。</summary>
        public string ActionName = string.Empty;

        /// <summary>主键名称。</summary>
        public string KeyText = string.Empty;

        /// <summary>是否需要控制键。</summary>
        public bool Control;

        /// <summary>是否需要替换键。</summary>
        public bool Alt;

        /// <summary>是否需要上档键。</summary>
        public bool Shift;

        /// <summary>触发回调。</summary>
        public Action? Callback;

        /// <summary>执行结果。</summary>
        public TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>();
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int identifier, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int identifier);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr windowHandle,
        uint minimum, uint maximum, uint remove);

    private readonly ConcurrentQueue<HotkeyCommand> commands = new ConcurrentQueue<HotkeyCommand>();
    private readonly Dictionary<int, Action> callbacks = new Dictionary<int, Action>();
    private readonly Dictionary<int, string> actionNames = new Dictionary<int, string>();
    private readonly AutoResetEvent threadReady = new AutoResetEvent(false);
    private readonly object startLock = new object();
    private Thread? messageThread;
    private volatile bool running;
    private int nextIdentifier = 1;

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public bool Register(string actionName, string keyText, bool ctrl, bool alt, bool shift, Action callback)
    {
        this.EnsureMessageThread();
        HotkeyCommand command = new HotkeyCommand();
        command.ActionName = actionName;
        command.KeyText = keyText;
        command.Control = ctrl;
        command.Alt = alt;
        command.Shift = shift;
        command.Callback = callback;
        command.Identifier = this.AllocateIdentifier();
        this.commands.Enqueue(command);
        if (!command.Done.Task.Wait(3000))
        {
            return false;
        }
        return command.Done.Task.Result;
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        if (this.messageThread == null)
        {
            return;
        }
        HotkeyCommand command = new HotkeyCommand();
        command.UnregisterAll = true;
        this.commands.Enqueue(command);
        command.Done.Task.Wait(3000);
    }

    /// <summary>完成注册请求：只允许在消息线程内调用。</summary>
    private bool ExecuteRegister(HotkeyCommand command)
    {
        uint virtualKey = ToVirtualKey(command.KeyText);
        if (virtualKey == 0)
        {
            return false;
        }
        uint modifiers = ModifierNoRepeat;
        if (command.Control)
        {
            modifiers = modifiers | ModifierControl;
        }
        if (command.Alt)
        {
            modifiers = modifiers | ModifierAlt;
        }
        if (command.Shift)
        {
            modifiers = modifiers | ModifierShift;
        }
        if (!RegisterHotKey(IntPtr.Zero, command.Identifier, modifiers, virtualKey))
        {
            return false;
        }
        if (command.Callback != null)
        {
            this.callbacks[command.Identifier] = command.Callback;
        }
        this.actionNames[command.Identifier] = command.ActionName;
        return true;
    }

    /// <summary>注销全部热键：只允许在消息线程内调用。</summary>
    private void ExecuteUnregisterAll()
    {
        foreach (int identifier in this.actionNames.Keys)
        {
            UnregisterHotKey(IntPtr.Zero, identifier);
        }
        this.callbacks.Clear();
        this.actionNames.Clear();
    }

    /// <summary>消息线程：轮询线程消息队列，处理 WM_HOTKEY 与注册命令。</summary>
    private void RunMessageLoop()
    {
        this.running = true;
        this.threadReady.Set();
        while (this.running)
        {
            bool processedMessage = false;
            NativeMessage message;
            while (PeekMessage(out message, IntPtr.Zero, 0, 0, PeekMessageRemove))
            {
                processedMessage = true;
                if (message.Message == WindowMessageHotkey)
                {
                    int identifier = message.ParameterW.ToInt32();
                    Action? callback = null;
                    if (this.callbacks.TryGetValue(identifier, out Action? registered))
                    {
                        callback = registered;
                    }
                    if (callback != null)
                    {
                        // 热键回调转入线程池，避免阻塞消息循环导致后续热键延迟
                        ThreadPool.QueueUserWorkItem(delegate (object? state)
                        {
                            try
                            {
                                callback();
                            }
                            catch
                            {
                                // 回调异常不能终止消息线程
                            }
                        });
                    }
                }
            }

            HotkeyCommand? command;
            while (this.commands.TryDequeue(out command))
            {
                bool result = false;
                if (command.UnregisterAll)
                {
                    this.ExecuteUnregisterAll();
                    result = true;
                }
                else
                {
                    result = this.ExecuteRegister(command);
                }
                command.Done.TrySetResult(result);
            }

            if (!processedMessage)
            {
                Thread.Sleep(10);
            }
        }
        this.ExecuteUnregisterAll();
    }

    /// <summary>确保消息线程已经启动且完成初始化。</summary>
    private void EnsureMessageThread()
    {
        if (this.messageThread != null)
        {
            return;
        }
        lock (this.startLock)
        {
            if (this.messageThread != null)
            {
                return;
            }
            this.messageThread = new Thread(new ThreadStart(this.RunMessageLoop));
            this.messageThread.IsBackground = true;
            this.messageThread.Name = "全局快捷键消息线程";
            this.messageThread.Start();
            this.threadReady.WaitOne(2000);
        }
    }

    private int AllocateIdentifier()
    {
        int identifier = Interlocked.Increment(ref this.nextIdentifier);
        if (identifier > MaximumHotkeyIdentifier)
        {
            Interlocked.Exchange(ref this.nextIdentifier, 1);
            identifier = 1;
        }
        return identifier;
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
            default:
                return 0;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.running = false;
        if (this.messageThread != null)
        {
            this.messageThread.Join(1000);
            this.messageThread = null;
        }
        this.threadReady.Dispose();
    }
}
