using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 截图与录屏面板：按钮只调用视图模型，具体截图、录屏、保存、打开产物文件夹与全局快捷键都由协调器完成。
/// </summary>
public partial class CapturePanel : UserControl
{
    /// <summary>按下后等待发起产物拖拽的条目。</summary>
    private CaptureHistoryItem? historyDragItem;

    /// <summary>按下时的指针事件，发起系统拖拽时作为事件来源传入。</summary>
    private PointerPressedEventArgs? historyDragPressEvent;

    /// <summary>按下时指针相对历史列表的位置，用于判断是否超过 6 像素拖拽阈值。</summary>
    private Point historyDragPressPoint;

    /// <summary>是否已越过阈值发起系统拖拽。</summary>
    private bool historyDragStarted;

    public CapturePanel()
    {
        this.InitializeComponent();
        // 产物历史沿用文件列表的拖拽阈值与处理方式：自身处理器在条目容器处理之后仍会执行，
        // 因此即使列表已处理按下事件，也能记录拖拽起点
        this.HistoryList.AddHandler(InputElement.PointerPressedEvent, this.OnHistoryPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        this.HistoryList.AddHandler(InputElement.PointerMovedEvent, this.OnHistoryPointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        this.HistoryList.AddHandler(InputElement.PointerReleasedEvent, this.OnHistoryPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
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

    private async void OnCapturePinClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=贴图截图 被点击：视图模型未就绪，结果=失败，未执行贴图截图");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=贴图截图 被点击：结果=已请求贴图截图"
            + " 保存产物=" + viewModel.Settings.CapturePinShotSaveFile
            + " 延迟索引=" + viewModel.DelayIndex);
        await viewModel.CapturePinRegionAsync();
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

    private async void OnColorPickerClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=屏幕取色 被点击：视图模型未就绪，结果=失败");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=屏幕取色 被点击：结果=已请求打开全屏取色器");
        await viewModel.OpenScreenColorPickerAsync();
    }

    private async void OnPinClipboardClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=贴图（剪贴板） 被点击：视图模型未就绪，结果=失败");
            return;
        }
        UiLogger.Log("截图录屏", "按钮=贴图（剪贴板） 被点击：结果=已请求把剪贴板图片贴到屏幕");
        await viewModel.PinClipboardImageAsync();
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
            + " 贴图截图保存产物=" + viewModel.Settings.CapturePinShotSaveFile
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

