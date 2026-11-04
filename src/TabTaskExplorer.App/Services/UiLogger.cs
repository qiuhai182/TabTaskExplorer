using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TabTaskExplorer.App.Services;

/// <summary>日志级别：数值越大越严重；低于当前最低级别的记录直接丢弃，不写文件。</summary>
public enum LogLevel
{
    /// <summary>跟踪：最细粒度的输入与事件轨迹。</summary>
    Trace = 0,

    /// <summary>调试：排查问题时打开的详细记录。</summary>
    Debug = 1,

    /// <summary>信息：正常运行过程中的关键行为。</summary>
    Info = 2,

    /// <summary>警告：不中断流程但需要关注的异常情况。</summary>
    Warn = 3,

    /// <summary>错误：操作失败或捕获到异常。</summary>
    Error = 4,
}

/// <summary>日志分类标签：固定六个业务分类，另保留系统、启动、配置与诊断四个辅助分类。</summary>
public static class LogCategories
{
    /// <summary>截图、录屏、贴图与屏幕取色。</summary>
    public const string Screenshot = "截图";

    /// <summary>文件浏览、文件操作、搜索、压缩包、比对与编辑器。</summary>
    public const string File = "文件";

    /// <summary>远程连接、SSH / FTP 会话与传输队列。</summary>
    public const string Remote = "远程";

    /// <summary>网页端服务、权限校验与网页页面交互。</summary>
    public const string Web = "Web";

    /// <summary>界面操作、窗口、输入与用户交互。</summary>
    public const string Ui = "UI";

    /// <summary>性能采样、进程监控与耗时统计。</summary>
    public const string Performance = "性能";

    /// <summary>程序生命周期、启动项、崩溃与系统级事件。</summary>
    public const string System = "系统";

    /// <summary>程序启动与退出。</summary>
    public const string Startup = "启动";

    /// <summary>配置读取、修改与保存。</summary>
    public const string Config = "配置";

    /// <summary>日志查看器与诊断包。</summary>
    public const string Diagnostics = "诊断";

    /// <summary>
    /// 把历史分类名规整为固定分类标签：无法识别时原样保留，避免丢失既有分类信息。
    /// </summary>
    public static string Normalize(string category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return Ui;
        }
        switch (category)
        {
            case "截图":
            case "录屏":
            case "截图录屏":
            case "截图与录屏":
            case "贴图":
            case "屏幕取色":
                return Screenshot;
            case "文件":
            case "文件操作":
            case "文件列表":
            case "搜索":
            case "全局搜索":
            case "压缩包":
            case "比对":
            case "编辑器":
            case "媒体":
            case "磁盘视图":
                return File;
            case "远程":
            case "SSH":
            case "SFTP":
            case "FTP":
            case "传输":
            case "远程传输":
            case "连接":
                return Remote;
            case "网页端":
            case "网页端服务":
            case "网页端审计":
            case "Web":
                return Web;
            case "程序行为":
            case "键盘输入":
            case "键盘按下":
            case "鼠标按下":
            case "鼠标松开":
            case "鼠标滚轮":
            case "窗口任务栏":
            case "设置窗口":
            case "界面异常":
            case "任务栏":
                return Ui;
            case "性能":
            case "进程监控":
            case "磁盘扫描":
                return Performance;
            case "程序启动":
            case "程序退出":
            case "启动项":
            case "单实例服务":
            case "崩溃":
                return System;
            case "配置更改":
                return Config;
            case "诊断":
                return Diagnostics;
            default:
                return category;
        }
    }
}

/// <summary>
/// 界面操作、输入与程序行为日志：只写单个文件，写入上限超出后按队列顶出最旧的记录。
/// 每条记录包含时间、级别、分类、事件名与完整字段；所有写入都在 try 中完成，
/// 任何失败都不会影响主程序运行。
/// </summary>
public static class UiLogger
{
    /// <summary>单个日志文件的最大字节数：32 兆。超出后从最旧的记录开始顶出，只保留最新的一半容量（16 兆）。</summary>
    internal const long DefaultMaxFileBytes = 32L * 1024 * 1024;

    /// <summary>裁剪时保留的比例分母：只保留最新的一半容量，避免每次写入都触发裁剪。</summary>
    private const int KeepDivisor = 2;

    /// <summary>保留的日志文件份数：启动时按日期从新到旧保留该数量，超出的旧文件删除。</summary>
    private const int RetentionFileCount = 7;

