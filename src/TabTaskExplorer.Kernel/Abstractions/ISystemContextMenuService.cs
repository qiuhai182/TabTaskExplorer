using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 系统右键菜单服务：枚举系统真实右键菜单项、启停经典菜单项、唤起菜单项命令。
/// Windows 平台在适配层实现；其他平台此能力留空（见 NullSystemContextMenuService）。
/// </summary>
public interface ISystemContextMenuService
{
    /// <summary>当前平台是否支持系统右键菜单管理。</summary>
    bool IsSupported { get; }

    /// <summary>
    /// 取全部系统右键菜单项（带缓存，首次调用或刷新后做完整扫描）。
    /// </summary>
    /// <returns>全部条目列表；平台不支持时返回空列表。</returns>
    IReadOnlyList<SystemContextMenuEntry> Enumerate();

    /// <summary>丢弃缓存重新扫描全部系统右键菜单项，供设置页刷新按钮调用。</summary>
    void Refresh();

    /// <summary>
    /// 启用或禁用一条经典菜单项。
    /// </summary>
    /// <param name="entry">要操作的条目。</param>
    /// <param name="enabled">目标状态：true 启用，false 禁用。</param>
    /// <returns>设置成功返回 true；平台不支持、条目只读或注册表写入失败返回 false。</returns>
    bool SetEnabled(SystemContextMenuEntry entry, bool enabled);

    /// <summary>
    /// 唤起一条系统菜单项的命令（把目标路径代入命令模板后执行）。
    /// </summary>
    /// <param name="entry">要唤起的条目（必须 CanExecute）。</param>
    /// <param name="targetPath">右键目标的完整路径。</param>
    /// <returns>唤起成功返回 true。</returns>
    bool Execute(SystemContextMenuEntry entry, string targetPath);
}
