using System.Diagnostics;
using System.Runtime.Versioning;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 平台的进程提供者：枚举本机进程与结束指定进程。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        List<ProcessInfo> result = new List<ProcessInfo>();
        Process[] processes = Process.GetProcesses();
        foreach (Process process in processes)
        {
            ProcessInfo info = new ProcessInfo();
            try
            {
                info.ProcessId = process.Id;
                info.Name = process.ProcessName;
                info.MemoryBytes = process.WorkingSet64;
                info.WindowTitle = process.MainWindowTitle ?? string.Empty;
            }
            catch
            {
                // 部分系统进程已退出或不允许读取详细信息，跳过即可
                process.Dispose();
                continue;
            }
            result.Add(info);
            process.Dispose();
        }
        return result;
    }

    public bool Terminate(int processId)
    {
        try
        {
            Process target = Process.GetProcessById(processId);
            target.Kill();
            return true;
        }
        catch
        {
            // 进程不存在或没有权限时结束失败
            return false;
        }
    }
}
