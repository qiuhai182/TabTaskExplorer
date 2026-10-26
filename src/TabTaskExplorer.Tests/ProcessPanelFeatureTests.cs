using System;
using System.Collections.Generic;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>
/// 进程监控功能内核层测试：进程树构建、每进程处理器占用差分、亲和性掩码转换。
/// 对应 Process Explorer 的进程树视图与占用列算法、Process Lasso 的亲和性调节实现。
/// </summary>
public class ProcessPanelFeatureTests
{
    /// <summary>构造一个进程信息（其余字段用默认值，只填测试关心的字段）。</summary>
    private static ProcessInfo MakeProcess(int processId, int parentProcessId, long cpuTimeTicks)
    {
        ProcessInfo info = new ProcessInfo();
        info.ProcessId = processId;
        info.Name = "进程" + processId;
        info.ParentProcessId = parentProcessId;
        info.CpuTimeTicks = cpuTimeTicks;
        return info;
    }

    // ================================================================
    //  进程树构建
    // ================================================================

    /// <summary>父子关系正确时：父进程在前、子进程紧跟其后且深度加一。</summary>
    [Fact]
    public void TreeBuilder父子相连且子进程紧跟父进程()
    {
        List<ProcessInfo> processes = new List<ProcessInfo>();
        processes.Add(MakeProcess(100, 0, 0));
        processes.Add(MakeProcess(200, 100, 0));
        processes.Add(MakeProcess(300, 100, 0));

        IReadOnlyList<ProcessTreeRow> rows = ProcessTreeBuilder.Build(processes);

        Assert.Equal(3, rows.Count);
        Assert.Equal(100, rows[0].Process.ProcessId);
        Assert.Equal(0, rows[0].Depth);
        Assert.Equal(200, rows[1].Process.ProcessId);
        Assert.Equal(1, rows[1].Depth);
        Assert.Equal(300, rows[2].Process.ProcessId);
        Assert.Equal(1, rows[2].Depth);
    }

    /// <summary>父进程编号指向不存在的进程时，该进程按顶层节点输出，不会漏掉。</summary>
    [Fact]
    public void TreeBuilder父进程缺失时按顶层输出()
    {
        List<ProcessInfo> processes = new List<ProcessInfo>();
        processes.Add(MakeProcess(50, 9999, 0));

        IReadOnlyList<ProcessTreeRow> rows = ProcessTreeBuilder.Build(processes);

        Assert.Single(rows);
        Assert.Equal(50, rows[0].Process.ProcessId);
        Assert.Equal(0, rows[0].Depth);
    }

