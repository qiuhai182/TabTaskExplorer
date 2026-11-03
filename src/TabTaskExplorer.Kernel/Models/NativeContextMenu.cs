using System.Collections.Generic;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 原生右键菜单里由本程序提供的一条命令：用固定编号与显示名交给平台层，
/// 用户选中后把编号回传给应用层，由应用层执行既有动作。
/// </summary>
public class NativeContextMenuCommand
{
    /// <summary>命令编号（应用层与平台层约定的固定值，取值从 0x8000 起，避开外壳菜单编号段）。</summary>
    public int CommandId { get; set; }

    /// <summary>菜单显示名（纯文本，可带 &amp; 加速键标记）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>是否可用；不可用时按禁用项显示。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>是否为分隔线；为真时忽略命令编号与显示名。</summary>
    public bool IsSeparator { get; set; }

    /// <summary>是否为默认项（原生菜单以粗体显示）。</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// 弹出一次原生右键菜单的请求：目标路径列表、是否空白处、屏幕坐标与本程序命令。
/// </summary>
public class NativeContextMenuRequest
{
    /// <summary>右键目标的完整路径列表（多选时包含全部选中项；空白处菜单传当前目录）。</summary>
    public List<string> Paths { get; set; } = new List<string>();

    /// <summary>true 表示目录空白处菜单，false 表示文件或文件夹条目菜单。</summary>
    public bool IsBackground { get; set; }

    /// <summary>弹出位置（屏幕坐标，像素）。</summary>
    public int ScreenX { get; set; }

    /// <summary>弹出位置（屏幕坐标，像素）。</summary>
    public int ScreenY { get; set; }

    /// <summary>本程序命令列表，按传入顺序显示在系统项之前。</summary>
    public List<NativeContextMenuCommand> Commands { get; set; } = new List<NativeContextMenuCommand>();
}

/// <summary>
/// 原生右键菜单的弹出结果：区分“未弹出（需回退）”“用户选了本程序命令”“已唤起系统/扩展命令”“用户取消”。
/// </summary>
public class NativeContextMenuResult
{
    /// <summary>当前平台是否支持原生外壳右键菜单。</summary>
    public bool Supported { get; set; }

    /// <summary>菜单是否成功弹出；为 false 时调用方应退回自有菜单。</summary>
    public bool MenuShown { get; set; }

    /// <summary>用户选择的本程序命令编号；未选择本程序命令时为 -1。</summary>
    public int OwnCommandId { get; set; } = -1;

    /// <summary>是否已把选择交给系统外壳菜单（静态动词或第三方扩展）唤起。</summary>
    public bool ShellCommandInvoked { get; set; }

    /// <summary>失败或降级原因（纯中文，用于日志）。</summary>
    public string Message { get; set; } = string.Empty;
}
