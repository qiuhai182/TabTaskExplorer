using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 启动项提供者：注册表 Run / RunOnce（当前用户、所有用户、32 位视图）、
/// 用户与公共启动文件夹、自动启动的 Win32 服务。
/// 禁用注册表项时把原值与类型备份到本程序的禁用清单，重新启用时按原样写回；
/// 禁用启动文件夹条目时在文件名后追加 .tabxdisabled 后缀，重新启用时还原文件名；
/// 禁用服务时把 Start 值改为 4（禁用）并记录原始启动类型，重新启用时写回。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsStartupItemProvider : IStartupItemProvider
{
    private const string DisabledRunKeyPath = "Software\\TabTaskExplorer\\DisabledStartup";
    private const string DisabledServiceKeyPath = "Software\\TabTaskExplorer\\DisabledServices";
    private const string DisabledSuffix = ".tabxdisabled";

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public string PlatformText
    {
        get { return "Windows 注册表 Run / RunOnce、启动文件夹、开机或登录触发的计划任务与 Win32 自动启动服务"; }
    }

    /// <inheritdoc />
    public IReadOnlyList<StartupItem> Enumerate()
    {
        List<StartupItem> items = new List<StartupItem>();
        HashSet<string> seenIds = new HashSet<string>();
        bool isAdministrator = IsAdministrator();

        AddRegistryRunItems(items, seenIds, Registry.CurrentUser, "HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Run",
            StartupItemScope.CurrentUser, true);
        AddRegistryRunItems(items, seenIds, Registry.CurrentUser, "HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
            StartupItemScope.CurrentUser, true);
        AddRegistryRunItems(items, seenIds, Registry.LocalMachine, "HKLM", "Software\\Microsoft\\Windows\\CurrentVersion\\Run",
            StartupItemScope.AllUsers, isAdministrator);
        AddRegistryRunItems(items, seenIds, Registry.LocalMachine, "HKLM", "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
            StartupItemScope.AllUsers, isAdministrator);
        AddRegistryRunItems(items, seenIds, Registry.LocalMachine, "HKLM", "Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Run",
            StartupItemScope.AllUsers, isAdministrator);
        AddRegistryRunItems(items, seenIds, Registry.LocalMachine, "HKLM", "Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
            StartupItemScope.AllUsers, isAdministrator);
        AddDisabledRunItems(items, seenIds, Registry.CurrentUser, isAdministrator);
        AddDisabledRunItems(items, seenIds, Registry.LocalMachine, isAdministrator);
        AddScheduledTasks(items, seenIds, isAdministrator);

        AddStartupFolderItems(items, seenIds, Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            StartupItemScope.CurrentUser, true);
        AddStartupFolderItems(items, seenIds, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            StartupItemScope.AllUsers, isAdministrator);

        AddServiceItems(items, seenIds, isAdministrator);

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
        if (item.Id.StartsWith("run|", StringComparison.Ordinal))
        {
            return SetRegistryRunEnabled(item, enabled);
        }
        if (item.Id.StartsWith("folder|", StringComparison.Ordinal))
        {
            return SetStartupFolderEnabled(item, enabled);
        }
        if (item.Id.StartsWith("service|", StringComparison.Ordinal))
        {
            return SetServiceEnabled(item, enabled);
        }
        if (item.Id.StartsWith("task|", StringComparison.Ordinal))
        {
            return SetScheduledTaskEnabled(item, enabled);
        }
        return false;
    }

    /// <summary>枚举一个注册表 Run / RunOnce 键下的全部值。</summary>
    private static void AddRegistryRunItems(List<StartupItem> items, HashSet<string> seenIds,
        RegistryKey hive, string hiveName, string keyPath, string scope, bool canToggle)
    {
        try
        {
            using (RegistryKey? key = hive.OpenSubKey(keyPath))
            {
                if (key == null)
                {
                    return;
                }
                foreach (string valueName in key.GetValueNames())
                {
                    object? value = key.GetValue(valueName);
                    if (value == null)
                    {
                        continue;
                    }
                    string rawCommand = value.ToString() ?? string.Empty;
                    if (rawCommand.Length == 0)
                    {
                        continue;
                    }
                    string id = "run|" + hiveName + "|" + keyPath + "|" + valueName;
                    if (!seenIds.Add(id))
                    {
                        continue;
                    }
                    string expanded = Environment.ExpandEnvironmentVariables(rawCommand);
                    string command;
                    string arguments;
                    SplitCommand(expanded, out command, out arguments);
                    StartupItem item = new StartupItem();
                    item.Id = id;
                    item.Name = valueName;
                    item.Source = StartupItemSource.RegistryRun;
                    item.ItemType = DetectItemType(command);
                    item.Command = command;
                    item.Arguments = arguments;
                    item.Location = hiveName + "\\" + keyPath + "\\" + valueName;
                    item.Scope = scope;
                    item.Enabled = true;
                    item.CanToggle = canToggle;
                    item.ToggleHint = canToggle ? string.Empty : "修改所有用户的注册表启动项需要管理员权限";
                    item.Description = "注册表值：" + rawCommand;
                    item.IsSelfAutoStart = hiveName == "HKCU"
                        && string.Equals(valueName, "TabTaskExplorer", StringComparison.OrdinalIgnoreCase)
                        && keyPath.EndsWith("\\Run", StringComparison.OrdinalIgnoreCase);
                    items.Add(item);
                }
            }
        }
        catch
        {
            // 注册表键读取失败时跳过该来源，不影响其他启动项展示
        }
    }

    /// <summary>按注册表配置单元枚举禁用备份；当前用户备份与所有用户备份分别读取。</summary>
    private static void AddDisabledRunItems(List<StartupItem> items, HashSet<string> seenIds, RegistryKey hive, bool isAdministrator)
    {
        try
        {
            using (RegistryKey? backupKey = hive.OpenSubKey(DisabledRunKeyPath))
            {
                if (backupKey == null)
                {
                    return;
                }
                foreach (string encodedName in backupKey.GetValueNames())
                {
                    string id = DecodeBackupName(encodedName);
                    string[] parts = id.Split(new char[] { '|' }, 4);
                    if (parts.Length < 4 || parts[0] != "run")
                    {
                        continue;
                    }
                    if (!seenIds.Add(id))
                    {
                        continue;
                    }
                    string hiveName = parts[1];
                    string keyPath = parts[2];
                    string valueName = parts[3];
                    string storedText = backupKey.GetValue(encodedName)?.ToString() ?? string.Empty;
                    string kindText = "String";
                    string rawCommand = storedText;
                    int newlineIndex = storedText.IndexOf('\n');
                    if (newlineIndex >= 0)
                    {
                        kindText = storedText.Substring(0, newlineIndex);
                        rawCommand = storedText.Substring(newlineIndex + 1);
                    }
                    if (rawCommand.Length == 0)
                    {
                        continue;
                    }
                    string expanded = Environment.ExpandEnvironmentVariables(rawCommand);
                    string command;
                    string arguments;
                    SplitCommand(expanded, out command, out arguments);
                    bool canToggle = hiveName == "HKCU" || isAdministrator;
                    StartupItem item = new StartupItem();
                    item.Id = id;
                    item.Name = valueName;
                    item.Source = StartupItemSource.RegistryRun;
                    item.ItemType = DetectItemType(command);
                    item.Command = command;
                    item.Arguments = arguments;
                    item.Location = hiveName + "\\" + keyPath + "\\" + valueName;
                    item.Scope = hiveName == "HKCU" ? StartupItemScope.CurrentUser : StartupItemScope.AllUsers;
                    item.Enabled = false;
                    item.CanToggle = canToggle;
                    item.ToggleHint = canToggle ? string.Empty : "修改所有用户的注册表启动项需要管理员权限";
                    item.Description = "已由本程序禁用；注册表值类型=" + kindText;
                    item.IsSelfAutoStart = hiveName == "HKCU"
                        && string.Equals(valueName, "TabTaskExplorer", StringComparison.OrdinalIgnoreCase)
                        && keyPath.EndsWith("\\Run", StringComparison.OrdinalIgnoreCase);
                    items.Add(item);
                }
            }
        }
        catch
        {
            // 禁用备份读取失败时跳过，不影响其他启动项展示
        }
    }

    /// <summary>枚举一个启动文件夹中的条目；文件名带禁用后缀时按禁用状态展示。</summary>
    private static void AddStartupFolderItems(List<StartupItem> items, HashSet<string> seenIds,
        string folderPath, string scope, bool canToggle)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }
        try
        {
            string[] files = Directory.GetFiles(folderPath);
            foreach (string filePath in files)
            {
                bool disabled = filePath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                string basePath = disabled
                    ? filePath.Substring(0, filePath.Length - DisabledSuffix.Length)
                    : filePath;
                string id = "folder|" + basePath;
                if (!seenIds.Add(id))
                {
                    continue;
                }
                string displayName = Path.GetFileName(basePath);
                StartupItem item = new StartupItem();
                item.Id = id;
                item.Name = displayName;
                item.Source = StartupItemSource.StartupFolder;
                item.ItemType = DetectItemType(basePath);
                item.Command = basePath;
                item.Arguments = string.Empty;
                item.Location = filePath;
                item.Scope = scope;
                item.Enabled = !disabled;
                item.CanToggle = canToggle;
                item.ToggleHint = canToggle ? string.Empty : "修改所有用户的启动文件夹需要管理员权限";
                item.Description = disabled ? "已由本程序禁用（文件名后缀 " + DisabledSuffix + "）" : "启动文件夹：" + folderPath;
                items.Add(item);
            }
        }
        catch
        {
            // 启动文件夹不可读时跳过该来源
        }
    }

    /// <summary>枚举自动启动的 Win32 服务与本程序禁用过的服务。</summary>
    private static void AddServiceItems(List<StartupItem> items, HashSet<string> seenIds, bool isAdministrator)
    {
        HashSet<string> disabledServices = ReadDisabledServiceNames();
        try
        {
            using (RegistryKey? servicesKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services"))
            {
                if (servicesKey == null)
                {
                    return;
                }
                foreach (string serviceName in servicesKey.GetSubKeyNames())
                {
                    using (RegistryKey? serviceKey = servicesKey.OpenSubKey(serviceName))
                    {
                        if (serviceKey == null)
                        {
                            continue;
                        }
                        object? startValue = serviceKey.GetValue("Start");
                        object? typeValue = serviceKey.GetValue("Type");
                        int start = startValue is int ? (int)startValue : -1;
                        int serviceType = typeValue is int ? (int)typeValue : 0;
                        // Win32 服务类型位：0x10 独占进程、0x20 共享进程、0x100 可交互；
                        // 内核驱动与文件系统驱动不属于“开机自启程序与服务”，不展示
                        bool isWin32Service = (serviceType & 0x30) != 0 || (serviceType & 0x100) != 0;
                        if (!isWin32Service)
                        {
                            continue;
                        }
                        bool isAuto = start == 2;
                        bool wasDisabled = disabledServices.Contains(serviceName);
                        if (!isAuto && !wasDisabled)
                        {
                            continue;
                        }
                        string id = "service|" + serviceName;
                        if (!seenIds.Add(id))
                        {
                            continue;
                        }
                        object? displayNameValue = serviceKey.GetValue("DisplayName");
                        string displayName = displayNameValue == null ? string.Empty : displayNameValue.ToString() ?? string.Empty;
                        if (displayName.Length == 0 || displayName.StartsWith("@", StringComparison.Ordinal))
                        {
                            displayName = serviceName;
                        }
                        object? imagePathValue = serviceKey.GetValue("ImagePath");
                        string imagePath = imagePathValue == null ? string.Empty : imagePathValue.ToString() ?? string.Empty;
                        string expanded = Environment.ExpandEnvironmentVariables(imagePath);
                        string command;
                        string arguments;
                        SplitCommand(expanded, out command, out arguments);
                        StartupItem item = new StartupItem();
                        item.Id = id;
                        item.Name = displayName;
                        item.Source = StartupItemSource.Service;
                        item.ItemType = StartupItemType.Service;
                        item.Command = command;
                        item.Arguments = arguments;
                        item.Location = serviceName;
                        item.Scope = StartupItemScope.System;
                        item.Enabled = isAuto;
                        item.CanToggle = isAdministrator;
                        item.ToggleHint = isAdministrator ? string.Empty : "启用或禁用系统服务需要管理员权限";
                        item.Description = "服务名称=" + serviceName + " 启动类型=" + DescribeServiceStart(start);
                        items.Add(item);
                    }
                }
            }
        }
        catch
        {
            // 服务注册表不可读时跳过该来源
        }
    }

    /// <summary>把服务 Start 数值转成中文说明。</summary>
    private static string DescribeServiceStart(int start)
    {
        if (start == 0)
        {
            return "引导启动";
        }
        if (start == 1)
        {
            return "系统启动";
        }
        if (start == 2)
        {
            return "自动";
        }
        if (start == 3)
        {
            return "手动";
        }
        if (start == 4)
        {
            return "已禁用";
        }
        return "未知(" + start + ")";
    }

    /// <summary>启用或禁用注册表 Run 项：禁用时把原值与类型备份，启用时按原样写回。</summary>
    private static bool SetRegistryRunEnabled(StartupItem item, bool enabled)
    {
        string[] parts = item.Id.Split(new char[] { '|' }, 4);
        if (parts.Length < 4)
        {
            return false;
        }
        string hiveName = parts[1];
        string keyPath = parts[2];
        string valueName = parts[3];
        RegistryKey hive = hiveName == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
        string encodedName = EncodeBackupName(item.Id);
        try
        {
            if (enabled)
            {
                using (RegistryKey? backupKey = hive.OpenSubKey(DisabledRunKeyPath))
                {
                    if (backupKey == null)
                    {
                        return false;
                    }
                    string storedText = backupKey.GetValue(encodedName)?.ToString() ?? string.Empty;
                    string kindText = "String";
                    string rawCommand = storedText;
                    int newlineIndex = storedText.IndexOf('\n');
                    if (newlineIndex >= 0)
                    {
                        kindText = storedText.Substring(0, newlineIndex);
                        rawCommand = storedText.Substring(newlineIndex + 1);
                    }
                    if (rawCommand.Length == 0)
                    {
                        return false;
                    }
                    using (RegistryKey writableBackup = hive.OpenSubKey(DisabledRunKeyPath, true) ?? hive.CreateSubKey(DisabledRunKeyPath))
                    {
                        writableBackup.DeleteValue(encodedName, false);
                    }
                    using (RegistryKey targetKey = hive.CreateSubKey(keyPath))
                    {
                        targetKey.SetValue(valueName, rawCommand, ParseValueKind(kindText));
                        object? actual = targetKey.GetValue(valueName);
                        return actual != null && string.Equals(actual.ToString(), rawCommand, StringComparison.Ordinal);
                    }
                }
            }
            using (RegistryKey? sourceKey = hive.OpenSubKey(keyPath))
            {
                if (sourceKey == null)
                {
                    return false;
                }
                object? value = sourceKey.GetValue(valueName);
                if (value == null)
                {
                    return false;
                }
                string rawCommand = value.ToString() ?? string.Empty;
                RegistryValueKind valueKind = sourceKey.GetValueKind(valueName);
                using (RegistryKey writableSource = hive.OpenSubKey(keyPath, true) ?? sourceKey)
                {
                    writableSource.DeleteValue(valueName, false);
                }
                using (RegistryKey backupKey = hive.CreateSubKey(DisabledRunKeyPath))
                {
                    backupKey.SetValue(encodedName, valueKind.ToString() + "\n" + rawCommand, RegistryValueKind.String);
                }
                using (RegistryKey? verifyKey = hive.OpenSubKey(keyPath))
                {
                    return verifyKey == null || verifyKey.GetValue(valueName) == null;
                }
            }
        }
        catch
        {
            // 没有管理员权限或注册表被保护时返回失败，由界面给出提示
            return false;
        }
    }

    /// <summary>启用或禁用启动文件夹条目：禁用时追加后缀，启用时去掉后缀。</summary>
    private static bool SetStartupFolderEnabled(StartupItem item, bool enabled)
    {
        string[] parts = item.Id.Split(new char[] { '|' }, 2);
        if (parts.Length < 2)
        {
            return false;
        }
        string basePath = parts[1];
        try
        {
            if (enabled)
            {
                string disabledPath = basePath + DisabledSuffix;
                if (!File.Exists(disabledPath))
                {
                    return false;
                }
                File.Move(disabledPath, basePath, true);
                return File.Exists(basePath);
            }
            if (!File.Exists(basePath))
            {
                return false;
            }
            string targetPath = basePath + DisabledSuffix;
            File.Move(basePath, targetPath, true);
            return File.Exists(targetPath);
        }
        catch
        {
            // 公共启动文件夹没有写入权限时返回失败
            return false;
        }
    }

    /// <summary>启用或禁用 Win32 服务：修改 Start 值，禁用时记录原始启动类型便于恢复。</summary>
    private static bool SetServiceEnabled(StartupItem item, bool enabled)
    {
        string[] parts = item.Id.Split(new char[] { '|' }, 2);
        if (parts.Length < 2)
        {
            return false;
        }
        string serviceName = parts[1];
        try
        {
            if (enabled)
            {
                int originalStart = 2;
                using (RegistryKey? disabledKey = Registry.CurrentUser.OpenSubKey(DisabledServiceKeyPath))
                {
                    if (disabledKey != null)
                    {
                        object? originalValue = disabledKey.GetValue(serviceName);
                        if (originalValue is int)
                        {
                            originalStart = (int)originalValue;
                        }
                    }
                }
                using (RegistryKey? serviceKey = Registry.LocalMachine.OpenSubKey(
                    "SYSTEM\\CurrentControlSet\\Services\\" + serviceName, true))
                {
                    if (serviceKey == null)
                    {
                        return false;
                    }
                    serviceKey.SetValue("Start", originalStart, RegistryValueKind.DWord);
                }
                using (RegistryKey? disabledKey = Registry.CurrentUser.OpenSubKey(DisabledServiceKeyPath, true))
                {
                    if (disabledKey != null)
                    {
                        disabledKey.DeleteValue(serviceName, false);
                    }
                }
                return true;
            }
            int currentStart = -1;
            using (RegistryKey? readKey = Registry.LocalMachine.OpenSubKey(
                "SYSTEM\\CurrentControlSet\\Services\\" + serviceName))
            {
                if (readKey == null)
                {
                    return false;
                }
                object? startValue = readKey.GetValue("Start");
                if (startValue is int)
                {
                    currentStart = (int)startValue;
                }
            }
            using (RegistryKey? serviceKey = Registry.LocalMachine.OpenSubKey(
                "SYSTEM\\CurrentControlSet\\Services\\" + serviceName, true))
            {
                if (serviceKey == null)
                {
                    return false;
                }
                serviceKey.SetValue("Start", 4, RegistryValueKind.DWord);
            }
            using (RegistryKey disabledKey = Registry.CurrentUser.CreateSubKey(DisabledServiceKeyPath))
            {
                disabledKey.SetValue(serviceName, currentStart, RegistryValueKind.DWord);
            }
            return true;
        }
        catch
        {
            // 修改系统服务需要管理员权限，权限不足时返回失败
            return false;
        }
    }

    /// <summary>读取本程序禁用过的服务名称清单。</summary>
    private static HashSet<string> ReadDisabledServiceNames()
    {
        HashSet<string> names = new HashSet<string>();
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(DisabledServiceKeyPath))
            {
                if (key == null)
                {
                    return names;
                }
                foreach (string valueName in key.GetValueNames())
                {
                    names.Add(valueName);
                }
            }
        }
        catch
        {
            // 禁用清单不可读时按没有禁用记录处理
        }
        return names;
    }

    /// <summary>把启动项编号编码成合法的注册表值名称。</summary>
    private static string EncodeBackupName(string id)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(id));
    }

    /// <summary>把注册表值名称还原成启动项编号；无法解码时返回空文本。</summary>
    private static string DecodeBackupName(string encodedName)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encodedName));
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>把注册表值类型名称还原成枚举值；未知类型按字符串处理。</summary>
    private static RegistryValueKind ParseValueKind(string kindText)
    {
        if (string.Equals(kindText, "ExpandString", StringComparison.OrdinalIgnoreCase))
        {
            return RegistryValueKind.ExpandString;
        }
        if (string.Equals(kindText, "String", StringComparison.OrdinalIgnoreCase))
        {
            return RegistryValueKind.String;
        }
        return RegistryValueKind.String;
    }

    /// <summary>按目标路径的扩展名推断启动项类型。</summary>
    private static string DetectItemType(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".lnk" || extension == ".app" || extension == ".desktop")
        {
            return StartupItemType.Shortcut;
        }
        if (extension == ".url")
        {
            return StartupItemType.WebLink;
        }
        if (extension == ".bat" || extension == ".cmd" || extension == ".ps1" || extension == ".sh"
            || extension == ".py" || extension == ".js" || extension == ".vbs")
        {
            return StartupItemType.Script;
        }
        return StartupItemType.Program;
    }

    /// <summary>
    /// 拆分命令行：优先按成对双引号取可执行文件路径；没有引号时按最长且真实存在的文件路径前缀切分，
    /// 最后才按第一个空格切分（避免把带空格的未安装路径全部当成可执行文件名）。
    /// </summary>
    private static void SplitCommand(string rawText, out string command, out string arguments)
    {
        string text = rawText.Trim();
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            int closingQuote = text.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                command = text.Substring(1, closingQuote - 1);
                arguments = text.Substring(closingQuote + 1).Trim();
                return;
            }
        }
        for (int index = text.Length - 1; index > 0; index--)
        {
            if (text[index] != ' ')
            {
                continue;
            }
            string prefix = text.Substring(0, index);
            if (File.Exists(prefix))
            {
                command = prefix;
                arguments = text.Substring(index + 1).Trim();
                return;
            }
        }
        int firstSpace = text.IndexOf(' ');
        if (firstSpace > 0)
        {
            command = text.Substring(0, firstSpace);
            arguments = text.Substring(firstSpace + 1).Trim();
            return;
        }
        command = text;
        arguments = string.Empty;
    }

    /// <summary>判断当前进程是否以管理员身份运行。</summary>
    private static bool IsAdministrator()
    {
        try
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>枚举开机或登录触发的计划任务（通过任务计划程序 COM 接口递归读取）。</summary>
    private static void AddScheduledTasks(List<StartupItem> items, HashSet<string> seenIds, bool isAdministrator)
    {
        try
        {
            Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null)
            {
                return;
            }
            object? serviceObject = Activator.CreateInstance(serviceType);
            if (serviceObject == null)
            {
                return;
            }
            dynamic service = serviceObject;
            service.Connect();
            dynamic rootFolder = service.GetFolder("\\");
            AddScheduledTasksFromFolder(rootFolder, items, seenIds, isAdministrator);
        }
        catch
        {
            // 任务计划程序服务不可用时跳过该来源，不影响注册表、启动文件夹与服务枚举
        }
    }

    /// <summary>递归读取一个计划任务文件夹下的全部任务，只保留开机或登录触发的条目。</summary>
    private static void AddScheduledTasksFromFolder(dynamic folder, List<StartupItem> items,
        HashSet<string> seenIds, bool isAdministrator)
    {
        try
        {
            dynamic tasks = folder.GetTasks(1);
            int taskCount = (int)tasks.Count;
            for (int index = 1; index <= taskCount; index++)
            {
                try
                {
                    dynamic task = tasks.Item(index);
                    string xml = (string)task.Xml;
                    if (!HasStartupTrigger(xml))
                    {
                        continue;
                    }
                    string taskPath = (string)task.Path;
                    string taskName = (string)task.Name;
                    string id = "task|" + taskPath;
                    if (!seenIds.Add(id))
                    {
                        continue;
                    }
                    string command;
                    string arguments;
                    ReadTaskCommand(xml, out command, out arguments);
                    StartupItem item = new StartupItem();
                    item.Id = id;
                    item.Name = taskName;
                    item.Source = StartupItemSource.ScheduledTask;
                    item.ItemType = StartupItemType.Program;
                    item.Command = command.Length > 0 ? command : taskPath;
                    item.Arguments = arguments;
                    item.Location = taskPath;
                    item.Scope = StartupItemScope.AllUsers;
                    item.Enabled = (bool)task.Enabled;
                    item.CanToggle = isAdministrator;
                    item.ToggleHint = isAdministrator ? string.Empty : "启用或禁用计划任务需要管理员权限";
                    item.Description = "计划任务路径=" + taskPath;
                    items.Add(item);
                }
                catch
                {
                    // 单个任务读取失败时跳过该任务
                }
            }
        }
        catch
        {
            // 文件夹不可读时跳过该文件夹
        }
        try
        {
            dynamic subFolders = folder.GetFolders(0);
            int folderCount = (int)subFolders.Count;
            for (int index = 1; index <= folderCount; index++)
            {
                AddScheduledTasksFromFolder(subFolders.Item(index), items, seenIds, isAdministrator);
            }
        }
        catch
        {
            // 子文件夹读取失败时跳过该层
        }
    }

    /// <summary>判断计划任务 XML 是否包含开机触发或登录触发。</summary>
    private static bool HasStartupTrigger(string xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return false;
        }
        return xml.IndexOf("<BootTrigger", StringComparison.OrdinalIgnoreCase) >= 0
            || xml.IndexOf("<LogonTrigger", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>从计划任务 XML 中读取执行命令与参数。</summary>
    private static void ReadTaskCommand(string xml, out string command, out string arguments)
    {
        command = string.Empty;
        arguments = string.Empty;
        try
        {
            XDocument document = XDocument.Parse(xml);
            foreach (XElement element in document.Descendants())
            {
                if (string.Equals(element.Name.LocalName, "Command", StringComparison.OrdinalIgnoreCase)
                    && command.Length == 0)
                {
                    command = element.Value;
                }
                else if (string.Equals(element.Name.LocalName, "Arguments", StringComparison.OrdinalIgnoreCase)
                    && arguments.Length == 0)
                {
                    arguments = element.Value;
                }
            }
        }
        catch
        {
            // XML 解析失败时只展示任务路径
        }
    }

    /// <summary>启用或禁用计划任务。</summary>
    private static bool SetScheduledTaskEnabled(StartupItem item, bool enabled)
    {
        string[] parts = item.Id.Split(new char[] { '|' }, 2);
        if (parts.Length < 2)
        {
            return false;
        }
        string fullPath = parts[1];
        int lastSlash = fullPath.LastIndexOf('\\');
        if (lastSlash < 0)
        {
            return false;
        }
        string folderPath = lastSlash <= 0 ? "\\" : fullPath.Substring(0, lastSlash);
        string taskName = fullPath.Substring(lastSlash + 1);
        if (taskName.Length == 0)
        {
            return false;
        }
        try
        {
            Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null)
            {
                return false;
            }
            object? serviceObject = Activator.CreateInstance(serviceType);
            if (serviceObject == null)
            {
                return false;
            }
            dynamic service = serviceObject;
            service.Connect();
            dynamic folder = service.GetFolder(folderPath);
            dynamic task = folder.GetTask(taskName);
            task.Enabled = enabled;
            return (bool)task.Enabled == enabled;
        }
        catch
        {
            // 修改计划任务需要管理员权限，权限不足时返回失败
            return false;
        }
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
