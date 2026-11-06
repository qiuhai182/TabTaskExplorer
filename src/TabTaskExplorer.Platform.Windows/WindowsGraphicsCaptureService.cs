using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 截图主路径：Windows.Graphics.Capture（WGC）+ Direct3D11 读回像素，
/// 失败或不可用时自动回退 <see cref="WindowsScreenCaptureService"/> 的 GDI 实现。
/// 本文件不依赖 WinRT 投影（工程 TFM 不含 Windows 目标），全部通过手写 COM 互操作调用；
/// 接口 IID 与 vtable 序号取自本机 Windows SDK：
/// Windows.Graphics.Capture.idl、Windows.Graphics.Capture.Interop.h、d3d11.h、dxgi.h。
/// </summary>
public class WindowsGraphicsCaptureService : IScreenCaptureService
{
    private const int WgcMinimumBuild = 18362;

    private const int PixelFormatBgra8 = 87;

    private const int FramePoolBufferCount = 2;

    private const int FirstFrameTimeoutMilliseconds = 1500;

    private const int D3D11SdkVersion = 7;

    private const uint D3D11CreateDeviceBgraSupport = 0x20;

    private const int D3DDriverTypeHardware = 1;

    private const int D3D11UsageStaging = 3;

    private const int D3D11CpuAccessRead = 0x20000;

    private const int D3D11MapRead = 1;

    private const int MonitorDefaultToNearest = 2;

    private static readonly Guid GraphicsCaptureItemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private static readonly Guid GraphicsCaptureItemInteropId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    private static readonly Guid GraphicsCaptureSessionStaticsId = new Guid("2224A540-5974-49AA-B232-0882536F4CB5");

    private static readonly Guid GraphicsCaptureSession2Id = new Guid("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");

    private static readonly Guid GraphicsCaptureSession3Id = new Guid("F2CDD966-22AE-5EA1-9596-3A289344C3BE");

    private static readonly Guid DxgiInterfaceAccessId = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    private static readonly Guid D3D11Texture2DId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private static readonly Guid DxgiDeviceId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    private static readonly object DeviceSyncRoot = new object();

    private static WgcDevice? sharedDevice;

    private static bool? sessionSupported;

    private static string sessionUnsupportedReason = string.Empty;

    private readonly WindowsScreenCaptureService gdiCapture;

    /// <summary>
    /// 构造主路径服务。
    /// </summary>
    /// <param name="gdiCapture">回退用的 GDI 截图实现；为空时内部新建一个。</param>
    public WindowsGraphicsCaptureService(WindowsScreenCaptureService? gdiCapture = null)
    {
        this.gdiCapture = gdiCapture ?? new WindowsScreenCaptureService();
    }

    /// <summary>诊断日志回调（由组合根接到界面日志），可为空。</summary>
    public Action<string>? Log { get; set; }

    /// <inheritdoc />
    public ScreenCaptureCapabilities Capabilities { get; } = new ScreenCaptureCapabilities
    {
        FullScreen = true,
        PrimaryScreen = true,
        Region = true,
        ActiveWindow = true,
        WindowPicker = false,
        HdrAware = true,
        OccludedWindow = true,
        BackendName = "WGC（失败自动回退 GDI）",
    };

    /// <inheritdoc />
    public Task<ScreenCaptureResult> CaptureAsync(ScreenCaptureRequest request)
    {
        return Task.Run(delegate
        {
            return this.CaptureCore(request);
        });
    }

    /// <summary>按请求的后端偏好执行：自动/强制 WGC 先走 WGC，失败回退 GDI。</summary>
    private ScreenCaptureResult CaptureCore(ScreenCaptureRequest request)
    {
        if (request.Backend == CaptureBackend.Gdi)
        {
            return this.CaptureWithGdi(request, "已强制 GDI");
        }
        string unsupportedReason;
        if (!IsWgcAvailable(out unsupportedReason))
        {
            this.Log?.Invoke("采集后端：WGC 不可用（" + unsupportedReason + "），本帧回退 GDI");
            return this.CaptureWithGdi(request, "WGC 不可用：" + unsupportedReason);
        }
        string failureReason;
        ScreenCaptureResult? wgcResult = this.TryCaptureWithWgc(request, out failureReason);
        if (wgcResult != null)
        {
            return wgcResult;
        }
        this.Log?.Invoke("采集后端：WGC 采集失败（" + failureReason + "），本帧回退 GDI");
        return this.CaptureWithGdi(request, "WGC 失败：" + failureReason);
    }

