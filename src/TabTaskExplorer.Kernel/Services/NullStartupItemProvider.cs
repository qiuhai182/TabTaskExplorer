using System;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>不支持系统启动项管理的平台实现：返回空清单，启停一律失败。</summary>
public class NullStartupItemProvider : TabTaskExplorer.Kernel.Abstractions.IStartupItemProvider
{
    /// <inheritdoc />
    public bool IsSupported
    {
        get { return false; }
    }

    /// <inheritdoc />
    public string PlatformText
    {
        get { return "当前平台不支持系统启动项管理"; }
    }

    /// <inheritdoc />
    public System.Collections.Generic.IReadOnlyList<TabTaskExplorer.Kernel.Models.StartupItem> Enumerate()
    {
        return new System.Collections.Generic.List<TabTaskExplorer.Kernel.Models.StartupItem>();
    }

    /// <inheritdoc />
    public bool SetEnabled(TabTaskExplorer.Kernel.Models.StartupItem item, bool enabled)
    {
        return false;
    }
}
