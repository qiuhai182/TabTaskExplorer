using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 多窗口注册表：同进程内全部主窗口的有序清单。
/// 负责窗口登记 / 注销、主窗口顺序维护，以及拖出标签时创建承载该标签的新窗口。
/// 单进程多窗口，配合单实例服务实现「禁止多实例、允许多窗口」。
/// </summary>
public static class WindowRegistry
{
    /// <summary>已注册窗口（按创建顺序，首个即主窗口）。</summary>
    private static readonly List<MainWindow> windows = new List<MainWindow>();

    /// <summary>创建拖出窗口所需的组合根服务（应用启动时注入一次）。</summary>
    private static IServiceProvider? registryServices;

    /// <summary>登记窗口（主窗口与拖出窗口共用）。</summary>
    public static void Register(MainWindow window)
    {
        if (!windows.Contains(window))
        {
            windows.Add(window);
            UiLogger.Log("程序行为", "窗口登记：窗口总数=" + windows.Count);
        }
    }

    /// <summary>注销窗口（窗口真正关闭后调用）。</summary>
    public static void Unregister(MainWindow window)
    {
        if (windows.Remove(window))
        {
            UiLogger.Log("程序行为", "窗口关闭并移出注册表：剩余窗口数=" + windows.Count);
            PromoteMainWindowIfNeeded(window);
        }
    }

    /// <summary>全部已登记窗口（按创建顺序）。</summary>
    public static IReadOnlyList<MainWindow> GetAllWindows()
    {
        return windows;
    }

    /// <summary>主窗口：注册表中首个仍登记的窗口。</summary>
    public static MainWindow? Primary
    {
        get
        {
            return windows.Count > 0 ? windows[0] : null;
        }
    }

    /// <summary>窗口在注册表中的序号（从 0 开始，未登记或空引用返回 -1），用于日志中定位具体窗口。</summary>
    public static int GetWindowIndex(MainWindow? window)
    {
        if (window == null)
        {
            return -1;
        }
        return windows.IndexOf(window);
    }

    /// <summary>给定窗口是否为最后一个窗口（Closing 阶段调用，此时窗口尚未注销）。</summary>
    public static bool IsLastWindow(MainWindow window)
    {
        return windows.Count <= 1;
    }

    /// <summary>应用启动时注入组合根服务（隐藏窗口标题等能力依赖）。</summary>
    public static void Init(IServiceProvider services)
    {
        registryServices = services;
    }

    /// <summary>
    /// 创建并显示承载单个标签的新窗口：与源窗口共享同一份设置对象，
    /// 不恢复会话标签（由调用方把标签移入），固定以资源管理器模式打开。
    /// 组合根服务来自启动时的 Init 注入。
    /// </summary>
    public static MainWindow CreateSecondaryWindow(AppSettings sharedSettings, Window sourceWindow)
    {
        if (registryServices == null)
        {
            throw new InvalidOperationException("多窗口注册表尚未初始化：创建新窗口前必须先调用 Init 注入组合根服务");
        }
        IServiceProvider services = registryServices;
        MainWindow window = new MainWindow();
        MainWindowViewModel viewModel = new MainWindowViewModel(services, sharedSettings);
        window.DataContext = viewModel;
        // 新窗口在源窗口右下方级联展开，随后钳制到屏幕内
        // 拖出标签产生的仍是主窗口家族，但任务栏只保留首个主窗口一个按钮；
        // 在 Show 之前关闭本窗口的任务栏入口，之后仍可通过 Alt+Tab 或窗口切换访问
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        try
        {
            window.Position = new PixelPoint(sourceWindow.Position.X + 40, sourceWindow.Position.Y + 40);
        }
        catch
        {
            // 源窗口位置不可用时交由系统决定默认位置
        }
        window.Opened += OnSecondaryWindowOpened;
        window.Closed += OnSecondaryWindowClosed;
        UiLogger.Log("程序行为", "创建新窗口：源窗口位置=" + sourceWindow.Position
            + " 新窗口标题=" + (viewModel.SelectedTab != null ? "已移入标签" : "空")
            + " 任务栏按钮=关闭（仅保留首个主窗口图标）");
        Register(window);
        window.Show();
        window.Activate();
        ClampToScreen(window);
        return window;
    }

    /// <summary>新窗口打开后：隐藏系统绘制的窗口标题（与主窗口一致，只留自绘徽标）。</summary>
    private static void OnSecondaryWindowOpened(object? sender, EventArgs e)
    {
        if (sender is Window window && registryServices != null)
        {
            IntPtr? handle = window.TryGetPlatformHandle()?.Handle;
            if (handle != null)
            {
                registryServices.GetRequiredService<IWindowCaptionHider>().HideCaption(handle.Value);
            }
        }
    }

    /// <summary>新窗口关闭：移出注册表；若主窗口已被关闭则把剩余窗口提升为生命周期主窗口。</summary>
    private static void OnSecondaryWindowClosed(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.Opened -= OnSecondaryWindowOpened;
            window.Closed -= OnSecondaryWindowClosed;
            Unregister(window);
        }
    }

    /// <summary>主窗口真正关闭后：把注册表中下一个窗口提升为生命周期主窗口，保证托盘与退出逻辑指向有效窗口。</summary>
    private static void PromoteMainWindowIfNeeded(MainWindow closingWindow)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && object.ReferenceEquals(desktop.MainWindow, closingWindow))
            {
                desktop.MainWindow = windows.Count > 0 ? windows[0] : null;
                if (desktop.MainWindow != null)
                {
                    // 被提升的拖出窗口原本关闭了任务栏入口；提升后恢复为当前唯一的主窗口按钮
                    desktop.MainWindow.ShowInTaskbar = true;
                    UiLogger.Log("程序行为", "主窗口已关闭，把剩余窗口提升为生命周期主窗口：剩余窗口数=" + windows.Count
                        + " 任务栏按钮=恢复");
                }
            }
        }
        catch
        {
            // 生命周期对象在退出阶段可能已不可用：忽略即可
        }
    }

    /// <summary>把窗口位置钳制到其所在屏幕内（显示后调用，避免新窗口落在屏幕外）。</summary>
    private static void ClampToScreen(MainWindow window)
    {
        try
        {
            Screen? screen = window.Screens.ScreenFromPoint(window.Position) ?? window.Screens.Primary;
            if (screen == null)
            {
                return;
            }
            PixelRect bounds = screen.Bounds;
            double scaling = window.RenderScaling;
            int widthPixels = (int)(window.Bounds.Width * scaling);
            int heightPixels = (int)(window.Bounds.Height * scaling);
            int clampedX = Math.Min(Math.Max(window.Position.X, bounds.X), bounds.X + Math.Max(bounds.Width - widthPixels, 0));
            int clampedY = Math.Min(Math.Max(window.Position.Y, bounds.Y), bounds.Y + Math.Max(bounds.Height - heightPixels, 0));
            if (clampedX != window.Position.X || clampedY != window.Position.Y)
            {
                window.Position = new PixelPoint(clampedX, clampedY);
            }
        }
        catch
        {
            // 屏幕信息不可用时保持级联位置
        }
    }
}
