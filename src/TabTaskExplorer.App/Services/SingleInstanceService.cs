using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 单实例服务：命名互斥量保证同一时间只运行一个桌面程序实例，
/// 第二个实例通过命名管道把系统唤起的路径转发给既有实例后退出。
/// 互斥量与命名管道均为 .NET 跨平台能力，Windows / Linux / macOS 行为一致。
/// </summary>
public static class SingleInstanceService
{
    /// <summary>互斥量名称（跨进程唯一标识）。</summary>
    private const string MutexName = "TabTaskExplorer_DesktopSingleInstance";

    /// <summary>命名管道名称（命令转发通道）。</summary>
    private const string PipeName = "TabTaskExplorer_CommandPipe";

    /// <summary>管道协议应答：既有实例已收到命令。</summary>
    private const string ReplyOk = "OK";

    /// <summary>转发重试次数（既有实例可能还在启动中）。</summary>
    private const int SendRetryCount = 10;

    /// <summary>转发重试间隔（毫秒）。</summary>
    private const int SendRetryIntervalMilliseconds = 500;

    /// <summary>主实例持有的互斥量句柄：保存在静态字段中，保证整个进程生命周期不释放。</summary>
    private static Mutex? primaryMutex;

    /// <summary>命令处理器：由应用启动时注入，管道收到消息后回 UI 线程调用。</summary>
    private static Action<string>? commandHandler;

    /// <summary>尝试成为主实例：成功时持有互斥量并返回 true；已有实例运行时返回 false。</summary>
    public static bool AcquirePrimary()
    {
        bool createdNew;
        Mutex mutex = new Mutex(true, MutexName, out createdNew);
        if (createdNew)
        {
            primaryMutex = mutex;
            UiLogger.Log("单实例服务", "获取单实例互斥量成功：互斥量名称=" + MutexName + "，本进程作为主实例运行");
            return true;
        }
        // 已有实例持有互斥量：立即释放本句柄，不取得所有权
        mutex.Dispose();
        return false;
    }

    /// <summary>
    /// 作为第二实例执行转发：把启动参数经命名管道发给既有实例。
    /// 转发成功返回 true（本进程退出）；转发失败且成功抢占到互斥量时返回 false（降级为正常主实例启动，保证唤起请求不丢失）。
    /// </summary>
    public static bool TryRunAsSecondary(string[] args)
    {
        string message = BuildMessage(args);
        for (int attempt = 1; attempt <= SendRetryCount; attempt++)
        {
            if (TrySendMessage(message, attempt))
            {
                return true;
            }
            Thread.Sleep(SendRetryIntervalMilliseconds);
        }
        // 转发始终失败：尝试抢占互斥量，抢到说明既有实例正在退出，则降级为正常启动
        UiLogger.Log("单实例服务", "启动参数转发连续失败，转入抢占互斥量降级判定：重试次数=" + SendRetryCount
            + " 重试间隔毫秒=" + SendRetryIntervalMilliseconds + " 最终消息内容=" + message.Replace("\n", " | "));
        return !AcquirePrimary();
    }

    /// <summary>启动命令管道服务端：在后台任务中循环等待连接，收到消息后回 UI 线程交由处理器执行。</summary>
    public static void StartServer(Action<string> onCommand)
    {
        commandHandler = onCommand;
        Task.Run(new Action(RunServerLoop));
        UiLogger.Log("单实例服务", "命令管道服务端已启动：管道名称=" + PipeName);
    }

    /// <summary>把启动参数组装为管道消息：有路径为 OPEN + 换行分隔的路径，无路径为聚焦命令。</summary>
    private static string BuildMessage(string[] args)
    {
        List<string> paths = new List<string>();
        foreach (string arg in args)
        {
            if (!string.IsNullOrWhiteSpace(arg))
            {
                paths.Add(arg.Trim());
            }
        }
        if (paths.Count == 0)
        {
            return "FOCUS";
        }
        StringBuilder builder = new StringBuilder("OPEN");
        foreach (string path in paths)
        {
            builder.Append('\n');
            builder.Append(path);
        }
        return builder.ToString();
    }

