namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 系统右键菜单条目的分组：与火绒右键菜单管理器一致的三组经典菜单，外加 Win11 新版菜单只读组。
/// </summary>
public enum SystemMenuGroup
{
    /// <summary>文件右键：资源管理器里右键文件或文件夹出现的菜单项。</summary>
    FileContext,

    /// <summary>桌面右键：桌面与目录空白处出现的菜单项。</summary>
    DesktopContext,

    /// <summary>新建菜单：新建子菜单里的条目。</summary>
    ShellNew,

    /// <summary>Windows 11 新版菜单：打包应用提供，只读展示，不做启停。</summary>
    ModernMenu,
}

/// <summary>
/// 一条系统右键菜单项：来自注册表经典菜单位置（或 Win11 打包应用清单），
/// 记录显示名、注册表位置、命令模板与启停状态，供本程序右键菜单展示与设置页管理。
/// </summary>
public class SystemContextMenuEntry
{
    /// <summary>显示名称：静态动词取 MUIVerb 或默认值，无则取子键名。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>所属分组。</summary>
    public SystemMenuGroup Group { get; set; } = SystemMenuGroup.FileContext;

    /// <summary>注册表根键名称，例如 HKEY_CURRENT_USER 或 HKEY_LOCAL_MACHINE。</summary>
    public string RegistryRootName { get; set; } = string.Empty;

    /// <summary>完整注册表路径（含根键），用于界面展示与日志记录。</summary>
    public string RegistryPath { get; set; } = string.Empty;

    /// <summary>
    /// 相对 Classes 根的子键路径：经典菜单为动词或处理器子键路径，新建菜单为扩展名基础键（如 .txt）。
    /// 平台层凭根键名与本路径重新打开注册表执行启停。
    /// </summary>
    public string ClassesRelativePath { get; set; } = string.Empty;

    /// <summary>
    /// 适用对象标识：*、Directory、Drive、Folder、AllFilesystemObjects、.扩展名。
    /// 由策略按右键目标筛选是否展示，见 SystemContextMenuPolicy.SelectForTarget。
    /// </summary>
    public string ContextSelector { get; set; } = string.Empty;

    /// <summary>命令模板（含 %1、%V 等占位符）；shellex 处理器与只读项为空。</summary>
    public string CommandTemplate { get; set; } = string.Empty;

    /// <summary>来源说明：处理器的 CLSID 与服务 DLL，或 Win11 打包应用的安装目录。</summary>
    public string SourceDescription { get; set; } = string.Empty;

    /// <summary>是否系统自带菜单项（命令或服务 DLL 位于 Windows 目录）：界面标注保护，避免用户禁用关键项。</summary>
    public bool IsSystemItem { get; set; }

    /// <summary>当前是否已启用：经典菜单看 LegacyDisable 值，新建菜单看 ShellNew 子键是否存在。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>是否需要管理员权限（条目位于 HKLM 机器级键）。</summary>
    public bool RequiresAdministrator { get; set; }

    /// <summary>是否只读（Win11 新版菜单无官方启停开关，仅展示）。</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>是否可在本程序右键菜单里直接唤起（静态动词且有命令模板）。</summary>
    public bool CanExecute { get; set; }

    /// <summary>唤起时是否需要管理员权限（动词名为 runas 即以管理员身份运行）。</summary>
    public bool RequiresElevation { get; set; }
}
