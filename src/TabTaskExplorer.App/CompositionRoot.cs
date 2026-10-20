using Microsoft.Extensions.DependencyInjection;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Services;
using TabTaskExplorer.Kernel.Storage;
using TabTaskExplorer.Platform.Linux;
using TabTaskExplorer.Platform.MacOS;
using TabTaskExplorer.Platform.Windows;
using TabTaskExplorer.Protocol.Ssh;

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

        // 连接管理：内核自带的跨平台 JSON 存储与 TCP 连通性测试
        services.AddSingleton<IConnectionStore, JsonConnectionStore>();
        services.AddSingleton<IConnectivityTester, TcpConnectivityTester>();

        // 凭据加密：Windows 使用 DPAPI 当前用户作用域；其他平台此能力留空，降级为明文保存
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ISecretProtector, WindowsSecretProtector>();
        }
        else
        {
            services.AddSingleton<ISecretProtector, PlaintextSecretProtector>();
        }
        services.AddSingleton<ConnectionManager>();

        // 远程协议适配：按连接协议分派 SSH / SFTP 与 FTP 实现（终端、远程文件系统与传输队列）
        services.AddSingleton<IRemoteSessionFactory, CompositeRemoteSessionFactory>();

        // 系统标题隐藏：Windows 去除 WS_CAPTION 样式位；其他平台留空
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IWindowCaptionHider, WindowsCaptionHider>();
        }
        else
        {
            services.AddSingleton<IWindowCaptionHider, NullWindowCaptionHider>();
        }

        // 浏览器调试镜像服务：仅绑定本机回环地址，页面与程序界面实时同构
        services.AddSingleton<Services.DebugWebHost>();

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
