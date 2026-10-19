using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;

namespace TabTaskExplorer.App;

/// <summary>Avalonia ?????????????????????????????? App.axaml ??????????</summary>
public class App : Application
{
    private TrayIcon? trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ?????????????Ψ??????????????λ??????????????κ???????
            IServiceProvider services = CompositionRoot.BuildServices();

            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.DataContext = new MainWindowViewModel(services);

            // ???????????????????????????????? http://127.0.0.1:15919 ??
            MainWindowViewModel mainViewModel = (MainWindowViewModel)desktop.MainWindow.DataContext;
            mainViewModel.StartDebugWebHost(services.GetRequiredService<Services.DebugWebHost>());

            // ???δ???????????????????? /api/diagnostics ?????
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += this.OnUiUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += this.OnDomainUnhandledException;

            this.CreateTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>UI ???δ???????????????????????????????????С?</summary>
    private void OnUiUnhandledException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Services.DebugWebHost.RecordDiagnostic("UI???", e.Exception);
        e.Handled = true;
    }

    /// <summary>AppDomain δ????????????????????</summary>
    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception error)
        {
            Services.DebugWebHost.RecordDiagnostic("AppDomain", error);
        }
    }

    /// <summary>????????????????????????????????????????????????</summary>
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
            // ????????????????????
        }

        NativeMenu menu = new NativeMenu();

        NativeMenuItem showItem = new NativeMenuItem("?????????");
        showItem.Click += this.OnTrayShowClick;
        menu.Add(showItem);

        NativeMenuItem autoStartItem = new NativeMenuItem("????????????");
        autoStartItem.Click += this.OnTrayAutoStartClick;
        menu.Add(autoStartItem);

        NativeMenuItem exitItem = new NativeMenuItem("???");
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

    /// <summary>????????????????</summary>
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

    /// <summary>??????????</summary>
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

    /// <summary>??????????п?????????????????</summary>
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
            if (item.Header != null && item.Header.ToString()!.StartsWith("????????", StringComparison.Ordinal))
            {
                item.Header = autoStart ? "????????????" : "????????????";
            }
        }
    }

    /// <summary>???????????????????????????</summary>
    public void OnTrayIconClicked(object? sender, EventArgs e)
    {
        this.ShowMainWindow();
    }

    /// <summary>??????????????????</summary>
    public void OnTrayShowClick(object? sender, EventArgs e)
    {
        this.ShowMainWindow();
    }

    /// <summary>?????????л???????????</summary>
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

    /// <summary>????????????????????????</summary>
    public void OnTrayExitClick(object? sender, EventArgs e)
    {
        this.CurrentMainWindow?.ForceClose();
    }

    /// <summary>????????????????</summary>
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
