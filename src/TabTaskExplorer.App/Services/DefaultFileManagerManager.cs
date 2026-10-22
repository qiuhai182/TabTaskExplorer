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

    /// <summary>应用默认文件管理器设置（仅 Windows 生效，其他平台记录跳过日志）。</summary>
    public static void Apply(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            UiLogger.Log("程序行为", "文件夹接管设置开始执行：目标=" + (enabled ? "启用" : "关闭")
                + " 生效范围=当前用户的注册表 键路径=" + ShellKeyPath);
            SetEnabled(enabled);
        }
        else
        {
            UiLogger.Log("程序行为", "文件夹接管设置：当前操作系统不支持该设置，本次不写注册表"
                + " 操作系统=" + Environment.OSVersion.VersionString + " 设置值=" + enabled);
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
                    UiLogger.Log("程序行为", "文件夹接管设置失败：取不到本程序可执行文件路径，未写入注册表");
                    return;
                }
                string commandText = "\"" + executablePath + "\" \"%V\"";
                shellKey.SetValue(string.Empty, "open");
                using (RegistryKey openKey = shellKey.CreateSubKey("open"))
                using (RegistryKey commandKey = openKey.CreateSubKey("command"))
                {
                    commandKey.SetValue(string.Empty, commandText);
                }
                UiLogger.Log("程序行为", "文件夹接管设置执行完成：结果=已启用"
                    + " 默认动词=open" + " 唤起命令=" + commandText);
            }
            else
            {
                shellKey.DeleteValue(string.Empty, false);
                shellKey.DeleteSubKeyTree("open", false);
                UiLogger.Log("程序行为", "文件夹接管设置执行完成：结果=已关闭，注册表默认动词与 open 子键树已删除");
            }
        }
    }
}