    /// <summary>历史列表按下鼠标：记录待拖拽条目与按下位置（非左键留给右键菜单，空白处不参与拖拽）。</summary>
    private void OnHistoryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.ClearHistoryDragPress();
        PointerPointProperties properties = e.GetCurrentPoint(this.HistoryList).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            UiLogger.Log("截图录屏", "产物历史按下鼠标：左键未按下 右键=" + properties.IsRightButtonPressed
                + " 中键=" + properties.IsMiddleButtonPressed + " 结果=不进入拖拽流程");
            return;
        }
        CaptureHistoryItem? item = FindHistoryItem(e.Source as Visual);
        if (item == null)
        {
            UiLogger.Log("截图录屏", "产物历史空白处按下左键：结果=未命中历史条目，不进入拖拽流程");
            return;
        }
        this.historyDragItem = item;
        this.historyDragPressEvent = e;
        this.historyDragPressPoint = e.GetPosition(this.HistoryList);
        IList? selectedItems = this.HistoryList.SelectedItems;
        int selectedCount = selectedItems == null ? 0 : selectedItems.Count;
        UiLogger.Log("截图录屏", "产物历史条目上按下左键：文件=" + item.FullPath
            + " 按下位置=横坐标" + this.historyDragPressPoint.X + "、纵坐标" + this.historyDragPressPoint.Y
            + " 当前选中条目数=" + selectedCount
            + " 结果=已记录按下状态，拖动超过6像素后按只读拷贝发起系统拖拽");
    }

    /// <summary>历史列表指针移动：超过 6 像素阈值后按只读拷贝发起系统拖拽。</summary>
    private void OnHistoryPointerMoved(object? sender, PointerEventArgs e)
    {
        if (this.historyDragStarted)
        {
            return;
        }
        if (!e.GetCurrentPoint(this.HistoryList).Properties.IsLeftButtonPressed)
        {
            return;
        }
        PointerPressedEventArgs? pressEvent = this.historyDragPressEvent;
        CaptureHistoryItem? pressedItem = this.historyDragItem;
        if (pressEvent == null || pressedItem == null)
        {
            return;
        }
        Point dragPoint = e.GetPosition(this.HistoryList);
        double offsetX = dragPoint.X - this.historyDragPressPoint.X;
        double offsetY = dragPoint.Y - this.historyDragPressPoint.Y;
        if (offsetX * offsetX + offsetY * offsetY <= 36.0)
        {
            return;
        }
        this.historyDragStarted = true;
        this.BeginHistoryDrag(pressEvent, pressedItem);
    }

    /// <summary>历史列表指针松开：未超过阈值时补记普通点击日志，并清掉本次按下状态。</summary>
    private void OnHistoryPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CaptureHistoryItem? pressedItem = this.historyDragItem;
        bool wasDragStarted = this.historyDragStarted;
        if (pressedItem != null && !wasDragStarted)
        {
            UiLogger.Log("截图录屏", "产物历史条目上松开左键：文件=" + pressedItem.FullPath
                + " 结果=未超过6像素阈值，未发起系统拖拽，本次为普通点击");
        }
        this.ClearHistoryDragPress();
    }

    /// <summary>清掉历史列表的拖拽按下状态。</summary>
    private void ClearHistoryDragPress()
    {
        this.historyDragItem = null;
        this.historyDragPressEvent = null;
        this.historyDragStarted = false;
    }

    /// <summary>沿可视化树向上找命中元素所属的历史条目（含释放效果、时间与大小等模板内元素）。</summary>
    private static CaptureHistoryItem? FindHistoryItem(Visual? source)
    {
        for (Visual? current = source; current != null; current = current.GetVisualParent())
        {
            if (current is Border border && border.DataContext is CaptureHistoryItem item)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>收集本次要拖出的历史条目：按住的条目在选中集合内就带上全部选中项，否则只拖该条目。</summary>
    private List<CaptureHistoryItem> CollectHistoryDragItems(CaptureHistoryItem pressedItem)
    {
        List<CaptureHistoryItem> items = new List<CaptureHistoryItem>();
        CaptureTabViewModel? viewModel = this.ViewModel;
        IList? selectedItems = this.HistoryList.SelectedItems;
        if (viewModel == null || selectedItems == null || !selectedItems.Contains(pressedItem))
        {
            items.Add(pressedItem);
            return items;
        }
        foreach (CaptureHistoryItem item in viewModel.HistoryItems)
        {
            if (selectedItems.Contains(item))
            {
                items.Add(item);
            }
        }
        if (items.Count == 0)
        {
            items.Add(pressedItem);
        }
        return items;
    }

    /// <summary>
    /// 发起产物历史拖拽：载荷声明只读拷贝语义，允许效果只报复制（首选放置效果为复制），
    /// 并携带文件项让资源管理器、微信输入框、网页上传区域等外部程序收到 CF_HDROP 文件列表；
    /// 全程只读取源文件，绝不移动、改名、解压或删除。
    /// </summary>
    private async void BeginHistoryDrag(PointerPressedEventArgs pressEvent, CaptureHistoryItem pressedItem)
    {
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (owner == null)
        {
            UiLogger.Log("截图录屏", "产物历史发起拖拽：按下条目=" + pressedItem.FullPath
                + " 结果=失败，未找到所属窗口，未发起拖拽");
            this.ClearHistoryDragPress();
            return;
        }
        List<CaptureHistoryItem> items = this.CollectHistoryDragItems(pressedItem);
        List<string> paths = new List<string>();
        foreach (CaptureHistoryItem item in items)
        {
            if (File.Exists(item.FullPath))
            {
                paths.Add(item.FullPath);
            }
        }
        if (paths.Count == 0)
        {
            UiLogger.Log("截图录屏", "产物历史发起拖拽：按下条目=" + pressedItem.FullPath
                + " 选中条目数=" + items.Count
                + " 结果=失败，没有可拖出的产物文件（文件可能已被删除或移走），未发起拖拽");
            this.ClearHistoryDragPress();
            return;
        }
        DataTransfer data = new DataTransfer();
        FileDragPayload payload = new FileDragPayload();
        payload.Paths.AddRange(paths);
        // 产物历史是只读来源：内部目录标签页按此语义只复制一份，外部程序据此收到文件投递
        payload.Mode = FileDragMode.CopyOnly;
        data.Add(DataTransferItem.Create(FileDropPolicy.Format, payload));
        int fileItemCount = 0;
        foreach (string path in paths)
        {
            IStorageItem? storageItem = await FileDropPolicy.TryCreateStorageItemAsync(owner, path);
            if (storageItem != null)
            {
                data.Add(DataTransferItem.CreateFile(storageItem));
                fileItemCount++;
            }
        }
        if (fileItemCount == 0)
        {
            UiLogger.Log("截图录屏", "产物历史发起拖拽：按下条目=" + pressedItem.FullPath
                + " 拖拽条目数=" + paths.Count + " 携带文件项数=0"
                + " 结果=失败，文件项转换全部失败，外部程序收不到文件列表，未发起拖拽");
            this.ClearHistoryDragPress();
            return;
        }
        UiLogger.Log("截图录屏", "产物历史发起拖拽：按下条目=" + pressedItem.FullPath
            + " 拖拽条目数=" + paths.Count + " 拖拽条目=" + string.Join("、", paths)
            + " 携带文件项数=" + fileItemCount
            + " 拖拽源语义=" + FileDropPolicy.DescribeDragMode(payload.Mode)
            + " 允许效果=只复制（首选放置效果为复制） 发起时修饰键="
            + FileDropPolicy.DescribeModifiers(pressEvent.KeyModifiers)
            + " 结果=成功，拖拽内容已交给系统拖放，源产物保持原样");
        FileDropPolicy.LastInternalDropAction = string.Empty;
        DragDropEffects applied;
        try
        {
            applied = await DragDrop.DoDragDropAsync(pressEvent, data, DragDropEffects.Copy);
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图录屏", "产物历史拖拽结束：拖拽条目数=" + paths.Count
                + " 拖拽条目=" + string.Join("、", paths) + " 结果=失败，系统拖放过程中出错", error);
            this.ClearHistoryDragPress();
            return;
        }
        string internalAction = FileDropPolicy.LastInternalDropAction;
        FileDropPolicy.LastInternalDropAction = string.Empty;
        string finishResult;
        if (internalAction == "导航")
        {
            finishResult = "目标为本程序的导航落点，只切换了目录，未移动或复制任何产物";
        }
        else if (internalAction == "复制" || (applied & DragDropEffects.Copy) != 0)
        {
            finishResult = "目标按复制接收（或在程序内复制了一份），源产物保持原样";
        }
        else if (applied == DragDropEffects.None)
        {
            finishResult = "目标未接收本次拖放（无效区域自动回弹），源产物保持原样";
        }
        else
        {
            finishResult = "目标回报的放置效果=" + FileDropPolicy.DescribeEffects(applied)
                + "，但拖拽源只允许复制，源产物保持原样";
        }
        UiLogger.Log("截图录屏", "产物历史拖拽结束：拖拽条目数=" + paths.Count
            + " 拖拽条目=" + string.Join("、", paths)
            + " 目标实际应用效果=" + FileDropPolicy.DescribeEffects(applied)
            + " 程序内落点动作=" + (internalAction.Length == 0 ? "无" : internalAction)
            + " 结果=" + finishResult);
        this.ClearHistoryDragPress();
    }

    private void OnHistoryDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        // 多选模式下选中项可能有多条，双击要打开指针下的那一条，而不是选中集合里的第一条
        CaptureHistoryItem? item = FindHistoryItem(e.Source as Visual);
        if (viewModel == null || item == null)
        {
            UiLogger.Log("截图录屏", "产物历史双击：视图模型为空或未命中条目，结果=未打开");
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

    private void OnHistoryEditClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        CaptureHistoryItem? item = ResolveMenuItemItem(sender);
        if (viewModel == null || item == null)
        {
            UiLogger.Log("截图录屏", "产物历史右键标注此图：视图模型或条目为空，结果=未打开");
            return;
        }
        UiLogger.Log("截图录屏", "产物历史右键标注此图：文件=" + item.FullPath);
        viewModel.OpenHistoryItemInEditor(item);
    }

    private void OnHistoryPinClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        CaptureHistoryItem? item = ResolveMenuItemItem(sender);
        if (viewModel == null || item == null)
        {
            UiLogger.Log("截图录屏", "产物历史右键贴到屏幕：视图模型或条目为空，结果=未贴图");
            return;
        }
        UiLogger.Log("截图录屏", "产物历史右键贴到屏幕：文件=" + item.FullPath);
        viewModel.PinHistoryItem(item);
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

    /// <summary>产物历史：右键「复制到剪贴板」——多选时复制全部选中条目，否则复制右键命中的条目。</summary>
    private async void OnHistoryCopyImageClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        List<string> paths = this.CollectSelectedHistoryPaths();
        CaptureHistoryItem? item = ResolveMenuItemItem(sender);
        if (paths.Count == 0 && item != null)
        {
            paths.Add(item.FullPath);
        }
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "产物历史右键复制到剪贴板：视图模型为空，结果=复制取消");
            return;
        }
        UiLogger.Log("截图录屏", "产物历史右键复制到剪贴板：文件数量=" + paths.Count
            + " 文件列表=" + (paths.Count == 0 ? "（无）" : string.Join("、", paths)));
        await viewModel.CopyToClipboardAsync(paths, "产物历史右键复制");
    }

    /// <summary>产物历史工具栏「复制选中到剪贴板」：按 Ctrl / Shift 多选的条目批量复制。</summary>
    private async void OnCopySelectedClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=复制选中到剪贴板 被点击：视图模型未就绪，结果=复制取消");
            return;
        }
        List<string> paths = this.CollectSelectedHistoryPaths();
        UiLogger.Log("截图录屏", "按钮=复制选中到剪贴板 被点击：选中数量=" + paths.Count);
        await viewModel.CopyToClipboardAsync(paths, "产物历史多选复制");
    }

    /// <summary>产物历史工具栏「复制最近一张」：复制历史列表第一条（按修改时间倒序）。</summary>
    private async void OnCopyLatestClick(object? sender, RoutedEventArgs e)
    {
        CaptureTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("截图录屏", "按钮=复制最近一张 被点击：视图模型未就绪，结果=复制取消");
            return;
        }
        List<string> paths = new List<string>();
        if (viewModel.HistoryItems.Count > 0)
        {
            paths.Add(viewModel.HistoryItems[0].FullPath);
        }
        UiLogger.Log("截图录屏", "按钮=复制最近一张 被点击：产物数量=" + viewModel.HistoryItems.Count
            + " 目标=" + (paths.Count == 0 ? "（无）" : paths[0]));
        await viewModel.CopyToClipboardAsync(paths, "产物历史复制最近一张");
    }

    /// <summary>收集产物历史当前多选的全部路径，保持列表顺序。</summary>
    private List<string> CollectSelectedHistoryPaths()
    {
        List<string> paths = new List<string>();
        IList? selectedItems = this.HistoryList.SelectedItems;
        if (selectedItems == null)
        {
            return paths;
        }
        foreach (object selectedItem in selectedItems)
        {
            CaptureHistoryItem? item = selectedItem as CaptureHistoryItem;
            if (item != null)
            {
                paths.Add(item.FullPath);
            }
        }
        return paths;
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
