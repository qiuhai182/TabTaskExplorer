using System;
using System.IO;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>
/// 截图与录屏产物的路径规则：默认保存到「我的图片\TabTaskExplorer\截图录屏」，
/// 文件按「截图_日期_时间.png / 录屏_日期_时间.mp4」命名，重名时自动追加序号。
/// </summary>
public static class CapturePaths
{
    /// <summary>截图文件名前缀。</summary>
    public const string ScreenshotPrefix = "截图_";

    /// <summary>录屏文件名前缀。</summary>
    public const string RecordingPrefix = "录屏_";

    /// <summary>截图与录屏共用的产物扩展名清单（历史列表只展示这些类型）。</summary>
    public static readonly string[] CaptureExtensions = new string[]
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
        ".mp4", ".mkv", ".mov", ".avi", ".webm",
    };

    /// <summary>解析产物保存目录：用户配置优先，留空或配置非法时使用默认图片目录。</summary>
    public static string ResolveOutputDirectory(AppSettings settings)
    {
        if (settings != null && !string.IsNullOrWhiteSpace(settings.CaptureSaveDirectory))
        {
            try
            {
                return Path.GetFullPath(settings.CaptureSaveDirectory.Trim());
            }
            catch
            {
                // 配置中有非法字符时回落到默认图片目录，保证截图仍然可以保存
            }
        }
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(pictures))
        {
            pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        }
        return Path.Combine(pictures, "TabTaskExplorer", "截图录屏");
    }

    /// <summary>确保产物目录存在；创建失败返回 false 并带回中文原因。</summary>
    public static bool EnsureOutputDirectory(string directory, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            errorMessage = "产物保存目录为空";
            return false;
        }
        try
        {
            Directory.CreateDirectory(directory);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception error)
        {
            errorMessage = "创建产物保存目录失败：" + error.Message;
            return false;
        }
    }

    /// <summary>创建截图文件的唯一路径。</summary>
    public static string CreateScreenshotPath(string directory, DateTime timestamp)
    {
        return CreateUniquePath(directory, ScreenshotPrefix, ".png", timestamp);
    }

    /// <summary>创建录屏文件的唯一路径。</summary>
    public static string CreateRecordingPath(string directory, DateTime timestamp)
    {
        return CreateUniquePath(directory, RecordingPrefix, ".mp4", timestamp);
    }

    /// <summary>按前缀、时间与扩展名生成唯一路径；已存在时追加序号直到不重复。</summary>
    public static string CreateUniquePath(string directory, string prefix, string extension, DateTime timestamp)
    {
        string baseName = prefix + timestamp.ToString("yyyyMMdd_HHmmss");
        string candidate = Path.Combine(directory, baseName + extension);
        int sequence = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, baseName + "_" + sequence + extension);
            sequence++;
        }
        return candidate;
    }

    /// <summary>判断文件是否为截图或录屏产物（按扩展名）。</summary>
    public static bool IsCaptureFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }
        string extension = Path.GetExtension(fileName);
        foreach (string allowed in CaptureExtensions)
        {
            if (string.Equals(extension, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>把字节数格式化为便于阅读的大小文字。</summary>
    public static string DescribeSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " 字节";
        }
        double kilobytes = bytes / 1024.0;
        if (kilobytes < 1024)
        {
            return Math.Round(kilobytes, 1) + " KB";
        }
        double megabytes = kilobytes / 1024.0;
        if (megabytes < 1024)
        {
            return Math.Round(megabytes, 1) + " MB";
        }
        return Math.Round(megabytes / 1024.0, 2) + " GB";
    }
}
