using System;
using System.Collections.Generic;
using System.Diagnostics;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.MacOS;

/// <summary>
/// macOS 平台的进程提供者。
/// 通过标准库枚举进程；父进程编号一次 ps 调用取全（仅进程采样时执行一次），累计处理器时间用于每进程处理器占用差分。
/// </summary>
public class MacProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        Dictionary<int, int> parentProcessMap = ReadParentProcessIds();
        List<ProcessInfo> result = new List<ProcessInfo>();
        foreach (Process process in Process.GetProcesses())
        {
            ProcessInfo info = new ProcessInfo();
            try
            {
                info.ProcessId = process.Id;
                info.Name = process.ProcessName;
                info.MemoryBytes = process.WorkingSet64;
                info.CpuTimeTicks = process.TotalProcessorTime.Ticks;
                int parentProcessId;
                if (parentProcessMap.TryGetValue(info.ProcessId, out parentProcessId))
                {
                    info.ParentProcessId = parentProcessId;
                }
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

    /// <summary>
    /// 读取全部进程的父子关系：执行一次 ps 列出"进程编号 父进程编号"两列（等号去掉表头行）。
    /// 执行失败时返回空表，此时所有进程按顶层进程平铺展示。
    /// </summary>
    private static Dictionary<int, int> ReadParentProcessIds()
    {
        Dictionary<int, int> parentProcessMap = new Dictionary<int, int>();
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "/bin/ps";
            startInfo.Arguments = "-axo pid=,ppid=";
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            using (Process? psProcess = Process.Start(startInfo))
            {
                if (psProcess == null)
                {
                    return parentProcessMap;
                }
                string outputText = psProcess.StandardOutput.ReadToEnd();
                psProcess.WaitForExit(3000);
                ParsePsOutput(outputText, parentProcessMap);
            }
        }
        catch
        {
            // ps 执行失败（进程已退出或权限受限）时返回空表，按平铺处理
        }
        return parentProcessMap;
    }

    /// <summary>
    /// 解析 ps 输出：每行两列，第一列是进程编号，第二列是父进程编号。
    /// </summary>
    private static void ParsePsOutput(string outputText, Dictionary<int, int> parentProcessMap)
    {
        if (string.IsNullOrEmpty(outputText))
        {
            return;
        }
        string[] lines = outputText.Split(new char[] { '\n' });
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (trimmedLine.Length == 0)
            {
                continue;
            }
            string[] columns = trimmedLine.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2)
            {
                continue;
            }
            int processId;
            int parentProcessId;
            if (!int.TryParse(columns[0], out processId))
            {
                continue;
            }
            if (!int.TryParse(columns[1], out parentProcessId))
            {
                continue;
            }
            if (processId > 0 && parentProcessId > 0 && !parentProcessMap.ContainsKey(processId))
            {
                parentProcessMap.Add(processId, parentProcessId);
            }
        }
    }
}
