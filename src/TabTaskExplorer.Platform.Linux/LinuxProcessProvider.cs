using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Linux;

/// <summary>
/// Linux 平台的进程提供者。
/// 直接遍历 /proc 目录：stat 提供名称、父进程编号、累计处理器时间、优先级、线程数、启动时间与常驻内存，
/// cmdline 提供命令行，status 提供用户名，io 提供读写字节数；
/// 累计处理器时间用于每进程处理器占用差分，启动时间用于首轮占用估算。
/// </summary>
public class LinuxProcessProvider : IProcessProvider
{
    /// <summary>系统时钟频率：/proc/stat 里的 CPU 时间以每秒 100 个时钟节拍计。</summary>
    private const double ClockTicksPerSecond = 100.0;

    /// <summary>一个时钟节拍折算成 DateTime 的 Tick 数（10^9 ÷ 100）。</summary>
    private const long Nanoseconds100PerClockTick = 100000L;

    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        long bootTimeSeconds = ReadBootTimeSeconds();
        Dictionary<long, string> userNameByUserId = ReadUserNames();
        List<ProcessInfo> result = new List<ProcessInfo>();
        string[] processDirectories;
        try
        {
            processDirectories = Directory.GetDirectories("/proc");
        }
        catch
        {
            // /proc 整体读取失败时返回空表
            return result;
        }
        foreach (string processDirectory in processDirectories)
        {
            if (!int.TryParse(Path.GetFileName(processDirectory), out int processId))
            {
                // /proc 下不全是进程编号目录（还有 cpuinfo、sys 等），不是数字就跳过
                continue;
            }
            ProcessInfo info = new ProcessInfo();
            info.ProcessId = processId;
            bool statAccessDenied;
            if (!TryReadProcStat(processDirectory, info, bootTimeSeconds, out statAccessDenied))
            {
                if (statAccessDenied)
                {
                    // 读得到目录但 stat 被拒绝：整行保留并标记"拒绝访问"
                    info.IsAccessDenied = true;
                    result.Add(info);
                }
                continue;
            }
            info.CommandLine = ReadCommandLine(processDirectory);
            info.ExecutablePath = ReadExecutablePath(info.CommandLine);
            info.UserName = ReadProcessUserName(processDirectory, userNameByUserId);
            bool ioAccessDenied;
            if (!TryReadIoBytes(processDirectory, info, out ioAccessDenied) & ioAccessDenied)
            {
                info.IsAccessDenied = true;
            }
            result.Add(info);
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
    /// 解析 /proc/[编号]/stat 并填入进程信息。
    /// 内容格式：编号 (进程名) 状态 父进程编号 …，进程名可能含空格与右括号，
    /// 因此先用左右括号夹出进程名，再从最后一个右括号之后按空白切字段（切出的第 0 个字段是状态）。
    /// 无权限读取时置 accessDenied 并返回 false，其他失败只返回 false。
    /// </summary>
    internal static bool TryReadProcStat(string processDirectory, ProcessInfo info, long bootTimeSeconds, out bool accessDenied)
    {
        accessDenied = false;
        string statText;
        try
        {
            statText = File.ReadAllText(Path.Combine(processDirectory, "stat"));
        }
        catch (UnauthorizedAccessException)
        {
            accessDenied = true;
            return false;
        }
        catch
        {
            // 进程已退出或 stat 不可读
            return false;
        }
        int nameStartIndex = statText.IndexOf('(');
        int nameEndIndex = statText.LastIndexOf(')');
        if (nameEndIndex < 0 || nameStartIndex < 0 || nameStartIndex >= nameEndIndex)
        {
            return false;
        }
        info.Name = statText.Substring(nameStartIndex + 1, nameEndIndex - nameStartIndex - 1);
        string afterName = statText.Substring(nameEndIndex + 1).TrimStart();
        string[] fields = afterName.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        long fieldValue;
        if (TryParseField(fields, 1, out fieldValue))
        {
            info.ParentProcessId = (int)fieldValue;
        }
        long userTicks = 0;
        long systemTicks = 0;
        bool hasUserTicks = TryParseField(fields, 11, out userTicks);
        bool hasSystemTicks = TryParseField(fields, 12, out systemTicks);
        if (hasUserTicks && hasSystemTicks)
        {
            info.CpuTimeTicks = (userTicks + systemTicks) * Nanoseconds100PerClockTick;
        }
        long statPriority = 0;
        long niceValue = 0;
        TryParseField(fields, 15, out statPriority);
        TryParseField(fields, 16, out niceValue);
        info.PriorityLevel = ToPriorityLevel((int)statPriority, (int)niceValue);
        if (TryParseField(fields, 17, out fieldValue))
        {
            info.ThreadCount = (int)fieldValue;
        }
        if (bootTimeSeconds > 0 && TryParseField(fields, 19, out fieldValue))
        {
            // starttime 是开机以来的时钟节拍数，加上开机时刻换算成本地启动时间
            double startTimeSeconds = bootTimeSeconds + fieldValue / ClockTicksPerSecond;
            if (startTimeSeconds > 0)
            {
                info.StartTime = DateTimeOffset.FromUnixTimeSeconds((long)startTimeSeconds).LocalDateTime;
            }
        }
        if (TryParseField(fields, 21, out fieldValue))
        {
            // rss 单位是页，乘页大小换算成字节
            info.MemoryBytes = fieldValue * Environment.SystemPageSize;
        }
        return true;
    }

    /// <summary>按字段序号读取 stat 切分结果；序号越界或不是数字时返回 false。</summary>
    private static bool TryParseField(string[] fields, int index, out long value)
    {
        if (index >= 0 && index < fields.Length)
        {
            return long.TryParse(fields[index], out value);
        }
        value = 0;
        return false;
    }

    /// <summary>把 stat 的优先级与 nice 值映射为内核优先级级别。</summary>
    private static ProcessPriorityLevel? ToPriorityLevel(int statPriority, int niceValue)
    {
        if (statPriority < 0)
        {
            // 内核优先级为负表示被提升了调度优先级，按实时处理
            return ProcessPriorityLevel.Realtime;
        }
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

    /// <summary>读取开机时刻（Unix 秒）：/proc/stat 里的 btime 行；读不到返回 0（此时不推算启动时间）。</summary>
    internal static long ReadBootTimeSeconds()
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/stat"))
            {
                if (line.StartsWith("btime ", StringComparison.Ordinal))
                {
                    string secondsText = line.Substring("btime ".Length).Trim();
                    if (long.TryParse(secondsText, out long bootTimeSeconds))
                    {
                        return bootTimeSeconds;
                    }
                }
            }
        }
        catch
        {
            // /proc/stat 不可读
        }
        return 0;
    }

