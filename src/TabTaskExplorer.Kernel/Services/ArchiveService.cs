using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using SharpCompress.Compressors.ZStandard;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 压缩包服务实现：探测（扩展名 + 魔数 + tar 内容嗅探）、分派到具体引擎
/// （tar → 自研原始字节引擎；ZIP / 7z / RAR 等 → SharpCompress；单文件 gz / bz2 / xz / zst → 压缩流直读写），
/// 并负责打包源收集（字符串模式与原始字节模式双路径）与修改压缩包的临时文件替换流程。
/// </summary>
public sealed class ArchiveService : IArchiveService
{
    private readonly IArchivePathAccess pathAccess;
    private readonly TarArchiveEngine tarEngine;
    private readonly SharpArchiveEngine sharpEngine;

    public ArchiveService(IArchivePathAccess pathAccess)
    {
        this.pathAccess = pathAccess;
        this.tarEngine = new TarArchiveEngine(pathAccess);
        this.sharpEngine = new SharpArchiveEngine(pathAccess);
    }

    // ================================================================
    //  探测
    // ================================================================

    /// <summary>文件路径是否具有已知压缩包扩展名（Iso / LZH 等未支持格式不拦截）。</summary>
    public bool IsArchivePath(string path)
    {
        ArchiveFormatInfo info;
        return ArchiveFormats.TryParsePath(path, out info)
            && info.Format != ArchiveFormat.Unknown
            && ArchiveFormats.IsSupported(info.Format);
    }

    /// <summary>
    /// 探测压缩包格式（扩展名 + 文件头魔数校验 + 单文件压缩流的 tar 内容嗅探）。
    /// 文件不存在、扩展名未知或内容与扩展名不符时返回 null。
    /// </summary>
    public ArchiveFormatInfo? Probe(string archivePath)
    {
        try
        {
            return this.RequireInfo(archivePath);
        }
        catch
        {
            return null;
        }
    }

