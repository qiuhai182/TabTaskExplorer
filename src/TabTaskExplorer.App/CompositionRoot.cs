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

        // 本机文本文件服务：IDE 模式本地文件读写与编码探测（跨平台）
        services.AddSingleton<ITextFileService, DefaultTextFileService>();

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

        // 网页端服务：仅绑定本机回环地址，页面与程序界面实时同构
        services.AddSingleton<Services.DebugWebHost>();

        // 平台专用实现：文件系统、进程枚举与系统资源采样
        if (OperatingSystem.IsWindows())
        {
            // Windows 文件系统实现：删除移入回收站
            services.AddSingleton<IFileSystemProvider, WindowsFileSystemProvider>();
            services.AddSingleton<IProcessProvider, WindowsProcessProvider>();
            services.AddSingleton<ISystemMetricsProvider, WindowsSystemMetricsProvider>();
            // 磁盘占用统计：Windows 平台实现
            services.AddSingleton<IDiskUsageProvider, WindowsDiskUsageProvider>();

            // 全盘搜索：内核遍历实现作为兜底，Windows 上优先走 Everything 消息接口
            services.AddSingleton<FileSystemWalkSearchService>();
            services.AddSingleton<IFileSearchService, EverythingIpcSearchService>();
        }
        else
        {
            // 其他平台：文件系统使用内核跨平台默认实现，全盘搜索仅用内置遍历实现
            services.AddSingleton<IFileSystemProvider, DefaultFileSystemProvider>();
            // 磁盘占用统计：其他平台内核默认实现
            services.AddSingleton<IDiskUsageProvider, DefaultDiskUsageProvider>();
            services.AddSingleton<FileSystemWalkSearchService>();
            services.AddSingleton<IFileSearchService, FileSystemWalkSearchService>();
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
