using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>
/// 压缩包服务测试：格式探测（扩展名 + 魔数 + tar 嗅探）、创建 / 列出 / 解压 /
/// 增删改链路，以及 tar 原始字节文件名往返（LC_ALL=C 语义）。
/// </summary>
public class ArchiveServiceTests : IDisposable
{
    private readonly string workDirectory;
    private readonly string sourceDirectory;
    private readonly ArchiveService service;

    public ArchiveServiceTests()
    {
        this.workDirectory = Path.Combine(Path.GetTempPath(), "archive-tests-" + Guid.NewGuid().ToString("N"));
        this.sourceDirectory = Path.Combine(this.workDirectory, "源");
        Directory.CreateDirectory(Path.Combine(this.sourceDirectory, "子目录"));
        File.WriteAllText(Path.Combine(this.sourceDirectory, "你好.txt"), "中文内容abc");
        File.WriteAllBytes(Path.Combine(this.sourceDirectory, "子目录", "内部.dat"), CreatePatternBytes(5000));
        IArchivePathAccess access = new DefaultArchivePathAccess();
        this.service = new ArchiveService(access);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.workDirectory, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static byte[] CreatePatternBytes(int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }
        return bytes;
    }

    private string TempFile(string fileName)
    {
        return Path.Combine(this.workDirectory, fileName);
    }

    // ================================================================
    //  格式探测
    // ================================================================

    /// <summary>已知压缩包扩展名拦截，普通文本文件不拦截。</summary>
    [Fact]
    public void IsArchivePath_KnownExtensions_MatchesByExtensionOnly()
    {
        Assert.True(this.service.IsArchivePath(this.TempFile("a.zip")));
        Assert.True(this.service.IsArchivePath(this.TempFile("a.7z")));
        Assert.True(this.service.IsArchivePath(this.TempFile("a.tar.gz")));
        Assert.False(this.service.IsArchivePath(this.TempFile("a.txt")));
        Assert.False(this.service.IsArchivePath(this.TempFile("a.iso")));
    }

    /// <summary>扩展名是 zip 但内容不是 zip（魔数不符）时 Probe 返回 null。</summary>
    [Fact]
    public void Probe_FakeExtensionWithWrongMagic_ReturnsNull()
    {
        string fakePath = this.TempFile("伪装.zip");
        File.WriteAllText(fakePath, "这不是 zip 文件");

        Assert.Null(this.service.Probe(fakePath));
    }

    /// <summary>文件不存在或扩展名未知时 Probe 返回 null。</summary>
    [Fact]
    public void Probe_MissingOrUnknownExtension_ReturnsNull()
    {
        Assert.Null(this.service.Probe(this.TempFile("不存在.zip")));
        Assert.Null(this.service.Probe(this.TempFile("unknown.xyz")));
    }

    /// <summary>伪造 RAR 文件头（魔数正确）被识别为只读格式：可读不可改。</summary>
    [Fact]
    public void Probe_RarMagic_ReadOnlyFormat()
    {
        string rarPath = this.TempFile("fake.rar");
        using (FileStream stream = File.Create(rarPath))
        {
            stream.Write(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 });
        }

        ArchiveFormatInfo? info = this.service.Probe(rarPath);

