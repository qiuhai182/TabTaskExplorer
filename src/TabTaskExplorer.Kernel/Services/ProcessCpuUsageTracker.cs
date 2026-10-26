using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 每进程处理器占用计算：相邻两次采样的累计处理器时间差 ÷ 墙钟时间差 ÷ 处理器核心数。
/// 参照 Process Explorer 的占用列算法；没有历史基线的首轮进程，用启动时间反推存活时长做一次估算。
/// 只保存当前仍存在的进程，退出进程的基线随下一轮采样自动清理。
/// </summary>
public class ProcessCpuUsageTracker
{
    private readonly Dictionary<int, long> lastCpuTimeTicks = new Dictionary<int, long>();
    private readonly Dictionary<int, DateTime> lastSampleTimeUtc = new Dictionary<int, DateTime>();

    /// <summary>
    /// 记录一轮采样并计算每个进程的占用百分比。
    /// </summary>
    /// <param name="processes">本轮进程列表。</param>
    /// <param name="sampleTimeUtc">本轮采样时间（世界协调时）。</param>
    /// <param name="processorCount">处理器核心数。</param>
    /// <returns>进程编号 → 占用百分比（0 到 100，保留一位小数）；启动时间未知的首轮进程不在结果中。</returns>
    public IReadOnlyDictionary<int, double> Update(
        IReadOnlyList<ProcessInfo> processes,
        DateTime sampleTimeUtc,
        int processorCount)
    {
        int coreCount = processorCount < 1 ? 1 : processorCount;
        Dictionary<int, double> usageByProcessId = new Dictionary<int, double>();
        Dictionary<int, long> nextCpuTimeTicks = new Dictionary<int, long>();
        Dictionary<int, DateTime> nextSampleTimeUtc = new Dictionary<int, DateTime>();

        foreach (ProcessInfo info in processes)
        {
            long previousCpuTimeTicks;
            // 先给初值：本变量在 && 短路链的第二段才赋值，编译器无法推断分支里一定已赋值
            DateTime previousSampleTimeUtc = default;
            bool hasBaseline = this.lastCpuTimeTicks.TryGetValue(info.ProcessId, out previousCpuTimeTicks)
                && this.lastSampleTimeUtc.TryGetValue(info.ProcessId, out previousSampleTimeUtc);

            if (hasBaseline && sampleTimeUtc > previousSampleTimeUtc)
            {
                double wallSeconds = (sampleTimeUtc - previousSampleTimeUtc).TotalSeconds;
                double cpuSeconds = (info.CpuTimeTicks - previousCpuTimeTicks) / 10000000.0;
                if (cpuSeconds < 0)
                {
                    // 进程编号被复用（旧进程退出、新进程同号）时累计时间会倒退，按 0 处理
                    cpuSeconds = 0;
                }
                double percent = wallSeconds > 0 ? (cpuSeconds * 100.0) / (wallSeconds * coreCount) : 0;
                if (percent > 100)
                {
                    percent = 100;
                }
                usageByProcessId[info.ProcessId] = Math.Round(percent, 1);
            }
            else if (!hasBaseline)
            {
                // 首轮采样：没有上一轮基线，用"累计处理器时间 ÷ 从启动至今的存活时长"估算
                double? firstRoundPercent = EstimateFirstRound(info, sampleTimeUtc, coreCount);
                if (firstRoundPercent.HasValue)
                {
                    usageByProcessId[info.ProcessId] = firstRoundPercent.Value;
                }
            }

            nextCpuTimeTicks[info.ProcessId] = info.CpuTimeTicks;
            nextSampleTimeUtc[info.ProcessId] = sampleTimeUtc;
        }

        this.lastCpuTimeTicks.Clear();
        this.lastSampleTimeUtc.Clear();
        foreach (KeyValuePair<int, long> pair in nextCpuTimeTicks)
        {
            this.lastCpuTimeTicks[pair.Key] = pair.Value;
        }
        foreach (KeyValuePair<int, DateTime> pair in nextSampleTimeUtc)
        {
            this.lastSampleTimeUtc[pair.Key] = pair.Value;
        }
        return usageByProcessId;
    }

    /// <summary>
    /// 首轮 CPU 占用估算：累计处理器时间 ÷ （采样时刻 − 启动时刻）÷ 核心数，结果限制在 0 到 100。
    /// 启动时间未知、累计时间非法或进程刚启动时返回 null（不出结果，等下一轮差分）。
    /// </summary>
    private static double? EstimateFirstRound(ProcessInfo info, DateTime sampleTimeUtc, int coreCount)
    {
        if (!info.StartTime.HasValue || info.CpuTimeTicks < 0)
        {
            return null;
        }
        DateTime startUtc = ToUtc(info.StartTime.Value);
        double aliveSeconds = (sampleTimeUtc - startUtc).TotalSeconds;
        if (aliveSeconds < 0.1)
        {
            // 进程刚启动或启动时间晚于采样时间，分母过小不具参考性
            return null;
        }
        double cpuSeconds = info.CpuTimeTicks / 10000000.0;
        double percent = cpuSeconds * 100.0 / (aliveSeconds * coreCount);
        if (percent > 100)
        {
            percent = 100;
        }
        if (percent < 0)
        {
            percent = 0;
        }
        return Math.Round(percent, 1);
    }

    /// <summary>把启动时间统一换算为世界协调时；Kind 为 Unspecified 时按本地时间处理。</summary>
    private static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }
        if (value.Kind == DateTimeKind.Local)
        {
            return value.ToUniversalTime();
        }
        return DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
    }
}
