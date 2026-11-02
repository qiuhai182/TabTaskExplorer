using System;
using System.Collections.Generic;
using System.IO;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Platform.Windows;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>系统外壳实时右键菜单测试：验证当前文件与文件夹能读取到已启用的系统菜单项。</summary>
public class SystemShellContextMenuTests
{
    [Fact]
    public void 外壳实时菜单包含当前文件与文件夹的已启用项()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        WindowsSystemContextMenuService service = new WindowsSystemContextMenuService();
        string directory = Path.Combine(Path.GetTempPath(), "tabtaskexplorer-shellmenu-" + Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(directory, "sample.txt");
        Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, "系统外壳右键菜单测试");
        try
        {
            IReadOnlyList<SystemContextMenuEntry> fileEntries = service.EnumerateShellMenu(filePath, false);
            Assert.True(fileEntries.Count > 0, "当前文件的系统外壳右键菜单应至少包含一个已启用项");
            int fileTotal = CountEntries(fileEntries);
            Assert.True(fileTotal > 0, "当前文件的系统外壳右键菜单全部条目数应为正数");

            IReadOnlyList<SystemContextMenuEntry> backgroundEntries = service.EnumerateShellMenu(directory, true);
            Assert.True(backgroundEntries.Count > 0, "当前文件夹空白处的系统外壳右键菜单应至少包含一个已启用项");
            int backgroundTotal = CountEntries(backgroundEntries);
            Assert.True(backgroundTotal > 0, "当前文件夹空白处的系统外壳右键菜单全部条目数应为正数");

            foreach (SystemContextMenuEntry entry in fileEntries)
            {
                Assert.True(entry.IsSeparator || entry.IsShellMenuEntry, "外壳菜单项应带有外壳来源标记");
                Assert.True(entry.IsSeparator || !string.IsNullOrEmpty(entry.DisplayName), "外壳菜单项应带有显示名称");
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>统计菜单项数量（含子菜单，不含分隔线）。</summary>
    private static int CountEntries(IReadOnlyList<SystemContextMenuEntry> entries)
    {
        int count = 0;
        foreach (SystemContextMenuEntry entry in entries)
        {
            if (!entry.IsSeparator)
            {
                count++;
            }
            count += CountEntries(entry.Children);
        }
        return count;
    }
}
