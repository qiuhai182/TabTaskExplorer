using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SkiaSharp;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 文件缩略图服务：按「文件路径 + 大小 + 修改时间 + 尺寸档位」生成并缓存 PNG，
/// 图片使用 Skia 解码缩放，视频用 FFmpeg 抽取首帧，字体渲染示例文字；PDF 暂无跨平台渲染器，保留类型图标。
/// 全部生成在后台线程执行并限制并发，缓存落在用户本地数据目录，避免滚动时重复解码。
/// </summary>
public static class ThumbnailService
{
    /// <summary>缩略图尺寸档位（像素）：96 / 192 / 384。</summary>
    public const int TierSmall = 96;
    public const int TierMedium = 192;
    public const int TierLarge = 384;

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TabTaskExplorer",
        "thumbnails");

    private static readonly ConcurrentDictionary<string, string?> MemoryCache = new ConcurrentDictionary<string, string?>();

    private static readonly ConcurrentDictionary<string, DateTime> FailureCache = new ConcurrentDictionary<string, DateTime>();

    private static readonly SemaphoreSlim GenerateGate = new SemaphoreSlim(3, 3);

    private static string? ffmpegPath;

    private static bool initialized;

    /// <summary>由主窗口启动时注入设置中的 FFmpeg 路径；未注入时按内核定位器自动查找。</summary>
    public static void Configure(string? configuredFfmpegPath)
    {
        ffmpegPath = FfmpegLocator.Find(configuredFfmpegPath);
        EnsureInitialized();
    }

    /// <summary>把显示尺寸换算成最近的缓存档位。</summary>
    public static int ResolveTier(double displaySize)
    {
        if (displaySize <= TierSmall)
        {
            return TierSmall;
        }
        if (displaySize <= TierMedium)
        {
            return TierMedium;
        }
        return TierLarge;
    }

    /// <summary>获取缩略图缓存文件路径；不支持或生成失败返回 null，调用方保留类型图标。</summary>
    public static async Task<string?> GetThumbnailPathAsync(string fullPath, double displaySize, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            return null;
        }
        int tier = ResolveTier(displaySize);
        string extension = Path.GetExtension(fullPath);
        if (!IsSupported(extension))
        {
            return null;
        }
        FileInfo info;
        try
        {
            info = new FileInfo(fullPath);
        }
        catch (Exception)
        {
            return null;
        }
        string key = BuildKey(fullPath, info.Length, info.LastWriteTimeUtc.Ticks, tier);
        string cachePath = Path.Combine(CacheDirectory, key + ".png");

        string? memory;
        if (MemoryCache.TryGetValue(key, out memory))
        {
            return memory;
        }
        DateTime failedTime;
        if (FailureCache.TryGetValue(key, out failedTime) && DateTime.UtcNow - failedTime < TimeSpan.FromMinutes(5))
        {
            return null;
        }
        if (File.Exists(cachePath))
        {
            MemoryCache[key] = cachePath;
            return cachePath;
        }

        bool entered = false;
        try
        {
            await GenerateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            if (File.Exists(cachePath))
            {
                MemoryCache[key] = cachePath;
                return cachePath;
            }
            string? generated = await Task.Run(delegate
            {
                return Generate(fullPath, extension, tier, cachePath);
            }, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            MemoryCache[key] = generated;
            if (generated == null)
            {
                FailureCache[key] = DateTime.UtcNow;
            }
            return generated;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error)
        {
            FailureCache[key] = DateTime.UtcNow;
            UiLogger.Log("缩略图", "生成失败：文件=" + fullPath + " 档位=" + tier + " 原因=" + error.Message);
            return null;
        }
        finally
        {
            if (entered)
            {
                GenerateGate.Release();
            }
        }
    }

    /// <summary>同步读取缓存文件是否存在，供已生成过的缩略图快速命中。</summary>
    public static bool TryGetCachedPath(string fullPath, double displaySize, out string path)
    {
        path = string.Empty;
        if (!File.Exists(fullPath))
        {
            return false;
        }
        try
        {
            FileInfo info = new FileInfo(fullPath);
            int tier = ResolveTier(displaySize);
            string key = BuildKey(fullPath, info.Length, info.LastWriteTimeUtc.Ticks, tier);
            string candidate = Path.Combine(CacheDirectory, key + ".png");
            if (!File.Exists(candidate))
            {
                return false;
            }
            MemoryCache[key] = candidate;
            path = candidate;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? Generate(string fullPath, string extension, int tier, string cachePath)
    {
        string tempPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
        bool produced = false;
        try
        {
            string lower = extension.ToLowerInvariant();
            if (IsImageExtension(lower))
            {
                produced = GenerateImage(fullPath, tier, tempPath);
            }
            else if (IsVideoExtension(lower))
            {
                produced = GenerateVideoFrame(fullPath, tier, tempPath);
            }
            else if (IsFontExtension(lower))
            {
                produced = GenerateFontPreview(fullPath, tier, tempPath);
            }
            if (!produced || !File.Exists(tempPath))
            {
                return null;
            }
            File.Move(tempPath, cachePath, true);
            return cachePath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception)
                {
                    // 临时文件清理失败不影响缓存结果
                }
            }
        }
    }

    /// <summary>图片缩略图：按最长边降到目标档位后保存 PNG。</summary>
    private static bool GenerateImage(string fullPath, int tier, string outputPath)
    {
        using FileStream input = File.OpenRead(fullPath);
        using Bitmap probe = new Bitmap(input);
        int width = probe.PixelSize.Width;
        int height = probe.PixelSize.Height;
        if (width <= 0 || height <= 0)
        {
            return false;
        }
        int maxSide = Math.Max(width, height);
        if (maxSide <= tier)
        {
            using FileStream direct = File.Create(outputPath);
            probe.Save(direct);
            return true;
        }
        int targetWidth = Math.Max(1, (int)Math.Round(width * ((double)tier / maxSide)));
        input.Position = 0;
        using Bitmap scaled = Bitmap.DecodeToWidth(input, targetWidth, BitmapInterpolationMode.HighQuality);
        using FileStream output = File.Create(outputPath);
        scaled.Save(output);
        return true;
    }

    /// <summary>视频首帧：调用 FFmpeg 输出指定宽度的 PNG；未找到 FFmpeg 时返回失败。</summary>
    private static bool GenerateVideoFrame(string fullPath, int tier, string outputPath)
    {
        string? executable = ffmpegPath;
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
        {
            executable = FfmpegLocator.Find(null);
            ffmpegPath = executable;
        }
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
        {
            return false;
        }
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = executable;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            // ArgumentList 逐个参数传递，避免路径含空格或过滤表达式被命令拆行
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(fullPath);
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add("scale=" + tier + ":-2");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(outputPath);
            using Process process = Process.Start(startInfo)!;
            bool exited = process.WaitForExit(10000);
            if (!exited)
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception)
                {
                    // 结束超时进程失败时按失败返回
                }
                return false;
            }
            return process.ExitCode == 0 && File.Exists(outputPath);
        }
        catch (Exception error)
        {
            UiLogger.Log("缩略图", "视频首帧失败：文件=" + fullPath + " 原因=" + error.Message);
            return false;
        }
    }

    /// <summary>字体预览：用 Skia 以字体本身渲染示例文字（含字体名），生成 PNG。</summary>
    private static bool GenerateFontPreview(string fullPath, int tier, string outputPath)
    {
        try
        {
            using SKTypeface? typeface = SKTypeface.FromFile(fullPath);
            if (typeface == null)
            {
                return false;
            }
            int size = tier;
            using SKBitmap bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using SKCanvas canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(0, 0, 0, 0));
            string sample = "Aa";
            float textSize = Math.Max(12f, size * 0.42f);
            using SKFont font = new SKFont(typeface, textSize);
            font.Edging = SKFontEdging.Antialias;
            using SKPaint paint = new SKPaint();
            paint.Color = new SKColor(0x2B, 0x6F, 0xE0);
            float textWidth = font.MeasureText(sample);
            canvas.DrawText(sample, (size - textWidth) / 2f, size * 0.52f, font, paint);
            string name = Path.GetFileNameWithoutExtension(fullPath);
            if (name.Length > 14)
            {
                name = name.Substring(0, 14);
            }
            float nameSize = Math.Max(9f, size * 0.115f);
            using SKFont nameFont = new SKFont(typeface, nameSize);
            nameFont.Edging = SKFontEdging.Antialias;
            using SKPaint namePaint = new SKPaint();
            namePaint.Color = new SKColor(0x80, 0x80, 0x80);
            float nameWidth = nameFont.MeasureText(name);
            canvas.DrawText(name, (size - nameWidth) / 2f, size * 0.88f, nameFont, namePaint);
            canvas.Flush();
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData? data = image.Encode(SKEncodedImageFormat.Png, 90);
            if (data == null)
            {
                return false;
            }
            File.WriteAllBytes(outputPath, data.ToArray());
            return true;
        }
        catch (Exception error)
        {
            UiLogger.Log("缩略图", "字体预览失败：文件=" + fullPath + " 原因=" + error.Message);
            return false;
        }
    }

    /// <summary>该路径的扩展名是否支持生成缩略图（图片/视频/字体/PDF；PDF 生成时回退图标）。</summary>
    public static bool IsSupportedPath(string fullPath)
    {
        return IsSupported(Path.GetExtension(fullPath ?? string.Empty).ToLowerInvariant());
    }

    private static bool IsSupported(string extension)
    {
        string lower = extension.ToLowerInvariant();
        return IsImageExtension(lower) || IsVideoExtension(lower) || IsFontExtension(lower) || IsPdfExtension(lower);
    }

    private static bool IsImageExtension(string lower)
    {
        return lower == ".png" || lower == ".jpg" || lower == ".jpeg" || lower == ".bmp"
            || lower == ".gif" || lower == ".webp" || lower == ".tif" || lower == ".tiff";
    }

    private static bool IsVideoExtension(string lower)
    {
        return lower == ".mp4" || lower == ".mkv" || lower == ".avi" || lower == ".mov"
            || lower == ".wmv" || lower == ".flv" || lower == ".webm" || lower == ".m4v"
            || lower == ".ts" || lower == ".mpg" || lower == ".mpeg";
    }

    private static bool IsFontExtension(string lower)
    {
        return lower == ".ttf" || lower == ".otf" || lower == ".ttc";
    }

    private static bool IsPdfExtension(string lower)
    {
        return lower == ".pdf";
    }

    private static string BuildKey(string fullPath, long length, long modifiedTicks, int tier)
    {
        string raw = fullPath + "|" + length + "|" + modifiedTicks + "|" + tier;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).Substring(0, 24).ToLowerInvariant() + "-" + tier;
    }

    private static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            PruneCache();
        }
        catch (Exception error)
        {
            UiLogger.Log("缩略图", "初始化缓存目录失败：目录=" + CacheDirectory + " 原因=" + error.Message);
        }
    }

    /// <summary>启动时粗粒度清理：删除 30 天未访问或超过 512MB 的最旧缓存。</summary>
    private static void PruneCache()
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(CacheDirectory).GetFiles("*.png");
            if (files.Length == 0)
            {
                return;
            }
            DateTime threshold = DateTime.UtcNow.AddDays(-30);
            long total = 0;
            List<FileInfo> sorted = new List<FileInfo>();
            foreach (FileInfo file in files)
            {
                total += file.Length;
                if (file.LastAccessTimeUtc < threshold)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception)
                    {
                        // 单个缓存删除失败不阻断初始化
                    }
                }
                else
                {
                    sorted.Add(file);
                }
            }
            const long limit = 512L * 1024 * 1024;
            if (total <= limit)
            {
                return;
            }
            sorted.Sort(delegate(FileInfo left, FileInfo right)
            {
                return left.LastAccessTimeUtc.CompareTo(right.LastAccessTimeUtc);
            });
            foreach (FileInfo file in sorted)
            {
                try
                {
                    file.Delete();
                    total -= file.Length;
                }
                catch (Exception)
                {
                    // 忽略单个文件删除失败
                }
                if (total <= limit)
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            // 缓存清理属于辅助逻辑，失败不影响缩略图生成
        }
    }
}