        Assert.NotNull(info);
        Assert.Equal(ArchiveFormat.Rar, info!.Format);
        Assert.True(info.CanRead);
        Assert.False(info.CanModify);
    }

    // ================================================================
    //  ZIP
    // ================================================================

    /// <summary>ZIP 创建 → 列出 → 解压 → 重命名 → 删目录连带 → 追加 → 包内建目录 全链路。</summary>
    [Fact]
    public void Zip_FullLifecycle_Works()
    {
        string zipPath = this.TempFile("a.zip");
        this.service.Create(zipPath, new List<string> { this.sourceDirectory },
            new ArchiveCreateOptions { Format = ArchiveFormat.Zip, CompressionLevel = 6 });
        Assert.True(File.Exists(zipPath));

        IReadOnlyList<ArchiveEntry> entries = this.service.ListEntries(zipPath);
        Assert.Equal(4, entries.Count);

        string target = this.TempFile("zip-out");
        this.service.Extract(zipPath, new ArchiveExtractOptions { TargetDirectory = target });
        Assert.Equal("中文内容abc", File.ReadAllText(Path.Combine(target, "源", "你好.txt")));

        this.service.RenameEntry(zipPath, "源/你好.txt", "源/改名.txt");
        IReadOnlyList<ArchiveEntry> afterRename = this.service.ListEntries(zipPath);
        Assert.Contains(afterRename, e => e.Name == "源/改名.txt");
        Assert.DoesNotContain(afterRename, e => e.Name == "源/你好.txt");

        this.service.RemoveEntries(zipPath, new List<string> { "源/子目录" });
        IReadOnlyList<ArchiveEntry> afterRemove = this.service.ListEntries(zipPath);
        Assert.DoesNotContain(afterRemove, e => e.Name.StartsWith("源/子目录"));

        this.service.AddPaths(zipPath, new List<string> { Path.Combine(this.sourceDirectory, "你好.txt") });
        IReadOnlyList<ArchiveEntry> afterAdd = this.service.ListEntries(zipPath);
        Assert.Contains(afterAdd, e => e.Name.Contains("你好.txt"));

        this.service.AddDirectory(zipPath, "源/新文件夹");
        IReadOnlyList<ArchiveEntry> afterDir = this.service.ListEntries(zipPath);
        Assert.Contains(afterDir, e => e.Name == "源/新文件夹" && e.IsDirectory);
    }

    /// <summary>
    /// ZIP 非 ASCII 条目名必须置 UTF-8 标志位（bit 11 / 0x800），
    /// 否则第三方读取器（资源管理器、.NET Framework ZipArchive 等）会按 ANSI 解码出乱码文件名；
    /// 创建与全量重写（改名）两条写入路径都必须保持该标志。
    /// </summary>
    [Fact]
    public void Zip_ChineseEntryNames_SetUtf8FlagForInterop()
    {
        string zipPath = this.TempFile("中文包.zip");
        this.service.Create(zipPath, new List<string> { this.sourceDirectory },
            new ArchiveCreateOptions { Format = ArchiveFormat.Zip, CompressionLevel = 6 });

        Assert.True(HasUtf8NameFlag(zipPath), "创建后的 ZIP 条目名未置 UTF-8 标志位（bit 11）");

        this.service.RenameEntry(zipPath, "源/你好.txt", "源/改名.txt");
        Assert.True(HasUtf8NameFlag(zipPath), "全量重写（改名）后的 ZIP 条目名丢失了 UTF-8 标志位（bit 11）");

        using (System.IO.Compression.ZipArchive rewritten = System.IO.Compression.ZipFile.OpenRead(zipPath))
        {
            Assert.Contains(rewritten.Entries, e => e.FullName == "源/改名.txt");
        }
    }

    /// <summary>解析 ZIP 本地文件头：存在至少一个非 ASCII 名字条目且其 general purpose flags 置了 bit 11。</summary>
    private static bool HasUtf8NameFlag(string zipPath)
    {
        byte[] bytes = File.ReadAllBytes(zipPath);
        int position = 0;
        bool sawNonAsciiName = false;
        while (position + 30 <= bytes.Length)
        {
            uint signature = BitConverter.ToUInt32(bytes, position);
            if (signature != 0x04034b50)
            {
                break;
            }
            ushort flags = BitConverter.ToUInt16(bytes, position + 6);
            ushort nameLength = BitConverter.ToUInt16(bytes, position + 26);
            ushort extraLength = BitConverter.ToUInt16(bytes, position + 28);
            uint compressedSize = BitConverter.ToUInt32(bytes, position + 18);
            if (nameLength > 0)
            {
                string name = Encoding.UTF8.GetString(bytes, position + 30, nameLength);
                if (name.Any(c => c > 127))
                {
                    sawNonAsciiName = true;
                    if ((flags & 0x800) == 0)
                    {
                        return false;
                    }
                }
            }
            position = position + 30 + nameLength + extraLength + (int)compressedSize;
        }
        return sawNonAsciiName;
    }

    // ================================================================
    //  tar（原始字节保真）
    // ================================================================

    /// <summary>tar 创建 → 列出 → 解压 → 重命名 → 删目录连带 → 追加后再整体解压。</summary>
    [Fact]
    public void Tar_FullLifecycle_Works()
    {
        string tarPath = this.TempFile("a.tar");
        this.service.Create(tarPath, new List<string> { this.sourceDirectory },
            new ArchiveCreateOptions { Format = ArchiveFormat.Tar });
        IReadOnlyList<ArchiveEntry> entries = this.service.ListEntries(tarPath);
        Assert.Equal(4, entries.Count);

        string target = this.TempFile("tar-out");
        this.service.Extract(tarPath, new ArchiveExtractOptions { TargetDirectory = target });
        Assert.Equal("中文内容abc", File.ReadAllText(Path.Combine(target, "源", "你好.txt")));
        Assert.Equal(5000, new FileInfo(Path.Combine(target, "源", "子目录", "内部.dat")).Length);

        this.service.RenameEntry(tarPath, "源/你好.txt", "源/改名.txt");
        IReadOnlyList<ArchiveEntry> afterRename = this.service.ListEntries(tarPath);
        Assert.Contains(afterRename, e => e.Name == "源/改名.txt");
        Assert.DoesNotContain(afterRename, e => e.Name == "源/你好.txt");

        this.service.RemoveEntries(tarPath, new List<string> { "源/子目录" });
        IReadOnlyList<ArchiveEntry> afterRemove = this.service.ListEntries(tarPath);
        Assert.DoesNotContain(afterRemove, e => e.Name.StartsWith("源/子目录"));

        // 追加一条非 512 对齐大小的文件后，整个包仍能正常解析（改写流对齐回归）
        this.service.AddPaths(tarPath, new List<string> { Path.Combine(this.sourceDirectory, "你好.txt") });
        string secondTarget = this.TempFile("tar-out2");
        this.service.Extract(tarPath, new ArchiveExtractOptions { TargetDirectory = secondTarget });
        Assert.True(Directory.Exists(Path.Combine(secondTarget, "源")));
    }

    /// <summary>tar 条目名字节按原始序列往返：打包与解包两侧均不转码（LC_ALL=C 语义）。</summary>
    [Fact]
    public void Tar_RawNameBytes_RoundTripWithoutTranscoding()
    {
        // 构造一个 GBK 编码文件名的 tar：名字字节为 GBK 序列，非合法 UTF-8
        string gbkName = "中文测试.txt";
        Encoding gbk = ArchiveNameCodec.Gbk ?? Encoding.GetEncoding(936);
        byte[] nameBytes = gbk.GetBytes(gbkName);
        byte[] content = new byte[] { 1, 2, 3, 4, 5 };

        string tarPath = this.TempFile("gbk.tar");
        using (FileStream output = File.Create(tarPath))
        {
            WriteTarWithRawName(output, nameBytes, content);
        }

        // 列出：RawName 必须与写入字节完全一致
        IReadOnlyList<ArchiveEntry> entries = this.service.ListEntries(tarPath);
        Assert.Single(entries);
        Assert.Equal(nameBytes, entries[0].RawName);
        // 智能解码应还原出可读字符串（GBK 路径）
        Assert.Equal(gbkName, entries[0].Name);

        // 解压后磁盘上落盘的文件名字符串应与解码名一致
        string target = this.TempFile("gbk-out");
        this.service.Extract(tarPath, new ArchiveExtractOptions { TargetDirectory = target });
        Assert.True(File.Exists(Path.Combine(target, gbkName)));
    }

    /// <summary>手工写出一个含原始字节文件名的最小 tar（ustar 头 + 数据 + 零块收尾）。</summary>
    private static void WriteTarWithRawName(Stream output, byte[] nameBytes, byte[] content)
    {
        byte[] header = new byte[512];
        int nameCopy = Math.Min(nameBytes.Length, 100);
        Buffer.BlockCopy(nameBytes, 0, header, 0, nameCopy);
        WriteOctal(header, 100, 8, Convert.ToInt32("644", 8)); // 权限 0644
        WriteOctal(header, 108, 8, 0);
        WriteOctal(header, 116, 8, 0);
        WriteOctal(header, 124, 12, content.Length);
        WriteOctal(header, 136, 12, 1767252245); // 2026-01-02 03:04:05 UTC
        header[148] = (byte)' ';
        header[149] = (byte)' ';
        header[150] = (byte)' ';
        header[151] = (byte)' ';
        header[152] = (byte)' ';
        header[153] = (byte)' ';
        header[154] = (byte)' ';
        header[155] = (byte)' ';
        header[156] = (byte)'0';
        // ustar 魔数
        header[257] = (byte)'u';
        header[258] = (byte)'s';
        header[259] = (byte)'t';
        header[260] = (byte)'a';
        header[261] = (byte)'r';
        header[262] = (byte)' ';

        // 计算校验和：先按全空格视为 0 求和
        int checksum = 0;
        for (int i = 0; i < 512; i++)
        {
            checksum += header[i];
        }
        WriteOctal(header, 148, 7, checksum);
        header[154] = 0;
        header[155] = (byte)' ';

        output.Write(header, 0, 512);
        output.Write(content, 0, content.Length);
        int padding = (512 - (content.Length % 512)) % 512;
        if (padding > 0)
        {
            output.Write(new byte[padding], 0, padding);
        }
        output.Write(new byte[1024], 0, 1024);
    }

    private static void WriteOctal(byte[] header, int offset, int width, long value)
    {
        string octal = Convert.ToString(value, 8);
        // 右对齐，左侧补 '0'，末位宽度外留 NUL
        int digits = width - 1;
        octal = octal.PadLeft(digits, '0');
        for (int i = 0; i < digits && i < octal.Length; i++)
        {
            header[offset + i] = (byte)octal[i];
        }
        if (offset + digits < header.Length)
        {
            header[offset + digits] = 0;
        }
    }

    // ================================================================
    //  tar 外层压缩
    // ================================================================

    /// <summary>tar.gz 创建 → 列出 → 解压 → 追加改写。</summary>
    [Fact]
    public void TarGzip_CreateListExtractRewrite_Works()
    {
        string path = this.TempFile("a.tar.gz");
        this.service.Create(path, new List<string> { this.sourceDirectory },
            new ArchiveCreateOptions { Format = ArchiveFormat.Tar, Compression = ArchiveCompression.GZip });
        IReadOnlyList<ArchiveEntry> entries = this.service.ListEntries(path);
        Assert.Equal(4, entries.Count);

        string target = this.TempFile("tgz-out");
        this.service.Extract(path, new ArchiveExtractOptions { TargetDirectory = target });
        Assert.Equal("中文内容abc", File.ReadAllText(Path.Combine(target, "源", "你好.txt")));

        this.service.AddPaths(path, new List<string> { Path.Combine(this.sourceDirectory, "你好.txt") });
        IReadOnlyList<ArchiveEntry> afterAdd = this.service.ListEntries(path);
        Assert.True(afterAdd.Count > entries.Count);
    }

    // ================================================================
    //  单文件压缩流
    // ================================================================

    /// <summary>单文件 gz 创建 → 列出（内部名按压缩包文件名推导）→ 解压。</summary>
    [Fact]
    public void GzipSingleFile_CreateListExtract_Works()
    {
        string path = this.TempFile("你好.txt.gz");
        this.service.Create(path, new List<string> { Path.Combine(this.sourceDirectory, "你好.txt") },
            new ArchiveCreateOptions { Format = ArchiveFormat.GZip });

        IReadOnlyList<ArchiveEntry> entries = this.service.ListEntries(path);
        Assert.Single(entries);
        Assert.Equal("你好.txt", entries[0].Name);

        string target = this.TempFile("gz-out");
        Directory.CreateDirectory(target);
        this.service.Extract(path, new ArchiveExtractOptions { TargetDirectory = target });
        Assert.Equal("中文内容abc", File.ReadAllText(Path.Combine(target, "你好.txt")));
    }

    /// <summary>单文件 bz2 创建 → 解压往返。</summary>
    [Fact]
    public void Bzip2SingleFile_RoundTrip_Works()
    {
        string path = this.TempFile("你好.txt.bz2");
        this.service.Create(path, new List<string> { Path.Combine(this.sourceDirectory, "你好.txt") },
            new ArchiveCreateOptions { Format = ArchiveFormat.BZip2 });

        string target = this.TempFile("bz-out");
        Directory.CreateDirectory(target);
        this.service.Extract(path, new ArchiveExtractOptions { TargetDirectory = target });
        Assert.Equal("中文内容abc", File.ReadAllText(Path.Combine(target, "你好.txt")));
    }

    /// <summary>目录打成 .gz 自动内嵌 tar，Probe 嗅探为 tar + gz；单文件 gz 不误判。</summary>
    [Fact]
    public void GzipWithDirectory_SniffedAsTar_SingleFileNotMisdetected()
    {
        string dirGzPath = this.TempFile("目录.gz");
        this.service.Create(dirGzPath, new List<string> { this.sourceDirectory },
            new ArchiveCreateOptions { Format = ArchiveFormat.GZip });

        ArchiveFormatInfo? dirInfo = this.service.Probe(dirGzPath);
        Assert.NotNull(dirInfo);
        Assert.Equal(ArchiveFormat.Tar, dirInfo!.Format);
        Assert.Equal(ArchiveCompression.GZip, dirInfo.Compression);

        string target = this.TempFile("dirgz-out");
        this.service.Extract(dirGzPath, new ArchiveExtractOptions { TargetDirectory = target });
        Assert.Equal("中文内容abc", File.ReadAllText(Path.Combine(target, "源", "你好.txt")));

        string fileGzPath = this.TempFile("单个.gz");
        this.service.Create(fileGzPath, new List<string> { Path.Combine(this.sourceDirectory, "你好.txt") },
            new ArchiveCreateOptions { Format = ArchiveFormat.GZip });
        ArchiveFormatInfo? fileInfo = this.service.Probe(fileGzPath);
        Assert.NotNull(fileInfo);
        Assert.Equal(ArchiveFormat.GZip, fileInfo!.Format);
        Assert.False(fileInfo.CanModify);
    }

    // ================================================================
    //  7z
    // ================================================================

    /// <summary>7z 创建 → 列出 → 解压 → 重命名。</summary>
    [Fact]
    public void SevenZip_CreateListExtractRename_Works()
    {
        string path = this.TempFile("a.7z");
        this.service.Create(path, new List<string> { this.sourceDirectory },
            new ArchiveCreateOptions { Format = ArchiveFormat.SevenZip, CompressionLevel = 5 });
        IReadOnlyList<ArchiveEntry> entries = this.service.ListEntries(path);
        Assert.Equal(4, entries.Count);

        string target = this.TempFile("7z-out");
        this.service.Extract(path, new ArchiveExtractOptions { TargetDirectory = target });
        Assert.Equal("中文内容abc", File.ReadAllText(Path.Combine(target, "源", "你好.txt")));

        this.service.RenameEntry(path, "源/你好.txt", "源/改名.txt");
        IReadOnlyList<ArchiveEntry> afterRename = this.service.ListEntries(path);
        Assert.Contains(afterRename, e => e.Name == "源/改名.txt");
    }

    // ================================================================
    //  ArchivePathFilter
    // ================================================================

    /// <summary>条目过滤器：精确匹配、目录前缀连带与重命名连带。</summary>
    [Fact]
    public void ArchivePathFilter_ExactAndDirectoryPrefix_Matches()
    {
        HashSet<string>? filter = ArchivePathFilter.Build(new List<string> { "源/子目录/", @"\源\文件.txt" });
        Assert.NotNull(filter);
        Assert.True(ArchivePathFilter.Matches(filter, "源/子目录"));
        Assert.True(ArchivePathFilter.Matches(filter, "源/子目录/内部.dat"));
        Assert.True(ArchivePathFilter.Matches(filter, "源/文件.txt"));
        Assert.False(ArchivePathFilter.Matches(filter, "源/你好.txt"));
        // null 过滤器恒真
        Assert.True(ArchivePathFilter.Matches(null, "任意路径"));

        Dictionary<string, string> renameMap = ArchivePathFilter.BuildRenameMap(
            new List<string[]> { new string[] { "源/子目录", "源/新子目录" } });
        Assert.True(ArchivePathFilter.TryRename(renameMap, "源/子目录", out string renamedExact));
        Assert.Equal("源/新子目录", renamedExact);
        Assert.True(ArchivePathFilter.TryRename(renameMap, "源/子目录/内部.dat", out string renamedChild));
        Assert.Equal("源/新子目录/内部.dat", renamedChild);
        Assert.False(ArchivePathFilter.TryRename(renameMap, "源/你好.txt", out _));
    }
}
