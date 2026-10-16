using System.Text.Json;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 应用设置：对应 TabX Explorer 设置中心的功能子集。
/// </summary>
public class AppSettings
{
    /// <summary>是否显示隐藏的文件与文件夹。</summary>
    public bool ShowHiddenFiles { get; set; } = false;

    /// <summary>是否显示文件扩展名。</summary>
    public bool ShowFileExtensions { get; set; } = true;

    /// <summary>双击标签是否关闭当前标签。</summary>
    public bool DoubleClickTabClose { get; set; } = true;

    /// <summary>是否显示文件浏览面板的导航工具栏。</summary>
    public bool ShowToolbar { get; set; } = true;

    /// <summary>是否显示顶部书签栏。</summary>
    public bool ShowBookmarkBar { get; set; } = true;

    /// <summary>是否开机自动启动（Windows 注册表实现）。</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>启动时是否恢复上次关闭的标签页。</summary>
    public bool RestoreLastTabs { get; set; } = false;

    /// <summary>上次关闭时打开的标签页路径列表。</summary>
    public List<string> OpenTabPaths { get; set; } = new List<string>();

    /// <summary>固定到快捷访问的路径列表。</summary>
    public List<string> PinnedPaths { get; set; } = new List<string>();
}

/// <summary>
/// 应用设置的 JSON 文件存取。
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

    /// <summary>读取设置；文件不存在或损坏时返回默认设置。</summary>
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
                    return settings;
                }
            }
        }
        catch
        {
            // 设置文件损坏时退回默认设置
        }
        return new AppSettings();
    }

    /// <summary>保存设置。</summary>
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
            // 保存失败不影响程序运行，下次保存再试
        }
    }
}
