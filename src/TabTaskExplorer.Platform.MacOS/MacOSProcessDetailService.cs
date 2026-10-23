using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.MacOS;

/// <summary>
/// macOS 平台的进程详情与调节服务：
/// 详情用一次 ps 调用读出用户名、nice 优先级、启动时间与命令行（可执行路径取命令行第一个参数），
/// ps 失败时至少退回标准库接口给出启动时间；
/// 优先级用 setpriority 调整 nice（实时档 macOS 未提供可调接口，如实返回失败），
/// CPU 亲和性掩码 macOS 未提供按进程设置的系统接口，读取返回"不支持"、设置返回失败。
/// </summary>
public class MacOSProcessDetailService : IProcessDetailService
{
    /// <summary>macOS 平台支持详情查询与优先级调节（亲和性掩码不支持，读写均如实返回）。</summary>
    public bool IsSupported
    {
        get { return true; }
    }

    /// <summary>查询指定进程的详情；进程不存在且 ps 也不可用时返回 null。</summary>
    public ProcessDetail? GetDetail(int processId)
    {
        ProcessDetail detail = new ProcessDetail();
        detail.ProcessId = processId;
        // macOS 没有按进程设置 CPU 亲和性掩码的系统接口：恒为"不支持"
        detail.AffinityMask = -1;

        bool readFromPs = TryReadFromPs(processId, detail);
        if (!readFromPs && !TryStartTimeFromProcessApi(processId, detail))
        {
            // 进程不存在且标准库也读不到：按失败返回
            return null;
        }
        if (detail.StartTimeText.Length == 0)
        {
            // ps 没给出可解析的启动时间时，用标准库接口补齐
            TryStartTimeFromProcessApi(processId, detail);
        }
        return detail;
    }

    /// <summary>设置指定进程的优先级级别；失败返回 false（权限不足或实时档时如实上报）。</summary>
    public bool SetPriorityLevel(int processId, ProcessPriorityLevel level)
    {
        if (level == ProcessPriorityLevel.Realtime)
        {
            // macOS 未提供设置实时调度策略的应用程序接口：如实返回失败
            return false;
        }
        int niceValue = NiceValueFor(level);
        return setpriority(PrioProcess, processId, niceValue) == 0;
    }

    /// <summary>设置指定进程的 CPU 亲和性位掩码：macOS 未提供该系统接口，恒返回失败。</summary>
    public bool SetProcessorAffinity(int processId, long affinityMask)
    {
        return false;
    }

    /// <summary>
    /// 用一次 ps 调用读取单个进程的用户名、nice 优先级、启动时间与命令行；
    /// ps 执行失败或没有输出时返回假。
    /// </summary>
    private static bool TryReadFromPs(int processId, ProcessDetail detail)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "/bin/ps";
            startInfo.Arguments = "-p " + processId + " -o pid=,user=,ni=,lstart=,args=";
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            using (Process? psProcess = Process.Start(startInfo))
            {
                if (psProcess == null)
                {
                    return false;
                }
                string outputText = psProcess.StandardOutput.ReadToEnd();
                if (!psProcess.WaitForExit(3000) || psProcess.ExitCode != 0)
                {
                    return false;
                }
                return ParsePsLine(outputText, detail);
            }
        }
        catch
        {
            // ps 执行失败：退回标准库接口
            return false;
        }
    }

    /// <summary>
    /// 解析 ps 单行输出：编号 用户名 nice 值 启动时间（五个词） 命令行（其余词）。
    /// 没有输出行（进程刚退出）返回假。
    /// </summary>
    private static bool ParsePsLine(string outputText, ProcessDetail detail)
    {
        if (string.IsNullOrEmpty(outputText))
        {
            return false;
        }
        string[] lines = outputText.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string[] columns = line.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            // 固定列：0 编号、1 用户名、2 nice 值、3 到 7 启动时间五个词、8 起为命令行
            int processId;
            if (columns.Length < 8 || !int.TryParse(columns[0], out processId))
            {
                continue;
            }
            detail.UserName = columns[1];
            int niceValue;
            if (int.TryParse(columns[2], out niceValue))
            {
                detail.PriorityLevel = PriorityLevelForNice(niceValue);
            }
            string startTimeText = columns[3] + " " + columns[4] + " " + columns[5] + " " + columns[6] + " " + columns[7];
            DateTime startTime;
            if (DateTime.TryParse(startTimeText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out startTime))
            {
                detail.StartTimeText = startTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            detail.CommandLine = JoinColumns(columns, 8);
            detail.ExecutablePath = ReadExecutablePath(detail.CommandLine);
            return true;
        }
        return false;
    }

    /// <summary>ps 读不到时退回标准库接口，至少给出启动时间；进程不存在时返回假。</summary>
    private static bool TryStartTimeFromProcessApi(int processId, ProcessDetail detail)
    {
        try
        {
            using (Process process = Process.GetProcessById(processId))
            {
                detail.StartTimeText = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                if (detail.CommandLine.Length == 0)
                {
                    detail.CommandLine = process.ProcessName;
                }
                return true;
            }
        }
        catch
        {
            // 进程不存在或没有权限
            return false;
        }
    }

    /// <summary>把命令行的剩余列用单个空格重新连接（ps 已按空白切分过）。</summary>
    private static string JoinColumns(string[] columns, int startIndex)
    {
        if (startIndex >= columns.Length)
        {
            return string.Empty;
        }
        return string.Join(" ", columns, startIndex, columns.Length - startIndex);
    }

    /// <summary>命令行的第一个参数通常是可执行文件完整路径；命令行为空时返回空字符串。</summary>
    private static string ReadExecutablePath(string commandLine)
    {
        if (commandLine.Length == 0)
        {
            return string.Empty;
        }
        string[] parts = commandLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    /// <summary>把 nice 值映射为内核优先级档位：nice 越大优先级越低。</summary>
    private static ProcessPriorityLevel? PriorityLevelForNice(int niceValue)
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

    /// <summary>把内核优先级档位映射为 nice 值（与读取时的映射互逆）。</summary>
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

    /// <summary>setpriority 的作用对象类型：按进程（PRIO_PROCESS）。</summary>
    private const int PrioProcess = 0;

    [DllImport("libc", SetLastError = true)]
    private static extern int setpriority(int which, int who, int priority);
}
