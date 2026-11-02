using System.Collections.Generic;

namespace TabTaskExplorer.Kernel.Capture;

/// <summary>
/// 空区域预选器：非 Windows 平台没有窗口枚举能力时的降级实现。
/// 框选窗口仍然可全屏预选并手工拖动框选，不影响区域截图本身。
/// </summary>
public class NullScreenRegionDetector : IScreenRegionDetector
{
    /// <inheritdoc />
    public bool IsSupported
    {
        get { return false; }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScreenRegionCandidate> Detect(CaptureArea screenArea)
    {
        return new List<ScreenRegionCandidate>();
    }

    /// <inheritdoc />
    public bool TryGetPointerPosition(out int x, out int y)
    {
        x = 0;
        y = 0;
        return false;
    }
}
