namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 目录比对条目状态：两侧都存在且一致、内容或时间不同、仅单侧存在或类型不一致。
/// </summary>
public enum CompareEntryStatus
{
    /// <summary>两侧都存在且一致。</summary>
    Same = 0,

    /// <summary>两侧都存在但内容或时间不同。</summary>
    Different = 1,

    /// <summary>仅左侧存在。</summary>
    LeftOnly = 2,

    /// <summary>仅右侧存在。</summary>
    RightOnly = 3,

    /// <summary>两侧都存在，左侧修改时间较新。</summary>
    LeftNewer = 4,

    /// <summary>两侧都存在，右侧修改时间较新。</summary>
    RightNewer = 5,

    /// <summary>两侧同名条目类型不一致（一侧为目录，另一侧为文件）。</summary>
    TypeMismatch = 6,
}

/// <summary>
/// 目录比对选项：与 WinMerge 文件夹模式对应，支持递归、隐藏条目、按内容比较。
/// </summary>
public class DirectoryCompareOptions
{
    /// <summary>是否递归比较子目录（默认开启）。</summary>
    public bool IncludeSubDirectories { get; set; } = true;

    /// <summary>是否包含隐藏条目（默认包含）。</summary>
    public bool IncludeHidden { get; set; } = true;

    /// <summary>是否按文件内容比较；关闭时按大小与修改时间判断（更快）。</summary>
    public bool CompareFileContent { get; set; }

    /// <summary>文件名匹配是否忽略大小写（默认开启，与 Windows 文件夹比较习惯一致）。</summary>
    public bool IgnoreCase { get; set; } = true;

    /// <summary>修改时间差异容差（秒）：小于该差值的两个文件视为同一时间。</summary>
    public int ModifiedTimeToleranceSeconds { get; set; } = 2;
}

/// <summary>
/// 目录比对中的一条记录：相对路径、两侧完整路径、大小、修改时间与状态。
/// </summary>
public class DirectoryCompareEntry
{
    /// <summary>相对根目录的路径，使用正斜杠分隔（跨平台显示一致）。</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>条目名称（不含目录部分）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否为目录。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>左侧完整路径；左侧不存在时为空字符串。</summary>
    public string LeftPath { get; set; } = string.Empty;

    /// <summary>右侧完整路径；右侧不存在时为空字符串。</summary>
    public string RightPath { get; set; } = string.Empty;

    /// <summary>左侧是否存在。</summary>
    public bool LeftExists { get; set; }

    /// <summary>右侧是否存在。</summary>
    public bool RightExists { get; set; }

    /// <summary>左侧文件大小（字节）；目录或不存在时为 0。</summary>
    public long LeftLengthBytes { get; set; }

    /// <summary>右侧文件大小（字节）；目录或不存在时为 0。</summary>
    public long RightLengthBytes { get; set; }

    /// <summary>左侧最后修改时间（世界协调时）；不存在时为最小值。</summary>
    public DateTime LeftModifiedTimeUtc { get; set; }

    /// <summary>右侧最后修改时间（世界协调时）；不存在时为最小值。</summary>
    public DateTime RightModifiedTimeUtc { get; set; }

    /// <summary>比对状态。</summary>
    public CompareEntryStatus Status { get; set; }
}

/// <summary>
/// 目录比对结果：按相对路径排序的条目与各状态计数。
/// </summary>
public class DirectoryCompareResult
{
    /// <summary>全部比对条目。</summary>
    public List<DirectoryCompareEntry> Entries { get; set; } = new List<DirectoryCompareEntry>();

    /// <summary>一致条目数。</summary>
    public int SameCount { get; set; }

    /// <summary>内容或时间不同的条目数。</summary>
    public int DifferentCount { get; set; }

    /// <summary>仅左侧存在的条目数。</summary>
    public int LeftOnlyCount { get; set; }

    /// <summary>仅右侧存在的条目数。</summary>
    public int RightOnlyCount { get; set; }
}

/// <summary>
/// 文本比对行类型：两侧相同、两侧修改、仅左侧、仅右侧。
/// </summary>
public enum DiffRowKind
{
    /// <summary>两侧完全相同。</summary>
    Same = 0,

    /// <summary>两侧都有但内容不同（已配对的行内差异）。</summary>
    Modified = 1,

    /// <summary>仅左侧存在。</summary>
    LeftOnly = 2,

    /// <summary>仅右侧存在。</summary>
    RightOnly = 3,
}

/// <summary>
/// 文本比对中的一行：左侧行号与文本、右侧行号与文本、行类型以及行内差异区间。
/// </summary>
public class DiffRow
{
    /// <summary>左侧行号（1 基）；本行左侧不存在时为 0。</summary>
    public int LeftLineNumber { get; set; }

    /// <summary>左侧文本；本行左侧不存在时为空字符串。</summary>
    public string LeftText { get; set; } = string.Empty;

    /// <summary>右侧行号（1 基）；本行右侧不存在时为 0。</summary>
    public int RightLineNumber { get; set; }

    /// <summary>右侧文本；本行右侧不存在时为空字符串。</summary>
    public string RightText { get; set; } = string.Empty;

    /// <summary>行类型。</summary>
    public DiffRowKind Kind { get; set; }

    /// <summary>左侧行内差异起始字符位置。</summary>
    public int LeftChangedStart { get; set; }

    /// <summary>左侧行内差异字符长度。</summary>
    public int LeftChangedLength { get; set; }

    /// <summary>右侧行内差异起始字符位置。</summary>
    public int RightChangedStart { get; set; }

    /// <summary>右侧行内差异字符长度。</summary>
    public int RightChangedLength { get; set; }
}

/// <summary>
/// 文本比对结果：按行对齐后的完整行列表与统计信息。
/// </summary>
public class TextCompareResult
{
    /// <summary>按行对齐后的结果行。</summary>
    public List<DiffRow> Rows { get; set; } = new List<DiffRow>();

    /// <summary>左侧总行数。</summary>
    public int LeftLineCount { get; set; }

    /// <summary>右侧总行数。</summary>
    public int RightLineCount { get; set; }

    /// <summary>相同行数。</summary>
    public int SameCount { get; set; }

    /// <summary>修改行数（成对统计）。</summary>
    public int ModifiedCount { get; set; }

    /// <summary>仅左侧行数。</summary>
    public int LeftOnlyCount { get; set; }

    /// <summary>仅右侧行数。</summary>
    public int RightOnlyCount { get; set; }

    /// <summary>是否因文本过大而降级为线性对齐（结果仍可用但不保证最小差异）。</summary>
    public bool UsedFallbackAlignment { get; set; }
}
