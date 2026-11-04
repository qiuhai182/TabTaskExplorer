using System;
using System.Diagnostics;
using System.Threading;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// UI 线程看门狗：界面线程每 200 毫秒打一次心跳，后台线程每 500 毫秒检查一次；
/// 心跳超过 2.5 秒未更新时写一条「UI 阻塞疑似」日志，并带上最近进入的阶段名。
/// 只用于定位，不参与业务控制流。
/// </summary>
public static class UiWatchdog
{
    private const double StallThresholdMs = 2500;
    private const double RepeatLogIntervalMs = 5000;

    private static readonly object gate = new object();
    private static bool started;
    private static long lastBeatTicks;
    private static long stageStartedTicks;
    private static long lastStallLogTicks;
    private static volatile string stage = "空闲";

    /// <summary>启动看门狗；重复调用只生效一次。</summary>
    public static void Start()
    {
        lock (UiWatchdog.gate)
        {
            if (UiWatchdog.started)
            {
                return;
            }
            UiWatchdog.started = true;
        }
        long now = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref UiWatchdog.lastBeatTicks, now);
        Interlocked.Exchange(ref UiWatchdog.stageStartedTicks, now);

        Avalonia.Threading.DispatcherTimer timer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        timer.Tick += delegate
        {
            Interlocked.Exchange(ref UiWatchdog.lastBeatTicks, Stopwatch.GetTimestamp());
        };
        timer.Start();

        Thread thread = new Thread(UiWatchdog.WatchLoop);
        thread.IsBackground = true;
        thread.Name = "UI线程看门狗";
        thread.Start();
        UiLogger.Log("UI看门狗", "已启动：心跳间隔=200 毫秒 阻塞阈值=" + (int)UiWatchdog.StallThresholdMs + " 毫秒");
    }

    /// <summary>进入一个关键阶段（剪贴板探测、Shell 枚举、任务提交等），供阻塞日志标注。</summary>
    public static void EnterStage(string name)
    {
        UiWatchdog.stage = name ?? string.Empty;
        Interlocked.Exchange(ref UiWatchdog.stageStartedTicks, Stopwatch.GetTimestamp());
    }

    /// <summary>离开阶段；仅当当前阶段仍是该名称时清空，避免嵌套阶段误清。</summary>
    public static void LeaveStage(string name)
    {
        if (string.Equals(UiWatchdog.stage, name, StringComparison.Ordinal))
        {
            UiWatchdog.stage = "空闲";
        }
    }

    private static void WatchLoop()
    {
        while (true)
        {
            Thread.Sleep(500);
            long now = Stopwatch.GetTimestamp();
            long beat = Interlocked.Read(ref UiWatchdog.lastBeatTicks);
            if (beat == 0)
            {
                continue;
            }
            double stalledMs = (now - beat) * 1000.0 / Stopwatch.Frequency;
            if (stalledMs < UiWatchdog.StallThresholdMs)
            {
                continue;
            }
            long lastLog = Interlocked.Read(ref UiWatchdog.lastStallLogTicks);
            if (lastLog != 0 && (now - lastLog) * 1000.0 / Stopwatch.Frequency < UiWatchdog.RepeatLogIntervalMs)
            {
                continue;
            }
            Interlocked.Exchange(ref UiWatchdog.lastStallLogTicks, now);
            string currentStage = UiWatchdog.stage;
            double stageMs = (now - Interlocked.Read(ref UiWatchdog.stageStartedTicks)) * 1000.0 / Stopwatch.Frequency;
            UiLogger.Log("UI看门狗", "UI 阻塞疑似 阶段=" + currentStage
                + " 耗时=" + (int)stalledMs + " 毫秒 当前阶段已持续=" + (int)stageMs
                + " 毫秒 结果=界面线程心跳超时，建议检查该阶段的阻塞调用");
        }
    }
}
