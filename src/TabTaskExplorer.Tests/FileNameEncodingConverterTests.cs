using System;
using System.Collections.Generic;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>文件名批量编码转换测试（对照 convmv 语义）。</summary>
public class FileNameEncodingConverterTests
{
    private const int Utf8Page = 65001;
    private const int GbkPage = 936;
    private const int Latin1Page = 28591;
    private const int Koi8RPage = 20866;
    private const int Windows1251Page = 1251;

    [Fact]
    public void FixesMoJibakeFromUtf8BytesMisreadAsGbk()
    {
        // 正确名字的 UTF-8 字节被当成 GBK 解读后变成乱码，再按 GBK → UTF-8 应还原
        byte[] utf8Bytes = Encoding.UTF8.GetBytes("中文文件.txt");
        Encoding gbk = TextFileCodec.ResolveCodePage(GbkPage);
        Assert.NotNull(gbk);
        string mojibake = gbk.GetString(utf8Bytes);

        string result = FileNameEncodingConverter.ConvertName(mojibake, GbkPage, Utf8Page, out string reason);

        Assert.Equal("中文文件.txt", result);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void FailsWhenSourceEncodingCannotRepresentName()
    {
        // 中文字符超出 Latin-1 字符集：源编码无法完整表示
        string name = "文件.txt";

        string result = FileNameEncodingConverter.ConvertName(name, Latin1Page, Utf8Page, out string reason);

        Assert.Equal(name, result);
        Assert.Equal("原编码无法完整表示该名称中的字符", reason);
    }

    [Fact]
    public void FailsWhenTargetEncodingCannotParseBytes()
    {
        // “文” 的 UTF-8 尾字节 0x87 会与后续的“.”组成非法 GBK 配对（0x2E 小于 0x40 不是合法尾字节）：目标编码无法解析
        string name = "文.txt";

        string result = FileNameEncodingConverter.ConvertName(name, Utf8Page, GbkPage, out string reason);

        Assert.Equal(name, result);
        Assert.Equal("目标编码无法解析原编码字节", reason);
    }

    [Fact]
    public void SkipsWhenSameCodePageOrPureAscii()
    {
        string same = FileNameEncodingConverter.ConvertName("中文.txt", GbkPage, GbkPage, out string sameReason);
        Assert.Equal("中文.txt", same);
        Assert.Equal(string.Empty, sameReason);

        string ascii = FileNameEncodingConverter.ConvertName("readme.txt", GbkPage, Koi8RPage, out string asciiReason);
        Assert.Equal("readme.txt", ascii);
        Assert.Equal(string.Empty, asciiReason);
    }

    [Fact]
    public void RoundTripsCyrillicBetweenWindows1251AndKoi8R()
    {
        string name = "Отчёт.txt";

        string converted = FileNameEncodingConverter.ConvertName(name, Windows1251Page, Koi8RPage, out string forwardReason);
        Assert.Equal(string.Empty, forwardReason);
        Assert.NotEqual(name, converted);

        string restored = FileNameEncodingConverter.ConvertName(converted, Koi8RPage, Windows1251Page, out string backwardReason);
        Assert.Equal(string.Empty, backwardReason);
        Assert.Equal(name, restored);
    }

    [Fact]
    public void AllSupportedEncodingsResolve()
    {
        foreach (EncodingChoice choice in FileNameEncodingConverter.GetSupportedEncodings())
        {
            Assert.NotNull(TextFileCodec.ResolveCodePage(choice.CodePage));
            Assert.Equal(choice.Name, FileNameEncodingConverter.GetEncodingName(choice.CodePage));
        }
    }

    [Fact]
    public void BuildsRecursivePlanCoveringAllDescendants()
    {
        FakeProvider provider = new FakeProvider();
        List<string> paths = new List<string> { "root" };

        List<EncodeConvertEntry> recursivePlan = FileNameEncodingConverter.BuildPlan(
            provider, paths, true, Utf8Page, Utf8Page);
        Assert.Equal(6, recursivePlan.Count);

        List<EncodeConvertEntry> flatPlan = FileNameEncodingConverter.BuildPlan(
            provider, paths, false, Utf8Page, Utf8Page);
        Assert.Equal(1, flatPlan.Count);
        Assert.Equal("root", flatPlan[0].NewName);
    }

    /// <summary>内存假文件系统：root/a.txt、root/sub/b.txt、root/sub/sub2/c.txt。</summary>
    private sealed class FakeProvider : IFileSystemProvider
    {
        private readonly Dictionary<string, List<FileEntry>> directories =
            new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal)
            {
                { "root", new List<FileEntry> { Entry("a.txt", false), Entry("sub", true) } },
                { "root/sub", new List<FileEntry> { Entry("b.txt", false), Entry("sub2", true) } },
                { "root/sub/sub2", new List<FileEntry> { Entry("c.txt", false) } },
            };

        private static FileEntry Entry(string name, bool isDirectory)
        {
            FileEntry entry = new FileEntry();
            entry.Name = name;
            entry.FullPath = name;
            entry.IsDirectory = isDirectory;
            return entry;
        }

        public IReadOnlyList<FileEntry> ListDirectory(string directoryPath)
        {
            return this.ListDirectory(directoryPath, new DirectoryListingOptions());
        }

        public IReadOnlyList<FileEntry> ListDirectory(string directoryPath, DirectoryListingOptions options)
        {
            List<FileEntry> children;
            if (!this.directories.TryGetValue(directoryPath, out children))
            {
                throw new System.IO.DirectoryNotFoundException(directoryPath);
            }
            // 按父路径拼出完整路径，保证收集出的路径层级正确
            List<FileEntry> fixedChildren = new List<FileEntry>();
            foreach (FileEntry child in children)
            {
                FileEntry copy = new FileEntry();
                copy.Name = child.Name;
                copy.FullPath = directoryPath + "/" + child.Name;
                copy.IsDirectory = child.IsDirectory;
                fixedChildren.Add(copy);
            }
            return fixedChildren;
        }

        public void CreateDirectory(string directoryPath)
        {
            throw new NotImplementedException();
        }

        public void Rename(string path, string newName)
        {
            throw new NotImplementedException();
        }

        public void Delete(string path)
        {
            throw new NotImplementedException();
        }

        public void Copy(string sourcePath, string targetDirectory)
        {
            throw new NotImplementedException();
        }

        public void Move(string sourcePath, string targetDirectory)
        {
            throw new NotImplementedException();
        }
    }
}
