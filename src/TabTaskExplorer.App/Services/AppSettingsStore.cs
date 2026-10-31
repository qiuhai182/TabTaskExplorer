using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TabTaskExplorer.Kernel.Media;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 应用设置：与 TabX Explorer 一致的持久化配置项。
/// </summary>
public class AppSettings
{
    /// <summary>是否显示隐藏的文件与文件夹。</summary>
    public bool ShowHiddenFiles { get; set; } = false;

    /// <summary>是否显示文件扩展名。</summary>
    public bool ShowFileExtensions { get; set; } = true;

    /// <summary>双击标签是否关闭当前标签。</summary>
    public bool DoubleClickTabClose { get; set; } = true;

    /// <summary>界面主题：Dark（默认深色）或 Light（浅色），可由标题栏按钮或网页端服务切换。</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>关闭窗口时是否最小化到托盘常驻。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>是否显示工具栏。</summary>
    public bool ShowToolbar { get; set; } = true;

    /// <summary>是否显示书签栏。</summary>
    public bool ShowBookmarkBar { get; set; } = true;

    /// <summary>是否显示窗口顶部的白色边框条（Avalonia 自绘的窗口装饰边框，关闭后窗口无可见边框）。</summary>
    public bool ShowTopWhiteStrip { get; set; } = false;

    /// <summary>是否开机自启（Windows 注册表 / Linux 自启动 / macOS LaunchAgent）。</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>是否随程序启动网页端服务（仅绑定本机回环地址）。</summary>
    public bool EnableWebService { get; set; } = true;

    /// <summary>网页端服务监听端口（仅本机回环地址，默认 15919）。</summary>
    public int WebServicePort { get; set; } = 15919;

    /// <summary>
    /// 网页端端口清单：每个端口独立配置，任意数量；同一权限方案可被多个端口共用。
    /// 首次升级时由旧的单端口配置（EnableWebService / WebServicePort）自动迁移而来。
    /// </summary>
    public List<WebPortSetting> WebPorts { get; set; } = new List<WebPortSetting>();

    /// <summary>网页端权限方案清单：描述来源 IP、可访问路径、读写与增删权限、是否需要账号密码。</summary>
    public List<WebAccessProfile> WebAccessProfiles { get; set; } = new List<WebAccessProfile>();

    /// <summary>启动时是否恢复上次关闭的标签页。</summary>
    public bool RestoreLastTabs { get; set; } = false;

    /// <summary>上次关闭时打开的标签页路径列表。</summary>
    public List<string> OpenTabPaths { get; set; } = new List<string>();

    /// <summary>是否默认用本程序打开文件夹（Windows，仅当前用户）。</summary>
    public bool DefaultFileManager { get; set; } = false;

    /// <summary>快捷键配置。</summary>
    public List<HotkeySetting> Hotkeys { get; set; } = HotkeySetting.CreateDefaults();

    /// <summary>固定到快捷访问的路径列表。</summary>
    public List<string> PinnedPaths { get; set; } = new List<string>();

    /// <summary>远程终端快捷命令列表（终端面板一键发送，参考 Xshell 快捷命令）。</summary>
    public List<string> RemoteQuickCommands { get; set; } = new List<string>
    {
        "ls -la", "df -h", "free -m", "uptime",
    };

    /// <summary>编辑器等宽字体回退链（默认 Cascadia Code，回退 Consolas / 等宽字体）。</summary>
    public string EditorFontFamily { get; set; } = "Cascadia Code, Consolas, DejaVu Sans Mono, monospace";

    /// <summary>编辑器字号（默认 13）。</summary>
    public double EditorFontSize { get; set; } = 13;

    /// <summary>Tab 宽度（按空格数计算，默认 4）。</summary>
    public int EditorTabWidth { get; set; } = 4;

    /// <summary>按下 Tab 时是否用空格替换（默认启用）。</summary>
    public bool EditorInsertSpaces { get; set; } = true;