    private ArchiveFormatInfo RequireInfo(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("压缩包路径为空", nameof(archivePath));
        }
        if (!ArchiveFormats.TryParsePath(archivePath, out ArchiveFormatInfo info))
        {
            throw new InvalidDataException("无法识别的压缩包扩展名：" + archivePath);
        }
        if (!ArchiveFormats.IsSupported(info.Format))
        {
            throw new NotSupportedException("该压缩格式暂不支持解析：" + info.DisplayName);
        }
        if (ArchiveFormats.IsReadOnly(info.Format))
        {
            // RAR / ACE / ARC / ARJ 等专有格式：仅支持列出与解压，不支持创建与修改
            info.CanModify = false;
        }
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("压缩包文件不存在：" + archivePath, archivePath);
        }
        if (!ValidateMagic(archivePath, info))
        {
            throw new InvalidDataException("文件内容与扩展名不符或文件已损坏：" + archivePath);
        }
        // 单文件压缩流内容嗅探：内部实际是 tar（扩展名不规范）时按 tar + 对应外层压缩处理
        if (info.Format != ArchiveFormat.Tar && IsSingleStream(info))
        {
            ArchiveCompression compression = SingleToOuter(info.Format);
            if (this.LooksLikeTarInside(archivePath, info.Format))
            {
                info.Format = ArchiveFormat.Tar;
                info.Compression = compression;
            }
            else
            {
                // 单文件压缩流没有"条目"概念，不支持增删改（可重新创建覆盖）
                info.CanModify = false;
            }
        }
        return info;
    }

    // ================================================================
    //  列出
    // ================================================================

    /// <summary>列出压缩包全部条目（含目录；目录名已归一化为无尾斜杠）。</summary>
    public IReadOnlyList<ArchiveEntry> ListEntries(string archivePath, ArchiveListOptions? options = null)
    {
        ArchiveFormatInfo info = this.RequireInfo(archivePath);

        if (info.Format == ArchiveFormat.Tar)
        {
            Stream raw;
            Stream content = this.OpenContent(archivePath, info, out raw);
            try
            {
                return this.tarEngine.ReadEntries(content, options);
            }
            finally
            {
                content.Dispose();
                raw.Dispose();
            }
        }

        if (IsSingleStream(info))
        {
            // 单文件压缩流：只呈现一个条目（文件名 = 压缩包名去掉压缩扩展名）
            ArchiveEntry entry = new ArchiveEntry();
            entry.Name = GetInnerFileName(archivePath);
            entry.RawName = System.Text.Encoding.UTF8.GetBytes(entry.Name);
            entry.LengthBytes = 0;
            entry.ModifiedTimeUtc = File.GetLastWriteTimeUtc(archivePath);
            return new List<ArchiveEntry> { entry };
        }

        return this.sharpEngine.ReadEntries(archivePath, info, null);
    }

    // ================================================================
    //  解压
    // ================================================================

    /// <summary>解压压缩包到指定目录（进度逐条上报，可取消）。</summary>
    public void Extract(string archivePath, ArchiveExtractOptions options,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        this.ExtractCore(archivePath, options, progress, cancellationToken);
    }

    /// <summary>解压指定条目（归档内相对路径列表）到指定目录。</summary>
    public void ExtractEntries(string archivePath, IReadOnlyList<string> entryNames, ArchiveExtractOptions options,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options.EntryFilter = entryNames;
        this.ExtractCore(archivePath, options, progress, cancellationToken);
    }

    private void ExtractCore(string archivePath, ArchiveExtractOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        ArchiveFormatInfo info = this.RequireInfo(archivePath);
        if (!info.CanRead)
        {
            throw new NotSupportedException("该压缩格式暂不支持解压：" + info.DisplayName);
        }
        if (string.IsNullOrEmpty(options.TargetDirectory))
        {
            throw new ArgumentException("解压目标目录为空", nameof(options));
        }
        Directory.CreateDirectory(options.TargetDirectory);

        if (info.Format == ArchiveFormat.Tar)
        {
            Stream raw;
            Stream content = this.OpenContent(archivePath, info, out raw);
            string firstError;
            int failureCount;
            try
            {
                failureCount = this.tarEngine.Extract(content, options, out firstError, progress, cancellationToken);
            }
            finally
            {
                content.Dispose();
                raw.Dispose();
            }
            ThrowIfAnyFailure(failureCount, firstError);
            return;
        }

        if (IsSingleStream(info))
        {
            this.ExtractSingleStream(archivePath, info, options, progress, cancellationToken);
            return;
        }

        string sharpError;
        int sharpFailures = this.sharpEngine.Extract(archivePath, info, options, out sharpError, progress, cancellationToken);
        ThrowIfAnyFailure(sharpFailures, sharpError);
    }

    private static void ThrowIfAnyFailure(int failureCount, string firstError)
    {
        if (failureCount > 0)
        {
            throw new IOException($"解压完成，但有 {failureCount} 个条目失败：{firstError}");
        }
    }

    /// <summary>单文件压缩流解压：把解压后的字节写为目标目录下的单个文件。</summary>
    private void ExtractSingleStream(string archivePath, ArchiveFormatInfo info, ArchiveExtractOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        string innerName = GetInnerFileName(archivePath);
        HashSet<string>? filter = ArchivePathFilter.Build(options.EntryFilter);
        if (!ArchivePathFilter.Matches(filter, innerName))
        {
            return;
        }

        string targetPath = Path.Combine(options.TargetDirectory, innerName);
        if (!options.Overwrite && File.Exists(targetPath))
        {
            return;
        }

        long compressedTotal = new FileInfo(archivePath).Length;
        long bytesDone = 0;
        byte[] buffer = new byte[65536];
        Stream raw;
        Stream content = this.OpenContent(archivePath, info, out raw);
        try
        {
            using (FileStream output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = content.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }
                    output.Write(buffer, 0, read);
                    bytesDone += read;
                    if (progress != null)
                    {
                        ArchiveProgress snapshot = new ArchiveProgress();
                        snapshot.EntriesDone = 1;
                        snapshot.EntriesTotal = 1;
                        snapshot.BytesDone = bytesDone;
                        snapshot.BytesTotal = compressedTotal;
                        snapshot.CurrentEntryName = innerName;
                        progress.Report(snapshot);
                    }
                }
            }
        }
        finally
        {
            content.Dispose();
            raw.Dispose();
        }
    }

    // ================================================================
    //  创建
    // ================================================================

    /// <summary>创建新压缩包（已存在时覆盖），来源可为文件与文件夹（文件夹递归打包）。</summary>
    public void Create(string archivePath, IReadOnlyList<string> sourcePaths, ArchiveCreateOptions options,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!ArchiveFormats.TryParsePath(archivePath, out ArchiveFormatInfo info))
        {
            throw new NotSupportedException("无法识别的压缩包格式：" + archivePath);
        }
        if (!ArchiveFormats.CanCreate(info.Format, info.Compression))
        {
            throw new NotSupportedException("该压缩格式不支持创建：" + ArchiveFormats.GetDisplayName(info.Format, info.Compression));
        }

        List<ArchiveSourceItem> items = this.CollectSourceItems(sourcePaths, options.NameEncoding);
        if (items.Count == 0)
        {
            throw new ArgumentException("没有可打包的文件或文件夹", nameof(sourcePaths));
        }

        if (info.Format == ArchiveFormat.Tar)
        {
            this.CreateTarPackage(archivePath, info.Compression, items, options, progress, cancellationToken);
            return;
        }
        if (info.Format == ArchiveFormat.Zip || info.Format == ArchiveFormat.SevenZip)
        {
            this.sharpEngine.Create(archivePath, info, items, options, progress, cancellationToken);
            return;
        }
        // 单文件压缩流：单个文件直接压缩；目录或多条目先打成 tar 再压（tar.gz / tar.bz2 语义）
        this.CreateSingleStream(archivePath, info.Format, items, options, progress, cancellationToken);
    }

    private void CreateTarPackage(string archivePath, ArchiveCompression compression,
        IReadOnlyList<ArchiveSourceItem> items, ArchiveCreateOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        ArchiveCreateOptions tarOptions = new ArchiveCreateOptions();
        tarOptions.Format = ArchiveFormat.Tar;
        tarOptions.Compression = compression;
        tarOptions.CompressionLevel = options.CompressionLevel;
        tarOptions.NameEncoding = options.NameEncoding;

        using (FileStream output = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (compression == ArchiveCompression.None)
            {
                this.tarEngine.Write(output, items, tarOptions, progress, cancellationToken);
                return;
            }
            Stream compressed = WrapCompress(output, compression, options.CompressionLevel);
            try
            {
                this.tarEngine.Write(compressed, items, tarOptions, progress, cancellationToken);
            }
            finally
            {
                FinishCompress(compressed, compression);
                output.Dispose();
            }
        }
    }

    private void CreateSingleStream(string archivePath, ArchiveFormat format,
        IReadOnlyList<ArchiveSourceItem> items, ArchiveCreateOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        ArchiveCompression compression = format == ArchiveFormat.BZip2
            ? ArchiveCompression.BZip2
            : ArchiveCompression.GZip;
        bool directFile = items.Count == 1 && !items[0].IsDirectory;

        using (FileStream output = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Stream compressed = WrapCompress(output, compression, options.CompressionLevel);
            try
            {
                if (directFile)
                {
                    this.CopySourceToFile(compressed, items[0], progress, cancellationToken);
                }
                else
                {
                    ArchiveCreateOptions tarOptions = new ArchiveCreateOptions();
                    tarOptions.Format = ArchiveFormat.Tar;
                    tarOptions.NameEncoding = options.NameEncoding;
                    this.tarEngine.Write(compressed, items, tarOptions, progress, cancellationToken);
                }
            }
            finally
            {
                FinishCompress(compressed, compression);
                output.Dispose();
            }
        }
    }

    private void CopySourceToFile(Stream output, ArchiveSourceItem item,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(item.SourcePath))
        {
            throw new InvalidOperationException("打包源条目缺少本机来源路径：" + item.Name);
        }
        byte[] buffer = new byte[65536];
        long bytesDone = 0;
        using (Stream source = this.pathAccess.OpenRead(item.SourcePath))
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = source.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                output.Write(buffer, 0, read);
                bytesDone += read;
                if (progress != null)
                {
                    ArchiveProgress snapshot = new ArchiveProgress();
                    snapshot.EntriesDone = 1;
                    snapshot.EntriesTotal = 1;
                    snapshot.BytesDone = bytesDone;
                    snapshot.BytesTotal = item.LengthBytes;
                    snapshot.CurrentEntryName = item.Name;
                    progress.Report(snapshot);
                }
            }
        }
    }

    // ================================================================
    //  修改（追加 / 删除 / 重命名 / 新建文件夹）
    // ================================================================

    /// <summary>
    /// 向已有压缩包追加文件 / 文件夹（修改压缩包）。
    /// destinationInnerDirectory 指定包内目标子目录（'/' 分隔，空或 null 表示放到包根）。
    /// </summary>
    public void AddPaths(string archivePath, IReadOnlyList<string> sourcePaths,
        string? destinationInnerDirectory = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArchiveFormatInfo info = this.RequireModifiable(archivePath);
        List<ArchiveSourceItem> items = this.CollectSourceItems(sourcePaths, null);
        if (items.Count == 0)
        {
            throw new ArgumentException("没有可追加的文件或文件夹", nameof(sourcePaths));
        }
        string destination = (destinationInnerDirectory ?? string.Empty).Replace('\\', '/').Trim('/');
        if (destination.Length > 0)
        {
            PrefixItemNames(items, destination);
        }
        this.ModifyArchive(archivePath, info, null, null, items, progress, cancellationToken);
    }

    /// <summary>把打包源条目名整体加上包内目标目录前缀（粘贴进压缩包子目录时用）。</summary>
    private static void PrefixItemNames(List<ArchiveSourceItem> items, string destination)
    {
        byte[] prefixBytes = ArchiveNameCodec.Encode(destination, null);
        for (int i = 0; i < items.Count; i++)
        {
            ArchiveSourceItem item = items[i];
            byte[] joined = new byte[prefixBytes.Length + 1 + item.NameBytes.Length];
            Buffer.BlockCopy(prefixBytes, 0, joined, 0, prefixBytes.Length);
            joined[prefixBytes.Length] = (byte)'/';
            Buffer.BlockCopy(item.NameBytes, 0, joined, prefixBytes.Length + 1, item.NameBytes.Length);
            item.Name = destination + "/" + item.Name;
            item.NameBytes = joined;
        }
    }

    /// <summary>从压缩包删除条目（文件夹条目连同其下全部条目一并删除）。</summary>
    public void RemoveEntries(string archivePath, IReadOnlyList<string> entryNames,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArchiveFormatInfo info = this.RequireModifiable(archivePath);
        if (entryNames == null || entryNames.Count == 0)
        {
            return;
        }
        this.ModifyArchive(archivePath, info, new List<string>(entryNames), null,
            new List<ArchiveSourceItem>(), progress, cancellationToken);
    }

    /// <summary>重命名压缩包内条目（文件夹连同其下条目一并改名，数据与其余元数据不变）。</summary>
    public void RenameEntry(string archivePath, string entryName, string newName,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArchiveFormatInfo info = this.RequireModifiable(archivePath);
        if (string.IsNullOrEmpty(entryName) || string.IsNullOrEmpty(newName))
        {
            throw new ArgumentException("重命名的新旧名字不能为空");
        }
        List<string[]> pairs = new List<string[]>();
        pairs.Add(new string[] { entryName, newName });
        this.ModifyArchive(archivePath, info, null, pairs, new List<ArchiveSourceItem>(), progress, cancellationToken);
    }

    /// <summary>在压缩包内创建空文件夹条目（文件夹视图"新建文件夹"用）。</summary>
    public void AddDirectory(string archivePath, string directoryInnerPath,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArchiveFormatInfo info = this.RequireModifiable(archivePath);
        string name = (directoryInnerPath ?? string.Empty).Replace('\\', '/').Trim('/');
        if (name.Length == 0)
        {
            throw new ArgumentException("文件夹名为空", nameof(directoryInnerPath));
        }
        ArchiveSourceItem item = new ArchiveSourceItem();
        item.Name = name;
        item.NameBytes = ArchiveNameCodec.Encode(name, null);
        item.IsDirectory = true;
        item.ModifiedTimeUtc = DateTime.UtcNow;
        List<ArchiveSourceItem> appended = new List<ArchiveSourceItem>();
        appended.Add(item);
        this.ModifyArchive(archivePath, info, null, null, appended, progress, cancellationToken);
    }

    private ArchiveFormatInfo RequireModifiable(string archivePath)
    {
        ArchiveFormatInfo info = this.RequireInfo(archivePath);
        if (!info.CanModify || ArchiveFormats.IsReadOnly(info.Format))
        {
            throw new NotSupportedException("该压缩格式为只读，无法修改：" + info.DisplayName);
        }
        if (info.Format == ArchiveFormat.Xz || info.Format == ArchiveFormat.ZStandard
            || info.Format == ArchiveFormat.GZip || info.Format == ArchiveFormat.BZip2)
        {
            throw new NotSupportedException("单文件压缩流没有条目结构，无法修改（可重新压缩覆盖）：" + info.DisplayName);
        }
        return info;
    }

    /// <summary>
    /// 修改压缩包核心流程：改写到同目录临时文件，成功后原地替换原文件，失败清理临时文件。
    /// tar 走原始字节改写（未变更条目零保真损失）；其余格式走 SharpCompress 全量重写。
    /// </summary>
    private void ModifyArchive(string archivePath, ArchiveFormatInfo info,
        List<string>? removedNames, List<string[]>? renamedPairs, List<ArchiveSourceItem> appendedItems,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        ArchiveCreateOptions rewriteOptions = new ArchiveCreateOptions();
        rewriteOptions.Format = info.Format;
        rewriteOptions.Compression = info.Compression;

        string tempPath = archivePath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
        try
        {
            if (info.Format == ArchiveFormat.Tar)
            {
                this.RewriteTar(archivePath, info, removedNames ?? new List<string>(),
                    renamedPairs ?? new List<string[]>(), appendedItems, rewriteOptions,
                    tempPath, progress, cancellationToken);
            }
            else
            {
                this.sharpEngine.Rewrite(archivePath, info, removedNames ?? new List<string>(),
                    renamedPairs ?? new List<string[]>(), appendedItems, rewriteOptions,
                    tempPath, progress, cancellationToken);
            }
            this.pathAccess.ReplaceFile(tempPath, archivePath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void RewriteTar(string archivePath, ArchiveFormatInfo info,
        IReadOnlyList<string> removedNames, IReadOnlyList<string[]> renamedPairs,
        IReadOnlyList<ArchiveSourceItem> appendedItems, ArchiveCreateOptions options,
        string tempPath, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        Stream raw;
        Stream input = this.OpenContent(archivePath, info, out raw);
        try
        {
            using (FileStream tempOutput = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (info.Compression == ArchiveCompression.None)
                {
                    this.tarEngine.Rewrite(input, tempOutput, removedNames, renamedPairs, appendedItems,
                        options, progress, cancellationToken);
                }
                else
                {
                    Stream compressed = WrapCompress(tempOutput, info.Compression, options.CompressionLevel);
                    try
                    {
                        this.tarEngine.Rewrite(input, compressed, removedNames, renamedPairs, appendedItems,
                            options, progress, cancellationToken);
                    }
                    finally
                    {
                        FinishCompress(compressed, info.Compression);
                        tempOutput.Dispose();
                    }
                }
            }
        }
        finally
        {
            input.Dispose();
            raw.Dispose();
        }
    }

    // ================================================================
    //  打包源收集
    // ================================================================

    /// <summary>
    /// 收集打包源（文件 / 文件夹递归）为归档条目列表。
    /// 支持原始字节模式（Linux 文件名字节透传，LC_ALL=C 语义）与字符串模式两条路径。
    /// </summary>
    private List<ArchiveSourceItem> CollectSourceItems(IReadOnlyList<string> sourcePaths, string? nameEncoding)
    {
        List<ArchiveSourceItem> items = new List<ArchiveSourceItem>();
        if (sourcePaths == null)
        {
            return items;
        }
        for (int i = 0; i < sourcePaths.Count; i++)
        {
            string? source = sourcePaths[i];
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }
            string clean = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(clean))
            {
                string fileName = Path.GetFileName(clean);
                items.Add(this.BuildFileItem(clean, ArchiveNameCodec.Encode(fileName, nameEncoding), fileName, nameEncoding));
            }
            else if (Directory.Exists(clean))
            {
                string dirName = Path.GetFileName(clean);
                byte[] dirNameBytes = ArchiveNameCodec.Encode(dirName, nameEncoding);
                if (this.pathAccess.SupportsRawBytePaths)
                {
                    byte[] dirPathBytes = System.Text.Encoding.UTF8.GetBytes(clean);
                    this.CollectRawDirectory(items, dirPathBytes, dirNameBytes, nameEncoding);
                }
                else
                {
                    this.CollectStringDirectory(items, clean, dirName, nameEncoding);
                }
            }
            else
            {
                throw new FileNotFoundException("打包源不存在：" + source, source);
            }
        }
        return items;
    }

    private void CollectStringDirectory(List<ArchiveSourceItem> items, string dirPath, string innerPrefix, string? nameEncoding)
    {
        ArchiveSourceItem dirItem = new ArchiveSourceItem();
        dirItem.Name = innerPrefix;
        dirItem.NameBytes = ArchiveNameCodec.Encode(innerPrefix, nameEncoding);
        dirItem.IsDirectory = true;
        dirItem.SourcePath = dirPath;
        dirItem.ModifiedTimeUtc = Directory.GetLastWriteTimeUtc(dirPath);
        items.Add(dirItem);

        foreach (string subDir in Directory.EnumerateDirectories(dirPath))
        {
            string name = innerPrefix + "/" + Path.GetFileName(subDir);
            this.CollectStringDirectory(items, subDir, name, nameEncoding);
        }
        foreach (string file in Directory.EnumerateFiles(dirPath))
        {
            string name = innerPrefix + "/" + Path.GetFileName(file);
            items.Add(this.BuildFileItem(file, ArchiveNameCodec.Encode(name, nameEncoding), name, nameEncoding));
        }
    }

    /// <summary>原始字节模式收集目录树（Linux）：文件名字节全程不转码，磁盘字节原样进归档。</summary>
    private void CollectRawDirectory(List<ArchiveSourceItem> items, byte[] dirPathBytes, byte[] prefixBytes, string? nameEncoding)
    {
        IReadOnlyList<ArchiveFsRawEntry> entries = this.pathAccess.ListDirectoryRaw(dirPathBytes);
        for (int i = 0; i < entries.Count; i++)
        {
            ArchiveFsRawEntry rawEntry = entries[i];
            if (rawEntry.Name.Length == 0)
            {
                continue;
            }
            byte[] nameBytes = JoinNameBytes(prefixBytes, rawEntry.Name);
            string displayName = ArchiveNameCodec.Decode(nameBytes, nameEncoding);
            if (rawEntry.IsDirectory)
            {
                ArchiveSourceItem dirItem = new ArchiveSourceItem();
                dirItem.Name = displayName;
                dirItem.NameBytes = nameBytes;
                dirItem.IsDirectory = true;
                dirItem.SourcePathRaw = JoinPathBytes(dirPathBytes, rawEntry.Name);
                dirItem.ModifiedTimeUtc = rawEntry.ModifiedTimeUtc;
                dirItem.UnixMode = rawEntry.UnixMode;
                items.Add(dirItem);
                this.CollectRawDirectory(items, dirItem.SourcePathRaw!, nameBytes, nameEncoding);
            }
            else
            {
                ArchiveSourceItem fileItem = new ArchiveSourceItem();
                fileItem.Name = displayName;
                fileItem.NameBytes = nameBytes;
                fileItem.SourcePathRaw = JoinPathBytes(dirPathBytes, rawEntry.Name);
                fileItem.LengthBytes = rawEntry.LengthBytes;
                fileItem.ModifiedTimeUtc = rawEntry.ModifiedTimeUtc;
                fileItem.UnixMode = rawEntry.UnixMode;
                items.Add(fileItem);
            }
        }
    }

    private ArchiveSourceItem BuildFileItem(string sourcePath, byte[] nameBytes, string displayName, string? nameEncoding)
    {
        FileInfo fileInfo = new FileInfo(sourcePath);
        ArchiveSourceItem item = new ArchiveSourceItem();
        item.Name = displayName;
        item.NameBytes = nameBytes;
        item.SourcePath = sourcePath;
        item.LengthBytes = fileInfo.Length;
        item.ModifiedTimeUtc = fileInfo.LastWriteTimeUtc;
        return item;
    }

    private static byte[] JoinNameBytes(byte[] prefix, byte[] name)
    {
        byte[] result = new byte[prefix.Length + 1 + name.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        result[prefix.Length] = (byte)'/';
        Buffer.BlockCopy(name, 0, result, prefix.Length + 1, name.Length);
        return result;
    }

    private static byte[] JoinPathBytes(byte[] dirPath, byte[] name)
    {
        // 目录路径以 '/' 结尾时不再重复分隔符
        if (dirPath.Length > 0 && dirPath[dirPath.Length - 1] == (byte)'/')
        {
            byte[] joined = new byte[dirPath.Length + name.Length];
            Buffer.BlockCopy(dirPath, 0, joined, 0, dirPath.Length);
            Buffer.BlockCopy(name, 0, joined, dirPath.Length, name.Length);
            return joined;
        }
        byte[] result = new byte[dirPath.Length + 1 + name.Length];
        Buffer.BlockCopy(dirPath, 0, result, 0, dirPath.Length);
        result[dirPath.Length] = (byte)'/';
        Buffer.BlockCopy(name, 0, result, dirPath.Length + 1, name.Length);
        return result;
    }

    // ================================================================
    //  内容流与压缩包装
    // ================================================================

    /// <summary>打开压缩包内容流：tar + 外层压缩时返回解压包装流；rawStream 为底层文件流（调用方一并释放）。</summary>
    private Stream OpenContent(string archivePath, ArchiveFormatInfo info, out Stream rawStream)
    {
        rawStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (info.Format == ArchiveFormat.Tar && info.Compression != ArchiveCompression.None)
        {
            return WrapDecompress(rawStream, info.Compression);
        }
        if (info.Format != ArchiveFormat.Tar && IsSingleStream(info))
        {
            return WrapDecompress(rawStream, SingleToOuter(info.Format));
        }
        return rawStream;
    }

    private static Stream WrapDecompress(Stream raw, ArchiveCompression compression)
    {
        switch (compression)
        {
            case ArchiveCompression.GZip:
                return new GZipStream(raw, CompressionMode.Decompress, leaveOpen: false);
            case ArchiveCompression.BZip2:
                return BZip2Stream.Create(raw, SharpCompress.Compressors.CompressionMode.Decompress, true, leaveOpen: false);
            case ArchiveCompression.Xz:
                return new XZStream(raw);
            case ArchiveCompression.ZStandard:
                return new DecompressionStream(raw, bufferSize: 65536, checkEndOfStream: false, leaveOpen: false);
            default:
                return raw;
        }
    }

    private static Stream WrapCompress(Stream output, ArchiveCompression compression, int level)
    {
        switch (compression)
        {
            case ArchiveCompression.GZip:
                CompressionLevel bclLevel = level <= 0 ? CompressionLevel.NoCompression
                    : level <= 5 ? CompressionLevel.Fastest
                    : CompressionLevel.SmallestSize;
                return new GZipStream(output, bclLevel, leaveOpen: false);
            case ArchiveCompression.BZip2:
                return BZip2Stream.Create(output, SharpCompress.Compressors.CompressionMode.Compress, false, leaveOpen: false);
            case ArchiveCompression.ZStandard:
                return new CompressionStream(output, Math.Clamp(level, 1, 22), leaveOpen: false);
            default:
                throw new NotSupportedException("该外层压缩类型没有托管写实现：" + compression);
        }
    }

    /// <summary>完成压缩写出（bzip2 需要显式 Finish；gzip / zstd 释放即完成）。收尾失败直接抛出，避免用残缺文件替换原包。</summary>
    private static void FinishCompress(Stream compressed, ArchiveCompression compression)
    {
        if (compression == ArchiveCompression.BZip2 && compressed is BZip2Stream bzip2)
        {
            bzip2.Finish();
        }
        compressed.Dispose();
    }

    /// <summary>单文件压缩流（非 tar）内容嗅探：解压前 512 字节判断是否为 tar 归档头。</summary>
    private bool LooksLikeTarInside(string archivePath, ArchiveFormat format)
    {
        try
        {
            FileStream raw = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Stream content;
            try
            {
                content = WrapDecompress(raw, SingleToOuter(format));
            }
            catch
            {
                raw.Dispose();
                return false;
            }
            try
            {
                byte[] head = new byte[512];
                int total = 0;
                while (total < head.Length)
                {
                    int read = content.Read(head, total, head.Length - total);
                    if (read <= 0)
                    {
                        break;
                    }
                    total += read;
                }
                return total == head.Length && TarArchiveEngine.LooksLikeTarHeader(head);
            }
            finally
            {
                content.Dispose();
                raw.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    // ================================================================
    //  杂项辅助
    // ================================================================

    private static bool ValidateCompressionMagic(byte[] head, int total, ArchiveCompression compression)
    {
        switch (compression)
        {
            case ArchiveCompression.GZip:
                return total >= 2 && head[0] == 0x1F && head[1] == 0x8B;
            case ArchiveCompression.BZip2:
                return total >= 3 && head[0] == (byte)'B' && head[1] == (byte)'Z' && head[2] == (byte)'h';
            case ArchiveCompression.Xz:
                return total >= 6 && head[0] == 0xFD && head[1] == 0x37 && head[2] == 0x7A
                    && head[3] == 0x58 && head[4] == 0x5A && head[5] == 0x00;
            case ArchiveCompression.ZStandard:
                return total >= 4 && head[0] == 0x28 && head[1] == 0xB5 && head[2] == 0x2F && head[3] == 0xFD;
            default:
                return true;
        }
    }

    /// <summary>压缩包文件头魔数校验（扩展名与内容是否一致）。</summary>
    private static bool ValidateMagic(string archivePath, ArchiveFormatInfo info)
    {
        try
        {
            byte[] head = new byte[512];
            int total;
            using (FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                total = stream.Read(head, 0, head.Length);
            }
            if (total <= 0)
            {
                return false;
            }
            switch (info.Format)
            {
                case ArchiveFormat.Zip:
                    return total >= 4 && head[0] == (byte)'P' && head[1] == (byte)'K';
                case ArchiveFormat.SevenZip:
                    return total >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC
                        && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C;
                case ArchiveFormat.Rar:
                    return total >= 7 && head[0] == (byte)'R' && head[1] == (byte)'a' && head[2] == (byte)'r'
                        && head[3] == (byte)'!' && head[4] == 0x1A && head[5] == 0x07;
                case ArchiveFormat.GZip:
                    return total >= 2 && head[0] == 0x1F && head[1] == 0x8B;
                case ArchiveFormat.BZip2:
                    return total >= 3 && head[0] == (byte)'B' && head[1] == (byte)'Z' && head[2] == (byte)'h';
                case ArchiveFormat.Xz:
                    return total >= 6 && head[0] == 0xFD && head[1] == 0x37 && head[2] == 0x7A
                        && head[3] == 0x58 && head[4] == 0x5A && head[5] == 0x00;
                case ArchiveFormat.ZStandard:
                    return total >= 4 && head[0] == 0x28 && head[1] == 0xB5 && head[2] == 0x2F && head[3] == 0xFD;
                case ArchiveFormat.Tar:
                    // tar + 外层压缩（tar.gz 等）：先校验外层压缩流魔数
                    if (info.Compression != ArchiveCompression.None)
                    {
                        return ValidateCompressionMagic(head, total, info.Compression);
                    }
                    if (TarArchiveEngine.LooksLikeTarHeader(head))
                    {
                        return true;
                    }
                    // 空 tar：前两个 512 字节块全零且长度为块的整数倍
                    bool allZero = true;
                    for (int i = 0; i < Math.Min(total, head.Length); i++)
                    {
                        if (head[i] != 0)
                        {
                            allZero = false;
                            break;
                        }
                    }
                    FileInfo fileInfo = new FileInfo(archivePath);
                    return allZero && fileInfo.Length >= 1024 && fileInfo.Length % 512 == 0;
                case ArchiveFormat.Ace:
                    // ACE 魔数：偏移 7 处的 **ACE**
                    for (int i = 0; i + 7 <= total; i++)
                    {
                        if (head[i] == (byte)'*' && head[i + 1] == (byte)'*' && head[i + 2] == (byte)'A'
                            && head[i + 3] == (byte)'C' && head[i + 4] == (byte)'E' && head[i + 5] == (byte)'*'
                            && head[i + 6] == (byte)'*')
                        {
                            return true;
                        }
                    }
                    return false;
                case ArchiveFormat.Arj:
                    return total >= 2 && head[0] == 0x60 && head[1] == 0xEA;
                case ArchiveFormat.Arc:
                    return true; // ARC 无稳定魔数，按扩展名接受
                default:
                    return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSingleStream(ArchiveFormatInfo info)
    {
        return info.Format == ArchiveFormat.GZip || info.Format == ArchiveFormat.BZip2
            || info.Format == ArchiveFormat.Xz || info.Format == ArchiveFormat.ZStandard;
    }

    private static ArchiveCompression SingleToOuter(ArchiveFormat format)
    {
        switch (format)
        {
            case ArchiveFormat.GZip:
                return ArchiveCompression.GZip;
            case ArchiveFormat.BZip2:
                return ArchiveCompression.BZip2;
            case ArchiveFormat.Xz:
                return ArchiveCompression.Xz;
            case ArchiveFormat.ZStandard:
                return ArchiveCompression.ZStandard;
            default:
                return ArchiveCompression.None;
        }
    }

    /// <summary>压缩包内单文件条目的显示名：去掉压缩扩展名（a.txt.gz → a.txt）。</summary>
    private static string GetInnerFileName(string archivePath)
    {
        string fileName = Path.GetFileName(archivePath);
        string inner = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrEmpty(inner) ? fileName : inner;
    }
}
