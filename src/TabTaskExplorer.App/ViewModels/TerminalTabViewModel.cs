using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 远程终端标签页：承载一个 ITerminalSession 会话，提供输出显示、
/// 行输入发送、会话日志记录（参考 Xshell 会话日志功能）与断开操作。
/// </summary>
public class TerminalTabViewModel : INotifyPropertyChanged
{
    /// <summary>输出缓冲区上限（字符数），超出后从头丢弃，避免长会话占用过多内存。</summary>
    private const int OutputLimitChars = 400000;

    /// <summary>匹配 ANSI 转义序列（CSI 与 OSC）的正则。</summary>
    private static readonly Regex ansiPattern = new Regex(
        "\x1B\\[[0-9;?]*[ -/]*[@-~]|\x1B\\][^\x07]*(\x07|\x1B\\\\)",
        RegexOptions.Compiled);

    private readonly ITerminalSession session;
    private readonly StringBuilder outputBuffer = new StringBuilder();
    private readonly Queue<string> pendingLogLines = new Queue<string>();
    private readonly DispatcherTimer? logTimer;
    private readonly object logGate = new object();
    private string logFilePath = string.Empty;
    private string outputText = string.Empty;
    private string inputText = string.Empty;
    private bool loggingEnabled;
    private bool closed;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>标签页标题。</summary>
    public string Title { get; }

    /// <summary>关联的连接配置编号（同一连接只保留一个终端标签）。</summary>
    public string AttachedProfileId { get; set; } = string.Empty;

    /// <summary>重连请求事件：参数为连接配置编号，由主窗口负责重建会话。</summary>
    public event EventHandler<string>? ReconnectRequested;

    /// <summary>快捷命令列表（一键发送到远端，参考 Xshell 快捷命令）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> QuickCommands { get; } =
        new System.Collections.ObjectModel.ObservableCollection<string>();

    /// <summary>输出区文本（已去除 ANSI 控制序列）。</summary>
    public string OutputText
    {
        get { return this.outputText; }
        private set { this.outputText = value; this.RaisePropertyChanged(nameof(this.OutputText)); }
    }

    /// <summary>输入区文本。</summary>
    public string InputText
    {
        get { return this.inputText; }
        set { this.inputText = value; this.RaisePropertyChanged(nameof(this.InputText)); }
    }

    /// <summary>会话是否已连接。</summary>
    public bool IsConnected
    {
        get { return !this.closed && this.session.IsConnected; }
    }

    /// <summary>是否正在记录会话日志。</summary>
    public bool LoggingEnabled
    {
        get { return this.loggingEnabled; }
        set
        {
            this.loggingEnabled = value;
            this.RaisePropertyChanged(nameof(this.LoggingEnabled));
        }
    }

    /// <summary>当前日志文件完整路径。</summary>
    public string LogFilePath
    {
        get { return this.logFilePath; }
        private set { this.logFilePath = value; this.RaisePropertyChanged(nameof(this.LogFilePath)); }
    }

    /// <summary>使用会话实例创建终端标签页并启动会话。</summary>
    public TerminalTabViewModel(ITerminalSession session, string title, int columns, int rows)
    {
        this.session = session;
        this.Title = title;
        this.session.OutputReceived += this.OnSessionOutput;
        this.session.Closed += this.OnSessionClosed;
        this.session.Start(columns, rows);

        // 日志落盘定时器：每秒把缓冲的输出写入日志文件，避免逐条写盘
        this.logTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, this.OnLogTimerTick);
        this.logTimer.Start();
    }

    /// <summary>发送输入行（回车提交，自动补换行）。</summary>
    public void SendInput()
    {
        if (string.IsNullOrEmpty(this.inputText))
        {
            return;
        }
        this.session.WriteInput(this.inputText + "\n");
        this.InputText = string.Empty;
    }

    /// <summary>发送一条快捷命令（命令后自动回车）。</summary>
    public void SendQuickCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }
        this.session.WriteInput(command.Trim() + "\n");
    }

    /// <summary>请求重连：断开后由主窗口按连接配置重建会话。</summary>
    public void RequestReconnect()
    {
        EventHandler<string>? handler = this.ReconnectRequested;
        if (handler != null)
        {
            handler(this, this.AttachedProfileId);
        }
    }

    /// <summary>清空输出显示（不影响远端会话）。</summary>
    public void ClearOutput()
    {
        this.outputBuffer.Clear();
        this.OutputText = string.Empty;
    }

    /// <summary>断开会话并关闭标签页数据。</summary>
    public void Disconnect()
    {
        this.session.Close();
    }

    /// <summary>切换日志记录：开启时创建本次会话的日志文件。</summary>
    public void ToggleLogging()
    {
        if (this.LoggingEnabled)
        {
            this.LoggingEnabled = false;
            this.FlushPendingLogLines();
            return;
        }

        string safeName = string.Join("_", this.Title.Split(Path.GetInvalidFileNameChars()));
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabTaskExplorer", "logs");
        Directory.CreateDirectory(directory);
        this.LogFilePath = Path.Combine(directory, safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
        this.LoggingEnabled = true;
    }

    /// <summary>释放会话与定时器。</summary>
    public void Dispose()
    {
        this.session.OutputReceived -= this.OnSessionOutput;
        this.session.Closed -= this.OnSessionClosed;
        this.FlushPendingLogLines();
        this.logTimer?.Stop();
        this.session.Dispose();
    }

    /// <summary>会话输出到达：清洗 ANSI 序列后追加缓冲。</summary>
    private void OnSessionOutput(string text)
    {
        string clean = ansiPattern.Replace(text, string.Empty);
        clean = clean.Replace("\r\n", "\n").Replace('\r', '\n');

        Dispatcher.UIThread.Post(delegate
        {
            this.outputBuffer.Append(clean);
            if (this.outputBuffer.Length > OutputLimitChars)
            {
                this.outputBuffer.Remove(0, this.outputBuffer.Length - OutputLimitChars);
            }
            this.OutputText = this.outputBuffer.ToString();
        });

        if (this.LoggingEnabled)
        {
            lock (this.logGate)
            {
                this.pendingLogLines.Enqueue(clean);
            }
        }
    }

    /// <summary>会话关闭：更新连接状态。</summary>
    private void OnSessionClosed()
    {
        Dispatcher.UIThread.Post(delegate
        {
            this.closed = true;
            this.RaisePropertyChanged(nameof(this.IsConnected));
        });
    }

    /// <summary>日志定时器：把缓冲输出写入日志文件。</summary>
    private void OnLogTimerTick(object? sender, EventArgs e)
    {
        this.FlushPendingLogLines();
    }

    /// <summary>把待写日志行追加到日志文件（后台队列 + 定时批量写入）。</summary>
    private void FlushPendingLogLines()
    {
        if (string.IsNullOrEmpty(this.LogFilePath) || !this.LoggingEnabled)
        {
            return;
        }
        List<string> lines = new List<string>();
        lock (this.logGate)
        {
            while (this.pendingLogLines.Count > 0)
            {
                lines.Add(this.pendingLogLines.Dequeue());
            }
        }
        if (lines.Count == 0)
        {
            return;
        }
        try
        {
            File.AppendAllText(this.LogFilePath, string.Concat(lines));
        }
        catch
        {
            // 日志写盘失败不影响终端会话
        }
    }

    /// <summary>触发属性变更通知。</summary>
    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
