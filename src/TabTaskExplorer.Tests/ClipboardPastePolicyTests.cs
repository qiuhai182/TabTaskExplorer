using System.Collections.Generic;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>粘贴判定纯逻辑测试：状态标记、外部文件列表与内部剪贴板的优先级与移动 / 复制方式。</summary>
public class ClipboardPastePolicyTests
{
    /// <summary>剪贴板里有我方状态标记时，优先使用状态标记里的路径（含压缩包虚拟路径）。</summary>
    [Fact]
    public void 状态标记优先于外部文件列表与内部剪贴板()
    {
        ClipboardContent content = new ClipboardContent();
        content.HasTransfer = true;
        content.StatePaths = new List<string> { @"D:\甲\压缩包.zip/内部文件.txt" };
        content.StateIsCut = false;
        content.SystemFiles = new List<string> { @"D:\乙\外部文件.txt" };

        ClipboardPasteDecision decision = ClipboardPastePolicy.Decide(content,
            new List<string> { @"D:\丙\内部文件.txt" }, true);

        Assert.Equal(ClipboardPasteCategory.StateFormat, decision.Category);
        Assert.Single(decision.Paths);
        Assert.Equal(@"D:\甲\压缩包.zip/内部文件.txt", decision.Paths[0]);
        Assert.False(decision.IsCut);
    }

    /// <summary>没有状态标记但有外部文件列表时，使用外部程序复制进来的文件并按其剪切标记决定方式。</summary>
    [Fact]
    public void 无状态标记时使用外部文件列表()
    {
        ClipboardContent content = new ClipboardContent();
        content.HasTransfer = true;
        content.SystemFiles = new List<string> { @"D:\乙\外部文件.txt" };
        content.SystemCutIndicated = false;

        ClipboardPasteDecision decision = ClipboardPastePolicy.Decide(content,
            new List<string> { @"D:\丙\内部文件.txt" }, true);

        Assert.Equal(ClipboardPasteCategory.SystemFiles, decision.Category);
        Assert.Single(decision.Paths);
        Assert.Equal(@"D:\乙\外部文件.txt", decision.Paths[0]);
        Assert.False(decision.IsCut);
    }

    /// <summary>外部程序留下剪切标记时，粘贴按移动处理（与资源管理器剪切一致）。</summary>
    [Fact]
    public void 外部剪切标记为真时按移动粘贴()
    {
        ClipboardContent content = new ClipboardContent();
        content.HasTransfer = true;
        content.SystemFiles = new List<string> { @"D:\乙\外部文件.txt" };
        content.SystemCutIndicated = true;

        ClipboardPasteDecision decision = ClipboardPastePolicy.Decide(content, null, false);

        Assert.Equal(ClipboardPasteCategory.SystemFiles, decision.Category);
        Assert.True(decision.IsCut);
    }

    /// <summary>剪贴板有数据但没有任何文件（例如外部只复制了文本）时，判定为无可粘贴的文件。</summary>
    [Fact]
    public void 剪贴板有数据但没有文件时判定为无可粘贴文件()
    {
        ClipboardContent content = new ClipboardContent();
        content.HasTransfer = true;

        ClipboardPasteDecision decision = ClipboardPastePolicy.Decide(content,
            new List<string> { @"D:\丙\内部文件.txt" }, false);

        Assert.Equal(ClipboardPasteCategory.NoFileData, decision.Category);
        Assert.Empty(decision.Paths);
    }

    /// <summary>系统剪贴板为空时回退到内部剪贴板里的路径与复制方式。</summary>
    [Fact]
    public void 系统剪贴板为空时回退内部剪贴板复制方式()
    {
        ClipboardContent content = new ClipboardContent();

        ClipboardPasteDecision decision = ClipboardPastePolicy.Decide(content,
            new List<string> { @"D:\丙\内部文件.txt" }, false);

        Assert.Equal(ClipboardPasteCategory.InternalFallback, decision.Category);
        Assert.Single(decision.Paths);
        Assert.Equal(@"D:\丙\内部文件.txt", decision.Paths[0]);
        Assert.False(decision.IsCut);
    }

    /// <summary>读取系统剪贴板失败（结果为 null）且内部剪贴板有路径时，同样回退内部剪贴板的移动方式。</summary>
    [Fact]
    public void 读取系统剪贴板失败时回退内部剪贴板移动方式()
    {
        ClipboardPasteDecision decision = ClipboardPastePolicy.Decide(null,
            new List<string> { @"D:\丙\内部文件.txt" }, true);

        Assert.Equal(ClipboardPasteCategory.InternalFallback, decision.Category);
        Assert.True(decision.IsCut);
    }

    /// <summary>系统剪贴板与内部剪贴板都没有内容时，判定为剪贴板为空。</summary>
    [Fact]
    public void 系统剪贴板与内部剪贴板都为空时判定为空()
    {
        ClipboardContent content = new ClipboardContent();

        ClipboardPasteDecision decision = ClipboardPastePolicy.Decide(content,
            new List<string>(), false);

        Assert.Equal(ClipboardPasteCategory.Empty, decision.Category);
        Assert.Empty(decision.Paths);
        Assert.False(decision.IsCut);
    }
}
