using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 非 Windows 平台的留空实现：系统右键菜单是 Windows 注册表与 Shell 扩展的专属能力，
/// 其他平台此能力留空——枚举返回空列表，启停与唤起一律返回 false，界面结构保持一致并展示"当前系统不支持"。
/// </summary>
public class NullSystemContextMenuService : ISystemContextMenuService
{
    public bool IsSupported
    {
        get { return false; }
    }

    public IReadOnlyList<SystemContextMenuEntry> Enumerate()
    {
        // 留空：非 Windows 平台没有系统右键菜单注册表
        return Array.Empty<SystemContextMenuEntry>();
    }

    public void Refresh()
    {
        // 留空：非 Windows 平台没有可刷新的系统右键菜单
    }

    public bool SetEnabled(SystemContextMenuEntry entry, bool enabled)
    {
        // 留空：非 Windows 平台不提供启停
        return false;
    }

    public bool Execute(SystemContextMenuEntry entry, string targetPath)
    {
        // 留空：非 Windows 平台不提供系统菜单项唤起
        return false;
    }
}
