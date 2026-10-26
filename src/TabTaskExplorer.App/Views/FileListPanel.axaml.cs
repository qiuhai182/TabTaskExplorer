using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>文件浏览面板：文件列表、视图切换、信息窗格（导航工具栏与地址栏在主窗口）。</summary>
public partial class FileListPanel : UserControl
{
    /// <summary>框选过程中记录的一个列表条目及其当前在列表面板坐标系中的矩形。</summary>
    private sealed class BandRowRect
    {
        /// <summary>对应的列表条目。</summary>
        public FileRow Row { get; }

        /// <summary>该条目当前占据的矩形（列表面板坐标系）。</summary>
        public Rect Rect { get; }

        public BandRowRect(FileRow row, Rect rect)
        {
            Row = row;
            Rect = rect;
        }
    }

    /// <summary>一次列表内拖放的评估结果：落点、源条目、允许效果与拒绝放置的原因。</summary>
    private sealed class ListDropDecision
    {
        /// <summary>落点类型：文件夹行、文件行或列表空白处。</summary>
        public string DropKind = string.Empty;

        /// <summary>落点对应的接收目录。</summary>
        public string TargetDirectory = string.Empty;

        /// <summary>拖放内容中的源条目路径列表。</summary>
        public List<string> SourcePaths = new List<string>();

        /// <summary>是否为本程序发起的拖拽。</summary>
        public bool IsInternalDrag;

        /// <summary>该落点允许的拖放效果。</summary>
        public DragDropEffects Effects = DragDropEffects.None;

        /// <summary>拒绝放置的原因；为 null 表示允许放置。</summary>
        public string? RejectReason;

        /// <summary>落点所在的标签视图模型。</summary>
        public FileTabViewModel? TargetTab;

        /// <summary>落点所在的条目行容器（用于落点高亮）。</summary>
        public Border? RowBorder;
    }

    /// <summary>按下后等待发起系统拖拽的条目。</summary>
    private FileRow? dragRow;

    /// <summary>按下时的指针事件，发起系统拖拽时作为事件来源传入。</summary>
    private PointerPressedEventArgs? dragPressEvent;

    /// <summary>按下时指针相对本面板的位置，用于判断是否超过 6 像素拖拽阈值。</summary>
    private Point dragPressPoint;

    /// <summary>是否已越过阈值发起系统拖拽。</summary>
    private bool dragStarted;

    /// <summary>当前带落点高亮样式类的行容器。</summary>
    private Border? dropHighlight;

    /// <summary>拖拽经过日志的去重键：落点状态不变时不重复记日志。</summary>
    private string dropLogKey = string.Empty;

    /// <summary>空白处按下左键的位置，作为框选起点。</summary>
    private Point bandPressPoint;

    /// <summary>空白处已按下左键（尚未开始框选）。</summary>
    private bool bandPressActive;

    /// <summary>框选正在进行中。</summary>
    private bool bandActive;

    /// <summary>框选矩形的起点。</summary>
    private Point bandStartPoint;

    /// <summary>框选矩形的当前终点。</summary>
    private Point bandEndPoint;

    /// <summary>框选矩形的可视化元素。</summary>
    private Border? bandBorder;

    /// <summary>框选开始时收集到的可选条目矩形列表。</summary>
    private List<BandRowRect> bandRowRects = new List<BandRowRect>();

    /// <summary>条目与空白处右键菜单里系统菜单项占位子菜单的标记。</summary>
    private const string SystemMenuHostTag = "SystemMenuItemsHost";

    /// <summary>条目右键菜单打开时记录的右键条目，供系统菜单项点击时兜底使用。</summary>
    private FileRow? systemMenuContextRow;

    public FileListPanel()
    {
        this.InitializeComponent();
        this.AddHandler(InputElement.PointerPressedEvent, this.OnPointerPressedForDrag, RoutingStrategies.Bubble, handledEventsToo: true);
        this.AddHandler(InputElement.PointerMovedEvent, this.OnPointerMovedForDrag, RoutingStrategies.Bubble, handledEventsToo: true);
        this.AddHandler(InputElement.PointerReleasedEvent, this.OnPointerReleasedForDrag, RoutingStrategies.Bubble, handledEventsToo: true);
        DragDrop.AddDragEnterHandler(this, this.OnListDragEnter);
        DragDrop.AddDragOverHandler(this, this.OnListDragOver);
        DragDrop.AddDragLeaveHandler(this, this.OnListDragLeave);
        DragDrop.AddDropHandler(this, this.OnListDrop);
    }

    /// <summary>当前标签页视图模型。</summary>
    private FileTabViewModel? ViewModel
    {
        get { return this.DataContext as FileTabViewModel; }
    }

