using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// 滚动长截图拼接器：维护一张不断向下增长的长图，每次把新帧与长图底部的一段「探针带」做匹配，
/// 找到探针带在新帧中的位置后，只追加它下方真正新增的像素。
/// 采用逐行亮度采样签名搜索，避免整图逐像素比对带来的性能问题；
/// 因为只追加探针带下方的内容，固定页眉（sticky header）不会重复出现。
/// </summary>
internal sealed class ScrollStitcher : IDisposable
{
    /// <summary>每行用于比对的采样列数。</summary>
    private const int SignatureSamples = 64;

    /// <summary>探针带最大高度（像素）。</summary>
    private const int MaxProbeHeight = 180;

    /// <summary>判定「有新内容」所需的最小新增行数。</summary>
    private const int MinAddedRows = 3;

    private readonly int width;
    private readonly int maxHeight;
    private readonly int minOverlapRows;
    private readonly double tolerance;

    private Bitmap? canvas;
    private int canvasHeight;

    /// <summary>按目标宽度、最大高度与匹配参数创建拼接器。</summary>
    public ScrollStitcher(int width, int maxHeight, int minOverlapRows, double tolerance)
    {
        this.width = width;
        this.maxHeight = maxHeight;
        this.minOverlapRows = Math.Max(8, minOverlapRows);
        this.tolerance = tolerance <= 0 ? 0.04 : tolerance;
    }

    /// <summary>当前长图高度（像素）。</summary>
    public int Height
    {
        get { return this.canvasHeight; }
    }

    /// <summary>当前长图宽度（像素）。</summary>
    public int Width
    {
        get { return this.canvas == null ? 0 : this.width; }
    }

    /// <summary>是否已经有内容。</summary>
    public bool HasContent
    {
        get { return this.canvasHeight > 0; }
    }