    /// <summary>编辑器是否自动换行（默认关闭，与 VSCode 代码编辑一致）。</summary>
    public bool EditorWordWrap { get; set; } = false;

    /// <summary>是否显示空白符（原生编辑器暂不渲染，仅保留设置项）。</summary>
    public bool EditorShowWhitespace { get; set; } = false;

    /// <summary>默认打开模式：Explorer（资源管理器）或 Ide（IDE 模式）。</summary>
    public string DefaultOpenMode { get; set; } = "Explorer";

    /// <summary>IDE 模式上次使用的工作区根路径（本机路径，可空）。</summary>
    public string IdeWorkspacePath { get; set; } = string.Empty;

    /// <summary>IDE 模式下打开文件的默认优先级：Builtin（优先内置编辑器，默认）或 System（优先系统默认程序）。</summary>
    public string FileOpenDefaultPriority { get; set; } = FileOpenPriority.Builtin;

    /// <summary>按文件类型覆盖打开方式的规则列表（优先于默认优先级）。</summary>
    public List<FileOpenRule> FileOpenRules { get; set; } = new List<FileOpenRule>();

    /// <summary>媒体打开方式：Builtin（内置媒体标签，默认）/ System（系统默认程序）/ Ask（每次询问）。</summary>
    public string MediaOpenMode { get; set; } = MediaOpenModeValues.Builtin;

    /// <summary>按媒体大类覆盖打开方式的规则列表（图片 / 视频 / 音频可各自设置）。</summary>
    public List<MediaOpenRule> MediaOpenRules { get; set; } = new List<MediaOpenRule>();

    /// <summary>是否显示播放列表侧边栏（默认显示）。</summary>
    public bool MediaShowPlaylist { get; set; } = true;

    /// <summary>播放列表侧边栏宽度（像素，默认 260）。</summary>
    public double MediaPlaylistWidth { get; set; } = 260;

    /// <summary>播放列表收录范围：SameKind（只列同类，默认）/ AllMedia（同目录全部媒体）。</summary>
    public string MediaPlaylistScope { get; set; } = "SameKind";

    /// <summary>播放结束后是否自动播放下一项（默认开启）。</summary>
    public bool MediaAutoPlayNext { get; set; } = true;

    /// <summary>同一文件重复双击时是否复用已打开的媒体标签（默认复用；关闭则每次新开）。</summary>
    public bool MediaReuseTab { get; set; } = true;

    /// <summary>图片解码最大边长（像素，默认 4096；0 表示不限制，超大图按此降采样）。</summary>
    public int MediaImageMaxDecodeSize { get; set; } = 4096;

    /// <summary>上次音量（0-100）。</summary>
    public int MediaVolume { get; set; } = 80;

    /// <summary>上次播放倍速（0.25 - 4.0）。</summary>
    public double MediaRate { get; set; } = 1.0;
}

