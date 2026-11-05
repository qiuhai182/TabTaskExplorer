using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 远程凭据完整性检查：SSH 密码认证必须有可用明文密码。
/// Xshell / Xftp 会话导入只带连接参数，不会带入密码（Xftp 的 Password 是其自有加密格式），
/// 因此导入连接在首次使用前必须要求用户手动补填。
/// </summary>
public static class RemoteCredentialCheck
{
    /// <summary>导入/历史配置缺少密码时的统一提示文案；不包含任何明文或密文。</summary>
    public const string MissingPasswordHint =
        "Xshell / Xftp 导入只读取主机、端口和账号，不会带入密码（Xftp 密码使用其自有加密格式）。请在「连接管理」中选中该连接，在“密码”框重新输入并保存后重试。";

    /// <summary>
    /// 检查连接是否缺少可用密码；返回可读原因，凭据齐全时返回 null。
    /// plaintextSecret 为当前解密结果：密文存在但解密失败（如换用户/换机器）同样按缺密码处理。
    /// </summary>
    public static string? DescribeMissing(RemoteConnectionProfile profile, string plaintextSecret)
    {
        if (profile == null)
        {
            return null;
        }
        if (profile.Protocol == RemoteProtocol.Ssh && profile.AuthType == RemoteAuthType.Password
            && string.IsNullOrEmpty(plaintextSecret))
        {
            if (!string.IsNullOrEmpty(profile.ProtectedSecret))
            {
                return "连接「" + profile.Name + "」保存的密码无法解密（可能来自其他 Windows 用户或机器），请重新输入密码后重试。";
            }
            return "连接「" + profile.Name + "」尚未保存密码。" + MissingPasswordHint;
        }
        if (profile.JumpHops != null)
        {
            for (int index = 0; index < profile.JumpHops.Count; index++)
            {
                RemoteJumpHop hop = profile.JumpHops[index];
                if (hop.AuthType == RemoteAuthType.Password && string.IsNullOrEmpty(hop.ProtectedSecret))
                {
                    return "第 " + (index + 1) + " 跳（" + hop.Username + "@" + hop.Host + ":" + hop.GetEffectivePort()
                        + "）尚未保存密码。" + MissingPasswordHint;
                }
            }
        }
        return null;
    }
}
