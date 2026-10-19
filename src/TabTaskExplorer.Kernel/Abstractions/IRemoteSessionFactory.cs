using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 远程会话工厂：按连接配置打开终端、远程文件系统与传输服务。
/// 协议差异（SSH / FTP / 未来扩展）收敛在协议适配工程，应用层只依赖本接口。
/// </summary>
public interface IRemoteSessionFactory
{
    /// <summary>打开远程终端会话。credential 为已解密的明文凭据。</summary>
    ITerminalSession OpenTerminal(RemoteConnectionProfile profile, string credential, int columns, int rows);

    /// <summary>打开远程文件系统提供者（路径为远端风格的绝对路径，例如 /home/user）。</summary>
    IFileSystemProvider OpenFileSystem(RemoteConnectionProfile profile, string credential);

    /// <summary>打开远程传输服务（上传 / 下载队列，支持暂停、继续、断点续传）。</summary>
    IRemoteTransferService OpenTransferService(RemoteConnectionProfile profile, string credential);
}
