using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.App;

/// <summary>Avalonia 应用类：装配服务、创建主窗口；托盘图标在此注册并常驻守护。</summary>
public class App : Application
{
    private TrayIcon? trayIcon;
    private IServiceProvider? appServices;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 组合根：整个应用唯一按操作系统分支的位置，业务代码不出现任何平台类型
            IServiceProvider services = CompositionRoot.BuildServices();
            this.appServices = services;

            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.DataContext = new MainWindowViewModel(services);

            // 扩展客户区下隐藏系统绘制的窗口图标与程序名（与 TabX 一致，只留自绘徽标）
            desktop.MainWindow.Opened += this.OnMainWindowOpened;

            // 浏览器调试镜像：界面同构实时同步，浏览器打开 http://127.0.0.1:15919 查看
            MainWindowViewModel mainViewModel = (MainWindowViewModel)desktop.MainWindow.DataContext;
            mainViewModel.StartDebugWebHost(services.GetRequiredService<Services.DebugWebHost>());

            // 采集未处理异常到调试镜像（浏览器 /api/diagnostics 可查看）
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += this.OnUiUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += this.OnDomainUnhandledException;

            this.CreateTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>主窗口打开后：隐藏系统绘制的窗口标题（图标 + 程序名）。</summary>
    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            IntPtr? handle = desktop.MainWindow.TryGetPlatformHandle()?.Handle;
            if (handle != null && this.appServices != null)
            {
                this.appServices.GetRequiredService<IWindowCaptionHider>().HideCaption(handle.Value);
            }
        }
    }

    /// <summary>UI 线程未处理异常：记录到调试镜像，并尽量保持程序运行。</summary>
    private void OnUiUnhandledException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Services.DebugWebHost.RecordDiagnostic("UI线程", e.Exception);
        e.Handled = true;
    }

    /// <summary>AppDomain 未处理异常：记录到调试镜像。</summary>
    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception error)
        {
            Services.DebugWebHost.RecordDiagnostic("AppDomain", error);
        }
    }

    /// <summary>创建托盘常驻图标与右键菜单：显示主窗口、开机自启、退出。</summary>
    private void CreateTrayIcon()
    {
        WindowIcon? trayWindowIcon = null;
        try
        {
            using (System.IO.Stream? stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("TabTaskExplorer.App.Assets.app.ico"))
            {
                if (stream != null)
                {
                    trayWindowIcon = new WindowIcon(stream);
                }
            }
        }
        catch
        {
            // 图标资源缺失时使用系统默认图标
        }

        NativeMenu menu = new NativeMenu();

        NativeMenuItem showItem = new NativeMenuItem("显示主窗口");
        showItem.Click += this.OnTrayShowClick;
        menu.Add(showItem);

        NativeMenuItem autoStartItem = new NativeMenuItem("开机自启：关");
        autoStartItem.Click += this.OnTrayAutoStartClick;
        menu.Add(autoStartItem);

        NativeMenuItem exitItem = new NativeMenuItem("退出");
        exitItem.Click += this.OnTrayExitClick;
        menu.Add(exitItem);

        this.trayIcon = new TrayIcon
        {
            ToolTipText = "TabTaskExplorer",
            Menu = menu,
            IsVisible = true,
        };
        if (trayWindowIcon != null)
        {
            this.trayIcon.Icon = trayWindowIcon;
        }
        this.trayIcon.Clicked += this.OnTrayIconClicked;

        // 关键：必须挂到 Application 的 TrayIcons 附加属性，图标才会向系统注册显示
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { this.trayIcon });
    }

    /// <summary>当前主窗口视图模型。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow?.DataContext as MainWindowViewModel;
            }
            return null;
        }
    }

    /// <summary>当前主窗口。</summary>
    private MainWindow? CurrentMainWindow
    {
        get
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow as MainWindow;
            }
            return null;
        }
    }

    /// <summary>同步托盘菜单中开机自启的开关文字。</summary>
    public void SyncTrayAutoStartState()
    {
        MainWindowViewModel? viewModel = this.MainViewModel;
        bool autoStart = viewModel?.Settings.AutoStart ?? false;
        if (this.trayIcon?.Menu == null)
        {
            return;
        }
        foreach (NativeMenuItem item in this.trayIcon.Menu.Items)
        {
            if (item.Header != null && item.Header.ToString()!.StartsWith("开机自启", StringComparison.Ordinal))
            {
                item.Header = autoStart ? "开机自启：开" : "开机自启：关";
            }
        }
    }

    /// <summary>托盘图标左键单击：唤起主窗口。</summary>
    public void OnTrayIconClicked(object? sender, EventArgs e)
    {
        this.ShowMainWindow();
    }

    /// <summary>托盘菜单：显示主窗口。</summary>
    public void OnTrayShowClick(object? sender, EventArgs e)
    {
        this.ShowMainWindow();
    }

    /// <summary>托盘菜单：切换开机自启。</summary>
    public void OnTrayAutoStartClick(object? sender, EventArgs e)
    {
        MainWindowViewModel? viewModel = this.MainViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.Settings.AutoStart = !viewModel.Settings.AutoStart;
        AutoStartManager.Apply(viewModel.Settings.AutoStart);
        viewModel.SaveSettingsAndApply();
        this.SyncTrayAutoStartState();
    }

    /// <summary>托盘菜单：真正退出并保存会话。</summary>
    public void OnTrayExitClick(object? sender, EventArgs e)
    {
        this.CurrentMainWindow?.ForceClose();
    }

    /// <summary>显示并激活主窗口。</summary>
    private void ShowMainWindow()
    {
        MainWindow? window = this.CurrentMainWindow;
        if (window == null)
        {
            return;
        }
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
