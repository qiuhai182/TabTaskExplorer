namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 系统剪贴板的读取结果：我方状态标记、外部程序的文件列表与系统剪切标记三部分。
/// 状态标记里含压缩包虚拟路径；虚拟路径磁盘上不存在，只进状态标记，不进系统文件列表。
/// </summary>
public class ClipboardContent
{
    /// <summary>剪贴板里是否有任何数据；false 表示剪贴板为空或读取失败，上层回退内部剪贴板。</summary>
    public bool HasTransfer { get; set; }

    /// <summary>我方状态标记里的全部路径（含压缩包虚拟路径），粘贴时视为真值。</summary>
    public IReadOnlyList<string> StatePaths { get; set; } = new List<string>();

    /// <summary>我方状态标记记录的方式：true 为剪切，false 为复制。</summary>
    public bool StateIsCut { get; set; }

    /// <summary>外部程序复制进系统剪贴板的磁盘路径列表（只含真实存在的文件与文件夹）。</summary>
    public IReadOnlyList<string> SystemFiles { get; set; } = new List<string>();

    /// <summary>外部程序留下的剪切标记：true 表示粘贴应按移动处理。</summary>
    public bool SystemCutIndicated { get; set; }
}

/// <summary>剪贴板探测结果：只判断是否含文件数据与格式描述，不读文件内容，供工具栏粘贴按钮启用态使用。</summary>
public class ClipboardPresence
{
    /// <summary>是否含文件或文件夹（CF_HDROP 或我方状态标记）。纯文本与图片为 false。</summary>
    public bool HasFileData { get; set; }

    /// <summary>可粘贴的文件项数。</summary>
    public int FileCount { get; set; }

    /// <summary>中文格式描述，用于日志：文件列表（CF_HDROP）/ 应用状态标记 / 非文件（文本或图片）/ 空。</summary>
    public string FormatDescription { get; set; } = "空";

    /// <summary>剪贴板当前是否可打开；false 表示被其他进程占用（读取或写入都应放弃本次操作）。</summary>
    public bool ClipboardAvailable { get; set; } = true;
}
