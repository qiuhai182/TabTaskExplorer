using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 系统外壳右键菜单读取器：通过 IContextMenu 让系统按当前目标构建完整菜单，
/// 枚举出全部已启用的静态动词与外壳扩展（含级联子菜单），并按命令编号或规范动词唤起。
/// 这样本程序右键菜单里的系统项与资源管理器右键同一文件 / 文件夹时完全一致。
/// </summary>
internal static class WindowsShellContextMenuReader
{
    private const uint CommandFirst = 0;
    private const int CommandLast = 0x7FFF;
    private const uint ContextMenuNormal = 0x00000000;
    private const uint ContextMenuExplore = 0x00000004;
    private const uint MenuByPosition = 0x00000400;
    private const uint MenuSeparator = 0x00000800;
    private const uint MenuGrayed = 0x00000001;
    private const uint MenuDisabled = 0x00000002;
    private const uint CommandStringVerbWide = 0x00000004;
    private const uint InvokeMaskUnicode = 0x00004000;
    private const int MaximumMenuDepth = 4;
    private const int ShowNormal = 1;
    private const uint BackgroundItem = 0;

    /// <summary>取当前目标适用的系统外壳菜单项；失败时返回空列表，由调用方回退到注册表静态清单。</summary>
    public static IReadOnlyList<SystemContextMenuEntry> Enumerate(string targetPath, bool isBackground)
    {
        List<SystemContextMenuEntry> result = new List<SystemContextMenuEntry>();
        if (string.IsNullOrEmpty(targetPath))
        {
            return result;
        }
        int initResult = CoInitializeEx(IntPtr.Zero, 2);
        bool initialized = initResult >= 0;
        IntPtr pidl = IntPtr.Zero;
        IntPtr menuHandle = IntPtr.Zero;
        IContextMenu? contextMenu = null;
        IShellFolder? parentFolder = null;
        IShellFolder? shellFolder = null;
        IShellView? shellView = null;
        try
        {
            SHParseDisplayName(targetPath, IntPtr.Zero, out pidl, 0, out uint attributes);
            Guid shellFolderIid = typeof(IShellFolder).GUID;
            IntPtr childPidl;
            SHBindToParent(pidl, ref shellFolderIid, out parentFolder, out childPidl);
            IntPtr ownerWindow = GetForegroundWindow();
            if (ownerWindow == IntPtr.Zero)
            {
                ownerWindow = GetActiveWindow();
            }
            if (isBackground)
            {
                IntPtr folderPointer;
                parentFolder.BindToObject(childPidl, IntPtr.Zero, ref shellFolderIid, out folderPointer);
                shellFolder = ComInterfaceFromPointer<IShellFolder>(folderPointer);
                Guid shellViewIid = typeof(IShellView).GUID;
                IntPtr viewPointer;
                shellFolder.CreateViewObject(ownerWindow, ref shellViewIid, out viewPointer);
                shellView = ComInterfaceFromPointer<IShellView>(viewPointer);
                Guid contextMenuIid = typeof(IContextMenu).GUID;
                IntPtr contextPointer;
                shellView.GetItemObject(BackgroundItem, ref contextMenuIid, out contextPointer);
                contextMenu = ComInterfaceFromPointer<IContextMenu>(contextPointer);
            }
            else
            {
                Guid contextMenuIid = typeof(IContextMenu).GUID;
                IntPtr contextPointer;
                IntPtr[] childPidls = new IntPtr[] { childPidl };
                parentFolder.GetUIObjectOf(ownerWindow, 1, childPidls, ref contextMenuIid, 0, out contextPointer);
                contextMenu = ComInterfaceFromPointer<IContextMenu>(contextPointer);
            }
            if (contextMenu == null)
            {
                return result;
            }
            menuHandle = CreatePopupMenu();
            uint flags = isBackground ? (ContextMenuNormal | ContextMenuExplore) : ContextMenuNormal;
            int queryResult = contextMenu.QueryContextMenu(menuHandle, 0, CommandFirst, CommandLast, flags);
            if (queryResult < 0)
            {
                return result;
            }
            ReadMenu(menuHandle, 0, contextMenu, result);
            TrimTrailingSeparators(result);
        }
        catch
        {
            // 外壳扩展未注册、路径无法解析或 COM 不可用时返回空列表，由调用方回退
            result.Clear();
        }
        finally
        {
            if (menuHandle != IntPtr.Zero)
            {
                DestroyMenu(menuHandle);
            }
            ReleaseComObject(contextMenu);
            ReleaseComObject(shellView);
            ReleaseComObject(shellFolder);
            ReleaseComObject(parentFolder);
            if (pidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
            if (initialized)
            {
                CoUninitialize();
            }
        }
        return result;
    }

    /// <summary>唤起一条系统外壳菜单项：优先按规范动词唤起，失败时退回命令编号。</summary>
    public static bool Invoke(SystemContextMenuEntry entry, string targetPath, bool isBackground)
    {
        if (entry == null || !entry.IsShellMenuEntry || entry.ShellCommandId < 0 || string.IsNullOrEmpty(targetPath))
        {
            return false;
        }
        int initResult = CoInitializeEx(IntPtr.Zero, 2);
        bool initialized = initResult >= 0;
        IntPtr pidl = IntPtr.Zero;
        IntPtr menuHandle = IntPtr.Zero;
        IContextMenu? contextMenu = null;
        IShellFolder? parentFolder = null;
        IShellFolder? shellFolder = null;
        IShellView? shellView = null;
        try
        {
            SHParseDisplayName(targetPath, IntPtr.Zero, out pidl, 0, out uint attributes);
            Guid shellFolderIid = typeof(IShellFolder).GUID;
            IntPtr childPidl;
            SHBindToParent(pidl, ref shellFolderIid, out parentFolder, out childPidl);
            IntPtr ownerWindow = GetForegroundWindow();
            if (ownerWindow == IntPtr.Zero)
            {
                ownerWindow = GetActiveWindow();
            }
            if (isBackground)
            {
                IntPtr folderPointer;
                parentFolder.BindToObject(childPidl, IntPtr.Zero, ref shellFolderIid, out folderPointer);
                shellFolder = ComInterfaceFromPointer<IShellFolder>(folderPointer);
                Guid shellViewIid = typeof(IShellView).GUID;
                IntPtr viewPointer;
                shellFolder.CreateViewObject(ownerWindow, ref shellViewIid, out viewPointer);
                shellView = ComInterfaceFromPointer<IShellView>(viewPointer);
                Guid contextMenuIid = typeof(IContextMenu).GUID;
                IntPtr contextPointer;
                shellView.GetItemObject(BackgroundItem, ref contextMenuIid, out contextPointer);
                contextMenu = ComInterfaceFromPointer<IContextMenu>(contextPointer);
            }
            else
            {
                Guid contextMenuIid = typeof(IContextMenu).GUID;
                IntPtr contextPointer;
                IntPtr[] childPidls = new IntPtr[] { childPidl };
                parentFolder.GetUIObjectOf(ownerWindow, 1, childPidls, ref contextMenuIid, 0, out contextPointer);
                contextMenu = ComInterfaceFromPointer<IContextMenu>(contextPointer);
            }
            if (contextMenu == null)
            {
                return false;
            }
            menuHandle = CreatePopupMenu();
            uint flags = isBackground ? (ContextMenuNormal | ContextMenuExplore) : ContextMenuNormal;
            if (contextMenu.QueryContextMenu(menuHandle, 0, CommandFirst, CommandLast, flags) < 0)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(entry.ShellCanonicalVerb))
            {
                if (InvokeWithCanonicalVerb(contextMenu, entry.ShellCanonicalVerb, ownerWindow))
                {
                    return true;
                }
            }
            return InvokeWithCommandId(contextMenu, entry.ShellCommandId, ownerWindow);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (menuHandle != IntPtr.Zero)
            {
                DestroyMenu(menuHandle);
            }
            ReleaseComObject(contextMenu);
            ReleaseComObject(shellView);
            ReleaseComObject(shellFolder);
            ReleaseComObject(parentFolder);
            if (pidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
            if (initialized)
            {
                CoUninitialize();
            }
        }
    }

    /// <summary>递归读取一个 HMENU 下的菜单项，生成带子菜单结构的条目列表。</summary>
    private static void ReadMenu(IntPtr menuHandle, int depth, IContextMenu contextMenu, List<SystemContextMenuEntry> output)
    {
        if (menuHandle == IntPtr.Zero || depth > MaximumMenuDepth)
        {
            return;
        }
        int itemCount = GetMenuItemCount(menuHandle);
        for (int index = 0; index < itemCount; index++)
        {
            uint state = GetMenuState(menuHandle, (uint)index, MenuByPosition);
            if ((state & MenuSeparator) != 0)
            {
                if (output.Count > 0 && !output[output.Count - 1].IsSeparator)
                {
                    SystemContextMenuEntry separator = new SystemContextMenuEntry();
                    separator.IsSeparator = true;
                    separator.Group = SystemMenuGroup.FileContext;
                    output.Add(separator);
                }
                continue;
            }
            if ((state & (MenuGrayed | MenuDisabled)) != 0)
            {
                // 用户要求只加载已启用的菜单项，灰化与禁用项直接跳过
                continue;
            }
            uint rawCommandId = GetMenuItemID(menuHandle, index);
            IntPtr subMenuHandle = GetSubMenu(menuHandle, index);
            StringBuilder textBuilder = new StringBuilder(512);
            GetMenuString(menuHandle, (uint)index, textBuilder, 512, MenuByPosition);
            string displayName = NormalizeMenuText(textBuilder.ToString());
            if (displayName.Length == 0 && subMenuHandle == IntPtr.Zero)
            {
                continue;
            }
            SystemContextMenuEntry entry = new SystemContextMenuEntry();
            entry.DisplayName = displayName.Length > 0 ? displayName : "更多命令";
            entry.IsShellMenuEntry = true;
            entry.Group = SystemMenuGroup.FileContext;
            entry.RegistryPath = "Windows 外壳实时菜单";
            entry.SourceDescription = "由系统外壳 IContextMenu 构建";
            entry.IsEnabled = true;
            entry.CanExecute = false;
            if (subMenuHandle != IntPtr.Zero)
            {
                ReadMenu(subMenuHandle, depth + 1, contextMenu, entry.Children);
                TrimTrailingSeparators(entry.Children);
                entry.CanExecute = entry.Children.Count > 0;
            }
            else
            {
                int commandId = unchecked((int)rawCommandId);
                if (commandId < 0)
                {
                    continue;
                }
                entry.ShellCommandId = commandId;
                entry.ShellCanonicalVerb = ReadCanonicalVerb(contextMenu, commandId);
                entry.CanExecute = true;
            }
            output.Add(entry);
        }
        TrimTrailingSeparators(output);
    }

    /// <summary>读取菜单命令的规范动词名（GetCommandString 的 GCS_VERBW），失败时返回空文本。</summary>
    private static string ReadCanonicalVerb(IContextMenu contextMenu, int commandId)
    {
        IntPtr buffer = Marshal.AllocHGlobal(1024);
        try
        {
            int result = contextMenu.GetCommandString(new IntPtr(commandId), CommandStringVerbWide,
                IntPtr.Zero, buffer, 512);
            if (result < 0)
            {
                return string.Empty;
            }
            string verb = Marshal.PtrToStringUni(buffer) ?? string.Empty;
            return verb.Trim();
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>按规范动词唤起菜单命令。</summary>
    private static bool InvokeWithCanonicalVerb(IContextMenu contextMenu, string verb, IntPtr ownerWindow)
    {
        IntPtr verbPointer = IntPtr.Zero;
        try
        {
            verbPointer = Marshal.StringToHGlobalUni(verb);
            CMINVOKECOMMANDINFOEX info = CreateInvokeInfo(ownerWindow, true);
            info.lpVerbW = verbPointer;
            return contextMenu.InvokeCommand(ref info) >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (verbPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(verbPointer);
            }
        }
    }

    /// <summary>按菜单命令编号唤起。</summary>
    private static bool InvokeWithCommandId(IContextMenu contextMenu, int commandId, IntPtr ownerWindow)
    {
        try
        {
            CMINVOKECOMMANDINFOEX info = CreateInvokeInfo(ownerWindow, false);
            info.lpVerb = new IntPtr(commandId);
            return contextMenu.InvokeCommand(ref info) >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>构造唤起信息：带光标位置与前台窗口，供外壳扩展放置对话框。</summary>
    private static CMINVOKECOMMANDINFOEX CreateInvokeInfo(IntPtr ownerWindow, bool unicode)
    {
        CMINVOKECOMMANDINFOEX info = new CMINVOKECOMMANDINFOEX();
        info.cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>();
        info.fMask = unicode ? (int)InvokeMaskUnicode : 0;
        info.hwnd = ownerWindow;
        info.nShow = ShowNormal;
        POINT point = new POINT();
        GetCursorPos(out point);
        info.ptInvokeX = point.X;
        info.ptInvokeY = point.Y;
        return info;
    }

    /// <summary>规范化菜单文字：去掉助记符与制表符后的快捷键说明，保留可读名称。</summary>
    private static string NormalizeMenuText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        string value = text;
        int tabIndex = value.IndexOf('\t');
        if (tabIndex >= 0)
        {
            value = value.Substring(0, tabIndex);
        }
        value = value.Replace("&&", "\u0001");
        value = value.Replace("&", string.Empty);
        value = value.Replace("\u0001", "&");
        return value.Trim();
    }

    /// <summary>去掉列表开头和结尾的分隔线，避免菜单出现空白分组。</summary>
    private static void TrimTrailingSeparators(List<SystemContextMenuEntry> entries)
    {
        while (entries.Count > 0 && entries[0].IsSeparator)
        {
            entries.RemoveAt(0);
        }
        while (entries.Count > 0 && entries[entries.Count - 1].IsSeparator)
        {
            entries.RemoveAt(entries.Count - 1);
        }
    }

    /// <summary>把接口指针转换成 COM 接口对象；指针为空时返回 null。</summary>
    private static T? ComInterfaceFromPointer<T>(IntPtr pointer) where T : class
    {
        if (pointer == IntPtr.Zero)
        {
            return null;
        }
        return Marshal.GetObjectForIUnknown(pointer) as T;
    }

    /// <summary>释放 COM 对象，失败时忽略（对象可能已由 GC 回收）。</summary>
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
        catch
        {
            // 已释放时忽略
        }
    }

    /// <summary>系统外壳文件夹接口：只使用 BindToObject、CreateViewObject 与 GetUIObjectOf。</summary>
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

        void CreateViewObject(IntPtr ownerWindow, ref Guid riid, out IntPtr result);

        void GetAttributesOf();

        void GetUIObjectOf(IntPtr ownerWindow, uint itemCount,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref Guid riid, uint reserved, out IntPtr result);
    }

    /// <summary>系统外壳视图接口：只使用 GetItemObject 取空白处菜单。</summary>
    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();

        void ContextSensitiveHelp();

        void TranslateAccelerator();

        void EnableModeless();

        void UIActivate();

        void Refresh();

        void CreateViewWindow();

        void DestroyViewWindow();

        void GetCurrentInfo();

        void AddPropertySheetPages();

        void SaveViewState();

        void SelectItem();

        void GetItemObject(uint item, ref Guid riid, out IntPtr result);
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

    /// <summary>外壳菜单唤起信息（扩展版，含 Unicode 动词与光标位置）。</summary>
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

    /// <summary>屏幕坐标点。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int apartmentType);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHParseDisplayName(string name, IntPtr bindContext,
        out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHBindToParent(IntPtr pidl, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder shellFolder, out IntPtr childPidl);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menuHandle);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menuHandle);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(IntPtr menuHandle, int position);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSubMenu(IntPtr menuHandle, int position);

    [DllImport("user32.dll")]
    private static extern uint GetMenuState(IntPtr menuHandle, uint itemId, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr menuHandle, uint itemId, StringBuilder text, int maxLength, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
