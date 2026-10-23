using System.Text.RegularExpressions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 系统右键菜单的跨平台通用策略：按右键目标筛选适用条目、代入命令模板占位符、拆分命令行。
/// 桌面端与网页端共用本策略，保证两端展示的系统菜单项完全一致。
/// </summary>
public static partial class SystemContextMenuPolicy
{
    [GeneratedRegex("%[0-9a-zA-Z*]")]
    private static partial Regex LeftoverPlaceHolderRegex();

    /// <summary>
    /// 从全部条目中选出与右键目标匹配、且可在本程序菜单里唤起的文件右键菜单项。
    /// </summary>
    /// <param name="entries">全部系统右键菜单项。</param>
    /// <param name="targetPath">右键目标的完整路径。</param>
    /// <param name="isDirectory">右键目标是否目录。</param>
    /// <returns>可唤起且匹配的条目列表（按枚举顺序）。</returns>
    public static List<SystemContextMenuEntry> SelectForTarget(IReadOnlyList<SystemContextMenuEntry> entries, string targetPath, bool isDirectory)
    {
        List<SystemContextMenuEntry> selected = new List<SystemContextMenuEntry>();
        if (entries == null)
        {
            return selected;
        }
        foreach (SystemContextMenuEntry entry in entries)
        {
            if (entry.Group != SystemMenuGroup.FileContext)
            {
                continue;
            }
            if (!entry.CanExecute || !entry.IsEnabled)
            {
                continue;
            }
            if (IsTargetMatch(entry, targetPath, isDirectory))
            {
                selected.Add(entry);
            }
        }
        return selected;
    }

    /// <summary>
    /// 从全部条目中选出右键「目录空白处」时适用的条目（资源管理器里在文件夹空白处右键出现的系统菜单项）。
    /// 取桌面 / 目录空白处分组中可唤起、已启用且匹配该目录的条目，命令里的 %V 代入该目录路径。
    /// </summary>
    /// <param name="entries">全部系统右键菜单项。</param>
    /// <param name="directoryPath">右键所在的目录完整路径。</param>
    /// <returns>可唤起且匹配的条目列表（按枚举顺序）。</returns>
    public static List<SystemContextMenuEntry> SelectForBackground(IReadOnlyList<SystemContextMenuEntry> entries, string directoryPath)
    {
        List<SystemContextMenuEntry> selected = new List<SystemContextMenuEntry>();
        if (entries == null)
        {
            return selected;
        }
        foreach (SystemContextMenuEntry entry in entries)
        {
            if (entry.Group != SystemMenuGroup.DesktopContext)
            {
                continue;
            }
            if (!entry.CanExecute || !entry.IsEnabled)
            {
                continue;
            }
            if (IsTargetMatch(entry, directoryPath, true))
            {
                selected.Add(entry);
            }
        }
        return selected;
    }

    /// <summary>
    /// 判断单条菜单项是否适用于右键目标。
    /// 规则：* / AllFilesystemObjects / Folder 对任意目标生效；Directory / Drive 只对目录生效；
    /// 点开头的按扩展名匹配；exefile、lnkfile、Unknown 等文件类型标识按扩展名对表。
    /// </summary>
    public static bool IsTargetMatch(SystemContextMenuEntry entry, string targetPath, bool isDirectory)
    {
        string selector = entry.ContextSelector;
        if (string.Equals(selector, "*", StringComparison.OrdinalIgnoreCase)
            || string.Equals(selector, "AllFilesystemObjects", StringComparison.OrdinalIgnoreCase)
            || string.Equals(selector, "Folder", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(selector, "Directory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(selector, "Drive", StringComparison.OrdinalIgnoreCase))
        {
            return isDirectory;
        }
        string extension = string.Empty;
        try
        {
            extension = Path.GetExtension(targetPath ?? string.Empty);
        }
        catch (ArgumentException)
        {
            // 目标路径含非法字符时按无扩展名处理
        }
        extension = extension.ToLowerInvariant();
        if (selector.StartsWith(".", StringComparison.Ordinal))
        {
            return string.Equals(selector, extension, StringComparison.OrdinalIgnoreCase);
        }
        if (string.Equals(selector, "exefile", StringComparison.OrdinalIgnoreCase))
        {
            return extension == ".exe";
        }
        if (string.Equals(selector, "lnkfile", StringComparison.OrdinalIgnoreCase))
        {
            return extension == ".lnk";
        }
        if (string.Equals(selector, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(extension);
        }
        // 其他具体 ProgID（如 xmp.mp4）只在同扩展名时命中
        if (string.Equals(selector, extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 把命令模板里的占位符代入目标路径：处理环境变量与带引号/裸露的 %1、%V 等占位符，
    /// 其余占位符（图标序号、附加参数等）清除，返回可直接执行的命令行。
    /// </summary>
    /// <param name="commandTemplate">注册表 command 子键的命令模板。</param>
    /// <param name="targetPath">右键目标的完整路径。</param>
    public static string ExpandCommand(string commandTemplate, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            return string.Empty;
        }
        string path = targetPath ?? string.Empty;
        string quotedPath = "\"" + path + "\"";
        string result = Environment.ExpandEnvironmentVariables(commandTemplate);
        string[] tokens = new string[] { "1", "V", "v", "L", "l", "*" };
        foreach (string token in tokens)
        {
            // 先替换已带引号的占位符，避免代入后出现双层引号
            result = result.Replace("\"%" + token + "\"", quotedPath);
            result = result.Replace("%" + token, quotedPath);
        }
        result = LeftoverPlaceHolderRegex().Replace(result, string.Empty);
        return result.Trim();
    }

    /// <summary>
    /// 把完整命令行拆为可执行文件路径与其余参数（规则与 Windows 一致：引号开头取到下一个引号，否则取到首个空白）。
    /// </summary>
    /// <param name="commandLine">完整命令行。</param>
    /// <param name="fileName">输出：可执行文件路径。</param>
    /// <param name="arguments">输出：其余参数。</param>
    public static void SplitCommandLine(string commandLine, out string fileName, out string arguments)
    {
        fileName = string.Empty;
        arguments = string.Empty;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return;
        }
        string line = commandLine.Trim();
        if (line.StartsWith("\"", StringComparison.Ordinal))
        {
            int closingQuote = line.IndexOf('"', 1);
            if (closingQuote > 0)
            {
                fileName = line.Substring(1, closingQuote - 1);
                arguments = line.Substring(closingQuote + 1).Trim();
                return;
            }
            fileName = line.Trim('"');
            arguments = string.Empty;
            return;
        }
        int spaceIndex = line.IndexOfAny(new char[] { ' ', '\t' });
        if (spaceIndex < 0)
        {
            fileName = line;
            arguments = string.Empty;
            return;
        }
        fileName = line.Substring(0, spaceIndex);
        arguments = line.Substring(spaceIndex + 1).Trim();
    }
}
