using System;
using System.Collections.Generic;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>标签视图契约测试：确保桌面端与网页端共用同一份视图清单，新增视图不会被单端遗漏。</summary>
public class TabContractTests
{
    /// <summary>网页端已实现专用渲染器的标签类型；新增类型必须同时更新网页页面与本清单。</summary>
    private static readonly string[] WebRenderedKinds = new string[]
    {
        TabKinds.File, TabKinds.Disk, TabKinds.Process, TabKinds.Search, TabKinds.Board, TabKinds.Connections,
        TabKinds.Compare, TabKinds.Startup,
    };

    [Fact]
    public void KnownKindsAreDistinctAndCoverWebRenderer()
    {
        Assert.Equal(8, TabKinds.All.Count);
        HashSet<string> seen = new HashSet<string>();
        foreach (string kind in TabKinds.All)
        {
            Assert.False(string.IsNullOrEmpty(kind));
            Assert.True(seen.Add(kind));
            Assert.Contains(kind, WebRenderedKinds);
        }
        Assert.Equal(TabKinds.All.Count, WebRenderedKinds.Length);
    }

    [Fact]
    public void OpenActionRoundTripsToKind()
    {
        foreach (string kind in TabKinds.All)
        {
            string action = TabKinds.ToOpenAction(kind);
            Assert.Equal(kind, TabKinds.FromOpenAction(action));
        }
        Assert.Null(TabKinds.FromOpenAction("open"));
        Assert.Null(TabKinds.FromOpenAction("openunknown"));
        Assert.Null(TabKinds.FromOpenAction("pin"));
        Assert.Null(TabKinds.FromOpenAction(string.Empty));
    }

    [Fact]
    public void KnownKindsImplementTabViewContract()
    {
        Dictionary<string, Type> expectedTypes = new Dictionary<string, Type>();
        expectedTypes[TabKinds.File] = typeof(FileTabViewModel);
        expectedTypes[TabKinds.Disk] = typeof(DiskViewPanelViewModel);
        expectedTypes[TabKinds.Process] = typeof(ProcessPanelViewModel);
        expectedTypes[TabKinds.Search] = typeof(SearchTabViewModel);
        expectedTypes[TabKinds.Board] = typeof(TaskBoardViewModel);
        expectedTypes[TabKinds.Connections] = typeof(ConnectionManagerViewModel);
        expectedTypes[TabKinds.Compare] = typeof(CompareTabViewModel);
        expectedTypes[TabKinds.Startup] = typeof(StartupManagerViewModel);
        foreach (string kind in TabKinds.All)
        {
            Assert.True(expectedTypes.ContainsKey(kind));
            Assert.True(typeof(ITabView).IsAssignableFrom(expectedTypes[kind]));
        }
    }

    [Fact]
    public void UnknownTabFallsBackToUnknownKind()
    {
        DebugTabState tabState = new DebugTabState();
        TabSnapshotBuilder.Fill(new object(), tabState);
        Assert.Equal(TabKinds.Unknown, tabState.TabType);
    }
}
