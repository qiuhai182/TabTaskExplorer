using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;

namespace TabTaskExplorer.App;

/// <summary>Avalonia 应用类：负责装配服务并创建主窗口。</summary>
public class App : Application
{
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

            MainWindowViewModel viewModel = new MainWindowViewModel(services);
            MainWindow mainWindow = new MainWindow();
            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
