using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 原生外壳右键菜单弹窗：先放本程序命令，再把 IContextMenu 生成的系统静态动词与全部第三方扩展
/// 插入同一张原生菜单，用 TrackPopupMenuEx 显示，并转发 WM_INITMENUPOPUP / WM_DRAWITEM /
/// WM_MEASUREITEM / WM_MENUCHAR 给 IContextMenu2 / IContextMenu3。
/// 多选时把同一目录下的全部选中项一起交给外壳，扩展的批量命令据此一次处理全部对象。
/// 构建在专用 STA 线程里进行，超时或出错立即降级返回，绝不阻塞界面线程。
/// </summary>
internal static class WindowsShellMenuPopup
{
    /// <summary>本程序命令编号起点（十六进制 0x8000），避开外壳菜单 1 到 0x7FFF 的编号段。</summary>
    private const int OwnCommandBase = 0x8000;

    /// <summary>外壳菜单命令编号起点；0 保留给“用户取消”。</summary>
    private const int ShellCommandFirst = 1;

    /// <summary>外壳菜单命令编号上界。</summary>
    private const int ShellCommandLast = 0x7FFF;

    /// <summary>外壳菜单普通标志。</summary>
    private const uint ContextMenuNormal = 0x00000000;

    /// <summary>外壳菜单浏览模式标志（空白处菜单用）。</summary>
    private const uint ContextMenuExplore = 0x00000004;

    /// <summary>TrackPopupMenuEx：返回用户选择的命令编号而不是发送 WM_COMMAND。</summary>
    private const uint TrackPopupReturnCommand = 0x0100;

    /// <summary>TrackPopupMenuEx：允许在菜单上用右键选择。</summary>
    private const uint TrackPopupRightButton = 0x0002;

    /// <summary>菜单项：普通文本项。</summary>
    private const uint MenuString = 0x00000000;

    /// <summary>菜单项：分隔线。</summary>
    private const uint MenuSeparator = 0x00000800;

    /// <summary>菜单项：灰化不可用。</summary>
    private const uint MenuGrayed = 0x00000001;

    /// <summary>菜单项：禁用。</summary>
    private const uint MenuDisabled = 0x00000002;

    /// <summary>窗口消息：绘制所有者绘制菜单项。</summary>
    private const uint WmDrawItem = 0x002B;

    /// <summary>窗口消息：测量所有者绘制菜单项。</summary>
    private const uint WmMeasureItem = 0x002C;

    /// <summary>窗口消息：弹出子菜单前通知所有者填充动态项。</summary>
    private const uint WmInitMenuPopup = 0x0117;

    /// <summary>窗口消息：菜单字符查找。</summary>
    private const uint WmMenuChar = 0x0120;

    /// <summary>所有者窗口类名：用于接收菜单消息并转发给外壳扩展。</summary>
    private const string OwnerClassName = "TabTaskExplorerShellMenuOwner";

    /// <summary>当前正在显示菜单的会话，供窗口过程转发消息时取用（同一时刻只允许一个菜单）。</summary>
    private static MenuSession? activeSession;

    /// <summary>保护 activeSession 的锁。</summary>
    private static readonly object SessionLock = new object();

    /// <summary>窗口过程委托实例，注册窗口类后必须长期持有，避免被回收。</summary>
    private static WindowProcDelegate? ownerWindowProc;

    /// <summary>窗口类是否已注册。</summary>
    private static bool ownerClassRegistered;

    /// <summary>构建外壳菜单的等待上限（毫秒）：TortoiseGit / TortoiseSVN 等重型扩展首次初始化可能超过 1 秒，
    /// 上限放宽到 2.5 秒；超时即降级回自有菜单，等待期间不占用界面线程。</summary>
    private const int BuildTimeoutMilliseconds = 2500;

