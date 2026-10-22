using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Common.Options;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Writers.Zip;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// SharpCompress 托管引擎：覆盖 ZIP / 7z / RAR / ACE / ARC / ARJ 等容器格式的
/// 列出、解压、创建与改写（改写 = 全量重写到临时文件后替换）。
/// tar 由 <see cref="TarArchiveEngine"/> 按原始字节处理；单文件压缩流（gz/bz2/xz/zst）由 ArchiveService 处理。
/// </summary>
public sealed class SharpArchiveEngine
{
    private readonly IArchivePathAccess pathAccess;

    public SharpArchiveEngine(IArchivePathAccess pathAccess)
    {
        this.pathAccess = pathAccess;
    }

    // ================================================================
    //  列出
    // ================================================================

    /// <summary>列出压缩包全部条目（目录名已去掉尾部斜杠归一化）。</summary>
    public IReadOnlyList<ArchiveEntry> ReadEntries(string archivePath, ArchiveFormatInfo info, string? password)
    {
        EnsureReadable(info);
        using (IArchive archive = OpenArchive(archivePath, password))
        {
            List<ArchiveEntry> result = new List<ArchiveEntry>();
            foreach (IArchiveEntry entry in archive.Entries)
            {
                string key = NormalizeKey(entry.Key);
                if (key.Length == 0)
                {
                    continue;
                }
                ArchiveEntry item = new ArchiveEntry();
                item.Name = key;
                item.RawName = Encoding.UTF8.GetBytes(key);
                item.IsDirectory = entry.IsDirectory;
                item.IsSymbolicLink = !string.IsNullOrEmpty(entry.LinkTarget);
                item.LengthBytes = entry.IsDirectory ? 0 : entry.Size;
                item.CompressedBytes = entry.CompressedSize;
                item.ModifiedTimeUtc = ToUtc(entry.LastModifiedTime);
                result.Add(item);
            }
            return result;
        }
    }

    // ================================================================
    //  解压
    // ================================================================

