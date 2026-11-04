using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>图片剪贴板写入结果：成功标志与可直接展示的中文诊断说明。</summary>
public class ClipboardImageWriteResult
{
    /// <summary>是否至少有一种图片格式写入成功。</summary>
    public bool Success { get; set; }

    /// <summary>中文诊断说明，包含每种格式的写入结果，供上层原样记录日志。</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>构造成功结果。</summary>
    public static ClipboardImageWriteResult CreateSuccess(string detail)
    {
        ClipboardImageWriteResult result = new ClipboardImageWriteResult();
        result.Success = true;
        result.Detail = detail;
        return result;
    }

    /// <summary>构造失败结果。</summary>
    public static ClipboardImageWriteResult CreateFailure(string detail)
    {
        ClipboardImageWriteResult result = new ClipboardImageWriteResult();
        result.Success = false;
        result.Detail = detail;
        return result;
    }
}

/// <summary>
/// 图片剪贴板写入服务：一次写入同时提供 PNG、CF_DIB / CF_DIBV5 与文件列表，
/// 让聊天软件粘贴得到图片、资源管理器粘贴得到文件。
/// </summary>
public interface IClipboardImageWriter
{
    /// <summary>当前平台是否支持图片剪贴板写入。</summary>
    bool IsSupported { get; }

    /// <summary>
    /// 把一张 PNG 图片与可选文件路径写入系统剪贴板。
    /// </summary>
    /// <param name="pngBytes">图片的 PNG 字节。</param>
    /// <param name="filePaths">同时写入文件列表的路径（可为空，不影响图片写入）。</param>
    /// <returns>写入结果；失败时上层回退为仅写文件路径。</returns>
    Task<ClipboardImageWriteResult> WriteAsync(byte[] pngBytes, IReadOnlyList<string> filePaths);
}

/// <summary>不支持图片剪贴板的平台使用：永远返回失败，上层回退为只写文件路径。</summary>
public class NullClipboardImageWriter : IClipboardImageWriter
{
    /// <inheritdoc />
    public bool IsSupported
    {
        get { return false; }
    }

    /// <inheritdoc />
    public Task<ClipboardImageWriteResult> WriteAsync(byte[] pngBytes, IReadOnlyList<string> filePaths)
    {
        return Task.FromResult(ClipboardImageWriteResult.CreateFailure("当前平台不支持图片剪贴板写入"));
    }
}
