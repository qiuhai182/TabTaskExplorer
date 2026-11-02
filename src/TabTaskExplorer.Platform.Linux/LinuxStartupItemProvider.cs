using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Linux;

/// <summary>
/// Linux 启动项提供者：XDG autostart 桌面文件（当前用户与系统目录）与 systemd 服务。
/// 禁用系统级桌面文件时在当前用户目录写入同名覆盖文件（Hidden=true），不需要 root 权限；
/// 禁用 systemd 服务时执行 systemctl disable 并把服务名记录到本程序的禁用清单，便于重新启用。
/// </summary>
public class LinuxStartupItemProvider : IStartupItemProvider
{
    private const string DisabledServiceFileName = "disabled-systemd-services.txt";

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public string PlatformText
    {
        get { return "Linux XDG autostart 桌面文件与 systemd 服务"; }
    }

    /// <inheritdoc />
    public IReadOnlyList<StartupItem> Enumerate()
    {
        List<StartupItem> items = new List<StartupItem>();
        HashSet<string> seenIds = new HashSet<string>();

        string userAutoStartDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
        string systemAutoStartDirectory = "/etc/xdg/autostart";

        AddAutoStartItems(items, seenIds, userAutoStartDirectory, StartupItemScope.CurrentUser, true, false);
        AddAutoStartItems(items, seenIds, systemAutoStartDirectory, StartupItemScope.AllUsers, true, true);
        AddSystemdItems(items, seenIds);

        items.Sort(CompareItems);
        return items;
    }

    /// <inheritdoc />
    public bool SetEnabled(StartupItem item, bool enabled)
    {
        if (item == null || string.IsNullOrEmpty(item.Id))
        {
            return false;
        }
        if (item.Id.StartsWith("xdguser|", StringComparison.Ordinal))
        {
            return SetAutoStartFileEnabled(item.Id.Substring(8), enabled, false);
        }
        if (item.Id.StartsWith("xdgsys|", StringComparison.Ordinal))
        {
            return SetAutoStartFileEnabled(item.Id.Substring(7), enabled, true);
        }
        if (item.Id.StartsWith("systemd|", StringComparison.Ordinal))
        {
            return SetSystemdEnabled(item.Id.Substring(8), enabled);
        }
        return false;
    }

