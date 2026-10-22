using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 进程监控的资源走势图控件：三条折线（系统处理器、系统内存、选中进程处理器），
/// 横轴为最近 60 次采样（约两分钟），纵轴为 0% 到 100%。
/// 订阅视图模型的走势图变化通知后重绘，不引入任何后台定时器。
/// </summary>
public class ProcessTrendChart : Control
{
    /// <summary>与视图模型约定的采样点数（与视图模型的缓冲容量保持一致）。</summary>
    private const int SampleCapacity = 60;

    /// <summary>已订阅的视图模型（切换 DataContext 时先退订旧的）。</summary>
    private ProcessPanelViewModel? subscribedViewModel;

    /// <summary>背景填充。</summary>
    private static readonly Brush BackgroundFill = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128));

    /// <summary>网格线画笔（0%、50%、100% 三条横线）。</summary>
    private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), 1);

    /// <summary>系统处理器占用折线画笔（绿色）。</summary>
    private static readonly Pen SystemCpuPen = new Pen(new SolidColorBrush(Color.FromRgb(46, 125, 50)), 1.5);

    /// <summary>系统内存占用折线画笔（紫色）。</summary>
    private static readonly Pen SystemMemoryPen = new Pen(new SolidColorBrush(Color.FromRgb(126, 87, 194)), 1.5);

    /// <summary>选中进程处理器占用折线画笔（橙色）。</summary>
    private static readonly Pen SelectedCpuPen = new Pen(new SolidColorBrush(Color.FromRgb(239, 108, 0)), 1.5);

    /// <summary>图例文字画笔。</summary>
    private static readonly Brush LegendTextBrush = new SolidColorBrush(Color.FromArgb(200, 64, 64, 64));

    /// <summary>创建控件并订阅数据源切换事件。</summary>
    public ProcessTrendChart()
    {
        this.DataContextChanged += this.OnChartDataContextChanged;
    }

    /// <summary>数据源切换：退订旧视图模型，订阅新视图模型的属性变化，然后重绘一次。</summary>
    private void OnChartDataContextChanged(object? sender, EventArgs e)
    {
        if (this.subscribedViewModel != null)
        {
            this.subscribedViewModel.PropertyChanged -= this.OnChartViewModelPropertyChanged;
        }
        this.subscribedViewModel = this.DataContext as ProcessPanelViewModel;
        if (this.subscribedViewModel != null)
        {
            this.subscribedViewModel.PropertyChanged += this.OnChartViewModelPropertyChanged;
        }
        this.InvalidateVisual();
    }

    /// <summary>视图模型属性变化：只有走势图数据更新时才重绘。</summary>
    private void OnChartViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessPanelViewModel.TrendUpdatedAt))
        {
            this.InvalidateVisual();
        }
    }

    /// <summary>绘制背景、网格、三条折线与图例。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new Rect(this.Bounds.Size);
        if (bounds.Width < 20 || bounds.Height < 20)
        {
            // 尺寸不足时不绘制，避免除零与文字重叠
            return;
        }
        context.FillRectangle(BackgroundFill, bounds);

        // 网格：100%、50%、0% 三条横线
        context.DrawLine(GridPen, new Point(0, 1), new Point(bounds.Width, 1));
        context.DrawLine(GridPen, new Point(0, bounds.Height / 2), new Point(bounds.Width, bounds.Height / 2));
        context.DrawLine(GridPen, new Point(0, bounds.Height - 1), new Point(bounds.Width, bounds.Height - 1));

        ProcessPanelViewModel? viewModel = this.subscribedViewModel;
        if (viewModel != null)
        {
            this.DrawSeries(context, bounds, viewModel.SystemCpuTrend, SystemCpuPen);
            this.DrawSeries(context, bounds, viewModel.SystemMemoryTrend, SystemMemoryPen);
            this.DrawSeries(context, bounds, viewModel.SelectedCpuTrend, SelectedCpuPen);
        }
        this.DrawLegend(context, bounds);
    }

    /// <summary>把一条序列画成折线：横轴按固定 60 点铺满（历史未满时占左侧），纵轴按 0 到 100 映射。</summary>
    private void DrawSeries(DrawingContext context, Rect bounds, IReadOnlyList<double>? values, Pen pen)
    {
        if (values == null || values.Count < 2)
        {
            // 少于两个点画不出线段
            return;
        }
        Point previousPoint = default;
        for (int index = 0; index < values.Count; index++)
        {
            double percent = values[index];
            if (percent < 0)
            {
                percent = 0;
            }
            if (percent > 100)
            {
                percent = 100;
            }
            double x = bounds.Width * index / (SampleCapacity - 1);
            double y = bounds.Height - (percent / 100.0) * bounds.Height;
            Point currentPoint = new Point(x, y);
            if (index > 0)
            {
                context.DrawLine(pen, previousPoint, currentPoint);
            }
            previousPoint = currentPoint;
        }
    }

    /// <summary>绘制左上角图例：色块 + 名称，标明三条折线各自的含义。</summary>
    private void DrawLegend(DrawingContext context, Rect bounds)
    {
        double textY = 3;
        double currentX = 6;
        FormattedText systemCpuText = ProcessTrendChart.MakeLegendText("系统处理器");
        FormattedText systemMemoryText = ProcessTrendChart.MakeLegendText("系统内存");
        FormattedText selectedCpuText = ProcessTrendChart.MakeLegendText("选中进程");
        currentX = ProcessTrendChart.DrawLegendItem(context, currentX, textY, SystemCpuPen.Brush, systemCpuText, bounds);
        currentX = ProcessTrendChart.DrawLegendItem(context, currentX, textY, SystemMemoryPen.Brush, systemMemoryText, bounds);
        ProcessTrendChart.DrawLegendItem(context, currentX, textY, SelectedCpuPen.Brush, selectedCpuText, bounds);
    }

    /// <summary>绘制单个图例项（色块 + 文字），返回下一个图例项的起始横坐标。</summary>
    private static double DrawLegendItem(
        DrawingContext context,
        double x,
        double y,
        IBrush brush,
        FormattedText text,
        Rect bounds)
    {
        if (x > bounds.Width - 40)
        {
            // 右侧空间不足：后面的图例项不再绘制
            return x;
        }
        Rect colorRect = new Rect(x, y + 3, 12, 4);
        context.FillRectangle(brush, colorRect);
        double textX = x + 16;
        context.DrawText(text, new Point(textX, y));
        return textX + text.Width + 10;
    }

    /// <summary>生成一条图例文字。</summary>
    private static FormattedText MakeLegendText(string text)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            11,
            LegendTextBrush);
    }
}
