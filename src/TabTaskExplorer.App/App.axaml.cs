using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;

namespace TabTaskExplorer.App;

/// <summary>Avalonia 应用类：负责装配服务、创建主窗口与托盘常驻图标。</summary>
public class App : Application
{
    private MainWindow? mainWindow;
    private TrayIcon? trayIcon;
    private NativeMenuItem? trayAutoStartItem;

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

            this.mainWindow = new MainWindow();
            this.mainWindow.DataContext = new MainWindowViewModel(services);
            desktop.MainWindow = this.mainWindow;

            this.CreateTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>创建托盘常驻图标与右键菜单：显示主窗口、开机自启、退出。</summary>
    private void CreateTrayIcon()
    {
        WindowIcon? trayWindowIcon = null;
        try
        {
            trayWindowIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://TabTaskExplorer/Assets/app.ico")));
        }
        catch
        {
            // 图标资源缺失时使用系统默认图标
        }

        NativeMenu menu = new NativeMenu();

        NativeMenuItem showItem = new NativeMenuItem("显示主窗口");
        showItem.Click += this.OnTrayShowClick;
        menu.Add(showItem);

        this.trayAutoStartItem = new NativeMenuItem("开机自启：关");
        this.trayAutoStartItem.Click += this.OnTrayAutoStartClick;
        menu.Add(this.trayAutoStartItem);

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
        this.trayIcon.Clicked += this.OnTrayShowClick;

        this.SyncTrayAutoStartState();
    }

    /// <summary>同步托盘菜单中开机自启的开关文字。</summary>
    public void SyncTrayAutoStartState()
    {
        MainWindowViewModel? viewModel = this.MainViewModel;
        if (viewModel != null && this.trayAutoStartItem != null)
        {
            this.trayAutoStartItem.Header = viewModel.Settings.AutoStart ? "开机自启：开" : "开机自启：关";
        }
    }

    /// <summary>主窗口视图模型。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get { return this.mainWindow?.DataContext as MainWindowViewModel; }
    }

    private void OnTrayShowClick(object? sender, EventArgs e)
    {
        if (this.mainWindow == null)
        {
            return;
        }
        this.mainWindow.Show();
        this.mainWindow.WindowState = WindowState.Normal;
        this.mainWindow.Activate();
    }

    private void OnTrayAutoStartClick(object? sender, EventArgs e)
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

    private void OnTrayExitClick(object? sender, EventArgs e)
    {
        this.mainWindow?.ForceClose();
    }
}
