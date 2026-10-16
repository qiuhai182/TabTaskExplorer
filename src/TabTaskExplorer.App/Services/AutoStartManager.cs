using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 开机启动管理：Windows 通过当前用户注册表 Run 键实现，其他平台留待后续版本。
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AppName = "TabTaskExplorer";

    /// <summary>应用开机启动设置（仅 Windows 生效，其他平台静默跳过）。</summary>
    public static void Apply(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            SetEnabled(enabled);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                return;
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                key.SetValue(AppName, "\"" + executablePath + "\"");
            }
        }
        else
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                key?.DeleteValue(AppName, false);
            }
        }
    }
}
