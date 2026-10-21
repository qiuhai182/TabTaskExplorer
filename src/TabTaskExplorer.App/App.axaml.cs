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

            // 网页端服务：界面同构实时同步，浏览器打开后访问本机回环地址查看；是否启动由设置决定
            MainWindowViewModel mainViewModel = (MainWindowViewModel)desktop.MainWindow.DataContext;
            mainViewModel.AttachDebugWebHost(services.GetRequiredService<Services.WebServiceHost>());

            // 开机自启：以配置为准重新写入系统注册并校验结果，保证设置与系统真实状态一致
            this.ReconcileAutoStart(mainViewModel);

            // 采集未处理异常到网页端服务（浏览器 /api/diagnostics 可查看）
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += this.OnUiUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += this.OnDomainUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += this.OnUnobservedTaskException;

            // 程序生命周期日志：正常退出与进程退出，保证每次运行都有明确的起点与终点
            desktop.ShutdownRequested += this.OnShutdownRequested;
            desktop.Exit += this.OnDesktopExit;
            AppDomain.CurrentDomain.ProcessExit += this.OnProcessExit;

            // 程序启动日志：版本、进程、可执行文件、运行环境与关键启动配置
            System.Reflection.AssemblyName assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName();
            string version = assemblyName.Version != null ? assemblyName.Version.ToString() : "未知";
            string executablePath = Environment.ProcessPath ?? "未知";
            Services.UiLogger.Log("程序启动", "版本=" + version + " 进程号=" + Environment.ProcessId + " 可执行文件=" + executablePath);
            Services.UiLogger.Log("程序启动", "操作系统=" + Environment.OSVersion.VersionString + " 运行环境=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                + " 开机自启=" + mainViewModel.Settings.AutoStart + " 网页端服务开关=" + mainViewModel.Settings.EnableWebService
                + " 网页端服务端口=" + mainViewModel.Settings.WebServicePort + " 界面主题=" + mainViewModel.Settings.Theme);
            Services.UiLogger.Log("程序启动", "日志文件路径=" + Services.UiLogger.FilePath);

            this.CreateTrayIcon();
            // 托盘菜单在创建后才存在，此处再同步一次开机自启的真实状态文字
            this.SyncTrayAutoStartState();
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

    /// <summary>UI 线程未处理异常：记录到网页端服务，并尽量保持程序运行。</summary>
    private void OnUiUnhandledException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Services.WebServiceHost.RecordDiagnostic("UI线程", e.Exception);
        Services.UiLogger.LogError("界面异常", "UI 线程未处理异常，已拦截并保持运行", e.Exception);
        e.Handled = true;
    }

    /// <summary>AppDomain 未处理异常：进程即将终止，按闪退崩溃记录完整堆栈。</summary>
    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception error)
        {
            Services.WebServiceHost.RecordDiagnostic("AppDomain", error);
            Services.UiLogger.LogError("崩溃", "AppDomain 未处理异常，是否终止进程=" + e.IsTerminating, error);
            return;
        }
        Services.UiLogger.Log("崩溃", "AppDomain 未处理异常，异常对象无法转换为 Exception，是否终止进程=" + e.IsTerminating
            + " 异常对象=" + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "空"));
    }

    /// <summary>后台任务未观察异常：不终止进程，记录后标记为已观察，避免进程被升级终止。</summary>
    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        Services.WebServiceHost.RecordDiagnostic("后台任务", e.Exception);
        Services.UiLogger.LogError("界面异常", "后台任务未观察异常（不影响界面，已标记为已观察）", e.Exception);
        e.SetObserved();
    }

    /// <summary>系统会话结束：记录退出请求来源。</summary>
    private void OnShutdownRequested(object? sender, Avalonia.Controls.ApplicationLifetimes.ShutdownRequestedEventArgs e)
    {
        Services.UiLogger.Log("程序退出", "收到系统关机或会话结束请求：取消退出=" + e.Cancel);
    }

    /// <summary>桌面生命周期结束：程序正常退出。</summary>
    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Services.UiLogger.Log("程序退出", "主程序正常退出：退出码=" + e.ApplicationExitCode + " 运行时长="
            + (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).ToString(@"hh\:mm\:ss"));
    }

    /// <summary>进程退出：作为最后的兜底记录，保证正常退出与闪退都能留下终止痕迹。</summary>
    private void OnProcessExit(object? sender, EventArgs e)
    {
        Services.UiLogger.Log("程序退出", "进程退出：退出码=" + Environment.ExitCode);
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

        NativeMenuItem openWebItem = new NativeMenuItem("打开网页");
        openWebItem.Click += this.OnTrayOpenWebClick;
        menu.Add(openWebItem);

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

    /// <summary>托盘菜单：用系统默认浏览器打开本机网页端服务页面。</summary>
    public void OnTrayOpenWebClick(object? sender, EventArgs e)
    {
        Services.WebServiceHost? webHost = null;
        if (this.appServices != null)
        {
            webHost = this.appServices.GetService<Services.WebServiceHost>();
        }
        string address = webHost != null && webHost.ListenUrls.Count > 0 ? webHost.ListenUrls[0] : string.Empty;
        if (string.IsNullOrEmpty(address))
        {
            Services.UiLogger.Log("网页端服务", "托盘打开网页失败：网页端服务未启动");
            MessageDialog.Show(this.CurrentMainWindow, "网页端未启动",
                "网页端服务当前未启动。请在设置中心开启「随程序启动网页端服务」，或检查配置的端口是否被占用。");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address)
            {
                UseShellExecute = true,
            });
            Services.UiLogger.Log("网页端服务", "托盘打开网页：" + address);
        }
        catch (Exception error)
        {
            Services.UiLogger.LogError("网页端服务", "托盘打开网页失败：" + address, error);
            MessageDialog.Show(this.CurrentMainWindow, "打开失败", "无法用系统默认浏览器打开网页端地址：" + address);
        }
    }

    /// <summary>
    /// 启动时校正开机自启：以配置为准用当前可执行文件路径重新写入系统注册，
    /// 注册失败时把配置回写为关，避免配置与系统真实状态长期不一致。
    /// </summary>
    private void ReconcileAutoStart(MainWindowViewModel viewModel)
    {
        AppSettings settings = viewModel.Settings;
        bool applied = AutoStartManager.Apply(settings.AutoStart);
        Services.UiLogger.Log("配置更改", "开机自启：启动时校正 配置值=" + settings.AutoStart + " 系统写入结果=" + applied);
        if (settings.AutoStart && !applied)
        {
            settings.AutoStart = false;
            viewModel.SaveSettingsAndApply();
            Services.UiLogger.Log("配置更改", "开机自启：系统写入未生效，配置已回写为关闭");
            Services.WebServiceHost.RecordDiagnostic("开机自启", new InvalidOperationException("开机自启注册未生效，已把配置回写为关闭"));
        }
        this.SyncTrayAutoStartState();
    }

    /// <summary>托盘菜单：切换开机自启，仅在系统实际生效后更新配置。</summary>
    public void OnTrayAutoStartClick(object? sender, EventArgs e)
    {
        MainWindowViewModel? viewModel = this.MainViewModel;
        if (viewModel == null)
        {
            return;
        }
        bool target = !viewModel.Settings.AutoStart;
        bool applied = AutoStartManager.Apply(target);
        if (applied)
        {
            viewModel.Settings.AutoStart = target;
            viewModel.SaveSettingsAndApply();
        }
        Services.UiLogger.Log("配置更改", "开机自启：托盘菜单切换 目标值=" + target + " 系统写入结果=" + applied);
        this.SyncTrayAutoStartState();
    }

    /// <summary>托盘菜单：真正退出并保存会话。</summary>
    public void OnTrayExitClick(object? sender, EventArgs e)
    {
        Services.UiLogger.Log("程序退出", "托盘菜单选择退出：保存会话后关闭主窗口");
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