    /// <summary>
    /// 解压压缩包到目标目录（目标目录由调用方先行创建）。
    /// 返回失败条目数；失败详情写入 <paramref name="firstError"/>。
    /// </summary>
    public int Extract(string archivePath, ArchiveFormatInfo info, ArchiveExtractOptions options,
        out string firstError, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        EnsureReadable(info);
        firstError = string.Empty;
        int failureCount = 0;
        long entriesDone = 0;
        long bytesDone = 0;
        HashSet<string>? filter = ArchivePathFilter.Build(options.EntryFilter);

        using (IArchive archive = OpenArchive(archivePath, options.Password))
        {
            List<IArchiveEntry> entries = new List<IArchiveEntry>();
            foreach (IArchiveEntry entry in archive.Entries)
            {
                entries.Add(entry);
            }

            foreach (IArchiveEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = NormalizeKey(entry.Key);
                if (key.Length == 0)
                {
                    continue;
                }

                if (!ArchivePathFilter.Matches(filter, key))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.LinkTarget))
                {
                    // 符号链接：与 tar 引擎策略一致，解压时跳过（防路径穿越与跨平台不兼容）
                    continue;
                }

                try
                {
                    if (entry.IsDirectory)
                    {
                        string dirPath = CombineTargetPath(options.TargetDirectory, key);
                        Directory.CreateDirectory(dirPath);
                    }
                    else
                    {
                        this.ExtractOneEntry(entry, key, options);
                    }
                }
                catch (Exception error)
                {
                    failureCount++;
                    if (string.IsNullOrEmpty(firstError))
                    {
                        firstError = key + "：" + error.Message;
                    }
                }

                entriesDone++;
                if (!entry.IsDirectory)
                {
                    bytesDone += entry.Size;
                }
                if (progress != null)
                {
                    ArchiveProgress snapshot = new ArchiveProgress();
                    snapshot.EntriesDone = entriesDone;
                    snapshot.BytesDone = bytesDone;
                    snapshot.CurrentEntryName = key;
                    progress.Report(snapshot);
                }
            }
        }
        return failureCount;
    }

    private void ExtractOneEntry(IArchiveEntry entry, string key, ArchiveExtractOptions options)
    {
        if (!IsSafeKey(key))
        {
            throw new InvalidDataException("条目路径不安全（绝对路径或包含 ..），已拒绝解压");
        }
        string targetPath = CombineTargetPath(options.TargetDirectory, key);
        if (!options.Overwrite && File.Exists(targetPath))
        {
            return;
        }
        string? parent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        using (FileStream output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            ExtractionOptions extractOptions = new ExtractionOptions();
            extractOptions.ExtractFullPath = false;
            extractOptions.Overwrite = true;
            extractOptions.CheckCrc = true;
            entry.WriteTo(output, extractOptions);
        }
    }

    // ================================================================
    //  创建
    // ================================================================

    /// <summary>创建新压缩包（ZIP / 7z），来源条目按顺序写入。</summary>
    public void Create(string archivePath, ArchiveFormatInfo info, IReadOnlyList<ArchiveSourceItem> items,
        ArchiveCreateOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        EnsureWritable(info);
        using (FileStream output = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (IWriter writer = WriterFactory.OpenWriter(output, ToArchiveType(info.Format), BuildWriterOptions(info, options)))
        {
            this.WriteItems(writer, items, progress, cancellationToken);
        }
    }

    /// <summary>把打包源条目顺序写入已打开的写入器。</summary>
    private void WriteItems(IWriter writer, IReadOnlyList<ArchiveSourceItem> items,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        long entriesDone = 0;
        long bytesDone = 0;
        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveSourceItem item = items[i];
            string key = NormalizeKey(item.Name);
            if (key.Length == 0)
            {
                continue;
            }
            if (item.IsDirectory)
            {
                writer.WriteDirectory(key, item.ModifiedTimeUtc);
            }
            else
            {
                if (string.IsNullOrEmpty(item.SourcePath))
                {
                    throw new InvalidOperationException("打包源条目缺少本机来源路径：" + key);
                }
                using (Stream source = this.pathAccess.OpenRead(item.SourcePath))
                {
                    writer.Write(key, source, item.ModifiedTimeUtc);
                }
            }
            entriesDone++;
            if (!item.IsDirectory)
            {
                bytesDone += item.LengthBytes;
            }
            if (progress != null)
            {
                ArchiveProgress snapshot = new ArchiveProgress();
                snapshot.EntriesDone = entriesDone;
                snapshot.BytesDone = bytesDone;
                snapshot.CurrentEntryName = key;
                progress.Report(snapshot);
            }
        }
    }

    // ================================================================
    //  改写（删除 / 重命名 / 追加一步完成）
    // ================================================================

    /// <summary>
    /// 改写压缩包：保留条目重新压缩写入临时文件，被删除条目跳过，
    /// 重命名条目（含其下子条目）按新名字写入，最后追加新条目。
    /// 输出写入 <paramref name="tempOutputPath"/>，由调用方替换原文件。
    /// </summary>
    public void Rewrite(string archivePath, ArchiveFormatInfo info,
        IReadOnlyList<string> removedNames, IReadOnlyList<string[]> renamedPairs,
        IReadOnlyList<ArchiveSourceItem> appendedItems, ArchiveCreateOptions options,
        string tempOutputPath, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        EnsureWritable(info);
        HashSet<string> removeSet = ArchivePathFilter.Build(removedNames) ?? new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string> renameMap = ArchivePathFilter.BuildRenameMap(renamedPairs);

        using (IArchive oldArchive = OpenArchive(archivePath, null))
        using (FileStream output = new FileStream(tempOutputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (IWriter writer = WriterFactory.OpenWriter(output, ToArchiveType(info.Format), BuildWriterOptions(info, options)))
        {
            long entriesDone = 0;
            foreach (IArchiveEntry entry in oldArchive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = NormalizeKey(entry.Key);
                if (key.Length == 0 || ArchivePathFilter.Matches(removeSet, key))
                {
                    continue;
                }

                string newKey;
                if (!ArchivePathFilter.TryRename(renameMap, key, out newKey))
                {
                    newKey = key;
                }

                if (entry.IsDirectory)
                {
                    writer.WriteDirectory(newKey, entry.LastModifiedTime);
                }
                else
                {
                    using (Stream source = entry.OpenEntryStream())
                    {
                        writer.Write(newKey, source, entry.LastModifiedTime);
                    }
                }

                entriesDone++;
                if (progress != null)
                {
                    ArchiveProgress snapshot = new ArchiveProgress();
                    snapshot.EntriesDone = entriesDone;
                    snapshot.CurrentEntryName = newKey;
                    progress.Report(snapshot);
                }
            }

            this.WriteItems(writer, appendedItems, progress, cancellationToken);
        }
    }

    // ================================================================
    //  内部辅助
    // ================================================================

    private static IArchive OpenArchive(string archivePath, string? password)
    {
        ReaderOptions readerOptions = new ReaderOptions();
        ArchiveEncoding encoding = new ArchiveEncoding();
        encoding.CustomDecoder = DecodeArchiveName;
        readerOptions.ArchiveEncoding = encoding;
        if (!string.IsNullOrEmpty(password))
        {
            readerOptions.Password = password;
        }
        return ArchiveFactory.OpenArchive(archivePath, readerOptions);
    }

    /// <summary>归档条目名字智能解码：严格 UTF-8 → GBK → 单字节（吸收 LC_ALL=C 字节语义到字符串界面）。</summary>
    private static string DecodeArchiveName(byte[] buffer, int offset, int length, EncodingType encodingType)
    {
        if (length <= 0)
        {
            return string.Empty;
        }
        byte[] bytes;
        if (offset == 0 && length == buffer.Length)
        {
            bytes = buffer;
        }
        else
        {
            bytes = new byte[length];
            Buffer.BlockCopy(buffer, offset, bytes, 0, length);
        }
        return ArchiveNameCodec.Decode(bytes);
    }

    private static IWriterOptions BuildWriterOptions(ArchiveFormatInfo info, ArchiveCreateOptions options)
    {
        int level = Math.Clamp(options.CompressionLevel, 0, 9);
        if (info.Format == ArchiveFormat.SevenZip)
        {
            SevenZipWriterOptions sevenZipOptions = new SevenZipWriterOptions(CompressionType.LZMA2);
            sevenZipOptions.CompressionLevel = level;
            return sevenZipOptions;
        }
        ZipWriterOptions zipOptions = new ZipWriterOptions(CompressionType.Deflate, level);
        // 条目名以 UTF-8 字节写入并置 ZIP 的 UTF-8 标志位（bit 11 / Efs）：
        // SharpCompress 仅当编码判定为 UTF-8 时才置该标志，
        // 缺失标志会让资源管理器、.NET Framework 等第三方读取器按 ANSI 解码出乱码文件名。
        ArchiveEncoding zipNameEncoding = new ArchiveEncoding();
        zipNameEncoding.Default = Encoding.UTF8;
        zipNameEncoding.UTF8 = Encoding.UTF8;
        zipNameEncoding.Forced = Encoding.UTF8;
        zipOptions.ArchiveEncoding = zipNameEncoding;
        return zipOptions;
    }

    private static ArchiveType ToArchiveType(ArchiveFormat format)
    {
        switch (format)
        {
            case ArchiveFormat.Zip:
                return ArchiveType.Zip;
            case ArchiveFormat.SevenZip:
                return ArchiveType.SevenZip;
            case ArchiveFormat.Rar:
                return ArchiveType.Rar;
            case ArchiveFormat.Ace:
                return ArchiveType.Ace;
            case ArchiveFormat.Arc:
                return ArchiveType.Arc;
            case ArchiveFormat.Arj:
                return ArchiveType.Arj;
            default:
                throw new NotSupportedException("不支持创建该压缩格式：" + ArchiveFormats.GetDisplayName(format, ArchiveCompression.None));
        }
    }

    private static void EnsureReadable(ArchiveFormatInfo info)
    {
        if (!info.CanRead || !ArchiveFormats.IsSupported(info.Format))
        {
            throw new NotSupportedException("该压缩格式暂不支持解析：" + info.DisplayName);
        }
    }

    private static void EnsureWritable(ArchiveFormatInfo info)
    {
        if (!info.CanModify || ArchiveFormats.IsReadOnly(info.Format))
        {
            throw new NotSupportedException("该压缩格式为只读，无法创建或修改：" + info.DisplayName);
        }
    }

    /// <summary>归档内路径归一化：'\' 转 '/'、去掉首尾斜杠。</summary>
    private static string NormalizeKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }
        return key.Replace('\\', '/').Trim('/');
    }

    private static bool IsSafeKey(string key)
    {
        if (key.Length == 0 || key[0] == '/' || key.Contains('\0'))
        {
            return false;
        }
        if (key.Length >= 2 && key[1] == ':')
        {
            return false; // Windows 盘符
        }
        string[] parts = key.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "..")
            {
                return false;
            }
        }
        return true;
    }

    private static string CombineTargetPath(string targetDirectory, string key)
    {
        string relative = key.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(targetDirectory, relative));
    }

    private static DateTime ToUtc(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
        {
            return DateTime.UnixEpoch;
        }
        DateTime dateTime = value.Value;
        if (dateTime.Kind == DateTimeKind.Utc)
        {
            return dateTime;
        }
        if (dateTime.Kind == DateTimeKind.Local)
        {
            return dateTime.ToUniversalTime();
        }
        return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
    }
}
