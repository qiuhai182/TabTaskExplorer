using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 启动项拉起器：按类型选择系统外壳或脚本解释器拉起目标。
/// 程序、快捷方式与网页链接走系统外壳（与资源管理器双击等价）；
/// 脚本按扩展名选择解释器，保证 .ps1、.sh、.py 等不会因为文件关联缺失而静默失败。
/// </summary>
public static class StartupItemLauncher
{
    /// <summary>拉起一条启动项。</summary>
    public static StartupLaunchResult Launch(string itemType, string target, string arguments)
    {
        StartupLaunchResult result = new StartupLaunchResult();
        if (string.IsNullOrWhiteSpace(target))
        {
            result.Success = false;
            result.Message = "启动目标为空";
            return result;
        }
        string trimmedTarget = target.Trim();
        if (itemType == StartupItemType.WebLink)
        {
            return LaunchWebLink(trimmedTarget);
        }
        if (itemType == StartupItemType.Script)
        {
            return LaunchScript(trimmedTarget, arguments == null ? string.Empty : arguments);
        }
        return LaunchShellTarget(trimmedTarget, arguments == null ? string.Empty : arguments);
    }

    /// <summary>拉起网页链接：缺少协议头时补 https://，交给系统默认浏览器。</summary>
    private static StartupLaunchResult LaunchWebLink(string target)
    {
        string address = target;
        if (!address.Contains("://", StringComparison.Ordinal)
            && !address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            && !address.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase))
        {
            address = "https://" + address;
        }
        return StartShellProcess(address, string.Empty);
    }

    /// <summary>拉起程序、快捷方式或普通文件：交给系统外壳。</summary>
    private static StartupLaunchResult LaunchShellTarget(string target, string arguments)
    {
        return StartShellProcess(target, arguments);
    }

    /// <summary>按脚本扩展名选择解释器拉起；解释器不存在时返回失败原因。</summary>
    private static StartupLaunchResult LaunchScript(string target, string arguments)
    {
        string extension = Path.GetExtension(target).ToLowerInvariant();
        if (extension == ".ps1")
        {
            string executable = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            string scriptArguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(target) + AppendArguments(arguments);
            return StartDirectProcess(executable, scriptArguments, Directory.GetParent(target)?.FullName);
        }
        if (extension == ".bat" || extension == ".cmd")
        {
            if (!OperatingSystem.IsWindows())
            {
                StartupLaunchResult unsupported = new StartupLaunchResult();
                unsupported.Success = false;
                unsupported.Message = "批处理脚本只能在 Windows 上运行：" + target;
                return unsupported;
            }
            string batchArguments = "/c " + Quote(target) + AppendArguments(arguments);
            return StartDirectProcess("cmd.exe", batchArguments, Directory.GetParent(target)?.FullName);
        }
        if (extension == ".sh")
        {
            return StartDirectProcess("/bin/sh", Quote(target) + AppendArguments(arguments), Directory.GetParent(target)?.FullName);
        }
        if (extension == ".py")
        {
            string executable = OperatingSystem.IsWindows() ? "python" : "python3";
            return StartDirectProcess(executable, Quote(target) + AppendArguments(arguments), Directory.GetParent(target)?.FullName);
        }
        if (extension == ".js")
        {
            return StartDirectProcess("node", Quote(target) + AppendArguments(arguments), Directory.GetParent(target)?.FullName);
        }
        if (extension == ".vbs")
        {
            return StartDirectProcess("cscript.exe", "//nologo " + Quote(target) + AppendArguments(arguments), Directory.GetParent(target)?.FullName);
        }
        // 其他脚本交给系统外壳（例如双击可执行的 .jar、.psm1 关联程序）
        return StartShellProcess(target, arguments);
    }

    /// <summary>通过系统外壳拉起（UseShellExecute 为真），适用于程序、快捷方式与网页链接。</summary>
    private static StartupLaunchResult StartShellProcess(string fileName, string arguments)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments == null ? string.Empty : arguments;
            startInfo.UseShellExecute = true;
            if (File.Exists(fileName))
            {
                startInfo.WorkingDirectory = Path.GetDirectoryName(fileName) ?? string.Empty;
            }
            Process? process = Process.Start(startInfo);
            StartupLaunchResult result = new StartupLaunchResult();
            result.Success = true;
            result.ProcessId = ReadProcessId(process);
            result.Message = process == null
                ? "已交给系统外壳打开：" + fileName
                : "已启动，进程号=" + result.ProcessId + " 目标=" + fileName;
            return result;
        }
        catch (Exception error)
        {
            StartupLaunchResult result = new StartupLaunchResult();
            result.Success = false;
            result.Message = "拉起失败：" + error.Message + " 目标=" + fileName;
            return result;
        }
    }

    /// <summary>直接创建进程（UseShellExecute 为假），用于指定解释器的脚本。</summary>
    private static StartupLaunchResult StartDirectProcess(string fileName, string arguments, string? workingDirectory)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }
            Process? process = Process.Start(startInfo);
            StartupLaunchResult result = new StartupLaunchResult();
            result.Success = true;
            result.ProcessId = ReadProcessId(process);
            result.Message = "已启动，进程号=" + result.ProcessId + " 解释器=" + fileName;
            return result;
        }
        catch (Exception error)
        {
            StartupLaunchResult result = new StartupLaunchResult();
            result.Success = false;
            result.Message = "拉起失败：" + error.Message + " 解释器=" + fileName;
            return result;
        }
    }

    /// <summary>读取进程号；进程已退出或外壳未返回进程时返回 0，不影响已提交的拉起结果。</summary>
    private static int ReadProcessId(Process? process)
    {
        if (process == null)
        {
            return 0;
        }
        try
        {
            return process.Id;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>把参数追加到命令行；参数为空时不追加空格。</summary>
    private static string AppendArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }
        return " " + arguments.Trim();
    }

    /// <summary>给路径加双引号，路径内部的双引号先做转义。</summary>
    private static string Quote(string path)
    {
        return "\"" + path.Replace("\"", "\\\"") + "\"";
    }
}