    /// <summary>弹出原生外壳右键菜单；构建超时或失败时返回未弹出，由调用方退回自有菜单。</summary>
    public static async Task<NativeContextMenuResult> ShowAsync(NativeContextMenuRequest request)
    {
        NativeContextMenuResult immediate = new NativeContextMenuResult();
        immediate.Supported = true;
        immediate.MenuShown = false;
        if (request == null || (request.Paths.Count == 0 && !request.IsBackground))
        {
            immediate.Message = "原生右键菜单请求为空（没有目标路径）";
            return immediate;
        }
        MenuSession session = new MenuSession(request);
        Thread worker = new Thread(session.Run);
        worker.SetApartmentState(ApartmentState.STA);
        worker.IsBackground = true;
        worker.Name = "TabTaskExplorerShellMenu";
        worker.Start();
        Task ready = session.BuiltTask;
        Task completed = await Task.WhenAny(ready, Task.Delay(BuildTimeoutMilliseconds));
        if (completed != ready)
        {
            // 超时降级：让工作线程在显示菜单前自行放弃，避免再弹出一张菜单
            session.RequestCancel();
            NativeContextMenuResult degraded = new NativeContextMenuResult();
            degraded.Supported = true;
            degraded.MenuShown = false;
            degraded.Message = "外壳菜单构建超时（" + BuildTimeoutMilliseconds + "毫秒内未就绪），已退回本程序菜单";
            return degraded;
        }
        if (session.BuildError.Length > 0)
        {
            NativeContextMenuResult failed = new NativeContextMenuResult();
            failed.Supported = true;
            failed.MenuShown = false;
            failed.Message = "外壳菜单构建失败：" + session.BuildError;
            return failed;
        }
        return await session.Completion;
    }

    /// <summary>取本程序命令显示用的菜单标识：条目自带编号优先，否则按序号分配。</summary>
    private static uint OwnMenuId(NativeContextMenuCommand command, int index)
    {
        if (command.CommandId >= OwnCommandBase)
        {
            return (uint)command.CommandId;
        }
        return (uint)(OwnCommandBase + index);
    }

