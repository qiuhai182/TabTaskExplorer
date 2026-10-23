using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 表格列宽拖动结束后的视图模型契约：接收拖动得到的全像素列宽并记录完成日志。
/// 表头与行模板是两个独立网格，列宽变化必须经视图模型的同一个属性同时通知两边。
/// </summary>
public interface IColumnWidthViewModel
{
    /// <summary>把拖动得到的全像素列宽应用到界面（表头与所有行同时生效）。</summary>
    /// <param name="widths">按列序号排列的列宽，单位像素。</param>
    void ApplyColumnWidths(double[] widths);

    /// <summary>拖动结束后记录一条包含视图名与全部列宽的纯中文日志。</summary>
    void LogColumnWidths();
}

/// <summary>
/// 表格列宽任意拖动行为：
/// 表头网格用 Enabled 附加属性在加载后为每一列右边缘生成拖动条，
/// 拖动时相邻两列此消彼长（最小列宽 40 像素），拖最后一列只吃右侧留白且总宽不超过表头；
/// 调整结果经 Widths 附加属性重建列定义，表头与虚拟化行模板里的独立网格同时生效。
/// 列宽只在本次运行内有效，不写入设置存储，重启后恢复界面文件里的默认列宽。
/// </summary>
public class ColumnResize
{
    /// <summary>单列允许的最小宽度，单位像素。</summary>
    private const double MinimumColumnWidth = 40;

    /// <summary>拖动条宽度，单位像素（向列边界左右各压住 3 像素）。</summary>
    private const double ThumbWidth = 6;

    /// <summary>列宽字符串附加属性：形如 30,26,480,140,130,110；空值表示沿用界面文件里的默认列宽。</summary>
    public static readonly AttachedProperty<string> WidthsProperty =
        AvaloniaProperty.RegisterAttached<ColumnResize, Grid, string>("Widths", string.Empty);

    /// <summary>启用列宽拖动附加属性：仅在表头网格上取真。</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ColumnResize, Grid, bool>("Enabled", false);

    static ColumnResize()
    {
        WidthsProperty.Changed.AddClassHandler<Grid>(OnWidthsChanged);
        EnabledProperty.Changed.AddClassHandler<Grid>(OnEnabledChanged);
    }

    /// <summary>读取列宽字符串附加属性。</summary>
    /// <param name="element">网格。</param>
    /// <returns>列宽字符串。</returns>
    public static string GetWidths(Grid element)
    {
        return element.GetValue(WidthsProperty);
    }

    /// <summary>写入列宽字符串附加属性。</summary>
    /// <param name="element">网格。</param>
    /// <param name="value">列宽字符串。</param>
    public static void SetWidths(Grid element, string value)
    {
        element.SetValue(WidthsProperty, value);
    }

    /// <summary>读取是否启用列宽拖动。</summary>
    /// <param name="element">表头网格。</param>
    /// <returns>是否启用。</returns>
    public static bool GetEnabled(Grid element)
    {
        return element.GetValue(EnabledProperty);
    }

    /// <summary>写入是否启用列宽拖动。</summary>
    /// <param name="element">表头网格。</param>
    /// <param name="value">是否启用。</param>
    public static void SetEnabled(Grid element, bool value)
    {
        element.SetValue(EnabledProperty, value);
    }

    /// <summary>把全像素列宽数组拼成列宽字符串（固定使用点作小数分隔符，避免逗号分隔符冲突）。</summary>
    /// <param name="widths">按列序号排列的列宽，单位像素。</param>
    /// <returns>逗号分隔的列宽字符串。</returns>
    public static string FormatWidths(double[] widths)
    {
        string[] parts = new string[widths.Length];
        for (int index = 0; index < widths.Length; index++)
        {
            parts[index] = widths[index].ToString("0.##", CultureInfo.InvariantCulture);
        }
        return string.Join(",", parts);
    }

    /// <summary>列宽字符串变化回调：非空时按字符串重建列定义（全部为像素列），空值保留界面文件里的默认列宽。</summary>
    /// <param name="grid">目标网格（表头或行模板里的网格）。</param>
    /// <param name="e">属性变化信息。</param>
    private static void OnWidthsChanged(Grid grid, AvaloniaPropertyChangedEventArgs e)
    {
        string value = e.GetNewValue<string>() ?? string.Empty;
        if (value.Length == 0)
        {
            UiLogger.Log("界面交互", "表头列宽同步跳过：列宽字符串为空，沿用界面文件默认列宽 所在视图模型="
                + DescribeViewModel(grid));
            return;
        }
        try
        {
            grid.ColumnDefinitions = new ColumnDefinitions(value);
        }
        catch (Exception exception)
        {
            UiLogger.LogError("界面交互", "表头列宽同步失败：列宽字符串无法解析 列宽字符串=" + value
                + " 所在视图模型=" + DescribeViewModel(grid), exception);
            return;
        }
    }

