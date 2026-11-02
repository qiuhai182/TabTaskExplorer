using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 目录比对表格行：状态、相对路径、左右两侧的时间与大小。
/// </summary>
public class DirectoryCompareRow
{
    /// <summary>相对根目录的路径（正斜杠分隔）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否为目录。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>比对状态。</summary>
    public CompareEntryStatus Status { get; set; }

    /// <summary>状态中文文字。</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>状态强调色（十六进制）。</summary>
    public string StatusColor { get; set; } = "#4CAF50";

    /// <summary>左侧完整路径；不存在时为空字符串。</summary>
    public string LeftPath { get; set; } = string.Empty;

    /// <summary>右侧完整路径；不存在时为空字符串。</</summary>
    public string RightPath { get; set; } = string.Empty;

    /// <summary>左侧是否存在。</summary>
    public bool LeftExists { get; set; }

    /// <summary>右侧是否存在。</summary>
    public bool RightExists { get; set; }

    /// <summary>左侧信息文字：修改时间与大小（不存在时显示“无”）。</summary>
    public string LeftInfoText { get; set; } = string.Empty;

    /// <summary>右侧信息文字：修改时间与大小（不存在时显示“无”）。</summary>
    public string RightInfoText { get; set; } = string.Empty;

    /// <summary>类型图标分类键。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.File;

    /// <summary>类型图标颜色。</summary>
    public string IconColor { get; set; } = FileIconCatalog.GetAccent(FileIconCatalog.File);

    /// <summary>是否为差异行（相同行不参与差异导航）。</summary>
    public bool IsDifferent
    {
        get { return this.Status != CompareEntryStatus.Same; }
    }

    /// <summary>状态样式键，供界面与网页端套用颜色样式。</summary>
    public string StatusKey
    {
        get
        {
            switch (this.Status)
            {
                case CompareEntryStatus.Same:
                    return "same";
                case CompareEntryStatus.LeftOnly:
                    return "leftonly";
                case CompareEntryStatus.RightOnly:
                    return "rightonly";
                case CompareEntryStatus.LeftNewer:
                    return "leftnewer";
                case CompareEntryStatus.RightNewer:
                    return "rightnewer";
                case CompareEntryStatus.TypeMismatch:
                    return "type";
                default:
                    return "different";
            }
        }
    }

    /// <summary>把比对状态翻译成中文文字。</summary>
    public static string DescribeStatus(CompareEntryStatus status)
    {
        switch (status)
        {
            case CompareEntryStatus.Same:
                return "相同";
            case CompareEntryStatus.LeftOnly:
                return "仅左侧";
            case CompareEntryStatus.RightOnly:
                return "仅右侧";
            case CompareEntryStatus.LeftNewer:
                return "左侧较新";
            case CompareEntryStatus.RightNewer:
                return "右侧较新";
            case CompareEntryStatus.TypeMismatch:
                return "类型不同";
            default:
                return "不同";
        }
    }

    /// <summary>取比对状态对应的强调色。</summary>
    public static string DescribeStatusColor(CompareEntryStatus status)
    {
        switch (status)
        {
            case CompareEntryStatus.Same:
                return "#4CAF50";
            case CompareEntryStatus.LeftOnly:
                return "#F2B23E";
            case CompareEntryStatus.RightOnly:
                return "#2F6FD0";
            case CompareEntryStatus.LeftNewer:
            case CompareEntryStatus.RightNewer:
                return "#8E7CC3";
            default:
                return "#E05252";
        }
    }

    /// <summary>格式化一侧信息：时间 · 大小 / 文件夹 / 无。</summary>
    public static string FormatSideInfo(bool exists, bool isDirectory, long lengthBytes, DateTime modifiedUtc)
    {
        if (!exists)
        {
            return "（无）";
        }
        string timeText = modifiedUtc == DateTime.MinValue
            ? "时间未知"
            : modifiedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        if (isDirectory)
        {
            return timeText + " · 文件夹";
        }
        return timeText + " · " + FormatSize(lengthBytes);
    }

    /// <summary>把字节数格式化成易读文字。</summary>
    public static string FormatSize(long bytes)
    {
        double value = bytes;
        if (value < 1024)
        {
            return value.ToString("0") + " B";
        }
        value = value / 1024;
        if (value < 1024)
        {
            return value.ToString("0.#") + " KB";
        }
        value = value / 1024;
        if (value < 1024)
        {
            return value.ToString("0.#") + " MB";
        }
        value = value / 1024;
        return value.ToString("0.##") + " GB";
    }
}

/// <summary>
/// 文件比对表格行：左右行号、文本与行内差异片段。
/// </summary>
public class FileDiffRow
{
    /// <summary>左侧行号（1 基）；本行左侧不存在时为 0。</summary>
    public int LeftLineNumber { get; private set; }

    /// <summary>右侧行号（1 基）；本行右侧不存在时为 0。</summary>
    public int RightLineNumber { get; private set; }

