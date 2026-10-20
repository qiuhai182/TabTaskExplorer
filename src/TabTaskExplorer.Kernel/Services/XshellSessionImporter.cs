using System.Text;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// Xshell 会话文件导入器：解析 .xsh / .xshf 会话文件（INI 风格键值对，UTF-16LE 或 UTF-8 编码），
/// 提取主机、端口、用户名与描述生成连接配置。密码在 Xshell 中为主密码加密存储，无法直接迁移，导入后需重新输入。
/// </summary>
public static class XshellSessionImporter
{
    /// <summary>解析单个 Xshell 会话文件；解析失败或缺少主机信息时返回 null。</summary>
    public static RemoteConnectionProfile? ImportFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }
        string text = ReadWithDetectedEncoding(filePath);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        RemoteConnectionProfile profile = new RemoteConnectionProfile();
        bool hasHost = false;
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }
            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            string key = line.Substring(0, separator).Trim();
            string value = line.Substring(separator + 1).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            // 键名不区分大小写（Xshell 各版本大小写不一致）
            if (string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase))
            {
                profile.Host = value;
                hasHost = true;
            }
            else if (string.Equals(key, "Port", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, out int port);
                profile.Port = port;
            }
            else if (string.Equals(key, "UserName", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "User", StringComparison.OrdinalIgnoreCase))
            {
                profile.Username = value;
            }
            else if (string.Equals(key, "Description", StringComparison.OrdinalIgnoreCase))
            {
                profile.Name = value;
            }
        }

        if (!hasHost || profile.Host.Length == 0)
        {
            return null;
        }
        if (profile.Name.Length == 0)
        {
            profile.Name = Path.GetFileNameWithoutExtension(filePath);
        }
        return profile;
    }

    /// <summary>按 BOM 检测编码读取文本：UTF-16LE 优先，其次 UTF-8。</summary>
    private static string ReadWithDetectedEncoding(string filePath)
    {
        byte[] head = new byte[2];
        using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            if (stream.Length >= 2)
            {
                stream.Read(head, 0, 2);
            }
        }
        if (head[0] == 0xFF && head[1] == 0xFE)
        {
            return File.ReadAllText(filePath, Encoding.Unicode);
        }
        return File.ReadAllText(filePath, Encoding.UTF8);
    }
}
