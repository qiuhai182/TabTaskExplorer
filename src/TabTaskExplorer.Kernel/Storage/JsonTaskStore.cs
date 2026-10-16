using System.Text.Json;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Storage;

/// <summary>
/// 基于 JSON 文件的任务存储默认实现，跨平台可用。
/// 数据量增大或需要多端同步时，可替换为 SQLite 等实现。
/// </summary>
public class JsonTaskStore : ITaskStore
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private readonly string filePath;

    /// <summary>使用默认数据目录创建存储：当前用户应用数据目录下的 TabTaskExplorer 文件夹。</summary>
    public JsonTaskStore()
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
        this.filePath = Path.Combine(dataDirectory, "tasks.json");
    }

    /// <summary>使用指定文件路径创建存储，供测试与便携模式使用。</summary>
    /// <param name="filePath">任务 JSON 文件的完整路径。</param>
    public JsonTaskStore(string filePath)
    {
        this.filePath = filePath;
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public IReadOnlyList<TaskItem> LoadAll()
    {
        if (!File.Exists(this.filePath))
        {
            return new List<TaskItem>();
        }

        string json = File.ReadAllText(this.filePath);
        List<TaskItem>? tasks = JsonSerializer.Deserialize<List<TaskItem>>(json, jsonOptions);
        if (tasks == null)
        {
            return new List<TaskItem>();
        }
        return tasks;
    }

    public void SaveAll(IReadOnlyList<TaskItem> tasks)
    {
        string json = JsonSerializer.Serialize(tasks, jsonOptions);
        File.WriteAllText(this.filePath, json);
    }
}
