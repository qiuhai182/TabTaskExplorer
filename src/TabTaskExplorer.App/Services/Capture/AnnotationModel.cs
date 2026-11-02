using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>标注工具类型：对齐 Snipaste 截图编辑器的形状工具集合。</summary>
public enum AnnotationTool
{
    /// <summary>不绘制，仅用于浏览或取色。</summary>
    None,

    /// <summary>矩形框。</summary>
    Rectangle,

    /// <summary>椭圆框。</summary>
    Ellipse,

    /// <summary>箭头。</summary>
    Arrow,

    /// <summary>自由画笔。</summary>
    Pen,

    /// <summary>荧光笔（半透明粗线）。</summary>
    Highlight,

    /// <summary>马赛克（像素化遮挡）。</summary>
    Mosaic,

    /// <summary>高斯模糊（多次偏移采样近似）。</summary>
    Blur,

    /// <summary>文字。</summary>
    Text,

    /// <summary>序号标记（圆形数字）。</summary>
    Number,

    /// <summary>放大镜（放大点击位置附近的图像）。</summary>
    Magnify,

    /// <summary>橡皮擦（删除已有标注）。</summary>
    Eraser,
}

/// <summary>一条标注形状：坐标全部使用原图像素，渲染时再换算到界面坐标。</summary>
public class AnnotationShape
{
    /// <summary>形状类型。</summary>
    public AnnotationTool Tool { get; set; } = AnnotationTool.None;

    /// <summary>形状经过的采样点（自由画笔、荧光笔、马赛克、模糊使用）。</summary>
    public List<Point> Points { get; set; } = new List<Point>();

    /// <summary>起始点（矩形、椭圆、箭头的起点，文字与序号的落点）。</summary>
    public Point Start { get; set; }

    /// <summary>结束点（矩形、椭圆、箭头的终点）。</summary>
    public Point End { get; set; }

    /// <summary>线条或笔刷宽度（原图像素）。</summary>
    public double Thickness { get; set; } = 3.0;

    /// <summary>颜色。</summary>
    public Color Color { get; set; } = Color.FromRgb(255, 59, 48);

    /// <summary>是否填充矩形 / 椭圆。</summary>
    public bool Filled { get; set; }

    /// <summary>文字内容。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>文字字号（原图像素）。</summary>
    public double FontSize { get; set; } = 24.0;

    /// <summary>序号数值。</summary>
    public int Number { get; set; } = 1;

    /// <summary>马赛克 / 模糊的块大小（原图像素）。</summary>
    public double BlockSize { get; set; } = 12.0;

    /// <summary>计算形状的像素包围盒，用于命中测试与渲染范围。</summary>
    public Rect GetExtent()
    {
        if (this.Tool == AnnotationTool.Text)
        {
            double textWidth = Math.Max(40.0, this.Text.Length * this.FontSize * 0.62);
            return new Rect(this.Start.X, this.Start.Y, textWidth, this.FontSize * 1.25);
        }
        if (this.Tool == AnnotationTool.Number)
        {
            double radius = this.FontSize * 0.75;
            return new Rect(this.Start.X - radius, this.Start.Y - radius, radius * 2.0, radius * 2.0);
        }
        if (this.Tool == AnnotationTool.Magnify)
        {
            double radius = this.Thickness * 4.0;
            return new Rect(this.Start.X - radius, this.Start.Y - radius, radius * 2.0, radius * 2.0);
        }
        if (this.Points.Count > 0)
        {
            double minX = this.Points[0].X;
            double minY = this.Points[0].Y;
            double maxX = this.Points[0].X;
            double maxY = this.Points[0].Y;
            foreach (Point point in this.Points)
            {
                if (point.X < minX)
                {
                    minX = point.X;
                }
                if (point.Y < minY)
                {
                    minY = point.Y;
                }
                if (point.X > maxX)
                {
                    maxX = point.X;
                }
                if (point.Y > maxY)
                {
                    maxY = point.Y;
                }
            }
            double padding = this.Thickness * 0.5 + 2.0;
            return new Rect(minX - padding, minY - padding,
                maxX - minX + padding * 2.0, maxY - minY + padding * 2.0);
        }
        double left = Math.Min(this.Start.X, this.End.X);
        double top = Math.Min(this.Start.Y, this.End.Y);
        double widthValue = Math.Abs(this.End.X - this.Start.X);
        double heightValue = Math.Abs(this.End.Y - this.Start.Y);
        return new Rect(left, top, widthValue, heightValue);
    }

