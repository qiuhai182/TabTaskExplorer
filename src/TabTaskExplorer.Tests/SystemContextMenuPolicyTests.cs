using System.Collections.Generic;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>系统右键菜单通用策略测试：按右键目标筛选条目、命令模板占位符代入、命令行拆分。</summary>
public class SystemContextMenuPolicyTests
{
    /// <summary>星号选择器命中普通文件目标。</summary>
    [Fact]
    public void 星号选择器命中普通文件目标()
    {
        List<SystemContextMenuEntry> entries = new List<SystemContextMenuEntry>();
        entries.Add(CreateEntry(SystemMenuGroup.FileContext, "*", true, true));

        List<SystemContextMenuEntry> selected = SystemContextMenuPolicy.SelectForTarget(entries, @"C:\文档\报告.txt", false);

        Assert.True(selected.Count == 1, "星号选择器应命中普通文件目标，实际选中数量=" + selected.Count);
    }

    /// <summary>目录选择器只在目标为目录时命中。</summary>
    [Fact]
    public void 目录选择器只在目标为目录时命中()
    {
        List<SystemContextMenuEntry> entries = new List<SystemContextMenuEntry>();
        entries.Add(CreateEntry(SystemMenuGroup.FileContext, "Directory", true, true));

        List<SystemContextMenuEntry> 目录目标 = SystemContextMenuPolicy.SelectForTarget(entries, @"C:\文档", true);
        List<SystemContextMenuEntry> 文件目标 = SystemContextMenuPolicy.SelectForTarget(entries, @"C:\文档\报告.txt", false);

        Assert.True(目录目标.Count == 1, "目录选择器应在目标为目录时命中，实际选中数量=" + 目录目标.Count);
        Assert.True(文件目标.Count == 0, "目录选择器不应在目标为文件时命中，实际选中数量=" + 文件目标.Count);
    }

    /// <summary>扩展名选择器按目标扩展名匹配且忽略大小写。</summary>
    [Fact]
    public void 扩展名选择器忽略大小写匹配目标扩展名()
    {
        List<SystemContextMenuEntry> entries = new List<SystemContextMenuEntry>();
        entries.Add(CreateEntry(SystemMenuGroup.FileContext, ".txt", true, true));

        List<SystemContextMenuEntry> 大写扩展名 = SystemContextMenuPolicy.SelectForTarget(entries, @"C:\文档\报告.TXT", false);
        List<SystemContextMenuEntry> 其他扩展名 = SystemContextMenuPolicy.SelectForTarget(entries, @"C:\文档\报告.md", false);

        Assert.True(大写扩展名.Count == 1, "扩展名选择器应对大写目标扩展名忽略大小写命中，实际选中数量=" + 大写扩展名.Count);
        Assert.True(其他扩展名.Count == 0, "扩展名选择器不应命中扩展名不相符的目标，实际选中数量=" + 其他扩展名.Count);
    }

    /// <summary>不可唤起、非文件右键分组或已禁用的条目不入选。</summary>
    [Fact]
    public void 不可唤起或非文件组或已禁用的条目不入选()
    {
        List<SystemContextMenuEntry> entries = new List<SystemContextMenuEntry>();
        entries.Add(CreateEntry(SystemMenuGroup.FileContext, "*", false, true));
        entries.Add(CreateEntry(SystemMenuGroup.DesktopContext, "*", true, true));
        entries.Add(CreateEntry(SystemMenuGroup.FileContext, "*", true, false));

        List<SystemContextMenuEntry> selected = SystemContextMenuPolicy.SelectForTarget(entries, @"C:\文档\报告.txt", false);

        Assert.True(selected.Count == 0, "不可唤起、非文件右键分组、已禁用的条目都不应入选，实际选中数量=" + selected.Count);
    }

    /// <summary>带引号占位符代入目标路径后不出现双层引号。</summary>
    [Fact]
    public void 带引号占位符代入后不出现双层引号()
    {
        string result = SystemContextMenuPolicy.ExpandCommand("\"C:\\app\\tool.exe\" \"%1\"", @"C:\文档\报告.txt");

        Assert.False(result.Contains("\"\""), "占位符代入后不应残留双层引号，实际结果=" + result);
        Assert.True(result == "\"C:\\app\\tool.exe\" \"C:\\文档\\报告.txt\"", "命令模板应精确代入带引号的目标路径，实际结果=" + result);
    }

