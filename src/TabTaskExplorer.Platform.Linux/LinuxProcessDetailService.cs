using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Linux;

/// <summary>
/// Linux 平台的进程详情与调节服务：
/// 详情读 /proc —— 可执行路径读 exe 符号链接、命令行读 cmdline、启动时间由 btime 加 stat 推算、
/// 用户名读 status 对照 /etc/passwd、优先级读 stat 的 nice 值、CPU 亲和性读 sched_getaffinity；
/// 优先级用 setpriority 调整 nice（实时档用 sched_setscheduler 设 SCHED_FIFO），
/// CPU 亲和性用 sched_setaffinity 写掩码。参考 htop 与任务管理器的取值方式。
/// </summary>
public class LinuxProcessDetailService : IProcessDetailService
{
    /// <summary>Linux 平台支持详情查询与调节。</summary>
    public bool IsSupported
    {
        get { return true; }
    }

    /// <summary>查询指定进程的详情；进程不存在或读取失败时返回 null。</summary>
    public ProcessDetail? GetDetail(int processId)
    {
        string processDirectory = "/proc/" + processId;
        if (!Directory.Exists(processDirectory))
        {
            // 进程已退出（或 hidepid 限制看不到该目录）
            return null;
        }
        ProcessDetail detail = new ProcessDetail();
        detail.ProcessId = processId;
        detail.ExecutablePath = ReadExecutablePath(processDirectory);
        detail.CommandLine = LinuxProcessProvider.ReadCommandLine(processDirectory);
        detail.AffinityMask = ReadAffinityMask(processId);

        ProcessInfo statInfo = new ProcessInfo();
        statInfo.ProcessId = processId;
        bool statAccessDenied;
        if (LinuxProcessProvider.TryReadProcStat(
                processDirectory, statInfo, LinuxProcessProvider.ReadBootTimeSeconds(), out statAccessDenied))
        {
            detail.StartTimeText = statInfo.StartTime.HasValue
                ? statInfo.StartTime.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : string.Empty;
            detail.PriorityLevel = statInfo.PriorityLevel;
        }
        detail.UserName = LinuxProcessProvider.ReadProcessUserName(
            processDirectory, LinuxProcessProvider.ReadUserNames());
        return detail;
    }

    /// <summary>设置指定进程的优先级级别；失败返回 false（权限不足时按失败如实上报）。</summary>
    public bool SetPriorityLevel(int processId, ProcessPriorityLevel level)
    {
        if (level == ProcessPriorityLevel.Realtime)
        {
            // 实时档：用 SCHED_FIFO 策略（优先级 1），没有 CAP_SYS_NICE 权限时失败
            SchedParam realtimeParam = new SchedParam();
            realtimeParam.SchedPriority = 1;
            return sched_setscheduler(processId, SchedFifo, ref realtimeParam) == 0;
        }
        // 从实时策略切回普通调度：本来就不在实时策略上时该调用失败，可忽略
        SchedParam normalParam = new SchedParam();
        normalParam.SchedPriority = 0;
        sched_setscheduler(processId, SchedOther, ref normalParam);
        int niceValue = NiceValueFor(level);
        return setpriority(PrioProcess, processId, niceValue) == 0;
    }

    /// <summary>设置指定进程的 CPU 亲和性位掩码；失败返回 false。</summary>
    public bool SetProcessorAffinity(int processId, long affinityMask)
    {
        if (affinityMask == -1 || affinityMask == 0)
        {
            // 掩码无效（-1 表示不支持，0 表示没有任何核心）
            return false;
        }
        try
        {
            byte[] cpuSet = new byte[AffinityByteCount];
            for (int processorIndex = 0; processorIndex < 64; processorIndex++)
            {
                if ((affinityMask & (1L << processorIndex)) != 0)
                {
                    cpuSet[processorIndex / 8] |= (byte)(1 << (processorIndex % 8));
                }
            }
            return sched_setaffinity(processId, new UIntPtr((uint)AffinityByteCount), cpuSet) == 0;
        }
        catch
        {
            // 进程已退出或没有权限
            return false;
        }
    }

    /// <summary>把内核优先级档位映射为 nice 值（与读取时的映射互逆）；实时档由调用方单独处理。</summary>
    private static int NiceValueFor(ProcessPriorityLevel level)
    {
        if (level == ProcessPriorityLevel.Idle)
        {
            return 19;
        }
        if (level == ProcessPriorityLevel.BelowNormal)
        {
            return 10;
        }
        if (level == ProcessPriorityLevel.AboveNormal)
        {
            return -5;
        }
        if (level == ProcessPriorityLevel.High)
        {
            return -10;
        }
        // 标准档
        return 0;
    }

    /// <summary>
    /// 读取 CPU 亲和性掩码：sched_getaffinity 返回允许运行的处理器位图（第 n 位代表第 n 个处理器）；
    /// 读取失败返回 -1（界面显示"不支持"）。
    /// </summary>
    private static long ReadAffinityMask(int processId)
    {
        try
        {
            byte[] cpuSet = new byte[AffinityByteCount];
            if (sched_getaffinity(processId, new UIntPtr((uint)AffinityByteCount), cpuSet) != 0)
            {
                return -1;
            }
            long mask = 0;
            for (int byteIndex = 0; byteIndex < AffinityByteCount; byteIndex++)
            {
                mask |= ((long)cpuSet[byteIndex]) << (8 * byteIndex);
            }
            return mask;
        }
        catch
        {
            // 进程已退出或没有权限
            return -1;
        }
    }

    /// <summary>读取可执行文件完整路径：优先解析 /proc/[编号]/exe 符号链接，失败时退回命令行第一个参数。</summary>
    private static string ReadExecutablePath(string processDirectory)
    {
        try
        {
            FileSystemInfo? linkTarget = File.ResolveLinkTarget(Path.Combine(processDirectory, "exe"), true);
            if (linkTarget != null && linkTarget.FullName.Length > 0)
            {
                return linkTarget.FullName;
            }
        }
        catch
        {
            // 内核线程没有 exe 链接，或进程已退出：退回命令行
        }
        string commandLine = LinuxProcessProvider.ReadCommandLine(processDirectory);
        if (commandLine.Length == 0)
        {
            return string.Empty;
        }
        string[] parts = commandLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    /// <summary>进程调度策略编号：普通分时调度（SCHED_OTHER）。</summary>
    private const int SchedOther = 0;

    /// <summary>进程调度策略编号：先进先出实时调度（SCHED_FIFO）。</summary>
    private const int SchedFifo = 1;

    /// <summary>setpriority 的作用对象类型：按进程（PRIO_PROCESS）。</summary>
    private const int PrioProcess = 0;

    /// <summary>亲和性位图的字节数：支持最多 64 个逻辑处理器。</summary>
    private const int AffinityByteCount = 8;

    /// <summary>实时调度参数结构（sched_setscheduler 使用）。</summary>
    private struct SchedParam
    {
        public int SchedPriority;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int setpriority(int which, int who, int priority);

    [DllImport("libc", SetLastError = true)]
    private static extern int sched_setscheduler(int processId, int policy, ref SchedParam param);

    [DllImport("libc", SetLastError = true)]
    private static extern int sched_getaffinity(int processId, UIntPtr cpuSetSize, byte[] cpuSet);

    [DllImport("libc", SetLastError = true)]
    private static extern int sched_setaffinity(int processId, UIntPtr cpuSetSize, byte[] cpuSet);
}
