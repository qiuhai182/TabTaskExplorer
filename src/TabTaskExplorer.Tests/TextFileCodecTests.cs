using System.IO;
using System.Text;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>文本文件编码探测与读写测试（BOM 优先 / 严格 UTF-8 / GBK 回退 / 二进制与大文件保护）。</summary>
public class TextFileCodecTests
{
    /// <summary>无 BOM 的纯 ASCII / 中文 UTF-8 文本按 UTF-8 解码。</summary>
    [Fact]
    public void Decode_NoBomUtf8_ReturnsUtf8Name()
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes("中文内容 content");

        TextFileContent content = TextFileCodec.Decode(bytes);

        Assert.Equal(TextFileCodec.Utf8Name, content.EncodingName);
        Assert.Equal("中文内容 content", content.Text);
        Assert.False(content.IsBinary);
        Assert.False(content.IsTooLarge);
        Assert.True(content.IsEditable);
    }

    /// <summary>带 BOM 的 UTF-8 文本按 UTF-8 BOM 解码且剥掉前导标记。</summary>
    [Fact]
    public void Decode_Utf8Bom_StripsPreamble()
    {
        byte[] body = new UTF8Encoding(false).GetBytes("带标记");
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF };
        byte[] combined = new byte[bytes.Length + body.Length];
        Buffer.BlockCopy(bytes, 0, combined, 0, bytes.Length);
        Buffer.BlockCopy(body, 0, combined, bytes.Length, body.Length);

        TextFileContent content = TextFileCodec.Decode(combined);

        Assert.Equal(TextFileCodec.Utf8BomName, content.EncodingName);
        Assert.Equal("带标记", content.Text);
    }

    /// <summary>UTF-16 LE / BE 与 UTF-32 的 BOM 都能正确识别并按对应编码解码。</summary>
    [Fact]
    public void Decode_Utf16AndUtf32Bom_RecognizedByEncodingName()
    {
        byte[] utf16Le = WithPreamble(new UnicodeEncoding(false, true), "文本A");
        byte[] utf16Be = WithPreamble(new UnicodeEncoding(true, true), "文本B");
        byte[] utf32Le = WithPreamble(new UTF32Encoding(false, true), "文本C");
        byte[] utf32Be = WithPreamble(new UTF32Encoding(true, true), "文本D");

        Assert.Equal("文本A", TextFileCodec.Decode(utf16Le).Text);
        Assert.Equal(TextFileCodec.Utf16LeName, TextFileCodec.Decode(utf16Le).EncodingName);
        Assert.Equal("文本B", TextFileCodec.Decode(utf16Be).Text);
        Assert.Equal(TextFileCodec.Utf16BeName, TextFileCodec.Decode(utf16Be).EncodingName);
        Assert.Equal("文本C", TextFileCodec.Decode(utf32Le).Text);
        Assert.Equal(TextFileCodec.Utf32LeName, TextFileCodec.Decode(utf32Le).EncodingName);
        Assert.Equal("文本D", TextFileCodec.Decode(utf32Be).Text);
        Assert.Equal(TextFileCodec.Utf32BeName, TextFileCodec.Decode(utf32Be).EncodingName);
    }

    /// <summary>按编码把文本编成带前导标记的字节数组。</summary>
    private static byte[] WithPreamble(Encoding encoding, string text)
    {
        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes(text);
        byte[] combined = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, combined, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, combined, preamble.Length, body.Length);
        return combined;
    }

    /// <summary>非法 UTF-8 字节序列回退 GBK 解码，中文不再显示为乱码。</summary>
    [Fact]
    public void Decode_InvalidUtf8_FallsBackToGbk()
    {
        byte[] bytes = Encoding.GetEncoding(936).GetBytes("中文测试");

        TextFileContent content = TextFileCodec.Decode(bytes);

        Assert.Equal(TextFileCodec.GbkName, content.EncodingName);
        Assert.Equal("中文测试", content.Text);
    }

    /// <summary>无 BOM 且含空字节的内容判定为二进制，不可按文本编辑。</summary>
    [Fact]
    public void Decode_NullByte_MarkedBinary()
    {
        byte[] bytes = new byte[] { 0x50, 0x4B, 0x00, 0x01 };

        TextFileContent content = TextFileCodec.Decode(bytes);

        Assert.True(content.IsBinary);
        Assert.False(content.IsEditable);
    }

    /// <summary>超过 5MB 的内容标记为超大，拒绝按文本打开。</summary>
    [Fact]
    public void Decode_OversizedBytes_MarkedTooLarge()
    {
        byte[] bytes = new byte[TextFileCodec.MaxTextFileBytes + 10];

        TextFileContent content = TextFileCodec.Decode(bytes);

        Assert.True(content.IsTooLarge);
        Assert.False(content.IsEditable);
    }

    /// <summary>编码后再解码保持内容一致（UTF-8 / UTF-8 BOM / UTF-16 / GBK 往返）。</summary>
    [Theory]
    [InlineData(TextFileCodec.Utf8Name)]
    [InlineData(TextFileCodec.Utf8BomName)]
    [InlineData(TextFileCodec.Utf16LeName)]
    [InlineData(TextFileCodec.Utf16BeName)]
    [InlineData(TextFileCodec.GbkName)]
    public void EncodeThenDecode_RoundTripsText(string encodingName)
    {
        string text = "第一行\n第二行 中文 content";

        byte[] bytes = TextFileCodec.Encode(text, encodingName);

        Assert.Equal(text, TextFileCodec.Decode(bytes).Text);
    }

    /// <summary>本机文本服务：写入后读回内容与编码名一致，文件确实落盘。</summary>
    [Fact]
    public void DefaultTextFileService_WriteThenRead_KeepsContentAndEncoding()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tte-text-service-test");
        Directory.CreateDirectory(directory);
        string filePath = Path.Combine(directory, "sample.txt");
        DefaultTextFileService service = new DefaultTextFileService();
        try
        {
            service.Write(filePath, "中文写入测试", TextFileCodec.Utf8BomName);

            TextFileContent content = service.Read(filePath);

            Assert.Equal("中文写入测试", content.Text);
            Assert.Equal(TextFileCodec.Utf8BomName, content.EncodingName);
            byte[] raw = File.ReadAllBytes(filePath);
            Assert.Equal(0xEF, raw[0]);
            Assert.Equal(0xBB, raw[1]);
            Assert.Equal(0xBF, raw[2]);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }
}
