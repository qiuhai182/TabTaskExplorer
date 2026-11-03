using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 网页端右键菜单项：把系统外壳菜单层级拍平成前端可直接渲染的结构。
/// 本程序自己的命令由前端固定分组构建，本结构只承载系统静态动词与第三方外壳扩展项。
/// </summary>
public class WebContextMenuItem
{
    /// <summary>条目来源：shell 表示系统外壳菜单项。</summary>
    public string Source { get; set; } = "shell";

    /// <summary>显示标题（已去掉助记符与快捷键后缀）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>快捷键说明（例如 Ctrl+C），网页端显示在标题右侧。</summary>
    public string Shortcut { get; set; } = string.Empty;

    /// <summary>访问键（标题中 &amp; 后的单个字符），网页端在标题对应字符上加下划线。</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>是否为分隔线。</summary>
    public bool IsSeparator { get; set; }

    /// <summary>是否可用；禁用项保留在菜单里但灰化。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>是否带勾选标记。</summary>
    public bool Checked { get; set; }

    /// <summary>是否为单选样式勾选。</summary>
    public bool Radio { get; set; }

    /// <summary>是否为系统默认项（网页端以粗体显示）。</summary>
    public bool IsDefault { get; set; }

    /// <summary>是否带盾牌标识（以管理员身份运行类命令）。</summary>
    public bool Shield { get; set; }

    /// <summary>是否含子菜单。</summary>
    public bool HasSubmenu
    {
        get { return this.Children.Count > 0; }
    }

    /// <summary>外壳菜单命令编号；子菜单与分隔线为 -1。</summary>
    public int CommandId { get; set; } = -1;

    /// <summary>图标 data URI；系统未提供可直接读取的位图时为空。</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>子菜单项。</summary>
    public List<WebContextMenuItem> Children { get; } = new List<WebContextMenuItem>();
}

/// <summary>一次网页端右键菜单枚举结果：系统项层级、可用性与降级说明。</summary>
public class WebContextMenuResult
{
    /// <summary>系统外壳菜单项（含分隔线与子菜单）。</summary>
    public List<WebContextMenuItem> Items { get; } = new List<WebContextMenuItem>();

    /// <summary>系统项是否可用（平台支持、本地标签且枚举成功）。</summary>
    public bool SystemMenuAvailable { get; set; }

    /// <summary>降级或失败原因；系统项可用时为空。</summary>
    public string SystemMenuNote { get; set; } = string.Empty;

    /// <summary>枚举耗时（毫秒），供页面诊断与日志。</summary>
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// 网页端右键菜单构建器：调用平台外壳枚举接口取得系统静态动词与全部第三方扩展，
/// 转换成网页端结构；枚举在独立 STA 线程执行并带超时，避免某个扩展无响应拖住整个页面请求。
/// </summary>
public static class WebContextMenuBuilder
{
    /// <summary>外壳菜单枚举超时（毫秒）：超时后返回受限菜单并明确标记系统项不可用。</summary>
    public const int EnumerationTimeoutMilliseconds = 2500;

    /// <summary>构建系统菜单层级；平台不支持或枚举失败/超时时返回带说明的空结果。</summary>
    /// <param name="service">系统右键菜单服务；为空表示平台不支持。</param>
    /// <param name="paths">目标路径列表（多选为全部选中项，空白处为当前目录）。</param>
    /// <param name="isBackground">是否为目录空白处菜单。</param>
    /// <param name="unavailableReason">调用方已判定的不可用原因（远程标签、压缩包、非 Windows 等）。</param>
    public static WebContextMenuResult Build(ISystemContextMenuService? service, IReadOnlyList<string> paths,
        bool isBackground, string unavailableReason)
    {
        WebContextMenuResult result = new WebContextMenuResult();
        if (service == null || !service.IsSupported)
        {
            result.SystemMenuAvailable = false;
            result.SystemMenuNote = unavailableReason.Length > 0 ? unavailableReason : "当前平台不支持系统右键菜单";
            return result;
        }
        if (paths == null || paths.Count == 0)
        {
            result.SystemMenuAvailable = false;
            result.SystemMenuNote = "没有可用的右键目标路径";
            return result;
        }
        List<SystemContextMenuEntry>? entries = null;
        string failure = string.Empty;
        long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        EnumerateOnStaThread(service, paths, isBackground, out entries, out failure);
        result.ElapsedMilliseconds = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        if (entries == null)
        {
            result.SystemMenuAvailable = false;
            result.SystemMenuNote = failure;
            UiLogger.Log("网页端右键菜单", "枚举系统外壳菜单失败：目标数=" + paths.Count
                + " 落点=" + (isBackground ? "空白处" : "条目") + " 原因=" + failure
                + " 结果=返回受限菜单，仅保留本程序命令与基础项");
            return result;
        }
        foreach (SystemContextMenuEntry entry in entries)
        {
            WebContextMenuItem item = ConvertEntry(entry);
            if (item != null)
            {
                result.Items.Add(item);
            }
        }
        TrimSeparators(result.Items);
        result.SystemMenuAvailable = true;
        result.SystemMenuNote = result.Items.Count == 0 ? "系统外壳未返回任何菜单项" : string.Empty;
        UiLogger.Log("网页端右键菜单", "枚举系统外壳菜单完成：目标数=" + paths.Count
            + " 落点=" + (isBackground ? "空白处" : "条目") + " 顶层条目数=" + result.Items.Count
            + " 全部条目数=" + CountItems(result.Items) + " 耗时毫秒=" + result.ElapsedMilliseconds
            + " 结果=成功，系统项与第三方扩展已随层级返回");
        return result;
    }

