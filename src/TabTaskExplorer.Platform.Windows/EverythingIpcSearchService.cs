using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 实现：通过 Everything 的消息接口（WM_COPYDATA）执行毫秒级全盘搜索。
/// 本服务不直接读磁盘，只消费 Everything 已建好的索引；Everything 未运行、
/// 查询超时或应答解析失败时，自动回退到内核内置遍历搜索。
/// </summary>
public class EverythingIpcSearchService : IFileSearchService
{
    /// <summary>应答窗口的消息处理回调类型，实例必须存成字段防止被垃圾回收。</summary>
    private delegate IntPtr WindowProcDelegate(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>WM_COPYDATA 消息编号。</summary>
    private const uint WM_COPYDATA = 0x004A;

    /// <summary>SMTO_BLOCK：发送期间阻塞调用线程。</summary>
    private const uint SMTO_BLOCK = 0x0001;

    /// <summary>SMTO_ABORTIFHUNG：目标窗口挂起时立即放弃。</summary>
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>Everything 查询与应答共用的 COPYDATA 命令码（EVERYTHING_IPC_COPYDATAQUERYW）。</summary>
    private const int COPYDATA_COMMAND_QUERY = 2;

    /// <summary>Everything 主窗口的类名，用于探测进程是否在运行。</summary>
    private const string EVERYTHING_WINDOW_CLASS = "EVERYTHING_TASKBAR_NOTIFICATION";

    /// <summary>应答窗口使用的类名，仅本进程内部可见。</summary>
    private const string REPLY_WINDOW_CLASS = "TabTaskExplorerEverythingReply";

    /// <summary>WS_POPUP：隐藏的仅消息窗口使用的样式。</summary>
    private const uint WINDOW_STYLE_POPUP = 0x80000000;

    /// <summary>应答包 LISTW 的头部字节数：numfolders、numfiles、numitems、totalitems 与对齐填充。</summary>
    private const int LIST_HEADER_BYTES = 20;

    /// <summary>应答条目 ITEMW 的字节数：flags、两个字符串偏移、64 位大小、修改时间。</summary>
    private const int ITEM_BYTES = 28;

    /// <summary>ITEMW flags 的文件夹标记位（EVERYTHING_IPC_FOLDER）。</summary>
    private const int ITEM_FLAG_FOLDER = 0x00000001;

    /// <summary>Everything 不可用或查询失败时使用的内置遍历搜索。</summary>
    private readonly FileSystemWalkSearchService fallback;

    /// <summary>保护应答缓冲与消息窗口创建过程的锁。</summary>
    private readonly object syncRoot = new object();

    /// <summary>应答到达事件：Everything 异步回发结果时由消息泵线程置位。</summary>
    private readonly AutoResetEvent replyEvent = new AutoResetEvent(false);

    /// <summary>消息处理回调实例，字段持有防止被垃圾回收。</summary>
    private WindowProcDelegate windowProcDelegate;

    /// <summary>隐藏的应答窗口句柄。</summary>
    private IntPtr messageWindow;

    /// <summary>消息泵线程就绪事件。</summary>
    private ManualResetEvent pumpReadyEvent;

    /// <summary>最近一次收到的应答包字节内容。</summary>
    private byte[] replyBuffer = new byte[0];

    /// <summary>
    /// 创建 Everything 消息搜索服务。
    /// </summary>
    /// <param name="fallback">Everything 不可用或查询失败时使用的内置遍历搜索。</param>
    public EverythingIpcSearchService(FileSystemWalkSearchService fallback)
    {
        this.fallback = fallback;
    }

    /// <summary>Everything 是否正在运行（按窗口类名探测）。</summary>
    public bool IsAvailable
    {
        get { return FindWindow(EVERYTHING_WINDOW_CLASS, null) != IntPtr.Zero; }
    }

    /// <summary>搜索来源的可读名称。</summary>
    public string DisplayName
    {
        get { return "Everything 消息搜索"; }
    }

    /// <summary>
    /// 按条件执行全盘搜索，查询与等待应答放在后台线程执行。
    /// </summary>
    /// <param name="options">搜索条件。</param>
    /// <returns>命中的结果列表。</returns>
    public Task<IReadOnlyList<FileSearchResult>> SearchAsync(FileSearchOptions options)
    {
        return Task.Run(delegate { return this.SearchSync(options); });
    }

    /// <summary>
    /// 同步执行搜索：探测 Everything、发送查询、等待应答并解析结果。
    /// 任一环节失败都回退到内置遍历搜索。
    /// </summary>
    /// <param name="options">搜索条件。</param>
    /// <returns>命中的结果列表。</returns>
    private IReadOnlyList<FileSearchResult> SearchSync(FileSearchOptions options)
    {
        IntPtr everythingWindow = FindWindow(EVERYTHING_WINDOW_CLASS, null);
        if (everythingWindow == IntPtr.Zero)
        {
            // Everything 未运行，直接回退到内置遍历搜索
            return this.fallback.Search(options);
        }

        try
        {
            this.EnsureMessageWindow();
            if (this.messageWindow == IntPtr.Zero)
            {
                return this.fallback.Search(options);
            }

            // 查询体手工写内存：max_results 在偏移 0、offset 在偏移 4、应答窗口句柄在偏移 8、
            // 搜索字符串从 8 + 指针宽度 处开始（UTF-16 编码并以两个空字符结尾）
            byte[] searchBytes = Encoding.Unicode.GetBytes(options.Query + "\0\0");
            int queryBytes = 8 + IntPtr.Size + searchBytes.Length;
            IntPtr queryPointer = Marshal.AllocHGlobal(queryBytes);
            try
            {
                Marshal.WriteInt32(queryPointer, 0, options.MaxResults);
                Marshal.WriteInt32(queryPointer, 4, 0);
                Marshal.WriteIntPtr(queryPointer, 8, this.messageWindow);
                Marshal.Copy(searchBytes, 0, queryPointer + 8 + IntPtr.Size, searchBytes.Length);

                COPYDATASTRUCT copyData = new COPYDATASTRUCT();
                copyData.dwData = (IntPtr)COPYDATA_COMMAND_QUERY;
                copyData.cbData = queryBytes;
                copyData.lpData = queryPointer;

                this.replyEvent.Reset();
                UIntPtr sendResult;
                IntPtr sent = SendMessageTimeout(everythingWindow, WM_COPYDATA, this.messageWindow, ref copyData,
                    SMTO_BLOCK | SMTO_ABORTIFHUNG, 2000, out sendResult);
                if (sent == IntPtr.Zero || !this.replyEvent.WaitOne(5000))
                {
                    // 发送失败或 Everything 没有按时回发应答，回退到内置遍历搜索
                    return this.fallback.Search(options);
                }

                byte[] replyBytes;
                lock (this.syncRoot)
                {
                    replyBytes = this.replyBuffer;
                }

                return this.ParseReply(replyBytes, options.MaxResults);
            }
            finally
            {
                Marshal.FreeHGlobal(queryPointer);
            }
        }
        catch (Exception)
        {
            // 任何异常都回退到内置遍历搜索，保证搜索功能始终可用
            return this.fallback.Search(options);
        }
    }

    /// <summary>确保消息泵线程与应答窗口已经创建，只创建一次。</summary>
    private void EnsureMessageWindow()
    {
        lock (this.syncRoot)
        {
            if (this.messageWindow != IntPtr.Zero)
            {
                return;
            }

            this.pumpReadyEvent = new ManualResetEvent(false);
            this.windowProcDelegate = this.WindowProc;
            Thread pumpThread = new Thread(this.MessagePumpMain);
            pumpThread.IsBackground = true;
            pumpThread.SetApartmentState(ApartmentState.STA);
            pumpThread.Start();

            if (!this.pumpReadyEvent.WaitOne(3000))
            {
                // 消息泵线程没有按时就绪，视为创建失败
                return;
            }
        }
    }

    /// <summary>消息泵线程主体：注册窗口类、创建隐藏窗口并循环分发消息。</summary>
    private void MessagePumpMain()
    {
        WNDCLASSW windowClass = new WNDCLASSW();
        windowClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(this.windowProcDelegate);
        windowClass.hInstance = GetModuleHandle(null);
        windowClass.lpszClassName = REPLY_WINDOW_CLASS;

        ushort atom = RegisterClass(ref windowClass);
        if (atom == 0)
        {
            // 窗口类注册失败，通知等待方后结束线程
            this.pumpReadyEvent.Set();
            return;
        }

        this.messageWindow = CreateWindowEx(0, REPLY_WINDOW_CLASS, REPLY_WINDOW_CLASS, WINDOW_STYLE_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, windowClass.hInstance, IntPtr.Zero);
        this.pumpReadyEvent.Set();

        if (this.messageWindow == IntPtr.Zero)
        {
            return;
        }

        MSG message = new MSG();
        while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    /// <summary>应答窗口的消息处理：接收 Everything 回发的 WM_COPYDATA 应答包并置位事件。</summary>
    private IntPtr WindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_COPYDATA)
        {
            COPYDATASTRUCT copyData = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));
            if (copyData.dwData == (IntPtr)COPYDATA_COMMAND_QUERY && copyData.cbData > 0 && copyData.lpData != IntPtr.Zero)
            {
                // SendMessage 是同步调用，这里必须趁调用线程被阻塞时把数据复制出来
                byte[] buffer = new byte[copyData.cbData];
                Marshal.Copy(copyData.lpData, buffer, 0, copyData.cbData);
                lock (this.syncRoot)
                {
                    this.replyBuffer = buffer;
                }
                this.replyEvent.Set();
                return (IntPtr)1;
            }

