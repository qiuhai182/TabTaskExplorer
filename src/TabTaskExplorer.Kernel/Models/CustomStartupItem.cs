using System;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 一条用户自定义启动项：本程序首次启动时按开机标识检查，未在本开机拉起过则自动拉起。
/// 配置随应用设置持久化，运行标记也一并保存，重启本程序后仍能识别本次开机是否已执行。
/// </summary>
public class CustomStartupItem
{
    /// <summary>唯一编号。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>类型标识（程序 / 快捷方式 / 脚本 / 网页链接）。</summary>
    public string ItemType { get; set; } = StartupItemType.Program;

    /// <summary>启动目标：程序路径、快捷方式路径、脚本路径或网页链接。</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>启动参数（没有时为空）。</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>本程序启动后延迟多少秒再拉起（0 表示立即）。</summary>
    public int DelaySeconds { get; set; } = 0;

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>最后一次拉起时所在的开机标识；与本机当前开机标识相同表示本次开机已执行过。</summary>
    public string LastLaunchBootId { get; set; } = string.Empty;

    /// <summary>最后一次拉起时间文字（yyyy-MM-dd HH:mm:ss）。</summary>
    public string LastLaunchTimeText { get; set; } = string.Empty;

    /// <summary>最后一次拉起结果说明。</summary>
    public string LastResult { get; set; } = string.Empty;
}
