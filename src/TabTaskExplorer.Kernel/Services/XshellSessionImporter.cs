using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>单个会话文件的解析结果（供自动发现流程与单元测试使用）。</summary>
public class XshellSessionEntry
{
    /// <summary>连接配置；解析失败或会话类型不支持时为 null。</summary>
    public RemoteConnectionProfile? Profile { get; set; }

    /// <summary>会话文件完整路径。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>会话名称（文件名去掉扩展名）。</summary>
    public string SessionName { get; set; } = string.Empty;

    /// <summary>会话分组（自动发现时取会话所在子目录名，否则取 Xshell 或 Xftp）。</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>跳过原因（带全角括号说明）；解析成功时为空串。</summary>
    public string SkipReason { get; set; } = string.Empty;
}

/// <summary>扫描本机 NetSarang 配置目录的结果。</summary>
public class XshellDiscoveryResult
{
    /// <summary>解析成功的会话，已按名称去重（同名会话保留高版本目录中的配置）。</summary>
    public List<XshellSessionEntry> Sessions { get; } = new List<XshellSessionEntry>();

    /// <summary>跳过的会话说明，格式为“会话名（原因）”。</summary>
    public List<string> Skipped { get; } = new List<string>();

    /// <summary>实际扫描到的配置根目录。</summary>
    public List<string> RootsScanned { get; } = new List<string>();
}

/// <summary>一键导入已安装配置的汇总结果。</summary>
public class XshellImportSummary
{
    /// <summary>新增连接数。</summary>
    public int Imported { get; set; }

    /// <summary>更新已有同名连接的数量。</summary>
    public int Updated { get; set; }

    /// <summary>导入（含更新）成功的会话名称，已去重。</summary>
    public List<string> Names { get; } = new List<string>();

    /// <summary>跳过的会话说明，格式为“会话名（原因）”。</summary>
    public List<string> Skipped { get; } = new List<string>();

    /// <summary>实际扫描到的配置根目录。</summary>
    public List<string> RootsScanned { get; } = new List<string>();
}

