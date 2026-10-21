using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 磁盘用量增量快照：记录每个目录上次扫描时的修改时间与统计大小。
/// 下次扫描时若目录修改时间与上次一致且记录未过期，则整棵子树直接复用上次大小，
/// 只重扫变化过的部分；记录以「TSV 文本 + GZip」持久化，程序重启后继续生效。
/// 线程安全：并行扫描线程同时读写。
/// </summary>
public class DiskUsageSnapshot
{
    private readonly ConcurrentDictionary<string, Entry> entries;
    private long reusedDirectoryCount;
    private long scannedDirectoryCount;

    /// <summary>创建快照：Windows 路径大小写不敏感，其他平台区分大小写。</summary>
    public DiskUsageSnapshot()
    {
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        this.entries = new ConcurrentDictionary<string, Entry>(comparer);
    }

    /// <summary>记录的有效期：超过该时间未更新的记录视为过期，下次扫描重新统计。</summary>
    public TimeSpan RecordTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>快照中的目录记录条数。</summary>
    public int Count
    {
        get { return this.entries.Count; }
    }

    /// <summary>累计复用（整棵子树未重扫）的目录数。</summary>
    public long ReusedDirectoryCount
    {
        get { return Interlocked.Read(ref this.reusedDirectoryCount); }
    }

    /// <summary>累计实际重扫并回写的目录数。</summary>
    public long ScannedDirectoryCount
    {
        get { return Interlocked.Read(ref this.scannedDirectoryCount); }
    }

    /// <summary>
    /// 尝试复用上次统计结果：修改时间一致、记录完整且未过期才复用。
    /// 复用成功计入复用统计，失败由调用方走正常重扫。
    /// </summary>
    /// <param name="directoryPath">目录完整路径。</param>
    /// <param name="lastWriteTicks">该目录当前的最后修改时间（UTC ticks）。</param>
    /// <param name="sizeBytes">复用得到的上次统计大小。</param>
    /// <returns>是否复用成功。</returns>
    public bool TryReuse(string directoryPath, long lastWriteTicks, out long sizeBytes)
    {
        sizeBytes = 0;
        Entry? entry;
        if (!this.entries.TryGetValue(directoryPath, out entry))
        {
            return false;
        }
        if (!this.IsFresh(entry, lastWriteTicks))
        {
            return false;
        }
        sizeBytes = entry.SizeBytes;
        Interlocked.Increment(ref this.reusedDirectoryCount);
        return true;
    }

    /// <summary>取某目录上次记录的修改时间；没有记录返回 false。</summary>
    /// <param name="directoryPath">目录完整路径。</param>
    /// <param name="lastWriteTicks">上次记录的修改时间（UTC ticks）。</param>
    /// <returns>是否存在记录。</returns>
    public bool TryGetRecordedMtime(string directoryPath, out long lastWriteTicks)
    {
        Entry? entry;
        if (this.entries.TryGetValue(directoryPath, out entry))
        {
            lastWriteTicks = entry.LastWriteTicks;
            return true;
        }
        lastWriteTicks = 0;
        return false;
    }

    /// <summary>记录一个目录的统计结果（含完整性与记录时间）。</summary>
    /// <param name="directoryPath">目录完整路径。</param>
    /// <param name="lastWriteTicks">扫描时该目录的最后修改时间（UTC ticks）。</param>
    /// <param name="sizeBytes">统计得到的占用大小（字节）。</param>
    /// <param name="complete">本次统计是否完整（枚举失败或子目录不完整时为否，下次必须重试）。</param>
    public void Record(string directoryPath, long lastWriteTicks, long sizeBytes, bool complete)
    {
        Entry entry = new Entry();
        entry.LastWriteTicks = lastWriteTicks;
        entry.SizeBytes = sizeBytes;
        entry.Complete = complete;
        entry.ScannedAtTicks = DateTime.UtcNow.Ticks;
        this.entries[directoryPath] = entry;
        Interlocked.Increment(ref this.scannedDirectoryCount);
    }

    /// <summary>判断一条记录能否复用：修改时间一致、记录完整且在有效期内。</summary>
    private bool IsFresh(Entry entry, long lastWriteTicks)
    {
        if (!entry.Complete)
        {
            return false;
        }
        if (entry.LastWriteTicks != lastWriteTicks)
        {
            return false;
        }
        if (DateTime.UtcNow.Ticks - entry.ScannedAtTicks > this.RecordTtl.Ticks)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// 从持久化文件加载记录：文件不存在时视为全新快照。
    /// 行格式为「修改时间、大小、完整性、记录时间、路径」（路径放最后一列，可含制表符以外的任意字符）。
    /// </summary>
    /// <param name="filePath">快照文件路径（TSV + GZip）。</param>
    public void Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }
        using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress);
        using StreamReader reader = new StreamReader(gzip, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
            {
                continue;
            }
            string[] parts = line.Split('\t', 5);
            if (parts.Length < 5)
            {
                continue;
            }
            long lastWriteTicks;
            long sizeBytes;
            long scannedAtTicks;
            if (!long.TryParse(parts[0], out lastWriteTicks)
                || !long.TryParse(parts[1], out sizeBytes)
                || !long.TryParse(parts[3], out scannedAtTicks))
            {
                continue;
            }
            Entry entry = new Entry();
            entry.LastWriteTicks = lastWriteTicks;
            entry.SizeBytes = sizeBytes;
            entry.Complete = parts[2] == "1";
            entry.ScannedAtTicks = scannedAtTicks;
            this.entries[parts[4]] = entry;
        }
    }

    /// <summary>把全部记录持久化到文件（TSV + GZip）；自动创建所在目录。</summary>
    /// <param name="filePath">快照文件路径。</param>
    public void Save(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        using FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using GZipStream gzip = new GZipStream(stream, CompressionLevel.Fastest);
        using StreamWriter writer = new StreamWriter(gzip, new UTF8Encoding(false));
        foreach (KeyValuePair<string, Entry> pair in this.entries)
        {
            writer.Write(pair.Value.LastWriteTicks);
            writer.Write('\t');
            writer.Write(pair.Value.SizeBytes);
            writer.Write('\t');
            writer.Write(pair.Value.Complete ? "1" : "0");
            writer.Write('\t');
            writer.Write(pair.Value.ScannedAtTicks);
            writer.Write('\t');
            writer.Write(pair.Key);
            writer.Write('\n');
        }
    }

    /// <summary>一条目录记录：上次扫描时的修改时间、大小、完整性与记录时刻。</summary>
    private sealed class Entry
    {
        public long LastWriteTicks { get; set; }

        public long SizeBytes { get; set; }

        public bool Complete { get; set; }

        public long ScannedAtTicks { get; set; }
    }
}
