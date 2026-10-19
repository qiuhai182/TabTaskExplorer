using System.Diagnostics;
using System.Net.Sockets;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 基于 TCP 的连通性测试默认实现，跨平台可用：只验证主机端口可达，不做协议认证握手。
/// </summary>
public class TcpConnectivityTester : IConnectivityTester
{
    /// <summary>默认测试超时时间（毫秒）。</summary>
    public const int DefaultTimeoutMilliseconds = 5000;

    public ConnectionTestResult Test(RemoteConnectionProfile profile, int timeoutMilliseconds)
    {
        ConnectionTestResult result = new ConnectionTestResult();
        if (profile == null || string.IsNullOrWhiteSpace(profile.Host))
        {
            result.Message = "主机地址为空，请先填写主机";
            return result;
        }

        string host = profile.Host.Trim();
        int port = profile.GetEffectivePort();
        if (timeoutMilliseconds <= 0)
        {
            timeoutMilliseconds = DefaultTimeoutMilliseconds;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            using TcpClient client = new TcpClient();
            Task connectTask = client.ConnectAsync(host, port);
            if (!connectTask.Wait(timeoutMilliseconds))
            {
                result.Message = "连接超时（超过 " + timeoutMilliseconds + " 毫秒）";
                return result;
            }
            stopwatch.Stop();
            result.Success = true;
            result.LatencyMilliseconds = (int)stopwatch.ElapsedMilliseconds;
            result.Message = host + ":" + port + " 可达，耗时 " + result.LatencyMilliseconds + " 毫秒";
            return result;
        }
        catch (AggregateException wrapped)
        {
            stopwatch.Stop();
            SocketException? socketError = wrapped.InnerException as SocketException;
            if (socketError != null)
            {
                result.Message = "连接失败：" + DescribeSocketError(socketError) + "（" + host + ":" + port + "）";
            }
            else
            {
                result.Message = "连接失败：" + wrapped.InnerException?.Message;
            }
            return result;
        }
        catch (Exception error)
        {
            stopwatch.Stop();
            result.Message = "连接失败：" + error.Message;
            return result;
        }
    }

    /// <summary>把套接字错误码翻译为中文说明。</summary>
    private static string DescribeSocketError(SocketException error)
    {
        switch (error.SocketErrorCode)
        {
            case SocketError.HostNotFound:
                return "找不到主机，请检查主机地址";
            case SocketError.ConnectionRefused:
                return "目标端口拒绝连接，请检查端口与远程服务状态";
            case SocketError.NetworkUnreachable:
                return "网络不可达，请检查本机网络";
            case SocketError.TimedOut:
                return "连接超时";
            default:
                return error.Message;
        }
    }
}