    /// <summary>
    /// 追加一帧：首帧直接作为长图，后续帧按重叠区匹配后只追加新增部分。
    /// 返回 true 表示匹配成功（<paramref name="addedRows"/> 可能为 0，代表画面没有滚动）。
    /// </summary>
    public bool Append(Bitmap frame, out int addedRows, out string detail)
    {
        addedRows = 0;
        if (frame.Width != this.width)
        {
            detail = "帧宽度与长图不一致：" + frame.Width + "/" + this.width;
            return false;
        }
        if (this.canvas == null)
        {
            return this.AppendFirstFrame(frame, out addedRows, out detail);
        }
        if (frame.Height < this.minOverlapRows)
        {
            detail = "帧高度小于最小重叠行数：" + frame.Height;
            return false;
        }

        byte[] frameSignature = ComputeRowsSignature(frame, 0, frame.Height);
        int probeHeight = Math.Min(MaxProbeHeight, this.canvasHeight);
        if (probeHeight < this.minOverlapRows)
        {
            probeHeight = Math.Min(this.minOverlapRows, this.canvasHeight);
        }
        // 优先取长图最底部作为探针带；若匹配成功但没有新内容，可能是固定底栏（sticky footer），
        // 再把探针带往上挪两倍高度重试，避免把固定底栏当成「页面没有滚动」
        int bottomTop = Math.Max(0, this.canvasHeight - probeHeight - 8);
        int upperTop = Math.Max(0, bottomTop - probeHeight * 2);
        bool matchedAny = false;
        bool hasAppendable = false;
        int appendStart = 0;
        int bestScroll = 0;
        int[] candidateTops = bottomTop == upperTop
            ? new int[] { bottomTop }
            : new int[] { bottomTop, upperTop };
        foreach (int bandTop in candidateTops)
        {
            int bandHeight = Math.Min(probeHeight, this.canvasHeight - bandTop);
            if (bandHeight < this.minOverlapRows)
            {
                continue;
            }
            byte[] canvasSignature = ComputeRowsSignature(this.canvas, bandTop, bandHeight);
            int matchedY;
            double score;
            if (!TryMatch(canvasSignature, bandHeight, frameSignature, frame.Height, out matchedY, out score))
            {
                continue;
            }
            matchedAny = true;
            // 探针带可能故意不贴长图最底部（避开固定底栏），因此真正的「新增起点」要按
            // 长图当前底部位置换算：探针带起点对应新帧的 matchedY，长图底部对应
            // matchedY + (canvasHeight - bandTop)，只从这一行往下追加，避免重复像素。
            int appendOffset = matchedY + (this.canvasHeight - bandTop);
            int added = frame.Height - appendOffset;
            if (added >= MinAddedRows)
            {
                hasAppendable = true;
                appendStart = appendOffset;
                bestScroll = bandTop - matchedY;
                detail = "探针位置=" + bandTop + " 新帧匹配位置=" + matchedY
                    + " 匹配误差=" + Math.Round(score, 4)
                    + " 滚动位移=" + bestScroll;
                break;
            }
            detail = "探针位置=" + bandTop + " 新帧匹配位置=" + matchedY
                + " 匹配误差=" + Math.Round(score, 4) + " 新增行数=0";
        }
        if (!hasAppendable)
        {
            if (matchedAny)
            {
                detail = "已匹配到重叠区但没有新增内容（画面未继续滚动）";
                return true;
            }
            detail = "未找到重叠区（可能滚动过快、页面跳动或已损坏）";
            return false;
        }

        int available = this.maxHeight - this.canvasHeight;
        if (available <= 0)
        {
            detail = "已达到最大高度：" + this.maxHeight + " 像素";
            return true;
        }
        int rows = Math.Min(frame.Height - appendStart, available);
        if (rows < MinAddedRows)
        {
            detail = "剩余可追加高度不足：" + rows + " 行";
            return true;
        }
        Bitmap next = new Bitmap(this.width, this.canvasHeight + rows, PixelFormat.Format32bppArgb);
        try
        {
            using (Graphics graphics = Graphics.FromImage(next))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(this.canvas, new Rectangle(0, 0, this.width, this.canvasHeight),
                    0, 0, this.width, this.canvasHeight, GraphicsUnit.Pixel);
                graphics.DrawImage(frame, new Rectangle(0, this.canvasHeight, this.width, rows),
                    0, appendStart, this.width, rows, GraphicsUnit.Pixel);
            }
        }
        catch
        {
            next.Dispose();
            throw;
        }
        this.canvas.Dispose();
        this.canvas = next;
        this.canvasHeight += rows;
        addedRows = rows;
        detail = "追加新增内容：起始行=" + appendStart + " 新增行数=" + rows
            + " 滚动位移=" + bestScroll + " 长图高度=" + this.canvasHeight;
        return true;
    }

    /// <summary>把当前长图编码为 PNG 字节。</summary>
    public byte[] EncodePng()
    {
        if (this.canvas == null)
        {
            return Array.Empty<byte>();
        }
        using (MemoryStream stream = new MemoryStream())
        {
            this.canvas.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.canvas != null)
        {
            this.canvas.Dispose();
            this.canvas = null;
        }
        this.canvasHeight = 0;
    }

    /// <summary>首帧直接成为长图起点，超过最大高度时只保留顶部。</summary>
    private bool AppendFirstFrame(Bitmap frame, out int addedRows, out string detail)
    {
        int rows = Math.Min(frame.Height, this.maxHeight);
        Bitmap first = new Bitmap(this.width, rows, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(first))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(frame, new Rectangle(0, 0, this.width, rows),
                0, 0, this.width, rows, GraphicsUnit.Pixel);
        }
        this.canvas = first;
        this.canvasHeight = rows;
        addedRows = rows;
        detail = "首帧入图：帧高度=" + frame.Height + " 实际高度=" + rows;
        return true;
    }

    /// <summary>
    /// 在候选位置中寻找探针带的最佳匹配：返回匹配位置与归一化平均像素差。
    /// 误差小于容差才算匹配成功；搜索到明显更优的结果时提前结束。
    /// </summary>
    private bool TryMatch(byte[] canvasSignature, int bandHeight, byte[] frameSignature,
        int frameHeight, out int matchedY, out double bestScore)
    {
        matchedY = -1;
        bestScore = double.MaxValue;
        int maxY = frameHeight - bandHeight;
        if (maxY < 0)
        {
            return false;
        }
        double earlyExit = this.tolerance * 0.35;
        for (int y = 0; y <= maxY; y++)
        {
            long sum = 0;
            for (int row = 0; row < bandHeight; row++)
            {
                int canvasOffset = row * SignatureSamples;
                int frameOffset = (y + row) * SignatureSamples;
                for (int sample = 0; sample < SignatureSamples; sample++)
                {
                    int difference = canvasSignature[canvasOffset + sample] - frameSignature[frameOffset + sample];
                    if (difference < 0)
                    {
                        difference = -difference;
                    }
                    sum += difference;
                }
            }
            double score = sum / (double)(bandHeight * SignatureSamples * 255);
            if (score < bestScore)
            {
                bestScore = score;
                matchedY = y;
                if (bestScore <= earlyExit)
                {
                    break;
                }
            }
        }
        return bestScore <= this.tolerance;
    }

    /// <summary>
    /// 计算指定行范围的逐行亮度签名：每行均匀采样固定数量的列，得到 0-255 的灰度值。
    /// 位图必须是 32bppArgb（调用方已归一化）。
    /// </summary>
    private static byte[] ComputeRowsSignature(Bitmap bitmap, int top, int height)
    {
        byte[] signature = new byte[height * SignatureSamples];
        Rectangle rectangle = new Rectangle(0, top, bitmap.Width, height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] rowBuffer = new byte[stride];
            int[] sampleColumns = new int[SignatureSamples];
            for (int sample = 0; sample < SignatureSamples; sample++)
            {
                long column = (long)sample * (bitmap.Width - 1) / (SignatureSamples - 1);
                sampleColumns[sample] = (int)column * 4;
            }
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, row * stride), rowBuffer, 0, stride);
                int offset = row * SignatureSamples;
                for (int sample = 0; sample < SignatureSamples; sample++)
                {
                    int pixel = sampleColumns[sample];
                    int blue = rowBuffer[pixel];
                    int green = rowBuffer[pixel + 1];
                    int red = rowBuffer[pixel + 2];
                    // 整数近似亮度：0.299R + 0.587G + 0.114B
                    signature[offset + sample] = (byte)((red * 77 + green * 150 + blue * 29) >> 8);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return signature;
    }
}
