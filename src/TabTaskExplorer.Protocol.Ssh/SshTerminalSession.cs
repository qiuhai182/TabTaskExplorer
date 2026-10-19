using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Protocol.Ssh;

/// <summary>
/// 基于 SSH ShellStream 的远程终端会话。
/// 输出按 UTF-8 解码后回调给界面层，窗口尺寸变化同步到远端 pty。
/// </summary>
public class SshTerminalSession : ITerminalSession
{
    private readonly SshSessionFactory factory;
    private readonly string profileId;
    private readonly SshClient client;
    private ShellStream? stream;

    /// <inheritdoc />
    public event Action<string>? OutputReceived;

    /// <inheritdoc />
    public event Action? Closed;

    /// <inheritdoc />
    public bool IsConnected
    {
        get { return this.stream != null && this.client.IsConnected; }
    }

    /// <summary>使用已连接的 SSH 客户端创建终端会话。</summary>
    public SshTerminalSession(SshSessionFactory factory, string profileId, SshClient client, int columns, int rows)
    {
        this.factory = factory;
        this.profileId = profileId;
        this.client = client;
        this.InitialColumns = columns;
        this.InitialRows = rows;
    }

    /// <summary>启动时使用的终端列数。</summary>
    public int InitialColumns { get; }

    /// <summary>启动时使用的终端行数。</summary>
    public int InitialRows { get; }

    /// <inheritdoc />
    public void Start(int columns, int rows)
    {
        if (this.stream != null)
        {
            return;
        }
        ShellStream shell = this.client.CreateShellStream(
            "xterm-256color", (uint)columns, (uint)rows, 0, 0, 1024);
        shell.DataReceived += this.OnShellData;
        shell.Closed += this.OnShellClosed;
        shell.ErrorOccurred += this.OnShellError;
        this.stream = shell;
    }

    /// <inheritdoc />
    public void WriteInput(string text)
    {
        ShellStream? shell = this.stream;
        if (shell != null && shell.CanWrite)
        {
            shell.Write(text);
            shell.Flush();
        }
    }

    /// <inheritdoc />
    public void Resize(int columns, int rows)
    {
        ShellStream? shell = this.stream;
        if (shell != null)
        {
            shell.ChangeWindowSize((uint)columns, (uint)rows, 0, 0);
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        ShellStream? shell = this.stream;
        this.stream = null;
        if (shell != null)
        {
            shell.DataReceived -= this.OnShellData;
            shell.Closed -= this.OnShellClosed;
            shell.ErrorOccurred -= this.OnShellError;
            try
            {
                shell.Dispose();
            }
            catch
            {
                // 会话可能已被远端关闭
            }
        }
        this.factory.CloseClient(this.profileId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Close();
    }

    /// <summary>ShellStream 输出到达：解码后转发给界面层。</summary>
    private void OnShellData(object? sender, ShellDataEventArgs e)
    {
        string text = Encoding.UTF8.GetString(e.Data);
        Action<string>? handler = this.OutputReceived;
        if (handler != null)
        {
            handler(text);
        }
    }

    /// <summary>远端正常关闭会话。</summary>
    private void OnShellClosed(object? sender, EventArgs e)
    {
        Action? handler = this.Closed;
        if (handler != null)
        {
            handler();
        }
    }

    /// <summary>会话发生错误：视为关闭并通知界面层。</summary>
    private void OnShellError(object? sender, ExceptionEventArgs e)
    {
        Action? handler = this.Closed;
        if (handler != null)
        {
            handler();
        }
    }
}
