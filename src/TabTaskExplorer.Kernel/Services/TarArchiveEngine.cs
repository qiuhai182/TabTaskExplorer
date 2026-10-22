using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 自研 tar 引擎：ustar / GNU / PAX 归档头的解析、生成与字节级改写。
/// 文件名字节按原始序列写入与读出，打包与解包两侧均不做字符集转码
/// （对应 gnu-tar 的 LC_ALL=C 字节透传语义）；字符串仅用于界面显示与字符串模式平台的落盘。
/// </summary>
public sealed class TarArchiveEngine
{
    private const int BlockSize = 512;
    private const int LongNameLimit = 100;
    private const int PrefixLimit = 155;
    private const long OctalMax = 8589934591L; // 11 位八进制上限（8 GB - 1）

    private readonly IArchivePathAccess pathAccess;

    public TarArchiveEngine(IArchivePathAccess pathAccess)
    {
        this.pathAccess = pathAccess;
    }

    /// <summary>tar 归档头解析结果（逻辑条目）。</summary>
    private sealed class TarHeaderInfo
    {
        public byte[] NameBytes = Array.Empty<byte>();
        public byte[] LinkNameBytes = Array.Empty<byte>();
        public bool IsDirectory;
        public bool IsSymbolicLink;
        public bool IsRegularFile;
        public long Size;
        public long ModifiedUnixSeconds;
        public int Mode;
        /// <summary>该条目前面的原始辅助块（GNU LongLink / PAX 扩展头），改写保留用。</summary>
        public List<byte[]> PreBlocks = new List<byte[]>();
    }

    // ================================================================
    //  列出
    // ================================================================

    /// <summary>读取 tar 归档的全部条目（只读头与跳过数据，不落地）。</summary>
    public List<ArchiveEntry> ReadEntries(Stream input, ArchiveListOptions? options)
    {
        List<ArchiveEntry> entries = new List<ArchiveEntry>();
        string? nameEncoding = options == null ? null : options.NameEncoding;
        byte[] header = new byte[BlockSize];
        while (true)
        {
            BlockReadState state = TryReadBlock(input, header);
            if (state != BlockReadState.Data)
            {
                break;
            }
            TarHeaderInfo? info;
            if (!this.TryParseHeaderBlock(input, header, out info))
            {
                throw new InvalidDataException("tar 归档头校验和不正确，文件可能已损坏");
            }
            if (info == null)
            {
                // 辅助块（LongLink / PAX），继续读下一个块
                continue;
            }
            entries.Add(this.ToArchiveEntry(info, nameEncoding));
            SkipData(input, info.Size);
        }
        return entries;
    }

    private ArchiveEntry ToArchiveEntry(TarHeaderInfo info, string? nameEncoding)
    {
        ArchiveEntry entry = new ArchiveEntry();
        entry.RawName = info.NameBytes;
        entry.Name = ArchiveNameCodec.Decode(info.NameBytes, nameEncoding);
        entry.IsDirectory = info.IsDirectory;
        if (entry.IsDirectory)
        {
            // 目录名统一去掉尾部斜杠（界面显示与后续增删改按此归一化名字匹配）
            entry.Name = TrimTrailingSlash(entry.Name);
        }
        entry.IsSymbolicLink = info.IsSymbolicLink;
        entry.LengthBytes = info.IsDirectory ? 0 : info.Size;
        entry.ModifiedTimeUtc = UnixToUtc(info.ModifiedUnixSeconds);
        return entry;
    }

    // ================================================================
    //  解压
    // ================================================================

