namespace TabTaskExplorer.Kernel.Media;

/// <summary>
/// 媒体大类：内置查看器按此分派到图片视图、视频视图或音频视图。
/// </summary>
public enum MediaKind
{
    /// <summary>非媒体文件，或扩展名未登记。</summary>
    Unknown = 0,

    /// <summary>图片（静态图或动图）。</summary>
    Image = 1,

    /// <summary>视频（含音轨）。</summary>
    Video = 2,

    /// <summary>纯音频。</summary>
    Audio = 3,
}
