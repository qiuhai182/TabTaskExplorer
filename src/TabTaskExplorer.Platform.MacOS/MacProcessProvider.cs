using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.MacOS;

/// <summary>
/// macOS 平台的进程提供者。
/// 通过标准库枚举进程并补齐内存、累计处理器时间与启动时间；
/// 父进程编号、优先级、线程数、用户名与命令行一次 ps 调用取全（仅进程采样时执行一次），
/// ps 列数按版本降级重试，累计处理器时间用于每进程处理器占用差分。
/// </summary>
public class MacProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        Dictionary<int, PsProcessInfo> psProcessMap = ReadPsProcessInfo();
        List<ProcessInfo> result = new List<ProcessInfo>();
        foreach (Process process in Process.GetProcesses())
        {
            ProcessInfo info = new ProcessInfo();
            try
            {
                info.ProcessId = process.Id;
            }
            catch
            {
                // 进程已退出，跳过即可
                process.Dispose();
                continue;
            }
            try
            {
                info.Name = process.ProcessName;
            }
            catch
            {
                info.IsAccessDenied = true;
            }
            try
            {
                info.MemoryBytes = process.WorkingSet64;
            }
            catch
            {
                info.IsAccessDenied = true;
            }
            try
            {
                info.CpuTimeTicks = process.TotalProcessorTime.Ticks;
            }
            catch
            {
                info.IsAccessDenied = true;
            }
            try
            {
                info.StartTime = process.StartTime;
            }
            catch
            {
                info.StartTime = null;
            }
            if (psProcessMap.TryGetValue(info.ProcessId, out PsProcessInfo? psInfo))
            {
                info.ParentProcessId = psInfo.ParentProcessId;
                info.PriorityLevel = psInfo.PriorityLevel;
                info.ThreadCount = psInfo.ThreadCount;
                info.UserName = psInfo.UserName;
                info.CommandLine = psInfo.CommandLine;
                info.ExecutablePath = ReadExecutablePath(psInfo.CommandLine);
            }
            if (info.Name.Length == 0 && info.ExecutablePath.Length > 0)
            {
                // 取不到进程名时用命令行里的映像名兜底
                info.Name = Path.GetFileNameWithoutExtension(info.ExecutablePath);
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
    /// 依次用完整列、缺线程数列、只有父子关系列三种参数执行 ps，取第一个成功的结果。
    /// 老版本 ps 没有 nlwp 列时会降级，全失败时返回空表（此时父进程按顶层平铺）。
    /// </summary>
    private static Dictionary<int, PsProcessInfo> ReadPsProcessInfo()
    {
        Dictionary<int, PsProcessInfo> psProcessMap;
        if (TryReadPsProcesses("-axo pid=,ppid=,ni=,nlwp=,user=,args=", PsColumnLayout.Full, out psProcessMap))
        {
            return psProcessMap;
        }
        if (TryReadPsProcesses("-axo pid=,ppid=,ni=,user=,args=", PsColumnLayout.WithoutThreadCount, out psProcessMap))
        {
            return psProcessMap;
        }
        TryReadPsProcesses("-axo pid=,ppid=", PsColumnLayout.ParentOnly, out psProcessMap);
        return psProcessMap;
    }

    /// <summary>
    /// 用指定参数执行一次 ps 并解析；进程启动失败、超时、非零退出码或解析不到任何进程都算失败。
    /// </summary>
    private static bool TryReadPsProcesses(string arguments, PsColumnLayout layout, out Dictionary<int, PsProcessInfo> psProcessMap)
    {
        psProcessMap = new Dictionary<int, PsProcessInfo>();
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "/bin/ps";
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            using Process? psProcess = Process.Start(startInfo);
            if (psProcess == null)
            {
                return false;
            }
            string outputText = psProcess.StandardOutput.ReadToEnd();
            if (!psProcess.WaitForExit(3000))
            {
                // ps 卡住：不采信这份输出
                return false;
            }
            if (psProcess.ExitCode != 0 || outputText.Trim().Length == 0)
            {
                return false;
            }
            ParsePsOutput(outputText, layout, psProcessMap);
            return psProcessMap.Count > 0;
        }
        catch
        {
            // ps 执行失败（进程已退出或权限受限）
            return false;
        }
    }

    /// <summary>
    /// 解析 ps 输出：每行至少两列，第一列是进程编号，第二列是父进程编号；
    /// 其余列按 PsColumnLayout 指定的列序取 nice 值、线程数、用户名与命令行。
    /// </summary>
    private static void ParsePsOutput(string outputText, PsColumnLayout layout, Dictionary<int, PsProcessInfo> psProcessMap)
    {
        if (string.IsNullOrEmpty(outputText))
        {
            return;
        }
        int requiredColumnCount = layout switch
        {
            PsColumnLayout.WithoutThreadCount => 4,
            PsColumnLayout.Full => 5,
            _ => 2,
        };
        string[] lines = outputText.Split(new char[] { '\n' });
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (trimmedLine.Length == 0)
            {
                continue;
            }
            string[] columns = trimmedLine.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int processId;
            int parentProcessId;
            if (columns.Length < requiredColumnCount
                || !int.TryParse(columns[0], out processId)
                || !int.TryParse(columns[1], out parentProcessId)
                || processId <= 0
                || psProcessMap.ContainsKey(processId))
            {
                continue;
            }
            PsProcessInfo psInfo = new PsProcessInfo();
            psInfo.ParentProcessId = parentProcessId;
            if (layout != PsColumnLayout.ParentOnly)
            {
                int niceValue;
                if (int.TryParse(columns[2], out niceValue))
                {
                    psInfo.PriorityLevel = ToPriorityLevel(niceValue);
                }
                if (layout == PsColumnLayout.Full)
                {
                    int threadCount;
                    if (int.TryParse(columns[3], out threadCount))
                    {
                        psInfo.ThreadCount = threadCount;
                    }
                    psInfo.UserName = columns[4];
                    psInfo.CommandLine = JoinColumns(columns, 5);
                }
                else
                {
                    psInfo.UserName = columns[3];
                    psInfo.CommandLine = JoinColumns(columns, 4);
                }
            }
            psProcessMap.Add(processId, psInfo);
        }
    }

    /// <summary>把从 startIndex 开始的列用空格重新拼回命令行；没有可拼的列时返回空字符串。</summary>
    private static string JoinColumns(string[] columns, int startIndex)
    {
        if (startIndex >= columns.Length)
        {
            return string.Empty;
        }
        return string.Join(" ", columns, startIndex, columns.Length - startIndex);
    }

    /// <summary>把 ps 的 nice 值映射为内核优先级级别。</summary>
    private static ProcessPriorityLevel? ToPriorityLevel(int niceValue)
    {
        if (niceValue >= 15)
        {
            return ProcessPriorityLevel.Idle;
        }
        if (niceValue >= 1)
        {
            return ProcessPriorityLevel.BelowNormal;
        }
        if (niceValue <= -10)
        {
            return ProcessPriorityLevel.High;
        }
        if (niceValue < 0)
        {
            return ProcessPriorityLevel.AboveNormal;
        }
        return ProcessPriorityLevel.Normal;
    }

    /// <summary>从命令行取第一个空格分隔的片段作为可执行路径；命令行为空时返回空字符串。</summary>
    private static string ReadExecutablePath(string commandLine)
    {
        if (commandLine.Length == 0)
        {
            return string.Empty;
        }
        string[] parts = commandLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    /// <summary>ps 输出的列组合方式（老版本 ps 缺列时逐级降级）。</summary>
    private enum PsColumnLayout
    {
        /// <summary>pid、ppid、ni、nlwp、user、args 六段（完整列）。</summary>
        Full,

        /// <summary>pid、ppid、ni、user、args 五段（ps 不认识 nlwp 列时）。</summary>
        WithoutThreadCount,

        /// <summary>pid、ppid 两段（ps 连 nice 列都不支持时）。</summary>
        ParentOnly,
    }

    /// <summary>ps 一次调用能取到、且不依赖 Process 类的进程字段。</summary>
    private class PsProcessInfo
    {
        public int ParentProcessId { get; set; }

        public ProcessPriorityLevel? PriorityLevel { get; set; }

        public int ThreadCount { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string CommandLine { get; set; } = string.Empty;
    }
}