    /// <summary>在独立 STA 线程上枚举并等待结果：超时后放弃等待，避免扩展无响应阻塞网页请求。</summary>
    private static void EnumerateOnStaThread(ISystemContextMenuService service, IReadOnlyList<string> paths,
        bool isBackground, out List<SystemContextMenuEntry>? entries, out string failure)
    {
        EnumerationState state = new EnumerationState();
        Thread thread = new Thread(delegate ()
        {
            try
            {
                state.Entries = new List<SystemContextMenuEntry>(service.EnumerateShellMenu(paths, isBackground));
            }
            catch (Exception error)
            {
                state.Error = error;
            }
            finally
            {
                state.Completed.Set();
            }
        });
        thread.IsBackground = true;
        thread.Name = "网页端右键菜单枚举";
        if (OperatingSystem.IsWindows())
        {
            try
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            catch (Exception)
            {
                // 运行时不允许设置套间时保持默认，外壳扩展仍可能正常工作
            }
        }
        thread.Start();
        if (!state.Completed.Wait(EnumerationTimeoutMilliseconds))
        {
            entries = null;
            failure = "系统外壳菜单枚举超时（" + EnumerationTimeoutMilliseconds + " 毫秒内未返回），可能是某个外壳扩展无响应";
            return;
        }
        if (state.Error != null)
        {
            entries = null;
            failure = "系统外壳菜单枚举出错：" + state.Error.Message;
            return;
        }
        entries = state.Entries ?? new List<SystemContextMenuEntry>();
        failure = string.Empty;
    }

    /// <summary>把一条系统条目转换成网页端条目；分隔线与空标题按规则保留或丢弃。</summary>
    private static WebContextMenuItem? ConvertEntry(SystemContextMenuEntry entry)
    {
        WebContextMenuItem item = new WebContextMenuItem();
        item.IsSeparator = entry.IsSeparator;
        if (entry.IsSeparator)
        {
            return item;
        }
        item.Title = entry.DisplayName;
        if (item.Title.Length == 0)
        {
            return null;
        }
        item.Shortcut = entry.AcceleratorText;
        item.AccessKey = entry.AccessKey;
        item.Enabled = entry.IsEnabled;
        item.Checked = entry.IsChecked;
        item.Radio = entry.IsRadioCheck;
        item.IsDefault = entry.IsDefault;
        item.Shield = entry.RequiresElevation;
        item.Icon = entry.IconDataUri;
        item.CommandId = entry.Children.Count > 0 ? -1 : entry.ShellCommandId;
        foreach (SystemContextMenuEntry child in entry.Children)
        {
            WebContextMenuItem childItem = ConvertEntry(child);
            if (childItem != null)
            {
                item.Children.Add(childItem);
            }
        }
        TrimSeparators(item.Children);
        // 子菜单项自身没有命令编号；禁用的子菜单仍保留，让用户看到扩展存在但不可用
        if (item.Children.Count > 0)
        {
            item.CommandId = -1;
        }
        return item;
    }

    /// <summary>去掉开头、结尾与连续重复的分隔线，避免网页端出现空白分组。</summary>
    private static void TrimSeparators(List<WebContextMenuItem> items)
    {
        while (items.Count > 0 && items[0].IsSeparator)
        {
            items.RemoveAt(0);
        }
        while (items.Count > 0 && items[items.Count - 1].IsSeparator)
        {
            items.RemoveAt(items.Count - 1);
        }
        for (int index = items.Count - 1; index > 0; index--)
        {
            if (items[index].IsSeparator && items[index - 1].IsSeparator)
            {
                items.RemoveAt(index);
            }
        }
    }

    /// <summary>统计条目总数（含子菜单，不含分隔线）。</summary>
    private static int CountItems(List<WebContextMenuItem> items)
    {
        int count = 0;
        foreach (WebContextMenuItem item in items)
        {
            if (!item.IsSeparator)
            {
                count++;
            }
            count += CountItems(item.Children);
        }
        return count;
    }

    /// <summary>枚举线程的状态容器。</summary>
    private sealed class EnumerationState
    {
        /// <summary>枚举结果。</summary>
        public List<SystemContextMenuEntry>? Entries { get; set; }

        /// <summary>枚举异常；成功时为空。</summary>
        public Exception? Error { get; set; }

