using System.Collections.Generic;

namespace TabTaskExplorer.Kernel.Capture;

/// <summary>预选区域的类型：全屏、窗口或窗口内子视图。</summary>
public enum ScreenRegionKind
{
    /// <summary>当前显示器的完整屏幕区域。</summary>
    FullScreen,

    /// <summary>一个顶层窗口。</summary>
    Window,

    /// <summary>顶层窗口内部的子视图（Win32 子窗口）。</summary>
    ChildView,
}

/// <summary>
/// 区域截图框选窗口的一个候选区域：记录物理像素范围、类型、显示名称与层级信息，
/// 供框选窗口在鼠标移动时自动预选鼠标下方的窗口或子视图。
/// </summary>
public class ScreenRegionCandidate
{
    /// <summary>候选区域的物理像素范围。</summary>
    public CaptureArea Area { get; set; } = CaptureArea.Empty;

    /// <summary>候选区域类型。</summary>
    public ScreenRegionKind Kind { get; set; } = ScreenRegionKind.FullScreen;

    /// <summary>用于界面展示的名称（窗口标题或子视图类名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>窗口句柄等平台原生标识；子视图记录所属顶层窗口的标识。</summary>
    public long NativeHandle { get; set; }

    /// <summary>子视图所属顶层窗口的原生标识；顶层窗口与全屏为空。</summary>
    public long ParentHandle { get; set; }

    /// <summary>层级深度：全屏为 0，顶层窗口为 1，子视图逐级递增。</summary>
    public int Depth { get; set; }

    /// <summary>顶层窗口的层叠顺序：数值越小越靠前。</summary>
    public int ZOrder { get; set; }
}

/// <summary>
/// 屏幕区域预选器：在区域框选窗口显示前，枚举当前屏幕上可预选的窗口与子视图。
/// Windows 提供完整实现；其他平台此能力留空，框选窗口降级为全屏预选与手工拖动。
/// </summary>
public interface IScreenRegionDetector
{
    /// <summary>当前平台是否支持窗口与子视图预选。</summary>
    bool IsSupported { get; }

    /// <summary>
    /// 枚举指定屏幕区域内的候选区域。
    /// </summary>
    /// <param name="screenArea">当前框选窗口覆盖的屏幕物理像素区域。</param>
    /// <returns>候选区域列表；平台不支持时返回空列表。</returns>
    IReadOnlyList<ScreenRegionCandidate> Detect(CaptureArea screenArea);

    /// <summary>
    /// 读取当前鼠标指针的物理屏幕坐标，用于框选窗口打开时立即预选鼠标下方区域。
    /// </summary>
    /// <param name="x">指针横坐标（物理像素）。</param>
    /// <param name="y">指针纵坐标（物理像素）。</param>
    /// <returns>读取成功返回 true；平台不支持或读取失败返回 false。</returns>
    bool TryGetPointerPosition(out int x, out int y);

    /// <summary>
    /// 按物理屏幕坐标实时命中光标下方的窗口及其嵌套子视图。
    /// 返回顺序为「顶层窗口 → 逐级子视图」，不包含全屏候选；平台不支持或命中外壳窗口时返回空列表。
    /// 框选窗口据此支持 Tab / 滚轮在窗口、控件与全屏之间切换，并在鼠标静止时持续刷新。
    /// </summary>
    /// <param name="x">物理屏幕横坐标。</param>
    /// <param name="y">物理屏幕纵坐标。</param>
    /// <returns>命中链；没有可截取窗口时为空列表。</returns>
    IReadOnlyList<ScreenRegionCandidate> HitTest(int x, int y);
}
