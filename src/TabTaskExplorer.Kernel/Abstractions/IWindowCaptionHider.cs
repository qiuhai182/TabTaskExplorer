using System;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 窗口系统标题隐藏器：扩展客户区模式下隐藏系统绘制的窗口图标与程序名，
/// 只保留应用自绘的标题栏。Windows 实现（去除 WS_CAPTION 样式位）；其他平台留空。
/// </summary>
public interface IWindowCaptionHider
{
    /// <summary>隐藏指定原生窗口句柄上的系统标题（图标 + 程序名）。</summary>
    /// <param name="nativeHandle">原生窗口句柄。</param>
    void HideCaption(IntPtr nativeHandle);
}
