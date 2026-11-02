using System;
using System.Collections.Generic;
using System.IO;

namespace TabTaskExplorer.Kernel.Media;

/// <summary>
/// 媒体格式白名单与分类：全项目唯一的格式判定入口。
/// 桌面端、网页端、设置页与文件列表共用同一份清单，避免各写一套扩展名列表导致行为不一致。
/// </summary>
public static class MediaFormatCatalog
{
    /// <summary>图片扩展名（含前导点，小写）。</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } = new string[]
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".apng", ".bmp", ".dib",
        ".gif", ".webp", ".ico", ".tif", ".tiff",
    };

    /// <summary>视频扩展名（含前导点，小写）。</summary>
    public static IReadOnlyList<string> VideoExtensions { get; } = new string[]
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".asf", ".flv", ".f4v",
        ".webm", ".mpg", ".mpeg", ".mpe", ".ts", ".m2ts", ".mts", ".3gp", ".3g2",
        ".rm", ".rmvb", ".vob", ".ogv", ".divx", ".m2v",
    };

    /// <summary>音频扩展名（含前导点，小写）。</summary>
    public static IReadOnlyList<string> AudioExtensions { get; } = new string[]
    {
        ".mp3", ".flac", ".wav", ".wave", ".aac", ".m4a", ".m4b", ".ogg", ".oga",
        ".opus", ".wma", ".ape", ".alac", ".aiff", ".aif", ".aifc", ".wv", ".tak",
    };

    /// <summary>
    /// 已登记但当前后端暂不支持解码的图片格式：分类时仍算图片，但不开内置标签，
    /// 直接交系统默认程序（避免用户双击后只看到一句"不支持"）。
    /// </summary>
    public static IReadOnlyList<string> UnsupportedImageExtensions { get; } = new string[]
    {
        ".svg", ".heic", ".heif", ".avif", ".raw", ".cr2", ".nef", ".arw", ".dng", ".psd",
    };

    private static readonly HashSet<string> imageSet = BuildSet(ImageExtensions);
    private static readonly HashSet<string> videoSet = BuildSet(VideoExtensions);
    private static readonly HashSet<string> audioSet = BuildSet(AudioExtensions);
    private static readonly HashSet<string> unsupportedImageSet = BuildSet(UnsupportedImageExtensions);

    /// <summary>自然排序比较器（img2 排在 img10 之前，大小写不敏感）。</summary>
    public static IComparer<string> NaturalComparer { get; } = NaturalStringComparer.Instance;

    /// <summary>取小写扩展名（含前导点）；无扩展名返回空串。</summary>
    public static string GetExtension(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }
        string extension = Path.GetExtension(path);
        return extension == null ? string.Empty : extension.ToLowerInvariant();
    }

    /// <summary>按扩展名判定媒体大类；非媒体（或已登记但不支持的图片）返回对应结果。</summary>
    public static MediaKind Classify(string? path)
    {
        string extension = GetExtension(path);
        if (extension.Length == 0)
        {
            return MediaKind.Unknown;
        }
        if (imageSet.Contains(extension) || unsupportedImageSet.Contains(extension))
        {
            return MediaKind.Image;
        }
        if (videoSet.Contains(extension))
        {
            return MediaKind.Video;
        }
        if (audioSet.Contains(extension))
        {
            return MediaKind.Audio;
        }
        return MediaKind.Unknown;
    }

    /// <summary>是否可交给内置查看器：扩展名已登记，且后端确实能解码。</summary>
    public static bool IsPlayable(string? path)
    {
        string extension = GetExtension(path);
        if (extension.Length == 0 || unsupportedImageSet.Contains(extension))
        {
            return false;
        }
        return imageSet.Contains(extension) || videoSet.Contains(extension) || audioSet.Contains(extension);
    }

    /// <summary>是否为动图格式（GIF / 动态 WebP / APNG），图片视图据此启用逐帧播放。</summary>
    public static bool IsAnimatedImage(string? path)
    {
        string extension = GetExtension(path);
        return extension == ".gif" || extension == ".webp" || extension == ".apng";
    }

    /// <summary>取界面展示用的类型名，例如 "JPEG 图片" / "MP4 视频" / "MP3 音频"。</summary>
    public static string DescribeType(string? path)
    {
        MediaKind kind = Classify(path);
        string extension = GetExtension(path);
        string tag = extension.Length > 1 ? extension.Substring(1).ToUpperInvariant() : string.Empty;
        string kindText;
        if (kind == MediaKind.Image)
        {
            kindText = "图片";
        }
        else if (kind == MediaKind.Video)
        {
            kindText = "视频";
        }
        else if (kind == MediaKind.Audio)
        {
            kindText = "音频";
        }
        else
        {
            return "文件";
        }
        return tag.Length == 0 ? kindText : tag + " " + kindText;
    }

    /// <summary>取媒体大类的界面名（图片 / 视频 / 音频）。</summary>
    public static string DescribeKind(MediaKind kind)
    {
        if (kind == MediaKind.Image)
        {
            return "图片";
        }
        if (kind == MediaKind.Video)
        {
            return "视频";
        }
        if (kind == MediaKind.Audio)
        {
            return "音频";
        }
        return "文件";
    }

    /// <summary>生成文件对话框用的过滤器，例如 "媒体文件|*.jpg;*.png;..."。</summary>
    public static string BuildDialogFilter()
    {
        return "媒体文件|" + string.Join(";", BuildAllPatterns())
            + "|图片|" + string.Join(";", BuildPatterns(ImageExtensions))
            + "|视频|" + string.Join(";", BuildPatterns(VideoExtensions))
            + "|音频|" + string.Join(";", BuildPatterns(AudioExtensions))
            + "|全部文件|*.*";
    }

    /// <summary>把扩展名清单转成通配符清单（*.jpg）。</summary>
    private static IEnumerable<string> BuildPatterns(IReadOnlyList<string> extensions)
    {
        for (int index = 0; index < extensions.Count; index++)
        {
            yield return "*" + extensions[index];
        }
    }

    /// <summary>全部可播放格式的通配符清单。</summary>
    private static IEnumerable<string> BuildAllPatterns()
    {
        foreach (string pattern in BuildPatterns(ImageExtensions))
        {
            yield return pattern;
        }
        foreach (string pattern in BuildPatterns(VideoExtensions))
        {
            yield return pattern;
        }
        foreach (string pattern in BuildPatterns(AudioExtensions))
        {
            yield return pattern;
        }
    }

    /// <summary>把扩展名清单构建为小写集合，保证判定 O(1)。</summary>
    private static HashSet<string> BuildSet(IReadOnlyList<string> extensions)
    {
        HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < extensions.Count; index++)
        {
            set.Add(extensions[index]);
        }
        return set;
    }
}

