using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>粘贴内容的判定结果分类。</summary>
public enum ClipboardPasteCategory
{
    /// <summary>使用我方状态标记里的路径（含压缩包虚拟路径，视为真值）。</summary>
    StateFormat,

    /// <summary>使用外部程序复制进系统剪贴板的文件列表。</summary>
    SystemFiles,

    /// <summary>系统剪贴板无数据，回退内部剪贴板字段。</summary>
    InternalFallback,

    /// <summary>剪贴板有数据但没有任何文件（例如外部只复制了文本）。</summary>
    NoFileData,

    /// <summary>剪贴板与内部剪贴板都为空。</summary>
    Empty,
}

/// <summary>粘贴内容的判定结果。</summary>
public class ClipboardPasteDecision
{
    /// <summary>判定分类。</summary>
    public ClipboardPasteCategory Category { get; set; }

    /// <summary>要落地的路径列表；分类为「无可粘贴的文件」或「剪贴板为空」时为空列表。</summary>
    public IReadOnlyList<string> Paths { get; set; } = new List<string>();

    /// <summary>true 表示移动（剪切），false 表示复制。</summary>
    public bool IsCut { get; set; }
}

/// <summary>
/// 粘贴判定纯逻辑：按「我方状态标记 → 外部文件列表 → 内部剪贴板」的优先级决定粘贴内容与移动 / 复制方式，
/// 桌面端与网页端共用同一套判定，保证跨平台行为一致。
/// </summary>
public static class ClipboardPastePolicy
{
    /// <summary>
    /// 决定粘贴应使用的路径与方式。
    /// </summary>
    /// <param name="content">系统剪贴板读取结果；读取失败时为 null。</param>
    /// <param name="internalPaths">内部剪贴板路径列表（我方复制 / 剪切后暂存）。</param>
    /// <param name="internalIsCut">内部剪贴板是否为剪切模式。</param>
    /// <returns>判定结果：路径、移动 / 复制方式与分类。</returns>
    public static ClipboardPasteDecision Decide(ClipboardContent? content,
        IReadOnlyList<string>? internalPaths, bool internalIsCut)
    {
        ClipboardPasteDecision decision = new ClipboardPasteDecision();
        if (content != null && content.HasTransfer)
        {
            if (content.StatePaths.Count > 0)
            {
                decision.Category = ClipboardPasteCategory.StateFormat;
                decision.Paths = content.StatePaths;
                decision.IsCut = content.StateIsCut;
                return decision;
            }
            if (content.SystemFiles.Count > 0)
            {
                decision.Category = ClipboardPasteCategory.SystemFiles;
                decision.Paths = content.SystemFiles;
                decision.IsCut = content.SystemCutIndicated;
                return decision;
            }
            decision.Category = ClipboardPasteCategory.NoFileData;
            return decision;
        }
        if (internalPaths != null && internalPaths.Count > 0)
        {
            decision.Category = ClipboardPasteCategory.InternalFallback;
            decision.Paths = internalPaths;
            decision.IsCut = internalIsCut;
            return decision;
        }
        decision.Category = ClipboardPasteCategory.Empty;
        return decision;
    }
}
