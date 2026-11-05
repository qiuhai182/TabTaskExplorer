using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 远程连接失败原因翻译：把 SSH.NET / 网络异常翻成可读中文，并标注失败层次。
/// 桌面端与网页端共用，避免两套提示口径不一致。
/// </summary>
public static class RemoteFailureText
{
    /// <summary>把底层异常翻译成用户能看懂的中文失败原因。</summary>
    public static string Describe(Exception error, RemoteConnectionProfile profile)
    {
        string host = profile.Host + ":" + profile.GetEffectivePort();
        string typeName = error.GetType().Name;
        string message = error.Message ?? string.Empty;
        if (typeName == "SshOperationTimeoutException"
            || message.IndexOf("Connection failed to establish", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "网络不可达：连接 " + host + " 在 15 秒内 TCP 超时。请检查网络、防火墙或主机是否在线。";
        }
        if (typeName == "SshAuthenticationException"
            || message.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("No more authentication methods", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "认证失败：用户名/密码或私钥被拒绝（" + message + "）。请确认账号密码是否正确；"
                + "如果该连接来自 Xshell / Xftp 导入，导入不会带入密码（Xftp 密码使用其自有加密格式），"
                + "请在「连接管理」中重新输入密码后重试。";
        }
        if (message.IndexOf("尚未保存密码", StringComparison.Ordinal) >= 0
            || message.IndexOf("无法解密", StringComparison.Ordinal) >= 0)
        {
            return message;
        }
        if (message.IndexOf("sftp", StringComparison.OrdinalIgnoreCase) >= 0
            && message.IndexOf("subsystem", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "SFTP 子系统不可用：服务器未启用 sftp 子系统。请联系管理员开启后重试。";
        }
        if (typeName == "SocketException")
        {
            return "网络不可达：" + host + " 连接失败（" + message + "）。";
        }
        return "连接 " + host + " 失败：" + message;
    }

    /// <summary>标注失败发生在哪一层：网络 / 主机密钥 / 认证 / SFTP / 其他。</summary>
    public static string Layer(Exception error)
    {
        string typeName = error.GetType().Name;
        string message = error.Message ?? string.Empty;
        if (typeName == "SshOperationTimeoutException" || typeName == "SocketException"
            || message.IndexOf("Connection failed to establish", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "网络层";
        }
        if (typeName == "SshAuthenticationException"
            || message.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("No more authentication methods", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("尚未保存密码", StringComparison.Ordinal) >= 0
            || message.IndexOf("无法解密", StringComparison.Ordinal) >= 0)
        {
            return "认证层";
        }
        if (message.IndexOf("sftp", StringComparison.OrdinalIgnoreCase) >= 0
            && message.IndexOf("subsystem", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "SFTP 子系统";
        }
        return "其他";
    }
}