/// <summary>
/// Xshell / Xftp 会话文件解析与已安装配置自动发现。
/// 解析规则与参考实现（sshtools 的 ximport）保持一致：.xfp/.xftp 读 [Connection] 节，
/// .xsh 读 [CONNECTION] 与 [CONNECTION:AUTHENTICATION] 节，INI 的节名与键名均不区分大小写。
/// </summary>
public static class XshellSessionImporter
{
    /// <summary>解析单个会话文件；解析失败或会话类型不支持时返回 null。</summary>
    public static RemoteConnectionProfile? ImportFromFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }
        return ParseSessionFile(filePath).Profile;
    }

    /// <summary>解析单个会话文件并返回完整条目（含跳过原因）。</summary>
    public static XshellSessionEntry ParseSessionFile(string filePath)
    {
        XshellSessionEntry entry = new XshellSessionEntry
        {
            FilePath = filePath,
            SessionName = Path.GetFileNameWithoutExtension(filePath),
        };
        try
        {
            Dictionary<string, Dictionary<string, string>> sections = ParseIni(ReadTextAuto(filePath));
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            bool isFtpFile = extension == ".xfp" || extension == ".xftp";

            string host;
            string portText;
            string username;
            string userKey;
            RemoteProtocol protocol;

            if (isFtpFile)
            {
                // Xftp 会话：连接信息在 [Connection] 节；该文件另有 [Proxy] 节也含 Host 与 UserName，必须按节取值
                host = Pick(sections, "Connection", "Host");
                portText = Pick(sections, "Connection", "Port");
                username = Pick(sections, "Connection", "UserName");
                protocol = Pick(sections, "Connection", "Protocol") == "0" ? RemoteProtocol.Ftp : RemoteProtocol.Ssh;
                userKey = Pick(sections, "Connection", "UserKey");
            }
            else
            {
                // Xshell 会话：连接在 [CONNECTION] 节，认证在 [CONNECTION:AUTHENTICATION] 节
                host = Pick(sections, "CONNECTION", "Host");
                portText = Pick(sections, "CONNECTION", "Port");
                username = Pick(sections, "CONNECTION:AUTHENTICATION", "UserName");
                string protocolText = Pick(sections, "CONNECTION", "Protocol").ToUpperInvariant();
                if (protocolText == "FTP")
                {
                    protocol = RemoteProtocol.Ftp;
                }
                else if (protocolText == "SSH" || protocolText == "SFTP")
                {
                    protocol = RemoteProtocol.Ssh;
                }
                else
                {
                    // TELNET / RLOGIN / SERIAL 等类型暂不支持
                    entry.SkipReason = "（暂不支持的会话类型）";
                    return entry;
                }
                userKey = Pick(sections, "CONNECTION:AUTHENTICATION", "UserKey");
            }

            if (string.IsNullOrEmpty(host))
            {
                entry.SkipReason = "（暂不支持的会话类型）";
                return entry;
            }

            int port = 0;
            if (int.TryParse(portText, out int parsedPort))
            {
                port = parsedPort;
            }
            if (port < 1 || port > 65535)
            {
                port = protocol == RemoteProtocol.Ftp ? 21 : 22;
            }

            RemoteConnectionProfile profile = new RemoteConnectionProfile
            {
                Name = entry.SessionName,
                Group = string.Empty,
                Protocol = protocol,
                Host = host,
                Port = port,
                Username = username,
                AuthType = string.IsNullOrEmpty(userKey) ? RemoteAuthType.Password : RemoteAuthType.PrivateKey,
                PrivateKeyPath = string.Empty,
            };
            entry.Profile = profile;
        }
        catch (Exception)
        {
            entry.Profile = null;
            entry.SkipReason = "（解析失败）";
        }
        return entry;
    }

    /// <summary>NetSarang 配置根目录候选（覆盖常见的文档目录位置）。</summary>
    public static List<string> GetNetSarangRoots(string? homeDirectory = null)
    {
        string home = homeDirectory;
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        }
        List<string> roots = new List<string>();
        if (string.IsNullOrEmpty(home))
        {
            return roots;
        }
        roots.Add(Path.Combine(home, "Documents", "NetSarang Computer"));
        roots.Add(Path.Combine(home, "文档", "NetSarang Computer"));
        roots.Add(Path.Combine(home, "OneDrive", "Documents", "NetSarang Computer"));
        return roots;
    }

    /// <summary>
    /// 扫描本机已安装的 Xshell / Xftp 会话配置。
    /// 版本目录按名称升序遍历，同名会话由高版本目录覆盖低版本。
    /// </summary>
    public static XshellDiscoveryResult DiscoverInstalledSessions(string? homeDirectory = null)
    {
        XshellDiscoveryResult result = new XshellDiscoveryResult();
        Dictionary<string, int> nameIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string root in GetNetSarangRoots(homeDirectory))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            result.RootsScanned.Add(root);

            List<string> versions = new List<string>();
            try
            {
                foreach (string directory in Directory.GetDirectories(root))
                {
                    versions.Add(Path.GetFileName(directory));
                }
            }
            catch (Exception)
            {
                continue;
            }
            versions.Sort(StringComparer.Ordinal);

            foreach (string version in versions)
            {
                foreach (string kind in new string[] { "Xshell", "Xftp" })
                {
                    string sessionsDirectory = Path.Combine(root, version, kind, "Sessions");
                    if (!Directory.Exists(sessionsDirectory))
                    {
                        continue;
                    }
                    VisitDirectory(sessionsDirectory, kind, string.Empty, result, nameIndex);
                }
            }
        }
        return result;
    }

    /// <summary>递归收集一个 Sessions 目录下的会话文件。</summary>
    private static void VisitDirectory(
        string directory,
        string defaultGroup,
        string subGroup,
        XshellDiscoveryResult result,
        Dictionary<string, int> nameIndex)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(directory);
        }
        catch (Exception)
        {
            return;
        }

        foreach (string entryPath in entries)
        {
            if (Directory.Exists(entryPath))
            {
                string directoryName = Path.GetFileName(entryPath);
                string sub = subGroup.Length > 0 ? subGroup + "/" + directoryName : directoryName;
                VisitDirectory(entryPath, defaultGroup, sub, result, nameIndex);
                continue;
            }

            string fileName = Path.GetFileName(entryPath);
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension != ".xsh" && extension != ".xfp" && extension != ".xftp")
            {
                continue;
            }

            XshellSessionEntry parsed = ParseSessionFile(entryPath);
            if (parsed.Profile == null)
            {
                result.Skipped.Add(parsed.SessionName + parsed.SkipReason);
                continue;
            }
            parsed.Group = subGroup.Length > 0 ? subGroup : defaultGroup;
            int existingIndex;
            if (nameIndex.TryGetValue(parsed.SessionName, out existingIndex))
            {
                result.Sessions[existingIndex] = parsed;
            }
            else
            {
                nameIndex[parsed.SessionName] = result.Sessions.Count;
                result.Sessions.Add(parsed);
            }
        }
    }

    /// <summary>读取文本文件，自动识别 UTF-16LE 与 UTF-8 BOM 编码。</summary>
    private static string ReadTextAuto(string filePath)
    {
        byte[] buffer = File.ReadAllBytes(filePath);
        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(buffer, 2, buffer.Length - 2);
        }
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(buffer, 3, buffer.Length - 3);
        }
        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>解析 INI 文本：同名节合并，节名与键名统一转小写。</summary>
    private static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
    {
        Dictionary<string, Dictionary<string, string>> sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.Ordinal);
        sections[string.Empty] = current;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (line[0] == '[')
            {
                int end = line.IndexOf(']');
                string name = end > 0 ? line.Substring(1, end - 1).Trim().ToLowerInvariant() : string.Empty;
                Dictionary<string, string>? existed = null;
                if (sections.TryGetValue(name, out Dictionary<string, string>? foundSection))
                {
                    existed = foundSection;
                }
                if (existed != null)
                {
                    current = existed;
                }
                else
                {
                    current = new Dictionary<string, string>(StringComparer.Ordinal);
                    sections[name] = current;
                }
                continue;
            }
            int equalIndex = line.IndexOf('=');
            if (equalIndex <= 0)
            {
                continue;
            }
            string key = line.Substring(0, equalIndex).Trim().ToLowerInvariant();
            string value = line.Substring(equalIndex + 1).Trim();
            current[key] = value;
        }
        return sections;
    }

    /// <summary>取指定节指定键的值（节名与键均不区分大小写），缺失时返回空串。</summary>
    private static string Pick(Dictionary<string, Dictionary<string, string>> sections, string section, string key)
    {
        if (!sections.TryGetValue(section.ToLowerInvariant(), out Dictionary<string, string>? target) || target == null)
        {
            return string.Empty;
        }
        if (!target.TryGetValue(key.ToLowerInvariant(), out string? value) || value == null)
        {
            return string.Empty;
        }
        return value;
    }
}
