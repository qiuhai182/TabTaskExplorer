using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 开机启动管理：Windows 注册表 Run 键、Linux XDG autostart、macOS LaunchAgent。
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AppName = "TabTaskExplorer";

    /// <summary>应用开机启动设置，按当前操作系统选择实现。</summary>
    public static void Apply(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindows(enabled);
        }
        else if (OperatingSystem.IsLinux())
        {
            ApplyLinux(enabled);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ApplyMac(enabled);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows(bool enabled)
    {
        try
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
        catch
        {
            // 注册表不可写时静默跳过
        }
    }

    private static void ApplyLinux(bool enabled)
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
                    return;
                }
                Directory.CreateDirectory(autostartDirectory);
                string content = "[Desktop Entry]\nType=Application\nName=TabTaskExplorer\nExec=" + executablePath + "\nX-GNOME-Autostart-enabled=true\n";
                File.WriteAllText(desktopFilePath, content);
            }
            else if (File.Exists(desktopFilePath))
            {
                File.Delete(desktopFilePath);
            }
        }
        catch
        {
            // 目录不可写时静默跳过
        }
    }

    private static void ApplyMac(bool enabled)
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
                    return;
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
            }
            else if (File.Exists(plistFilePath))
            {
                File.Delete(plistFilePath);
            }
        }
        catch
        {
            // 目录不可写时静默跳过
        }
    }
}
