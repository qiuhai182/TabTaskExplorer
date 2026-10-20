namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 远端 POSIX 风格路径工具：SFTP 与 FTP 远端共用同一套路径规整规则。
/// 路径采用以 / 开头的绝对路径形式。
/// </summary>
public static class RemotePathUtility
{
    /// <summary>把任意输入规整为以 / 开头的远端绝对路径。</summary>
    public static string Normalize(string path)
    {
        string trimmed = (path ?? string.Empty).Replace('\\', '/').Trim();
        if (trimmed.Length == 0)
        {
            return "/";
        }
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = "/" + trimmed;
        }

        // 折叠连续斜杠并去除结尾斜杠
        while (trimmed.Contains("//"))
        {
            trimmed = trimmed.Replace("//", "/");
        }
        while (trimmed.Length > 1 && trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimEnd('/');
        }
        return trimmed;
    }

    /// <summary>取远端路径的最后一段名称。</summary>
    public static string GetFileName(string path)
    {
        string normalized = Normalize(path);
        if (normalized == "/")
        {
            return normalized;
        }
        int index = normalized.LastIndexOf('/');
        return normalized.Substring(index + 1);
    }

    /// <summary>取远端路径的父目录；根目录返回自身。</summary>
    public static string GetParentPath(string path)
    {
        string normalized = Normalize(path);
        int index = normalized.LastIndexOf('/');
        if (index <= 0)
        {
            return "/";
        }
        return normalized.Substring(0, index);
    }

    /// <summary>拼接远端目录与名称。</summary>
    public static string Join(string directory, string name)
    {
        string dir = Normalize(directory);
        if (dir == "/")
        {
            return "/" + name;
        }
        return dir + "/" + name;
    }
}
