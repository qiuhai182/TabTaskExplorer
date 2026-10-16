using Avalonia;

namespace TabTaskExplorer.App;

/// <summary>程序入口：启动 Avalonia 桌面生命周期。</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia 应用构建配置：按当前操作系统选择平台实现。</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
