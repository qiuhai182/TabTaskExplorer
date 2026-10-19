namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 终端会话抽象：远程终端与本机终端共用的双向通道。
/// 应用层只依赖本接口，协议实现（SSH Shell、ConPTY、PTY）由各适配工程提供。
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>远端输出到达时触发，参数为按会话编码解码后的文本。</summary>
    event Action<string>? OutputReceived;

    /// <summary>会话结束（远端关闭或发生错误）时触发。</summary>
    event Action? Closed;

    /// <summary>会话是否处于已连接状态。</summary>
    bool IsConnected { get; }

    /// <summary>以指定终端尺寸启动会话。</summary>
    void Start(int columns, int rows);

    /// <summary>向远端发送用户输入。</summary>
    void WriteInput(string text);

    /// <summary>把终端窗口尺寸同步到远端。</summary>
    void Resize(int columns, int rows);

    /// <summary>主动关闭会话。</summary>
    void Close();
}
