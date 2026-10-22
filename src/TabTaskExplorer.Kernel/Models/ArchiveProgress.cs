using System;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 压缩 / 解压进度快照：随处理过程逐条上报，供界面状态栏显示。
/// </summary>
public class ArchiveProgress
{
    /// <summary>已处理完的条目数。</summary>
    public long EntriesDone { get; set; }

    /// <summary>条目总数；打开归档前未知时为 -1。</summary>
    public long EntriesTotal { get; set; } = -1;

    /// <summary>已处理的字节数（按未压缩大小累计）。</summary>
    public long BytesDone { get; set; }

    /// <summary>字节总数；未知时为 -1。</summary>
    public long BytesTotal { get; set; } = -1;

    /// <summary>当前正在处理的条目名称（归档内相对路径）。</summary>
    public string CurrentEntryName { get; set; } = string.Empty;
}
