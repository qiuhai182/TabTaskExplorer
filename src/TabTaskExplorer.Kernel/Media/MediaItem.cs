using System;
using System.IO;

namespace TabTaskExplorer.Kernel.Media;

/// <summary>
/// 播放列表条目：一个可显示 / 可播放的媒体文件的只读描述。
/// 内核只承载数据与格式判定，界面状态（是否当前项、是否已看）由应用层视图模型维护。
/// </summary>
public class MediaItem
{
    /// <summary>完整路径（本机绝对路径）。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>不含目录的文件名（含扩展名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>媒体大类。</summary>
    public MediaKind Kind { get; set; } = MediaKind.Unknown;

    /// <summary>文件字节数；读取失败时为 0。</summary>
    public long SizeBytes { get; set; }

    /// <summary>最后修改时间；读取失败时为 <see cref="DateTime.MinValue"/>。</summary>
    public DateTime ModifiedAt { get; set; } = DateTime.MinValue;

    /// <summary>界面展示用的类型名（"JPEG 图片"）。</summary>
    public string TypeText
    {
        get { return MediaFormatCatalog.DescribeType(this.FullPath); }
    }

    /// <summary>界面展示用的文件大小（"2.4 MB"）。</summary>
    public string SizeText
    {
        get { return FormatSize(this.SizeBytes); }
    }

    /// <summary>按路径构造条目：文件名与类型自动推导，大小与时间由调用方按需填充。</summary>
    public static MediaItem FromPath(string fullPath, MediaKind kind)
    {
        MediaItem item = new MediaItem();
        item.FullPath = fullPath;
        item.Name = Path.GetFileName(fullPath);
        item.Kind = kind;
        return item;
    }

    /// <summary>读取文件的大小与修改时间（失败时静默降级为零值，不抛异常打断扫描）。</summary>
    public void FillFileInfo()
    {
        try
        {
            FileInfo info = new FileInfo(this.FullPath);
            if (info.Exists)
            {
                this.SizeBytes = info.Length;
                this.ModifiedAt = info.LastWriteTime;
            }
        }
        catch (Exception)
        {
            // 权限不足 / 文件被占用 / 路径失效：保持零值，播放列表照常展示
        }
    }

    /// <summary>把字节数格式化为便于阅读的大小文本。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "-";
        }
        string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }
        if (unitIndex == 0)
        {
            return bytes.ToString() + " B";
        }
        return value.ToString(value >= 100 ? "F0" : "F1") + " " + units[unitIndex];
    }
}