    /// <summary>
    /// 解压 tar 流到目标目录（目标目录由调用方先行创建）。
    /// 返回失败条目数；失败详情写入 <paramref name="firstError"/>。
    /// </summary>
    public int Extract(Stream input, ArchiveExtractOptions options, out string firstError,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        firstError = string.Empty;
        int failureCount = 0;
        long entriesDone = 0;
        long bytesDone = 0;
        bool rawMode = this.pathAccess.SupportsRawBytePaths;
        byte[]? targetDirBytes = rawMode ? Encoding.UTF8.GetBytes(options.TargetDirectory) : null;
        HashSet<string>? filter = BuildFilter(options);
        byte[] header = new byte[BlockSize];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BlockReadState state = TryReadBlock(input, header);
            if (state != BlockReadState.Data)
            {
                break;
            }
            TarHeaderInfo? info;
            if (!this.TryParseHeaderBlock(input, header, out info))
            {
                throw new InvalidDataException("tar 归档头校验和不正确，文件可能已损坏");
            }
            if (info == null)
            {
                continue;
            }
            string displayName = ArchiveNameCodec.Decode(info.NameBytes, options.NameEncoding);

            bool selected = ArchivePathFilter.Matches(filter, displayName);
            if (!selected)
            {
                SkipData(input, info.Size);
                continue;
            }

            if (!info.IsRegularFile && !info.IsDirectory)
            {
                // 符号链接 / 硬链接 / 设备文件等：解压时跳过（避免链接型路径穿越与跨平台不兼容）
                SkipData(input, info.Size);
                continue;
            }

            try
            {
                if (rawMode)
                {
                    this.ExtractRaw(targetDirBytes!, info, options, input);
                }
                else
                {
                    this.ExtractString(info, displayName, options, input);
                }
                // 数据字节已按精确长度读走，此处再越过输入侧的 512 对齐填充，
                // 否则下一条目的头会错位（校验和失败）。
                SkipPadding(input, info.Size);
            }
            catch (Exception error)
            {
                failureCount++;
                if (string.IsNullOrEmpty(firstError))
                {
                    firstError = displayName + "：" + error.Message;
                }
                // 已失败：把该条目的数据从流中吃掉，保持后续条目对齐
                SkipData(input, info.Size);
            }

            entriesDone++;
            if (!info.IsDirectory)
            {
                bytesDone += info.Size;
            }
            if (progress != null)
            {
                ArchiveProgress snapshot = new ArchiveProgress();
                snapshot.EntriesDone = entriesDone;
                snapshot.BytesDone = bytesDone;
                snapshot.CurrentEntryName = displayName;
                progress.Report(snapshot);
            }
        }
        return failureCount;
    }

    private void ExtractRaw(byte[] targetDirBytes, TarHeaderInfo info, ArchiveExtractOptions options, Stream input)
    {
        if (!IsSafeNameBytes(info.NameBytes))
        {
            throw new InvalidDataException("条目路径不安全（绝对路径或包含 ..），已拒绝解压");
        }
        byte[] fullBytes = JoinPath(targetDirBytes, info.NameBytes);
        if (info.IsDirectory)
        {
            this.pathAccess.CreateDirectoryRaw(fullBytes);
            return;
        }
        byte[] parentBytes = ParentPath(fullBytes);
        if (parentBytes.Length > 0)
        {
            this.pathAccess.CreateDirectoryRaw(parentBytes);
        }
        if (!options.Overwrite && this.pathAccess.FileExistsRaw(fullBytes))
        {
            // 跳过不覆盖：数据字节仍要从输入流吃掉，保持后续条目对齐
            SkipBytes(input, info.Size);
            return;
        }
        using (Stream output = this.pathAccess.CreateFileRaw(fullBytes))
        {
            CopyExactly(input, output, info.Size);
        }
    }

    private void ExtractString(TarHeaderInfo info, string displayName, ArchiveExtractOptions options, Stream input)
    {
        if (!IsSafeNameString(displayName))
        {
            throw new InvalidDataException("条目路径不安全（绝对路径或包含 ..），已拒绝解压");
        }
        string relative = displayName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string fullPath = Path.Combine(options.TargetDirectory, relative);
        if (info.IsDirectory)
        {
            this.pathAccess.CreateDirectory(fullPath);
            return;
        }
        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            this.pathAccess.CreateDirectory(parent);
        }
        if (!options.Overwrite && this.pathAccess.FileExists(fullPath))
        {
            // 跳过不覆盖：数据字节仍要从输入流吃掉，保持后续条目对齐
            SkipBytes(input, info.Size);
            return;
        }
        using (Stream output = this.pathAccess.CreateFile(fullPath))
        {
            CopyExactly(input, output, info.Size);
        }
    }

    private static HashSet<string>? BuildFilter(ArchiveExtractOptions options)
    {
        IReadOnlyList<string>? filter = options.EntryFilter;
        if (filter == null || filter.Count == 0)
        {
            return null;
        }
        HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < filter.Count; i++)
        {
            string name = (filter[i] ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (name.Length > 0)
            {
                set.Add(name);
                set.Add(TrimTrailingSlash(name));
            }
        }
        return set;
    }

    // ================================================================
    //  生成
    // ================================================================

    /// <summary>把打包源条目顺序写入 tar 流（含收尾零块）。</summary>
    public void Write(Stream output, IReadOnlyList<ArchiveSourceItem> items, ArchiveCreateOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        long entriesDone = 0;
        long bytesDone = 0;
        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveSourceItem item = items[i];
            this.WriteOneEntry(output, item, options);
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
                snapshot.CurrentEntryName = item.Name;
                progress.Report(snapshot);
            }
        }
        WriteBlock(output, new byte[BlockSize]);
        WriteBlock(output, new byte[BlockSize]);
    }

    private void WriteOneEntry(Stream output, ArchiveSourceItem item, ArchiveCreateOptions options)
    {
        byte[] nameBytes = item.NameBytes.Length > 0
            ? item.NameBytes
            : ArchiveNameCodec.Encode(item.Name, options.NameEncoding);
        bool isDirectory = item.IsDirectory;
        if (isDirectory && (nameBytes.Length == 0 || nameBytes[nameBytes.Length - 1] != (byte)'/'))
        {
            byte[] withSlash = new byte[nameBytes.Length + 1];
            Buffer.BlockCopy(nameBytes, 0, withSlash, 0, nameBytes.Length);
            withSlash[nameBytes.Length] = (byte)'/';
            nameBytes = withSlash;
        }

        int mode = item.UnixMode != 0
            ? item.UnixMode & 0x7777
            : (isDirectory ? 0x1ED : 0x1A4); // 0755 / 0644
        long size = isDirectory ? 0 : item.LengthBytes;
        long mtime = UtcToUnix(item.ModifiedTimeUtc);

        byte[] nameField;
        byte[] prefixField = new byte[155];
        byte[]? preHeader = null;
        if (!TrySplitUstarName(nameBytes, out nameField, out prefixField))
        {
            // 超长文件名：写 PAX 扩展头（path 记录按原始字节透传），随后写截断的真实头
            byte[] paxRecord = BuildPaxPathRecord(nameBytes);
            preHeader = new byte[BlockSize];
            FillHeaderBase(preHeader, Encoding.ASCII.GetBytes("././@PaxHeader"), 0x1A4, 0, 0,
                paxRecord.Length, mtime, (byte)'x');
            WriteBlock(output, preHeader);
            WritePadded(output, paxRecord, paxRecord.Length);
            nameField = new byte[100];
            int copyLength = Math.Min(100, nameBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, nameField, 0, copyLength);
        }

        byte[] header = new byte[BlockSize];
        FillHeaderBase(header, nameField, mode, 0, 0, size, mtime, (byte)(isDirectory ? '5' : '0'));
        Buffer.BlockCopy(prefixField, 0, header, 345, 155);
        WriteBlock(output, header);

        if (!isDirectory)
        {
            this.WriteItemData(output, item, size);
        }
    }

    private void WriteItemData(Stream output, ArchiveSourceItem item, long size)
    {
        if (size == 0)
        {
            return;
        }
        Stream? input = null;
        try
        {
            if (item.SourcePathRaw != null)
            {
                input = this.pathAccess.OpenReadRaw(item.SourcePathRaw);
            }
            else if (!string.IsNullOrEmpty(item.SourcePath))
            {
                input = this.pathAccess.OpenRead(item.SourcePath);
            }
            else
            {
                throw new InvalidOperationException("打包源条目缺少本机来源路径：" + item.Name);
            }
            CopyExactly(input, output, size);
        }
        finally
        {
            if (input != null)
            {
                input.Dispose();
            }
        }
        // 数据补齐到 512 边界
        int remainder = (int)(size % BlockSize);
        if (remainder != 0)
        {
            output.Write(new byte[BlockSize - remainder], 0, BlockSize - remainder);
        }
    }

    // ================================================================
    //  改写（保留未变更条目的原始字节）
    // ================================================================

    /// <summary>
    /// 改写 tar 流：未变更条目的归档头与数据按原始字节复制（零保真损失），
    /// 重命名条目重新生成归档头并复制数据，删除条目整体跳过，最后追加新条目与收尾零块。
    /// removedNames / renamedPairs 的名称为归档内相对路径（'/' 分隔、与列出结果一致）。
    /// </summary>
    public void Rewrite(Stream input, Stream output,
        IReadOnlyList<string> removedNames, IReadOnlyList<string[]> renamedPairs,
        IReadOnlyList<ArchiveSourceItem> appendedItems, ArchiveCreateOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        HashSet<string> removeSet = ToNameSet(removedNames);
        Dictionary<string, string> renameMap = ArchivePathFilter.BuildRenameMap(renamedPairs);

        long entriesDone = 0;
        byte[] header = new byte[BlockSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BlockReadState state = TryReadBlock(input, header);
            if (state != BlockReadState.Data)
            {
                break;
            }
            TarHeaderInfo? info;
            if (!this.TryParseHeaderBlock(input, header, out info))
            {
                throw new InvalidDataException("tar 归档头校验和不正确，文件可能已损坏");
            }
            if (info == null)
            {
                continue;
            }
            string name = ArchiveNameCodec.Decode(info.NameBytes, options.NameEncoding);

            if (ArchivePathFilter.Matches(removeSet, name))
            {
                SkipData(input, info.Size);
                entriesDone++;
                continue;
            }

            string? newName;
            if (ArchivePathFilter.TryRename(renameMap, name, out newName))
            {
                // 重命名：按原元数据 + 新文件名重新生成头，再原样复制数据
                byte[] newNameBytes = ArchiveNameCodec.Encode(newName, options.NameEncoding);
                if (info.IsDirectory && (newNameBytes.Length == 0 || newNameBytes[newNameBytes.Length - 1] != (byte)'/'))
                {
                    byte[] withSlash = new byte[newNameBytes.Length + 1];
                    Buffer.BlockCopy(newNameBytes, 0, withSlash, 0, newNameBytes.Length);
                    withSlash[newNameBytes.Length] = (byte)'/';
                    newNameBytes = withSlash;
                }
                this.WriteRenamedHeader(output, info, newNameBytes);
                CopyData(input, output, info.Size);
            }
            else
            {
                // 未变更：辅助块与真实头全部按原始字节复制
                for (int i = 0; i < info.PreBlocks.Count; i++)
                {
                    output.Write(info.PreBlocks[i], 0, info.PreBlocks[i].Length);
                }
                output.Write(header, 0, header.Length);
                CopyData(input, output, info.Size);
            }
            entriesDone++;
            if (progress != null)
            {
                ArchiveProgress snapshot = new ArchiveProgress();
                snapshot.EntriesDone = entriesDone;
                snapshot.CurrentEntryName = name;
                progress.Report(snapshot);
            }
        }

        for (int i = 0; i < appendedItems.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.WriteOneEntry(output, appendedItems[i], options);
            entriesDone++;
            if (progress != null)
            {
                ArchiveProgress snapshot = new ArchiveProgress();
                snapshot.EntriesDone = entriesDone;
                snapshot.CurrentEntryName = appendedItems[i].Name;
                progress.Report(snapshot);
            }
        }

        WriteBlock(output, new byte[BlockSize]);
        WriteBlock(output, new byte[BlockSize]);
    }

    private void WriteRenamedHeader(Stream output, TarHeaderInfo info, byte[] nameBytes)
    {
        byte[] nameField;
        byte[] prefixField = new byte[155];
        if (!TrySplitUstarName(nameBytes, out nameField, out prefixField))
        {
            byte[] paxRecord = BuildPaxPathRecord(nameBytes);
            byte[] preHeader = new byte[BlockSize];
            FillHeaderBase(preHeader, Encoding.ASCII.GetBytes("././@PaxHeader"), 0x1A4, 0, 0,
                paxRecord.Length, info.ModifiedUnixSeconds, (byte)'x');
            WriteBlock(output, preHeader);
            WritePadded(output, paxRecord, paxRecord.Length);
            nameField = new byte[100];
            int copyLength = Math.Min(100, nameBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, nameField, 0, copyLength);
        }

        byte typeFlag;
        if (info.IsDirectory)
        {
            typeFlag = (byte)'5';
        }
        else if (info.IsSymbolicLink)
        {
            typeFlag = (byte)'2';
        }
        else if (info.IsRegularFile)
        {
            typeFlag = (byte)'0';
        }
        else
        {
            typeFlag = (byte)'1'; // 硬链接
        }

        byte[] header = new byte[BlockSize];
        FillHeaderBase(header, nameField, info.Mode, 0, 0, info.Size, info.ModifiedUnixSeconds, typeFlag);
        Buffer.BlockCopy(info.LinkNameBytes, 0, header, 157, Math.Min(100, info.LinkNameBytes.Length));
        Buffer.BlockCopy(prefixField, 0, header, 345, 155);
        WriteBlock(output, header);
    }

    // ================================================================
    //  归档头解析
    // ================================================================

    private enum BlockReadState
    {
        /// <summary>读到有效数据块。</summary>
        Data = 0,
        /// <summary>读到全零块或干净结束（归档正常收尾）。</summary>
        End,
    }

    private static BlockReadState TryReadBlock(Stream input, byte[] buffer)
    {
        int total = 0;
        while (total < BlockSize)
        {
            int read = input.Read(buffer, total, BlockSize - total);
            if (read <= 0)
            {
                if (total == 0)
                {
                    return BlockReadState.End;
                }
                throw new InvalidDataException("tar 归档数据截断，文件不完整");
            }
            total += read;
        }
        bool allZero = true;
        for (int i = 0; i < BlockSize; i++)
        {
            if (buffer[i] != 0)
            {
                allZero = false;
                break;
            }
        }
        return allZero ? BlockReadState.End : BlockReadState.Data;
    }

    /// <summary>解析一个 512 字节块：辅助块返回 null，普通条目返回解析结果，校验和错误抛异常。</summary>
    private bool TryParseHeaderBlock(Stream input, byte[] header, out TarHeaderInfo? info)
    {
        info = null;
        if (!IsChecksumValid(header))
        {
            return false;
        }

        byte typeFlag = header[156];
        long size = ParseNumber(header, 124, 12);
        long mtime = ParseNumber(header, 136, 12);
        int mode = (int)ParseNumber(header, 100, 8);

        if (typeFlag == (byte)'L' || typeFlag == (byte)'K')
        {
            // GNU 超长文件名 / 链接名：读取数据作为下一条目的名字，头与数据按原始字节保留（改写时原样回写）
            byte[] data = ReadDataBytes(input, size, out byte[] paddedData);
            this.AddPendingPreBlock(header, paddedData);
            if (typeFlag == (byte)'L')
            {
                this.pendingLongName = TrimTrailingNulls(data);
            }
            return true;
        }
        if (typeFlag == (byte)'x' || typeFlag == (byte)'X' || typeFlag == (byte)'g')
        {
            byte[] data = ReadDataBytes(input, size, out byte[] paddedData);
            // 扩展头同样按原始字节保留；'g' 全局块只保留字节、不应用其记录
            this.AddPendingPreBlock(header, paddedData);
            if (typeFlag != (byte)'g')
            {
                this.pendingPaxRecords = ParsePaxRecords(data);
            }
            return true;
        }

        // 真实条目头
        TarHeaderInfo parsed = new TarHeaderInfo();
        byte[] nameBytes = ResolveNameBytes(header);
        if (this.pendingLongName != null)
        {
            nameBytes = this.pendingLongName;
            this.pendingLongName = null;
        }
        if (this.pendingPaxRecords != null)
        {
            byte[]? paxPath;
            long? paxSize;
            long? paxMtime;
            this.pendingPaxRecords.Apply(out paxPath, out paxSize, out paxMtime);
            if (paxPath != null)
            {
                nameBytes = paxPath;
            }
            if (paxSize.HasValue)
            {
                size = paxSize.Value;
            }
            if (paxMtime.HasValue)
            {
                mtime = paxMtime.Value;
            }
            this.pendingPaxRecords = null;
        }

        parsed.NameBytes = nameBytes;
        parsed.LinkNameBytes = SliceField(header, 157, 100);
        parsed.Size = size;
        parsed.ModifiedUnixSeconds = mtime;
        parsed.Mode = mode;
        parsed.IsDirectory = typeFlag == (byte)'5';
        parsed.IsSymbolicLink = typeFlag == (byte)'2';
        parsed.IsRegularFile = typeFlag == (byte)'0' || typeFlag == 0 || typeFlag == (byte)'7';
        // 注意：硬链接（'1'）不视为普通文件，解压时跳过，避免空文件与路径穿越风险
        // 把此前累积的辅助块（GNU LongLink / PAX）交给该真实条目，改写时按原始字节回写
        parsed.PreBlocks = this.pendingPreBlocks ?? new List<byte[]>();
        this.pendingPreBlocks = null;
        info = parsed;
        return true;
    }

    // 辅助块状态（GNU LongLink 与 PAX 扩展头作用于紧随其后的真实条目）
    private byte[]? pendingLongName;
    private PaxRecords? pendingPaxRecords;

    /// <summary>累积待回写的辅助块原始字节（512 字节头 + 补零数据），随真实条目一起输出。</summary>
    private List<byte[]>? pendingPreBlocks;

    private void AddPendingPreBlock(byte[] header, byte[] paddedData)
    {
        if (this.pendingPreBlocks == null)
        {
            this.pendingPreBlocks = new List<byte[]>();
        }
        byte[] headerCopy = new byte[BlockSize];
        Buffer.BlockCopy(header, 0, headerCopy, 0, BlockSize);
        this.pendingPreBlocks.Add(headerCopy);
        if (paddedData.Length > 0)
        {
            this.pendingPreBlocks.Add(paddedData);
        }
    }

    private static byte[] ResolveNameBytes(byte[] header)
    {
        byte[] name = SliceField(header, 0, 100);
        byte[] prefix = SliceField(header, 345, 155);
        if (prefix.Length == 0)
        {
            return name;
        }
        byte[] combined = new byte[prefix.Length + 1 + name.Length];
        Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
        combined[prefix.Length] = (byte)'/';
        Buffer.BlockCopy(name, 0, combined, prefix.Length + 1, name.Length);
        return combined;
    }

    private static byte[] SliceField(byte[] source, int offset, int length)
    {
        int end = offset;
        int limit = offset + length;
        while (end < limit && source[end] != 0)
        {
            end++;
        }
        byte[] result = new byte[end - offset];
        Buffer.BlockCopy(source, offset, result, 0, result.Length);
        return result;
    }

    private static byte[] TrimTrailingNulls(byte[] data)
    {
        int end = data.Length;
        while (end > 0 && data[end - 1] == 0)
        {
            end--;
        }
        if (end == data.Length)
        {
            return data;
        }
        byte[] result = new byte[end];
        Buffer.BlockCopy(data, 0, result, 0, end);
        return result;
    }

    private static bool IsChecksumValid(byte[] header)
    {
        long expected = ParseNumber(header, 148, 8);
        long unsignedSum = 0;
        long signedSum = 0;
        for (int i = 0; i < BlockSize; i++)
        {
            byte value = (i >= 148 && i < 156) ? (byte)0x20 : header[i];
            unsignedSum += value;
            signedSum += (value >= 128) ? (value - 256) : value;
        }
        return unsignedSum == expected || signedSum == expected;
    }

    /// <summary>解析归档头数值字段：支持标准八进制与 GNU base-256（大文件）两种编码。</summary>
    private static long ParseNumber(byte[] header, int offset, int length)
    {
        byte first = header[offset];
        if ((first & 0x80) != 0)
        {
            // GNU base-256：0x80 为正、0xFF 为负（二补码），大端存储
            long value = (first == 0xFF) ? -1 : (first & 0x7F);
            for (int i = 1; i < length; i++)
            {
                value = (value << 8) | (header[offset + i] & 0xFFL);
            }
            return value;
        }
        long result = 0;
        bool started = false;
        for (int i = 0; i < length; i++)
        {
            byte b = header[offset + i];
            if (b == 0 || b == 0x20)
            {
                if (started)
                {
                    break;
                }
                continue;
            }
            if (b < (byte)'0' || b > (byte)'7')
            {
                return result;
            }
            started = true;
            result = (result << 3) + (b - (byte)'0');
        }
        return result;
    }

    /// <summary>读取辅助块数据：返回有效数据，同时输出补齐到 512 块边界的完整字节（供改写原样回写）。</summary>
    private byte[] ReadDataBytes(Stream input, long size, out byte[] paddedData)
    {
        byte[] data = new byte[size];
        int total = 0;
        while (total < size)
        {
            int read = input.Read(data, total, (int)(size - total));
            if (read <= 0)
            {
                throw new InvalidDataException("tar 归档数据截断，文件不完整");
            }
            total += read;
        }
        long padding = (BlockSize - (size % BlockSize)) % BlockSize;
        if (padding > 0)
        {
            SkipBytes(input, padding);
            paddedData = new byte[size + padding];
            Buffer.BlockCopy(data, 0, paddedData, 0, (int)size);
        }
        else
        {
            paddedData = data;
        }
        return data;
    }

    private static void SkipData(Stream input, long size)
    {
        long padded = size + (BlockSize - (size % BlockSize)) % BlockSize;
        if (input.CanSeek)
        {
            input.Seek(padded, SeekOrigin.Current);
            return;
        }
        SkipBytes(input, padded);
    }

    /// <summary>仅越过条目数据之后的 512 对齐填充（数据字节已被调用方读走时用）。</summary>
    private static void SkipPadding(Stream input, long size)
    {
        long padding = (BlockSize - (size % BlockSize)) % BlockSize;
        if (padding > 0)
        {
            SkipBytes(input, padding);
        }
    }

    private static void SkipBytes(Stream input, long count)
    {
        byte[] buffer = new byte[65536];
        while (count > 0)
        {
            int want = (int)Math.Min(buffer.Length, count);
            int read = input.Read(buffer, 0, want);
            if (read <= 0)
            {
                throw new InvalidDataException("tar 归档数据截断，文件不完整");
            }
            count -= read;
        }
    }

    private static void CopyData(Stream input, Stream output, long size)
    {
        if (size > 0)
        {
            CopyExactly(input, output, size);
        }
        long padding = (BlockSize - (size % BlockSize)) % BlockSize;
        if (padding > 0)
        {
            // 输入侧的 512 对齐填充必须一并越过（否则后续条目头错位），
            // 且原样复制其字节到输出，保持归档字节级保真。
            byte[] padBuffer = new byte[padding];
            long padRemaining = padding;
            int padOffset = 0;
            while (padRemaining > 0)
            {
                int padRead = input.Read(padBuffer, padOffset, (int)padRemaining);
                if (padRead <= 0)
                {
                    throw new InvalidDataException("tar 归档数据截断，文件不完整");
                }
                padOffset += padRead;
                padRemaining -= padRead;
            }
            output.Write(padBuffer, 0, (int)padding);
        }
    }

    private static void CopyExactly(Stream input, Stream output, long size)
    {
        byte[] buffer = new byte[65536];
        long remaining = size;
        while (remaining > 0)
        {
            int want = (int)Math.Min(buffer.Length, remaining);
            int read = input.Read(buffer, 0, want);
            if (read <= 0)
            {
                throw new InvalidDataException("归档条目数据长度不足，数据可能已损坏");
            }
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    // ================================================================
    //  归档头生成
    // ================================================================

    private static void FillHeaderBase(byte[] header, byte[] nameField, int mode, long uid, long gid,
        long size, long mtime, byte typeFlag)
    {
        int nameCopy = Math.Min(100, nameField.Length);
        Buffer.BlockCopy(nameField, 0, header, 0, nameCopy);
        WriteOctal(header, 100, 8, mode);
        WriteOctal(header, 108, 8, uid);
        WriteOctal(header, 116, 8, gid);
        WriteNumber(header, 124, 12, size);
        WriteNumber(header, 136, 12, mtime);
        // 校验和占位：148..155 全空格
        for (int i = 148; i < 156; i++)
        {
            header[i] = 0x20;
        }
        header[156] = typeFlag;
        // POSIX ustar 魔数与版本
        header[257] = (byte)'u';
        header[258] = (byte)'s';
        header[259] = (byte)'t';
        header[260] = (byte)'a';
        header[261] = (byte)'r';
        header[262] = 0;
        header[263] = (byte)'0';
        header[264] = (byte)'0';

        long checksum = 0;
        for (int i = 0; i < BlockSize; i++)
        {
            checksum += header[i];
        }
        // 标准格式：6 位八进制 + NUL + 空格（6 位上限 262145，足够容纳 512×255 的最大校验和）
        WriteOctal(header, 148, 7, checksum);
        header[155] = (byte)' ';
    }

    /// <summary>八进制字段：固定宽度、前导零、NUL 结尾（长度含结尾符）。</summary>
    private static void WriteOctal(byte[] header, int offset, int length, long value)
    {
        string octal = Convert.ToString(value, 8);
        int digits = length - 1;
        if (octal.Length > digits)
        {
            octal = octal.Substring(octal.Length - digits);
        }
        octal = octal.PadLeft(digits, '0');
        for (int i = 0; i < digits; i++)
        {
            header[offset + i] = (byte)octal[i];
        }
        header[offset + digits] = 0;
    }

    /// <summary>数值字段：八进制（≤ 8 GB）或 GNU base-256（超大值），共 length 字节。</summary>
    private static void WriteNumber(byte[] header, int offset, int length, long value)
    {
        if (value >= 0 && value <= OctalMax)
        {
            WriteOctal(header, offset, length, value);
            return;
        }
        // base-256：首字节 0x80（正）/ 0xFF（负），其余大端
        header[offset] = value < 0 ? (byte)0xFF : (byte)0x80;
        long v = value;
        for (int i = length - 1; i >= 1; i--)
        {
            header[offset + i] = (byte)(v & 0xFF);
            v >>= 8;
        }
    }

    private static void WriteBlock(Stream output, byte[] block)
    {
        output.Write(block, 0, BlockSize);
    }

    private static void WritePadded(Stream output, byte[] data, long size)
    {
        output.Write(data, 0, (int)size);
        long padding = (BlockSize - (size % BlockSize)) % BlockSize;
        if (padding > 0)
        {
            output.Write(new byte[padding], 0, (int)padding);
        }
    }

    /// <summary>把文件名尽量拆到 ustar 的 prefix(155) / name(100) 两字段；无法拆分返回 false。</summary>
    private static bool TrySplitUstarName(byte[] nameBytes, out byte[] nameField, out byte[] prefixField)
    {
        prefixField = new byte[155];
        if (nameBytes.Length <= LongNameLimit)
        {
            nameField = nameBytes;
            return true;
        }
        for (int i = nameBytes.Length - 2; i >= 0; i--)
        {
            if (nameBytes[i] != (byte)'/')
            {
                continue;
            }
            int prefixLength = i;
            int suffixLength = nameBytes.Length - i - 1;
            if (prefixLength == 0 || prefixLength > PrefixLimit || suffixLength > LongNameLimit)
            {
                continue;
            }
            if (suffixLength == 0)
            {
                continue;
            }
            prefixField = new byte[155];
            Buffer.BlockCopy(nameBytes, 0, prefixField, 0, prefixLength);
            nameField = new byte[LongNameLimit];
            Buffer.BlockCopy(nameBytes, i + 1, nameField, 0, suffixLength);
            return true;
        }
        nameField = new byte[LongNameLimit];
        return false;
    }

    /// <summary>构造 PAX "path" 扩展记录（值为原始文件名字节，长度自洽）。</summary>
    private static byte[] BuildPaxPathRecord(byte[] nameBytes)
    {
        byte[] suffix = Encoding.ASCII.GetBytes(" path=");
        int totalLength = 0;
        for (int digits = 1; digits <= 10; digits++)
        {
            totalLength = digits + suffix.Length + nameBytes.Length + 1;
            if (totalLength.ToString().Length == digits)
            {
                break;
            }
        }
        byte[] prefix = Encoding.ASCII.GetBytes(totalLength.ToString() + " ");
        byte[] record = new byte[prefix.Length + suffix.Length + nameBytes.Length + 1];
        int offset = 0;
        Buffer.BlockCopy(prefix, 0, record, offset, prefix.Length);
        offset += prefix.Length;
        Buffer.BlockCopy(suffix, 0, record, offset, suffix.Length);
        offset += suffix.Length;
        Buffer.BlockCopy(nameBytes, 0, record, offset, nameBytes.Length);
        offset += nameBytes.Length;
        record[offset] = (byte)'\n';
        return record;
    }

    /// <summary>PAX 扩展头解析结果（目前提取 path / size / mtime 三个键）。</summary>
    private sealed class PaxRecords
    {
        public byte[]? Path;
        public long? Size;
        public long? ModifiedUnixSeconds;

        public void Apply(out byte[]? path, out long? size, out long? mtime)
        {
            path = this.Path;
            size = this.Size;
            mtime = this.ModifiedUnixSeconds;
        }
    }

    private static PaxRecords ParsePaxRecords(byte[] data)
    {
        PaxRecords records = new PaxRecords();
        int offset = 0;
        while (offset < data.Length)
        {
            // 记录格式："<总长度> 键=值\n"
            int digits = 0;
            long recordLength = 0;
            while (offset + digits < data.Length && data[offset + digits] >= (byte)'0' && data[offset + digits] <= (byte)'9')
            {
                recordLength = recordLength * 10 + (data[offset + digits] - (byte)'0');
                digits++;
            }
            if (digits == 0 || recordLength <= 0)
            {
                break;
            }
            int recordStart = offset;
            int recordEnd = offset + (int)recordLength;
            if (recordEnd > data.Length)
            {
                break;
            }
            int eq = recordStart + digits;
            while (eq < recordEnd && data[eq] != (byte)'=')
            {
                eq++;
            }
            if (eq < recordEnd)
            {
                string key = Encoding.ASCII.GetString(data, recordStart + digits, eq - (recordStart + digits));
                int valueEnd = recordEnd - 1; // 去掉结尾换行
                if (valueEnd > eq + 1)
                {
                    byte[] value = new byte[valueEnd - (eq + 1)];
                    Buffer.BlockCopy(data, eq + 1, value, 0, value.Length);
                    if (key == "path")
                    {
                        records.Path = value;
                    }
                    else if (key == "size")
                    {
                        long parsed;
                        if (long.TryParse(Encoding.ASCII.GetString(value), out parsed))
                        {
                            records.Size = parsed;
                        }
                    }
                    else if (key == "mtime")
                    {
                        double parsedSeconds;
                        if (double.TryParse(Encoding.ASCII.GetString(value),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out parsedSeconds))
                        {
                            records.ModifiedUnixSeconds = (long)parsedSeconds;
                        }
                    }
                }
            }
            offset = recordEnd;
        }
        return records;
    }

    // ================================================================
    //  路径安全与工具
    // ================================================================

    private static bool IsSafeNameBytes(byte[] name)
    {
        if (name.Length == 0 || name[0] == (byte)'/')
        {
            return false;
        }
        int segmentStart = 0;
        for (int i = 0; i <= name.Length; i++)
        {
            bool atEnd = i == name.Length;
            if (atEnd || name[i] == (byte)'/')
            {
                int length = i - segmentStart;
                if (length == 2 && name[segmentStart] == (byte)'.' && name[segmentStart + 1] == (byte)'.')
                {
                    return false;
                }
                if (segmentStart == 0 && length >= 2 && name[1] == (byte)':')
                {
                    return false; // 盘符（如 C:）
                }
                if (length == 0 && segmentStart == 0)
                {
                    return false; // "./" 起始之外的空首段（"//"）不允许
                }
                segmentStart = i + 1;
            }
            if (name[i] == 0)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSafeNameString(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] == '/' || name[0] == '\\')
        {
            return false;
        }
        if (name.IndexOf('\0') >= 0)
        {
            return false;
        }
        string[] segments = name.Split('/', '\\');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == "..")
            {
                return false;
            }
        }
        if (name.Length >= 2 && name[1] == ':')
        {
            return false;
        }
        return true;
    }

    private static byte[] JoinPath(byte[] prefix, byte[] name)
    {
        // 规整 name 中的 "." 段与多余分隔符，保留原始字节
        List<byte[]> segments = new List<byte[]>();
        int start = 0;
        for (int i = 0; i <= name.Length; i++)
        {
            if (i == name.Length || name[i] == (byte)'/')
            {
                int length = i - start;
                if (length == 1 && name[start] == (byte)'.')
                {
                    start = i + 1;
                    continue;
                }
                if (length > 0)
                {
                    byte[] segment = new byte[length];
                    Buffer.BlockCopy(name, start, segment, 0, length);
                    segments.Add(segment);
                }
                start = i + 1;
            }
        }
        int total = prefix.Length;
        for (int i = 0; i < segments.Count; i++)
        {
            total += 1 + segments[i].Length;
        }
        byte[] result = new byte[total];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        int offset = prefix.Length;
        for (int i = 0; i < segments.Count; i++)
        {
            result[offset] = (byte)'/';
            offset++;
            Buffer.BlockCopy(segments[i], 0, result, offset, segments[i].Length);
            offset += segments[i].Length;
        }
        return result;
    }

    private static byte[] ParentPath(byte[] path)
    {
        for (int i = path.Length - 1; i >= 0; i--)
        {
            if (path[i] == (byte)'/')
            {
                byte[] parent = new byte[i];
                Buffer.BlockCopy(path, 0, parent, 0, i);
                return parent;
            }
        }
        return Array.Empty<byte>();
    }

    private static HashSet<string> ToNameSet(IReadOnlyList<string>? names)
    {
        HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
        if (names == null)
        {
            return set;
        }
        for (int i = 0; i < names.Count; i++)
        {
            string? name = names[i];
            if (!string.IsNullOrEmpty(name))
            {
                set.Add(name.Replace('\\', '/').TrimEnd('/'));
            }
        }
        return set;
    }

    private static string TrimTrailingSlash(string name)
    {
        return name.TrimEnd('/');
    }

    /// <summary>
    /// 判断 512 字节是否像合法 tar 归档头（校验和通过 + 类型标志合法 + 非空名字）。
    /// 供 .gz / .bz2 / .xz / .zst 内容嗅探：压缩流内部可能是 tar（错误扩展名）也可能是单个文件。
    /// </summary>
    public static bool LooksLikeTarHeader(byte[]? block)
    {
        if (block == null || block.Length < BlockSize)
        {
            return false;
        }
        if (block[0] == 0)
        {
            return false;
        }
        if (!IsChecksumValid(block))
        {
            return false;
        }
        char type = (char)block[156];
        return "01234567gGxXKkL".IndexOf(type) >= 0;
    }

    private static DateTime UnixToUtc(long seconds)
    {
        if (seconds <= 0)
        {
            return DateTime.UnixEpoch;
        }
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UnixEpoch;
        }
    }

    private static long UtcToUnix(DateTime time)
    {
        DateTime utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
        long seconds = (long)(utc - DateTime.UnixEpoch).TotalSeconds;
        return seconds < 0 ? 0 : seconds;
    }
}
