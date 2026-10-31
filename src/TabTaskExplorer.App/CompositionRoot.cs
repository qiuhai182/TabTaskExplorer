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

        // 压缩包服务：服务本体为内核跨平台实现（tar 原始字节引擎 + SharpCompress），桌面与网页端共用；
        // 路径访问 Linux 走平台层原始字节透传，其余平台用内核跨平台字符串实现
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IArchivePathAccess, LinuxArchivePathAccess>();
        }
        else
        {
            services.AddSingleton<IArchivePathAccess, DefaultArchivePathAccess>();
        }
        services.AddSingleton<IArchiveService, ArchiveService>();

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

        // 系统右键菜单：Windows 枚举真实系统菜单项；其他平台此能力留空
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ISystemContextMenuService, WindowsSystemContextMenuService>();
        }
        else
        {
            services.AddSingleton<ISystemContextMenuService, NullSystemContextMenuService>();
        }

        // 系统剪贴板：三平台共用同一套 Avalonia 实现，复制 / 剪切与外部程序双向互通
        services.AddSingleton<ISystemClipboardService, AvaloniaSystemClipboardService>();

        // 网页端服务：仅绑定本机回环地址，页面与程序界面实时同构
        services.AddSingleton<Services.WebServiceHost>();

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

            // 进程详情与调节（优先级、CPU 亲和性）：Windows 平台实现
            services.AddSingleton<IProcessDetailService, WindowsProcessDetailService>();
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
                // 进程详情与调节（优先级、CPU 亲和性）：Linux 平台实现
                services.AddSingleton<IProcessDetailService, LinuxProcessDetailService>();
            }
            else if (OperatingSystem.IsMacOS())
            {
                services.AddSingleton<IProcessProvider, MacProcessProvider>();
                services.AddSingleton<ISystemMetricsProvider, MacSystemMetricsProvider>();
                // 进程详情与调节（优先级、CPU 亲和性）：macOS 平台实现
                services.AddSingleton<IProcessDetailService, MacOSProcessDetailService>();
            }
        }

        // 磁盘扫描服务：共享增量快照与同路径单飞调度，桌面与全部网页工作区共用一个实例
        services.AddSingleton<Services.DiskScanService>();

        // 进程采样服务：共享的进程读取记录与单飞调度，新开标签先复用记录不重新读取
        services.AddSingleton<Services.ProcessSampleService>();

        // 媒体播放引擎：每个媒体标签一个实例；libvlc 缺失时引擎自动降级为不可用
        services.AddTransient<Kernel.Media.IMediaPlaybackEngine, Services.Media.VlcPlaybackEngine>();

        return services.BuildServiceProvider();
    }
}
