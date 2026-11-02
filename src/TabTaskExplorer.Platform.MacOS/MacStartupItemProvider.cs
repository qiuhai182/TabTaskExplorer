using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.MacOS;

/// <summary>
/// macOS 启动项提供者：launchd 代理（LaunchAgents）与守护进程（LaunchDaemons）plist。
/// 只有 RunAtLoad 为真且未被 Disabled 标记的条目视为开机自启；
/// 禁用时执行 launchctl unload -w，并把 plist 路径记录到本程序的禁用清单，便于重新启用。
/// </summary>
public class MacStartupItemProvider : IStartupItemProvider
{
    private const string DisabledListFileName = "disabled-launchd.txt";

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public string PlatformText
    {
        get { return "macOS LaunchAgents 与 LaunchDaemons 自启动 plist"; }
    }

    /// <inheritdoc />
    public IReadOnlyList<StartupItem> Enumerate()
    {
        List<StartupItem> items = new List<StartupItem>();
        HashSet<string> seenIds = new HashSet<string>();
        HashSet<string> disabledByUs = ReadDisabledList();
        bool isRoot = IsRunningAsRoot();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AddLaunchItems(items, seenIds, Path.Combine(home, "Library", "LaunchAgents"),
            StartupItemScope.CurrentUser, true, disabledByUs);
        AddLaunchItems(items, seenIds, "/Library/LaunchAgents",
            StartupItemScope.AllUsers, isRoot, disabledByUs);
        AddLaunchItems(items, seenIds, "/Library/LaunchDaemons",
            StartupItemScope.System, isRoot, disabledByUs);
        AddLaunchItems(items, seenIds, "/System/Library/LaunchAgents",
            StartupItemScope.System, false, disabledByUs);
        AddLaunchItems(items, seenIds, "/System/Library/LaunchDaemons",
            StartupItemScope.System, false, disabledByUs);

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
        if (!item.Id.StartsWith("launchd|", StringComparison.Ordinal))
        {
            return false;
        }
        string filePath = item.Id.Substring(8);
        return SetLaunchItemEnabled(filePath, enabled);
    }

