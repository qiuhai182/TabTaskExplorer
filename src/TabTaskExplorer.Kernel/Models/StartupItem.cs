using System;
using System.Collections.Generic;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>启动项来源：系统自带的开机自启机制。</summary>
public enum StartupItemSource
{
    /// <summary>来源未知。</summary>
    Unknown = 0,

    /// <summary>Windows 注册表 Run / RunOnce 键。</summary>
    RegistryRun = 1,

    /// <summary>Windows 启动文件夹。</summary>
    StartupFolder = 2,

    /// <summary>系统服务：Windows 服务或 Linux systemd 单元。</summary>
    Service = 3,

    /// <summary>Linux XDG autostart 桌面文件。</summary>
    AutoStartFile = 4,

    /// <summary>macOS launchd 代理或守护进程 plist。</summary>
    LaunchAgent = 5,

    /// <summary>Windows 计划任务（开机触发或登录触发）。</summary>
    ScheduledTask = 6,
}

/// <summary>启动项类型常量。</summary>
public static class StartupItemType
{
    public const string Program = "Program";
    public const string Shortcut = "Shortcut";
    public const string Script = "Script";
    public const string WebLink = "WebLink";
    public const string Service = "Service";

    /// <summary>把类型标识转成界面文字。</summary>
    public static string Describe(string itemType)
    {
        if (itemType == Shortcut)
        {
            return "快捷方式";
        }
        if (itemType == Script)
        {
            return "脚本";
        }
        if (itemType == WebLink)
        {
            return "网页链接";
        }
        if (itemType == Service)
        {
            return "服务";
        }
        return "程序";
    }

    /// <summary>把下拉框下标转成类型标识。</summary>
    public static string FromIndex(int index)
    {
        if (index == 1)
        {
            return Shortcut;
        }
        if (index == 2)
        {
            return Script;
        }
        if (index == 3)
        {
            return WebLink;
        }
        return Program;
    }

    /// <summary>把类型标识转成下拉框下标。</summary>
    public static int ToIndex(string itemType)
    {
        if (itemType == Shortcut)
        {
            return 1;
        }
        if (itemType == Script)
        {
            return 2;
        }
        if (itemType == WebLink)
        {
            return 3;
        }
        return 0;
    }
}

/// <summary>启动项作用范围常量。</summary>
public static class StartupItemScope
{
    public const string CurrentUser = "CurrentUser";
    public const string AllUsers = "AllUsers";
    public const string System = "System";

    /// <summary>把范围标识转成界面文字。</summary>
    public static string Describe(string scope)
    {
        if (scope == AllUsers)
        {
            return "所有用户";
        }
        if (scope == System)
        {
            return "系统";
        }
        return "当前用户";
    }
}

/// <summary>一条系统启动项：由平台适配层枚举得到，应用层只负责展示与调用启停。</summary>
public class StartupItem
{
    /// <summary>稳定编号，用于启停与网页端回传定位。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>来源类型。</summary>
    public StartupItemSource Source { get; set; } = StartupItemSource.Unknown;

    /// <summary>类型标识（程序 / 快捷方式 / 脚本 / 网页链接 / 服务）。</summary>
    public string ItemType { get; set; } = StartupItemType.Program;

    /// <summary>启动目标：可执行文件、文件路径、服务映像或网页链接。</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>启动参数（没有时为空）。</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>来源位置：注册表键路径、文件路径或服务名称。</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>作用范围（当前用户 / 所有用户 / 系统）。</summary>
    public string Scope { get; set; } = StartupItemScope.CurrentUser;

    /// <summary>当前是否启用。</summary>
    public bool Enabled { get; set; }

    /// <summary>是否允许从本程序修改启停状态。</summary>
    public bool CanToggle { get; set; } = true;

    /// <summary>不可修改时给出的原因（例如需要管理员权限）；可修改时为空。</summary>
    public string ToggleHint { get; set; } = string.Empty;

    /// <summary>补充说明（服务描述、注册表值名称等）。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否为本程序自身的开机自启项：启停时应用层同步更新「开机启动」配置，
    /// 避免下次程序启动时按旧配置重新写回系统启动项。
    /// </summary>
    public bool IsSelfAutoStart { get; set; }
}
