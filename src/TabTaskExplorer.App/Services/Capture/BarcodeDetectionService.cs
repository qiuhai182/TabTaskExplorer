using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>二维码 / 条码识别结果。</summary>
public class BarcodeScanResult
{
    /// <summary>是否成功解出内容。</summary>
    public bool Success { get; set; }

    /// <summary>解码文本（网址、文本或编号）。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>码制名称（QRCode、Code128 等）。</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>失败原因（中文）。</summary>
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 二维码 / 条码识别服务：使用 ZXing.Net 纯托管解码，三端同码，不依赖 zxing-cpp 原生库。
/// 对应 Snipaste 的 BarcodeDecoder 能力（其原实现基于 zxing-cpp）。
/// </summary>
public static class BarcodeDetectionService
{
    /// <summary>解码一张位图中的二维码或条码；没有找到时返回中文提示。</summary>
    public static BarcodeScanResult Decode(Bitmap bitmap)
    {
        if (bitmap == null)
        {
            return CreateFailure("没有可识别的图片");
        }
        PixelSampler sampler = new PixelSampler(bitmap);
        try
        {
            return DecodePixels(sampler.ToPixelBytes(), sampler.Width, sampler.Height);
        }
        finally
        {
            sampler.Dispose();
        }
    }

    /// <summary>从 BGRA 连续像素解码二维码 / 条码（与界面解耦，便于单元测试与复用）。</summary>
    public static BarcodeScanResult DecodePixels(byte[] bgraBytes, int width, int height)
    {
        if (bgraBytes == null || width <= 0 || height <= 0 || bgraBytes.Length < width * height * 4)
        {
            return CreateFailure("没有可识别的图片");
        }
        try
        {
            RGBLuminanceSource source = new RGBLuminanceSource(bgraBytes, width, height,
                RGBLuminanceSource.BitmapFormat.BGRA32);
            BarcodeReaderGeneric reader = new BarcodeReaderGeneric();
            reader.Options.TryHarder = true;
            reader.Options.TryInverted = true;
            reader.Options.PossibleFormats = BuildFormats();
            Result? result = reader.Decode(source);
            if (result == null)
            {
                return CreateFailure("没有检测到二维码或条码");
            }
            BarcodeScanResult success = new BarcodeScanResult();
            success.Success = true;
            success.Text = result.Text == null ? string.Empty : result.Text;
            success.Format = result.BarcodeFormat.ToString();
            return success;
        }
        catch (Exception error)
        {
            return CreateFailure("条码识别异常：" + error.Message);
        }
    }

    /// <summary>列出 Snipaste 同样支持的常用码制，避免全码制检索拖慢大图。</summary>
    private static List<BarcodeFormat> BuildFormats()
    {
        List<BarcodeFormat> formats = new List<BarcodeFormat>();
        formats.Add(BarcodeFormat.QR_CODE);
        formats.Add(BarcodeFormat.DATA_MATRIX);
        formats.Add(BarcodeFormat.AZTEC);
        formats.Add(BarcodeFormat.PDF_417);
        formats.Add(BarcodeFormat.CODE_128);
        formats.Add(BarcodeFormat.CODE_39);
        formats.Add(BarcodeFormat.CODE_93);
        formats.Add(BarcodeFormat.ITF);
        formats.Add(BarcodeFormat.EAN_13);
        formats.Add(BarcodeFormat.EAN_8);
        formats.Add(BarcodeFormat.UPC_A);
        formats.Add(BarcodeFormat.UPC_E);
        return formats;
    }

    /// <summary>构造失败结果。</summary>
    private static BarcodeScanResult CreateFailure(string message)
    {
        BarcodeScanResult failure = new BarcodeScanResult();
        failure.Success = false;
        failure.ErrorMessage = message;
        return failure;
    }
}
