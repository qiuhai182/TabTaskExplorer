using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 图片剪贴板写入：一次写入同时放置 PNG（注册格式）、CF_DIBV5、CF_DIB 与 CF_HDROP 文件列表。
/// 聊天软件按位图格式粘贴得到图片，资源管理器按文件列表粘贴得到文件，两者互不干扰。
/// 透明像素统一合成到白色背景，避免 32 位位图的 alpha 通道在只认 BI_RGB 的程序里显示为黑块。
/// </summary>
public class WindowsClipboardImageWriter : IClipboardImageWriter
{
    private const int ClipboardFormatDib = 8;
    private const int ClipboardFormatHDrop = 15;
    private const int ClipboardFormatDibV5 = 17;
    private const uint GlobalMoveable = 0x0002;
    private const uint GlobalZeroInit = 0x0040;
    private const int BitmapInfoHeaderSize = 40;
    private const int BitmapV5HeaderSize = 124;
    private const int BitmapCompressionRgb = 0;
    private const int BitmapCompressionBitFields = 3;
    private const uint ColorSpaceSrgb = 0x73524742;
    private const int OpenClipboardAttempts = 6;
    private const int OpenClipboardRetryDelayMilliseconds = 60;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr ownerWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(int format, IntPtr memoryHandle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegisterClipboardFormat(string formatName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, int bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memoryHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memoryHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memoryHandle);

    /// <inheritdoc />
    public bool IsSupported
    {
        get { return true; }
    }

    /// <inheritdoc />
    public Task<ClipboardImageWriteResult> WriteAsync(byte[] pngBytes, IReadOnlyList<string> filePaths)
    {
        return Task.Run(delegate
        {
            return this.WriteCore(pngBytes, filePaths);
        });
    }

    /// <summary>在后台线程执行实际写入；任何一步失败都返回失败结果，由上层回退为只写文件路径。</summary>
    private ClipboardImageWriteResult WriteCore(byte[] pngBytes, IReadOnlyList<string> filePaths)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return ClipboardImageWriteResult.CreateFailure("图片字节为空，未写入剪贴板");
        }
        int width = 0;
        int height = 0;
        byte[] bottomUpPixels = null;
        if (!TryReadPixels(pngBytes, out width, out height, out bottomUpPixels))
        {
            return ClipboardImageWriteResult.CreateFailure("PNG 解码为 32 位位图失败 图片字节数=" + pngBytes.Length);
        }
        List<string> existingPaths = new List<string>();
        if (filePaths != null)
        {
            foreach (string path in filePaths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    existingPaths.Add(path);
                }
            }
        }

        bool opened = false;
        int openErrorCode = 0;
        for (int attempt = 1; attempt <= OpenClipboardAttempts; attempt++)
        {
            opened = OpenClipboard(IntPtr.Zero);
            if (opened)
            {
                break;
            }
            openErrorCode = Marshal.GetLastWin32Error();
            System.Threading.Thread.Sleep(OpenClipboardRetryDelayMilliseconds);
        }
        if (!opened)
        {
            return ClipboardImageWriteResult.CreateFailure("剪贴板被其他程序占用，打开失败 尝试次数="
                + OpenClipboardAttempts + " 最后错误代码=" + openErrorCode);
        }

        bool pngWritten = false;
        bool dibV5Written = false;
        bool dibWritten = false;
        bool filesWritten = false;
        int pngFormat = 0;
        int emptyErrorCode = 0;
        try
        {
            if (!EmptyClipboard())
            {
                emptyErrorCode = Marshal.GetLastWin32Error();
                return ClipboardImageWriteResult.CreateFailure("清空剪贴板失败 错误代码=" + emptyErrorCode);
            }

            pngFormat = RegisterClipboardFormat("PNG");
            if (pngFormat != 0)
            {
                pngWritten = SetClipboardBytes(pngFormat, pngBytes);
            }

            dibV5Written = SetClipboardBytes(ClipboardFormatDibV5, BuildDibV5(width, height, bottomUpPixels));
            dibWritten = SetClipboardBytes(ClipboardFormatDib, BuildDib(width, height, bottomUpPixels));
            if (existingPaths.Count > 0)
            {
                filesWritten = SetClipboardBytes(ClipboardFormatHDrop, BuildDropFiles(existingPaths));
            }

            string detail = "图片宽度=" + width + " 图片高度=" + height
                + " 图片字节数=" + pngBytes.Length
                + " PNG格式编号=" + pngFormat + " PNG=" + (pngWritten ? "成功" : "失败")
                + " CF_DIBV5=" + (dibV5Written ? "成功" : "失败")
                + " CF_DIB=" + (dibWritten ? "成功" : "失败")
                + " 随附文件数量=" + existingPaths.Count
                + " CF_HDROP=" + (existingPaths.Count == 0 ? "未写入（没有随附文件）" : (filesWritten ? "成功" : "失败"));
            if (!pngWritten && !dibV5Written && !dibWritten)
            {
                return ClipboardImageWriteResult.CreateFailure("PNG / CF_DIBV5 / CF_DIB 三种图片格式全部写入失败 " + detail);
            }
            return ClipboardImageWriteResult.CreateSuccess(detail);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>把 PNG 解码为 32 位 BGRA 像素，并把透明像素合成到白色背景，行序改为自下而上。</summary>
    private static bool TryReadPixels(byte[] pngBytes, out int width, out int height, out byte[] bottomUpPixels)
    {
        width = 0;
        height = 0;
        bottomUpPixels = null;
        try
        {
            using (MemoryStream stream = new MemoryStream(pngBytes))
            using (Image image = Image.FromStream(stream))
            {
                width = image.Width;
                height = image.Height;
                if (width <= 0 || height <= 0)
                {
                    return false;
                }
                using (Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(canvas))
                    {
                        graphics.Clear(Color.White);
                        graphics.DrawImage(image, new Rectangle(0, 0, width, height));
                    }
                    BitmapData bitmapData = canvas.LockBits(new Rectangle(0, 0, width, height),
                        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int stride = bitmapData.Stride;
                        byte[] topDown = new byte[stride * height];
                        Marshal.Copy(bitmapData.Scan0, topDown, 0, topDown.Length);
                        byte[] output = new byte[width * height * 4];
                        for (int row = 0; row < height; row++)
                        {
                            Array.Copy(topDown, row * stride, output, (height - 1 - row) * width * 4, width * 4);
                        }
                        bottomUpPixels = output;
                    }
                    finally
                    {
                        canvas.UnlockBits(bitmapData);
                    }
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>构造 CF_DIBV5 数据：124 字节 BITMAPV5HEADER + 自下而上的 32 位 BGRA 像素。</summary>
    private static byte[] BuildDibV5(int width, int height, byte[] bottomUpPixels)
    {
        int pixelBytes = bottomUpPixels.Length;
        byte[] buffer = new byte[BitmapV5HeaderSize + pixelBytes];
        WriteInt32(buffer, 0, BitmapV5HeaderSize);
        WriteInt32(buffer, 4, width);
        WriteInt32(buffer, 8, height);
        WriteInt16(buffer, 12, (short)1);
        WriteInt16(buffer, 14, (short)32);
        WriteInt32(buffer, 16, BitmapCompressionBitFields);
        WriteInt32(buffer, 20, pixelBytes);
        WriteInt32(buffer, 24, 0);
        WriteInt32(buffer, 28, 0);
        WriteInt32(buffer, 32, 0);
        WriteInt32(buffer, 36, 0);
        WriteInt32(buffer, 40, unchecked((int)0x00FF0000));
        WriteInt32(buffer, 44, unchecked((int)0x0000FF00));
        WriteInt32(buffer, 48, unchecked((int)0x000000FF));
        WriteInt32(buffer, 52, unchecked((int)0xFF000000));
        WriteInt32(buffer, 56, unchecked((int)ColorSpaceSrgb));
        // 颜色端点、Gamma 与渲染意图保持全零：sRGB 颜色空间下按默认值处理
        Array.Copy(bottomUpPixels, 0, buffer, BitmapV5HeaderSize, pixelBytes);
        return buffer;
    }

    /// <summary>构造 CF_DIB 数据：40 字节 BITMAPINFOHEADER + 自下而上的 32 位 BGRA 像素，供只认旧格式的程序使用。</summary>
    private static byte[] BuildDib(int width, int height, byte[] bottomUpPixels)
    {
        int pixelBytes = bottomUpPixels.Length;
        byte[] buffer = new byte[BitmapInfoHeaderSize + pixelBytes];
        WriteInt32(buffer, 0, BitmapInfoHeaderSize);
        WriteInt32(buffer, 4, width);
        WriteInt32(buffer, 8, height);
        WriteInt16(buffer, 12, (short)1);
        WriteInt16(buffer, 14, (short)32);
        WriteInt32(buffer, 16, BitmapCompressionRgb);
        WriteInt32(buffer, 20, pixelBytes);
        WriteInt32(buffer, 24, 0);
        WriteInt32(buffer, 28, 0);
        WriteInt32(buffer, 32, 0);
        WriteInt32(buffer, 36, 0);
        Array.Copy(bottomUpPixels, 0, buffer, BitmapInfoHeaderSize, pixelBytes);
        return buffer;
    }

    /// <summary>构造 CF_HDROP 数据：20 字节 DROPFILES 头 + UTF-16 路径列表（双空字符结尾）。</summary>
    private static byte[] BuildDropFiles(IReadOnlyList<string> paths)
    {
        List<string> normalized = new List<string>();
        foreach (string path in paths)
        {
            normalized.Add(Path.GetFullPath(path));
        }
        string joined = string.Join("\0", normalized) + "\0\0";
        byte[] pathBytes = System.Text.Encoding.Unicode.GetBytes(joined);
        byte[] buffer = new byte[20 + pathBytes.Length];
        WriteInt32(buffer, 0, 20);
        WriteInt32(buffer, 4, 0);
        WriteInt32(buffer, 8, 0);
        WriteInt32(buffer, 12, 0);
        WriteInt32(buffer, 16, 1);
        Array.Copy(pathBytes, 0, buffer, 20, pathBytes.Length);
        return buffer;
    }

    /// <summary>把一段字节放进全局内存并交给剪贴板；所有权转移成功，失败时自行释放。</summary>
    private static bool SetClipboardBytes(int format, byte[] payload)
    {
        IntPtr memoryHandle = GlobalAlloc(GlobalMoveable | GlobalZeroInit, payload.Length);
        if (memoryHandle == IntPtr.Zero)
        {
            return false;
        }
        IntPtr lockedPointer = GlobalLock(memoryHandle);
        if (lockedPointer == IntPtr.Zero)
        {
            GlobalFree(memoryHandle);
            return false;
        }
        try
        {
            Marshal.Copy(payload, 0, lockedPointer, payload.Length);
        }
        finally
        {
            GlobalUnlock(memoryHandle);
        }
        IntPtr result = SetClipboardData(format, memoryHandle);
        if (result == IntPtr.Zero)
        {
            GlobalFree(memoryHandle);
            return false;
        }
        return true;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, 4);
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, 2);
    }
}
