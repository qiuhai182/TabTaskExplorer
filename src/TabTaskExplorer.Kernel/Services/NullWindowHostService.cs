using System;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 空窗口宿主实现：非 Windows 平台没有任务栏工具窗口扩展样式，
/// 保持 Avalonia 默认行为，界面层仍可通过 ShowInTaskbar / Owner 控制。
/// </summary>
public class NullWindowHostService : IWindowHostService
{
    /// <inheritdoc />
    public void ApplyToolWindow(IntPtr windowHandle)
    {
    }

    /// <inheritdoc />
    public void ApplyNoActivateWindow(IntPtr windowHandle)
    {
    }

    /// <inheritdoc />
    public void SetAppUserModelId(string appUserModelId)
    {
    }
}
