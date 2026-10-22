using System;
using System.IO;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>压缩包容器格式。</summary>
public enum ArchiveFormat
{
    Unknown = 0,
    /// <summary>ZIP 压缩包（读写）。</summary>
    Zip,
    /// <summary>7z 压缩包（读写）。</summary>
    SevenZip,
    /// <summary>tar 归档（读写，原始字节文件名透传）。</summary>
    Tar,
    /// <summary>单文件 gzip 压缩（读写）。</summary>
    GZip,
    /// <summary>单文件 bzip2 压缩（读写）。</summary>
    BZip2,
    /// <summary>单文件 xz 压缩（仅解压，压缩算法无托管写实现）。</summary>
    Xz,
    /// <summary>单文件 zstandard 压缩。</summary>
    ZStandard,
    /// <summary>RAR 压缩包（专有格式，只读）。</summary>
    Rar,
    /// <summary>ACE 压缩包（只读）。</summary>
    Ace,
    /// <summary>ARC 压缩包（只读）。</summary>
    Arc,
    /// <summary>ARJ 压缩包（只读）。</summary>
    Arj,
    /// <summary>ISO 光盘映像（仅识别，暂不支持解析）。</summary>
    Iso,
    /// <summary>LZH 压缩包（仅识别，暂不支持解析）。</summary>
    Lzh,
}

/// <summary>tar 外层压缩流类型（tar.gz / tar.bz2 / tar.xz / tar.zst）。</summary>
public enum ArchiveCompression
{
    None = 0,
    GZip,
    BZip2,
    Xz,
    ZStandard,
}

/// <summary>压缩包格式探测结果。</summary>
public class ArchiveFormatInfo
{
    /// <summary>容器格式。</summary>
    public ArchiveFormat Format { get; set; } = ArchiveFormat.Unknown;

    /// <summary>tar 外层压缩流；非 tar 格式恒为 None。</summary>
    public ArchiveCompression Compression { get; set; } = ArchiveCompression.None;

    /// <summary>是否可以解析（列出 / 解压 / 预览）。</summary>
    public bool CanRead { get; set; } = true;

    /// <summary>是否可以创建与修改（追加 / 删除 / 重命名）。</summary>
    public bool CanModify { get; set; } = true;

    /// <summary>格式显示名（中文，用于界面提示）。</summary>
    public string DisplayName
    {
        get { return ArchiveFormats.GetDisplayName(this.Format, this.Compression); }
    }
}

/// <summary>压缩包格式表：扩展名映射、默认扩展名、读写能力与中文显示名。</summary>
public static class ArchiveFormats
{
    /// <summary>按文件扩展名解析格式（含 tar 外层压缩流判定，如 .tar.gz → Tar + GZip）。</summary>
    public static bool TryParsePath(string? path, out ArchiveFormatInfo info)
    {
        info = new ArchiveFormatInfo();
        string extension = (Path.GetExtension(path ?? string.Empty) ?? string.Empty).ToLowerInvariant();
        string fileName = Path.GetFileName(path ?? string.Empty).ToLowerInvariant();
        switch (extension)
        {
            case ".zip":
            case ".zipx":
                info.Format = ArchiveFormat.Zip;
                return true;
            case ".7z":
                info.Format = ArchiveFormat.SevenZip;
                return true;
            case ".tar":
                info.Format = ArchiveFormat.Tar;
                return true;
            case ".gz":
                info.Format = ArchiveFormat.GZip;
                return true;
            case ".tgz":
                info.Format = ArchiveFormat.Tar;
                info.Compression = ArchiveCompression.GZip;
                return true;
            case ".bz2":
            case ".bzip2":
                info.Format = ArchiveFormat.BZip2;
                return true;
            case ".tbz":
            case ".tbz2":
            case ".tb2":
                info.Format = ArchiveFormat.Tar;
                info.Compression = ArchiveCompression.BZip2;
                return true;
            case ".xz":
                info.Format = ArchiveFormat.Xz;
                return true;
            case ".txz":
                info.Format = ArchiveFormat.Tar;
                info.Compression = ArchiveCompression.Xz;
                return true;
            case ".zst":
            case ".zstd":
                info.Format = ArchiveFormat.ZStandard;
                return true;
            case ".tzst":
                info.Format = ArchiveFormat.Tar;
                info.Compression = ArchiveCompression.ZStandard;
                return true;
            case ".rar":
                info.Format = ArchiveFormat.Rar;
                return true;
            case ".ace":
                info.Format = ArchiveFormat.Ace;
                return true;
            case ".arc":
                info.Format = ArchiveFormat.Arc;
                return true;
            case ".arj":
                info.Format = ArchiveFormat.Arj;
                return true;
            case ".iso":
                info.Format = ArchiveFormat.Iso;
                info.CanRead = false;
                info.CanModify = false;
                return true;
            case ".lzh":
            case ".lha":
                info.Format = ArchiveFormat.Lzh;
                info.CanRead = false;
                info.CanModify = false;
                return true;
            default:
                // 兼容 *.tar.gz 双扩展名：Path.GetExtension 已取到 .gz，此处再兜底识别前段
                if (fileName.EndsWith(".tar.gz", StringComparison.Ordinal)
                    || fileName.EndsWith(".tar.bz2", StringComparison.Ordinal)
                    || fileName.EndsWith(".tar.xz", StringComparison.Ordinal)
                    || fileName.EndsWith(".tar.zst", StringComparison.Ordinal))
                {
                    info.Format = ArchiveFormat.Tar;
                    if (fileName.EndsWith(".tar.gz", StringComparison.Ordinal))
                    {
                        info.Compression = ArchiveCompression.GZip;
                    }
                    else if (fileName.EndsWith(".tar.bz2", StringComparison.Ordinal))
                    {
                        info.Compression = ArchiveCompression.BZip2;
                    }
                    else if (fileName.EndsWith(".tar.xz", StringComparison.Ordinal))
                    {
                        info.Compression = ArchiveCompression.Xz;
                    }
                    else
                    {
                        info.Compression = ArchiveCompression.ZStandard;
                    }
                    return true;
                }
                info.Format = ArchiveFormat.Unknown;
                return false;
        }
    }

