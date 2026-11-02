using System;

namespace TabTaskExplorer.Kernel.Capture;

/// <summary>
/// 全局快捷键服务：在系统任何程序处于前台时都能触发指定动作。
/// Windows 通过 RegisterHotKey 注册线程级全局热键；Linux / macOS 暂不提供，注册返回失败并降级为应用内使用。
/// </summary>
public interface IGlobalHotkeyService
{
    /// <summary>当前平台是否支持全局快捷键。</summary>
    bool IsSupported { get; }

    /// <summary>
    /// 注册一个全局快捷键：动作标识仅用于日志与回调匹配，实际按键由主键名称与修饰键组成。
    /// </summary>
    /// <param name="actionName">动作标识（例如 ScreenshotRegion）。</param>
    /// <param name="keyText">主键名称（Avalonia Key 枚举名，例如 PrintScreen、F9、A）。</param>
    /// <param name="ctrl">是否需要控制键。</param>
    /// <param name="alt">是否需要替换键。</param>
    /// <param name="shift">是否需要上档键。</param>
    /// <param name="callback">触发回调（在后台消息线程调用，调用方需自行切换到界面线程）。</param>
    /// <returns>注册成功返回 true；被占用或平台不支持返回 false。</returns>
    bool Register(string actionName, string keyText, bool ctrl, bool alt, bool shift, Action callback);

    /// <summary>注销全部已注册的全局快捷键。</summary>
    void UnregisterAll();
}
