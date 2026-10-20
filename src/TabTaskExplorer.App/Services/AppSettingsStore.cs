using System.Text.Json;
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
}
