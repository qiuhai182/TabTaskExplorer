using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 每进程处理器占用计算：相邻两次采样的累计处理器时间差 ÷ 墙钟时间差 ÷ 处理器核心数。
/// 参照 Process Explorer 的占用列算法；首轮采样只记录基线、不产生结果。
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
    /// <returns>进程编号 → 占用百分比（0 到 100，保留一位小数）；首轮出现的进程不在结果中。</returns>
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
                && this.lastSampleTimeUtc.TryGetValue(info.ProcessId, out previousSampleTimeUtc)
                && sampleTimeUtc > previousSampleTimeUtc;

            if (hasBaseline)
            {
                double wallSeconds = (sampleTimeUtc - previousSampleTimeUtc).TotalSeconds;
                double cpuSeconds = (info.CpuTimeTicks - previousCpuTimeTicks) / 10000000.0;
                if (cpuSeconds < 0)
                {
                    // 进程编号被复用（旧进程退出、新进程同号）时累计时间会倒退，按基线重建处理
                    cpuSeconds = 0;
                }
                double percent = wallSeconds > 0 ? (cpuSeconds * 100.0) / (wallSeconds * coreCount) : 0;
                if (percent > 100)
                {
                    percent = 100;
                }
                usageByProcessId[info.ProcessId] = Math.Round(percent, 1);
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
}
