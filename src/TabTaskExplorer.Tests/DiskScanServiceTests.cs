using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>
/// 磁盘扫描服务目录计算单飞测试：
/// 同一路径并发回填只实际扫描一次并共享结果；
/// 部分订阅者取消不影响其余订阅者；全部订阅者取消后底层计算才取消并允许重新发起。
/// </summary>
public class DiskScanServiceTests
{
    /// <summary>可阻塞、可计数的磁盘用量提供者测试替身。</summary>
    private sealed class BlockingDiskUsageProvider : IDiskUsageProvider
    {
        /// <summary>ComputeSize 实际执行次数。</summary>
        public int ComputeCallCount;

        /// <summary>放行信号：打开后 ComputeSize 才返回。</summary>
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);

        /// <summary>进入信号：ComputeSize 被调用时置位，便于测试等待。</summary>
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);

        public IReadOnlyList<DiskEntry> GetTopLevelEntries(string rootPath)
        {
            return new List<DiskEntry>();
        }

        public long ComputeSize(string path, CancellationToken cancellationToken, Action<long>? onProgress = null, DiskUsageSnapshot? snapshot = null, bool forceFull = false)
        {
            Interlocked.Increment(ref this.ComputeCallCount);
            this.Entered.Set();
            this.Release.Wait(cancellationToken);
            return 12345;
        }
    }

    /// <summary>同路径两个订阅者：只执行一次计算，双方拿到同一结果。</summary>
    [Fact]
    public void ComputeSizeSharedSamePathRunsOnceAndSharesResult()
    {
        BlockingDiskUsageProvider provider = new BlockingDiskUsageProvider();
        DiskScanService service = new DiskScanService(provider);
        long firstResult = 0;
        long secondResult = 0;
        Exception? firstError = null;
        Exception? secondError = null;

        Task first = Task.Run(delegate
        {
            try
            {
                firstResult = service.ComputeSizeShared(@"C:\单飞测试目录", CancellationToken.None);
            }
            catch (Exception exception)
            {
                firstError = exception;
            }
        });
        Assert.True(provider.Entered.Wait(TimeSpan.FromSeconds(5)), "第一个订阅者应已进入计算");

        Task second = Task.Run(delegate
        {
            try
            {
                secondResult = service.ComputeSizeShared(@"C:\单飞测试目录", CancellationToken.None);
            }
            catch (Exception exception)
            {
                secondError = exception;
            }
        });
        // 留出时间让第二个订阅者加入进行中的计算，再放行底层计算
        Thread.Sleep(300);
        provider.Release.Set();

        Assert.True(Task.WaitAll(new Task[] { first, second }, TimeSpan.FromSeconds(5)), "两个订阅者应在限时内完成");
        Assert.Null(firstError);
        Assert.Null(secondError);
        Assert.Equal(12345, firstResult);
        Assert.Equal(12345, secondResult);
        Assert.Equal(1, provider.ComputeCallCount);
    }

    /// <summary>一个订阅者取消：底层计算继续，其余订阅者照常拿到结果。</summary>
    [Fact]
    public void ComputeSizeSharedKeepsRunningWhileOneSubscriberRemains()
    {
        BlockingDiskUsageProvider provider = new BlockingDiskUsageProvider();
        DiskScanService service = new DiskScanService(provider);
        using CancellationTokenSource cancellingSource = new CancellationTokenSource();
        long survivorResult = 0;
        Exception? survivorError = null;
        bool cancelledSubscriberExited = false;

        Task cancelled = Task.Run(delegate
        {
            try
            {
                service.ComputeSizeShared(@"C:\单飞测试目录", cancellingSource.Token);
            }
            catch (OperationCanceledException)
            {
                cancelledSubscriberExited = true;
            }
        });
        Assert.True(provider.Entered.Wait(TimeSpan.FromSeconds(5)), "第一个订阅者应已进入计算");

        Task survivor = Task.Run(delegate
        {
            try
            {
                survivorResult = service.ComputeSizeShared(@"C:\单飞测试目录", CancellationToken.None);
            }
            catch (Exception exception)
            {
                survivorError = exception;
            }
        });
        Thread.Sleep(300);
        cancellingSource.Cancel();

        Assert.True(Task.WaitAll(new Task[] { cancelled }, TimeSpan.FromSeconds(5)), "取消的订阅者应立即退出");
        Assert.True(cancelledSubscriberExited);
        Assert.Equal(1, provider.ComputeCallCount);

        // 底层计算仍在进行：放行后幸存订阅者应拿到结果
        provider.Release.Set();
        Assert.True(Task.WaitAll(new Task[] { survivor }, TimeSpan.FromSeconds(5)), "幸存订阅者应完成");
        Assert.Null(survivorError);
        Assert.Equal(12345, survivorResult);
        Assert.Equal(1, provider.ComputeCallCount);
    }

    /// <summary>全部订阅者取消：底层计算取消；之后同路径重新请求会发起新的计算。</summary>
    [Fact]
    public void ComputeSizeSharedCancelsWhenAllSubscribersGoneAndAllowsRestart()
    {
        BlockingDiskUsageProvider provider = new BlockingDiskUsageProvider();
        DiskScanService service = new DiskScanService(provider);
        using CancellationTokenSource source = new CancellationTokenSource();
        bool cancelledSubscriberExited = false;

        Task only = Task.Run(delegate
        {
            try
            {
                service.ComputeSizeShared(@"C:\单飞测试目录", source.Token);
            }
            catch (OperationCanceledException)
            {
                cancelledSubscriberExited = true;
            }
        });
        Assert.True(provider.Entered.Wait(TimeSpan.FromSeconds(5)), "订阅者应已进入计算");
        source.Cancel();
        Assert.True(Task.WaitAll(new Task[] { only }, TimeSpan.FromSeconds(5)), "订阅者取消后应退出");
        Assert.True(cancelledSubscriberExited);
        Assert.Equal(1, provider.ComputeCallCount);

        // 底层计算已随取消结束并注销：同路径再次请求会重新发起一次计算
        provider.Entered.Reset();
        Task restarted = Task.Run(delegate
        {
            try
            {
                service.ComputeSizeShared(@"C:\单飞测试目录", CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // 底层再次被取消也不影响本测试的重启断言
            }
        });
        Assert.True(provider.Entered.Wait(TimeSpan.FromSeconds(5)), "重启的计算应再次进入提供者");
        Assert.Equal(2, provider.ComputeCallCount);
        provider.Release.Set();
        Task.WaitAll(new Task[] { restarted }, TimeSpan.FromSeconds(5));
    }
}
