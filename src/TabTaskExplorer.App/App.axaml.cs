using System;
using System.Collections.Generic;
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
    private NativeMenuItem? copyWebAddressItem;
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

            // 多窗口：最后一个窗口关闭才退出程序（关闭中间窗口只关该窗口本身）
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.DataContext = new MainWindowViewModel(services);

            // 多窗口注册表：主窗口登记在册，窗口真正关闭后由 Closed 事件注销
            WindowRegistry.Init(services);
            WindowRegistry.Register((MainWindow)desktop.MainWindow);
            desktop.MainWindow.Closed += this.OnAnyMainWindowClosed;

            // 扩展客户区下隐藏系统绘制的窗口图标与程序名（与 TabX 一致，只留自绘徽标）
            desktop.MainWindow.Opened += this.OnMainWindowOpened;

            // 网页端服务：界面同构实时同步，浏览器打开后访问本机回环地址查看；是否启动由设置决定
            MainWindowViewModel mainViewModel = (MainWindowViewModel)desktop.MainWindow.DataContext;
            mainViewModel.AttachDebugWebHost(services.GetRequiredService<Services.WebServiceHost>());

            // 截图录屏协调器：绑定主窗口后注册全局快捷键，并接管截图、录屏与产物文件夹自动打开
            services.GetRequiredService<Services.Capture.CaptureCoordinator>().Attach(mainViewModel);

            // 单实例命令管道：第二实例把系统唤起的路径转发到这里（UI 线程处理）
            SingleInstanceService.StartServer(new Action<string>(this.HandleExternalCommand));

            // 程序启动参数：系统唤起直接拉起本进程时带入的文件夹路径，走系统唤起去重打开流程
            string[]? startupArgs = desktop.Args;
            if (startupArgs != null)
            {
                foreach (string arg in startupArgs)
                {
                    if (!string.IsNullOrWhiteSpace(arg))
                    {
                        Services.UiLogger.Log("程序行为", "收到系统唤起转发的路径：来源=程序启动参数 路径=" + arg);
                        mainViewModel.OpenPathFromSystem(arg.Trim(), "程序启动参数");
                    }
                }
            }

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

    /// <summary>
    /// 外部命令统一入口（单实例管道消息，UI 线程执行）：
    /// FOCUS = 激活主窗口；OPEN = 逐行路径交给系统唤起去重打开流程。
    /// </summary>
    private void HandleExternalCommand(string message)
    {
        string[] lines = message.Split('\n');
        if (lines.Length == 0)
        {
            return;
        }
        string command = lines[0].Trim();
        Services.UiLogger.Log("程序行为", "收到外部命令并解析：来源=单实例命令管道 命令类型=" + command
            + " 命令总行数=" + lines.Length + " 命令内容=" + message.Replace("\n", " | "));
        if (string.Equals(command, "FOCUS", StringComparison.Ordinal))
        {
            Services.UiLogger.Log("程序行为", "收到外部聚焦请求：来源=单实例命令管道，开始激活主窗口");
            this.ShowMainWindow();
            return;
        }
        if (string.Equals(command, "OPEN", StringComparison.Ordinal))
        {
            int pathCount = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0)
                {
                    pathCount++;
                }
            }
            MainWindow? targetWindow = this.CurrentMainWindow;
            MainWindowViewModel? target = targetWindow?.DataContext as MainWindowViewModel;
            if (target == null)
            {
                Services.UiLogger.Log("程序行为", "收到系统唤起路径但当前无可用窗口，已忽略：来源=单实例命令管道"
                    + " 待打开路径条数=" + pathCount + " 已登记窗口数=" + WindowRegistry.GetAllWindows().Count);
                return;
            }
            Services.UiLogger.Log("程序行为", "外部打开命令已受理：来源=单实例命令管道 待打开路径条数=" + pathCount
                + " 承接窗口序号=" + WindowRegistry.GetWindowIndex(targetWindow)
                + " 承接窗口状态=" + targetWindow!.WindowState
                + " 承接窗口标签总数=" + target.Tabs.Count);
            for (int i = 1; i < lines.Length; i++)
            {
                string path = lines[i].Trim();
                if (path.Length == 0)
                {
                    continue;
                }
                Services.UiLogger.Log("程序行为", "收到系统唤起转发的路径：来源=单实例命令管道 路径=" + path);
                target.OpenPathFromSystem(path, "单实例命令管道");
            }
            return;
        }
        Services.UiLogger.Log("程序行为", "收到无法识别的外部命令，已忽略：命令内容=" + message.Replace("\n", " | "));
    }

    /// <summary>窗口真正关闭（CloseToTray 取消关闭时不会触发）：从多窗口注册表注销。</summary>
    private void OnAnyMainWindowClosed(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            WindowRegistry.Unregister(window);
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
        Services.Capture.CaptureCoordinator? captureCoordinator = this.appServices == null
            ? null
            : this.appServices.GetService<Services.Capture.CaptureCoordinator>();
        if (captureCoordinator != null)
        {
            Services.UiLogger.Log("程序退出", "释放截图录屏协调器：注销全局快捷键并结束仍在进行的录制");
            captureCoordinator.Dispose();
        }
        Services.UiLogger.Log("程序退出", "主程序正常退出：退出码=" + e.ApplicationExitCode + " 运行时长="
            + (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).ToString(@"hh\:mm\:ss"));
    }

    /// <summary>进程退出：作为最后的兜底记录，保证正常退出与闪退都能留下终止痕迹。</summary>
    private void OnProcessExit(object? sender, EventArgs e)
    {
        Services.UiLogger.Log("程序退出", "进程退出：退出码=" + Environment.ExitCode);
    }

    /// <summary>创建托盘常驻图标与右键菜单：显示主窗口、打开网页、复制网页地址、开机自启、退出。</summary>
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

        NativeMenuItem copyWebAddressItem = new NativeMenuItem(BuildCopyWebAddressHeader(this.GetWebServiceAddress()));
        copyWebAddressItem.Click += this.OnTrayCopyWebAddressClick;
        menu.Add(copyWebAddressItem);
        this.copyWebAddressItem = copyWebAddressItem;

        // 菜单每次弹出前刷新标题里的当前地址：网页端服务可能中途启停
        menu.NeedsUpdate += this.OnTrayMenuNeedsUpdate;

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

    /// <summary>当前主窗口视图模型（生命周期主窗口不可用时回退到注册表窗口）。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get
        {
            return this.CurrentMainWindow?.DataContext as MainWindowViewModel;
        }
    }

    /// <summary>当前主窗口：优先生命周期主窗口，其次多窗口注册表中首个可见窗口，最后注册表首个窗口。</summary>
    private MainWindow? CurrentMainWindow
    {
        get
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow main && main.IsVisible)
            {
                return main;
            }
            foreach (MainWindow window in WindowRegistry.GetAllWindows())
            {
                if (window.IsVisible)
                {
                    return window;
                }
            }
            return WindowRegistry.Primary;
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
        string address = this.GetWebServiceAddress();
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

    /// <summary>取本机网页端服务地址；服务未启动时返回空字符串。</summary>
    private string GetWebServiceAddress()
    {
        Services.WebServiceHost? webHost = null;
        if (this.appServices != null)
        {
            webHost = this.appServices.GetService<Services.WebServiceHost>();
        }
        return webHost != null && webHost.ListenUrls.Count > 0 ? webHost.ListenUrls[0] : string.Empty;
    }

    /// <summary>组装“复制网页地址”菜单项标题：直接展示当前地址，服务未启动时展示提示文字。</summary>
    private static string BuildCopyWebAddressHeader(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return "复制网页地址：网页端未启动";
        }
        return "复制网页地址：" + address;
    }

    /// <summary>托盘菜单弹出前：刷新“复制网页地址”菜单项标题里的当前地址。</summary>
    private void OnTrayMenuNeedsUpdate(object? sender, EventArgs e)
    {
        if (this.copyWebAddressItem == null)
        {
            return;
        }
        string address = this.GetWebServiceAddress();
        this.copyWebAddressItem.Header = BuildCopyWebAddressHeader(address);
        Services.UiLogger.Log("网页端服务", "托盘菜单刷新复制网页地址：地址="
            + (string.IsNullOrEmpty(address) ? "（网页端未启动）" : address));
    }

    /// <summary>托盘菜单：把当前网页端地址复制到系统剪贴板。</summary>
    public void OnTrayCopyWebAddressClick(object? sender, EventArgs e)
    {
        string address = this.GetWebServiceAddress();
        if (string.IsNullOrEmpty(address))
        {
            Services.UiLogger.Log("网页端服务", "托盘复制网页地址失败：网页端服务未启动");
            MessageDialog.Show(this.CurrentMainWindow, "网页端未启动",
                "网页端服务当前未启动。请在设置中心开启「随程序启动网页端服务」，或检查配置的端口是否被占用。");
            return;
        }
        ISystemClipboardService? systemClipboard = this.appServices == null
            ? null
            : this.appServices.GetService<ISystemClipboardService>();
        if (systemClipboard == null)
        {
            Services.UiLogger.Log("程序行为", "托盘复制网页地址：系统剪贴板服务=未注册 结果=失败，未写入剪贴板");
            MessageDialog.Show(this.CurrentMainWindow, "复制失败", "系统剪贴板服务不可用，无法复制网页地址。");
            return;
        }
        Services.UiLogger.Log("程序行为", "托盘复制网页地址：地址=" + address
            + " 结果=已提交写入，具体结果见系统剪贴板服务日志");
        _ = this.CopyWebAddressAsync(systemClipboard, address);
    }

    /// <summary>执行网页端地址的剪贴板写入，失败时提示用户。</summary>
    private async System.Threading.Tasks.Task CopyWebAddressAsync(ISystemClipboardService systemClipboard, string address)
    {
        bool success = await systemClipboard.WriteTextAsync(address);
        if (success)
        {
            return;
        }
        Services.UiLogger.Log("程序行为", "托盘复制网页地址：地址=" + address + " 结果=失败，未写入剪贴板");
        MessageDialog.Show(this.CurrentMainWindow, "复制失败", "无法把网页地址写入系统剪贴板：" + address);
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

    /// <summary>托盘菜单：真正退出并保存会话（多窗口时逐个强制关闭）。</summary>
    public void OnTrayExitClick(object? sender, EventArgs e)
    {
        List<MainWindow> openWindows = new List<MainWindow>(WindowRegistry.GetAllWindows());
        Services.UiLogger.Log("程序退出", "托盘菜单选择退出：保存会话后关闭全部窗口 窗口总数=" + openWindows.Count);
        foreach (MainWindow window in openWindows)
        {
            window.ForceClose();
        }
    }

    /// <summary>显示并激活主窗口（多窗口时优先注册表中的主窗口，托盘常驻隐藏后由此唤起）。</summary>
    private void ShowMainWindow()
    {
        MainWindow? window = WindowRegistry.Primary;
        if (window == null)
        {
            Services.UiLogger.Log("程序行为", "外部聚焦请求执行结果：已登记窗口数=0，无可聚焦窗口");
            return;
        }
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        Services.UiLogger.Log("程序行为", "外部聚焦请求执行结果：聚焦窗口序号=" + WindowRegistry.GetWindowIndex(window)
            + " 窗口状态=" + window.WindowState + " 窗口可见=" + window.IsVisible
            + " 窗口标签总数=" + ((window.DataContext as MainWindowViewModel)?.Tabs.Count ?? 0));
    }
}