    /// <summary>裸露的百分号 V 占位符代入带引号的目标路径。</summary>
    [Fact]
    public void 裸露的百分号V占位符代入目标路径()
    {
        string result = SystemContextMenuPolicy.ExpandCommand("C:\\app\\tool.exe %V", @"C:\文档");

        Assert.True(result == "C:\\app\\tool.exe \"C:\\文档\"", "裸露占位符应代入带引号的目标路径，实际结果=" + result);
    }

    /// <summary>无关占位符（图标序号、附加参数）代入后被清除。</summary>
    [Fact]
    public void 无关占位符在代入目标路径后被清除()
    {
        string result = SystemContextMenuPolicy.ExpandCommand("C:\\app\\tool.exe \"%1\" %I %2", @"C:\文档\报告.txt");

        Assert.True(result == "C:\\app\\tool.exe \"C:\\文档\\报告.txt\"", "无关占位符应被清除且目标路径保留，实际结果=" + result);
    }

    /// <summary>空命令模板返回空串。</summary>
    [Fact]
    public void 空命令模板返回空串()
    {
        string result = SystemContextMenuPolicy.ExpandCommand(string.Empty, @"C:\文档\报告.txt");

        Assert.True(result == string.Empty, "空命令模板应返回空串，实际结果=" + result);
    }

    /// <summary>带引号的命令行拆出含空格的可执行文件路径与其余参数。</summary>
    [Fact]
    public void 带引号命令行拆出路径与参数()
    {
        SystemContextMenuPolicy.SplitCommandLine("\"C:\\Program Files\\tool.exe\" -a -b", out string fileName, out string arguments);

        Assert.True(fileName == "C:\\Program Files\\tool.exe", "带引号命令行应拆出引号内的可执行文件路径，实际结果=" + fileName);
        Assert.True(arguments == "-a -b", "带引号命令行应拆出其余参数，实际结果=" + arguments);
    }

    /// <summary>不带引号的命令行按首个空白拆分路径与参数。</summary>
    [Fact]
    public void 不带引号命令行按首个空白拆分()
    {
        SystemContextMenuPolicy.SplitCommandLine("C:\\tool.exe -a -b", out string fileName, out string arguments);

        Assert.True(fileName == "C:\\tool.exe", "不带引号命令行应拆出空白前的可执行文件路径，实际结果=" + fileName);
        Assert.True(arguments == "-a -b", "不带引号命令行应拆出其余参数，实际结果=" + arguments);
    }

    /// <summary>纯路径命令行只拆出可执行文件路径，参数为空。</summary>
    [Fact]
    public void 纯路径命令行拆出路径且参数为空()
    {
        SystemContextMenuPolicy.SplitCommandLine("C:\\tool.exe", out string fileName, out string arguments);

        Assert.True(fileName == "C:\\tool.exe", "纯路径命令行应拆出可执行文件路径，实际结果=" + fileName);
        Assert.True(arguments == string.Empty, "纯路径命令行不应拆出参数，实际结果=" + arguments);
    }

    /// <summary>目录空白处只取桌面 / 目录空白处分组里可唤起、已启用且匹配该目录的条目。</summary>
    [Fact]
    public void 目录空白处筛选出桌面分组可唤起条目()
    {
        List<SystemContextMenuEntry> entries = new List<SystemContextMenuEntry>();
        entries.Add(CreateEntry(SystemMenuGroup.DesktopContext, "Directory", true, true));
        entries.Add(CreateEntry(SystemMenuGroup.DesktopContext, "Desktop", true, true));
        entries.Add(CreateEntry(SystemMenuGroup.DesktopContext, "Directory", false, true));
        entries.Add(CreateEntry(SystemMenuGroup.DesktopContext, "Directory", true, false));
        entries.Add(CreateEntry(SystemMenuGroup.FileContext, "*", true, true));

        List<SystemContextMenuEntry> selected = SystemContextMenuPolicy.SelectForBackground(entries, @"C:\文档");

        Assert.True(selected.Count == 1, "空白处应只选出一条可唤起、已启用且匹配目录的桌面分组条目，实际选中数量=" + selected.Count);
        Assert.True(selected[0].ContextSelector == "Directory", "空白处应选中适用对象标识为目录的条目，实际标识=" + selected[0].ContextSelector);
    }

    private static SystemContextMenuEntry CreateEntry(SystemMenuGroup group, string selector, bool canExecute, bool isEnabled)
    {
        SystemContextMenuEntry entry = new SystemContextMenuEntry();
        entry.Group = group;
        entry.ContextSelector = selector;
        entry.CanExecute = canExecute;
        entry.IsEnabled = isEnabled;
        return entry;
    }
}