    /// <summary>左侧行号显示文字（行号为 0 时为空）。</summary>
    public string LeftLineNumberText
    {
        get { return this.LeftLineNumber > 0 ? this.LeftLineNumber.ToString() : string.Empty; }
    }

    /// <summary>右侧行号显示文字（行号为 0 时为空）。</summary>
    public string RightLineNumberText
    {
        get { return this.RightLineNumber > 0 ? this.RightLineNumber.ToString() : string.Empty; }
    }

    /// <summary>左侧行内差异之前的前缀文本。</summary>
    public string LeftPrefix { get; private set; } = string.Empty;

    /// <summary>左侧行内差异文本。</summary>
    public string LeftChanged { get; private set; } = string.Empty;

    /// <summary>左侧行内差异之后的剩余文本。</summary>
    public string LeftSuffix { get; private set; } = string.Empty;

    /// <summary>右侧行内差异之前的前缀文本。</summary>
    public string RightPrefix { get; private set; } = string.Empty;

    /// <summary>右侧行内差异文本。</summary>
    public string RightChanged { get; private set; } = string.Empty;

    /// <summary>右侧行内差异之后的剩余文本。</summary>
    public string RightSuffix { get; private set; } = string.Empty;

    /// <summary>左侧是否存在行内差异片段。</</summary>
    public bool HasLeftChange
    {
        get { return this.LeftChanged.Length > 0; }
    }

    /// <summary>右侧是否存在行内差异片段。</</summary>
    public bool HasRightChange
    {
        get { return this.RightChanged.Length > 0; }
    }

    /// <summary>行类型。</summary>
    public DiffRowKind Kind { get; private set; }

    /// <summary>是否为相同行。</summary>
    public bool IsSame
    {
        get { return this.Kind == DiffRowKind.Same; }
    }

    /// <summary>是否为修改行。</summary>
    public bool IsModified
    {
        get { return this.Kind == DiffRowKind.Modified; }
    }

    /// <summary>是否为仅左侧行。</</summary>
    public bool IsLeftOnly
    {
        get { return this.Kind == DiffRowKind.LeftOnly; }
    }

    /// <summary>是否为仅右侧行。</</summary>
    public bool IsRightOnly
    {
        get { return this.Kind == DiffRowKind.RightOnly; }
    }

    /// <summary>是否为差异行（非相同行）。</summary>
    public bool IsDifferent
    {
        get { return this.Kind != DiffRowKind.Same; }
    }

    /// <summary>行类型样式键，供界面与网页端套用颜色样式。</summary>
    public string KindKey
    {
        get
        {
            switch (this.Kind)
            {
                case DiffRowKind.Modified:
                    return "modified";
                case DiffRowKind.LeftOnly:
                    return "leftonly";
                case DiffRowKind.RightOnly:
                    return "rightonly";
                default:
                    return "same";
            }
        }
    }

    /// <summary>由内核比对行构建界面行，并把行内差异区间切成三段文本。</summary>
    public static FileDiffRow FromDiff(DiffRow diffRow)
    {
        FileDiffRow row = new FileDiffRow();
        row.LeftLineNumber = diffRow.LeftLineNumber;
        row.RightLineNumber = diffRow.RightLineNumber;
        row.Kind = diffRow.Kind;

        if (diffRow.Kind == DiffRowKind.Modified)
        {
            row.LeftPrefix = Slice(diffRow.LeftText, 0, diffRow.LeftChangedStart);
            row.LeftChanged = Slice(diffRow.LeftText, diffRow.LeftChangedStart, diffRow.LeftChangedLength);
            row.LeftSuffix = Slice(diffRow.LeftText, diffRow.LeftChangedStart + diffRow.LeftChangedLength,
                diffRow.LeftText.Length - diffRow.LeftChangedStart - diffRow.LeftChangedLength);
            row.RightPrefix = Slice(diffRow.RightText, 0, diffRow.RightChangedStart);
            row.RightChanged = Slice(diffRow.RightText, diffRow.RightChangedStart, diffRow.RightChangedLength);
            row.RightSuffix = Slice(diffRow.RightText, diffRow.RightChangedStart + diffRow.RightChangedLength,
                diffRow.RightText.Length - diffRow.RightChangedStart - diffRow.RightChangedLength);
            return row;
        }

        if (diffRow.Kind == DiffRowKind.LeftOnly)
        {
            row.LeftChanged = diffRow.LeftText;
            return row;
        }
        if (diffRow.Kind == DiffRowKind.RightOnly)
        {
            row.RightChanged = diffRow.RightText;
            return row;
        }
        row.LeftPrefix = diffRow.LeftText;
        row.RightPrefix = diffRow.RightText;
        return row;
    }

    /// <summary>安全截取字符串片段，越界时返回空字符串。</summary>
    private static string Slice(string text, int start, int length)
    {
        if (string.IsNullOrEmpty(text) || start < 0 || length <= 0 || start >= text.Length)
        {
            return string.Empty;
        }
        int safeLength = Math.Min(length, text.Length - start);
        return text.Substring(start, safeLength);
    }
}