    /// <summary>枚举一个目录下的 .desktop 启动项；系统目录跳过已被用户覆盖文件替代的同名项。</summary>
    private static void AddAutoStartItems(List<StartupItem> items, HashSet<string> seenIds,
        string directory, string scope, bool canToggle, bool isSystemDirectory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }
        string userDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
        try
        {
            string[] files = Directory.GetFiles(directory, "*.desktop");
            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                if (isSystemDirectory && File.Exists(Path.Combine(userDirectory, fileName)))
                {
                    // 用户覆盖文件优先，系统同名文件不再重复展示
                    continue;
                }
                DesktopEntry entry = ReadDesktopEntry(filePath);
                string id = (isSystemDirectory ? "xdgsys|" : "xdguser|") + filePath;
                if (!seenIds.Add(id))
                {
                    continue;
                }
                StartupItem item = new StartupItem();
                item.Id = id;
                item.Name = entry.Name.Length > 0 ? entry.Name : Path.GetFileNameWithoutExtension(fileName);
                item.Source = StartupItemSource.AutoStartFile;
                item.ItemType = DetectItemType(entry.Exec);
                item.Command = entry.Exec;
                item.Arguments = string.Empty;
                item.Location = filePath;
                item.Scope = scope;
                item.Enabled = entry.Enabled;
                item.CanToggle = canToggle;
                item.ToggleHint = string.Empty;
                item.Description = entry.Enabled ? "XDG 自启动文件：" + filePath : "已被覆盖文件禁用：" + filePath;
                item.IsSelfAutoStart = string.Equals(fileName, "tabtaskexplorer.desktop", StringComparison.OrdinalIgnoreCase);
                items.Add(item);
            }
        }
        catch
        {
            // 目录不可读时跳过该来源
        }
    }

    /// <summary>解析 .desktop 文件中的名称、执行命令与启用状态。</summary>
    private static DesktopEntry ReadDesktopEntry(string filePath)
    {
        DesktopEntry entry = new DesktopEntry();
        entry.Enabled = true;
        try
        {
            bool inDesktopEntrySection = false;
            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inDesktopEntrySection = string.Equals(line, "[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inDesktopEntrySection)
                {
                    continue;
                }
                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, equalsIndex).Trim();
                string value = line.Substring(equalsIndex + 1).Trim();
                if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Name = value;
                }
                else if (string.Equals(key, "Exec", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Exec = value;
                }
                else if (string.Equals(key, "Hidden", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Enabled = !IsTrue(value);
                }
                else if (string.Equals(key, "X-GNOME-Autostart-enabled", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsTrue(value))
                    {
                        entry.Enabled = false;
                    }
                }
            }
        }
        catch
        {
            // 单个文件解析失败时按空白条目处理，不影响其他条目
        }
        return entry;
    }

    /// <summary>判断 desktop 文件中的布尔值是否为真。</summary>
    private static bool IsTrue(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 启用或禁用桌面文件：当前用户文件直接改写 Hidden 键；
    /// 系统目录文件禁用时写入同名用户覆盖文件，启用时删除覆盖文件。
    /// </summary>
    private static bool SetAutoStartFileEnabled(string filePath, bool enabled, bool isSystemFile)
    {
        try
        {
            string userDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
            Directory.CreateDirectory(userDirectory);
            string overridePath = Path.Combine(userDirectory, Path.GetFileName(filePath));
            if (isSystemFile)
            {
                if (enabled)
                {
                    if (File.Exists(overridePath))
                    {
                        File.Delete(overridePath);
                    }
                    return !File.Exists(overridePath);
                }
                string original = File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
                File.WriteAllText(overridePath, BuildHiddenOverride(original));
                return File.Exists(overridePath);
            }
            if (!File.Exists(filePath))
            {
                return false;
            }
            string content = File.ReadAllText(filePath);
            string updated = ReplaceDesktopEntryKey(content, "Hidden", enabled ? "false" : "true");
            updated = ReplaceDesktopEntryKey(updated, "X-GNOME-Autostart-enabled", enabled ? "true" : "false");
            File.WriteAllText(filePath, updated);
            return true;
        }
        catch
        {
            // 文件不可写时返回失败
            return false;
        }
    }

    /// <summary>用原文件内容生成隐藏覆盖文件，保留名称与执行命令。</summary>
    private static string BuildHiddenOverride(string originalContent)
    {
        string content = originalContent;
        if (content.IndexOf("[Desktop Entry]", StringComparison.OrdinalIgnoreCase) < 0)
        {
            content = "[Desktop Entry]\nType=Application\n" + content;
        }
        string updated = ReplaceDesktopEntryKey(content, "Hidden", "true");
        return ReplaceDesktopEntryKey(updated, "X-GNOME-Autostart-enabled", "false");
    }

    /// <summary>替换 desktop 文件 [Desktop Entry] 段中的一个键值；键不存在时插入到该段末尾。</summary>
    private static string ReplaceDesktopEntryKey(string content, string key, string value)
    {
        string[] lines = content.Split('\n');
        StringBuilder builder = new StringBuilder();
        bool inDesktopEntrySection = false;
        bool replaced = false;
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                if (inDesktopEntrySection && !replaced)
                {
                    builder.Append(key).Append('=').Append(value).Append('\n');
                    replaced = true;
                }
                inDesktopEntrySection = string.Equals(trimmed, "[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
            }
            if (inDesktopEntrySection && trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(key).Append('=').Append(value).Append('\n');
                replaced = true;
                continue;
            }
            builder.Append(line).Append('\n');
        }
        if (!replaced)
        {
            builder.Append(key).Append('=').Append(value).Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>枚举 systemd 服务：已启用的服务与本程序禁用过的服务。</summary>
    private static void AddSystemdItems(List<StartupItem> items, HashSet<string> seenIds)
    {
        HashSet<string> disabledByUs = ReadDisabledServiceNames();
        string output = RunCommand("systemctl", "list-unit-files --type=service --no-legend --no-pager");
        if (output.Length == 0)
        {
            return;
        }
        bool isRoot = IsRunningAsRoot();
        string[] lines = output.Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }
            string unitName = parts[0];
            string state = parts[1];
            if (!unitName.EndsWith(".service", StringComparison.Ordinal))
            {
                continue;
            }
            bool enabled = state.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0;
            bool disabledByThisProgram = disabledByUs.Contains(unitName);
            if (!enabled && !disabledByThisProgram)
            {
                continue;
            }
            string id = "systemd|" + unitName;
            if (!seenIds.Add(id))
            {
                continue;
            }
            StartupItem item = new StartupItem();
            item.Id = id;
            item.Name = unitName.Substring(0, unitName.Length - ".service".Length);
            item.Source = StartupItemSource.Service;
            item.ItemType = StartupItemType.Service;
            item.Command = unitName;
            item.Arguments = string.Empty;
            item.Location = unitName;
            item.Scope = StartupItemScope.System;
            item.Enabled = enabled;
            item.CanToggle = isRoot;
            item.ToggleHint = isRoot ? string.Empty : "启用或禁用 systemd 服务需要 root 权限";
            item.Description = "systemd 单元状态=" + state;
            items.Add(item);
        }
    }

    /// <summary>启用或禁用 systemd 服务，并把禁用记录写入本程序的清单。</summary>
    private static bool SetSystemdEnabled(string unitName, bool enabled)
    {
        string arguments = (enabled ? "enable " : "disable ") + unitName;
        CommandResult result = RunCommandWithExitCode("systemctl", arguments);
        if (result.ExitCode != 0)
        {
            return false;
        }
        UpdateDisabledServiceList(unitName, enabled);
        return true;
    }

    /// <summary>读取本程序禁用过的 systemd 服务清单。</summary>
    private static HashSet<string> ReadDisabledServiceNames()
    {
        HashSet<string> names = new HashSet<string>();
        try
        {
            string filePath = ResolveDisabledServiceFilePath();
            if (!File.Exists(filePath))
            {
                return names;
            }
            foreach (string line in File.ReadLines(filePath))
            {
                string name = line.Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            // 清单不可读时按没有禁用记录处理
        }
        return names;
    }

    /// <summary>把服务名加入或移出本程序禁用清单并写回磁盘。</summary>
    private static void UpdateDisabledServiceList(string unitName, bool enabled)
    {
        try
        {
            string filePath = ResolveDisabledServiceFilePath();
            List<string> names = new List<string>(ReadDisabledServiceNames());
            if (enabled)
            {
                names.Remove(unitName);
            }
            else if (!names.Contains(unitName))
            {
                names.Add(unitName);
            }
            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (directory.Length > 0)
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllLines(filePath, names);
        }
        catch
        {
            // 清单写入失败不影响 systemctl 的实际结果
        }
    }

    /// <summary>禁用清单文件路径：用户配置目录下，避免写系统目录。</summary>
    private static string ResolveDisabledServiceFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "TabTaskExplorer", DisabledServiceFileName);
    }

    /// <summary>执行命令并返回标准输出；失败或超时返回空文本。</summary>
    private static string RunCommand(string fileName, string arguments)
    {
        return RunCommandWithExitCode(fileName, arguments).Output;
    }

    /// <summary>执行命令并返回输出与退出码。</summary>
    private static CommandResult RunCommandWithExitCode(string fileName, string arguments)
    {
        CommandResult result = new CommandResult();
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            // 固定英文输出，保证 systemctl 的 enabled / disabled 状态词可被稳定解析
            startInfo.Environment["LC_ALL"] = "C";
            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return result;
                }
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(10000))
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                        // 进程已退出时忽略
                    }
                }
                result.Output = output;
                result.ExitCode = process.HasExited ? process.ExitCode : -1;
            }
        }
        catch
        {
            // systemctl 不存在或无法执行时返回空结果
        }
        return result;
    }

    /// <summary>判断当前进程是否以 root 身份运行（读取 /proc/self/status 的 Uid 行）。</summary>
    private static bool IsRunningAsRoot()
    {
        try
        {
            if (!File.Exists("/proc/self/status"))
            {
                return false;
            }
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                {
                    continue;
                }
                string remain = line.Substring(4).Trim();
                string[] parts = remain.Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    int uid;
                    if (int.TryParse(parts[0], out uid))
                    {
                        return uid == 0;
                    }
                }
            }
        }
        catch
        {
            // 读取失败时按非 root 处理
        }
        return false;
    }

    /// <summary>按执行命令的扩展名推断启动项类型。</summary>
    private static string DetectItemType(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.StartsWith("\"", StringComparison.Ordinal))
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                trimmed = trimmed.Substring(1, closingQuote - 1);
            }
        }
        else
        {
            int spaceIndex = trimmed.IndexOf(' ');
            if (spaceIndex > 0)
            {
                trimmed = trimmed.Substring(0, spaceIndex);
            }
        }
        string extension = Path.GetExtension(trimmed).ToLowerInvariant();
        if (extension == ".sh" || extension == ".py" || extension == ".js")
        {
            return StartupItemType.Script;
        }
        if (extension == ".desktop")
        {
            return StartupItemType.Shortcut;
        }
        return StartupItemType.Program;
    }

    /// <summary>启动项排序：先按来源，再按名称（忽略大小写）。</summary>
    private static int CompareItems(StartupItem left, StartupItem right)
    {
        int sourceCompare = ((int)left.Source).CompareTo((int)right.Source);
        if (sourceCompare != 0)
        {
            return sourceCompare;
        }
        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>.desktop 文件解析结果：名称、执行命令与启用状态。</summary>
internal class DesktopEntry
{
    public string Name { get; set; } = string.Empty;

    public string Exec { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

/// <summary>命令执行结果：标准输出与退出码。</summary>
internal class CommandResult
{
    public string Output { get; set; } = string.Empty;

    public int ExitCode { get; set; } = -1;
}
