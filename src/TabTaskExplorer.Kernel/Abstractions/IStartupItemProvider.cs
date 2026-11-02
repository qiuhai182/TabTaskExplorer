using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 系统启动项服务：枚举全部开机自启程序与服务，并支持启用 / 禁用。
/// 各操作系统在平台适配层各自实现本接口；不支持的系统返回空清单。
/// </summary>
public interface IStartupItemProvider
{
    /// <summary>当前平台是否支持读取与管理系统启动项。</summary>
    bool IsSupported { get; }

    /// <summary>当前平台名称，用于界面提示（例如“Windows 注册表、启动文件夹与服务”）。</summary>
    string PlatformText { get; }

    /// <summary>枚举全部系统启动项（程序、快捷方式、启动文件夹条目与自动启动服务）。</summary>
    IReadOnlyList<StartupItem> Enumerate();

    /// <summary>
    /// 启用或禁用一条启动项。需要管理员权限而当前进程无权修改时返回 false，
    /// 由应用层把失败原因展示给用户。
    /// </summary>
    bool SetEnabled(StartupItem item, bool enabled);
}
