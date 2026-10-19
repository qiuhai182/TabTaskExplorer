using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 非 Windows 平台的留空实现：不做任何处理。
/// 系统标题隐藏是 Windows 特有能力（WS_CAPTION 样式位），其他平台此能力留空。
/// </summary>
public class NullWindowCaptionHider : IWindowCaptionHider
{
    public void HideCaption(IntPtr nativeHandle)
    {
        // 留空：非 Windows 平台无此需求
    }
}
