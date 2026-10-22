using System;
using System.Collections.Generic;
using System.Threading;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 进程采样服务（单例）：桌面主窗口与全部网页端工作区共用一份进程读取记录。
/// 职责：
/// 1. 持有最近一次共享采样（系统资源快照 + 进程列表），新开标签先直接显示这份记录，不重新读取；
/// 2. 并发读取合并为一次采样（单飞），同一时刻多个标签同时刷新只实际读取一遍；
/// 3. 完成结果在复用窗口内直接复用，避免各标签的周期刷新交错触发反复全量读取；
/// 4. 结束进程后调用作废接口，下一次读取强制重新采样，界面立刻看到进程消失。
/// </summary>
public class ProcessSampleService
{
    /// <summary>采样完成后，结果在该时间内可直接复用（小于界面 2000 毫秒的刷新周期，数据仍然新鲜）。</summary>
    private static readonly TimeSpan ReuseWindow = TimeSpan.FromMilliseconds(1500);

    private readonly IProcessProvider processProvider;
    private readonly ISystemMetricsProvider metricsProvider;
    private readonly object sampleLock = new object();
    private ProcessSample? latestSample;
    private bool sampling;
    private int sampleVersion;

    /// <summary>创建进程采样服务。</summary>
    /// <param name="processProvider">平台进程服务（枚举与结束进程）。</param>
    /// <param name="metricsProvider">平台系统资源服务（采样处理器与内存占用）。</param>
    public ProcessSampleService(IProcessProvider processProvider, ISystemMetricsProvider metricsProvider)
    {
        this.processProvider = processProvider;
        this.metricsProvider = metricsProvider;
    }

    /// <summary>读取最近一次共享采样；程序启动后还没采样过时返回 null。</summary>
    public ProcessSample? GetCached()
    {
        lock (this.sampleLock)
        {
            return this.latestSample;
        }
    }

    /// <summary>
    /// 读取一份进程采样（共享的进程读取记录）：
    /// 复用窗口内的现成结果直接返回；已有采样在进行时等待其完成（单飞）；
    /// 否则由本线程执行一次采样并更新共享记录。
    /// </summary>
    /// <returns>共享的采样结果（进程列表只读，各标签自行转换成行，不要修改列表本身）。</returns>
    public ProcessSample ReadShared()
    {
        while (true)
        {
            int versionAtStart;
            lock (this.sampleLock)
            {
                if (this.latestSample != null && this.latestSample.Age <= ReuseWindow)
                {
                    return this.latestSample;
                }
                if (this.sampling)
                {
                    // 已有采样在进行：等它完成后重新判断（完成结果多半已可复用）
                    Monitor.Wait(this.sampleLock);
                    continue;
                }
                this.sampling = true;
                versionAtStart = this.sampleVersion;
            }

            // 实际采样在锁外执行，等待方不会被采样耗时拖住
            ProcessSample result = new ProcessSample();
            try
            {
                result.Snapshot = this.metricsProvider.GetSnapshot();
                result.Processes = new List<ProcessInfo>(this.processProvider.ListProcesses());
                result.TakenAt = DateTime.UtcNow;
            }
            catch (Exception)
            {
                lock (this.sampleLock)
                {
                    this.sampling = false;
                    Monitor.PulseAll(this.sampleLock);
                }
                throw;
            }

            bool committed;
            lock (this.sampleLock)
            {
                this.sampling = false;
                committed = versionAtStart == this.sampleVersion;
                if (committed)
                {
                    this.latestSample = result;
                }
                Monitor.PulseAll(this.sampleLock);
            }
            if (committed)
            {
                return result;
            }
            // 采样期间记录被作废（例如刚结束了一个进程），重新读取拿到最新列表
        }
    }

    /// <summary>作废共享的进程读取记录：结束进程后调用，让下一次读取立即重新采样。</summary>
    public void Invalidate()
    {
        lock (this.sampleLock)
        {
            this.latestSample = null;
            this.sampleVersion++;
        }
    }
}

/// <summary>一次共享的进程读取记录：系统资源快照与进程列表。</summary>
public class ProcessSample
{
    /// <summary>系统资源快照（处理器占用、内存占用）。</summary>
    public SystemSnapshot Snapshot { get; set; } = new SystemSnapshot();

    /// <summary>采样得到的进程列表（只读使用，各标签不要修改）。</summary>
    public List<ProcessInfo> Processes { get; set; } = new List<ProcessInfo>();

    /// <summary>采样完成的时刻（世界标准时间）。</summary>
    public DateTime TakenAt { get; set; }

    /// <summary>本记录距现在有多久。</summary>
    public TimeSpan Age
    {
        get { return DateTime.UtcNow - this.TakenAt; }
    }
}
