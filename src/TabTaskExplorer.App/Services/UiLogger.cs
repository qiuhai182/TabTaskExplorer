using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 界面操作、输入与程序行为日志：只写单个文件，写入上限超出后按队列顶出最旧的记录。
/// 所有写入都在 try 中完成，任何失败都不会影响主程序运行。
/// </summary>
public static class UiLogger
{
    /// <summary>
    /// 单个日志文件的最大字节数：128 兆。超出后从最旧的记录开始顶出，只保留最新的一半容量（64 兆）。
    /// 容量依据高强度使用估算：每条记录平均约 270 字节，普通强度每天约 10000 条（约 2.7 兆），
    /// 保留的 64 兆可追溯约 23 天；即使极端强度每天约 25000 条（约 6.8 兆），也能追溯约 9 天，
    /// 因此可稳定保证追溯到约一周的量。
    /// </summary>
    private const long MaxFileBytes = 128L * 1024 * 1024;

    /// <summary>裁剪时保留的比例分母：只保留最新的一半容量，避免每次写入都触发裁剪。</summary>
    private const int KeepDivisor = 2;

    private static readonly object gate = new object();
    private static string? filePath;
    private static long currentBytes;
    private static bool sizeKnown;

    /// <summary>日志文件路径（程序数据目录下的 ui.log）。</summary>
    public static string FilePath
    {
        get
        {
            lock (gate)
            {
                if (UiLogger.filePath == null)
                {
                    string dataDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "TabTaskExplorer");
                    UiLogger.filePath = Path.Combine(dataDirectory, "ui.log");
                }
                return UiLogger.filePath;
            }
        }
    }

    /// <summary>记录一条界面操作或程序行为日志。时间戳带日期，保证跨天的记录能追溯到具体是哪一天。</summary>
    public static void Log(string category, string message)
    {
        UiLogger.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + category + "] " + message);
    }

    /// <summary>记录一条异常日志：含异常层次、类型、信息与完整堆栈。</summary>
    public static void LogError(string category, string message, Exception error)
    {
        UiLogger.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + category + "] " + message
            + " " + UiLogger.DescribeException(error, 1));
    }

    /// <summary>描述异常：类型、信息与完整堆栈；内部异常最多向下展开三层。</summary>
    private static string DescribeException(Exception error, int depth)
    {
        string text = "异常层次=" + depth + " 异常类型=" + error.GetType().Name
            + " 异常信息=" + error.Message
            + " 异常堆栈=" + UiLogger.Flatten(error.StackTrace);
        if (error.InnerException != null && depth < 3)
        {
            text = text + " " + UiLogger.DescribeException(error.InnerException, depth + 1);
        }
        return text;
    }

    /// <summary>把多行的堆栈文本压成一行，保证一条记录占一行，便于按行裁剪日志。</summary>
    private static string Flatten(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "无";
        }
        return text.Replace("\r\n", " | ").Replace("\n", " | ").Replace("\t", " ");
    }

    /// <summary>
    /// 为一个窗口挂接全局界面操作与输入日志：鼠标与键盘都在隧道阶段捕获，因此不受控件是否已处理事件的影响；
    /// 同时记录窗口打开与关闭，形成完整的程序行为轨迹。
    /// </summary>
    public static void AttachWindow(Window window)
    {
        window.AddHandler(InputElement.PointerPressedEvent, UiLogger.OnPointerPressed, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerReleasedEvent, UiLogger.OnPointerReleased, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.KeyDownEvent, UiLogger.OnKeyDown, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.TextInputEvent, UiLogger.OnTextInput, RoutingStrategies.Tunnel, true);
        // 滚轮在隧道阶段不拦，挂到冒泡末端并忽略已处理标记，用来判断滚轮是否到达窗口、是否被内层控件吞掉
        window.AddHandler(InputElement.PointerWheelChangedEvent, UiLogger.OnPointerWheelChanged, RoutingStrategies.Bubble, true);
        window.Opened += UiLogger.OnWindowOpened;
        window.Closed += UiLogger.OnWindowClosed;
    }

    /// <summary>窗口打开：记录窗口类型与标题，作为程序行为的起点。</summary>
    private static void OnWindowOpened(object? sender, EventArgs e)
    {
        Window? window = sender as Window;
        if (window != null)
        {
            UiLogger.Log("程序行为", "窗口打开：" + UiLogger.DescribeWindow(window));
        }
    }

    /// <summary>窗口关闭：记录窗口类型与标题，作为程序行为的终点。</summary>
    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        Window? window = sender as Window;
        if (window != null)
        {
            UiLogger.Log("程序行为", "窗口关闭：" + UiLogger.DescribeWindow(window));
        }
    }

    /// <summary>描述窗口：类型名与标题，便于在日志中区分是哪一个窗口。</summary>
    private static string DescribeWindow(Window window)
    {
        string title = window.Title;
        if (string.IsNullOrEmpty(title))
        {
            title = "无标题";
        }
        return window.GetType().Name + "(标题=" + title + ")";
    }

    /// <summary>文本输入：记录实际输入的字符与接收输入的控件，中文输入法的成字同样会被记录。</summary>
    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        string text = e.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        string source = UiLogger.DescribeSource(e.Source);
        if (UiLogger.IsSensitiveInput(e.Source))
        {
            // 密码框内容不落日志：只记录控件来源与脱敏标记
            UiLogger.Log("键盘输入", "输入文本=*** 来源=" + source + " 结果=密码框内容已脱敏");
            return;
        }
        UiLogger.Log("键盘输入", "输入文本=" + text + " 来源=" + source);
    }

    /// <summary>判断输入控件是否为敏感输入框：按控件名识别密码框与密钥框，避免明文写入日志。</summary>
    private static bool IsSensitiveInput(object? source)
    {
        // 事件来源可能是 TextBox 内部的文本呈现控件，向上最多查找 8 层父控件
        Control? current = source as Control;
        int depth = 0;
        while (current != null && depth < 8)
        {
            TextBox? box = current as TextBox;
            if (box != null && !string.IsNullOrEmpty(box.Name))
            {
                string name = box.Name ?? string.Empty;
                if (name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            current = current.Parent as Control;
            depth++;
        }
        return false;
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        UiLogger.LogPointer(sender, e, "鼠标按下");
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        UiLogger.LogPointer(sender, e, "鼠标松开");
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        UiLogger.Log("键盘按下", "按键=" + e.Key + " 修饰键=" + UiLogger.DescribeModifiers(e.KeyModifiers) + " 来源=" + UiLogger.DescribeSource(e.Source));
    }

    /// <summary>记录一次鼠标滚轮：写明滚动增量、落点控件，以及事件是否已被内层控件处理（已处理说明外层滚动容器收不到）。</summary>
    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        UiLogger.Log("鼠标滚轮", "滚动增量=" + e.Delta.Y
            + " 来源=" + UiLogger.DescribeSource(e.Source)
            + " 是否已被内层控件处理=" + e.Handled);
    }

    private static void LogPointer(object? sender, PointerEventArgs e, string action)
    {
        string button = UiLogger.DescribeButton(e);
        string position = "未知";
        Window? window = sender as Window;
        if (window != null)
        {
            Point point = e.GetPosition(window);
            position = "窗口内(" + Math.Round(point.X) + "," + Math.Round(point.Y) + ")";
        }
        UiLogger.Log(action, "目标=" + UiLogger.DescribeSource(e.Source) + " 位置=" + position + " 按键=" + button);
    }

    /// <summary>描述触发事件的控件：类型名与控件名，便于在日志里定位具体按钮。</summary>
    private static string DescribeSource(object? source)
    {
        Control? control = source as Control;
        if (control == null)
        {
            if (source == null)
            {
                return "无来源";
            }
            return source.GetType().Name;
        }
        // 事件来源常是按钮内部的图片或文本，此处向上找到第一个带名称、内容或提示的控件，便于定位具体按钮
        Control? current = control;
        int depth = 0;
        while (current != null && depth < 8)
        {
            string? detail = DescribeControl(current);
            if (detail != null)
            {
                return current.GetType().Name + "(" + detail + ")";
            }
            current = current.Parent as Control;
            depth++;
        }
        return control.GetType().Name + "(未命名)";
    }

    /// <summary>读取单个控件的可辨识信息：控件名、文本内容或提示文字，都没有时返回 null。</summary>
    private static string? DescribeControl(Control control)
    {
        if (!string.IsNullOrEmpty(control.Name))
        {
            return control.Name;
        }
        ContentControl? contentControl = control as ContentControl;
        if (contentControl != null && contentControl.Content is string contentText && contentText.Length > 0)
        {
            return "内容=" + contentText;
        }
        object? tip = ToolTip.GetTip(control);
        if (tip != null)
        {
            return "提示=" + tip;
        }
        return null;
    }

    /// <summary>描述鼠标按键，明确写出左键、右键、中键或松开。</summary>
    private static string DescribeButton(PointerEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(null);
        PointerUpdateKind kind = point.Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.LeftButtonPressed || kind == PointerUpdateKind.LeftButtonReleased)
        {
            return kind == PointerUpdateKind.LeftButtonPressed ? "左键按下" : "左键松开";
        }
        if (kind == PointerUpdateKind.RightButtonPressed || kind == PointerUpdateKind.RightButtonReleased)
        {
            return kind == PointerUpdateKind.RightButtonPressed ? "右键按下" : "右键松开";
        }
        if (kind == PointerUpdateKind.MiddleButtonPressed || kind == PointerUpdateKind.MiddleButtonReleased)
        {
            return kind == PointerUpdateKind.MiddleButtonPressed ? "中键按下" : "中键松开";
        }
        return kind.ToString();
    }

    /// <summary>描述按下的修饰键，逐个写出，不使用缩写。</summary>
    private static string DescribeModifiers(KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.None)
        {
            return "无";
        }
        string text = string.Empty;
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            text += "Ctrl ";
        }
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            text += "Alt ";
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            text += "Shift ";
        }
        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            text += "Win ";
        }
        return text.Trim();
    }

    private static void WriteLine(string line)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
            lock (UiLogger.gate)
            {
                string path = UiLogger.FilePath;
                string? directory = Path.GetDirectoryName(path);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                if (!UiLogger.sizeKnown)
                {
                    UiLogger.currentBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                    UiLogger.sizeKnown = true;
                }
                using (FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
                UiLogger.currentBytes = UiLogger.currentBytes + bytes.Length;
                if (UiLogger.currentBytes > MaxFileBytes)
                {
                    UiLogger.TrimOldest(path);
                }
            }
        }
        catch
        {
            // 日志写入失败不能影响主程序运行
        }
    }

    /// <summary>
    /// 文件超出上限时按队列方式顶出最旧的记录：只保留最新的一半容量。
    /// 实现上只读取文件尾部目标区间的字节并整行搬移，不把整个大文件读成行数组，避免上百兆文件造成内存尖峰。
    /// </summary>
    private static void TrimOldest(string path)
    {
        long keepLimit = MaxFileBytes / KeepDivisor;
        long length = new FileInfo(path).Length;
        long startOffset = length - keepLimit;
        if (startOffset < 0)
        {
            startOffset = 0;
        }

        string tempPath = path + ".trim";
        long keptBytes = 0;
        using (FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (FileStream target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (startOffset > 0)
            {
                source.Seek(startOffset, SeekOrigin.Begin);
                // 起点可能落在半条记录中间，向后跳过残行，保证顶出后第一行是完整记录
                int value = source.ReadByte();
                while (value != -1 && value != '\n')
                {
                    value = source.ReadByte();
                }
            }
            source.CopyTo(target);
            keptBytes = target.Length;
        }

        File.Move(tempPath, path, true);
        UiLogger.currentBytes = keptBytes;
    }
}
