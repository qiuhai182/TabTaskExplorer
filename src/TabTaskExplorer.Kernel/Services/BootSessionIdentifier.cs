using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 开机标识：给每一次系统启动生成一个稳定编号。
/// 同一开机会话内多次取到相同编号，重启后编号变化，用于判断自定义启动项本次开机是否已拉起。
/// Linux 读取 /proc/stat 的 btime，macOS 读取 sysctl 的 kern.boottime，其余平台按开机时长反推开机时刻。
/// </summary>
public static class BootSessionIdentifier
{
    /// <summary>取当前开机会话的稳定编号（形如 boot-1700000000）。</summary>
    public static string GetCurrentSessionId()
    {
        long bootSeconds = ReadBootSeconds();
        if (bootSeconds > 0)
        {
            return "boot-" + bootSeconds.ToString(CultureInfo.InvariantCulture);
        }
        // 兜底：按开机时长反推开机时刻，向下取整到分钟，抵消两次读取的毫秒级漂移
        long uptimeMilliseconds = Environment.TickCount64;
        DateTime bootTime = DateTime.Now.AddMilliseconds(-uptimeMilliseconds);
        bootTime = new DateTime(bootTime.Year, bootTime.Month, bootTime.Day,
            bootTime.Hour, bootTime.Minute, 0);
        return "boot-" + bootTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
    }

    /// <summary>读取本次开机的秒级时间戳；读不到返回 0。</summary>
    private static long ReadBootSeconds()
    {
        if (OperatingSystem.IsLinux())
        {
            return ReadLinuxBootSeconds();
        }
        if (OperatingSystem.IsMacOS())
        {
            return ReadMacBootSeconds();
        }
        return 0;
    }

    /// <summary>Linux：从 /proc/stat 的 btime 行读取开机秒级时间戳。</summary>
    private static long ReadLinuxBootSeconds()
    {
        try
        {
            if (!File.Exists("/proc/stat"))
            {
                return 0;
            }
            foreach (string line in File.ReadLines("/proc/stat"))
            {
                if (!line.StartsWith("btime ", StringComparison.Ordinal))
                {
                    continue;
                }
                long value;
                if (long.TryParse(line.Substring(6).Trim(), out value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // /proc 不可读时回落到按开机时长反推
        }
        return 0;
    }

    /// <summary>macOS：从 sysctl kern.boottime 输出中解析 sec = 秒级时间戳。</summary>
    private static long ReadMacBootSeconds()
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "sysctl";
            startInfo.Arguments = "-n kern.boottime";
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return 0;
                }
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                int secIndex = output.IndexOf("sec =", StringComparison.Ordinal);
                if (secIndex < 0)
                {
                    return 0;
                }
                string remain = output.Substring(secIndex + 5).Trim();
                int endIndex = remain.IndexOfAny(new char[] { ',', '}' });
                if (endIndex >= 0)
                {
                    remain = remain.Substring(0, endIndex).Trim();
                }
                long value;
                if (long.TryParse(remain, out value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // sysctl 不可用时回落到按开机时长反推
        }
        return 0;
    }
}
