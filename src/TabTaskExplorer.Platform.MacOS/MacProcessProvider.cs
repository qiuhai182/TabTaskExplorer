using System.Diagnostics;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.MacOS;

/// <summary>
/// macOS 平台的进程提供者。
/// 骨架阶段使用标准库枚举进程，后续改为 sysctl 原生实现。
/// </summary>
public class MacProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        List<ProcessInfo> result = new List<ProcessInfo>();
        foreach (Process process in Process.GetProcesses())
        {
            ProcessInfo info = new ProcessInfo();
            try
            {
                info.ProcessId = process.Id;
                info.Name = process.ProcessName;
                info.MemoryBytes = process.WorkingSet64;
            }
            catch
            {
                // 进程已退出或不允许读取详细信息，跳过即可
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
