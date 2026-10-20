using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 开机启动管理：Windows 注册表 Run 键、Linux XDG autostart、macOS LaunchAgent。
/// 每次应用后都会回读校验，返回系统真实生效结果，便于把结果回写配置。
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AppName = "TabTaskExplorer";

    /// <summary>
    /// 应用开机启动设置，按当前操作系统选择实现。
    /// 返回 true 表示系统已按期望状态生效；返回 false 表示写入失败或校验不一致。
    /// </summary>
    public static bool Apply(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            return ApplyWindows(enabled);
        }
        if (OperatingSystem.IsLinux())
        {
            return ApplyLinux(enabled);
        }
        if (OperatingSystem.IsMacOS())
        {
            return ApplyMac(enabled);
        }
        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool ApplyWindows(bool enabled)
    {
        try
        {
            string? executablePath = Environment.ProcessPath;
            if (enabled && string.IsNullOrEmpty(executablePath))
            {
                return false;
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                {
                    // 始终写入当前可执行文件路径：程序位置变化后重建条目，避免指向已失效的旧路径
                    string expected = "\"" + executablePath + "\"";
                    key.SetValue(AppName, expected, RegistryValueKind.String);
                    object? actual = key.GetValue(AppName);
                    return actual != null && string.Equals(actual.ToString(), expected, StringComparison.OrdinalIgnoreCase);
                }
                key.DeleteValue(AppName, false);
                return key.GetValue(AppName) == null;
            }
        }
        catch
        {
            // 注册表不可写时返回失败，由调用方决定是否回写配置
            return false;
        }
    }

    private static bool ApplyLinux(bool enabled)
    {
        try
        {
            string autostartDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart");
            string desktopFilePath = Path.Combine(autostartDirectory, "tabtaskexplorer.desktop");
            if (enabled)
            {
                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executablePath))
                {
                    return false;
                }
                Directory.CreateDirectory(autostartDirectory);
                string content = "[Desktop Entry]\nType=Application\nName=TabTaskExplorer\nExec=" + executablePath + "\nX-GNOME-Autostart-enabled=true\n";
                File.WriteAllText(desktopFilePath, content);
                return File.Exists(desktopFilePath);
            }
            if (File.Exists(desktopFilePath))
            {
                File.Delete(desktopFilePath);
            }
            return !File.Exists(desktopFilePath);
        }
        catch
        {
            // 目录不可写时返回失败，由调用方决定是否回写配置
            return false;
        }
    }

    private static bool ApplyMac(bool enabled)
    {
        try
        {
            string agentsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents");
            string plistFilePath = Path.Combine(agentsDirectory, "com.tabtaskexplorer.plist");
            if (enabled)
            {
                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executablePath))
                {
                    return false;
                }
                Directory.CreateDirectory(agentsDirectory);
                string content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                    + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
                    + "<plist version=\"1.0\">\n<dict>\n"
                    + "<key>Label</key><string>com.tabtaskexplorer</string>\n"
                    + "<key>ProgramArguments</key>\n<array>\n<string>" + executablePath + "</string>\n</array>\n"
                    + "<key>RunAtLoad</key><true/>\n"
                    + "</dict>\n</plist>\n";
                File.WriteAllText(plistFilePath, content);
                return File.Exists(plistFilePath);
            }
            if (File.Exists(plistFilePath))
            {
                File.Delete(plistFilePath);
            }
            return !File.Exists(plistFilePath);
        }
        catch
        {
            // 目录不可写时返回失败，由调用方决定是否回写配置
            return false;
        }
    }
}