    /// <summary>两个进程互为父子形成环时：每个进程都只输出一次，不出现无限递归。</summary>
    [Fact]
    public void TreeBuilder父子成环时每个进程只输出一次()
    {
        List<ProcessInfo> processes = new List<ProcessInfo>();
        processes.Add(MakeProcess(1, 2, 0));
        processes.Add(MakeProcess(2, 1, 0));

        IReadOnlyList<ProcessTreeRow> rows = ProcessTreeBuilder.Build(processes);

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Process.ProcessId);
        Assert.Equal(2, rows[1].Process.ProcessId);
    }

    /// <summary>多层嵌套时深度逐层递增，且输出顺序为编号升序的稳定顺序。</summary>
    [Fact]
    public void TreeBuilder多层嵌套深度逐层递增()
    {
        List<ProcessInfo> processes = new List<ProcessInfo>();
        processes.Add(MakeProcess(300, 200, 0));
        processes.Add(MakeProcess(200, 100, 0));
        processes.Add(MakeProcess(100, 0, 0));

        IReadOnlyList<ProcessTreeRow> rows = ProcessTreeBuilder.Build(processes);

        Assert.Equal(3, rows.Count);
        Assert.Equal(100, rows[0].Process.ProcessId);
        Assert.Equal(0, rows[0].Depth);
        Assert.Equal(200, rows[1].Process.ProcessId);
        Assert.Equal(1, rows[1].Depth);
        Assert.Equal(300, rows[2].Process.ProcessId);
        Assert.Equal(2, rows[2].Depth);
    }

    // ================================================================
    //  每进程处理器占用差分
    // ================================================================

    /// <summary>首轮采样只记录基线：不产生任何占用结果。</summary>
    [Fact]
    public void 占用计算首轮只记基线不产生结果()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        List<ProcessInfo> firstRound = new List<ProcessInfo>();
        firstRound.Add(MakeProcess(1, 0, 10000000L));

        IReadOnlyDictionary<int, double> result = tracker.Update(
            firstRound, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 4);

        Assert.Empty(result);
    }

    /// <summary>首轮采样时进程已启动：用累计处理器时间除以存活时长估算出非零占用。</summary>
    [Fact]
    public void 占用计算首轮按启动时间估算出非零结果()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        DateTime sampleTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 已运行 100 秒的进程累计占用 4 秒处理器时间，单核折算为 4%
        ProcessInfo process = MakeProcess(1, 0, 40000000L);
        process.StartTime = sampleTime.AddSeconds(-100);

        IReadOnlyDictionary<int, double> result = tracker.Update(new List<ProcessInfo> { process }, sampleTime, 4);

        Assert.True(result.ContainsKey(1));
        Assert.Equal(1.0, result[1], 1);
    }

    /// <summary>首轮采样时启动时间未知：无法估算存活时长，不产生占用结果。</summary>
    [Fact]
    public void 占用计算首轮启动时间未知时不产生结果()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        DateTime sampleTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        ProcessInfo process = MakeProcess(1, 0, 40000000L);
        process.StartTime = null;

        IReadOnlyDictionary<int, double> result = tracker.Update(new List<ProcessInfo> { process }, sampleTime, 4);

        Assert.Empty(result);
    }

    /// <summary>首轮采样时启动时间晚于采样时间：存活时长为负数属异常数据，不产生占用结果。</summary>
    [Fact]
    public void 占用计算首轮启动时间晚于采样时间不产生结果()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        DateTime sampleTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        ProcessInfo process = MakeProcess(1, 0, 40000000L);
        process.StartTime = sampleTime.AddSeconds(10);

        IReadOnlyDictionary<int, double> result = tracker.Update(new List<ProcessInfo> { process }, sampleTime, 4);

        Assert.Empty(result);
    }

    /// <summary>第二轮按累计处理器时间差计算：0.4 秒处理器时间 / 2 秒墙钟 / 4 核 = 5%。</summary>
    [Fact]
    public void 占用计算按时间差除以墙钟与核心数()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        DateTime firstTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime secondTime = firstTime.AddSeconds(2);

        List<ProcessInfo> firstRound = new List<ProcessInfo>();
        firstRound.Add(MakeProcess(1, 0, 40000000L));
        tracker.Update(firstRound, firstTime, 4);

        // 第二轮累计处理器时间增加 4,000,000 Tick = 0.4 秒
        List<ProcessInfo> secondRound = new List<ProcessInfo>();
        secondRound.Add(MakeProcess(1, 0, 44000000L));
        IReadOnlyDictionary<int, double> result = tracker.Update(secondRound, secondTime, 4);

        Assert.True(result.ContainsKey(1));
        Assert.Equal(5.0, result[1], 1);
    }

    /// <summary>同一样本时间重复应用不产生结果（要求采样时间严格前进）。</summary>
    [Fact]
    public void 占用计算同一样本时间重复应用不产生结果()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        DateTime sampleTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        List<ProcessInfo> firstRound = new List<ProcessInfo>();
        firstRound.Add(MakeProcess(1, 0, 10000000L));
        tracker.Update(firstRound, sampleTime, 4);

        // 时间不变但累计时间增长：不产生结果，避免同一样本被重复计算
        List<ProcessInfo> repeatRound = new List<ProcessInfo>();
        repeatRound.Add(MakeProcess(1, 0, 20000000L));
        IReadOnlyDictionary<int, double> result = tracker.Update(repeatRound, sampleTime, 4);

        Assert.Empty(result);
    }

    /// <summary>累计处理器时间倒退（进程编号被复用）时按 0% 处理，不产生负值。</summary>
    [Fact]
    public void 占用计算累计时间倒退按零处理()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        DateTime firstTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime secondTime = firstTime.AddSeconds(1);

        List<ProcessInfo> firstRound = new List<ProcessInfo>();
        firstRound.Add(MakeProcess(1, 0, 90000000L));
        tracker.Update(firstRound, firstTime, 4);

        // 新进程复用了编号 1，累计时间从 0 开始
        List<ProcessInfo> secondRound = new List<ProcessInfo>();
        secondRound.Add(MakeProcess(1, 0, 5000000L));
        IReadOnlyDictionary<int, double> result = tracker.Update(secondRound, secondTime, 4);

        Assert.True(result.ContainsKey(1));
        Assert.Equal(0.0, result[1]);
    }

    /// <summary>占用结果封顶 100%：单进程一个核心吃满也不超过 100（4 核机器单核满载为 25）。</summary>
    [Fact]
    public void 占用计算结果封顶一百()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        DateTime firstTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime secondTime = firstTime.AddSeconds(1);

        List<ProcessInfo> firstRound = new List<ProcessInfo>();
        firstRound.Add(MakeProcess(1, 0, 0L));
        tracker.Update(firstRound, firstTime, 1);

        // 1 秒墙钟内累计了 2 秒处理器时间（远超单核能力），按 100 封顶
        List<ProcessInfo> secondRound = new List<ProcessInfo>();
        secondRound.Add(MakeProcess(1, 0, 20000000L));
        IReadOnlyDictionary<int, double> result = tracker.Update(secondRound, secondTime, 1);

        Assert.Equal(100.0, result[1]);
    }

    /// <summary>退出的进程基线随下一轮清理：之后重新出现也不沿用旧基线。</summary>
    [Fact]
    public void 占用计算退出进程的基线被清理()
    {
        ProcessCpuUsageTracker tracker = new ProcessCpuUsageTracker();
        DateTime t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 第一轮有进程 1，第二轮没有（进程退出）
        List<ProcessInfo> firstRound = new List<ProcessInfo>();
        firstRound.Add(MakeProcess(1, 0, 0L));
        tracker.Update(firstRound, t1, 4);
        tracker.Update(new List<ProcessInfo>(), t1.AddSeconds(1), 4);

        // 进程 1 重新出现：首轮对它而言是新进程，不产生占用结果
        List<ProcessInfo> thirdRound = new List<ProcessInfo>();
        thirdRound.Add(MakeProcess(1, 0, 50000000L));
        IReadOnlyDictionary<int, double> result = tracker.Update(thirdRound, t1.AddSeconds(2), 4);

        Assert.Empty(result);
    }

    // ================================================================
    //  亲和性掩码转换
    // ================================================================

    /// <summary>核心编号列表转掩码：第 n 位代表第 n 个核心。</summary>
    [Fact]
    public void 掩码核心编号列表转位掩码()
    {
        long mask = AffinityMaskUtility.ToMask(new List<int> { 0, 1, 2 }, 8);

        Assert.Equal(0b111L, mask);
    }

    /// <summary>列表为空或编号越界时返回"不支持"掩码 -1。</summary>
    [Fact]
    public void 掩码空列表与越界编号返回不支持()
    {
        Assert.Equal(AffinityMaskUtility.UnsupportedMask, AffinityMaskUtility.ToMask(new List<int>(), 8));
        Assert.Equal(AffinityMaskUtility.UnsupportedMask, AffinityMaskUtility.ToMask(new List<int> { 8 }, 8));
        Assert.Equal(AffinityMaskUtility.UnsupportedMask, AffinityMaskUtility.ToMask(new List<int> { -1 }, 8));
    }

    /// <summary>掩码转文本：全部核心选中显示"全部核心"，部分选中显示编号列表，-1 显示"不支持"。</summary>
    [Fact]
    public void 掩码位掩码转显示文本()
    {
        Assert.Equal("全部核心", AffinityMaskUtility.ToText(0b1111L, 4));
        Assert.Equal("0,2", AffinityMaskUtility.ToText(0b101L, 4));
        Assert.Equal("不支持", AffinityMaskUtility.ToText(-1, 4));
        Assert.Equal("无", AffinityMaskUtility.ToText(0, 4));
    }

    /// <summary>解析界面输入：支持中英文逗号分号与空格分隔，去掉重复与非法项。</summary>
    [Fact]
    public void 掩码解析界面输入兼容多种分隔符与重复项()
    {
        Assert.Equal(new List<int> { 0, 1, 3 }, AffinityMaskUtility.ParseIndexes("0,1，3"));
        Assert.Equal(new List<int> { 2 }, AffinityMaskUtility.ParseIndexes("2; 2；2 "));
        // 整段不是合法数字时按无效项跳过（"5abc" 不会只取前面的 5）
        Assert.Empty(AffinityMaskUtility.ParseIndexes("5abc"));
        Assert.Empty(AffinityMaskUtility.ParseIndexes("abc"));
        Assert.Empty(AffinityMaskUtility.ParseIndexes(null));
        Assert.Empty(AffinityMaskUtility.ParseIndexes("-1,99"));
    }

    /// <summary>掩码与编号列表往返转换保持一致。</summary>
    [Fact]
    public void 掩码往返转换保持一致()
    {
        List<int> indexes = new List<int> { 0, 3, 7 };
        long mask = AffinityMaskUtility.ToMask(indexes, 8);
        List<int> parsed = AffinityMaskUtility.ParseIndexes(AffinityMaskUtility.ToText(mask, 8));

        Assert.Equal(indexes, parsed);
    }
}
