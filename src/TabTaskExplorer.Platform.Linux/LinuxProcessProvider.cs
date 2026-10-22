using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Linux;

/// <summary>
/// Linux 平台的进程提供者。
/// 通过标准库枚举进程；父进程编号读取 /proc/[编号]/stat，累计处理器时间用于每进程处理器占用差分。
/// </summary>
public class LinuxProcessProvider : IProcessProvider
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
    /// 读取全部进程的父子关系：遍历 /proc 目录，解析每个进程 stat 文件中的父进程编号。
    /// 读取失败时返回空表，此时所有进程按顶层进程平铺展示。
    /// </summary>
    private static Dictionary<int, int> ReadParentProcessIds()
    {
        Dictionary<int, int> parentProcessMap = new Dictionary<int, int>();
        try
        {
            string[] processDirectories = Directory.GetDirectories("/proc");
            foreach (string processDirectory in processDirectories)
            {
                string directoryName = Path.GetFileName(processDirectory);
                int processId;
                if (!int.TryParse(directoryName, out processId))
                {
                    // /proc 下不全是进程编号目录（还有 cpuinfo、sys 等），不是数字就跳过
                    continue;
                }
                try
                {
                    string statText = File.ReadAllText(Path.Combine(processDirectory, "stat"));
                    int parentProcessId = ParseParentProcessId(statText);
                    if (parentProcessId > 0 && !parentProcessMap.ContainsKey(processId))
                    {
                        parentProcessMap.Add(processId, parentProcessId);
                    }
                }
                catch
                {
                    // 单个进程的 stat 读取失败（已退出或无权限）时跳过
                }
            }
        }
        catch
        {
            // /proc 整体读取失败时返回空表，按平铺处理
        }
        return parentProcessMap;
    }

    /// <summary>
    /// 从 /proc/[编号]/stat 的内容解析父进程编号。
    /// 内容格式：编号 (进程名) 状态 父进程编号 …，进程名可能含空格与右括号，因此从最后一个右括号之后解析。
    /// 解析失败返回 0。
    /// </summary>
    private static int ParseParentProcessId(string statText)
    {
        if (string.IsNullOrEmpty(statText))
        {
            return 0;
        }
        int nameEndIndex = statText.LastIndexOf(')');
        if (nameEndIndex < 0 || nameEndIndex + 1 >= statText.Length)
        {
            return 0;
        }
        string afterName = statText.Substring(nameEndIndex + 1).TrimStart();
        string[] fields = afterName.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
        {
            return 0;
        }
        int parentProcessId;
        if (!int.TryParse(fields[1], out parentProcessId))
        {
            return 0;
        }
        return parentProcessId;
    }
}