    /// <summary>保留日志文件的总字节上限：256 兆，超出时从最旧的文件开始删除。</summary>
    private const long MaxTotalBytes = 256L * 1024 * 1024;

    /// <summary>日志文件名前缀：日期部分紧随其后。</summary>
    private const string LogFilePrefix = "ui-";

    /// <summary>日志文件名后缀。</summary>
    private const string LogFileSuffix = ".log";

    private static readonly object gate = new object();
    private static string? filePath;
    private static DateTime currentFileDate = DateTime.MinValue;
    private static long currentBytes;
    private static bool sizeKnown;
    private static LogLevel minimumLevel = LogLevel.Info;

    /// <summary>测试用时间源：默认为系统当前时间，测试可替换以验证跨天切换。</summary>
    internal static Func<DateTime> NowProvider = delegate { return DateTime.Now; };

    /// <summary>测试用日志目录覆盖：为空时使用用户数据目录下的 logs。</summary>
    internal static string? LogDirectoryOverride;

    /// <summary>测试用单文件上限覆盖：小于等于零时使用 32 兆默认值。</summary>
    internal static long MaxFileBytesOverride;

    /// <summary>清空测试覆盖并重置当前文件缓存，避免测试之间互相影响。</summary>
    internal static void ResetTestOverrides()
    {
        lock (UiLogger.gate)
        {
            UiLogger.NowProvider = delegate { return DateTime.Now; };
            UiLogger.LogDirectoryOverride = null;
            UiLogger.MaxFileBytesOverride = 0;
            UiLogger.filePath = null;
            UiLogger.currentFileDate = DateTime.MinValue;
            UiLogger.currentBytes = 0;
            UiLogger.sizeKnown = false;
        }
    }

    /// <summary>当前生效的单文件上限：测试覆盖优先，否则为 32 兆。</summary>
    private static long GetMaxFileBytes()
    {
        return UiLogger.MaxFileBytesOverride > 0
            ? UiLogger.MaxFileBytesOverride : UiLogger.DefaultMaxFileBytes;
    }

    /// <summary>当前最低写入级别：低于该级别的记录直接丢弃。</summary>
    public static LogLevel MinimumLevel
    {
        get
        {
            lock (UiLogger.gate)
            {
                return UiLogger.minimumLevel;
            }
        }
    }

    /// <summary>设置最低写入级别；非法值回落到 Info。</summary>
    public static void SetMinimumLevel(LogLevel level)
    {
        lock (UiLogger.gate)
        {
            UiLogger.minimumLevel = level;
        }
    }

    /// <summary>判断指定级别是否会被写入文件。</summary>
    public static bool IsEnabled(LogLevel level)
    {
        lock (UiLogger.gate)
        {
            return level >= UiLogger.minimumLevel;
        }
    }

