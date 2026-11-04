using System;
using Avalonia.Controls;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 辅助窗口任务栏策略：贴图、截图遮罩、取色、标注编辑器、结果窗与临时对话框统一
/// 隐藏任务栏按钮并附加 WS_EX_TOOLWINDOW，使其不进入 Alt+Tab。
/// Owner 由各窗口的 Show(owner) / ShowDialog(owner) 负责：遮罩与取色挂主窗口，
/// 贴图保持独立（主窗口隐藏到托盘时仍可见），主窗口本身始终是唯一任务栏入口。
/// </summary>
public static class WindowHostStyler
{
    /// <summary>进程级 AppUserModelID：与快捷方式 / 安装入口应保持一致，保证系统归入同一应用分组。</summary>
    public const string AppUserModelId = "TabTaskExplorer.Desktop";

    private static IWindowHostService? hostService;

    /// <summary>初始化窗口宿主服务并设置进程 AppUserModelID；必须在创建辅助窗口前调用。</summary>
    public static void Initialize(IWindowHostService windowHostService, Window? primaryWindow)
    {
        hostService = windowHostService;
        try
        {
            hostService.SetAppUserModelId(AppUserModelId);
            UiLogger.Log("窗口任务栏", "进程 AppUserModelID 已设置：取值=" + AppUserModelId
                + " 主窗口=" + (primaryWindow == null ? "尚未创建（启动早期设置）" : primaryWindow.GetType().Name));
        }
        catch (Exception error)
        {
            UiLogger.LogError("窗口任务栏", "设置进程 AppUserModelID 失败：取值=" + AppUserModelId, error);
        }
    }

    /// <summary>把窗口标记为辅助工具窗口：ShowInTaskbar=false，Opened 时附加 WS_EX_TOOLWINDOW。</summary>
    /// <param name="window">目标窗口。</param>
    public static void MakeAuxiliary(Window window)
    {
        if (window == null)
        {
            return;
        }
        window.ShowInTaskbar = false;
        window.Opened += OnAuxiliaryWindowOpened;
        if (window.IsVisible)
        {
            ApplyToolWindow(window);
        }
    }

    private static void OnAuxiliaryWindowOpened(object? sender, EventArgs e)
    {
        Window? window = sender as Window;
        if (window == null)
        {
            return;
        }
        ApplyToolWindow(window);
    }

    /// <summary>
    /// 把窗口标记为「可见但不打扰」的无焦点提示窗：不占任务栏、不进 Alt+Tab、点击不夺焦点。
    /// </summary>
    /// <param name="window">目标窗口。</param>
    public static void MakeNoActivate(Window window)
    {
        if (window == null)
        {
            return;
        }
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Opened += OnNoActivateWindowOpened;
        if (window.IsVisible)
        {
            ApplyNoActivate(window);
        }
    }

    private static void OnNoActivateWindowOpened(object? sender, EventArgs e)
    {
        Window? window = sender as Window;
        if (window == null)
        {
            return;
        }
        ApplyNoActivate(window);
    }

    /// <summary>对已创建的无焦点提示窗应用工具窗口与 WS_EX_NOACTIVATE 扩展样式。</summary>
    private static void ApplyNoActivate(Window window)
    {
        IWindowHostService? service = hostService;
        if (service == null)
        {
            return;
        }
        IntPtr? handle = window.TryGetPlatformHandle()?.Handle;
        if (handle == null || handle.Value == IntPtr.Zero)
        {
            UiLogger.Log("窗口任务栏", "无焦点提示窗句柄尚未就绪，等待 Opened 后重试：类型=" + window.GetType().Name);
            return;
        }
        try
        {
            service.ApplyNoActivateWindow(handle.Value);
            UiLogger.Log("窗口任务栏", "无焦点提示窗样式已应用：类型=" + window.GetType().Name
                + " 任务栏=隐藏 Alt加Tab=不进入 激活=不夺取焦点");
        }
        catch (Exception error)
        {
            UiLogger.LogError("窗口任务栏", "无焦点提示窗设置扩展样式失败：类型=" + window.GetType().Name, error);
        }
    }

    /// <summary>对已创建的窗口立即应用工具窗口扩展样式，并记录显示状态。</summary>
    private static void ApplyToolWindow(Window window)
    {
        IWindowHostService? service = hostService;
        if (service == null)
        {
            return;
        }
        IntPtr? handle = window.TryGetPlatformHandle()?.Handle;
        if (handle == null || handle.Value == IntPtr.Zero)
        {
            UiLogger.Log("窗口任务栏", "辅助窗口句柄尚未就绪，等待 Opened 后重试：类型=" + window.GetType().Name);
            return;
        }
        try
        {
            service.ApplyToolWindow(handle.Value);
            UiLogger.Log("窗口任务栏", "辅助窗口已标记为工具窗口：类型=" + window.GetType().Name
                + " 标题=" + (window.Title ?? string.Empty)
                + " 显示在任务栏=" + window.ShowInTaskbar
                + " 归属主窗口=" + (window.Owner == null ? "无" : window.Owner.GetType().Name));
        }
        catch (Exception error)
        {
            UiLogger.LogError("窗口任务栏", "辅助窗口设置工具窗口样式失败：类型=" + window.GetType().Name, error);
        }
    }
}