    /// <summary>启用拖动变化回调：表头已加载时立即生成拖动条，尚未加载时等加载完成再生成。</summary>
    /// <param name="grid">表头网格。</param>
    /// <param name="e">属性变化信息。</param>
    private static void OnEnabledChanged(Grid grid, AvaloniaPropertyChangedEventArgs e)
    {
        bool enabled = e.GetNewValue<bool>();
        if (!enabled)
        {
            UiLogger.Log("界面交互", "表头列宽拖动被关闭：所在视图模型=" + DescribeViewModel(grid));
            return;
        }
        if (grid.IsLoaded)
        {
            BuildThumbs(grid);
            return;
        }
        grid.Loaded += OnHeaderLoaded;
    }

    /// <summary>表头加载完成：此时界面文件里的列定义已全部就位，开始生成拖动条。</summary>
    /// <param name="sender">表头网格。</param>
    /// <param name="e">路由事件信息。</param>
    private static void OnHeaderLoaded(object? sender, RoutedEventArgs e)
    {
        Grid? grid = sender as Grid;
        if (grid == null)
        {
            UiLogger.Log("界面交互", "表头列宽拖动生成失败：加载事件来源不是网格");
            return;
        }
        grid.Loaded -= OnHeaderLoaded;
        BuildThumbs(grid);
    }

    /// <summary>为表头每一列的右边缘生成一个拖动条（已生成过时记录日志后直接返回）。</summary>
    /// <param name="header">表头网格。</param>
    private static void BuildThumbs(Grid header)
    {
        foreach (Control child in header.Children)
        {
            if (child is Thumb)
            {
                UiLogger.Log("界面交互", "表头列宽拖动条跳过生成：该表头已经生成过拖动条 所在视图模型="
                    + DescribeViewModel(header));
                return;
            }
        }
        int count = header.ColumnDefinitions.Count;
        if (count == 0)
        {
            UiLogger.Log("界面交互", "表头列宽拖动条生成失败：表头没有列定义 所在视图模型=" + DescribeViewModel(header));
            return;
        }
        for (int index = 0; index < count; index++)
        {
            Thumb thumb = new Thumb();
            thumb.Width = ThumbWidth;
            thumb.Margin = new Thickness(-3, 0, 0, 0);
            thumb.HorizontalAlignment = HorizontalAlignment.Left;
            thumb.VerticalAlignment = VerticalAlignment.Stretch;
            thumb.ZIndex = 10;
            thumb.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            thumb.DragDelta += OnThumbDragDelta;
            thumb.DragCompleted += OnThumbDragCompleted;
            Grid.SetColumn(thumb, index);
            header.Children.Add(thumb);
        }
        UiLogger.Log("界面交互", "表头列宽拖动条生成完成：列数=" + count + " 拖动条宽度=6像素 最小列宽=40像素 所在视图模型="
            + DescribeViewModel(header));
    }

