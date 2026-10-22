using Avalonia;

namespace TabTaskExplorer.App;

/// <summary>程序入口：单实例判定与参数转发，然后启动 Avalonia 桌面生命周期。</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 入口日志：本次启动收到的全部参数（系统唤起的文件夹路径走这里进入）
        Services.UiLogger.Log("程序启动", "程序入口：进程号=" + Environment.ProcessId
            + " 参数个数=" + args.Length + " 参数内容=" + string.Join(" | ", args));

        // 单实例：拿到互斥量说明没有既有实例，正常启动主实例
        if (Services.SingleInstanceService.AcquirePrimary())
        {
            Services.UiLogger.Log("单实例服务", "单实例判定结果：未发现既有实例，本进程作为主实例正常启动");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return;
        }
        // 已有实例运行：把本次启动参数（系统唤起的文件夹路径）经命名管道转发给既有实例后退出；
        // 转发失败但成功抢占到互斥量时降级为正常启动，保证唤起请求不丢失
        Services.UiLogger.Log("单实例服务", "单实例判定结果：检测到既有实例运行，本进程作为第二实例启动，先尝试经命令管道转发启动参数后退出");
        if (!Services.SingleInstanceService.TryRunAsSecondary(args))
        {
            Services.UiLogger.Log("单实例服务", "第二实例转发未成功但已抢占到互斥量，降级为独立主实例启动，唤起请求改由本进程直接处理");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        else
        {
            Services.UiLogger.Log("单实例服务", "第二实例转发完成，本进程退出，唤起请求由既有实例处理");
        }
    }

    /// <summary>Avalonia 应用构建配置：按当前操作系统选择平台实现。</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
