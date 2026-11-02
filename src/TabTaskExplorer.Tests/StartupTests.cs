using System;
using System.Collections.Generic;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>启动项内核能力测试：开机标识稳定性、拉起参数校验与系统启动项枚举结果完整性。</summary>
public class StartupTests
{
    [Fact]
    public void BootSessionIdIsStableWithinSameSession()
    {
        string first = BootSessionIdentifier.GetCurrentSessionId();
        string second = BootSessionIdentifier.GetCurrentSessionId();
        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public void LauncherRejectsEmptyTarget()
    {
        StartupLaunchResult result = StartupItemLauncher.Launch(StartupItemType.Program, "   ", string.Empty);
        Assert.False(result.Success);
        Assert.Contains("启动目标为空", result.Message);
    }

    [Fact]
    public void LauncherRejectsMissingProgram()
    {
        string missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tabtaskexplorer-missing-startup-target.exe");
        StartupLaunchResult result = StartupItemLauncher.Launch(StartupItemType.Program, missingPath, string.Empty);
        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Message));
    }

    [Fact]
    public void WindowsProviderEnumeratesItemsWithUniqueIds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        TabTaskExplorer.Platform.Windows.WindowsStartupItemProvider provider =
            new TabTaskExplorer.Platform.Windows.WindowsStartupItemProvider();
        IReadOnlyList<StartupItem> items = provider.Enumerate();
        Assert.True(items.Count > 0);
        HashSet<string> ids = new HashSet<string>();
        foreach (StartupItem item in items)
        {
            Assert.False(string.IsNullOrEmpty(item.Id));
            Assert.False(string.IsNullOrEmpty(item.Name));
            Assert.True(ids.Add(item.Id));
            Assert.True(item.CanToggle || !string.IsNullOrEmpty(item.ToggleHint));
        }
    }
}
