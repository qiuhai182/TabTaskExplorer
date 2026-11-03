using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TabTaskExplorer.App.Views;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>
/// 多显示器框选会话：为每个显示器创建一个独立遮罩窗口（各自使用该屏 DPI），
/// 统一处理光标跨屏切换、Tab/滚轮层级、跨屏拖拽与最终物理像素选区。
/// 单屏仍然走 RegionSelectionWindow 自身的轻量流程；本会话只在多屏时创建。
/// </summary>
public sealed class RegionSelectionSession
{
    private readonly IScreenRegionDetector detector;

    private readonly List<RegionSelectionWindow> windows = new List<RegionSelectionWindow>();

    private readonly TaskCompletionSource<CaptureArea?> completion = new TaskCompletionSource<CaptureArea?>();

    private readonly DispatcherTimer pointerTimer;

    private RegionSelectionWindow? activeWindow;

    private bool completed;

    private bool dragging;

    private int dragStartX;

    private int dragStartY;

    private int dragCurrentX;

    private int dragCurrentY;

    private bool dragMoved;

    public RegionSelectionSession(IScreenRegionDetector regionDetector)
    {
        this.detector = regionDetector;
        this.pointerTimer = new DispatcherTimer();
        this.pointerTimer.Interval = TimeSpan.FromMilliseconds(140);
        this.pointerTimer.Tick += this.OnPointerTimerTick;
    }

    /// <summary>等待用户完成或取消；结果为空表示取消。</summary>
    public Task<CaptureArea?> Completion
    {
        get { return this.completion.Task; }
    }

    /// <summary>登记一个显示器遮罩窗口（由窗口构造函数调用）。</summary>
    public void Register(RegionSelectionWindow window)
    {
        if (window != null && !this.windows.Contains(window))
        {
            this.windows.Add(window);
        }
    }

