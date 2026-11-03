using System;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 窗口宿主能力：把辅助窗口标记为工具窗口（不进入任务栏与 Alt+Tab），
/// 并为进程设置显式 AppUserModelID，保证系统把全部窗口归入同一个应用分组。
/// Windows 提供 Win32 实现；其他平台留空，不改变 Avalonia 的默认行为。
/// </summary>
public interface IWindowHostService
{
    /// <summary>给窗口附加 WS_EX_TOOLWINDOW 扩展样式，并移除 WS_EX_APPWINDOW。</summary>
    /// <param name="windowHandle">窗口原生句柄。</param>
    void ApplyToolWindow(IntPtr windowHandle);

    /// <summary>为当前进程设置显式 AppUserModelID；空值或平台不支持时静默忽略。</summary>
    /// <param name="appUserModelId">应用分组标识。</param>
    void SetAppUserModelId(string appUserModelId);
}
