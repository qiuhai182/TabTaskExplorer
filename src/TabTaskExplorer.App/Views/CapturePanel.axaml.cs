using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 截图与录屏面板：按钮只调用视图模型，具体截图、录屏、保存、打开产物文件夹与全局快捷键都由协调器完成。
/// </summary>
public partial class CapturePanel : UserControl
{
    public CapturePanel()
    {
        this.InitializeComponent();
    }

    /// <summary>截图录屏视图模型。</summary>
    private CaptureTabViewModel? ViewModel
    {
        get { return this.DataContext as CaptureTabViewModel; }
    }

    /// <summary>主窗口视图模型（复制路径等操作使用）。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get { return TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel; }
    }

    private async void OnCapturePrimaryClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=主屏截图 被点击：视图模型未就绪，结果=失败，未执行截图");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=主屏截图 被点击：结果=已请求主屏截图"
            + " 延迟索引=" + viewModel.DelayIndex
            + " 保存目录=" + viewModel.OutputDirectory);
        await viewModel.CapturePrimaryScreenAsync();
    }

    private async void OnCaptureFullClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=全屏截图 被点击：视图模型未就绪，结果=失败，未执行截图");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=全屏截图 被点击：结果=已请求全屏截图"
            + " 延迟索引=" + viewModel.DelayIndex
            + " 保存目录=" + viewModel.OutputDirectory);
        await viewModel.CaptureFullScreenAsync();
    }

    private async void OnCaptureRegionClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=区域截图 被点击：视图模型未就绪，结果=失败，未执行截图");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=区域截图 被点击：结果=已请求区域截图"
            + " 延迟索引=" + viewModel.DelayIndex);
        await viewModel.CaptureRegionAsync();
    }

    private async void OnCaptureWindowClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=活动窗口截图 被点击：视图模型未就绪，结果=失败，未执行截图");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=活动窗口截图 被点击：结果=已请求活动窗口截图"
            + " 延迟索引=" + viewModel.DelayIndex);
        await viewModel.CaptureWindowAsync();
    }

    private async void OnCaptureLastRegionClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=上次区域截图 被点击：视图模型未就绪，结果=失败，未执行截图");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=上次区域截图 被点击：结果=已请求复用上次区域"
            + " 上次区域宽度=" + viewModel.Settings.CaptureLastRegionWidth
            + " 上次区域高度=" + viewModel.Settings.CaptureLastRegionHeight
            + " 延迟索引=" + viewModel.DelayIndex);
        await viewModel.CaptureLastRegionAsync();
    }

    private async void OnToggleRecordingClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=开始或停止录屏 被点击：视图模型未就绪，结果=失败");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=开始或停止录屏 被点击：当前录制状态="
            + (viewModel.IsRecording ? "正在录制，本次请求停止" : "未录制，本次请求开始")
            + " 录制目标索引=" + viewModel.RecordingTargetIndex
            + " 帧率=" + viewModel.Settings.CaptureRecordFps
            + " 画质参数=" + viewModel.Settings.CaptureRecordQuality
            + " 麦克风设备=" + (string.IsNullOrEmpty(viewModel.Settings.CaptureMicrophoneDevice) ? "未启用" : viewModel.Settings.CaptureMicrophoneDevice));
        await viewModel.ToggleRecordingAsync();
    }

    private void OnCaptureOptionChanged(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "截图选项发生变化：视图模型未就绪，结果=未保存");
            return;
        }
        viewModel.PersistSettings("截图录屏标签页切换选项");
        UiLogger.Log("截图录屏", "截图选项已更新：截图后复制到剪贴板=" + viewModel.Settings.CaptureCopyToClipboard
            + " 完成后自动打开产物文件夹=" + viewModel.Settings.CaptureOpenFolderAfter
            + " 包含鼠标指针=" + viewModel.Settings.CaptureIncludeCursor
            + " 结果=已保存到设置");
    }

    private void OnOpenOutputFolderClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=打开产物文件夹 被点击：视图模型未就绪，结果=失败");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=打开产物文件夹 被点击：目录=" + viewModel.OutputDirectory);
        viewModel.OpenOutputFolder();
    }

    private void OnRefreshHistoryClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=刷新产物历史 被点击：视图模型未就绪，结果=失败");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=刷新产物历史 被点击：目录=" + viewModel.OutputDirectory);
        viewModel.RefreshHistory();
    }

    private void OnHistoryDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        CaptureHistoryItem? item = this.HistoryList.SelectedItem as CaptureHistoryItem;
        if (viewModel == null || item == null)
        {
            UiLogger.Log("截图录屏", "产物历史双击：视图模型或选中条目为空，结果=未打开");
            return;
        }
        UiLogger.Log("截图录屏", "产物历史双击：文件=" + item.FullPath);
        viewModel.OpenHistoryItem(item);
    }

    private void OnHistoryOpenClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        CaptureHistoryItem? item = ResolveMenuItemItem(sender);
        if (viewModel == null || item == null)
        {
            UiLogger.Log("截图录屏", "产物历史右键打开：视图模型或条目为空，结果=未打开");
            return;
        }
        UiLogger.Log("截图录屏", "产物历史右键打开：文件=" + item.FullPath);
        viewModel.OpenHistoryItem(item);
    }

    private void OnHistoryOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "产物历史右键打开产物文件夹：视图模型为空，结果=未打开");
            return;
        }
        UiLogger.Log("截图录屏", "产物历史右键打开产物文件夹：目录=" + viewModel.OutputDirectory);
        viewModel.OpenOutputFolder();
    }

    private void OnHistoryCopyPathClick(object? sender, RoutedEventArgs e)
    {
        CaptureHistoryItem? item = ResolveMenuItemItem(sender);
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null || item == null)
        {
            UiLogger.Log("截图录屏", "产物历史右键复制完整路径：主窗口视图模型或条目为空，结果=复制取消");
            return;
        }
        List<string> paths = new List<string>();
        paths.Add(item.FullPath);
        mainViewModel.CopyPathsToClipboard(paths);
        UiLogger.Log("截图录屏", "产物历史右键复制完整路径：文件=" + item.FullPath + " 结果=已请求写入系统剪贴板");
    }

    private async void OnHistoryDeleteClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        CaptureHistoryItem? item = ResolveMenuItemItem(sender);
        if (viewModel == null || item == null)
        {
            UiLogger.Log("截图录屏", "产物历史右键删除：视图模型或条目为空，结果=未删除");
            return;
        }
        Window? owner = TopLevel.GetTopLevel(this) as Window;
        bool confirmed = await MessageDialog.ConfirmAsync(owner, "删除产物",
            "确定删除「" + item.Name + "」吗？\n删除后文件将无法恢复。", "删除", "取消");
        if (!confirmed)
        {
            UiLogger.Log("截图录屏", "产物历史右键删除：文件=" + item.FullPath + " 用户选择=取消 结果=未删除");
            return;
        }
        try
        {
            File.Delete(item.FullPath);
            UiLogger.Log("截图录屏", "产物历史右键删除：文件=" + item.FullPath + " 结果=已删除");
            viewModel.RefreshHistory();
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图录屏", "产物历史右键删除失败：文件=" + item.FullPath, error);
            MessageDialog.Show(owner, "删除失败", "无法删除该产物文件：" + error.Message);
        }
    }

    /// <summary>从右键菜单项上取到绑定的产物历史条目。</summary>
    private static CaptureHistoryItem? ResolveMenuItemItem(object? sender)
    {
        MenuItem? menuItem = sender as MenuItem;
        if (menuItem == null)
        {
            return null;
        }
        return menuItem.DataContext as CaptureHistoryItem;
    }
}