    /// <summary>回退到 GDI 截图，并在结果中标明回退原因。</summary>
    private ScreenCaptureResult CaptureWithGdi(ScreenCaptureRequest request, string reason)
    {
        ScreenCaptureResult result = this.gdiCapture.CaptureAsync(request).GetAwaiter().GetResult();
        result.BackendName = result.Success ? "GDI（" + reason + "）" : "GDI（" + reason + "，失败）";
        return result;
    }

    /// <summary>判断当前系统与运行时是否支持 WGC；结果缓存，避免每次截图重复探测。</summary>
    private static bool IsWgcAvailable(out string reason)
    {
        if (sessionSupported.HasValue)
        {
            reason = sessionUnsupportedReason;
            return sessionSupported.Value;
        }
        lock (DeviceSyncRoot)
        {
            if (sessionSupported.HasValue)
            {
                reason = sessionUnsupportedReason;
                return sessionSupported.Value;
            }
            sessionSupported = CheckSessionSupport(out sessionUnsupportedReason);
            reason = sessionUnsupportedReason;
            return sessionSupported.Value;
        }
    }

    /// <summary>用官方 IsSupported 静态方法探测一次（需要 Windows 10 1903 及以上）。</summary>
    private static bool CheckSessionSupport(out string reason)
    {
        reason = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            reason = "非 Windows 系统";
            return false;
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, WgcMinimumBuild))
        {
            reason = "系统版本低于 Windows 10 1903";
            return false;
        }
        IntPtr factory = IntPtr.Zero;
        IntPtr statics = IntPtr.Zero;
        try
        {
            int hr = GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureSession", out factory);
            if (hr != 0)
            {
                reason = "无法取得 GraphicsCaptureSession 工厂 HRESULT=0x" + hr.ToString("X8");
                return false;
            }
            hr = QueryInterface(factory, GraphicsCaptureSessionStaticsId, out statics);
            if (hr != 0)
            {
                reason = "系统缺少 IsSupported 接口 HRESULT=0x" + hr.ToString("X8");
                return false;
            }
            IntPtr address = Marshal.ReadIntPtr(Marshal.ReadIntPtr(statics), 6 * IntPtr.Size);
            GetBooleanDelegate isSupported = Marshal.GetDelegateForFunctionPointer<GetBooleanDelegate>(address);
            int supported;
            hr = isSupported(statics, out supported);
            if (hr != 0)
            {
                reason = "IsSupported 调用失败 HRESULT=0x" + hr.ToString("X8");
                return false;
            }
            if (supported == 0)
            {
                reason = "系统报告 WGC 不支持（可能被组策略或图形驱动禁用）";
                return false;
            }
            return true;
        }
        catch (Exception error)
        {
            reason = "探测异常：" + error.Message;
            return false;
        }
        finally
        {
            ReleaseCom(statics);
            ReleaseCom(factory);
        }
    }

    /// <summary>WGC 采集一次；失败时返回 null 并给出中文原因（由调用方回退 GDI）。</summary>
    private ScreenCaptureResult? TryCaptureWithWgc(ScreenCaptureRequest request, out string failureReason)
    {
        failureReason = string.Empty;
        CaptureArea area;
        if (request.Target == CaptureTarget.Region)
        {
            area = request.Area;
        }
        else if (request.Target == CaptureTarget.PrimaryScreen)
        {
            area = WindowsScreenGeometry.GetPrimaryScreenArea();
        }
        else if (request.Target == CaptureTarget.ActiveWindow)
        {
            area = WindowsScreenGeometry.GetActiveWindowArea();
        }
        else
        {
            area = WindowsScreenGeometry.GetVirtualScreenArea();
        }
        if (!area.IsValid)
        {
            failureReason = "截图区域无效：宽高必须大于零";
            return null;
        }

        IntPtr item = IntPtr.Zero;
        IntPtr pool = IntPtr.Zero;
        IntPtr session = IntPtr.Zero;
        IntPtr frame = IntPtr.Zero;
        IntPtr surface = IntPtr.Zero;
        IntPtr access = IntPtr.Zero;
        IntPtr texture = IntPtr.Zero;
        IntPtr staging = IntPtr.Zero;
        bool mapped = false;
        WgcDevice? device = null;
        try
        {
            CaptureArea outputArea;
            int offsetX = 0;
            int offsetY = 0;
            item = this.CreateCaptureItem(request, area, out outputArea, out offsetX, out offsetY, out failureReason);
            if (item == IntPtr.Zero)
            {
                return null;
            }
            SizeInt32 itemSize;
            int hr = Fn<GetItemSizeDelegate>(item, 7)(item, out itemSize);
            if (hr != 0 || itemSize.Width <= 0 || itemSize.Height <= 0)
            {
                failureReason = "无法读取采集项尺寸 HRESULT=0x" + hr.ToString("X8");
                return null;
            }
            if (offsetX < 0 || offsetY < 0
                || offsetX + outputArea.Width > itemSize.Width
                || offsetY + outputArea.Height > itemSize.Height)
            {
                failureReason = "选区超出单个显示器范围";
                return null;
            }

            device = GetOrCreateDevice(out failureReason);
            if (device == null)
            {
                return null;
            }

            hr = CreateFramePool(device.WinRtDevice, itemSize, out pool);
            if (hr != 0 || pool == IntPtr.Zero)
            {
                failureReason = "创建帧池失败 HRESULT=0x" + hr.ToString("X8");
                return null;
            }
            hr = Fn<CreateCaptureSessionDelegate>(pool, 10)(pool, item, out session);
            if (hr != 0 || session == IntPtr.Zero)
            {
                failureReason = "创建采集会话失败 HRESULT=0x" + hr.ToString("X8");
                return null;
            }
            ApplySessionOptions(session, request.IncludeCursor);
            hr = Fn<StartCaptureDelegate>(session, 6)(session);
            if (hr != 0)
            {
                failureReason = "启动采集失败 HRESULT=0x" + hr.ToString("X8");
                return null;
            }

            if (!TryWaitForFrame(pool, out frame, out failureReason))
            {
                return null;
            }

            hr = Fn<GetSurfaceDelegate>(frame, 6)(frame, out surface);
            if (hr != 0 || surface == IntPtr.Zero)
            {
                failureReason = "取帧表面失败 HRESULT=0x" + hr.ToString("X8");
                return null;
            }
            hr = QueryInterface(surface, DxgiInterfaceAccessId, out access);
            if (hr != 0 || access == IntPtr.Zero)
            {
                failureReason = "帧表面不支持 DXGI 访问 HRESULT=0x" + hr.ToString("X8");
                return null;
            }
            Guid textureId = D3D11Texture2DId;
            hr = Fn<GetDxgiInterfaceDelegate>(access, 3)(access, ref textureId, out texture);
            if (hr != 0 || texture == IntPtr.Zero)
            {
                failureReason = "无法取得 D3D11 纹理 HRESULT=0x" + hr.ToString("X8");
                return null;
            }

            hr = CreateStagingTexture(device.Device, itemSize, out staging);
            if (hr != 0 || staging == IntPtr.Zero)
            {
                failureReason = "创建读回纹理失败 HRESULT=0x" + hr.ToString("X8");
                return null;
            }
            hr = Fn<CopyResourceDelegate>(device.Context, 47)(device.Context, staging, texture);
            if (hr != 0)
            {
                failureReason = "复制帧纹理失败 HRESULT=0x" + hr.ToString("X8");
                return null;
            }
            MappedSubresource mappedResource;
            hr = Fn<MapDelegate>(device.Context, 14)(device.Context, staging, 0, D3D11MapRead, 0, out mappedResource);
            if (hr != 0 || mappedResource.Data == IntPtr.Zero)
            {
                failureReason = "锁定读回纹理失败 HRESULT=0x" + hr.ToString("X8");
                return null;
            }
            mapped = true;
            byte[] pngBytes = EncodeRegionToPng(mappedResource.Data, mappedResource.RowPitch,
                offsetX, offsetY, outputArea.Width, outputArea.Height);
            ScreenCaptureResult result = ScreenCaptureResult.CreateSuccess(pngBytes,
                outputArea.Width, outputArea.Height, outputArea);
            result.BackendName = request.IncludeCursor ? "WGC（含指针）" : "WGC";
            return result;
        }
        catch (Exception error)
        {
            failureReason = "WGC 异常：" + error.Message;
            return null;
        }
        finally
        {
            if (mapped && staging != IntPtr.Zero && device != null)
            {
                Fn<UnmapDelegate>(device.Context, 15)(device.Context, staging, 0);
            }
            ReleaseCom(staging);
            ReleaseCom(texture);
            ReleaseCom(access);
            ReleaseCom(surface);
            ReleaseCom(frame);
            ReleaseCom(session);
            ReleaseCom(pool);
            ReleaseCom(item);
        }
    }

    /// <summary>
    /// 创建采集项：活动窗口用窗口项（可截取被遮挡内容），屏幕/区域用所在显示器项并按偏移裁剪。
    /// </summary>
    private IntPtr CreateCaptureItem(ScreenCaptureRequest request, CaptureArea area, out CaptureArea outputArea,
        out int offsetX, out int offsetY, out string failureReason)
    {
        outputArea = area;
        offsetX = 0;
        offsetY = 0;
        failureReason = string.Empty;
        if (request.Target == CaptureTarget.ActiveWindow)
        {
            IntPtr windowHandle = GetForegroundWindow();
            if (windowHandle == IntPtr.Zero)
            {
                failureReason = "没有可截取的活动窗口";
                return IntPtr.Zero;
            }
            if (IsIconic(windowHandle))
            {
                failureReason = "活动窗口已最小化";
                return IntPtr.Zero;
            }
            return CreateItemForTarget(windowHandle, false, out failureReason);
        }

        MonitorEntry monitor = ResolveMonitor(area);
        if (monitor.Handle == IntPtr.Zero)
        {
            failureReason = "无法定位选区所在显示器";
            return IntPtr.Zero;
        }
        offsetX = area.X - monitor.Area.X;
        offsetY = area.Y - monitor.Area.Y;
        if (offsetX < 0 || offsetY < 0
            || offsetX + area.Width > monitor.Area.Width
            || offsetY + area.Height > monitor.Area.Height)
        {
            failureReason = request.Target == CaptureTarget.FullScreen
                ? "全屏范围跨多个显示器（WGC 单次只能采集一个显示器）"
                : "选区跨越多个显示器";
            return IntPtr.Zero;
        }
        return CreateItemForTarget(monitor.Handle, true, out failureReason);
    }

    /// <summary>通过 IGraphicsCaptureItemInterop 创建窗口或显示器采集项。</summary>
    private static IntPtr CreateItemForTarget(IntPtr targetHandle, bool isMonitor, out string failureReason)
    {
        failureReason = string.Empty;
        IntPtr factory = IntPtr.Zero;
        IntPtr interop = IntPtr.Zero;
        try
        {
            int hr = GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", out factory);
            if (hr != 0)
            {
                failureReason = "无法取得采集项工厂 HRESULT=0x" + hr.ToString("X8");
                return IntPtr.Zero;
            }
            hr = QueryInterface(factory, GraphicsCaptureItemInteropId, out interop);
            if (hr != 0 || interop == IntPtr.Zero)
            {
                failureReason = "无法取得采集项互操作接口 HRESULT=0x" + hr.ToString("X8");
                return IntPtr.Zero;
            }
            Guid itemId = GraphicsCaptureItemId;
            IntPtr item = IntPtr.Zero;
            if (isMonitor)
            {
                hr = Fn<CreateItemForMonitorDelegate>(interop, 4)(interop, targetHandle, ref itemId, out item);
            }
            else
            {
                hr = Fn<CreateItemForWindowDelegate>(interop, 3)(interop, targetHandle, ref itemId, out item);
            }
            if (hr != 0 || item == IntPtr.Zero)
            {
                failureReason = (isMonitor ? "创建显示器采集项失败" : "创建窗口采集项失败")
                    + " HRESULT=0x" + hr.ToString("X8");
                return IntPtr.Zero;
            }
            return item;
        }
        finally
        {
            ReleaseCom(interop);
            ReleaseCom(factory);
        }
    }

    /// <summary>定位选区中心所在的显示器，返回句柄与物理像素区域。</summary>
    private static MonitorEntry ResolveMonitor(CaptureArea area)
    {
        NativePoint center = new NativePoint();
        center.X = area.X + area.Width / 2;
        center.Y = area.Y + area.Height / 2;
        IntPtr monitorHandle = MonitorFromPoint(center, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return new MonitorEntry();
        }
        MonitorInformation information = new MonitorInformation();
        information.Size = Marshal.SizeOf<MonitorInformation>();
        if (!GetMonitorInfo(monitorHandle, ref information))
        {
            return new MonitorEntry();
        }
        MonitorEntry entry = new MonitorEntry();
        entry.Handle = monitorHandle;
        entry.Area = new CaptureArea(information.Monitor.Left, information.Monitor.Top,
            information.Monitor.Right - information.Monitor.Left,
            information.Monitor.Bottom - information.Monitor.Top);
        return entry;
    }

    /// <summary>创建帧池：优先自由线程池，旧系统回退普通池。</summary>
    private static int CreateFramePool(IntPtr winRtDevice, SizeInt32 size, out IntPtr pool)
    {
        pool = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        IntPtr statics = IntPtr.Zero;
        try
        {
            int hr = GetActivationFactory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", out factory);
            if (hr != 0)
            {
                return hr;
            }
            Guid freeThreadedId = new Guid("589B103F-6BBC-5DF5-A991-02E28B3B66D5");
            hr = QueryInterface(factory, freeThreadedId, out statics);
            if (hr == 0 && statics != IntPtr.Zero)
            {
                hr = Fn<CreateFramePoolDelegate>(statics, 6)(statics, winRtDevice,
                    PixelFormatBgra8, FramePoolBufferCount, size, out pool);
                if (hr == 0 && pool != IntPtr.Zero)
                {
                    return 0;
                }
            }
            ReleaseCom(statics);
            statics = IntPtr.Zero;
            Guid staticsId = new Guid("7784056A-67AA-4D53-AE54-1088D5A8CA21");
            hr = QueryInterface(factory, staticsId, out statics);
            if (hr != 0 || statics == IntPtr.Zero)
            {
                return hr;
            }
            return Fn<CreateFramePoolDelegate>(statics, 6)(statics, winRtDevice,
                PixelFormatBgra8, FramePoolBufferCount, size, out pool);
        }
        finally
        {
            ReleaseCom(statics);
            ReleaseCom(factory);
        }
    }

    /// <summary>设置会话选项：是否包含鼠标指针、是否显示黄色采集边框。</summary>
    private static void ApplySessionOptions(IntPtr session, bool includeCursor)
    {
        IntPtr options = IntPtr.Zero;
        try
        {
            if (QueryInterface(session, GraphicsCaptureSession2Id, out options) == 0 && options != IntPtr.Zero)
            {
                Fn<SetBooleanDelegate>(options, 7)(options, includeCursor ? 1 : 0);
            }
        }
        finally
        {
            ReleaseCom(options);
        }
        options = IntPtr.Zero;
        try
        {
            if (QueryInterface(session, GraphicsCaptureSession3Id, out options) == 0 && options != IntPtr.Zero)
            {
                Fn<SetBooleanDelegate>(options, 7)(options, 0);
            }
        }
        finally
        {
            ReleaseCom(options);
        }
    }

    /// <summary>轮询首帧；WGC 会话启动后通常立即产生一帧。</summary>
    private static bool TryWaitForFrame(IntPtr pool, out IntPtr frame, out string failureReason)
    {
        failureReason = string.Empty;
        frame = IntPtr.Zero;
        TryGetNextFrameDelegate tryGetNextFrame = Fn<TryGetNextFrameDelegate>(pool, 7);
        int waited = 0;
        while (waited <= FirstFrameTimeoutMilliseconds)
        {
            int hr = tryGetNextFrame(pool, out frame);
            if (hr != 0)
            {
                failureReason = "取帧失败 HRESULT=0x" + hr.ToString("X8");
                return false;
            }
            if (frame != IntPtr.Zero)
            {
                return true;
            }
            Thread.Sleep(15);
            waited += 15;
        }
        failureReason = "等待首帧超时（" + FirstFrameTimeoutMilliseconds + " 毫秒）";
        return false;
    }

    /// <summary>创建可读回的暂存纹理。</summary>
    private static int CreateStagingTexture(IntPtr device, SizeInt32 size, out IntPtr texture)
    {
        Texture2DDescription description = new Texture2DDescription();
        description.Width = size.Width;
        description.Height = size.Height;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Format = PixelFormatBgra8;
        description.SampleCount = 1;
        description.SampleQuality = 0;
        description.Usage = D3D11UsageStaging;
        description.BindFlags = 0;
        description.CpuAccessFlags = D3D11CpuAccessRead;
        description.MiscFlags = 0;
        return Fn<CreateTexture2DDelegate>(device, 5)(device, ref description, IntPtr.Zero, out texture);
    }

    /// <summary>取得共享 D3D11 设备（懒创建，失败不缓存）。</summary>
    private static WgcDevice? GetOrCreateDevice(out string failureReason)
    {
        failureReason = string.Empty;
        lock (DeviceSyncRoot)
        {
            if (sharedDevice != null)
            {
                return sharedDevice;
            }
            IntPtr device = IntPtr.Zero;
            IntPtr context = IntPtr.Zero;
            IntPtr dxgiDevice = IntPtr.Zero;
            IntPtr winRtDevice = IntPtr.Zero;
            try
            {
                int featureLevel;
                int hr = D3D11CreateDevice(IntPtr.Zero, D3DDriverTypeHardware, IntPtr.Zero,
                    D3D11CreateDeviceBgraSupport, IntPtr.Zero, 0, D3D11SdkVersion,
                    out device, out featureLevel, out context);
                if (hr != 0 || device == IntPtr.Zero || context == IntPtr.Zero)
                {
                    failureReason = "创建 D3D11 硬件设备失败 HRESULT=0x" + hr.ToString("X8");
                    return null;
                }
                hr = QueryInterface(device, DxgiDeviceId, out dxgiDevice);
                if (hr != 0 || dxgiDevice == IntPtr.Zero)
                {
                    failureReason = "D3D11 设备不支持 DXGI HRESULT=0x" + hr.ToString("X8");
                    return null;
                }
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out winRtDevice);
                if (hr != 0 || winRtDevice == IntPtr.Zero)
                {
                    failureReason = "创建 WinRT 图形设备失败 HRESULT=0x" + hr.ToString("X8");
                    return null;
                }
                WgcDevice created = new WgcDevice();
                created.Device = device;
                created.Context = context;
                created.WinRtDevice = winRtDevice;
                sharedDevice = created;
                device = IntPtr.Zero;
                context = IntPtr.Zero;
                winRtDevice = IntPtr.Zero;
                return created;
            }
            finally
            {
                ReleaseCom(winRtDevice);
                ReleaseCom(dxgiDevice);
                ReleaseCom(context);
                ReleaseCom(device);
            }
        }
    }

    /// <summary>把读回纹理的指定区域编码为 PNG；WGC 输出为 BGRA，透明通道统一改为不透明。</summary>
    private static byte[] EncodeRegionToPng(IntPtr source, int rowPitch, int offsetX, int offsetY,
        int width, int height)
    {
        using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            BitmapData target = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    IntPtr rowSource = IntPtr.Add(source, (offsetY + y) * rowPitch + offsetX * 4);
                    Marshal.Copy(rowSource, row, 0, row.Length);
                    for (int index = 3; index < row.Length; index += 4)
                    {
                        row[index] = 255;
                    }
                    Marshal.Copy(row, 0, IntPtr.Add(target.Scan0, y * target.Stride), row.Length);
                }
            }
            finally
            {
                bitmap.UnlockBits(target);
            }
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }
    }

    /// <summary>读取 vtable 指定槽位的函数指针并转为委托。</summary>
    private static T Fn<T>(IntPtr instance, int index) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(instance);
        IntPtr address = Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    /// <summary>QueryInterface 封装。</summary>
    private static int QueryInterface(IntPtr instance, Guid iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (instance == IntPtr.Zero)
        {
            return unchecked((int)0x80004003);
        }
        QueryInterfaceDelegate query = Fn<QueryInterfaceDelegate>(instance, 0);
        Guid requested = iid;
        return query(instance, ref requested, out result);
    }

    /// <summary>安全释放 COM 引用。</summary>
    private static void ReleaseCom(IntPtr instance)
    {
        if (instance == IntPtr.Zero)
        {
            return;
        }
        try
        {
            Fn<ReleaseDelegate>(instance, 2)(instance);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>取得 WinRT 激活工厂。</summary>
    private static int GetActivationFactory(string runtimeClass, out IntPtr factory)
    {
        factory = IntPtr.Zero;
        IntPtr hstring = IntPtr.Zero;
        try
        {
            RoInitialize(1);
            int hr = WindowsCreateString(runtimeClass, runtimeClass.Length, out hstring);
            if (hr != 0)
            {
                return hr;
            }
            Guid iid = new Guid("00000035-0000-0000-C000-000000000046");
            return RoGetActivationFactory(hstring, ref iid, out factory);
        }
        finally
        {
            if (hstring != IntPtr.Zero)
            {
                WindowsDeleteString(hstring);
            }
        }
    }

    /// <summary>共享的 D3D11 设备与 WinRT 包装设备。</summary>
    private sealed class WgcDevice
    {
        public IntPtr Device;

        public IntPtr Context;

        public IntPtr WinRtDevice;
    }

    private struct MonitorEntry
    {
        public IntPtr Handle;

        public CaptureArea Area;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeInt32
    {
        public int Width;

        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInformation
    {
        public int Size;

        public NativeRect Monitor;

        public NativeRect Work;

        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDescription
    {
        public int Width;

        public int Height;

        public int MipLevels;

        public int ArraySize;

        public int Format;

        public int SampleCount;

        public int SampleQuality;

        public int Usage;

        public int BindFlags;

        public int CpuAccessFlags;

        public int MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedSubresource
    {
        public IntPtr Data;

        public int RowPitch;

        public int DepthPitch;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryInterfaceDelegate(IntPtr instance, ref Guid iid, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ReleaseDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetBooleanDelegate(IntPtr instance, out int value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetBooleanDelegate(IntPtr instance, int value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetItemSizeDelegate(IntPtr instance, out SizeInt32 size);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateItemForWindowDelegate(IntPtr instance, IntPtr windowHandle, ref Guid iid,
        out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateItemForMonitorDelegate(IntPtr instance, IntPtr monitorHandle, ref Guid iid,
        out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateFramePoolDelegate(IntPtr instance, IntPtr device, int pixelFormat,
        int numberOfBuffers, SizeInt32 size, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int TryGetNextFrameDelegate(IntPtr instance, out IntPtr frame);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateCaptureSessionDelegate(IntPtr instance, IntPtr item, out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int StartCaptureDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSurfaceDelegate(IntPtr instance, out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDxgiInterfaceDelegate(IntPtr instance, ref Guid iid, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateTexture2DDelegate(IntPtr instance, ref Texture2DDescription description,
        IntPtr initialData, out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MapDelegate(IntPtr instance, IntPtr resource, int subresource, int mapType,
        int mapFlags, out MappedSubresource mapped);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UnmapDelegate(IntPtr instance, IntPtr resource, int subresource);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CopyResourceDelegate(IntPtr instance, IntPtr destination, IntPtr source);

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr runtimeClass, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInformation information);
}