/// <summary>
/// 自然排序比较器：把连续数字段按数值比较，使 "第2集" 排在 "第10集" 之前。
/// 数字段优先于字母段，其余字符按小写序比较，最后回落到原始序以保证结果稳定。
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    /// <summary>共享实例（无状态，可安全复用）。</summary>
    public static NaturalStringComparer Instance { get; } = new NaturalStringComparer();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x == null)
        {
            return -1;
        }
        if (y == null)
        {
            return 1;
        }
        int left = 0;
        int right = 0;
        while (left < x.Length && right < y.Length)
        {
            char leftChar = x[left];
            char rightChar = y[right];
            bool leftDigit = char.IsDigit(leftChar);
            bool rightDigit = char.IsDigit(rightChar);
            if (leftDigit && rightDigit)
            {
                int leftStart = left;
                int rightStart = right;
                while (left < x.Length && char.IsDigit(x[left]))
                {
                    left++;
                }
                while (right < y.Length && char.IsDigit(y[right]))
                {
                    right++;
                }
                string leftNumber = x.Substring(leftStart, left - leftStart).TrimStart('0');
                string rightNumber = y.Substring(rightStart, right - rightStart).TrimStart('0');
                if (leftNumber.Length != rightNumber.Length)
                {
                    return leftNumber.Length < rightNumber.Length ? -1 : 1;
                }
                int digitCompare = string.CompareOrdinal(leftNumber, rightNumber);
                if (digitCompare != 0)
                {
                    return digitCompare < 0 ? -1 : 1;
                }
                continue;
            }
            if (leftDigit != rightDigit)
            {
                // 数字段排在字母段之前，与资源管理器的直觉一致
                return leftDigit ? -1 : 1;
            }
            char leftLower = char.ToLowerInvariant(leftChar);
            char rightLower = char.ToLowerInvariant(rightChar);
            if (leftLower != rightLower)
            {
                return leftLower < rightLower ? -1 : 1;
            }
            if (leftChar != rightChar)
            {
                return leftChar < rightChar ? -1 : 1;
            }
            left++;
            right++;
        }
        int leftRest = x.Length - left;
        int rightRest = y.Length - right;
        if (leftRest != rightRest)
        {
            return leftRest < rightRest ? -1 : 1;
        }
        return 0;
    }
}