    /// <summary>读取启动命令行：cmdline 各参数以空分隔，替换为普通空格后去除首尾空白；失败返回空字符串。</summary>
    internal static string ReadCommandLine(string processDirectory)
    {
        try
        {
            byte[] commandLineBytes = File.ReadAllBytes(Path.Combine(processDirectory, "cmdline"));
            return Encoding.UTF8.GetString(commandLineBytes).Replace('\0', ' ').Trim();
        }
        catch
        {
            // 进程已退出或无权读取
            return string.Empty;
        }
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

    /// <summary>读取 /etc/passwd 建立"用户编号 → 用户名"对照表；读取失败返回空表。</summary>
    internal static Dictionary<long, string> ReadUserNames()
    {
        Dictionary<long, string> userNameByUserId = new Dictionary<long, string>();
        try
        {
            foreach (string line in File.ReadLines("/etc/passwd"))
            {
                string[] columns = line.Split(':');
                // passwd 行格式：用户名:口令占位:用户编号:…
                if (columns.Length >= 3
                    && long.TryParse(columns[2], out long userId)
                    && !userNameByUserId.ContainsKey(userId))
                {
                    userNameByUserId[userId] = columns[0];
                }
            }
        }
        catch
        {
            // /etc/passwd 不可读时退回显示用户编号
        }
        return userNameByUserId;
    }

    /// <summary>
    /// 读取进程所有者：/proc/[编号]/status 的 Uid 行第一个编号（实际使用的真实用户编号），
    /// 对照 /etc/passwd 换成用户名，查不到时直接显示编号；读取失败返回空字符串。
    /// </summary>
    internal static string ReadProcessUserName(string processDirectory, Dictionary<long, string> userNameByUserId)
    {
        try
        {
            foreach (string line in File.ReadLines(Path.Combine(processDirectory, "status")))
            {
                if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                {
                    continue;
                }
                string uidText = line.Substring("Uid:".Length).Trim();
                string[] columns = uidText.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length > 0 && long.TryParse(columns[0], out long userId))
                {
                    string? userName;
                    if (userNameByUserId.TryGetValue(userId, out userName))
                    {
                        return userName;
                    }
                    return userId.ToString();
                }
                return string.Empty;
            }
        }
        catch
        {
            // 进程已退出或 status 不可读
        }
        return string.Empty;
    }

    /// <summary>
    /// 读取 /proc/[编号]/io 里累计读写的物理字节数；至少读到一项才算成功。
    /// 无权限读取（默认配置下对非属主进程隐藏）时置 accessDenied 并返回 false。
    /// </summary>
    private static bool TryReadIoBytes(string processDirectory, ProcessInfo info, out bool accessDenied)
    {
        accessDenied = false;
        try
        {
            foreach (string line in File.ReadLines(Path.Combine(processDirectory, "io")))
            {
                if (line.StartsWith("read_bytes:", StringComparison.Ordinal))
                {
                    long readBytes;
                    if (long.TryParse(line.Substring("read_bytes:".Length).Trim(), out readBytes))
                    {
                        info.ReadIoBytes = readBytes;
                    }
                }
                else if (line.StartsWith("write_bytes:", StringComparison.Ordinal))
                {
                    long writeBytes;
                    if (long.TryParse(line.Substring("write_bytes:".Length).Trim(), out writeBytes))
                    {
                        info.WriteIoBytes = writeBytes;
                    }
                }
            }
            return info.ReadIoBytes >= 0 || info.WriteIoBytes >= 0;
        }
        catch (UnauthorizedAccessException)
        {
            accessDenied = true;
            return false;
        }
        catch
        {
            // 进程已退出或 io 不可读
            return false;
        }
    }
}