        /// <summary>枚举完成信号。</summary>
        public ManualResetEventSlim Completed { get; } = new ManualResetEventSlim(false);
    }
}

/// <summary>
/// 网页端右键目标校验：只允许当前已打开本地标签内的路径，防止通过右键接口对任意路径执行 Shell 动词。
/// </summary>
public static class WebContextMenuPolicy
{
    /// <summary>
    /// 校验目标路径：空白处必须等于某个已打开目录；条目必须与某个已打开目录同父，
    /// 且（若提供可见条目集合）必须出现在该集合内。任一条不满足即抛 <see cref="UnauthorizedAccessException"/>。
    /// </summary>
    /// <param name="openDirectories">当前已打开本地标签的目录列表。</param>
    /// <param name="visibleItems">当前标签可见条目路径；为空表示不额外限制。</param>
    /// <param name="paths">请求的目标路径。</param>
    /// <param name="isBackground">是否为空白处菜单。</param>
    public static void Validate(IReadOnlyList<string> openDirectories, IReadOnlyCollection<string> visibleItems,
        IReadOnlyList<string> paths, bool isBackground)
    {
        if (paths == null || paths.Count == 0)
        {
            throw new UnauthorizedAccessException("右键请求缺少目标路径");
        }
        if (paths.Count > 64)
        {
            throw new UnauthorizedAccessException("右键目标数量过多");
        }
        if (openDirectories == null || openDirectories.Count == 0)
        {
            openDirectories = Array.Empty<string>();
        }
        List<string> normalizedDirectories = new List<string>();
        foreach (string directory in openDirectories)
        {
            string normalized = NormalizePath(directory);
            if (normalized.Length > 0)
            {
                normalizedDirectories.Add(normalized);
            }
        }
        if (isBackground)
        {
            if (normalizedDirectories.Count == 0)
            {
                throw new UnauthorizedAccessException("当前没有已打开的本地目录，不能使用系统右键菜单");
            }
            if (paths.Count != 1)
            {
                throw new UnauthorizedAccessException("空白处右键只能指定一个目录");
            }
            string target = NormalizePath(paths[0]);
            bool matched = false;
            foreach (string directory in normalizedDirectories)
            {
                if (string.Equals(directory, target, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                throw new UnauthorizedAccessException("空白处右键目标不在当前已打开的标签内：" + paths[0]);
            }
            return;
        }
        string? parentDirectory = null;
        foreach (string path in paths)
        {
            string normalized = NormalizePath(path);
            if (IsDriveRoot(normalized))
            {
                // 盘符根没有上级目录，只能作为当前标签的可见条目使用（例如此电脑视图中的驱动器行）
                if (visibleItems == null || !ContainsPath(visibleItems, normalized))
                {
                    throw new UnauthorizedAccessException("盘符根不在当前标签的可见条目内：" + path);
                }
                continue;
            }
            string parent = NormalizePath(Path.GetDirectoryName(normalized) ?? string.Empty);
            if (parent.Length == 0)
            {
                throw new UnauthorizedAccessException("右键目标没有上级目录：" + path);
            }
            if (parentDirectory == null)
            {
                parentDirectory = parent;
            }
            else if (!string.Equals(parentDirectory, parent, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("多选右键目标必须位于同一目录");
            }
            bool matched = false;
            foreach (string directory in normalizedDirectories)
            {
                if (string.Equals(directory, parent, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                throw new UnauthorizedAccessException("右键目标不在当前已打开的标签内：" + path);
            }
            if (visibleItems != null && visibleItems.Count > 0 && !ContainsPath(visibleItems, normalized))
            {
                throw new UnauthorizedAccessException("右键目标不在当前标签的可见条目内：" + path);
            }
        }
    }

    /// <summary>判断规范化后的路径是否为盘符根（如 D:\）。</summary>
    private static bool IsDriveRoot(string normalizedPath)
    {
        if (normalizedPath.Length == 0)
        {
            return false;
        }
        string? root = Path.GetPathRoot(normalizedPath);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }
        return string.Equals(NormalizePath(root), normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断可见条目集合里是否包含规范化后的路径。</summary>
    private static bool ContainsPath(IReadOnlyCollection<string> visibleItems, string normalizedPath)
    {
        foreach (string item in visibleItems)
        {
            if (string.Equals(NormalizePath(item), normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>规范化路径用于比较：展开为绝对路径并去掉末尾分隔符；无法解析时返回空。</summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
        try
        {
            string full = Path.GetFullPath(path.Trim());
            if (full.Length == 0)
            {
                return string.Empty;
            }
            if (full.Length == 3 && full[1] == ':' && (full[2] == '\\' || full[2] == '/'))
            {
                return full;
            }
            return full.TrimEnd('\\', '/');
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>判断路径是否位于指定目录之下（含目录本身）。</summary>
    public static bool IsUnderDirectory(string path, string directory)
    {
        string normalizedPath = NormalizePath(path);
        string normalizedDirectory = NormalizePath(directory);
        if (normalizedPath.Length == 0 || normalizedDirectory.Length == 0)
        {
            return false;
        }
        if (string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        string prefix = normalizedDirectory + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
