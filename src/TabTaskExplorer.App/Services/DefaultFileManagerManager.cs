using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 默认文件夹打开方式管理：Windows 通过当前用户注册表把文件夹的打开命令指向本程序。
/// 仅影响当前用户，可随时关闭恢复系统默认。
/// </summary>
public static class DefaultFileManagerManager
{
    private const string ShellKeyPath = "Software\\Classes\\Directory\\shell";

    /// <summary>应用默认文件管理器设置（仅 Windows 生效，其他平台静默跳过）。</summary>
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
        using (RegistryKey shellKey = Registry.CurrentUser.CreateSubKey(ShellKeyPath))
        {
            if (enabled)
            {
                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executablePath))
                {
                    return;
                }
                shellKey.SetValue(string.Empty, "open");
                using (RegistryKey openKey = shellKey.CreateSubKey("open"))
                using (RegistryKey commandKey = openKey.CreateSubKey("command"))
                {
                    commandKey.SetValue(string.Empty, "\"" + executablePath + "\" \"%V\"");
                }
            }
            else
            {
                shellKey.DeleteValue(string.Empty, false);
                shellKey.DeleteSubKeyTree("open", false);
            }
        }
    }
}