    /// <summary>深拷贝当前形状，供撤销栈使用。</summary>
    public AnnotationShape Clone()
    {
        AnnotationShape copy = new AnnotationShape();
        copy.Tool = this.Tool;
        copy.Start = this.Start;
        copy.End = this.End;
        copy.Thickness = this.Thickness;
        copy.Color = this.Color;
        copy.Filled = this.Filled;
        copy.Text = this.Text;
        copy.FontSize = this.FontSize;
        copy.Number = this.Number;
        copy.BlockSize = this.BlockSize;
        foreach (Point point in this.Points)
        {
            copy.Points.Add(point);
        }
        return copy;
    }
}

/// <summary>
/// 标注文档：保存已提交的形状、撤销栈与重做栈；所有操作都在界面线程执行。
/// 撤销栈保存形状列表快照，拖动过程中的临时形状不会进入历史。
/// </summary>
public class AnnotationDocument
{
    private const int MaxHistoryCount = 60;

    private readonly List<AnnotationShape> shapes = new List<AnnotationShape>();

    private readonly List<List<AnnotationShape>> undoStates = new List<List<AnnotationShape>>();

    private readonly List<List<AnnotationShape>> redoStates = new List<List<AnnotationShape>>();

    /// <summary>当前全部形状（只读视图）。</summary>
    public IReadOnlyList<AnnotationShape> Shapes
    {
        get { return this.shapes; }
    }

    /// <summary>是否可以撤销。</summary>
    public bool CanUndo
    {
        get { return this.undoStates.Count > 0; }
    }

    /// <summary>是否可以重做。</summary>
    public bool CanRedo
    {
        get { return this.redoStates.Count > 0; }
    }

    /// <summary>提交一条新形状并记录撤销点。</summary>
    public void Add(AnnotationShape shape)
    {
        if (shape == null)
        {
            return;
        }
        this.SaveUndoState();
        this.shapes.Add(shape);
        this.redoStates.Clear();
    }

    /// <summary>删除指定下标的形状并记录撤销点。</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= this.shapes.Count)
        {
            return false;
        }
        this.SaveUndoState();
        this.shapes.RemoveAt(index);
        this.redoStates.Clear();
        return true;
    }

    /// <summary>撤销一步。</summary>
    public bool Undo()
    {
        if (this.undoStates.Count == 0)
        {
            return false;
        }
        this.redoStates.Add(this.Snapshot(this.shapes));
        List<AnnotationShape> previous = this.undoStates[this.undoStates.Count - 1];
        this.undoStates.RemoveAt(this.undoStates.Count - 1);
        this.Restore(previous);
        return true;
    }

    /// <summary>重做一步。</summary>
    public bool Redo()
    {
        if (this.redoStates.Count == 0)
        {
            return false;
        }
        this.undoStates.Add(this.Snapshot(this.shapes));
        List<AnnotationShape> next = this.redoStates[this.redoStates.Count - 1];
        this.redoStates.RemoveAt(this.redoStates.Count - 1);
        this.Restore(next);
        return true;
    }

    /// <summary>清空全部形状（记录撤销点）。</summary>
    public void Clear()
    {
        if (this.shapes.Count == 0)
        {
            return;
        }
        this.SaveUndoState();
        this.shapes.Clear();
        this.redoStates.Clear();
    }

    /// <summary>取当前形状快照（供渲染线程安全遍历时使用）。</summary>
    public List<AnnotationShape> Snapshot()
    {
        return this.Snapshot(this.shapes);
    }

    private void SaveUndoState()
    {
        this.undoStates.Add(this.Snapshot(this.shapes));
        if (this.undoStates.Count > MaxHistoryCount)
        {
            this.undoStates.RemoveAt(0);
        }
    }

    private List<AnnotationShape> Snapshot(List<AnnotationShape> source)
    {
        List<AnnotationShape> copy = new List<AnnotationShape>(source.Count);
        foreach (AnnotationShape shape in source)
        {
            copy.Add(shape.Clone());
        }
        return copy;
    }

    private void Restore(List<AnnotationShape> source)
    {
        this.shapes.Clear();
        foreach (AnnotationShape shape in source)
        {
            this.shapes.Add(shape.Clone());
        }
    }
}