    /// <summary>把级别名称（英文或中文）解析为枚举；无法识别时返回回落值。</summary>
    public static LogLevel ParseLevel(string? text, LogLevel fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }
        string value = text.Trim();
        if (value.Equals("Trace", StringComparison.OrdinalIgnoreCase) || value == "跟踪")
        {
            return LogLevel.Trace;
        }
        if (value.Equals("Debug", StringComparison.OrdinalIgnoreCase) || value == "调试")
        {
            return LogLevel.Debug;
        }
        if (value.Equals("Info", StringComparison.OrdinalIgnoreCase) || value == "信息")
        {
            return LogLevel.Info;
        }
        if (value.Equals("Warn", StringComparison.OrdinalIgnoreCase) || value.Equals("Warning", StringComparison.OrdinalIgnoreCase) || value == "警告")
        {
            return LogLevel.Warn;
        }
        if (value.Equals("Error", StringComparison.OrdinalIgnoreCase) || value == "错误")
        {
            return LogLevel.Error;
        }
        return fallback;
    }

    /// <summary>把级别枚举转成中文显示名，日志正文与查看器共用同一套文字。</summary>
    public static string DescribeLevel(LogLevel level)
    {
        switch (level)
        {
            case LogLevel.Trace:
                return "跟踪";
            case LogLevel.Debug:
                return "调试";
            case LogLevel.Warn:
                return "警告";
            case LogLevel.Error:
                return "错误";
            default:
                return "信息";
        }
    }

    /// <summary>日志目录（程序数据目录下的 logs 子目录）。</summary>
    public static string LogDirectory
    {
        get
        {
            string? overrideDirectory = UiLogger.LogDirectoryOverride;
            if (!string.IsNullOrEmpty(overrideDirectory))
            {
                return overrideDirectory;
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TabTaskExplorer",
                "logs");
        }
    }

    /// <summary>当天日志文件路径：按日期命名，跨天自动切换到新文件。</summary>
    public static string FilePath
    {
        get
        {
            lock (UiLogger.gate)
            {
                return UiLogger.ResolveCurrentPath(UiLogger.NowProvider());
            }
        }
    }

    /// <summary>按日期计算日志文件路径；日期变化时重置文件大小缓存。</summary>
    private static string ResolveCurrentPath(DateTime now)
    {
        DateTime date = now.Date;
        if (UiLogger.filePath != null && UiLogger.currentFileDate == date)
        {
            return UiLogger.filePath;
        }
        UiLogger.currentFileDate = date;
        UiLogger.filePath = Path.Combine(UiLogger.LogDirectory,
            UiLogger.LogFilePrefix + date.ToString("yyyy-MM-dd") + UiLogger.LogFileSuffix);
        UiLogger.sizeKnown = false;
        return UiLogger.filePath;
    }

    /// <summary>
    /// 启动时清理超出保留策略的旧日志：先按文件份数保留最新的若干份，再按总容量从最旧删除。
    /// 全程在锁内完成且吞掉全部异常，清理失败不影响程序启动。
    /// </summary>
    public static void CleanupOldLogs()
    {
        try
        {
            lock (UiLogger.gate)
            {
                string directory = UiLogger.LogDirectory;
                if (!Directory.Exists(directory))
                {
                    return;
                }
                List<FileInfo> files = new List<FileInfo>();
                foreach (string path in Directory.GetFiles(directory,
                    UiLogger.LogFilePrefix + "*" + UiLogger.LogFileSuffix))
                {
                    try
                    {
                        files.Add(new FileInfo(path));
                    }
                    catch
                    {
                        // 单个文件信息读取失败时跳过，不影响其余文件清理
                    }
                }
                files.Sort(CompareByWriteTimeDescending);
                while (files.Count > RetentionFileCount)
                {
                    DeleteLogFile(files[files.Count - 1]);
                    files.RemoveAt(files.Count - 1);
                }
                long totalBytes = 0;
                foreach (FileInfo file in files)
                {
                    totalBytes = totalBytes + file.Length;
                }
                while (totalBytes > MaxTotalBytes && files.Count > 1)
                {
                    FileInfo oldest = files[files.Count - 1];
                    totalBytes = totalBytes - oldest.Length;
                    DeleteLogFile(oldest);
                    files.RemoveAt(files.Count - 1);
                }
            }
        }
        catch
        {
            // 日志清理失败不能影响主程序启动
        }
    }

    /// <summary>按最后写入时间从新到旧比较两个日志文件。</summary>
    private static int CompareByWriteTimeDescending(FileInfo left, FileInfo right)
    {
        return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
    }

    /// <summary>删除一个旧日志文件；文件被查看器占用等失败情况直接忽略。</summary>
    private static void DeleteLogFile(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
            // 文件被占用或权限不足时保留该文件，下次启动再清理
        }
    }

    /// <summary>
    /// 记录一条信息级日志（兼容旧调用）：分类名自动规整为固定标签，原分类名作为事件名，
    /// 调用方原有的完整中文字段直接保留。
    /// </summary>
    public static void Log(string category, string message)
    {
        UiLogger.WriteRecord(LogLevel.Info, LogCategories.Normalize(category), category, message);
    }

    /// <summary>记录一条跟踪级日志：用于鼠标、键盘等最细粒度的输入轨迹。</summary>
    public static void LogTrace(string category, string eventName, string detail)
    {
        UiLogger.WriteRecord(LogLevel.Trace, LogCategories.Normalize(category), eventName, detail);
    }

    /// <summary>记录一条调试级日志：排查问题时才写入。</summary>
    public static void LogDebug(string category, string eventName, string detail)
    {
        UiLogger.WriteRecord(LogLevel.Debug, LogCategories.Normalize(category), eventName, detail);
    }

    /// <summary>记录一条信息级日志。</summary>
    public static void LogInfo(string category, string eventName, string detail)
    {
        UiLogger.WriteRecord(LogLevel.Info, LogCategories.Normalize(category), eventName, detail);
    }

    /// <summary>记录一条警告级日志：流程继续但需要关注。</summary>
    public static void LogWarn(string category, string eventName, string detail)
    {
        UiLogger.WriteRecord(LogLevel.Warn, LogCategories.Normalize(category), eventName, detail);
    }

    /// <summary>记录一条错误级日志（不含异常对象）。</summary>
    public static void LogError(string category, string eventName, string detail)
    {
        UiLogger.WriteRecord(LogLevel.Error, LogCategories.Normalize(category), eventName, detail);
    }

    /// <summary>记录一条异常日志：含异常层次、类型、信息与完整堆栈。</summary>
    public static void LogError(string category, string message, Exception error)
    {
        UiLogger.WriteRecord(LogLevel.Error, LogCategories.Normalize(category), category,
            message + " " + UiLogger.DescribeException(error, 1));
    }

    /// <summary>记录一条错误日志：自定义事件名与说明，并附加异常层次、类型与完整堆栈。</summary>
    public static void LogError(string category, string eventName, string detail, Exception error)
    {
        UiLogger.WriteRecord(LogLevel.Error, LogCategories.Normalize(category), eventName,
            detail + " " + UiLogger.DescribeException(error, 1));
    }

    /// <summary>
    /// 统一记录入口：低于最低级别直接丢弃；每条记录固定包含时间、级别、分类、事件名与完整字段。
    /// </summary>
    private static void WriteRecord(LogLevel level, string category, string eventName, string detail)
    {
        lock (UiLogger.gate)
        {
            if (level < UiLogger.minimumLevel)
            {
                return;
            }
        }
        string resolvedCategory = category;
        string resolvedEventName = eventName;
        if (string.IsNullOrEmpty(resolvedCategory))
        {
            resolvedCategory = LogCategories.Ui;
        }
        if (string.IsNullOrEmpty(resolvedEventName))
        {
            resolvedEventName = resolvedCategory;
        }
        // 写入前统一脱敏：密码、密钥、令牌、Authorization 头与 URL 内嵌凭据一律替换为 ***
        string sanitizedDetail = LogSanitizer.MaskSecrets(detail);
        string line = UiLogger.NowProvider().ToString("yyyy-MM-dd HH:mm:ss.fff")
            + " [" + UiLogger.DescribeLevel(level) + "]"
            + " [分类=" + resolvedCategory + "]"
            + " [事件=" + resolvedEventName + "]"
            + " " + sanitizedDetail;
        UiLogger.WriteLine(line);
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
            UiLogger.LogInfo(LogCategories.Ui, "窗口打开", "窗口=" + UiLogger.DescribeWindow(window));
        }
    }

    /// <summary>窗口关闭：记录窗口类型与标题，作为程序行为的终点。</summary>
    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        Window? window = sender as Window;
        if (window != null)
        {
            UiLogger.LogInfo(LogCategories.Ui, "窗口关闭", "窗口=" + UiLogger.DescribeWindow(window));
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
            UiLogger.LogDebug(LogCategories.Ui, "文本输入", "输入文本=*** 来源=" + source + " 结果=密码框内容已脱敏");
            return;
        }
        UiLogger.LogDebug(LogCategories.Ui, "文本输入", "输入文本=" + text + " 来源=" + source);
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
        UiLogger.LogDebug(LogCategories.Ui, "键盘按下", "按键=" + e.Key + " 修饰键=" + UiLogger.DescribeModifiers(e.KeyModifiers) + " 来源=" + UiLogger.DescribeSource(e.Source));
    }

    /// <summary>记录一次鼠标滚轮：写明滚动增量、落点控件，以及事件是否已被内层控件处理（已处理说明外层滚动容器收不到）。</summary>
    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        UiLogger.LogDebug(LogCategories.Ui, "鼠标滚轮", "滚动增量=" + e.Delta.Y
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
        UiLogger.LogDebug(LogCategories.Ui, action,
            "目标=" + UiLogger.DescribeSource(e.Source) + " 位置=" + position + " 按键=" + button);
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
                string path = UiLogger.ResolveCurrentPath(UiLogger.NowProvider());
                string directory = UiLogger.LogDirectory;
                if (!Directory.Exists(directory))
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
                if (UiLogger.currentBytes > UiLogger.GetMaxFileBytes())
                {
                    UiLogger.TrimOldest(path, UiLogger.GetMaxFileBytes());
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
    private static void TrimOldest(string path, long maxFileBytes)
    {
        long keepLimit = maxFileBytes / KeepDivisor;
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