    /// <summary>注册所有者窗口类（只注册一次），供 CreateWindowEx 创建隐藏窗口。</summary>
    private static bool EnsureOwnerClassRegistered()
    {
        lock (SessionLock)
        {
            if (ownerClassRegistered)
            {
                return true;
            }
            ownerWindowProc = new WindowProcDelegate(OwnerWindowProc);
            WNDCLASSEX windowClass = new WNDCLASSEX();
            windowClass.cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>();
            windowClass.style = 0;
            windowClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ownerWindowProc);
            windowClass.cbClsExtra = 0;
            windowClass.cbWndExtra = 0;
            windowClass.hInstance = GetModuleHandle(null);
            windowClass.hIcon = IntPtr.Zero;
            windowClass.hCursor = IntPtr.Zero;
            windowClass.hbrBackground = IntPtr.Zero;
            windowClass.lpszMenuName = null;
            windowClass.lpszClassName = OwnerClassName;
            windowClass.hIconSm = IntPtr.Zero;
            ushort atom = RegisterClassEx(ref windowClass);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                // 1410 表示类已注册，其他错误视为注册失败
                return false;
            }
            ownerClassRegistered = true;
            return true;
        }
    }

    /// <summary>一次弹窗会话：持有 COM 对象、菜单句柄与所有者窗口，负责构建、显示、唤起与清理。</summary>
    private sealed class MenuSession
    {
        private readonly NativeContextMenuRequest request;

        private readonly TaskCompletionSource<bool> builtSignal = new TaskCompletionSource<bool>();

        private readonly TaskCompletionSource<NativeContextMenuResult> completion =
            new TaskCompletionSource<NativeContextMenuResult>();

        /// <summary>构建阶段失败原因；为空表示构建成功。</summary>
        private volatile string buildError = string.Empty;

        private volatile bool cancelled;

        private IntPtr window;

        private IntPtr menu;

        private IContextMenu? contextMenu;

        private IContextMenu2? contextMenu2;

        private IContextMenu3? contextMenu3;

        private IShellFolder? parentFolderObject;

        private readonly List<IntPtr> allocatedPidls = new List<IntPtr>();

        private readonly List<uint> ownMenuIds = new List<uint>();

        private readonly List<NativeContextMenuCommand> ownCommands = new List<NativeContextMenuCommand>();

        public Task<NativeContextMenuResult> Completion
        {
            get { return this.completion.Task; }
        }

        /// <summary>构建阶段是否已结束（成功或失败），用于在不阻塞界面线程的前提下等待。</summary>
        public Task BuiltTask
        {
            get { return this.builtSignal.Task; }
        }

        /// <summary>构建阶段失败原因；为空表示没有失败。</summary>
        public string BuildError
        {
            get { return this.buildError; }
        }

        public MenuSession(NativeContextMenuRequest request)
        {
            this.request = request;
        }

        /// <summary>请求取消：工作线程在显示菜单前检查该标记并退出。</summary>
        public void RequestCancel()
        {
            this.cancelled = true;
        }

        /// <summary>工作线程主体：初始化 COM、构建菜单、显示菜单、唤起选择并清理。</summary>
        public void Run()
        {
            NativeContextMenuResult result = new NativeContextMenuResult();
            result.Supported = true;
            int comResult = CoInitializeEx(IntPtr.Zero, 2);
            bool comInitialized = comResult >= 0;
            try
            {
                try
                {
                    this.BuildMenu();
                }
                catch (Exception buildFailure)
                {
                    // 构建阶段失败：立刻唤醒等待方并带回原因，界面层据此退回自有菜单
                    this.buildError = buildFailure.Message;
                    this.builtSignal.TrySetResult(false);
                    result.MenuShown = false;
                    result.Message = "原生外壳菜单构建失败：" + buildFailure.Message;
                    this.completion.TrySetResult(result);
                    return;
                }
                lock (SessionLock)
                {
                    activeSession = this;
                }
                this.builtSignal.TrySetResult(true);
                if (this.cancelled)
                {
                    result.Message = "界面已判定构建超时并降级，本次原生菜单未显示";
                    this.completion.TrySetResult(result);
                    return;
                }
                uint chosen = TrackPopupMenuEx(this.menu, TrackPopupReturnCommand | TrackPopupRightButton,
                    this.request.ScreenX, this.request.ScreenY, this.window, IntPtr.Zero);
                result.MenuShown = true;
                if (chosen == 0)
                {
                    result.Message = "用户在原生菜单外取消，未执行任何命令";
                    this.completion.TrySetResult(result);
                    return;
                }
                int ownIndex = this.ownMenuIds.IndexOf(chosen);
                if (ownIndex >= 0)
                {
                    NativeContextMenuCommand command = this.ownCommands[ownIndex];
                    result.OwnCommandId = command.CommandId;
                    result.Message = "用户选择了本程序命令：" + command.DisplayName;
                    this.completion.TrySetResult(result);
                    return;
                }
                if (chosen < (uint)ShellCommandFirst || chosen > (uint)ShellCommandLast)
                {
                    result.Message = "原生菜单返回了未知命令编号：" + chosen;
                    this.completion.TrySetResult(result);
                    return;
                }
                int shellOffset = (int)chosen - ShellCommandFirst;
                bool invoked = this.InvokeShellCommand(shellOffset);
                result.ShellCommandInvoked = invoked;
                result.Message = invoked
                    ? "已把选择交给系统外壳命令（含第三方扩展），命令编号=" + chosen
                    : "系统外壳命令唤起失败，命令编号=" + chosen;
                this.completion.TrySetResult(result);
            }
            catch (Exception error)
            {
                result.MenuShown = false;
                result.Message = "原生外壳菜单构建或显示失败：" + error.Message;
                this.completion.TrySetResult(result);
            }
            finally
            {
                lock (SessionLock)
                {
                    if (activeSession == this)
                    {
                        activeSession = null;
                    }
                }
                this.Cleanup();
                if (comInitialized)
                {
                    CoUninitialize();
                }
            }
        }

        /// <summary>构建菜单：创建所有者窗口、取 IContextMenu、放入本程序命令并插入外壳菜单。</summary>
        private void BuildMenu()
        {
            if (!EnsureOwnerClassRegistered())
            {
                throw new InvalidOperationException("注册原生菜单所有者窗口类失败");
            }
            IntPtr instance = GetModuleHandle(null);
            this.window = CreateWindowEx(0, OwnerClassName, OwnerClassName, 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (this.window == IntPtr.Zero)
            {
                throw new InvalidOperationException("创建原生菜单所有者窗口失败，错误码=" + Marshal.GetLastWin32Error());
            }
            Guid shellFolderIid = typeof(IShellFolder).GUID;
            if (this.request.IsBackground)
            {
                this.CreateBackgroundContextMenu(this.window, shellFolderIid);
            }
            else
            {
                this.CreateItemsContextMenu(this.window, shellFolderIid);
            }
            if (this.contextMenu == null)
            {
                throw new InvalidOperationException("系统外壳没有返回右键菜单接口");
            }
            this.menu = CreatePopupMenu();
            if (this.menu == IntPtr.Zero)
            {
                throw new InvalidOperationException("创建弹出菜单失败，错误码=" + Marshal.GetLastWin32Error());
            }
            this.AppendOwnCommands();
            uint flags = this.request.IsBackground ? (ContextMenuNormal | ContextMenuExplore) : ContextMenuNormal;
            uint insertIndex = (uint)(this.ownMenuIds.Count + (this.ownMenuIds.Count > 0 ? 1 : 0));
            int queryResult = this.contextMenu.QueryContextMenu(this.menu, insertIndex,
                (uint)ShellCommandFirst, (uint)ShellCommandLast, flags);
            if (queryResult < 0)
            {
                throw new InvalidOperationException("系统外壳构建右键菜单失败，错误码=" + queryResult);
            }
        }

        /// <summary>条目菜单：同一目录下的全部选中项一起取 IContextMenu，批量扩展命令据此处理完整选择。</summary>
        private void CreateItemsContextMenu(IntPtr ownerWindow, Guid shellFolderIid)
        {
            List<string> orderedPaths = new List<string>();
            string? commonDirectory = null;
            foreach (string candidate in this.request.Paths)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }
                string directory;
                try
                {
                    directory = Path.GetDirectoryName(candidate) ?? string.Empty;
                }
                catch (Exception)
                {
                    continue;
                }
                if (commonDirectory == null)
                {
                    commonDirectory = directory;
                }
                if (string.Equals(commonDirectory, directory, StringComparison.OrdinalIgnoreCase))
                {
                    orderedPaths.Add(candidate);
                }
            }
            if (orderedPaths.Count == 0)
            {
                throw new InvalidOperationException("没有可解析的右键目标路径");
            }
            List<IntPtr> childPidls = new List<IntPtr>();
            IntPtr parentPointer = IntPtr.Zero;
            for (int index = 0; index < orderedPaths.Count; index++)
            {
                IntPtr pidl = IntPtr.Zero;
                if (SHParseDisplayName(orderedPaths[index], IntPtr.Zero, out pidl, 0, out uint _) != 0 || pidl == IntPtr.Zero)
                {
                    continue;
                }
                this.allocatedPidls.Add(pidl);
                IntPtr itemParent = IntPtr.Zero;
                IntPtr childPidl = IntPtr.Zero;
                int bindResult = SHBindToParent(pidl, ref shellFolderIid, out itemParent, out childPidl);
                if (bindResult != 0 || itemParent == IntPtr.Zero || childPidl == IntPtr.Zero)
                {
                    continue;
                }
                if (parentPointer == IntPtr.Zero)
                {
                    parentPointer = itemParent;
                }
                else
                {
                    Marshal.Release(itemParent);
                }
                childPidls.Add(childPidl);
            }
            if (childPidls.Count == 0 || parentPointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("右键目标路径全部解析失败");
            }
            this.parentFolderObject = GetInterface<IShellFolder>(parentPointer);
            if (this.parentFolderObject == null)
            {
                throw new InvalidOperationException("取父目录外壳接口失败");
            }
            Guid contextMenuIid = typeof(IContextMenu).GUID;
            int objectResult = this.parentFolderObject.GetUIObjectOf(ownerWindow, (uint)childPidls.Count,
                childPidls.ToArray(), ref contextMenuIid, 0, out IntPtr contextPointer);
            if (objectResult != 0 || contextPointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("取条目右键菜单接口失败，错误码=" + objectResult);
            }
            this.AttachContextMenu(contextPointer);
        }

        /// <summary>空白处菜单：取目录的 IShellFolder，再用 CreateViewObject 取文件夹背景右键菜单。</summary>
        private void CreateBackgroundContextMenu(IntPtr ownerWindow, Guid shellFolderIid)
        {
            string directory = this.request.Paths.Count > 0 ? this.request.Paths[0] : string.Empty;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                throw new InvalidOperationException("空白处菜单的目标目录不存在：" + directory);
            }
            IntPtr directoryPidl = IntPtr.Zero;
            if (SHParseDisplayName(directory, IntPtr.Zero, out directoryPidl, 0, out uint _) != 0 || directoryPidl == IntPtr.Zero)
            {
                throw new InvalidOperationException("解析目录标识失败：" + directory);
            }
            this.allocatedPidls.Add(directoryPidl);
            IntPtr parentPointer = IntPtr.Zero;
            IntPtr childPidl = IntPtr.Zero;
            int bindResult = SHBindToParent(directoryPidl, ref shellFolderIid, out parentPointer, out childPidl);
            if (bindResult != 0 || parentPointer == IntPtr.Zero || childPidl == IntPtr.Zero)
            {
                throw new InvalidOperationException("取目录父外壳接口失败，错误码=" + bindResult);
            }
            this.parentFolderObject = GetInterface<IShellFolder>(parentPointer);
            if (this.parentFolderObject == null)
            {
                throw new InvalidOperationException("父外壳接口为空");
            }
            IntPtr folderPointer = IntPtr.Zero;
            this.parentFolderObject.BindToObject(childPidl, IntPtr.Zero, ref shellFolderIid, out folderPointer);
            if (folderPointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("取目录外壳接口失败");
            }
            IShellFolder? folder = GetInterface<IShellFolder>(folderPointer);
            if (folder == null)
            {
                throw new InvalidOperationException("目录外壳接口为空");
            }
            Guid contextMenuIid = typeof(IContextMenu).GUID;
            IntPtr contextPointer = IntPtr.Zero;
            folder.CreateViewObject(ownerWindow, ref contextMenuIid, out contextPointer);
            ReleaseComObject(folder);
            if (contextPointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("取目录背景右键菜单接口失败");
            }
            this.AttachContextMenu(contextPointer);
        }

        /// <summary>把接口指针绑定成 IContextMenu，并尝试取得 IContextMenu2 / IContextMenu3 用于消息转发。</summary>
        private void AttachContextMenu(IntPtr contextPointer)
        {
            this.contextMenu = GetInterface<IContextMenu>(contextPointer);
            if (this.contextMenu == null)
            {
                throw new InvalidOperationException("系统外壳返回的对象不支持 IContextMenu");
            }
            this.contextMenu2 = QueryInterface<IContextMenu2>(contextPointer);
            this.contextMenu3 = QueryInterface<IContextMenu3>(contextPointer);
            Marshal.Release(contextPointer);
        }

        /// <summary>把本程序命令按传入顺序放进菜单顶部，末尾补一条分隔线把两者分开。</summary>
        private void AppendOwnCommands()
        {
            foreach (NativeContextMenuCommand command in this.request.Commands)
            {
                if (command.IsSeparator)
                {
                    AppendMenuW(this.menu, MenuSeparator, UIntPtr.Zero, null);
                    continue;
                }
                uint menuId = OwnMenuId(command, this.ownMenuIds.Count);
                uint flags = MenuString;
                if (!command.IsEnabled)
                {
                    flags |= MenuGrayed | MenuDisabled;
                }
                AppendMenuW(this.menu, flags, menuId, command.DisplayName);
                this.ownMenuIds.Add(menuId);
                this.ownCommands.Add(command);
                if (command.IsDefault)
                {
                    SetMenuDefaultItem(this.menu, menuId, 0);
                }
            }
            if (this.ownMenuIds.Count > 0)
            {
                AppendMenuW(this.menu, MenuSeparator, UIntPtr.Zero, null);
            }
        }

        /// <summary>按命令偏移唤起外壳命令（静态动词或第三方扩展）。</summary>
        private bool InvokeShellCommand(int shellOffset)
        {
            if (this.contextMenu == null)
            {
                return false;
            }
            CMINVOKECOMMANDINFOEX info = new CMINVOKECOMMANDINFOEX();
            info.cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>();
            info.hwnd = GetForegroundWindow();
            info.nShow = 1;
            GetCursorPos(out POINT point);
            info.ptInvokeX = point.X;
            info.ptInvokeY = point.Y;
            info.lpVerb = new IntPtr(shellOffset);
            try
            {
                return this.contextMenu.InvokeCommand(ref info) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>释放菜单、窗口与 COM 对象；单项失败不影响其余释放。</summary>
        private void Cleanup()
        {
            if (this.menu != IntPtr.Zero)
            {
                DestroyMenu(this.menu);
                this.menu = IntPtr.Zero;
            }
            if (this.window != IntPtr.Zero)
            {
                DestroyWindow(this.window);
                this.window = IntPtr.Zero;
            }
            ReleaseComObject(this.contextMenu3);
            ReleaseComObject(this.contextMenu2);
            ReleaseComObject(this.contextMenu);
            ReleaseComObject(this.parentFolderObject);
            this.contextMenu3 = null;
            this.contextMenu2 = null;
            this.contextMenu = null;
            this.parentFolderObject = null;
            foreach (IntPtr pidl in this.allocatedPidls)
            {
                if (pidl != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            this.allocatedPidls.Clear();
        }

        /// <summary>转发菜单消息给 IContextMenu3 / IContextMenu2，让动态动词、图标与所有者绘制项正常工作。</summary>
        public IntPtr HandleMenuMessage(uint message, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (this.contextMenu3 != null)
                {
                    int result3 = this.contextMenu3.HandleMenuMsg2(message, wParam, lParam, out IntPtr menuResult);
                    if (result3 >= 0)
                    {
                        return menuResult;
                    }
                }
                if (this.contextMenu2 != null)
                {
                    this.contextMenu2.HandleMenuMsg(message, wParam, lParam);
                }
            }
            catch (Exception)
            {
                // 扩展抛异常时忽略，交给系统默认处理，保证菜单仍可显示
            }
            return IntPtr.Zero;
        }
    }

    /// <summary>原生菜单所有者窗口的窗口过程：只把菜单相关消息转给当前会话。</summary>
    private static IntPtr OwnerWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmInitMenuPopup || message == WmDrawItem || message == WmMeasureItem || message == WmMenuChar)
        {
            MenuSession? session;
            lock (SessionLock)
            {
                session = activeSession;
            }
            if (session != null)
            {
                return session.HandleMenuMessage(message, wParam, lParam);
            }
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    /// <summary>把 COM 接口指针转换成指定接口，失败返回空。</summary>
    private static T? GetInterface<T>(IntPtr pointer) where T : class
    {
        if (pointer == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            return Marshal.GetObjectForIUnknown(pointer) as T;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>从原始接口指针查询另一个接口，失败返回空。</summary>
    private static T? QueryInterface<T>(IntPtr pointer) where T : class
    {
        if (pointer == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            Guid iid = typeof(T).GUID;
            IntPtr queried = IntPtr.Zero;
            int result = Marshal.QueryInterface(pointer, ref iid, out queried);
            if (result != 0 || queried == IntPtr.Zero)
            {
                return null;
            }
            T? instance = Marshal.GetObjectForIUnknown(queried) as T;
            Marshal.Release(queried);
            return instance;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>释放 COM 对象，失败时忽略。</summary>
    private static void ReleaseComObject(object? instance)
    {
        if (instance == null || !Marshal.IsComObject(instance))
        {
            return;
        }
        try
        {
            Marshal.FinalReleaseComObject(instance);
        }
        catch (Exception)
        {
            // 已释放或对象不支持释放时忽略
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public int ptInvokeX;
        public int ptInvokeY;
    }

    /// <summary>窗口过程委托。</summary>
    private delegate IntPtr WindowProcDelegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>外壳文件夹接口：只使用 BindToObject、CreateViewObject 与 GetUIObjectOf。</summary>
    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName();

        void EnumObjects();

        void BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr result);

        void BindToStorage();

        void CompareIDs();

        [PreserveSig]
        int CreateViewObject(IntPtr ownerWindow, ref Guid riid, out IntPtr result);

        void GetAttributesOf();

        [PreserveSig]
        int GetUIObjectOf(IntPtr ownerWindow, uint itemCount,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref Guid riid, uint reserved, out IntPtr result);
    }

    /// <summary>系统外壳右键菜单接口。</summary>
    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr menuHandle, uint indexMenu, uint commandFirst, uint commandLast, uint flags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX invokeInfo);

        [PreserveSig]
        int GetCommandString(IntPtr commandId, uint type, IntPtr reserved, IntPtr name, uint nameLength);
    }

    /// <summary>带菜单消息转发的右键菜单接口（IContextMenu2）。</summary>
    [ComImport]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr menuHandle, uint indexMenu, uint commandFirst, uint commandLast, uint flags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX invokeInfo);

        [PreserveSig]
        int GetCommandString(IntPtr commandId, uint type, IntPtr reserved, IntPtr name, uint nameLength);

        [PreserveSig]
        int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>带结果返回的菜单消息转发接口（IContextMenu3）。</summary>
    [ComImport]
    [Guid("BCE01918-3B1B-11D2-B8D8-00C04FB92419")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr menuHandle, uint indexMenu, uint commandFirst, uint commandLast, uint flags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX invokeInfo);

        [PreserveSig]
        int GetCommandString(IntPtr commandId, uint type, IntPtr reserved, IntPtr name, uint nameLength);

        [PreserveSig]
        int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int extendedStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menuHandle, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menuHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenuW(IntPtr menuHandle, uint flags, UIntPtr itemId, string? text);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(IntPtr menuHandle, uint itemId, uint byPosition);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menuHandle, uint flags, int x, int y, IntPtr owner, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int apartmentType);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl,
        uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr shellFolder, out IntPtr childPidl);
}