    /// <summary>
    /// 向既有实例发送一条消息：4 字节小端长度前缀 + UTF-8 消息正文，随后等待 OK 应答。
    /// 连接或应答失败返回 false，并记录失败原因与第几次尝试。
    /// </summary>
    private static bool TrySendMessage(string message, int attempt)
    {
        try
        {
            using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
            {
                client.Connect(1000);
                byte[] payload = Encoding.UTF8.GetBytes(message);
                byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);
                client.Write(lengthPrefix, 0, lengthPrefix.Length);
                client.Write(payload, 0, payload.Length);
                client.Flush();

                byte[] replyBuffer = new byte[2];
                int replyLength = 0;
                while (replyLength < replyBuffer.Length)
                {
                    int read = client.Read(replyBuffer, replyLength, replyBuffer.Length - replyLength);
                    if (read <= 0)
                    {
                        break;
                    }
                    replyLength += read;
                }
                string reply = Encoding.UTF8.GetString(replyBuffer, 0, replyLength);
                if (string.Equals(reply, ReplyOk, StringComparison.Ordinal))
                {
                    UiLogger.Log("单实例服务", "启动参数已转发给既有实例：管道名称=" + PipeName
                        + " 第几次尝试=" + attempt + " 消息内容=" + message.Replace("\n", " | "));
                    return true;
                }
                UiLogger.Log("单实例服务", "转发应答不符合预期，判定为本次转发失败：管道名称=" + PipeName
                    + " 第几次尝试=" + attempt + " 应答内容=" + (reply.Length == 0 ? "空" : reply));
                return false;
            }
        }
        catch (Exception error)
        {
            // 管道不存在（既有实例尚未就绪）或连接中断：由调用方重试
            UiLogger.Log("单实例服务", "转发连接失败，等待后重试：管道名称=" + PipeName
                + " 第几次尝试=" + attempt + " 共需尝试次数=" + SendRetryCount
                + " 错误类型=" + error.GetType().Name + " 错误信息=" + error.Message);
            return false;
        }
    }

    /// <summary>服务端主循环：等待连接 → 读取消息 → 回 UI 线程处理 → 应答 OK，异常后稍等继续。</summary>
    private static void RunServerLoop()
    {
        while (true)
        {
            try
            {
                using (NamedPipeServerStream server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    server.WaitForConnection();
                    byte[] lengthBuffer = ReadExact(server, 4);
                    int payloadLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (payloadLength <= 0 || payloadLength > 1024 * 1024)
                    {
                        // 非法长度：不处理，断开等待下一次连接
                        UiLogger.Log("单实例服务", "命令管道收到非法消息长度，已丢弃并等待下次连接：管道名称=" + PipeName
                            + " 消息字节数=" + payloadLength + " 允许上限字节数=" + (1024 * 1024));
                        continue;
                    }
                    byte[] payload = ReadExact(server, payloadLength);
                    string message = Encoding.UTF8.GetString(payload);
                    UiLogger.Log("单实例服务", "命令管道收到消息：管道名称=" + PipeName
                        + " 消息字节数=" + payloadLength + " 消息内容=" + message.Replace("\n", " | "));
                    DispatchCommand(message);
                    byte[] reply = Encoding.UTF8.GetBytes(ReplyOk);
                    server.Write(reply, 0, reply.Length);
                    server.Flush();
                }
            }
            catch (Exception error)
            {
                UiLogger.LogError("单实例服务", "命令管道服务循环异常，稍后继续等待连接", error);
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>把消息投递到 UI 线程执行（应用启动后 commandHandler 已就绪）。</summary>
    private static void DispatchCommand(string message)
    {
        Action<string>? handler = commandHandler;
        if (handler == null)
        {
            UiLogger.Log("单实例服务", "收到命令但处理器尚未就绪，已忽略：消息内容=" + message.Replace("\n", " | "));
            return;
        }
        Dispatcher.UIThread.Post(OnCommandPosted, message, DispatcherPriority.Normal);
    }

    /// <summary>UI 线程上的命令回调（Post 的状态参数即管道消息原文）。</summary>
    private static void OnCommandPosted(object? state)
    {
        Action<string>? handler = commandHandler;
        if (handler != null && state is string message)
        {
            handler(message);
        }
    }

    /// <summary>从流中精确读取指定字节数，不足时抛出异常（连接被提前关闭）。</summary>
    private static byte[] ReadExact(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
            {
                throw new IOException("单实例管道连接被提前关闭：期望字节数=" + count + " 已读字节数=" + offset);
            }
            offset += read;
        }
        return buffer;
    }
}
