using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TabTaskExplorer.Kernel.Media;
using TabTaskExplorer.Kernel.Models;
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

    /// <summary>
    /// 用户自定义启动项：本程序每次开机后首次启动时检查，未在本次开机拉起过的条目自动拉起。
    /// 支持程序、快捷方式、脚本与网页链接，并可设置本程序启动后的延迟秒数。
    /// </summary>
    public List<CustomStartupItem> CustomStartupItems { get; set; } = new List<CustomStartupItem>();

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

    /// <summary>旧版应用内快捷键配置：仅用于从旧配置文件迁移到 <see cref="Shortcuts"/>。</summary>
    public List<HotkeySetting> Hotkeys { get; set; } = HotkeySetting.CreateDefaults();

    /// <summary>旧版全局快捷键配置：仅用于从旧配置文件迁移到 <see cref="Shortcuts"/>。</summary>
    public List<HotkeySetting> GlobalHotkeys { get; set; } = HotkeySetting.CreateGlobalDefaults();

    /// <summary>
    /// 统一快捷键配置：同一动作可有多条记录，每条记录可独立选择应用内或系统全局作用域。
    /// 旧版 Hotkeys / GlobalHotkeys 会在首次加载时迁移到这里。
    /// </summary>
    public List<HotkeySetting> Shortcuts { get; set; } = new List<HotkeySetting>();

    /// <summary>是否已完成旧快捷键配置迁移；用于区分「尚未迁移」与「用户主动删除了全部快捷键」。</summary>
    public bool ShortcutsMigrated { get; set; } = false;

    /// <summary>截图与录屏产物保存目录；留空表示使用「我的图片\TabTaskExplorer\截图录屏」。</summary>
    public string CaptureSaveDirectory { get; set; } = string.Empty;

    /// <summary>截图完成后是否把图片文件写入系统剪贴板（可在资源管理器直接粘贴）。</summary>
    public bool CaptureCopyToClipboard { get; set; } = true;

    /// <summary>截图或录屏完成后是否自动打开产物文件夹标签页。</summary>
    public bool CaptureOpenFolderAfter { get; set; } = true;

    /// <summary>截图保存后是否自动打开标注编辑器（吸收 Snipaste 截图编辑流程，默认开启）。</summary>
    public bool CaptureOpenEditorAfter { get; set; } = true;

    /// <summary>贴图窗口是否始终置顶（默认开启，关闭后贴图可被其他窗口覆盖）。</summary>
    public bool CapturePinTopmost { get; set; } = true;

    /// <summary>
    /// 「贴图截图」是否同时按产物规则保存文件（默认开启：先保存进产物历史，再用同一份文件生成贴图）；
    /// 关闭后仅在内存中生成贴图，不落盘、不进入产物历史。
    /// </summary>
    public bool CapturePinShotSaveFile { get; set; } = true;

    /// <summary>是否已为既有配置补入「贴图截图」的默认快捷键；只补一次，用户删除后不再重复添加。</summary>
    public bool PinShotShortcutSeeded { get; set; } = false;

    /// <summary>文字识别语言；留空表示 Windows 使用系统已安装语言，Tesseract 使用 eng+chi_sim 默认值。</summary>
    public string OcrLanguage { get; set; } = string.Empty;

    /// <summary>Tesseract 可执行文件完整路径；留空表示自动查找（与 FFmpeg 相同的查找策略）。</summary>
    public string OcrTesseractPath { get; set; } = string.Empty;

    /// <summary>截图与录屏是否包含鼠标指针。</summary>
    public bool CaptureIncludeCursor { get; set; } = true;

    /// <summary>截图前的延迟秒数：0 表示立即截图。</summary>
    public int CaptureDelaySeconds { get; set; } = 0;

    /// <summary>录屏帧率（每秒采集画面数量）。</summary>
    public int CaptureRecordFps { get; set; } = 30;

    /// <summary>录屏画质（H.264 恒定质量参数，数值越小画质越高）。</summary>
    public int CaptureRecordQuality { get; set; } = 23;

    /// <summary>录屏默认目标：FullScreen / Region / ActiveWindow。</summary>
    public string CaptureRecordTarget { get; set; } = "FullScreen";

    /// <summary>录屏麦克风设备名；留空表示不录制音频。</summary>
    public string CaptureMicrophoneDevice { get; set; } = string.Empty;

    /// <summary>FFmpeg 可执行文件完整路径；留空表示自动查找。</summary>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>上次区域截图的左上角横坐标（物理像素，用于「上次区域」快速截图）。</summary>
    public int CaptureLastRegionX { get; set; } = 0;

    /// <summary>上次区域截图的左上角纵坐标（物理像素）。</summary>
    public int CaptureLastRegionY { get; set; } = 0;

    /// <summary>上次区域截图的宽度（物理像素）。</summary>
    public int CaptureLastRegionWidth { get; set; } = 0;

    /// <summary>上次区域截图的高度（物理像素）。</summary>
    public int CaptureLastRegionHeight { get; set; } = 0;

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
                    EnsureShortcuts(settings);
                    EnsureFileOpenRules(settings);
                    EnsureWebSettings(settings);
                    EnsureCustomStartupItems(settings);
                    return settings;
                }
            }
        }
        catch
        {
            // 文件损坏时回落默认设置，不影响程序启动
        }
        AppSettings fallback = new AppSettings();
        EnsureShortcuts(fallback);
        return fallback;
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
    /// 迁移并补齐统一快捷键清单：
    /// 首次加载时把旧版 Hotkeys（应用内）与 GlobalHotkeys（全局）迁移到 Shortcuts；
    /// 用户主动删除全部快捷键后保留空清单，不再强行追加默认项。
    /// </summary>
    private static void EnsureShortcuts(AppSettings settings)
    {
        if (!settings.ShortcutsMigrated)
        {
            List<HotkeySetting> migrated = new List<HotkeySetting>();
            if (settings.Hotkeys != null)
            {
                foreach (HotkeySetting legacy in settings.Hotkeys)
                {
                    if (legacy != null)
                    {
                        migrated.Add(CopyShortcut(legacy, "Local"));
                    }
                }
            }
            if (settings.GlobalHotkeys != null)
            {
                foreach (HotkeySetting legacy in settings.GlobalHotkeys)
                {
                    if (legacy != null)
                    {
                        migrated.Add(CopyShortcut(legacy, "Global"));
                    }
                }
            }
            settings.Shortcuts = migrated.Count > 0 ? migrated : HotkeySetting.CreateDefaultShortcuts();
            settings.ShortcutsMigrated = true;
        }
        if (settings.Shortcuts == null)
        {
            settings.Shortcuts = new List<HotkeySetting>();
        }
        List<HotkeySetting> validShortcuts = new List<HotkeySetting>();
        foreach (HotkeySetting shortcut in settings.Shortcuts)
        {
            if (shortcut != null)
            {
                validShortcuts.Add(shortcut);
            }
        }
        if (validShortcuts.Count != settings.Shortcuts.Count)
        {
            settings.Shortcuts = validShortcuts;
        }
        // 既有配置补入「贴图截图」默认快捷键：只补一次，用户后续删除不会反复出现
        if (!settings.PinShotShortcutSeeded)
        {
            bool exists = false;
            foreach (HotkeySetting shortcut in settings.Shortcuts)
            {
                if (string.Equals(shortcut.ActionName, "ScreenshotPin", StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                settings.Shortcuts.Add(HotkeySetting.CreatePinShotDefault());
            }
            settings.PinShotShortcutSeeded = true;
        }
        // 迁移完成后清空旧字段，避免后续保存继续写入两份重复配置
        settings.Hotkeys = new List<HotkeySetting>();
        settings.GlobalHotkeys = new List<HotkeySetting>();
    }

    /// <summary>复制一条快捷键配置并指定作用域（迁移旧配置时使用）。</summary>
    private static HotkeySetting CopyShortcut(HotkeySetting source, string scope)
    {
        HotkeySetting copy = new HotkeySetting();
        copy.ActionName = source.ActionName;
        copy.KeyText = source.KeyText;
        copy.Ctrl = source.Ctrl;
        copy.Alt = source.Alt;
        copy.Shift = source.Shift;
        copy.Win = source.Win;
        copy.Scope = scope;
        return copy;
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

    /// <summary>补齐自定义启动项：旧配置没有该清单时补空列表，缺少编号的条目补发编号。</summary>
    private static void EnsureCustomStartupItems(AppSettings settings)
    {
        if (settings.CustomStartupItems == null)
        {
            settings.CustomStartupItems = new List<CustomStartupItem>();
            return;
        }
        foreach (CustomStartupItem item in settings.CustomStartupItems)
        {
            if (string.IsNullOrEmpty(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }
            if (string.IsNullOrEmpty(item.ItemType))
            {
                item.ItemType = StartupItemType.Program;
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
