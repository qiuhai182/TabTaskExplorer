using System.Collections.Generic;
using System.Threading.Tasks;
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

    public IReadOnlyList<SystemContextMenuEntry> EnumerateShellMenu(string targetPath, bool isBackground)
    {
        // 留空：非 Windows 平台没有系统外壳实时右键菜单
        return Array.Empty<SystemContextMenuEntry>();
    }

    public bool ExecuteShellMenuEntry(SystemContextMenuEntry entry, string targetPath, bool isBackground)
    {
        // 留空：非 Windows 平台不提供系统外壳菜单项唤起
        return false;
    }

    public bool SupportsNativeShellMenu
    {
        get { return false; }
    }

    public Task<NativeContextMenuResult> ShowNativeShellMenuAsync(NativeContextMenuRequest request)
    {
        // 留空：非 Windows 平台没有系统外壳原生菜单，调用方据此退回自有菜单
        NativeContextMenuResult result = new NativeContextMenuResult();
        result.Supported = false;
        result.MenuShown = false;
        result.Message = "当前系统不支持弹出 Windows 原生外壳右键菜单，已退回本程序菜单";
        return Task.FromResult(result);
    }

    public IReadOnlyList<SystemContextMenuEntry> EnumerateShellMenu(IReadOnlyList<string> paths, bool isBackground)
    {
        // 留空：非 Windows 平台没有系统外壳菜单，网页端只显示本程序命令
        return Array.Empty<SystemContextMenuEntry>();
    }

    public bool ExecuteShellMenuCommand(int commandId, IReadOnlyList<string> paths, bool isBackground)
    {
        // 留空：非 Windows 平台不提供系统外壳命令唤起
        return false;
    }
}