/// <summary>
/// 应用设置存储：读写 JSON 配置文件。
/// </summary>
public class AppSettingsStore
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private readonly string filePath;

    public AppSettingsStore()
    {
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabTaskExplorer");
        this.filePath = Path.Combine(dataDirectory, "settings.json");
    }

    /// <summary>读取设置；文件缺失或解析失败时返回默认设置。</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(this.filePath))
            {
                string json = File.ReadAllText(this.filePath);
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, jsonOptions);
                if (settings != null)
                {
                    EnsureDefaultHotkeys(settings);
                    EnsureFileOpenRules(settings);
                    EnsureWebSettings(settings);
                    return settings;
                }
            }
        }
        catch
        {
            // 文件损坏时回落默认设置，不影响程序启动
        }
        return new AppSettings();
    }

    /// <summary>保存设置；写入失败时静默跳过，不影响主流程。</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string? dataDirectory = Path.GetDirectoryName(this.filePath);
            if (!string.IsNullOrEmpty(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }
            string json = JsonSerializer.Serialize(settings, jsonOptions);
            File.WriteAllText(this.filePath, json);
        }
        catch
        {
            // 磁盘不可写等异常不阻断界面操作
        }
    }

    /// <summary>
    /// 补齐快捷键清单：旧配置文件缺少新增动作（IDE 保存 / 快速打开等）时追加默认项，
    /// 已存在的动作保持用户自定义值不变。
    /// </summary>
    private static void EnsureDefaultHotkeys(AppSettings settings)
    {
        if (settings.Hotkeys == null)
        {
            settings.Hotkeys = HotkeySetting.CreateDefaults();
            return;
        }
        foreach (HotkeySetting candidate in HotkeySetting.CreateDefaults())
        {
            bool exists = false;
            foreach (HotkeySetting existing in settings.Hotkeys)
            {
                if (string.Equals(existing.ActionName, candidate.ActionName, StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                settings.Hotkeys.Add(candidate);
            }
        }
    }

    /// <summary>
    /// 补齐打开方式设置：旧配置文件缺少规则列表时补空列表，缺少默认优先级时回落为「优先内置编辑器」。
    /// </summary>
    private static void EnsureFileOpenRules(AppSettings settings)
    {
        if (settings.FileOpenRules == null)
        {
            settings.FileOpenRules = new List<FileOpenRule>();
        }
        if (string.IsNullOrEmpty(settings.FileOpenDefaultPriority))
        {
            settings.FileOpenDefaultPriority = FileOpenPriority.Builtin;
        }
    }

    /// <summary>
    /// 补齐网页端配置：旧版本只有单端口配置，首次升级时迁移为一个端口方案；
    /// 已有多端口配置时保持用户配置不变，只补齐缺失的权限方案引用。
    /// </summary>
    private static void EnsureWebSettings(AppSettings settings)
    {
        if (settings.WebAccessProfiles == null)
        {
            settings.WebAccessProfiles = new List<WebAccessProfile>();
        }
        if (settings.WebAccessProfiles.Count == 0)
        {
            settings.WebAccessProfiles.Add(WebAccessProfile.CreateDefault());
        }
        if (settings.WebPorts == null)
        {
            settings.WebPorts = new List<WebPortSetting>();
        }
        if (settings.WebPorts.Count == 0)
        {
            WebPortSetting migrated = new WebPortSetting();
            migrated.Port = settings.WebServicePort > 0 ? settings.WebServicePort : 15919;
            migrated.Enabled = settings.EnableWebService;
            migrated.ProfileName = settings.WebAccessProfiles[0].Name;
            settings.WebPorts.Add(migrated);
        }
        // 端口引用了不存在的方案时，回落到第一个方案，避免该端口无权限可用
        foreach (WebPortSetting port in settings.WebPorts)
        {
            if (FindProfile(settings, port.ProfileName) == null)
            {
                port.ProfileName = settings.WebAccessProfiles[0].Name;
            }
        }
    }

    /// <summary>按名称查找权限方案；名称为空或不存在时返回 null。</summary>
    public static WebAccessProfile? FindProfile(AppSettings settings, string profileName)
    {
        if (string.IsNullOrEmpty(profileName) || settings.WebAccessProfiles == null)
        {
            return null;
        }
        foreach (WebAccessProfile profile in settings.WebAccessProfiles)
        {
            if (string.Equals(profile.Name, profileName, StringComparison.Ordinal))
            {
                return profile;
            }
        }
        return null;
    }
}

/// <summary>
/// 网页端权限方案：描述来源 IP 限制、可访问的目录与文件、修改与增删权限、是否需要账号密码。
/// 多个端口可引用同一方案，实现权限配置的共用；也允许每个端口各自引用不同方案。
/// </summary>
public class WebAccessProfile
{
    /// <summary>方案名称（端口按名称引用，重名时以靠前的为准）。</summary>
    public string Name { get; set; } = "默认方案";

    /// <summary>是否放行修改文件内容（写入已有文件）。默认关闭。</summary>
    public bool AllowModify { get; set; } = false;

    /// <summary>是否放行新建与删除（新建文件夹、重命名、删除条目）。默认关闭。</summary>
    public bool AllowCreateDelete { get; set; } = false;

    /// <summary>
    /// 允许访问的来源地址：支持单个 IPv4 / IPv6 地址与网段写法（如 192.168.1.0/24）。
    /// 列表为空表示不限制来源，但仅在监听地址为本机回环时才有实际意义。
    /// </summary>
    public List<string> AllowedAddresses { get; set; } = new List<string> { "127.0.0.1", "::1" };

    /// <summary>
    /// 允许访问的目录与文件路径：目录按前缀匹配，文件按完整路径匹配。
    /// 列表为空表示不限制路径（整个本机文件系统只读可浏览）。
    /// </summary>
    public List<string> AllowedPaths { get; set; } = new List<string>();

    /// <summary>是否要求账号密码验证；关闭时直接按本方案的权限放行。</summary>
    public bool RequirePassword { get; set; } = false;

    /// <summary>验证账号；仅在要求密码验证时生效。</summary>
    public string Account { get; set; } = "admin";

    /// <summary>密码摘要（不保存明文）；由 SetPassword 写入，VerifyPassword 校验。</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>密码摘要使用的随机盐值。</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>创建默认方案：仅本机回环访问，全盘只读浏览，不允许修改与增删，不要求密码。</summary>
    public static WebAccessProfile CreateDefault()
    {
        return new WebAccessProfile();
    }

    /// <summary>设置密码：生成随机盐并保存加盐摘要，明文不落盘。</summary>
    public void SetPassword(string password)
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
        this.PasswordSalt = Convert.ToBase64String(saltBytes);
        this.PasswordHash = WebAccessProfile.ComputeHash(password == null ? string.Empty : password, saltBytes);
    }

    /// <summary>校验密码：账号与密码都匹配才通过；未设置密码时一律不通过。</summary>
    public bool VerifyPassword(string account, string password)
    {
        if (string.IsNullOrEmpty(this.PasswordHash) || string.IsNullOrEmpty(this.PasswordSalt))
        {
            return false;
        }
        if (!string.Equals(this.Account, account, StringComparison.Ordinal))
        {
            return false;
        }
        byte[] saltBytes = Convert.FromBase64String(this.PasswordSalt);
        string computed = WebAccessProfile.ComputeHash(password == null ? string.Empty : password, saltBytes);
        return string.Equals(this.PasswordHash, computed, StringComparison.Ordinal);
    }

    /// <summary>加盐 SHA-256 摘要（十六进制文本）。</summary>
    private static string ComputeHash(string password, byte[] saltBytes)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] combined = new byte[saltBytes.Length + passwordBytes.Length];
        Array.Copy(saltBytes, 0, combined, 0, saltBytes.Length);
        Array.Copy(passwordBytes, 0, combined, saltBytes.Length, passwordBytes.Length);
        byte[] digest = SHA256.HashData(combined);
        return Convert.ToHexString(digest);
    }
}

/// <summary>
/// 网页端端口配置：一个端口一个监听器，独立引用一个权限方案。
/// 端口数量不受限制，可同时开启多个端口并分别应用不同权限。
/// </summary>
public class WebPortSetting
{
    /// <summary>监听端口号。</summary>
    public int Port { get; set; } = 15919;

    /// <summary>是否启用该端口。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>引用的权限方案名称（在 WebAccessProfiles 中按名称查找）。</summary>
    public string ProfileName { get; set; } = "默认方案";

    /// <summary>
    /// 监听范围：Loopback（仅本机，默认）或 Any（同时监听局域网地址，需配合方案的来源地址限制）。
    /// </summary>
    public string BindAddress { get; set; } = WebBindScope.Loopback;
}

/// <summary>网页端监听范围常量。</summary>
public static class WebBindScope
{
    /// <summary>仅监听本机回环地址。</summary>
    public const string Loopback = "Loopback";

    /// <summary>监听全部网络地址（含局域网），来源由权限方案的地址白名单控制。</summary>
    public const string Any = "Any";
}