    /// <summary>枚举一个 launchd 目录下的 plist 文件。</summary>
    private static void AddLaunchItems(List<StartupItem> items, HashSet<string> seenIds,
        string directory, string scope, bool canToggle, HashSet<string> disabledByUs)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }
        try
        {
            string[] files = Directory.GetFiles(directory, "*.plist");
            foreach (string filePath in files)
            {
                LaunchEntry entry = ReadLaunchEntry(filePath);
                bool disabledByThisProgram = disabledByUs.Contains(filePath);
                bool enabledBySystem = entry.RunAtLoad && !entry.Disabled;
                bool enabled = enabledBySystem && !disabledByThisProgram;
                if (!enabledBySystem && !disabledByThisProgram)
                {
                    // 不是开机自启的代理（例如按需唤醒）不纳入本清单
                    continue;
                }
                string id = "launchd|" + filePath;
                if (!seenIds.Add(id))
                {
                    continue;
                }
                StartupItem item = new StartupItem();
                item.Id = id;
                item.Name = entry.Label.Length > 0 ? entry.Label : Path.GetFileNameWithoutExtension(filePath);
                item.Source = StartupItemSource.LaunchAgent;
                item.ItemType = StartupItemType.Program;
                item.Command = entry.Command;
                item.Arguments = entry.Arguments;
                item.Location = filePath;
                item.Scope = scope;
                item.Enabled = enabled;
                item.CanToggle = canToggle;
                item.ToggleHint = canToggle ? string.Empty : "启用或禁用该系统级 launchd 条目需要 root 权限";
                item.Description = "plist：" + filePath;
                item.IsSelfAutoStart = string.Equals(Path.GetFileName(filePath),
                    "com.tabtaskexplorer.plist", StringComparison.OrdinalIgnoreCase);
                items.Add(item);
            }
        }
        catch
        {
            // 目录不可读时跳过该来源
        }
    }

    /// <summary>读取 plist 中的 Label、Program、ProgramArguments、RunAtLoad 与 Disabled。</summary>
    private static LaunchEntry ReadLaunchEntry(string filePath)
    {
        LaunchEntry entry = new LaunchEntry();
        string text = ReadPlistText(filePath);
        if (text.Length == 0 || text.StartsWith("bplist", StringComparison.Ordinal))
        {
            return entry;
        }
        try
        {
            XDocument document = XDocument.Parse(text);
            XElement? root = document.Root;
            if (root == null)
            {
                return entry;
            }
            XElement? dictionary = root.Element("dict");
            if (dictionary == null)
            {
                // 部分 plist 根节点直接就是 dict
                dictionary = root;
            }
            string label = ReadStringValue(dictionary, "Label");
            entry.Label = label;
            string program = ReadStringValue(dictionary, "Program");
            List<string> programArguments = ReadArrayValues(dictionary, "ProgramArguments");
            if (program.Length > 0)
            {
                entry.Command = program;
                entry.Arguments = JoinArguments(programArguments, 0);
            }
            else if (programArguments.Count > 0)
            {
                entry.Command = programArguments[0];
                entry.Arguments = JoinArguments(programArguments, 1);
            }
            entry.RunAtLoad = ReadBooleanValue(dictionary, "RunAtLoad");
            entry.Disabled = ReadBooleanValue(dictionary, "Disabled");
        }
        catch
        {
            // 非法或二进制 plist 解析失败时跳过该文件
        }
        return entry;
    }

    /// <summary>读取 plist 文本；二进制 plist 先尝试用 plutil 转成 XML。</summary>
    private static string ReadPlistText(string filePath)
    {
        try
        {
            string text = File.ReadAllText(filePath);
            if (!text.StartsWith("bplist", StringComparison.Ordinal))
            {
                return text;
            }
            CommandResult converted = RunCommand("plutil", "-convert xml1 -o - " + QuotePath(filePath));
            return converted.ExitCode == 0 ? converted.Output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>读取字典中的字符串值。</summary>
    private static string ReadStringValue(XElement dictionary, string keyName)
    {
        XElement? value = FindValueElement(dictionary, keyName);
        if (value == null)
        {
            return string.Empty;
        }
        return value.Value;
    }

    /// <summary>读取字典中的布尔值；元素为 true 空标签时返回 true。</summary>
    private static bool ReadBooleanValue(XElement dictionary, string keyName)
    {
        XElement? value = FindValueElement(dictionary, keyName);
        if (value == null)
        {
            return false;
        }
        return string.Equals(value.Name.LocalName, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>读取字典中数组元素的文本列表。</summary>
    private static List<string> ReadArrayValues(XElement dictionary, string keyName)
    {
        List<string> values = new List<string>();
        XElement? value = FindValueElement(dictionary, keyName);
        if (value == null)
        {
            return values;
        }
        foreach (XElement child in value.Elements())
        {
            values.Add(child.Value);
        }
        return values;
    }

    /// <summary>按 key 名找字典中紧随其后的值元素。</summary>
    private static XElement? FindValueElement(XElement dictionary, string keyName)
    {
        XElement? previousKey = null;
        foreach (XElement child in dictionary.Elements())
        {
            if (string.Equals(child.Name.LocalName, "key", StringComparison.OrdinalIgnoreCase))
            {
                previousKey = child;
                continue;
            }
            if (previousKey != null && string.Equals(previousKey.Value, keyName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }
        return null;
    }

    /// <summary>把参数数组从指定下标开始拼接成命令行文本。</summary>
    private static string JoinArguments(List<string> values, int startIndex)
    {
        StringBuilder builder = new StringBuilder();
        for (int index = startIndex; index < values.Count; index++)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
            string value = values[index];
            if (value.IndexOf(' ') >= 0)
            {
                builder.Append('"').Append(value).Append('"');
            }
            else
            {
                builder.Append(value);
            }
        }
        return builder.ToString();
    }

    /// <summary>启用或禁用 launchd 条目，并把禁用记录写入本程序清单。</summary>
    private static bool SetLaunchItemEnabled(string filePath, bool enabled)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }
        string arguments = (enabled ? "load -w " : "unload -w ") + QuotePath(filePath);
        CommandResult result = RunCommand("launchctl", arguments);
        if (result.ExitCode != 0)
        {
            return false;
        }
        UpdateDisabledList(filePath, enabled);
        return true;
    }

    /// <summary>读取本程序禁用过的 launchd plist 路径清单。</summary>
    private static HashSet<string> ReadDisabledList()
    {
        HashSet<string> paths = new HashSet<string>();
        try
        {
            string filePath = ResolveDisabledListPath();
            if (!File.Exists(filePath))
            {
                return paths;
            }
            foreach (string line in File.ReadLines(filePath))
            {
                string path = line.Trim();
                if (path.Length > 0)
                {
                    paths.Add(path);
                }
            }
        }
        catch
        {
            // 清单不可读时按没有禁用记录处理
        }
        return paths;
    }

    /// <summary>把 plist 路径加入或移出本程序禁用清单并写回磁盘。</summary>
    private static void UpdateDisabledList(string filePath, bool enabled)
    {
        try
        {
            string listPath = ResolveDisabledListPath();
            List<string> paths = new List<string>(ReadDisabledList());
            if (enabled)
            {
                paths.Remove(filePath);
            }
            else if (!paths.Contains(filePath))
            {
                paths.Add(filePath);
            }
            string directory = Path.GetDirectoryName(listPath) ?? string.Empty;
            if (directory.Length > 0)
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllLines(listPath, paths);
        }
        catch
        {
            // 清单写入失败不影响 launchctl 的实际结果
        }
    }

    /// <summary>禁用清单路径：用户应用支持目录。</summary>
    private static string ResolveDisabledListPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "TabTaskExplorer", DisabledListFileName);
    }

    /// <summary>执行命令并返回输出与退出码。</summary>
    private static CommandResult RunCommand(string fileName, string arguments)
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
            // launchctl / plutil 不存在或无法执行时返回空结果
        }
        return result;
    }

    /// <summary>判断当前进程是否以 root 身份运行。</summary>
    private static bool IsRunningAsRoot()
    {
        CommandResult result = RunCommand("id", "-u");
        if (result.ExitCode != 0)
        {
            return false;
        }
        return result.Output.Trim() == "0";
    }

    /// <summary>给路径加双引号。</summary>
    private static string QuotePath(string path)
    {
        return "\"" + path.Replace("\"", "\\\"") + "\"";
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

/// <summary>launchd plist 解析结果。</summary>
internal class LaunchEntry
{
    public string Label { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public bool RunAtLoad { get; set; }

    public bool Disabled { get; set; }
}

/// <summary>命令执行结果：标准输出与退出码。</summary>
internal class CommandResult
{
    public string Output { get; set; } = string.Empty;

    public int ExitCode { get; set; } = -1;
}
