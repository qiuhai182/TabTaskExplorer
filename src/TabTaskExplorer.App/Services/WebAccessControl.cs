using System;
using System.IO;
using System.Net;
using System.Text;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 网页端访问控制：来源地址匹配、路径白名单匹配与账号密码校验。
/// 所有判定都只依赖权限方案中的配置，不做任何隐式放行。
/// </summary>
public static class WebAccessControl
{
    /// <summary>
    /// 判断来源地址是否放行：支持单个地址与网段写法（如 192.168.1.0/24、fe80::/10）。
    /// 白名单为空时不限制来源。
    /// </summary>
    public static bool IsAddressAllowed(WebAccessProfile profile, IPAddress? address)
    {
        if (address == null)
        {
            return false;
        }
        if (profile.AllowedAddresses == null || profile.AllowedAddresses.Count == 0)
        {
            return true;
        }
        IPAddress candidate = WebAccessControl.NormalizeAddress(address);
        foreach (string entry in profile.AllowedAddresses)
        {
            if (WebAccessControl.MatchesAddress(entry, candidate))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 判断路径是否在放行范围内：目录按前缀匹配，文件按完整路径匹配。
    /// 路径为空表示"此电脑"根视图，恒放行；白名单为空表示不限制路径。
    /// </summary>
    public static bool IsPathAllowed(WebAccessProfile profile, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }
        if (profile.AllowedPaths == null || profile.AllowedPaths.Count == 0)
        {
            return true;
        }
        string fullPath = WebAccessControl.NormalizePath(path);
        foreach (string entry in profile.AllowedPaths)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }
            string allowedPath = WebAccessControl.NormalizePath(entry);
            if (string.Equals(fullPath, allowedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // 目录前缀匹配：必须是"目录 + 分隔符"开头，避免 D:\Data 误放行 D:\Database
            string prefix = allowedPath;
            if (!prefix.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                prefix = prefix + Path.DirectorySeparatorChar;
            }
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>解析 HTTP Basic 认证头：成功时返回账号与密码原文。</summary>
    public static bool TryParseBasic(string? header, out string account, out string password)
    {
        account = string.Empty;
        password = string.Empty;
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }
        const string prefix = "Basic ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            byte[] raw = Convert.FromBase64String(header.Substring(prefix.Length).Trim());
            string text = Encoding.UTF8.GetString(raw);
            int colon = text.IndexOf(':');
            if (colon < 0)
            {
                return false;
            }
            account = text.Substring(0, colon);
            password = text.Substring(colon + 1);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>把 IPv4 映射的 IPv6 地址（::ffff:127.0.0.1）还原为 IPv4，便于同一网段规则同时生效。</summary>
    public static IPAddress NormalizeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }
        return address;
    }

    /// <summary>规范化路径：转为完整路径并去掉末尾分隔符（保留盘符根与网络根）。</summary>
    public static string NormalizePath(string path)
    {
        string result = path;
        try
        {
            result = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            // 非法路径保持原文，交由后续比较判定
        }
        result = result.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        while (result.Length > 3 && result.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            result = result.Substring(0, result.Length - 1);
        }
        return result;
    }

    /// <summary>判断单个白名单条目是否命中来源地址；条目可为单个地址或 CIDR 网段。</summary>
    private static bool MatchesAddress(string entry, IPAddress candidate)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }
        string text = entry.Trim();
        int slash = text.IndexOf('/');
        if (slash < 0)
        {
            IPAddress? single = WebAccessControl.ParseAddress(text);
            if (single == null)
            {
                return false;
            }
            return single.Equals(candidate);
        }

        string addressText = text.Substring(0, slash);
        int prefixLength;
        if (!int.TryParse(text.Substring(slash + 1), out prefixLength))
        {
            return false;
        }
        IPAddress? network = WebAccessControl.ParseAddress(addressText);
        if (network == null)
        {
            return false;
        }
        network = WebAccessControl.NormalizeAddress(network);
        if (network.AddressFamily != candidate.AddressFamily)
        {
            return false;
        }
        int maxBits = network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxBits)
        {
            return false;
        }
        return WebAccessControl.HasSamePrefix(network, candidate, prefixLength);
    }

    /// <summary>按位数比较两个同族地址的前缀是否一致。</summary>
    private static bool HasSamePrefix(IPAddress left, IPAddress right, int prefixLength)
    {
        byte[] leftBytes = left.GetAddressBytes();
        byte[] rightBytes = right.GetAddressBytes();
        int fullBytes = prefixLength / 8;
        for (int index = 0; index < fullBytes; index++)
        {
            if (leftBytes[index] != rightBytes[index])
            {
                return false;
            }
        }
        int remainingBits = prefixLength % 8;
        if (remainingBits == 0)
        {
            return true;
        }
        int mask = (0xFF << (8 - remainingBits)) & 0xFF;
        return (leftBytes[fullBytes] & mask) == (rightBytes[fullBytes] & mask);
    }

    /// <summary>解析单个地址文本：失败返回 null。</summary>
    private static IPAddress? ParseAddress(string text)
    {
        IPAddress? parsed;
        if (IPAddress.TryParse(text, out parsed))
        {
            return parsed;
        }
        return null;
    }
}