    /// <summary>文件路径是否具有已知压缩包扩展名（双击拦截判定用）。</summary>
    public static bool IsArchivePath(string? path)
    {
        ArchiveFormatInfo info;
        return TryParsePath(path, out info) && info.Format != ArchiveFormat.Unknown;
    }

    /// <summary>创建指定格式压缩包时使用的默认扩展名（含外层压缩流，如 tar.gz）。</summary>
    public static string GetDefaultExtension(ArchiveFormat format, ArchiveCompression compression)
    {
        if (format == ArchiveFormat.Tar)
        {
            switch (compression)
            {
                case ArchiveCompression.GZip:
                    return ".tar.gz";
                case ArchiveCompression.BZip2:
                    return ".tar.bz2";
                case ArchiveCompression.Xz:
                    return ".tar.xz";
                case ArchiveCompression.ZStandard:
                    return ".tar.zst";
                default:
                    return ".tar";
            }
        }
        switch (format)
        {
            case ArchiveFormat.Zip:
                return ".zip";
            case ArchiveFormat.SevenZip:
                return ".7z";
            case ArchiveFormat.GZip:
                return ".gz";
            case ArchiveFormat.BZip2:
                return ".bz2";
            case ArchiveFormat.Xz:
                return ".xz";
            case ArchiveFormat.ZStandard:
                return ".zst";
            case ArchiveFormat.Rar:
                return ".rar";
            default:
                return string.Empty;
        }
    }

    /// <summary>该格式是否只读（不支持创建与修改）。</summary>
    public static bool IsReadOnly(ArchiveFormat format)
    {
        switch (format)
        {
            case ArchiveFormat.Rar:
            case ArchiveFormat.Ace:
            case ArchiveFormat.Arc:
            case ArchiveFormat.Arj:
            case ArchiveFormat.Iso:
            case ArchiveFormat.Lzh:
                return true;
            default:
                return false;
        }
    }

    /// <summary>该格式是否已被内置引擎完整支持（Iso / LZH 仅识别）。</summary>
    public static bool IsSupported(ArchiveFormat format)
    {
        switch (format)
        {
            case ArchiveFormat.Iso:
            case ArchiveFormat.Lzh:
            case ArchiveFormat.Unknown:
                return false;
            default:
                return true;
        }
    }

    /// <summary>该格式是否支持创建（xz / zstd 单文件与只读格式不支持创建）。</summary>
    public static bool CanCreate(ArchiveFormat format, ArchiveCompression compression)
    {
        switch (format)
        {
            case ArchiveFormat.Tar:
                // tar.xz 不支持：xz 压缩没有托管写实现（解压不受影响）
                return compression != ArchiveCompression.Xz;
            case ArchiveFormat.Zip:
            case ArchiveFormat.SevenZip:
            case ArchiveFormat.GZip:
            case ArchiveFormat.BZip2:
                return true;
            default:
                return false;
        }
    }

    /// <summary>格式中文显示名（界面提示用）。</summary>
    public static string GetDisplayName(ArchiveFormat format, ArchiveCompression compression)
    {
        string container;
        switch (format)
        {
            case ArchiveFormat.Zip:
                container = "ZIP";
                break;
            case ArchiveFormat.SevenZip:
                container = "7z";
                break;
            case ArchiveFormat.Tar:
                container = "tar";
                break;
            case ArchiveFormat.GZip:
                container = "GZip";
                break;
            case ArchiveFormat.BZip2:
                container = "BZip2";
                break;
            case ArchiveFormat.Xz:
                container = "XZ";
                break;
            case ArchiveFormat.ZStandard:
                container = "ZStandard";
                break;
            case ArchiveFormat.Rar:
                container = "RAR";
                break;
            case ArchiveFormat.Ace:
                container = "ACE";
                break;
            case ArchiveFormat.Arc:
                container = "ARC";
                break;
            case ArchiveFormat.Arj:
                container = "ARJ";
                break;
            case ArchiveFormat.Iso:
                container = "ISO";
                break;
            case ArchiveFormat.Lzh:
                container = "LZH";
                break;
            default:
                container = "未知格式";
                break;
        }
        if (format == ArchiveFormat.Tar && compression != ArchiveCompression.None)
        {
            switch (compression)
            {
                case ArchiveCompression.GZip:
                    return "tar.gz 压缩包";
                case ArchiveCompression.BZip2:
                    return "tar.bz2 压缩包";
                case ArchiveCompression.Xz:
                    return "tar.xz 压缩包";
                case ArchiveCompression.ZStandard:
                    return "tar.zst 压缩包";
            }
        }
        return container + " 压缩包";
    }
}
