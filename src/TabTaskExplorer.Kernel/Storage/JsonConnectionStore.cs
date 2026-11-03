using System.Text.Json;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Storage;

/// <summary>
/// 基于 JSON 文件的连接配置存储默认实现，跨平台可用。
/// </summary>
public class JsonConnectionStore : IConnectionStore
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private readonly string filePath;

    /// <summary>使用默认数据目录创建存储：当前用户应用数据目录下的 TabTaskExplorer 文件夹。</summary>
    public JsonConnectionStore()
    {
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabTaskExplorer");
        try
        {
            Directory.CreateDirectory(dataDirectory);
        }
        catch
        {
            // 数据目录不可用时允许启动，写入操作会在保存时报错
        }
        this.filePath = Path.Combine(dataDirectory, "connections.json");
    }

    /// <summary>使用指定文件路径创建存储，供测试与便携模式使用。</summary>
    /// <param name="filePath">连接配置 JSON 文件的完整路径。</param>
    public JsonConnectionStore(string filePath)
    {
        this.filePath = filePath;
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string StorePath
    {
        get { return this.filePath; }
    }

    public IReadOnlyList<RemoteConnectionProfile> LoadAll()
    {
        if (!File.Exists(this.filePath))
        {
            return new List<RemoteConnectionProfile>();
        }

        string json = File.ReadAllText(this.filePath);
        List<RemoteConnectionProfile>? profiles = JsonSerializer.Deserialize<List<RemoteConnectionProfile>>(json, jsonOptions);
        if (profiles == null)
        {
            return new List<RemoteConnectionProfile>();
        }
        return profiles;
    }

    public void SaveAll(IReadOnlyList<RemoteConnectionProfile> profiles)
    {
        string json = JsonSerializer.Serialize(profiles, jsonOptions);
        File.WriteAllText(this.filePath, json);
    }
}