            return DefWindowProc(windowHandle, message, wParam, lParam);
        }

        if (message == 0x0002)
        {
            // WM_DESTROY：退出消息循环
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(windowHandle, message, wParam, lParam);
    }

    /// <summary>
    /// 解析 Everything 回发的结果包（EVERYTHING_IPC_LISTW）。
    /// 头部 20 字节，numitems 在偏移 8；条目 28 字节一个，字符串偏移相对结果包起始。
    /// </summary>
    /// <param name="replyBytes">应答包字节内容。</param>
    /// <param name="maxResults">结果数量上限。</param>
    /// <returns>解析出的结果列表，解析失败时返回空列表。</returns>
    private List<FileSearchResult> ParseReply(byte[] replyBytes, int maxResults)
    {
        List<FileSearchResult> results = new List<FileSearchResult>();
        if (replyBytes == null || replyBytes.Length < LIST_HEADER_BYTES)
        {
            return results;
        }

        int itemCount = BitConverter.ToInt32(replyBytes, 8);
        int itemOffset = LIST_HEADER_BYTES;
        for (int index = 0; index < itemCount && results.Count < maxResults; index++)
        {
            if (itemOffset + ITEM_BYTES > replyBytes.Length)
            {
                // 数据不完整，停止解析已收到部分
                break;
            }

            int flags = BitConverter.ToInt32(replyBytes, itemOffset);
            int filenameOffset = BitConverter.ToInt32(replyBytes, itemOffset + 4);
            int pathOffset = BitConverter.ToInt32(replyBytes, itemOffset + 8);
            long lengthBytes = BitConverter.ToInt64(replyBytes, itemOffset + 12);
            long fileTimeValue = BitConverter.ToInt64(replyBytes, itemOffset + 20);

            string filename = this.ReadUtf16String(replyBytes, filenameOffset);
            string directory = this.ReadUtf16String(replyBytes, pathOffset);
            if (filename.Length == 0 || directory.Length == 0)
            {
                // 字符串越界或为空，跳过这条损坏的条目
                itemOffset += ITEM_BYTES;
                continue;
            }

            FileSearchResult result = new FileSearchResult();
            result.Name = filename;
            result.Directory = directory;
            result.IsDirectory = (flags & ITEM_FLAG_FOLDER) != 0;
            string separator = directory.EndsWith("\\", StringComparison.Ordinal) ? string.Empty : "\\";
            result.FullPath = directory + separator + filename;
            result.LengthBytes = lengthBytes;
            try
            {
                result.ModifiedTimeUtc = DateTime.FromFileTimeUtc(fileTimeValue);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 时间值非法时保留默认值即可
            }

            results.Add(result);
            itemOffset += ITEM_BYTES;
        }

        return results;
    }

    /// <summary>从应答包缓冲中读取一个以空字符结尾的 UTF-16 字符串，越界时返回空字符串。</summary>
    private string ReadUtf16String(byte[] replyBytes, int byteOffset)
    {
        if (byteOffset < 0 || byteOffset + 2 > replyBytes.Length)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        int offset = byteOffset;
        while (offset + 2 <= replyBytes.Length)
        {
            char character = (char)(replyBytes[offset] | (replyBytes[offset + 1] << 8));
            if (character == '\0')
            {
                break;
            }

            builder.Append(character);
            offset += 2;
        }

        return builder.ToString();
    }

    /// <summary>Windows 消息结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr WindowHandle;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public POINT Point;
    }

    /// <summary>屏幕坐标点结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>窗口类注册结构（W 版本）。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint Style;
        public IntPtr lpfnWndProc;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr hInstance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
    }

    /// <summary>WM_COPYDATA 消息携带的数据结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASSW windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parentWindow, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG message, IntPtr windowHandle, uint minMessage, uint maxMessage);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr windowHandle, uint message, IntPtr wParam,
        ref COPYDATASTRUCT copyData, uint flags, uint timeout, out UIntPtr result);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")]
    private static extern void PostQuitMessage(int exitCode);
}
