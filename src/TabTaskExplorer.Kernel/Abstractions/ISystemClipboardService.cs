using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 系统剪贴板服务：把复制 / 剪切的路径写进操作系统剪贴板，把剪贴板内容读回来，
/// 使本程序与资源管理器等外部程序双向互通。三平台通过同一套跨平台接口实现，行为一致。
/// </summary>
public interface ISystemClipboardService
{
    /// <summary>当前环境是否支持系统剪贴板；不支持时上层回退内部剪贴板。</summary>
    bool IsSupported { get; }

    /// <summary>
    /// 把路径列表写入系统剪贴板（真实磁盘路径写文件格式供外部程序粘贴，全部路径写我方状态标记）。
    /// </summary>
    /// <param name="paths">要写入的路径列表（可含压缩包虚拟路径）。</param>
    /// <param name="isCut">true 为剪切（外部粘贴为移动），false 为复制。</param>
    /// <returns>写入成功返回 true，失败或不支持返回 false。</returns>
    Task<bool> WriteAsync(IReadOnlyList<string> paths, bool isCut);

    /// <summary>
    /// 把一段纯文本写入系统剪贴板（例如网页端地址），供粘贴到浏览器地址栏等场景使用。
    /// </summary>
    /// <param name="text">要写入剪贴板的文本。</param>
    /// <returns>写入成功返回 true，失败或不支持返回 false。</returns>
    Task<bool> WriteTextAsync(string text);

    /// <summary>
    /// 读取系统剪贴板内容（我方状态标记 + 外部文件列表 + 系统剪切标记）。
    /// </summary>
    /// <returns>读取结果；读取失败时返回 HasTransfer 为 false 的结果，由上层回退内部剪贴板。</returns>
    Task<ClipboardContent> ReadAsync();

    /// <summary>
    /// 清空系统剪贴板。剪切落地成功后调用，与资源管理器一致：剪切的文件粘贴走后不再保留在剪贴板里。
    /// </summary>
    /// <returns>清空成功返回 true，失败或不支持返回 false。</returns>
    Task<bool> ClearAsync();

    /// <summary>
    /// 轻量探测剪贴板是否含文件 / 文件夹（不读文件内容、不写常规读取日志），用于工具栏粘贴按钮的启用态。
    /// </summary>
    /// <returns>探测结果；剪贴板不可用时 HasFileData 为 false。</returns>
    Task<ClipboardPresence> ProbeAsync();
}
