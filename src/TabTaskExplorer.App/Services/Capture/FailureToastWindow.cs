using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>
/// 截图失败提示浮层：以「托盘气泡」形态出现在屏幕右下角，不激活、不抢前台焦点、不占任务栏，
/// 数秒后自动关闭。Avalonia 的 TrayIcon 没有跨平台气泡接口，这里用无焦点置顶小窗实现同等效果。
/// </summary>
public class FailureToastWindow : Window
{
    private const int ToastWidth = 380;
    private const int ToastHeight = 92;
    private const int MarginRight = 16;
    private const int MarginBottom = 48;

    private DispatcherTimer? closeTimer;
    private readonly PixelRect workingArea;

    /// <summary>按标题、原因与目标屏幕工作区构造提示浮层。</summary>
    public FailureToastWindow(string title, string message, PixelRect targetWorkingArea)
    {
        this.workingArea = targetWorkingArea;
        this.ShowActivated = false;
        this.ShowInTaskbar = false;
        this.Topmost = true;
        this.WindowDecorations = WindowDecorations.None;
        this.CanResize = false;
        this.Width = ToastWidth;
        this.Height = ToastHeight;
        this.WindowStartupLocation = WindowStartupLocation.Manual;
        this.Background = new SolidColorBrush(Color.Parse("#EE2B2B2B"));
        // 工具窗口 + WS_EX_NOACTIVATE：不占任务栏、不进 Alt+Tab、点击不夺焦点
        WindowHostStyler.MakeNoActivate(this);

        Border border = new Border();
        border.BorderBrush = new SolidColorBrush(Color.Parse("#FF6B6B"));
        border.BorderThickness = new Thickness(1);
        border.CornerRadius = new CornerRadius(6);
        border.Padding = new Thickness(12, 10, 12, 10);
        StackPanel panel = new StackPanel();
        panel.Spacing = 4;
        TextBlock titleBlock = new TextBlock();
        titleBlock.Text = title;
        titleBlock.FontWeight = FontWeight.Medium;
        titleBlock.Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"));
        TextBlock messageBlock = new TextBlock();
        messageBlock.Text = message;
        messageBlock.TextWrapping = TextWrapping.Wrap;
        messageBlock.FontSize = 12;
        messageBlock.Foreground = new SolidColorBrush(Color.Parse("#FFD5D5D5"));
        panel.Children.Add(titleBlock);
        panel.Children.Add(messageBlock);
        border.Child = panel;
        this.Content = border;

        this.closeTimer = new DispatcherTimer();
        this.closeTimer.Interval = TimeSpan.FromSeconds(4);
        this.closeTimer.Tick += this.OnCloseTimerTick;
    }

    /// <summary>在目标屏幕右下角显示提示，不激活窗口。</summary>
    public void ShowToast()
    {
        this.Position = new PixelPoint(
            this.workingArea.X + this.workingArea.Width - ToastWidth - MarginRight,
            this.workingArea.Y + this.workingArea.Height - ToastHeight - MarginBottom);
        this.ShowActivated = false;
        this.Show();
        this.closeTimer?.Start();
    }

    private void OnCloseTimerTick(object? sender, EventArgs e)
    {
        this.closeTimer?.Stop();
        this.closeTimer = null;
        try
        {
            this.Close();
            UiLogger.Log("截图", "失败气泡已关闭：显示时长秒数=4 实例=已释放");
        }
        catch
        {
            // 浮层已被用户点击关闭时忽略重复关闭
        }
    }
}
