using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 日志与诊断包脱敏：把密码、密钥、Bearer 与会话令牌、Authorization 头、
/// URL 内嵌凭据、私钥文本与 JWT 统一替换为 ***；主机密钥指纹不在脱敏范围内。
/// </summary>
public static class LogSanitizer
{
    /// <summary>URL 中的用户信息（用户名与密码统一替换）。</summary>
    private static readonly Regex UrlCredentialPattern = new Regex(
        @"(\b(?:https?|ftp|sftp|ssh|smb|rdp):\/\/)([^\/\s@]+)@",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Authorization / Proxy-Authorization 头的完整值；支持带引号的 JSON 写法，遇引号、换行、逗号、分号或 &amp; 即停止。</summary>
    private static readonly Regex AuthorizationPattern = new Regex(
        @"(?<![A-Za-z])(?<key>proxy-authorization|authorization)(?<separator>[""']?\s*[:=]\s*)(?<quote>[""']?)(?<value>[^""'\r\n,;&]+)\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 密码、密钥、令牌、凭证等敏感键的值：键名本身保留，只把值替换为 ***。
    /// 值遇到空格、逗号、分号或查询串分隔符 &amp; 即停止，因此同一查询串中的其余参数原样保留；
    /// 允许键与值带引号，覆盖 JSON 等嵌套结构中的任意层级。
    /// </summary>
    private static readonly Regex KeyValuePattern = new Regex(
        @"(?<![A-Za-z])(?<key>access[_-]?token|refresh[_-]?token|api[_-]?key|apikey|password|passwd|pwd|secret|token|authorization|cookie|credential|session[_-]?id|密码|密钥|令牌|凭证|会话|授权|key)(?<separator>[""']?\s*[=:：]\s*)(?<quote>[""']?)(?<value>[^\s,;，；&""']+)\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>布尔、数字与常见枚举字面量：这些值不承载凭据，保持原样便于判断开关与状态。</summary>
    private static readonly HashSet<string> InertValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "on", "off", "enabled", "disabled",
        "none", "null", "nil", "auto", "manual", "readonly", "readwrite",
        "full", "limited", "default", "custom", "all", "any", "loopback",
        "basic", "bearer", "digest", "ntlm", "info", "debug", "warn", "warning",
        "error", "trace", "startup", "shutdown",
        "是", "否", "开", "关", "启用", "禁用", "无", "自动", "手动",
        "只读", "读写", "完全", "受限", "默认", "自定义", "全部", "服务", "交互", "系统",
    };

    /// <summary>纯数字值（可带小数、正负号），用于避免把计数、端口等数字误当凭据。</summary>
    private static readonly Regex NumericValuePattern = new Regex(
        @"^[-+]?\d+(?:\.\d+)?$",
        RegexOptions.Compiled);

    /// <summary>Bearer 令牌（可能出现在任意文本中）。</summary>
    private static readonly Regex BearerTokenPattern = new Regex(
        @"(bearer\s+)([A-Za-z0-9\-._~+/]+=*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>完整私钥文本块（包含换行的 PEM 内容）。</summary>
    private static readonly Regex PrivateKeyBlockPattern = new Regex(
        @"-----BEGIN[^\r\n]*PRIVATE KEY-----[\s\S]*?-----END[^\r\n]*PRIVATE KEY-----",
        RegexOptions.Compiled);

    /// <summary>JWT 形态的三段式令牌。</summary>
    private static readonly Regex JwtPattern = new Regex(
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
        RegexOptions.Compiled);

    /// <summary>对一段文本应用全部脱敏规则；空文本原样返回空串。</summary>
    public static string MaskSecrets(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        string result = text;
        result = PrivateKeyBlockPattern.Replace(result, "***");
        result = AuthorizationPattern.Replace(result, LogSanitizer.MaskAuthorizationValue);
        result = BearerTokenPattern.Replace(result, "$1***");
        result = UrlCredentialPattern.Replace(result, "$1***@");
        result = KeyValuePattern.Replace(result, LogSanitizer.MaskKeyValue);
        result = JwtPattern.Replace(result, "***");
        return result;
    }

    /// <summary>
    /// 只替换 Authorization 头的值：布尔、数字与枚举（如 Basic、NTLM）保持原样，
    /// 其余自由文本值统一掩为 ***。
    /// </summary>
    private static string MaskAuthorizationValue(Match match)
    {
        string value = match.Groups["value"].Value.Trim();
        if (LogSanitizer.IsInertValue(value))
        {
            return match.Value;
        }
        string quote = match.Groups["quote"].Value;
        return match.Groups["key"].Value + match.Groups["separator"].Value + quote + "***" + quote;
    }

    /// <summary>
    /// 只替换敏感键的值：键名、分隔符、引号与后续查询参数全部保留，
    /// 布尔、数字与枚举值不脱敏。
    /// </summary>
    private static string MaskKeyValue(Match match)
    {
        string value = match.Groups["value"].Value;
        if (LogSanitizer.IsInertValue(value))
        {
            return match.Value;
        }
        string quote = match.Groups["quote"].Value;
        return match.Groups["key"].Value + match.Groups["separator"].Value + quote + "***" + quote;
    }

    /// <summary>判断值是否属于不承载凭据的布尔、数字或常见枚举字面量。</summary>
    private static bool IsInertValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }
        if (LogSanitizer.InertValues.Contains(value))
        {
            return true;
        }
        return LogSanitizer.NumericValuePattern.IsMatch(value);
    }

    /// <summary>
    /// 判断配置字段名是否属于必须脱敏的类型：密码、密钥、令牌、授权、Cookie、
    /// 凭证与私钥；快捷键按键名（KeyText）等普通字段不在范围内。
    /// </summary>
    public static bool IsSensitivePropertyName(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }
        string name = propertyName.Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
        if (name.Contains("password", StringComparison.Ordinal)
            || name.Contains("passwd", StringComparison.Ordinal)
            || name.Contains("secret", StringComparison.Ordinal)
            || name.Contains("token", StringComparison.Ordinal)
            || name.Contains("apikey", StringComparison.Ordinal)
            || name.Contains("accesskey", StringComparison.Ordinal)
            || name.Contains("authorization", StringComparison.Ordinal)
            || name.Contains("cookie", StringComparison.Ordinal)
            || name.Contains("credential", StringComparison.Ordinal)
            || name.Contains("privatekey", StringComparison.Ordinal)
            || name.Contains("sessionkey", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }
}
