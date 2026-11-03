using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Win32;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 系统真实右键菜单实现：按两套根键（当前用户、本地计算机）扫描 Classes 下的经典菜单位置，
/// 读取显示名、命令模板与启停标记；启停经由 LegacyDisable 值与新建菜单的 ShellNew/ShellOld 子键改名完成，
/// 唤起时把目标路径代入命令模板并按需以管理员身份运行；
/// Windows 11 新版菜单只按打包应用清单只读展示，不做启停。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsSystemContextMenuService : ISystemContextMenuService
{
    /// <summary>HKEY_CURRENT_USER 根键名，当前用户作用域。</summary>
    private const string CurrentUserRootName = "HKEY_CURRENT_USER";

    /// <summary>HKEY_LOCAL_MACHINE 根键名，本地计算机作用域。</summary>
    private const string LocalMachineRootName = "HKEY_LOCAL_MACHINE";

    /// <summary>HKEY_CURRENT_USER 下 Classes 的相对路径。</summary>
    private const string CurrentUserClassesPath = "Software\\Classes";

    /// <summary>HKEY_LOCAL_MACHINE 下 Classes 的相对路径。</summary>
    private const string LocalMachineClassesPath = "SOFTWARE\\Classes";

    /// <summary>禁用标记值名：经典菜单项的子键存在该值即表示已被禁用。</summary>
    private const string LegacyDisableValueName = "LegacyDisable";

    /// <summary>新建菜单已启用状态的子键名。</summary>
    private const string ShellNewSubKeyName = "ShellNew";

    /// <summary>新建菜单已禁用状态的子键名。</summary>
    private const string ShellOldSubKeyName = "ShellOld";

    /// <summary>Windows 11 新版菜单的打包应用仓库位置（相对根键，两套根键下路径写法一致）。</summary>
    private const string PackagesRelativePath = "Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\CurrentVersion\\AppModel\\Repository\\Packages";

    /// <summary>打包应用仓库位置里属于 Classes 的前缀，去掉后得到相对 Classes 的子键路径。</summary>
    private const string PackagesClassesPrefix = "Software\\Classes\\";

    /// <summary>Shell 通知事件码：文件关联已改变（十六进制 0x08000000），资源管理器收到后重建右键菜单缓存。</summary>
    private const int AssociationChangedEvent = 0x08000000;

    /// <summary>Shell 通知附加标志：空标识列表（十六进制 0x0000），表示通知不携带附加的项目标识数据。</summary>
    private const int EmptyIdListFlag = 0x0000;

    /// <summary>打开注册表键的期望访问权限：读取、写入、查询等全部常用权限（十六进制 0x000F003F）。</summary>
    private const int KeyAllAccess = 0x000F003F;

    /// <summary>HKEY_CURRENT_USER 的预定义根键句柄（由系统持有的固定句柄，无需关闭）。</summary>
    private static readonly IntPtr currentUserPredefinedHandle = new IntPtr(unchecked((int)0x80000001));

    /// <summary>HKEY_LOCAL_MACHINE 的预定义根键句柄（由系统持有的固定句柄，无需关闭）。</summary>
    private static readonly IntPtr localMachinePredefinedHandle = new IntPtr(unchecked((int)0x80000002));

    /// <summary>文件右键静态动词的扫描位置（相对 Classes 根）：每项为位置路径与适用对象标识。</summary>
    private static readonly (string LocationPath, string Selector)[] fileVerbLocations = new (string, string)[]
    {
        ("*\\shell", "*"),
        ("AllFilesystemObjects\\shell", "AllFilesystemObjects"),
        ("Directory\\shell", "Directory"),
        ("Drive\\shell", "Drive"),
        ("Folder\\shell", "Folder"),
    };

    /// <summary>文件右键外壳扩展处理器的扫描位置（相对 Classes 根）：每项为位置路径与适用对象标识。</summary>
    private static readonly (string LocationPath, string Selector)[] fileHandlerLocations = new (string, string)[]
    {
        ("*\\shellex\\ContextMenuHandlers", "*"),
        ("AllFilesystemObjects\\shellex\\ContextMenuHandlers", "AllFilesystemObjects"),
        ("Directory\\shellex\\ContextMenuHandlers", "Directory"),
        ("Drive\\shellex\\ContextMenuHandlers", "Drive"),
        ("Folder\\shellex\\ContextMenuHandlers", "Folder"),
    };

    /// <summary>桌面右键的扫描位置（相对 Classes 根）：每项为位置路径、适用对象标识、是否外壳扩展处理器。</summary>
    private static readonly (string LocationPath, string Selector, bool IsHandler)[] desktopLocations = new (string, string, bool)[]
    {
        ("Directory\\Background\\shell", "Directory", false),
        ("DesktopBackground\\Shell", "Desktop", false),
        ("LibraryFolder\\background\\shell", "Desktop", false),
        ("Directory\\Background\\shellex\\ContextMenuHandlers", "Directory", true),
    };

    /// <summary>扫描结果缓存：首次枚举或刷新时生成，启停成功后就地同步对应条目。</summary>
    private List<SystemContextMenuEntry>? cache;

    public bool IsSupported
    {
        get { return true; }
    }

    public IReadOnlyList<SystemContextMenuEntry> Enumerate()
    {
        // 首次调用做完整扫描并缓存，之后直接复用缓存结果
        if (cache == null)
        {
            cache = ScanAll();
        }
        return cache;
    }

    public void Refresh()
    {
        // 丢弃旧缓存重新扫描，供设置页刷新按钮调用
        cache = ScanAll();
    }

    public bool SetEnabled(SystemContextMenuEntry entry, bool enabled)
    {
        // 唯一拦截：只读条目（Windows 11 新版菜单没有官方启停开关，仅展示）
        if (entry.IsReadOnly)
        {
            return false;
        }
        bool success;
        if (entry.Group == SystemMenuGroup.ShellNew)
        {
            success = SetShellNewEnabled(entry, enabled);
        }
        else
        {
            success = SetClassicEnabled(entry, enabled);
        }
        if (!success)
        {
            return false;
        }
        entry.IsEnabled = enabled;
        SyncCacheEnabled(entry, enabled);
        // 通知资源管理器刷新右键菜单缓存，让启停立即生效
        NotifyShellAssociationChanged();
        return true;
    }

    public bool Execute(SystemContextMenuEntry entry, string targetPath)
    {
        if (!entry.CanExecute)
        {
            return false;
        }
        string expanded = SystemContextMenuPolicy.ExpandCommand(entry.CommandTemplate, targetPath);
        if (string.IsNullOrEmpty(expanded))
        {
            return false;
        }
        SystemContextMenuPolicy.SplitCommandLine(expanded, out string fileName, out string arguments);
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = true;
            // 标注需要提权的条目以管理员身份唤起，其余交由系统默认方式运行
            startInfo.Verb = entry.RequiresElevation ? "runas" : string.Empty;
            using (Process? startedProcess = Process.Start(startInfo))
            {
                // 进程已交由系统运行，这里仅保证托管句柄被释放
            }
            return true;
        }
        catch (Exception)
        {
            // 命令不存在、路径被拒绝或权限不足时视为唤起失败
            return false;
        }
    }

    /// <summary>取当前目标适用的系统外壳实时菜单（包含全部静态动词与已启用的外壳扩展）。</summary>
    public IReadOnlyList<SystemContextMenuEntry> EnumerateShellMenu(string targetPath, bool isBackground)
    {
        return WindowsShellContextMenuReader.Enumerate(targetPath, isBackground);
    }

    /// <summary>唤起系统外壳实时菜单项。</summary>
    public bool ExecuteShellMenuEntry(SystemContextMenuEntry entry, string targetPath, bool isBackground)
    {
        return WindowsShellContextMenuReader.Invoke(entry, targetPath, isBackground);
    }

    /// <summary>Windows 平台支持弹出系统原生外壳右键菜单。</summary>
    public bool SupportsNativeShellMenu
    {
        get { return true; }
    }

    /// <summary>弹出系统原生外壳右键菜单（本程序命令 + 静态动词 + 全部第三方扩展）。</summary>
    public Task<NativeContextMenuResult> ShowNativeShellMenuAsync(NativeContextMenuRequest request)
    {
        return WindowsShellMenuPopup.ShowAsync(request);
    }

    /// <summary>网页端用：枚举多选 / 空白处的系统外壳菜单层级（保留禁用项与子菜单）。</summary>
    public IReadOnlyList<SystemContextMenuEntry> EnumerateShellMenu(IReadOnlyList<string> paths, bool isBackground)
    {
        return WindowsShellContextMenuReader.EnumerateMultiple(paths, isBackground);
    }

    /// <summary>网页端用：按绝对命令编号唤起系统外壳命令。</summary>
    public bool ExecuteShellMenuCommand(int commandId, IReadOnlyList<string> paths, bool isBackground)
    {
        return WindowsShellContextMenuReader.InvokeByCommandId(commandId, paths, isBackground);
    }

    /// <summary>扫描两套根键下的全部系统右键菜单位置，生成条目列表。</summary>
    private static List<SystemContextMenuEntry> ScanAll()
    {
        List<SystemContextMenuEntry> entries = new List<SystemContextMenuEntry>();
        ScanRoot(CurrentUserRootName, entries);
        ScanRoot(LocalMachineRootName, entries);
        return entries;
    }

    /// <summary>扫描单套根键：文件右键、桌面右键、新建菜单与 Windows 11 新版菜单。</summary>
    private static void ScanRoot(string rootName, List<SystemContextMenuEntry> entries)
    {
        string classesPath = ClassesPathOf(rootName);
        using RegistryKey rootKey = OpenRootKey(rootName);
        using RegistryKey? classesKey = rootKey.OpenSubKey(classesPath);
        if (classesKey == null)
        {
            return;
        }
        // 文件右键：静态动词位置
        foreach ((string locationPath, string selector) in fileVerbLocations)
        {
            ScanVerbLocation(classesKey, rootName, classesPath, locationPath, selector, SystemMenuGroup.FileContext, entries);
        }
        // 文件右键：外壳扩展处理器位置
        foreach ((string locationPath, string selector) in fileHandlerLocations)
        {
            ScanHandlerLocation(classesKey, rootName, classesPath, locationPath, selector, SystemMenuGroup.FileContext, entries);
        }
        // 文件右键：按扩展名的程序标识，每个子键再取其 shell 子键
        ScanSystemFileAssociations(classesKey, rootName, classesPath, entries);
        // 桌面右键：静态动词与外壳扩展处理器混合
        foreach ((string locationPath, string selector, bool isHandler) in desktopLocations)
        {
            if (isHandler)
            {
                ScanHandlerLocation(classesKey, rootName, classesPath, locationPath, selector, SystemMenuGroup.DesktopContext, entries);
            }
            else
            {
                ScanVerbLocation(classesKey, rootName, classesPath, locationPath, selector, SystemMenuGroup.DesktopContext, entries);
            }
        }
        // 新建菜单：扩展名基础键下的 ShellNew / ShellOld 状态子键
        ScanShellNew(classesKey, rootName, classesPath, entries);
        // Windows 11 新版菜单：打包应用仓库，按清单只读展示
        ScanModernMenu(rootKey, rootName, entries);
    }

    /// <summary>扫描一个静态动词位置：其下每个子键是一条菜单项。</summary>
    private static void ScanVerbLocation(RegistryKey classesKey, string rootName, string classesPath, string locationPath, string selector, SystemMenuGroup group, List<SystemContextMenuEntry> entries)
    {
        using RegistryKey? locationKey = classesKey.OpenSubKey(locationPath);
        if (locationKey == null)
        {
            return;
        }
        foreach (string verbName in locationKey.GetSubKeyNames())
        {
            using RegistryKey? verbKey = locationKey.OpenSubKey(verbName);
            if (verbKey == null)
            {
                continue;
            }
            entries.Add(CreateVerbEntry(rootName, classesPath, locationPath, verbName, selector, group, verbKey));
        }
    }

    /// <summary>扫描一个外壳扩展处理器位置：其下每个子键是一条菜单项。</summary>
    private static void ScanHandlerLocation(RegistryKey classesKey, string rootName, string classesPath, string locationPath, string selector, SystemMenuGroup group, List<SystemContextMenuEntry> entries)
    {
        using RegistryKey? locationKey = classesKey.OpenSubKey(locationPath);
        if (locationKey == null)
        {
            return;
        }
        foreach (string handlerName in locationKey.GetSubKeyNames())
        {
            using RegistryKey? handlerKey = locationKey.OpenSubKey(handlerName);
            if (handlerKey == null)
            {
                continue;
            }
            entries.Add(CreateHandlerEntry(rootName, classesPath, locationPath, handlerName, selector, group, handlerKey));
        }
    }

    /// <summary>扫描 SystemFileAssociations：每个子键是一个程序标识，取其 shell 子键下的动词，选择器取程序标识本身并转小写。</summary>
    private static void ScanSystemFileAssociations(RegistryKey classesKey, string rootName, string classesPath, List<SystemContextMenuEntry> entries)
    {
        using RegistryKey? associationsKey = classesKey.OpenSubKey("SystemFileAssociations");
        if (associationsKey == null)
        {
            return;
        }
        foreach (string typeName in associationsKey.GetSubKeyNames())
        {
            string locationPath = "SystemFileAssociations\\" + typeName + "\\shell";
            ScanVerbLocation(classesKey, rootName, classesPath, locationPath, typeName.ToLowerInvariant(), SystemMenuGroup.FileContext, entries);
        }
    }

    /// <summary>
    /// 扫描新建菜单：枚举 Classes 根下看起来像扩展名的子键（以点开头且长度不超过 12），
    /// 存在 ShellNew 子键视为已启用，存在 ShellOld 子键视为已禁用，两者皆无则跳过。
    /// </summary>
    private static void ScanShellNew(RegistryKey classesKey, string rootName, string classesPath, List<SystemContextMenuEntry> entries)
    {
        foreach (string subKeyName in classesKey.GetSubKeyNames())
        {
            if (!subKeyName.StartsWith(".", StringComparison.Ordinal) || subKeyName.Length > 12)
            {
                continue;
            }
            using RegistryKey? extensionKey = classesKey.OpenSubKey(subKeyName);
            if (extensionKey == null)
            {
                continue;
            }
            bool hasShellNew;
            bool hasShellOld;
            using (RegistryKey? shellNewKey = extensionKey.OpenSubKey(ShellNewSubKeyName))
            {
                hasShellNew = shellNewKey != null;
            }
            using (RegistryKey? shellOldKey = extensionKey.OpenSubKey(ShellOldSubKeyName))
            {
                hasShellOld = shellOldKey != null;
            }
            if (!hasShellNew && !hasShellOld)
            {
                continue;
            }
            string registryPath = rootName + "\\" + classesPath + "\\" + subKeyName;
            SystemContextMenuEntry entry = new SystemContextMenuEntry();
            entry.DisplayName = subKeyName + "（新建）";
            entry.Group = SystemMenuGroup.ShellNew;
            entry.RegistryRootName = rootName;
            entry.RegistryPath = registryPath;
            entry.ClassesRelativePath = subKeyName;
            entry.ContextSelector = subKeyName;
            entry.CommandTemplate = string.Empty;
            entry.SourceDescription = registryPath;
            entry.IsSystemItem = true;
            entry.IsEnabled = hasShellNew;
            entry.RequiresAdministrator = IsLocalMachineRoot(rootName);
            entry.IsReadOnly = false;
            entry.CanExecute = false;
            entry.RequiresElevation = false;
            entries.Add(entry);
        }
    }

    /// <summary>
    /// 扫描 Windows 11 新版菜单：枚举打包应用仓库的子键，取安装目录下的应用清单，
    /// 清单声明了资源管理器命令接口才生成只读条目；单个包处理失败只跳过该包，不影响其余包。
    /// </summary>
    private static void ScanModernMenu(RegistryKey rootKey, string rootName, List<SystemContextMenuEntry> entries)
    {
        using RegistryKey? packagesKey = rootKey.OpenSubKey(PackagesRelativePath);
        if (packagesKey == null)
        {
            return;
        }
        foreach (string packageName in packagesKey.GetSubKeyNames())
        {
            try
            {
                using RegistryKey? packageKey = packagesKey.OpenSubKey(packageName);
                if (packageKey == null)
                {
                    continue;
                }
                string? installDirectory = packageKey.GetValue("PackageRootFolder") as string;
                if (string.IsNullOrEmpty(installDirectory))
                {
                    continue;
                }
                string manifestPath = Path.Combine(installDirectory, "AppxManifest.xml");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }
                // 清单可能是带字节序标记或不带标记的 UTF-8 文本，按默认方式整体读取
                string manifestText = File.ReadAllText(manifestPath);
                bool declaresExplorerCommand = manifestText.Contains("windows.explorerCommand", StringComparison.OrdinalIgnoreCase)
                    || manifestText.Contains("IExplorerCommand", StringComparison.OrdinalIgnoreCase)
                    || manifestText.Contains("ContextMenu", StringComparison.OrdinalIgnoreCase);
                if (!declaresExplorerCommand)
                {
                    continue;
                }
                SystemContextMenuEntry entry = new SystemContextMenuEntry();
                // 显示名取包标识（安装的包名，原样展示）
                entry.DisplayName = packageName;
                entry.Group = SystemMenuGroup.ModernMenu;
                entry.RegistryRootName = rootName;
                entry.RegistryPath = rootName + "\\" + PackagesRelativePath + "\\" + packageName;
                entry.ClassesRelativePath = PackagesRelativePath.Substring(PackagesClassesPrefix.Length) + "\\" + packageName;
                entry.ContextSelector = string.Empty;
                entry.CommandTemplate = string.Empty;
                entry.SourceDescription = installDirectory;
                entry.IsSystemItem = false;
                entry.IsEnabled = true;
                entry.RequiresAdministrator = IsLocalMachineRoot(rootName);
                entry.IsReadOnly = true;
                entry.CanExecute = false;
                entry.RequiresElevation = false;
                entries.Add(entry);
            }
            catch (Exception)
            {
                // 单个打包应用处理失败（清单损坏、目录不可读等）时跳过，保证其余包照常展示
            }
        }
    }

    /// <summary>生成一条静态动词菜单项：显示名优先自定义动词名，命令模板取 command 子键默认值。</summary>
    private static SystemContextMenuEntry CreateVerbEntry(string rootName, string classesPath, string locationPath, string verbName, string selector, SystemMenuGroup group, RegistryKey verbKey)
    {
        string relativePath = locationPath + "\\" + verbName;
        // 显示名依次取自定义动词名、子键默认值，最后回落子键名
        string? displayName = verbKey.GetValue("MUIVerb") as string;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = verbKey.GetValue(string.Empty) as string;
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = verbName;
        }
        string commandTemplate = string.Empty;
        using (RegistryKey? commandKey = verbKey.OpenSubKey("command"))
        {
            if (commandKey != null)
            {
                commandTemplate = commandKey.GetValue(string.Empty) as string ?? string.Empty;
            }
        }
        SystemContextMenuEntry entry = new SystemContextMenuEntry();
        entry.DisplayName = CleanDisplayName(displayName);
        entry.Group = group;
        entry.RegistryRootName = rootName;
        entry.RegistryPath = rootName + "\\" + classesPath + "\\" + relativePath;
        entry.ClassesRelativePath = relativePath;
        entry.ContextSelector = selector;
        entry.CommandTemplate = commandTemplate;
        entry.SourceDescription = string.Empty;
        entry.IsSystemItem = IsCommandInWindowsDirectory(commandTemplate);
        // 存在 LegacyDisable 值即表示该菜单项已被禁用
        entry.IsEnabled = verbKey.GetValue(LegacyDisableValueName) == null;
        entry.RequiresAdministrator = IsLocalMachineRoot(rootName);
        entry.IsReadOnly = false;
        entry.CanExecute = !string.IsNullOrEmpty(commandTemplate);
        entry.RequiresElevation = string.Equals(verbName, "runas", StringComparison.OrdinalIgnoreCase);
        return entry;
    }

    /// <summary>
    /// 生成一条外壳扩展处理器菜单项：没有命令模板，不可唤起；
    /// 子键名形如全局唯一标识符时经 CLSID 键解析显示名与服务动态链接库。
    /// </summary>
    private static SystemContextMenuEntry CreateHandlerEntry(string rootName, string classesPath, string locationPath, string handlerName, string selector, SystemMenuGroup group, RegistryKey handlerKey)
    {
        string relativePath = locationPath + "\\" + handlerName;
        string displayName = handlerName;
        string sourceDescription = string.Empty;
        string serverPath = string.Empty;
        if (LooksLikeGuid(handlerName))
        {
            // 形如全局唯一标识符的处理器：从 CLSID 键取显示名，从进程内服务器子键取服务动态链接库
            string clsidRelativePath = "CLSID\\" + handlerName;
            string? clsidName = null;
            using (RegistryKey? clsidKey = OpenClassesSubKey(rootName, classesPath, clsidRelativePath))
            {
                if (clsidKey != null)
                {
                    clsidName = clsidKey.GetValue(string.Empty) as string;
                }
            }
            using (RegistryKey? serverKey = OpenClassesSubKey(rootName, classesPath, clsidRelativePath + "\\InProcServer32"))
            {
                if (serverKey != null)
                {
                    serverPath = serverKey.GetValue(string.Empty) as string ?? string.Empty;
                }
            }
            if (string.IsNullOrEmpty(clsidName) || clsidName.StartsWith("@", StringComparison.Ordinal))
            {
                // CLSID 默认值为空或是 @ 资源间接写法时，改取服务动态链接库的文件说明
                string fileDescription = ReadFileDescription(serverPath);
                if (!string.IsNullOrEmpty(fileDescription))
                {
                    clsidName = fileDescription;
                }
            }
            if (!string.IsNullOrEmpty(clsidName) && !clsidName.StartsWith("@", StringComparison.Ordinal))
            {
                displayName = clsidName;
            }
            if (!string.IsNullOrEmpty(serverPath))
            {
                sourceDescription = clsidRelativePath + " → " + serverPath;
            }
        }
        SystemContextMenuEntry entry = new SystemContextMenuEntry();
        entry.DisplayName = CleanDisplayName(displayName);
        entry.Group = group;
        entry.RegistryRootName = rootName;
        entry.RegistryPath = rootName + "\\" + classesPath + "\\" + relativePath;
        entry.ClassesRelativePath = relativePath;
        entry.ContextSelector = selector;
        // 外壳扩展处理器没有可直接执行的命令模板
        entry.CommandTemplate = string.Empty;
        entry.SourceDescription = sourceDescription;
        entry.IsSystemItem = IsPathInWindowsDirectory(serverPath);
        entry.IsEnabled = handlerKey.GetValue(LegacyDisableValueName) == null;
        entry.RequiresAdministrator = IsLocalMachineRoot(rootName);
        entry.IsReadOnly = false;
        entry.CanExecute = false;
        entry.RequiresElevation = string.Equals(handlerName, "runas", StringComparison.OrdinalIgnoreCase);
        return entry;
    }

    /// <summary>清理显示名：注册表菜单文本里的 &amp; 是 Windows 加速键标记，程序界面不识别，统一去掉。</summary>
    private static string CleanDisplayName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            return string.Empty;
        }
        return displayName.Replace("&", string.Empty).Trim();
    }

    /// <summary>
    /// 启停新建菜单条目：目标状态的子键已存在则直接视为成功，
    /// 否则把 ShellNew 与 ShellOld 子键互相改名完成状态切换。
    /// </summary>
    private static bool SetShellNewEnabled(SystemContextMenuEntry entry, bool enabled)
    {
        try
        {
            using RegistryKey? extensionKey = OpenWritableClassesSubKey(entry);
            if (extensionKey == null)
            {
                return false;
            }
            string targetName = enabled ? ShellNewSubKeyName : ShellOldSubKeyName;
            string sourceName = enabled ? ShellOldSubKeyName : ShellNewSubKeyName;
            using (RegistryKey? targetKey = extensionKey.OpenSubKey(targetName))
            {
                if (targetKey != null)
                {
                    // 目标状态子键已存在，说明该扩展名已处于期望状态
                    return true;
                }
            }
            // 改名接口需要父键句柄：按根键与相对路径以全部权限单独打开父键，用完立即关闭
            string parentPath = ClassesPathOf(entry.RegistryRootName) + "\\" + entry.ClassesRelativePath;
            int openResult = RegOpenKeyExW(RootHandleOf(entry.RegistryRootName), parentPath, 0, KeyAllAccess, out IntPtr parentHandle);
            if (openResult != 0)
            {
                // 打开父键失败（多见于本地计算机键缺少管理员权限）
                return false;
            }
            try
            {
                // 把源状态子键改为目标状态子键，返回 0 表示成功
                int renameResult = RegRenameKey(parentHandle, sourceName, targetName);
                return renameResult == 0;
            }
            finally
            {
                RegCloseKey(parentHandle);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 注册表项没有写入权限
            return false;
        }
        catch (SecurityException)
        {
            // 当前账户缺少修改注册表的安全权限
            return false;
        }
    }

    /// <summary>
    /// 启停经典菜单条目：禁用写入 LegacyDisable 值，启用删除该值；
    /// 注册表项不可写（非管理员访问本地计算机键等）时返回失败。
    /// </summary>
    private static bool SetClassicEnabled(SystemContextMenuEntry entry, bool enabled)
    {
        try
        {
            using RegistryKey? verbKey = OpenWritableClassesSubKey(entry);
            if (verbKey == null)
            {
                return false;
            }
            if (enabled)
            {
                verbKey.DeleteValue(LegacyDisableValueName, false);
            }
            else
            {
                verbKey.SetValue(LegacyDisableValueName, string.Empty, RegistryValueKind.String);
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // 注册表项没有写入权限
            return false;
        }
        catch (SecurityException)
        {
            // 当前账户缺少修改注册表的安全权限
            return false;
        }
    }

    /// <summary>按条目的根键与相对 Classes 路径打开可写子键，打开失败返回空。</summary>
    private static RegistryKey? OpenWritableClassesSubKey(SystemContextMenuEntry entry)
    {
        using RegistryKey rootKey = OpenRootKey(entry.RegistryRootName);
        return rootKey.OpenSubKey(ClassesPathOf(entry.RegistryRootName) + "\\" + entry.ClassesRelativePath, true);
    }

    /// <summary>按根键名打开指定相对路径的 Classes 子键（只读），根键句柄用完即释放。</summary>
    private static RegistryKey? OpenClassesSubKey(string rootName, string classesPath, string relativePath)
    {
        using RegistryKey rootKey = OpenRootKey(rootName);
        return rootKey.OpenSubKey(classesPath + "\\" + relativePath);
    }

    /// <summary>把启停结果同步到缓存里同一条目，保证界面状态与注册表一致。</summary>
    private void SyncCacheEnabled(SystemContextMenuEntry changedEntry, bool enabled)
    {
        if (cache == null)
        {
            return;
        }
        foreach (SystemContextMenuEntry cachedEntry in cache)
        {
            if (cachedEntry.Group != changedEntry.Group)
            {
                continue;
            }
            if (!string.Equals(cachedEntry.RegistryRootName, changedEntry.RegistryRootName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!string.Equals(cachedEntry.ClassesRelativePath, changedEntry.ClassesRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            cachedEntry.IsEnabled = enabled;
        }
    }

    /// <summary>按根键名打开根键：本地计算机或当前用户，视图取本进程默认位数。</summary>
    private static RegistryKey OpenRootKey(string rootName)
    {
        RegistryHive hive = IsLocalMachineRoot(rootName) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        return RegistryKey.OpenBaseKey(hive, RegistryView.Default);
    }

    /// <summary>取根键名对应的预定义根键句柄（预定义句柄由系统持有，不需关闭）。</summary>
    private static IntPtr RootHandleOf(string rootName)
    {
        return IsLocalMachineRoot(rootName) ? localMachinePredefinedHandle : currentUserPredefinedHandle;
    }

    /// <summary>取根键名对应的 Classes 相对路径（两套根键大小写写法不同）。</summary>
    private static string ClassesPathOf(string rootName)
    {
        return IsLocalMachineRoot(rootName) ? LocalMachineClassesPath : CurrentUserClassesPath;
    }

    /// <summary>判断条目是否位于本地计算机根键（需要管理员权限才能改写）。</summary>
    private static bool IsLocalMachineRoot(string rootName)
    {
        return string.Equals(rootName, LocalMachineRootName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断子键名是否形如全局唯一标识符：以左花括号开头且含连字符，或是纯 32 位十六进制字符。</summary>
    private static bool LooksLikeGuid(string name)
    {
        if (name.StartsWith("{", StringComparison.Ordinal) && name.IndexOf('-') >= 0)
        {
            return true;
        }
        if (name.Length != 32)
        {
            return false;
        }
        foreach (char character in name)
        {
            bool isHexDigit = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');
            if (!isHexDigit)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>读取动态链接库的文件说明（版本信息里的文件描述），取不到时返回空串。</summary>
    private static string ReadFileDescription(string serverPath)
    {
        if (string.IsNullOrWhiteSpace(serverPath))
        {
            return string.Empty;
        }
        try
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(serverPath);
            return versionInfo.FileDescription ?? string.Empty;
        }
        catch (Exception)
        {
            // 文件不存在或路径非法时取不到版本信息，回落为空
            return string.Empty;
        }
    }

    /// <summary>判断命令模板代入环境变量并拆分后，可执行文件是否位于 Windows 安装目录下。</summary>
    private static bool IsCommandInWindowsDirectory(string commandTemplate)
    {
        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            return false;
        }
        string expanded = Environment.ExpandEnvironmentVariables(commandTemplate);
        SystemContextMenuPolicy.SplitCommandLine(expanded, out string fileName, out _);
        return IsPathInWindowsDirectory(fileName);
    }

    /// <summary>判断路径是否位于 Windows 安装目录下（用于标注系统自带菜单项，避免用户误禁用）。</summary>
    private static bool IsPathInWindowsDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windowsDirectory))
        {
            return false;
        }
        return path.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 通知资源管理器刷新：事件码为文件关联已改变，附加标志为空标识列表，两个附加参数传空指针表示没有附加数据。
    /// </summary>
    private static void NotifyShellAssociationChanged()
    {
        SHChangeNotify(AssociationChangedEvent, EmptyIdListFlag, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>向 Shell 发送系统事件：事件码、附加数据格式与两个附加数据指针，用于让资源管理器立即刷新右键菜单。</summary>
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>重命名注册表子键（仅宽字符接口）：父键句柄加源子键名与新子键名，返回 0 表示成功，用于新建菜单状态切换。</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegRenameKey(IntPtr hKey, string lpSubKeyName, string lpNewKeyName);

    /// <summary>打开注册表子键（仅宽字符接口）：父键句柄、相对路径、保留参数、期望权限与输出句柄，返回 0 表示成功，用于取得改名接口所需的父键句柄。</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    /// <summary>关闭注册表键句柄：子键改名完成后释放临时打开的父键句柄。</summary>
    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern int RegCloseKey(IntPtr hKey);
}