    /// <summary>拖动中：按本次位移调整相邻两列（末列只吃右侧留白），再交给视图模型同步到表头与所有行。</summary>
    /// <param name="sender">拖动条。</param>
    /// <param name="e">位移信息（相对上一次事件的增量位移）。</param>
    private static void OnThumbDragDelta(object? sender, VectorEventArgs e)
    {
        Thumb? thumb = sender as Thumb;
        if (thumb == null)
        {
            UiLogger.Log("界面交互", "表头列宽拖动失败：事件来源不是列宽拖动条");
            return;
        }
        Grid? header = thumb.Parent as Grid;
        if (header == null)
        {
            UiLogger.Log("界面交互", "表头列宽拖动失败：列宽拖动条没有挂在表头网格上");
            return;
        }
        int index = Grid.GetColumn(thumb);
        int count = header.ColumnDefinitions.Count;
        if (index < 0 || index >= count)
        {
            UiLogger.Log("界面交互", "表头列宽拖动失败：拖动条所在列序号超出列范围 拖动列序号=" + index
                + " 表头列数=" + count + " 所在视图模型=" + DescribeViewModel(header));
            return;
        }
        double[]? snapshot = ColumnResize.SnapshotWidths(header);
        if (snapshot == null)
        {
            return;
        }
        double[] widths = snapshot;
        double delta = e.Vector.X;
        if (index == count - 1)
        {
            // 末列没有右邻列：只调整自己，且总宽不超过表头宽度，右侧留白全部归末列使用
            double total = 0;
            for (int position = 0; position < widths.Length; position++)
            {
                total = total + widths[position];
            }
            double room = header.Bounds.Width - total;
            if (delta > room)
            {
                delta = room;
            }
            widths[index] = widths[index] + delta;
            if (widths[index] < MinimumColumnWidth)
            {
                widths[index] = MinimumColumnWidth;
            }
        }
        else
        {
            // 中间列：左列加多少右列就减多少，两侧都不低于最小列宽
            double newLeft = widths[index] + delta;
            if (newLeft < MinimumColumnWidth)
            {
                newLeft = MinimumColumnWidth;
            }
            double newRight = widths[index + 1] - (newLeft - widths[index]);
            if (newRight < MinimumColumnWidth)
            {
                newRight = MinimumColumnWidth;
                newLeft = widths[index] + widths[index + 1] - MinimumColumnWidth;
            }
            widths[index] = newLeft;
            widths[index + 1] = newRight;
        }
        ColumnResize.ApplyToViewModel(header, widths);
    }

    /// <summary>拖动结束：通知视图模型记录本次列宽调整的完成日志。</summary>
    /// <param name="sender">拖动条。</param>
    /// <param name="e">结束信息（未使用）。</param>
    private static void OnThumbDragCompleted(object? sender, VectorEventArgs e)
    {
        Thumb? thumb = sender as Thumb;
        if (thumb == null)
        {
            UiLogger.Log("界面交互", "表头列宽拖动结束处理失败：事件来源不是列宽拖动条");
            return;
        }
        Grid? header = thumb.Parent as Grid;
        if (header == null)
        {
            UiLogger.Log("界面交互", "表头列宽拖动结束处理失败：列宽拖动条没有挂在表头网格上");
            return;
        }
        IColumnWidthViewModel? viewModel = header.DataContext as IColumnWidthViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("界面交互", "表头列宽拖动结束处理失败：视图模型没有实现列宽接口 所在视图模型="
                + DescribeViewModel(header));
            return;
        }
        viewModel.LogColumnWidths();
    }

    /// <summary>读取表头各列的当前实际像素宽度；列未完成布局时记录日志并返回空。</summary>
    /// <param name="header">表头网格。</param>
    /// <returns>按列序号排列的实际宽度数组；失败时为 null。</returns>
    private static double[]? SnapshotWidths(Grid header)
    {
        int count = header.ColumnDefinitions.Count;
        if (count == 0)
        {
            UiLogger.Log("界面交互", "表头列宽快照失败：表头没有列定义 所在视图模型=" + DescribeViewModel(header));
            return null;
        }
        double[] widths = new double[count];
        for (int index = 0; index < count; index++)
        {
            double actualWidth = header.ColumnDefinitions[index].ActualWidth;
            if (actualWidth < 1)
            {
                UiLogger.Log("界面交互", "表头列宽快照失败：列尚未完成布局 拖动列序号=" + index
                    + " 实际宽度=" + actualWidth.ToString("0.##", CultureInfo.InvariantCulture)
                    + " 所在视图模型=" + DescribeViewModel(header));
                return null;
            }
            widths[index] = actualWidth;
        }
        return widths;
    }

    /// <summary>把调整后的列宽交给视图模型，由它同时更新表头与所有行的列宽属性。</summary>
    /// <param name="header">表头网格。</param>
    /// <param name="widths">按列序号排列的列宽，单位像素。</param>
    private static void ApplyToViewModel(Grid header, double[] widths)
    {
        IColumnWidthViewModel? viewModel = header.DataContext as IColumnWidthViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("界面交互", "表头列宽拖动失败：视图模型没有实现列宽接口 所在视图模型="
                + DescribeViewModel(header));
            return;
        }
        viewModel.ApplyColumnWidths(widths);
    }

    /// <summary>取所在视图模型的类型名，用于日志定位（没有数据上下文时记为无）。</summary>
    /// <param name="grid">表头网格。</param>
    /// <returns>视图模型类型名。</returns>
    private static string DescribeViewModel(Grid grid)
    {
        if (grid.DataContext == null)
        {
            return "无";
        }
        return grid.DataContext.GetType().Name;
    }
}