    /// <summary>主窗口视图模型（用于新标签打开、复制剪切到剪贴板）。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get { return TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel; }
    }

    /// <summary>按下的按键只用于日志说明：把当前按下的鼠标按键拼成中文描述。</summary>
    private static string DescribePressedButtons(PointerPointProperties properties)
    {
        List<string> pressed = new List<string>();
        if (properties.IsLeftButtonPressed)
        {
            pressed.Add("鼠标左键");
        }
        if (properties.IsRightButtonPressed)
        {
            pressed.Add("鼠标右键");
        }
        if (properties.IsMiddleButtonPressed)
        {
            pressed.Add("鼠标中键");
        }
        if (pressed.Count == 0)
        {
            return "无按键";
        }
        return string.Join("、", pressed);
    }

    /// <summary>指针按下：空白处记录框选起点，条目上记录拖拽按下状态（左键且不在勾选框上）。</summary>
    private void OnPointerPressedForDrag(object? sender, PointerPressedEventArgs e)
    {
        this.ClearDragPress();
        this.ClearBandPress();
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            UiLogger.Log("程序行为", "列表按下鼠标按键=" + DescribePressedButtons(properties) + " 结果=不是鼠标左键，不进入拖动与框选流程");
            return;
        }
        Border? border = FindRowBorder(e.Source as Visual, out bool pressedOnCheckBox);
        if (border != null && pressedOnCheckBox)
        {
            if (border.DataContext is FileRow checkBoxRow)
            {
                UiLogger.Log("程序行为", "列表条目行首勾选框上按下左键：行路径=" + checkBoxRow.FullPath + " 结果=不进入拖动与框选流程，该点击交由勾选框处理");
            }
        }
        else if (border == null)
        {
            Point position = e.GetPosition(this.ListHost);
            if (position.X < 0.0 || position.Y < 0.0 || position.X > this.ListHost.Bounds.Width || position.Y > this.ListHost.Bounds.Height)
            {
                UiLogger.Log("程序行为", "列表面板按下左键：按下位置=横坐标" + position.X + "、纵坐标" + position.Y + " 结果=按下位置不在列表区域内，不进入拖动与框选流程");
            }
            else
            {
                this.bandPressPoint = position;
                this.bandPressActive = true;
                UiLogger.Log("程序行为", "列表空白处按下左键：按下位置=横坐标" + position.X + "、纵坐标" + position.Y + " 结果=已记录框选起点，拖动超过6像素后开始框选多选");
            }
        }
        else if (border.DataContext is FileRow pressedRow)
        {
            this.dragRow = pressedRow;
            this.dragPressEvent = e;
            this.dragPressPoint = e.GetPosition(this);
            UiLogger.Log("程序行为", "列表条目上按下左键：行路径=" + pressedRow.FullPath + " 条目类型=" + (pressedRow.IsDirectory ? "目录" : "文件")
                + " 按下位置=横坐标" + this.dragPressPoint.X + "、纵坐标" + this.dragPressPoint.Y
                + " 结果=已记录按下状态，拖动超过6像素后发起系统拖拽");
        }
    }

    /// <summary>指针移动：超过 6 像素后开始或推进框选，或发起列表内条目拖拽。</summary>
    private void OnPointerMovedForDrag(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (this.bandPressActive)
        {
            Point bandPoint = e.GetPosition(this.ListHost);
            if (!this.bandActive)
            {
                double offsetX = bandPoint.X - this.bandPressPoint.X;
                double offsetY = bandPoint.Y - this.bandPressPoint.Y;
                if (offsetX * offsetX + offsetY * offsetY <= 36.0)
                {
                    return;
                }
                this.BeginBand();
                if (!this.bandActive)
                {
                    return;
                }
                e.Pointer.Capture(this);
            }
            this.UpdateBand(bandPoint);
            return;
        }
        PointerPressedEventArgs? pressEvent = this.dragPressEvent;
        FileRow? pressedRow = this.dragRow;
        if (!this.dragStarted && pressEvent != null && pressedRow != null)
        {
            Point dragPoint = e.GetPosition(this);
            double offsetX = dragPoint.X - this.dragPressPoint.X;
            double offsetY = dragPoint.Y - this.dragPressPoint.Y;
            if (offsetX * offsetX + offsetY * offsetY > 36.0)
            {
                this.dragStarted = true;
                this.BeginFileDrag(pressEvent, pressedRow);
            }
        }
    }

    /// <summary>指针松开：结束框选或清掉本次按下的拖拽状态，并补记未达阈值的普通点击日志。</summary>
    private void OnPointerReleasedForDrag(object? sender, PointerReleasedEventArgs e)
    {
        bool wasBandActive = this.bandActive;
        bool wasBandPressActive = this.bandPressActive;
        FileRow? pressedRow = this.dragRow;
        bool wasDragStarted = this.dragStarted;
        if (wasBandActive)
        {
            e.Pointer.Capture(null);
            this.FinishBand();
        }
        else if (wasBandPressActive)
        {
            UiLogger.Log("程序行为", "列表空白处松开左键：按下位置=横坐标" + this.bandPressPoint.X + "、纵坐标" + this.bandPressPoint.Y
                + " 结果=未超过6像素阈值，未开始框选，本次为普通点击");
        }
        this.ClearBandPress();
        if (pressedRow != null)
        {
            if (!wasDragStarted)
            {
                UiLogger.Log("程序行为", "列表条目上松开左键：行路径=" + pressedRow.FullPath
                    + " 结果=未超过6像素阈值，未发起系统拖拽，本次为普通点击");
            }
            this.ClearDragPress();
        }
    }

    /// <summary>清掉条目拖拽的按下状态。</summary>
    private void ClearDragPress()
    {
        this.dragRow = null;
        this.dragPressEvent = null;
        this.dragStarted = false;
    }

    /// <summary>清掉空白处框选的按下状态（框选本身是否进行中由 bandActive 单独记录）。</summary>
    private void ClearBandPress()
    {
        this.bandPressActive = false;
    }

    /// <summary>开始框选：记录起点、收集可选条目矩形、清空原选中并显示框选矩形。</summary>
    private void BeginBand()
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "框选开始：标签视图模型为空 结果=失败，未显示框选矩形，本次框选取消");
            this.bandPressActive = false;
            return;
        }
        this.bandActive = true;
        this.bandStartPoint = this.bandPressPoint;
        this.bandEndPoint = this.bandPressPoint;
        this.bandRowRects = CollectBandRowRects(viewModel);
        this.RowList.SelectedItem = null;
        this.TileList.SelectedItem = null;
        Border band = new Border();
        band.IsHitTestVisible = false;
        band.ZIndex = 20;
        band.Background = new SolidColorBrush(Color.FromArgb(40, 76, 194, byte.MaxValue));
        band.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 76, 194, byte.MaxValue));
        band.BorderThickness = new Thickness(1.0);
        band.CornerRadius = new CornerRadius(2.0);
        band.RenderTransform = new TranslateTransform();
        this.bandBorder = band;
        this.ListHost.Children.Add(band);
        UiLogger.Log("程序行为", "框选开始：起点=横坐标" + this.bandStartPoint.X + "、纵坐标" + this.bandStartPoint.Y
            + " 可选条目数=" + this.bandRowRects.Count + " 列表原选中=已清空 结果=已显示框选矩形并开始框选多选");
    }

    /// <summary>推进框选：把矩形限制在列表区域内移动与缩放，并按相交情况实时切换条目选中状态。</summary>
    private void UpdateBand(Point hostPoint)
    {
        double endX = Math.Max(0.0, Math.Min(hostPoint.X, this.ListHost.Bounds.Width));
        double endY = Math.Max(0.0, Math.Min(hostPoint.Y, this.ListHost.Bounds.Height));
        this.bandEndPoint = new Point(endX, endY);
        double left = Math.Min(this.bandStartPoint.X, endX);
        double top = Math.Min(this.bandStartPoint.Y, endY);
        double width = Math.Abs(endX - this.bandStartPoint.X);
        double height = Math.Abs(endY - this.bandStartPoint.Y);
        Border? band = this.bandBorder;
        if (band == null)
        {
            return;
        }
        if (band.RenderTransform is TranslateTransform translate)
        {
            translate.X = left;
            translate.Y = top;
        }
        band.Width = width;
        band.Height = height;
        foreach (BandRowRect rowRect in this.bandRowRects)
        {
            double overlapWidth = Math.Min(rowRect.Rect.Right, left + width) - Math.Max(rowRect.Rect.Left, left);
            double overlapHeight = Math.Min(rowRect.Rect.Bottom, top + height) - Math.Max(rowRect.Rect.Top, top);
            bool inside = overlapWidth > 0.0 && overlapHeight > 0.0;
            if (rowRect.Row.IsSelected != inside)
            {
                rowRect.Row.IsSelected = inside;
            }
        }
    }

    /// <summary>收集当前可见条目在列表面板坐标系中的矩形（框选开始时快照一次）。</summary>
    private List<BandRowRect> CollectBandRowRects(FileTabViewModel viewModel)
    {
        List<BandRowRect> rowRects = new List<BandRowRect>();
        ListBox list = viewModel.IsRowStyleLayout ? this.RowList : this.TileList;
        foreach (FileRow visibleRow in viewModel.VisibleRows)
        {
            Border? border = FindRowBorder(list, visibleRow);
            if (border != null)
            {
                Point? origin = border.TranslatePoint(new Point(0.0, 0.0), this.ListHost);
                if (origin.HasValue)
                {
                    rowRects.Add(new BandRowRect(visibleRow, new Rect(origin.Value, border.Bounds.Size)));
                }
            }
        }
        return rowRects;
    }

    /// <summary>结束框选：移除框选矩形，统计最终选中的条目并刷新右侧信息窗格。</summary>
    private void FinishBand()
    {
        Border? band = this.bandBorder;
        if (band != null)
        {
            this.ListHost.Children.Remove(band);
        }
        this.bandBorder = null;
        this.bandActive = false;
        double left = Math.Min(this.bandStartPoint.X, this.bandEndPoint.X);
        double top = Math.Min(this.bandStartPoint.Y, this.bandEndPoint.Y);
        double right = Math.Max(this.bandStartPoint.X, this.bandEndPoint.X);
        double bottom = Math.Max(this.bandStartPoint.Y, this.bandEndPoint.Y);
        int selectedCount = 0;
        List<string> selectedPaths = new List<string>();
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel != null)
        {
            foreach (FileRow visibleRow in viewModel.VisibleRows)
            {
                if (visibleRow.IsSelected)
                {
                    selectedCount++;
                    selectedPaths.Add(visibleRow.FullPath);
                }
            }
        }
        string resultText = selectedCount == 0
            ? "框选完成，矩形内没有条目被选中"
            : "成功，已完成框选多选并刷新信息窗格";
        UiLogger.Log("程序行为", "框选结束：矩形范围=左上角(横坐标" + left + "、纵坐标" + top + ")、右下角(横坐标" + right + "、纵坐标" + bottom + ")"
            + "、宽" + (right - left) + "、高" + (bottom - top)
            + " 选中条目数=" + selectedCount + " 选中条目=" + string.Join("、", selectedPaths) + " 结果=" + resultText);
        this.UpdateInfoPane();
    }

    /// <summary>按条目找它在列表里的行容器（框选收集矩形时用）。</summary>
    private static Border? FindRowBorder(ListBox list, FileRow row)
    {
        if (list.ContainerFromItem(row) is not ListBoxItem item)
        {
            return null;
        }
        foreach (Visual descendant in item.GetVisualDescendants())
        {
            if (descendant is Border border && border.DataContext == row)
            {
                return border;
            }
        }
        return null;
    }

    /// <summary>从命中的可视元素向上找条目行容器，并说明这次按下是否落在行首勾选框上。</summary>
    private static Border? FindRowBorder(Visual? source, out bool pressedOnCheckBox)
    {
        pressedOnCheckBox = false;
        bool passedCheckBox = false;
        for (Visual? current = source; current != null; current = current.GetVisualParent())
        {
            if (current is CheckBox)
            {
                passedCheckBox = true;
            }
            if (current is Border border && border.DataContext is FileRow)
            {
                pressedOnCheckBox = passedCheckBox;
                return border;
            }
        }
        return null;
    }

    /// <summary>拖拽事件的命中元素：事件来源不在本面板内时改用坐标命中测试兜底。</summary>
    private Visual? ResolveDragSource(DragEventArgs e)
    {
        Visual? source = e.Source as Visual;
        for (Visual? current = source; current != null; current = current.GetVisualParent())
        {
            if (current == this)
            {
                return source;
            }
        }
        return this.InputHitTest(e.GetPosition(this)) as Visual;
    }

    /// <summary>越过 6 像素阈值后发起系统拖拽：带上选中条目路径与本机文件项，结束后按实际效果刷新源标签。</summary>
    private async void BeginFileDrag(PointerPressedEventArgs pressEvent, FileRow pressedRow)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "列表发起拖拽：标签视图模型或所属窗口为空 结果=失败，未发起拖拽");
            return;
        }
        if (!FileDropPolicy.CanDragFrom(viewModel.FileSystemProvider))
        {
            UiLogger.Log("程序行为", "列表发起拖拽：源目录=" + viewModel.CurrentPath + " 按下行=" + pressedRow.FullPath
                + " 结果=失败，压缩包虚拟目录不参与拖拽，未发起拖拽");
            viewModel.ShowMessage("压缩包内的条目不支持拖拽");
            return;
        }
        List<FileRow> rows = CollectTargetRows(viewModel, pressedRow);
        List<string> paths = new List<string>();
        foreach (FileRow row in rows)
        {
            paths.Add(row.FullPath);
        }
        bool carryFileItems = FileDropPolicy.CanDropToExternal(viewModel.FileSystemProvider);
        DataTransfer data = new DataTransfer();
        FileDragPayload payload = new FileDragPayload();
        payload.Paths.AddRange(paths);
        data.Add(DataTransferItem.Create(FileDropPolicy.Format, payload));
        int fileItemCount = 0;
        if (carryFileItems)
        {
            foreach (string path in paths)
            {
                IStorageItem? item = await TryCreateStorageItem(owner, path);
                if (item != null)
                {
                    data.Add(DataTransferItem.CreateFile(item));
                    fileItemCount++;
                }
            }
        }
        UiLogger.Log("程序行为", "列表发起拖拽：源目录=" + viewModel.CurrentPath + " 按下行=" + pressedRow.FullPath
            + " 拖拽条目数=" + paths.Count + " 拖拽条目=" + string.Join("、", paths)
            + " 是否本机条目=" + (carryFileItems ? "是" : "否") + " 携带文件项数=" + fileItemCount
            + " 允许效果=移动或复制 发起时修饰键=" + FileDropPolicy.DescribeModifiers(pressEvent.KeyModifiers)
            + " 结果=成功，拖拽内容已交给系统拖放");
        FileDropPolicy.LastInternalDropAction = string.Empty;
        DragDropEffects applied;
        try
        {
            applied = await DragDrop.DoDragDropAsync(pressEvent, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception error)
        {
            UiLogger.LogError("程序行为", "列表拖拽结束：拖拽条目数=" + paths.Count + " 拖拽条目=" + string.Join("、", paths)
                + " 结果=失败，系统拖放过程中出错", error);
            return;
        }
        string internalAction = FileDropPolicy.LastInternalDropAction;
        FileDropPolicy.LastInternalDropAction = string.Empty;
        string appliedDescription = internalAction == "导航" ? "导航（不移动也不复制）" : FileDropPolicy.DescribeEffects(applied);
        string finishResult;
        if (internalAction == "导航")
        {
            finishResult = "目标为本程序的导航落点，只切换了目录，未移动或复制任何条目";
        }
        else if (applied == DragDropEffects.None)
        {
            finishResult = "目标未接收本次拖放，源标签内容保持不变";
        }
        else if ((applied & DragDropEffects.Move) != 0)
        {
            viewModel.Refresh();
            finishResult = "目标按移动接收，源标签已刷新";
        }
        else
        {
            finishResult = "目标按复制接收，源标签内容保持不变";
        }
        UiLogger.Log("程序行为", "列表拖拽结束：拖拽条目数=" + paths.Count + " 拖拽条目=" + string.Join("、", paths)
            + " 目标实际应用效果=" + appliedDescription + " 结果=" + finishResult);
    }

    /// <summary>把本机路径转成系统存储项（拖拽时携带文件项用），转换出错时记日志并返回空。</summary>
    private async Task<IStorageItem?> TryCreateStorageItem(TopLevel owner, string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return await owner.StorageProvider.TryGetFolderFromPathAsync(CreateFileUri(path));
            }
            if (File.Exists(path))
            {
                return await owner.StorageProvider.TryGetFileFromPathAsync(CreateFileUri(path));
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("程序行为", "列表发起拖拽：条目路径=" + path + " 结果=失败，转换系统存储项时出错", error);
        }
        return null;
    }

    /// <summary>把路径转成文件协议 URI：本身不是合法绝对文件路径时补上 file:// 前缀。</summary>
    private static Uri CreateFileUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && uri.IsFile)
        {
            return uri;
        }
        return new Uri("file://" + path);
    }

    /// <summary>拖拽进入列表：按落点决定本次拖放允许的效果。</summary>
    private void OnListDragEnter(object? sender, DragEventArgs e)
    {
        this.ApplyListDropEffect(e);
    }

    /// <summary>拖拽在列表内移动：随落点变化更新允许效果与落点高亮。</summary>
    private void OnListDragOver(object? sender, DragEventArgs e)
    {
        this.ApplyListDropEffect(e);
    }

    /// <summary>拖拽离开列表区域：清除落点高亮与日志去重键。</summary>
    private void OnListDragLeave(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        Point position = e.GetPosition(this);
        if (position.X < 0.0 || position.Y < 0.0 || position.X > this.Bounds.Width || position.Y > this.Bounds.Height)
        {
            this.UpdateDropHighlight(null);
            this.dropLogKey = string.Empty;
            UiLogger.Log("程序行为", "拖拽离开列表区域：离开位置=横坐标" + position.X + "、纵坐标" + position.Y
                + " 结果=已清除列表落点高亮与日志去重键");
        }
    }

    /// <summary>评估落点后设置允许效果与高亮，落点状态变化时记一次拖拽经过日志。</summary>
    private void ApplyListDropEffect(DragEventArgs e)
    {
        e.Handled = true;
        ListDropDecision decision = EvaluateListDrop(e);
        e.DragEffects = decision.Effects;
        this.UpdateDropHighlight(decision.RejectReason == null ? decision.RowBorder : null);
        string logKey = decision.DropKind + "|" + decision.TargetDirectory + "|" + (decision.RejectReason ?? string.Empty)
            + "|" + FileDropPolicy.DescribeEffects(decision.Effects);
        if (!string.Equals(logKey, this.dropLogKey, StringComparison.Ordinal))
        {
            this.dropLogKey = logKey;
            UiLogger.Log("程序行为", "列表拖拽经过落点：落点类型=" + decision.DropKind + " 目标目录=" + decision.TargetDirectory
                + " 拖放来源=" + (decision.IsInternalDrag ? "本程序发起" : "外部程序拖入")
                + " 源条目数=" + decision.SourcePaths.Count + " 源条目=" + string.Join("、", decision.SourcePaths)
                + " 修饰键=" + FileDropPolicy.DescribeModifiers(e.KeyModifiers)
                + " 效果=" + FileDropPolicy.DescribeEffects(decision.Effects)
                + " 结果=" + (decision.RejectReason == null ? "允许在该落点放置" : "拒绝放置，原因：" + decision.RejectReason));
        }
    }

    /// <summary>执行列表内拖放：按允许效果移动或复制源条目，并刷新位于目标目录的标签。</summary>
    private void OnListDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        ListDropDecision decision = EvaluateListDrop(e);
        e.DragEffects = decision.Effects;
        this.UpdateDropHighlight(null);
        this.dropLogKey = string.Empty;
        if (decision.RejectReason != null || decision.TargetTab == null)
        {
            UiLogger.Log("程序行为", "列表拖放执行：落点类型=" + decision.DropKind + " 目标目录=" + decision.TargetDirectory
                + " 拖放来源=" + (decision.IsInternalDrag ? "本程序发起" : "外部程序拖入")
                + " 源条目数=" + decision.SourcePaths.Count + " 源条目=" + string.Join("、", decision.SourcePaths)
                + " 修饰键=" + FileDropPolicy.DescribeModifiers(e.KeyModifiers)
                + " 效果=" + FileDropPolicy.DescribeEffects(decision.Effects)
                + " 结果=失败，本次拖放未执行，原因：" + (decision.RejectReason ?? "未找到标签视图模型"));
            return;
        }
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "列表拖放执行：落点类型=" + decision.DropKind + " 目标目录=" + decision.TargetDirectory
                + " 结果=失败，未找到主窗口视图模型，本次拖放未执行");
            return;
        }
        bool isMove = (decision.Effects & DragDropEffects.Move) != 0;
        int successCount = mainViewModel.TransferPathsToDirectory(decision.TargetTab, decision.TargetDirectory, decision.SourcePaths, isMove, out int failedCount);
        foreach (object tab in mainViewModel.Tabs)
        {
            if (tab is FileTabViewModel fileTab && FileDropPolicy.IsSameDirectory(fileTab.CurrentPath, decision.TargetDirectory))
            {
                fileTab.Refresh();
            }
        }
        string actionText = isMove ? "移动" : "复制";
        if (decision.IsInternalDrag)
        {
            FileDropPolicy.LastInternalDropAction = actionText;
        }
        decision.TargetTab.ShowMessage("拖放完成（" + actionText + "）：目标=" + decision.TargetDirectory
            + " 成功 " + successCount + " 项，失败 " + failedCount + " 项");
        UiLogger.Log("程序行为", "列表拖放执行：落点类型=" + decision.DropKind + " 动作=" + actionText
            + " 拖放来源=" + (decision.IsInternalDrag ? "本程序发起" : "外部程序拖入")
            + " 源条目数=" + decision.SourcePaths.Count + " 源条目=" + string.Join("、", decision.SourcePaths)
            + " 修饰键=" + FileDropPolicy.DescribeModifiers(e.KeyModifiers)
            + " 目标目录=" + decision.TargetDirectory
            + " 效果=" + FileDropPolicy.DescribeEffects(decision.Effects)
            + " 成功条数=" + successCount + " 失败条数=" + failedCount
            + " 结果=已执行，已刷新位于目标目录的标签并提示用户");
    }

    /// <summary>评估一次拖放：判定落点类型、目标目录与源条目是否匹配，得出允许效果或拒绝原因。</summary>
    private ListDropDecision EvaluateListDrop(DragEventArgs e)
    {
        ListDropDecision decision = new ListDropDecision();
        decision.TargetTab = this.ViewModel;
        if (decision.TargetTab == null)
        {
            decision.RejectReason = "未找到标签视图模型";
        }
        Visual? source = ResolveDragSource(e);
        decision.RowBorder = FindRowBorder(source, out _);
        if (decision.RowBorder != null && decision.RowBorder.DataContext is FileRow row)
        {
            decision.DropKind = row.IsDirectory ? "文件夹行" : "文件行";
            if (row.IsDirectory)
            {
                decision.TargetDirectory = row.FullPath;
            }
            else if (decision.RejectReason == null)
            {
                decision.RejectReason = "文件行不是容器，不能接收拖放";
            }
        }
        else
        {
            decision.DropKind = "列表空白处";
            if (decision.TargetTab != null)
            {
                decision.TargetDirectory = decision.TargetTab.CurrentPath;
            }
            if (decision.RejectReason == null && string.IsNullOrEmpty(decision.TargetDirectory))
            {
                decision.RejectReason = "当前没有打开的目录（此电脑根视图），不能接收拖放";
            }
        }
        decision.SourcePaths = FileDropPolicy.CollectSourcePaths(e.DataTransfer, out decision.IsInternalDrag);
        if (decision.RejectReason == null && decision.SourcePaths.Count == 0)
        {
            decision.RejectReason = "拖放内容中没有可用的本机路径";
        }
        if (decision.RejectReason == null && decision.TargetTab != null)
        {
            string mismatchReason = FileDropPolicy.CheckSourceTargetMatch(decision.SourcePaths, decision.IsInternalDrag, decision.TargetTab.FileSystemProvider);
            if (mismatchReason.Length > 0)
            {
                decision.RejectReason = mismatchReason;
            }
            else if (!FileDropPolicy.IsDropTargetValid(decision.SourcePaths, decision.TargetDirectory, Directory.Exists(decision.TargetDirectory)))
            {
                decision.RejectReason = "目标目录不存在、与源条目同目录，或位于源文件夹自身内部";
            }
            else
            {
                decision.Effects = FileDropPolicy.ResolveEffects(decision.SourcePaths[0], decision.TargetDirectory, e.KeyModifiers);
            }
        }
        return decision;
    }

    /// <summary>切换落点高亮：给目标行加上 droptarget 样式类，离开的行去掉该样式类。</summary>
    private void UpdateDropHighlight(Border? rowBorder)
    {
        if (this.dropHighlight != rowBorder)
        {
            if (this.dropHighlight != null)
            {
                this.dropHighlight.Classes.Remove("droptarget");
            }
            this.dropHighlight = rowBorder;
            rowBorder?.Classes.Add("droptarget");
        }
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is FileRow row)
        {
            this.ViewModel?.OpenEntry(row);
            UiLogger.Log("程序行为", "双击列表条目：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已进入该目录" : "已按标签页规则打开该条目"));
            e.Handled = true;
        }
    }

    /// <summary>双击列表空白处返回上一级。</summary>
    private void OnListBackgroundDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled)
        {
            UiLogger.Log("程序行为", "双击列表空白处：事件已由列表条目处理 结果=已忽略，未返回上一级");
            return;
        }
        FileTabViewModel? viewModel = this.ViewModel;
        string originalPath = viewModel == null ? string.Empty : viewModel.CurrentPath;
        viewModel?.NavigateUp();
        UiLogger.Log("程序行为", "双击列表空白处返回上一级：原路径=" + (originalPath.Length == 0 ? "此电脑" : originalPath)
            + " 结果=" + (originalPath.Length == 0 ? "失败，当前没有打开的目录，未返回上一级" : "成功，已执行返回上一级"));
        e.Handled = true;
    }

    private void OnRowCheckChanged(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        int checkedCount = viewModel == null ? 0 : viewModel.GetCheckedRows().Count;
        UiLogger.Log("程序行为", "列表勾选状态变化：已勾选条目数=" + checkedCount + " 结果=成功，已刷新右侧信息窗格");
        this.UpdateInfoPane();
    }

    /// <summary>空白处右键菜单打开前：按当前目录启用菜单项，并把适用的系统右键菜单项注入占位子菜单。</summary>
    private void OnBackgroundMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            UiLogger.Log("程序行为", "空白处右键菜单打开：事件来源不是右键菜单 结果=失败，未准备菜单项");
            return;
        }
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "空白处右键菜单打开：当前目录=未取到 结果=失败，未找到标签视图模型，未准备菜单项");
            return;
        }
        string currentPath = viewModel.CurrentPath;
        string directoryText = currentPath.Length == 0 ? "此电脑" : currentPath;
        bool hasDirectory = currentPath.Length > 0;
        MenuItem? navigateUpItem = FindMenuItemByTag(menu, "MenuNavigateUp");
        if (navigateUpItem != null)
        {
            navigateUpItem.IsEnabled = hasDirectory;
        }
        MenuItem? newFolderItem = FindMenuItemByTag(menu, "MenuNewFolder");
        if (newFolderItem != null)
        {
            newFolderItem.IsEnabled = hasDirectory;
        }
        MenuItem? systemMenuHost = FindSystemMenuHost(menu);
        if (systemMenuHost == null)
        {
            UiLogger.Log("程序行为", "空白处右键菜单打开：当前目录=" + directoryText + " 结果=失败，未找到系统菜单项占位子菜单，未添加系统菜单项");
            return;
        }
        systemMenuHost.Items.Clear();
        ISystemContextMenuService? systemContextMenuService = this.MainViewModel?.SystemContextMenus;
        if (!hasDirectory || systemContextMenuService == null || !systemContextMenuService.IsSupported)
        {
            systemMenuHost.IsVisible = false;
            UiLogger.Log("程序行为", "空白处右键菜单打开：当前目录=" + directoryText + " 适用条目数=0 结果="
                + (hasDirectory ? "当前系统不支持系统右键菜单管理，未添加系统菜单项" : "此电脑根视图没有具体目录，未添加系统菜单项"));
            return;
        }
        List<SystemContextMenuEntry> entries = SystemContextMenuPolicy.SelectForBackground(systemContextMenuService.Enumerate(), currentPath);
        if (entries.Count == 0)
        {
            systemMenuHost.IsVisible = false;
            UiLogger.Log("程序行为", "空白处右键菜单打开：当前目录=" + directoryText + " 适用条目数=0 结果=成功，没有适用的系统菜单项，已隐藏系统菜单项子菜单");
            return;
        }
        foreach (SystemContextMenuEntry entry in entries)
        {
            MenuItem item = new MenuItem();
            item.Header = entry.DisplayName;
            string tip = entry.RequiresAdministrator ? entry.RegistryPath + "(需管理员)" : entry.RegistryPath;
            ToolTip.SetTip(item, tip);
            item.Click += this.OnBackgroundSystemMenuEntryClick;
            item.DataContext = entry;
            systemMenuHost.Items.Add(item);
        }
        systemMenuHost.IsVisible = true;
        UiLogger.Log("程序行为", "空白处右键菜单打开：当前目录=" + directoryText + " 系统菜单项注入数=" + entries.Count
            + " 结果=成功，已填充系统菜单项子菜单");
    }

    /// <summary>空白处右键菜单“刷新”：重新加载当前目录。</summary>
    private void OnBackgroundRefreshClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "空白处右键菜单刷新：未找到标签视图模型 结果=失败，本次刷新未执行");
            return;
        }
        string directoryText = viewModel.CurrentPath.Length == 0 ? "此电脑" : viewModel.CurrentPath;
        viewModel.Refresh();
        UiLogger.Log("程序行为", "空白处右键菜单刷新：当前目录=" + directoryText + " 结果=成功，已重新加载该目录");
    }

    /// <summary>空白处右键菜单“返回上一级”：已在最上级时不执行。</summary>
    private void OnBackgroundNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "空白处右键菜单返回上一级：未找到标签视图模型 结果=失败，本次返回未执行");
            return;
        }
        string currentPath = viewModel.CurrentPath;
        if (currentPath.Length == 0)
        {
            UiLogger.Log("程序行为", "空白处右键菜单返回上一级：当前目录=此电脑 结果=失败，已是最上级，未返回上一级");
            return;
        }
        viewModel.NavigateUp();
        UiLogger.Log("程序行为", "空白处右键菜单返回上一级：原路径=" + currentPath + " 结果=成功，已返回上一级");
    }

    /// <summary>空白处右键菜单“粘贴”：把剪贴板条目粘贴到当前目录。</summary>
    private void OnBackgroundPasteClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        FileTabViewModel? viewModel = this.ViewModel;
        if (mainViewModel == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "空白处右键菜单粘贴：主窗口或标签视图模型为空 结果=失败，本次粘贴未执行");
            return;
        }
        string directoryText = viewModel.CurrentPath.Length == 0 ? "此电脑" : viewModel.CurrentPath;
        mainViewModel.PasteFromClipboard();
        UiLogger.Log("程序行为", "空白处右键菜单粘贴：目标目录=" + directoryText
            + " 结果=已请求把剪贴板条目粘贴到该目录（剪贴板为空或目录不可写时由界面提示）");
    }

    /// <summary>空白处右键菜单“新建文件夹…”：输入名称后在当前目录创建文件夹。</summary>
    private async void OnBackgroundNewFolderClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "空白处右键菜单新建文件夹：所属窗口或标签视图模型为空 结果=失败，本次新建未执行");
            return;
        }
        string currentPath = viewModel.CurrentPath;
        if (currentPath.Length == 0)
        {
            UiLogger.Log("程序行为", "空白处右键菜单新建文件夹：当前目录=此电脑 结果=失败，此电脑视图不能新建文件夹");
            return;
        }
        string? folderName = await InputDialog.Show(owner, "新建文件夹", "请输入文件夹名称：", "新建文件夹");
        if (string.IsNullOrWhiteSpace(folderName))
        {
            UiLogger.Log("程序行为", "空白处右键菜单新建文件夹：目标目录=" + currentPath + " 结果=用户取消，未输入名称，本次新建未执行");
            return;
        }
        string newName = folderName.Trim();
        viewModel.CreateFolder(newName);
        UiLogger.Log("程序行为", "空白处右键菜单新建文件夹：目标目录=" + currentPath + " 新文件夹名称=" + newName
            + " 结果=成功，已提交创建请求并刷新列表（创建失败时由摘要栏提示）");
    }

    /// <summary>空白处菜单里注入的系统菜单项被点击：以当前目录为目标唤起该系统菜单项命令。</summary>
    private void OnBackgroundSystemMenuEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is SystemContextMenuEntry entry)
        {
            MainWindowViewModel? mainViewModel = this.MainViewModel;
            FileTabViewModel? viewModel = this.ViewModel;
            ISystemContextMenuService? systemContextMenuService = mainViewModel?.SystemContextMenus;
            string currentPath = viewModel == null ? string.Empty : viewModel.CurrentPath;
            if (currentPath.Length == 0 || systemContextMenuService == null)
            {
                UiLogger.Log("程序行为", "唤起空白处系统菜单项：条目名称=" + entry.DisplayName + " 注册表完整路径=" + entry.RegistryPath
                    + " 目标目录=未取到 结果=失败，未找到当前目录或系统右键菜单服务，本次唤起未执行");
                return;
            }
            bool succeeded = systemContextMenuService.Execute(entry, currentPath);
            UiLogger.Log("程序行为", "唤起空白处系统菜单项：条目名称=" + entry.DisplayName + " 注册表完整路径=" + entry.RegistryPath
                + " 目标目录=" + currentPath
                + " 结果=" + (succeeded ? "成功，已唤起该系统菜单项命令" : "失败，唤起该系统菜单项命令未成功"));
            if (!succeeded)
            {
                viewModel?.ShowMessage("唤起系统菜单项失败：" + entry.DisplayName);
            }
        }
        else
        {
            UiLogger.Log("程序行为", "唤起空白处系统菜单项：菜单项未关联系统菜单条目 结果=失败，本次唤起未执行");
        }
    }

    private void OnMenuOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            this.ViewModel?.OpenEntry(row);
            UiLogger.Log("程序行为", "右键菜单打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已进入该目录" : "已按标签页规则打开该条目"));
        }
    }

    private void OnMenuOpenInNewTabClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        FileTabViewModel? viewModel = this.ViewModel;
        if (mainViewModel == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单在新标签打开：主窗口或标签视图模型为空 结果=失败，本次操作取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            if (row.IsDirectory)
            {
                mainViewModel.AddTab(row.FullPath);
            }
            else
            {
                viewModel.OpenEntry(row);
            }
            UiLogger.Log("程序行为", "右键菜单在新标签打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已新建标签打开该目录" : "已按标签页规则打开该文件"));
        }
    }

    private void OnMenuCopyClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单复制：未找到主窗口视图模型 结果=失败，本次复制取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            mainViewModel.CopyPathsToClipboard(new List<string> { row.FullPath });
            UiLogger.Log("程序行为", "右键菜单复制：行路径=" + row.FullPath
                + " 结果=成功，已把该路径放入剪贴板，粘贴时生效");
        }
    }

    private void OnMenuCutClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单剪切：未找到主窗口视图模型 结果=失败，本次剪切取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            mainViewModel.CutPathsToClipboard(new List<string> { row.FullPath });
            UiLogger.Log("程序行为", "右键菜单剪切：行路径=" + row.FullPath
                + " 结果=成功，已把该路径放入剪贴板，粘贴时将移动该条目");
        }
    }

    /// <summary>右键"压缩为…"：选择保存位置后把选中（或右键）条目压缩为新压缩包。</summary>
    private async void OnMenuCompressClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "右键菜单压缩为：标签视图模型或所属窗口为空 结果=失败，本次压缩取消");
            return;
        }
        if (sender is not MenuItem menuItem || menuItem.DataContext is not FileRow row)
        {
            UiLogger.Log("程序行为", "右键菜单压缩为：菜单项未关联列表条目 结果=失败，本次压缩取消");
            return;
        }
        List<FileRow> rows = CollectTargetRows(viewModel, row);
        UiLogger.Log("程序行为", "右键菜单压缩为：右键行路径=" + row.FullPath + " 操作条目数=" + rows.Count
            + " 结果=成功，已弹出保存位置选择对话框");

        string suggestedName = rows.Count == 1
            ? Path.GetFileNameWithoutExtension(rows[0].DisplayName)
            : "新建压缩包";
        if (string.IsNullOrWhiteSpace(suggestedName))
        {
            suggestedName = "新建压缩包";
        }
        FilePickerSaveOptions options = new FilePickerSaveOptions();
        options.Title = "压缩为";
        options.SuggestedFileName = suggestedName;
        options.DefaultExtension = "zip";
        options.FileTypeChoices = new List<FilePickerFileType>
        {
            new FilePickerFileType("压缩包（ZIP）") { Patterns = new List<string> { "*.zip" } },
            new FilePickerFileType("7z 压缩包") { Patterns = new List<string> { "*.7z" } },
            new FilePickerFileType("tar 归档（含 gzip / bzip2 外层压缩）")
            {
                Patterns = new List<string> { "*.tar", "*.tar.gz", "*.tgz", "*.tar.bz2", "*.tbz2" },
            },
            new FilePickerFileType("单文件压缩流（gzip / bzip2 / xz / zstd）")
            {
                Patterns = new List<string> { "*.gz", "*.bz2", "*.xz", "*.zst" },
            },
        };
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(options);
        if (file == null)
        {
            UiLogger.Log("程序行为", "右键菜单压缩为：右键行路径=" + row.FullPath
                + " 结果=用户取消，未选择保存位置，本次压缩未执行");
            return;
        }
        string? archivePath = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(archivePath))
        {
            UiLogger.Log("程序行为", "右键菜单压缩为：右键行路径=" + row.FullPath
                + " 结果=失败，所选保存位置不是本地路径，本次压缩未执行");
            return;
        }
        if (string.IsNullOrEmpty(Path.GetExtension(archivePath)))
        {
            archivePath = archivePath + ".zip";
        }
        viewModel.CompressSelection(rows, archivePath);
    }

    /// <summary>右键"解压到…"：选择目标文件夹后解压压缩包，或把包内条目解出到该文件夹。</summary>
    private async void OnMenuExtractClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "右键菜单解压到：标签视图模型或所属窗口为空 结果=失败，本次解压取消");
            return;
        }
        if (sender is not MenuItem menuItem || menuItem.DataContext is not FileRow row)
        {
            UiLogger.Log("程序行为", "右键菜单解压到：菜单项未关联列表条目 结果=失败，本次解压取消");
            return;
        }
        if (!viewModel.IsExtractableRow(row))
        {
            UiLogger.Log("程序行为", "右键菜单解压到：行路径=" + row.FullPath
                + " 结果=失败，该条目不是压缩包，已提示用户");
            viewModel.ShowMessage("请右键压缩包条目使用「解压到…」");
            return;
        }
        List<FileRow> rows = CollectTargetRows(viewModel, row);
        UiLogger.Log("程序行为", "右键菜单解压到：右键行路径=" + row.FullPath + " 操作条目数=" + rows.Count
            + " 结果=成功，已弹出目标文件夹选择对话框");

        FolderPickerOpenOptions options = new FolderPickerOpenOptions();
        options.Title = "解压到";
        options.AllowMultiple = false;
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count == 0)
        {
            UiLogger.Log("程序行为", "右键菜单解压到：右键行路径=" + row.FullPath
                + " 结果=用户取消，未选择目标文件夹，本次解压未执行");
            return;
        }
        string? targetDirectory = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(targetDirectory))
        {
            UiLogger.Log("程序行为", "右键菜单解压到：右键行路径=" + row.FullPath
                + " 结果=失败，所选目标文件夹不是本地路径，本次解压未执行");
            return;
        }
        viewModel.ExtractSelectionTo(rows, targetDirectory);
    }

    /// <summary>操作目标行：有勾选条目时用全部勾选行，否则用右键所在行。</summary>
    private static List<FileRow> CollectTargetRows(FileTabViewModel viewModel, FileRow contextRow)
    {
        List<FileRow> checkedRows = viewModel.GetCheckedRows();
        if (checkedRows.Count > 0)
        {
            return checkedRows;
        }
        List<FileRow> rows = new List<FileRow>();
        rows.Add(contextRow);
        return rows;
    }

    /// <summary>修改远程文件权限（仅远程连接支持，输入八进制权限如 644）。</summary>
    private async void OnMenuChangePermissionsClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单修改权限：所属窗口或标签视图模型为空 结果=失败，本次修改取消");
            return;
        }
        if (viewModel.PermissionChanger == null)
        {
            UiLogger.Log("程序行为", "右键菜单修改权限：当前标签不是远程连接 结果=失败，无法修改权限");
            viewModel.ShowMessage("当前标签不是远程连接，无法修改权限");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            string? modeText = await InputDialog.Show(owner, "修改权限", "请输入八进制权限（如 644、755）：", "644");
            if (string.IsNullOrWhiteSpace(modeText))
            {
                UiLogger.Log("程序行为", "右键菜单修改权限：行路径=" + row.FullPath
                    + " 结果=用户取消，未输入权限，本次修改未执行");
                return;
            }
            if (!TabTaskExplorer.App.Services.WebWorkspace.TryParseOctalMode(modeText.Trim(), out int octalMode))
            {
                UiLogger.Log("程序行为", "右键菜单修改权限：行路径=" + row.FullPath + " 权限输入=" + modeText.Trim()
                    + " 结果=失败，权限格式无效，本次修改未执行");
                viewModel.ShowMessage("权限格式无效（0~777 八进制）");
                return;
            }
            string targetPath = row.FullPath;
            try
            {
                await System.Threading.Tasks.Task.Run(delegate
                {
                    viewModel.PermissionChanger.ChangePermissions(targetPath, octalMode);
                });
                viewModel.ShowMessage("权限已修改：" + row.DisplayName + " → " + modeText.Trim());
                UiLogger.Log("程序行为", "右键菜单修改权限：行路径=" + targetPath + " 新权限=" + modeText.Trim()
                    + " 结果=成功，已修改远程文件权限");
            }
            catch (Exception error)
            {
                viewModel.ShowMessage("修改权限失败：" + error.Message);
                UiLogger.LogError("程序行为", "右键菜单修改权限：行路径=" + targetPath + " 新权限=" + modeText.Trim()
                    + " 结果=失败", error);
            }
        }
    }

    private async void OnMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单重命名：所属窗口或标签视图模型为空 结果=失败，本次重命名取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            string? newName = await InputDialog.Show(owner, "重命名", "请输入新的名称：", row.DisplayName);
            if (!string.IsNullOrWhiteSpace(newName) && newName != row.DisplayName)
            {
                viewModel.RenameRow(row, newName);
                UiLogger.Log("程序行为", "右键菜单重命名：行路径=" + row.FullPath + " 新名称=" + newName
                    + " 结果=成功，已提交重命名请求");
            }
            else
            {
                UiLogger.Log("程序行为", "右键菜单重命名：行路径=" + row.FullPath
                    + " 新名称=" + (string.IsNullOrWhiteSpace(newName) ? "未输入" : newName)
                    + " 结果=未执行重命名，新名称为空或与原名称相同");
            }
        }
    }

    private void OnMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            FileTabViewModel? viewModel = this.ViewModel;
            if (viewModel == null)
            {
                UiLogger.Log("程序行为", "右键菜单删除：未找到标签视图模型 结果=失败，本次删除取消");
                return;
            }
            viewModel.DeleteRow(row);
            UiLogger.Log("程序行为", "右键菜单删除：行路径=" + row.FullPath
                + " 结果=成功，已提交删除请求");
        }
    }

    /// <summary>条目右键菜单打开前：按右键条目筛选系统右键菜单项并注入占位子菜单。</summary>
    private void OnSystemMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            UiLogger.Log("程序行为", "右键菜单打开：事件来源不是右键菜单 结果=失败，未添加系统菜单项");
            return;
        }
        if (menu.DataContext is not FileRow row)
        {
            UiLogger.Log("程序行为", "右键菜单打开：行路径=未取到 条目类型=未知 结果=失败，菜单未关联列表条目，未添加系统菜单项");
            return;
        }
        MenuItem? systemMenuHost = FindSystemMenuHost(menu);
        if (systemMenuHost == null)
        {
            UiLogger.Log("程序行为", "右键菜单打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=失败，未找到系统菜单项占位子菜单，未添加系统菜单项");
            return;
        }
        this.systemMenuContextRow = row;
        ISystemContextMenuService? systemContextMenuService = this.MainViewModel?.SystemContextMenus;
        if (systemContextMenuService == null || !systemContextMenuService.IsSupported)
        {
            systemMenuHost.Items.Clear();
            systemMenuHost.IsVisible = false;
            UiLogger.Log("程序行为", "右键菜单打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 适用条目数=0 结果=当前系统不支持系统右键菜单管理，未添加系统菜单项");
            return;
        }
        List<SystemContextMenuEntry> entries = SystemContextMenuPolicy.SelectForTarget(systemContextMenuService.Enumerate(), row.FullPath, row.IsDirectory);
        systemMenuHost.Items.Clear();
        if (entries.Count == 0)
        {
            systemMenuHost.IsVisible = false;
            UiLogger.Log("程序行为", "右键菜单打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 适用条目数=0 结果=成功，没有适用的系统菜单项，已隐藏系统菜单项子菜单");
            return;
        }
        foreach (SystemContextMenuEntry entry in entries)
        {
            MenuItem item = new MenuItem();
            item.Header = entry.DisplayName;
            string tip = entry.RequiresAdministrator ? entry.RegistryPath + "(需管理员)" : entry.RegistryPath;
            ToolTip.SetTip(item, tip);
            item.Click += this.OnSystemMenuEntryClick;
            item.DataContext = entry;
            systemMenuHost.Items.Add(item);
        }
        systemMenuHost.IsVisible = true;
        UiLogger.Log("程序行为", "右键菜单打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
            + " 系统菜单项注入数=" + entries.Count + " 目标路径=" + row.FullPath + " 结果=成功，已填充系统菜单项子菜单");
    }

    /// <summary>找到右键菜单里承载系统菜单项的占位子菜单。</summary>
    private MenuItem? FindSystemMenuHost(ContextMenu menu)
    {
        return FindMenuItemByTag(menu, SystemMenuHostTag);
    }

    /// <summary>按标记在菜单顶层项里找指定菜单项。</summary>
    private static MenuItem? FindMenuItemByTag(ContextMenu menu, string tag)
    {
        foreach (object item in menu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Tag is string itemTag && string.Equals(itemTag, tag, StringComparison.Ordinal))
            {
                return menuItem;
            }
        }
        return null;
    }

    /// <summary>条目右键菜单里注入的系统菜单项被点击：以该条目为目标唤起系统菜单项命令。</summary>
    private void OnSystemMenuEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is SystemContextMenuEntry entry)
        {
            FileRow? row = FindContextRow(menuItem) ?? this.systemMenuContextRow;
            ISystemContextMenuService? systemContextMenuService = this.MainViewModel?.SystemContextMenus;
            FileTabViewModel? viewModel = this.ViewModel;
            if (row == null || systemContextMenuService == null)
            {
                UiLogger.Log("程序行为", "唤起系统菜单项：条目名称=" + entry.DisplayName + " 注册表完整路径=" + entry.RegistryPath
                    + " 目标路径=未取到 结果=失败，未找到列表条目或系统右键菜单服务，本次唤起未执行");
                return;
            }
            bool succeeded = systemContextMenuService.Execute(entry, row.FullPath);
            UiLogger.Log("程序行为", "唤起系统菜单项：条目名称=" + entry.DisplayName + " 注册表完整路径=" + entry.RegistryPath
                + " 目标路径=" + row.FullPath
                + " 结果=" + (succeeded ? "成功，已唤起该系统菜单项命令" : "失败，唤起该系统菜单项命令未成功"));
            if (!succeeded)
            {
                viewModel?.ShowMessage("唤起系统菜单项失败：" + entry.DisplayName);
            }
        }
        else
        {
            UiLogger.Log("程序行为", "唤起系统菜单项：菜单项未关联系统菜单条目 结果=失败，本次唤起未执行");
        }
    }

    /// <summary>沿逻辑树向上找到挂了列表条目的右键菜单，用于系统菜单项点击时取右键条目。</summary>
    private static FileRow? FindContextRow(MenuItem start)
    {
        for (ILogical? current = start; current != null; current = current.GetLogicalParent())
        {
            if (current is ContextMenu menu && menu.DataContext is FileRow row)
            {
                return row;
            }
        }
        return null;
    }

    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null || sender is not Button button || button.Tag is not string sortField)
        {
            UiLogger.Log("程序行为", "列表排序切换：视图模型或表头参数缺失 结果=失败，本次排序未执行");
            return;
        }
        viewModel.SetSortField(sortField);
        this.UpdateSortArrows();
        UiLogger.Log("程序行为", "列表排序切换：排序字段=" + sortField
            + " 排序方向=" + (viewModel.SortAscending ? "升序" : "降序")
            + " 结果=成功，已按该字段重排列表并刷新表头箭头");
    }

    /// <summary>面板加载后按默认排序状态刷新表头箭头（默认按名称升序）。</summary>
    private void OnPanelLoaded(object? sender, RoutedEventArgs e)
    {
        this.UpdateSortArrows();
    }

    /// <summary>按当前排序状态刷新四个表头的升序 / 降序箭头（未排序的列不显示箭头）。</summary>
    private void UpdateSortArrows()
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        string field = viewModel.SortField;
        string arrow = viewModel.SortAscending ? "▲" : "▼";
        this.ArrowName.Text = field == "Name" ? arrow : string.Empty;
        this.ArrowModified.Text = field == "Modified" ? arrow : string.Empty;
        this.ArrowType.Text = field == "Type" ? arrow : string.Empty;
        this.ArrowSize.Text = field == "Size" ? arrow : string.Empty;
    }

    /// <summary>刷新右侧信息窗格内容。</summary>
    private void UpdateInfoPane()
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        FileRow? row = viewModel.FindSingleCheckedRow();
        if (row == null)
        {
            this.InfoNameText.Text = "未勾选条目";
            this.InfoTypeText.Text = string.Empty;
            this.InfoSizeText.Text = string.Empty;
            this.InfoModifiedText.Text = string.Empty;
            this.InfoPathText.Text = string.Empty;
            return;
        }
        this.InfoNameText.Text = row.DisplayName;
        this.InfoTypeText.Text = row.TypeText;
        this.InfoSizeText.Text = row.SizeText;
        this.InfoModifiedText.Text = row.ModifiedText;
        this.InfoPathText.Text = row.FullPath;
    }
}
