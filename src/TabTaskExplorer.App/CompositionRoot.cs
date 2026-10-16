using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Services;
using TabTaskExplorer.Kernel.Storage;
using TabTaskExplorer.Platform.Linux;
using TabTaskExplorer.Platform.MacOS;
using TabTaskExplorer.Platform.Windows;

namespace TabTaskExplorer.App;

/// <summary>
/// 组合根：整个项目唯一允许按操作系统分支的位置。
/// 应用层其余代码只认识内核接口，不接触任何平台类型。
/// </summary>
public static class CompositionRoot
{
    public static IServiceProvider BuildServices()
    {
        ServiceCollection services = new ServiceCollection();

        // 任务存储：内核自带的跨平台 JSON 实现
        services.AddSingleton<ITaskStore, JsonTaskStore>();

        // 平台专用实现：文件系统、进程枚举与系统资源采样
        if (OperatingSystem.IsWindows())
        {
            // Windows 文件系统实现：删除移入回收站
            services.AddSingleton<IFileSystemProvider, WindowsFileSystemProvider>();
            services.AddSingleton<IProcessProvider, WindowsProcessProvider>();
            services.AddSingleton<ISystemMetricsProvider, WindowsSystemMetricsProvider>();
        }
        else
        {
            // 其他平台：文件系统使用内核跨平台默认实现
            services.AddSingleton<IFileSystemProvider, DefaultFileSystemProvider>();
            if (OperatingSystem.IsLinux())
            {
                services.AddSingleton<IProcessProvider, LinuxProcessProvider>();
                services.AddSingleton<ISystemMetricsProvider, LinuxSystemMetricsProvider>();
            }
            else if (OperatingSystem.IsMacOS())
            {
                services.AddSingleton<IProcessProvider, MacProcessProvider>();
                services.AddSingleton<ISystemMetricsProvider, MacSystemMetricsProvider>();
            }
        }

        return services.BuildServiceProvider();
    }
}
