using System;
using TabTaskExplorer.App.Services.Capture;
using Xunit;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace TabTaskExplorer.Tests;

/// <summary>二维码 / 条码识别服务测试：用 ZXing 生成二维码位图，再走本项目解码通路。</summary>
public class BarcodeDetectionServiceTests
{
    /// <summary>QR 码生成后应能被识别出原始文本，且码制为 QR_CODE。</summary>
    [Fact]
    public void DecodePixels_GeneratedQrCode_ReturnsOriginalText()
    {
        const string expectedText = "https://example.com/tabtaskexplorer";
        BarcodeWriterGeneric writer = new BarcodeWriterGeneric();
        writer.Format = BarcodeFormat.QR_CODE;
        QrCodeEncodingOptions options = new QrCodeEncodingOptions();
        options.Width = 320;
        options.Height = 320;
        options.Margin = 2;
        writer.Options = options;
        BitMatrix matrix = writer.Encode(expectedText);
        int width = matrix.Width;
        int height = matrix.Height;
        byte[] bgra = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = matrix[x, y] ? (byte)0 : (byte)255;
                int offset = (y * width + x) * 4;
                bgra[offset] = value;
                bgra[offset + 1] = value;
                bgra[offset + 2] = value;
                bgra[offset + 3] = 255;
            }
        }
        BarcodeScanResult result = BarcodeDetectionService.DecodePixels(bgra, width, height);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(expectedText, result.Text);
        Assert.Equal("QR_CODE", result.Format);
    }

    /// <summary>没有码的纯色图像应返回失败结果而不是抛出异常。</summary>
    [Fact]
    public void DecodePixels_BlankImage_ReturnsFailure()
    {
        const int width = 64;
        const int height = 64;
        byte[] bgra = new byte[width * height * 4];
        for (int index = 0; index < bgra.Length; index += 4)
        {
            bgra[index] = 255;
            bgra[index + 1] = 255;
            bgra[index + 2] = 255;
            bgra[index + 3] = 255;
        }
        BarcodeScanResult result = BarcodeDetectionService.DecodePixels(bgra, width, height);
        Assert.False(result.Success);
        Assert.Contains("没有检测到", result.ErrorMessage);
    }
}
