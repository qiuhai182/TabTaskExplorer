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