    /// <summary>显示全部显示器遮罩并选定光标所在屏幕作为活动层。</summary>
    public void ShowAll(Window? owner)
    {
        foreach (RegionSelectionWindow window in this.windows)
        {
            if (owner != null && owner.IsVisible)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }
            // 除活动屏幕外先统一清空高亮与提示，避免多个遮罩同时显示窗口边框
            window.SetSessionActive(false);
        }
        int pointerX;
        int pointerY;
        if (this.detector.TryGetPointerPosition(out pointerX, out pointerY))
        {
            this.EnsureActiveWindow(pointerX, pointerY);
        }
        if (this.activeWindow == null && this.windows.Count > 0)
        {
            RegionSelectionWindow first = this.windows[0];
            this.SetActiveWindow(first);
            CaptureArea area = first.ScreenArea;
            first.RefreshHoverAtPhysical(area.X + area.Width / 2, area.Y + area.Height / 2, "初始预选");
        }
        this.pointerTimer.Start();
        UiLogger.Log("截图", "多屏框选会话已启动：显示器遮罩数量=" + this.windows.Count
            + " 活动屏幕=" + DescribeWindow(this.activeWindow));
    }

    /// <summary>窗口关闭：移出会话；全部关闭且用户未完成时按取消处理。</summary>
    public void OnWindowClosed(RegionSelectionWindow window)
    {
        this.windows.Remove(window);
        if (this.activeWindow == window)
        {
            this.activeWindow = null;
        }
        if (!this.completed && this.windows.Count == 0)
        {
            this.completion.TrySetResult(null);
            UiLogger.Log("截图", "多屏框选窗口全部关闭：用户未完成框选，结果=已取消");
        }
    }

    /// <summary>鼠标按下：记录全局物理起点并准备跨屏拖拽。</summary>
    public void NotifyPointerPressed()
    {
        this.dragging = true;
        this.dragMoved = false;
        if (this.detector.TryGetPointerPosition(out int x, out int y))
        {
            this.dragStartX = x;
            this.dragStartY = y;
            this.dragCurrentX = x;
            this.dragCurrentY = y;
        }
        else
        {
            this.dragStartX = 0;
            this.dragStartY = 0;
            this.dragCurrentX = 0;
            this.dragCurrentY = 0;
        }
        UiLogger.Log("截图", "多屏框选按下：起点横坐标=" + this.dragStartX + " 起点纵坐标=" + this.dragStartY);
    }

    /// <summary>拖拽移动：用全局物理像素更新跨屏选区，返回是否已接管本次移动。</summary>
    public bool NotifyPointerMoved()
    {
        if (!this.dragging)
        {
            return false;
        }
        if (!this.detector.TryGetPointerPosition(out int x, out int y))
        {
            return true;
        }
        this.dragCurrentX = x;
        this.dragCurrentY = y;
        if (Math.Abs(x - this.dragStartX) < 5 && Math.Abs(y - this.dragStartY) < 5)
        {
            return true;
        }
        this.dragMoved = true;
        CaptureArea selection = this.NormalizeSelection(this.dragStartX, this.dragStartY, x, y);
        this.ApplySelection(selection);
        return true;
    }

    /// <summary>拖拽释放：足够大则完成跨屏选区；没有拖动则交回窗口自己的单击逻辑。</summary>
    public bool NotifyPointerReleased()
    {
        if (!this.dragging)
        {
            return false;
        }
        this.dragging = false;
        if (!this.dragMoved)
        {
            return false;
        }
        if (this.detector.TryGetPointerPosition(out int x, out int y))
        {
            this.dragCurrentX = x;
            this.dragCurrentY = y;
        }
        CaptureArea selection = this.NormalizeSelection(this.dragStartX, this.dragStartY,
            this.dragCurrentX, this.dragCurrentY);
        if (!selection.IsValid || selection.Width < 8 || selection.Height < 8)
        {
            UiLogger.Log("截图", "多屏框选结果过小已忽略：物理宽度=" + selection.Width
                + " 物理高度=" + selection.Height + " 结果=恢复悬停预选");
            if (this.activeWindow != null)
            {
                this.activeWindow.RefreshHoverAtPhysical(this.dragCurrentX, this.dragCurrentY, "恢复悬停");
            }
            return true;
        }
        UiLogger.Log("截图", "多屏框选完成：物理区域=" + selection);
        this.Complete(selection);
        return true;
    }

    /// <summary>鼠标移动（非拖拽）：切换到光标所在屏幕的遮罩，保证高亮跟随当前屏幕。</summary>
    public void NotifyPointerMovedHover()
    {
        if (this.dragging)
        {
            return;
        }
        if (this.detector.TryGetPointerPosition(out int x, out int y))
        {
            this.EnsureActiveWindow(x, y);
        }
    }

    /// <summary>取消整次多屏框选：记录来源与截图模式，关闭全部遮罩。</summary>
    public void Cancel(string sourceText, string captureMode)
    {
        if (this.completed)
        {
            return;
        }
        this.dragging = false;
        UiLogger.Log("截图", "截图取消：来源=" + sourceText
            + " 截图模式=" + captureMode
            + " 阶段=多屏框选遮罩"
            + " 结果=已撤销全部显示器遮罩，未生成产物");
        this.Complete(null);
    }

    /// <summary>完成并关闭全部遮罩；区域为空表示取消。</summary>
    public void Complete(CaptureArea? area)
    {
        if (this.completed)
        {
            return;
        }
        this.completed = true;
        this.pointerTimer.Stop();
        this.pointerTimer.Tick -= this.OnPointerTimerTick;
        this.completion.TrySetResult(area);
        List<RegionSelectionWindow> closing = new List<RegionSelectionWindow>(this.windows);
        this.windows.Clear();
        foreach (RegionSelectionWindow window in closing)
        {
            window.Close();
        }
    }

    private void OnPointerTimerTick(object? sender, EventArgs e)
    {
        if (this.completed || this.dragging)
        {
            return;
        }
        if (!this.detector.TryGetPointerPosition(out int x, out int y))
        {
            return;
        }
        this.EnsureActiveWindow(x, y);
    }

    /// <summary>按物理坐标找到所在屏幕的遮罩，切换活动层并立即刷新该屏命中。</summary>
    private void EnsureActiveWindow(int x, int y)
    {
        RegionSelectionWindow? target = null;
        foreach (RegionSelectionWindow window in this.windows)
        {
            CaptureArea area = window.ScreenArea;
            if (x >= area.X && x < area.X + area.Width && y >= area.Y && y < area.Y + area.Height)
            {
                target = window;
                break;
            }
        }
        if (target == null)
        {
            return;
        }
        if (this.activeWindow != target)
        {
            this.SetActiveWindow(target);
            target.RefreshHoverAtPhysical(x, y, "光标进入屏幕");
            UiLogger.Log("截图", "多屏框选活动屏幕切换：屏幕区域=" + target.ScreenArea
                + " 缩放比例=" + target.ScreenScaling);
        }
    }

    private void SetActiveWindow(RegionSelectionWindow target)
    {
        if (this.activeWindow != null && this.activeWindow != target)
        {
            this.activeWindow.SetSessionActive(false);
        }
        this.activeWindow = target;
        target.SetSessionActive(true);
        target.Activate();
        target.Focus();
    }

    private CaptureArea NormalizeSelection(int startX, int startY, int endX, int endY)
    {
        int left = Math.Min(startX, endX);
        int top = Math.Min(startY, endY);
        int width = Math.Abs(endX - startX);
        int height = Math.Abs(endY - startY);
        return new CaptureArea(left, top, width, height);
    }

    private void ApplySelection(CaptureArea selection)
    {
        string label = "跨屏框选 · " + selection.Width + " × " + selection.Height + " 像素";
        foreach (RegionSelectionWindow window in this.windows)
        {
            bool isActive = window == this.activeWindow;
            window.ApplyExternalSelection(selection, label, isActive);
        }
    }

    private static string DescribeWindow(RegionSelectionWindow? window)
    {
        return window == null ? "无" : window.ScreenArea.ToString();
    }
}
