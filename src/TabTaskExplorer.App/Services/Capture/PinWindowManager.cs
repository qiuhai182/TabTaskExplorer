using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.Imaging;
using TabTaskExplorer.App.Services;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>
/// 贴图窗口管理器：统一登记所有贴图窗口，限制同时存在的数量，并提供「关闭全部贴图」。
/// 贴图窗口本身是跨平台 Avalonia 窗口，三端行为一致。
/// </summary>
public static class PinWindowManager
{
    private const int MaxPinnedWindows = 30;

    private static readonly List<PinnedImageWindow> Windows = new List<PinnedImageWindow>();

    /// <summary>当前贴图窗口数量。</summary>
    public static int Count
    {
        get { return Windows.Count; }
    }

    /// <summary>把合成图贴到屏幕最上层；超过数量上限时先关闭最早的一张。</summary>
    public static void ShowPinnedImage(Bitmap image, PixelPoint position, AppSettings settings)
    {
        if (image == null)
        {
            return;
        }
        while (Windows.Count >= MaxPinnedWindows)
        {
            PinnedImageWindow oldest = Windows[0];
            Windows.RemoveAt(0);
            oldest.Close();
        }
        PinnedImageWindow window = new PinnedImageWindow(image, position, settings);
        window.Closed += delegate
        {
            Windows.Remove(window);
            UiLogger.Log("贴图", "贴图窗口已关闭：剩余贴图数量=" + Windows.Count);
        };
        Windows.Add(window);
        window.Show();
        UiLogger.Log("贴图", "贴图窗口已创建：像素宽度=" + image.PixelSize.Width
            + " 像素高度=" + image.PixelSize.Height
            + " 初始横坐标=" + position.X
            + " 初始纵坐标=" + position.Y
            + " 当前贴图数量=" + Windows.Count);
    }

    /// <summary>关闭全部贴图窗口。</summary>
    public static void CloseAll()
    {
        List<PinnedImageWindow> windows = new List<PinnedImageWindow>(Windows);
        Windows.Clear();
        foreach (PinnedImageWindow window in windows)
        {
            window.Close();
        }
        UiLogger.Log("贴图", "关闭全部贴图：已关闭数量=" + windows.Count);
    }
}
