using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 连接连通性测试器。内核默认实现做 TCP 端口可达性测试；
/// 后续协议适配工程落地后，可替换为带认证握手（SSH / FTP）的完整测试。
/// </summary>
public interface IConnectivityTester
{
    /// <summary>测试指定连接配置的可达性。</summary>
    /// <param name="profile">要测试的连接配置。</param>
    /// <param name="timeoutMilliseconds">超时时间（毫秒）。</param>
    /// <returns>测试结果。</returns>
    ConnectionTestResult Test(RemoteConnectionProfile profile, int timeoutMilliseconds);
}
