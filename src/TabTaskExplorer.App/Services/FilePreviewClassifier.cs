using System;
using System.IO;
using System.Text;

namespace TabTaskExplorer.App.Services;

/// <summary>快速预览的文件类型：桌面快速预览窗与网页端预览层共用同一套判定。</summary>
public enum FilePreviewKind
{
    Other = 0,
    Image = 1,
    Text = 2,
    Pdf = 3,
    Audio = 4,
    Video = 5,
}

/// <summary>
/// 文件预览分类与文本读取：桌面 QuickLookWindow 与网页端 /api/preview 的唯一实现，
/// 避免两端维护不同的扩展名清单与文本截断规则。
/// </summary>
public static class FilePreviewClassifier
{
    private const int TextPreviewLimit = 512 * 1024;

    /// <summary>按扩展名判定预览类型。</summary>
    public static FilePreviewKind Detect(string path)
    {
        string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        if (IsImageExtension(extension))
        {
            return FilePreviewKind.Image;
        }
        if (extension == ".pdf")
        {
            return FilePreviewKind.Pdf;
        }
        if (extension == ".mp3" || extension == ".wav" || extension == ".flac" || extension == ".m4a"
            || extension == ".aac" || extension == ".ogg" || extension == ".wma")
        {
            return FilePreviewKind.Audio;
        }
        if (extension == ".mp4" || extension == ".mkv" || extension == ".avi" || extension == ".mov"
            || extension == ".wmv" || extension == ".flv" || extension == ".webm" || extension == ".m4v"
            || extension == ".ts" || extension == ".mpg" || extension == ".mpeg")
        {
            return FilePreviewKind.Video;
        }
        if (extension == ".txt" || extension == ".log" || extension == ".md" || extension == ".json"
            || extension == ".xml" || extension == ".cs" || extension == ".js" || extension == ".css"
            || extension == ".html" || extension == ".ini" || extension == ".config" || extension == ".csv")
        {
            return FilePreviewKind.Text;
        }
        return FilePreviewKind.Other;
    }

    /// <summary>是否为图片扩展名。</summary>
    public static bool IsImage(string path)
    {
        return IsImageExtension(Path.GetExtension(path ?? string.Empty).ToLowerInvariant());
    }

    /// <summary>读取文本预览：最多 512 KB；UTF-8 出现替换字符时回退 GBK；超长时追加截断提示。</summary>
    public static string ReadTextPreview(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            int length = (int)Math.Min(TextPreviewLimit, stream.Length);
            byte[] buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                int one = stream.Read(buffer, read, length - read);
                if (one <= 0)
                {
                    break;
                }
                read += one;
            }
            string text = Encoding.UTF8.GetString(buffer, 0, read);
            if (text.IndexOf('\uFFFD') >= 0)
            {
                try
                {
                    Encoding gb = Encoding.GetEncoding(936);
                    text = gb.GetString(buffer, 0, read);
                }
                catch (Exception)
                {
                    // 代码页不可用时保留 UTF-8 结果
                }
            }
            if (StreamLength(path) > TextPreviewLimit)
            {
                text += "\n\n…… 文件较大，仅显示前 512 KB";
            }
            return text;
        }
        catch (Exception error)
        {
            return "读取失败：" + error.Message;
        }
    }

    /// <summary>不可直接预览时的提示文案；可预览类型返回空字符串。</summary>
    public static string DescribeUnsupported(FilePreviewKind kind)
    {
        if (kind == FilePreviewKind.Pdf)
        {
            return "PDF 预览暂不可用：当前版本没有跨平台 PDF 渲染器。\n可双击文件使用系统默认程序打开。";
        }
        if (kind == FilePreviewKind.Other)
        {
            return "该类型暂不支持快速预览。";
        }
        return string.Empty;
    }

    private static bool IsImageExtension(string lower)
    {
        return lower == ".png" || lower == ".jpg" || lower == ".jpeg" || lower == ".bmp"
            || lower == ".gif" || lower == ".webp" || lower == ".tif" || lower == ".tiff";
    }

    private static long StreamLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
