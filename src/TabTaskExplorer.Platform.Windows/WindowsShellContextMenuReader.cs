using System;
using System.Collections.Generic;
using System.IO;
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
    private const uint MenuChecked = 0x00000008;
    private const uint MenuDefault = 0x00001000;
    private const uint MenuTypeRadioCheck = 0x00000200;
    private const uint MenuItemMaskType = 0x00000020;
    private const uint MenuItemMaskBitmap = 0x00000080;
    private const int DibRgbColors = 0;
    private const int MaximumIconSize = 256;
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
        int initResult = OleInitialize(IntPtr.Zero);
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
            uint flags = ContextMenuNormal | ContextMenuExplore;
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
                OleUninitialize();
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
        int initResult = OleInitialize(IntPtr.Zero);
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
            uint flags = ContextMenuNormal | ContextMenuExplore;
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
                OleUninitialize();
            }
        }
    }

    /// <summary>
    /// 多选 / 空白处的菜单枚举（供网页端结构化输出）：同一父目录下的全部选中项一起交给外壳，
    /// 保留禁用项与完整子菜单层级，并在读取子菜单前触发 WM_INITMENUPOPUP，让 Git / TortoiseSVN 等动态子项被填充。
    /// </summary>
    public static IReadOnlyList<SystemContextMenuEntry> EnumerateMultiple(IReadOnlyList<string> paths, bool isBackground)
    {
        List<SystemContextMenuEntry> result = new List<SystemContextMenuEntry>();
        if (paths == null || paths.Count == 0)
        {
            return result;
        }
        int initResult = OleInitialize(IntPtr.Zero);
        bool initialized = initResult >= 0;
        IntPtr menuHandle = IntPtr.Zero;
        IntPtr contextPointer = IntPtr.Zero;
        IntPtr parentPointer = IntPtr.Zero;
        object? rawContextMenu = null;
        IShellFolder? shellFolder = null;
        try
        {
            IntPtr ownerWindow = GetForegroundWindow();
            Guid shellFolderIid = typeof(IShellFolder).GUID;
            Guid contextMenuIid = typeof(IContextMenu).GUID;
            if (isBackground)
            {
                IntPtr directoryPidl = IntPtr.Zero;
                SHParseDisplayName(paths[0], IntPtr.Zero, out directoryPidl, 0, out uint _);
                if (directoryPidl == IntPtr.Zero)
                {
                    return result;
                }
                IntPtr childPidl = IntPtr.Zero;
                SHBindToParentPtr(directoryPidl, ref shellFolderIid, out IntPtr parentFolderPointer, out childPidl);
                parentPointer = parentFolderPointer;
                IShellFolder? parentFolder = ComInterfaceFromPointer<IShellFolder>(parentFolderPointer);
                if (parentFolder == null)
                {
                    Marshal.FreeCoTaskMem(directoryPidl);
                    return result;
                }
                IntPtr folderPointer = IntPtr.Zero;
                parentFolder.BindToObject(childPidl, IntPtr.Zero, ref shellFolderIid, out folderPointer);
                // childPidl 指向 directoryPidl 内部，必须在 BindToObject 之后才能释放父 PIDL
                Marshal.FreeCoTaskMem(directoryPidl);
                shellFolder = ComInterfaceFromPointer<IShellFolder>(folderPointer);
                if (shellFolder == null)
                {
                    return result;
                }
                shellFolder.CreateViewObject(ownerWindow, ref contextMenuIid, out contextPointer);
            }
            else
            {
                string firstDirectory = Path.GetDirectoryName(paths[0]) ?? string.Empty;
                List<IntPtr> childPidls = new List<IntPtr>();
                List<IntPtr> allocated = new List<IntPtr>();
                foreach (string candidate in paths)
                {
                    string directory = string.Empty;
                    try
                    {
                        directory = Path.GetDirectoryName(candidate) ?? string.Empty;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (!string.Equals(directory, firstDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    IntPtr pidl = IntPtr.Zero;
                    if (SHParseDisplayName(candidate, IntPtr.Zero, out pidl, 0, out uint _) != 0 || pidl == IntPtr.Zero)
                    {
                        continue;
                    }
                    allocated.Add(pidl);
                    IntPtr itemParent = IntPtr.Zero;
                    IntPtr childPidl = IntPtr.Zero;
                    if (SHBindToParentPtr(pidl, ref shellFolderIid, out itemParent, out childPidl) != 0)
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
                    foreach (IntPtr freedPidl in allocated)
                    {
                        Marshal.FreeCoTaskMem(freedPidl);
                    }
                    return result;
                }
                IShellFolder? parentFolder = ComInterfaceFromPointer<IShellFolder>(parentPointer);
                if (parentFolder == null)
                {
                    return result;
                }
                parentFolder.GetUIObjectOf(ownerWindow, (uint)childPidls.Count, childPidls.ToArray(), ref contextMenuIid, 0, out contextPointer);
                foreach (IntPtr freedPidl in allocated)
                {
                    Marshal.FreeCoTaskMem(freedPidl);
                }
            }
            if (contextPointer == IntPtr.Zero)
            {
                return result;
            }
            rawContextMenu = Marshal.GetObjectForIUnknown(contextPointer);
            IContextMenu? contextMenu = rawContextMenu as IContextMenu;
            if (contextMenu == null)
            {
                return result;
            }
            IContextMenu2? contextMenu2 = QueryInterface<IContextMenu2>(contextPointer);
            menuHandle = CreatePopupMenu();
            uint flags = ContextMenuNormal | ContextMenuExplore;
            if (contextMenu.QueryContextMenu(menuHandle, 0, CommandFirst, CommandLast, flags) < 0)
            {
                return result;
            }
            ReadMenuWithState(menuHandle, 0, contextMenu, contextMenu2, result);
            TrimTrailingSeparators(result);
            ReleaseComObject(contextMenu2);
        }
        catch
        {
            // 外壳扩展异常或 COM 不可用时返回已枚举到的部分，调用方据此降级
        }
        finally
        {
            if (menuHandle != IntPtr.Zero)
            {
                DestroyMenu(menuHandle);
            }
            ReleaseComObject(rawContextMenu);
            ReleaseComObject(shellFolder);
            if (parentPointer != IntPtr.Zero)
            {
                Marshal.Release(parentPointer);
            }
            if (contextPointer != IntPtr.Zero)
            {
                Marshal.Release(contextPointer);
            }
            if (initialized)
            {
                OleUninitialize();
            }
        }
        return result;
    }

    /// <summary>按命令编号唤起多选 / 空白处的系统外壳命令（编号为 QueryContextMenu 分配的绝对编号）。</summary>
    public static bool InvokeByCommandId(int commandId, IReadOnlyList<string> paths, bool isBackground)
    {
        if (paths == null || paths.Count == 0 || commandId < (int)CommandFirst || commandId > CommandLast)
        {
            return false;
        }
        int initResult = OleInitialize(IntPtr.Zero);
        bool initialized = initResult >= 0;
        IntPtr contextPointer = IntPtr.Zero;
        IntPtr parentPointer = IntPtr.Zero;
        object? rawContextMenu = null;
        IShellFolder? shellFolder = null;
        IntPtr menuHandle = IntPtr.Zero;
        try
        {
            IntPtr ownerWindow = GetForegroundWindow();
            Guid shellFolderIid = typeof(IShellFolder).GUID;
            Guid contextMenuIid = typeof(IContextMenu).GUID;
            if (isBackground)
            {
                IntPtr directoryPidl = IntPtr.Zero;
                SHParseDisplayName(paths[0], IntPtr.Zero, out directoryPidl, 0, out uint _);
                if (directoryPidl == IntPtr.Zero)
                {
                    return false;
                }
                IntPtr childPidl = IntPtr.Zero;
                SHBindToParentPtr(directoryPidl, ref shellFolderIid, out IntPtr parentFolderPointer, out childPidl);
                parentPointer = parentFolderPointer;
                IShellFolder? parentFolder = ComInterfaceFromPointer<IShellFolder>(parentFolderPointer);
                if (parentFolder == null)
                {
                    Marshal.FreeCoTaskMem(directoryPidl);
                    return false;
                }
                IntPtr folderPointer = IntPtr.Zero;
                parentFolder.BindToObject(childPidl, IntPtr.Zero, ref shellFolderIid, out folderPointer);
                // childPidl 指向 directoryPidl 内部，必须在 BindToObject 之后才能释放父 PIDL
                Marshal.FreeCoTaskMem(directoryPidl);
                shellFolder = ComInterfaceFromPointer<IShellFolder>(folderPointer);
                if (shellFolder == null)
                {
                    return false;
                }
                shellFolder.CreateViewObject(ownerWindow, ref contextMenuIid, out contextPointer);
            }
            else
            {
                string firstDirectory = Path.GetDirectoryName(paths[0]) ?? string.Empty;
                List<IntPtr> childPidls = new List<IntPtr>();
                List<IntPtr> allocated = new List<IntPtr>();
                foreach (string candidate in paths)
                {
                    string directory = string.Empty;
                    try
                    {
                        directory = Path.GetDirectoryName(candidate) ?? string.Empty;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (!string.Equals(directory, firstDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    IntPtr pidl = IntPtr.Zero;
                    if (SHParseDisplayName(candidate, IntPtr.Zero, out pidl, 0, out uint _) != 0 || pidl == IntPtr.Zero)
                    {
                        continue;
                    }
                    allocated.Add(pidl);
                    IntPtr itemParent = IntPtr.Zero;
                    IntPtr childPidl = IntPtr.Zero;
                    if (SHBindToParentPtr(pidl, ref shellFolderIid, out itemParent, out childPidl) != 0)
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
                    foreach (IntPtr freedPidl in allocated)
                    {
                        Marshal.FreeCoTaskMem(freedPidl);
                    }
                    return false;
                }
                IShellFolder? parentFolder = ComInterfaceFromPointer<IShellFolder>(parentPointer);
                if (parentFolder == null)
                {
                    return false;
                }
                parentFolder.GetUIObjectOf(ownerWindow, (uint)childPidls.Count, childPidls.ToArray(), ref contextMenuIid, 0, out contextPointer);
                foreach (IntPtr freedPidl in allocated)
                {
                    Marshal.FreeCoTaskMem(freedPidl);
                }
            }
            if (contextPointer == IntPtr.Zero)
            {
                return false;
            }
            rawContextMenu = Marshal.GetObjectForIUnknown(contextPointer);
            IContextMenu? contextMenu = rawContextMenu as IContextMenu;
            if (contextMenu == null)
            {
                return false;
            }
            menuHandle = CreatePopupMenu();
            uint flags = ContextMenuNormal | ContextMenuExplore;
            if (contextMenu.QueryContextMenu(menuHandle, 0, CommandFirst, CommandLast, flags) < 0)
            {
                return false;
            }
            CMINVOKECOMMANDINFOEX info = new CMINVOKECOMMANDINFOEX();
            info.cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>();
            info.hwnd = ownerWindow;
            info.nShow = ShowNormal;
            POINT point = new POINT();
            GetCursorPos(out point);
            info.ptInvokeX = point.X;
            info.ptInvokeY = point.Y;
            info.lpVerb = new IntPtr(commandId - (int)CommandFirst);
            return contextMenu.InvokeCommand(ref info) >= 0;
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
            ReleaseComObject(rawContextMenu);
            ReleaseComObject(shellFolder);
            if (parentPointer != IntPtr.Zero)
            {
                Marshal.Release(parentPointer);
            }
            if (contextPointer != IntPtr.Zero)
            {
                Marshal.Release(contextPointer);
            }
            if (initialized)
            {
                OleUninitialize();
            }
        }
    }

    /// <summary>读取菜单层级：保留禁用状态，子菜单先触发 WM_INITMENUPOPUP 再递归读取，命令编号保留绝对值。</summary>
    private static void ReadMenuWithState(IntPtr menuHandle, int depth, IContextMenu contextMenu,
        IContextMenu2? contextMenu2, List<SystemContextMenuEntry> output)
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
                SystemContextMenuEntry separator = new SystemContextMenuEntry();
                separator.IsSeparator = true;
                separator.Group = SystemMenuGroup.FileContext;
                output.Add(separator);
                continue;
            }
            uint rawCommandId = GetMenuItemID(menuHandle, index);
            IntPtr subMenuHandle = GetSubMenu(menuHandle, index);
            StringBuilder textBuilder = new StringBuilder(512);
            GetMenuString(menuHandle, (uint)index, textBuilder, 512, MenuByPosition);
            string rawText = textBuilder.ToString();
            string displayName = NormalizeMenuText(rawText);
            SystemContextMenuEntry entry = new SystemContextMenuEntry();
            entry.DisplayName = displayName.Length > 0 ? displayName : "更多命令";
            entry.IsShellMenuEntry = true;
            entry.Group = SystemMenuGroup.FileContext;
            entry.RegistryPath = "Windows 外壳实时菜单";
            entry.SourceDescription = "由系统外壳 IContextMenu 构建";
            entry.IsEnabled = (state & (MenuGrayed | MenuDisabled)) == 0;
            entry.CanExecute = false;
            // 网页端要显示快捷键与访问键：标题去掉助记符后单独保留这两项信息
            entry.AcceleratorText = ExtractAcceleratorText(rawText);
            entry.AccessKey = ExtractAccessKey(rawText);
            entry.IsChecked = (state & MenuChecked) != 0;
            entry.IsDefault = (state & MenuDefault) != 0;
            entry.IsRadioCheck = IsRadioCheckItem(menuHandle, index);
            entry.IconDataUri = TryReadItemIcon(menuHandle, index);
            if (subMenuHandle != IntPtr.Zero)
            {
                if (contextMenu2 != null)
                {
                    // 与真实弹出子菜单的时机一致：让扩展在 WM_INITMENUPOPUP 里补齐动态项
                    contextMenu2.HandleMenuMsg(0x0117, subMenuHandle, new IntPtr(index));
                }
                ReadMenuWithState(subMenuHandle, depth + 1, contextMenu, contextMenu2, entry.Children);
                TrimTrailingSeparators(entry.Children);
                entry.CanExecute = entry.IsEnabled && entry.Children.Count > 0;
                entry.ShellCommandId = -1;
            }
            else
            {
                int commandId = unchecked((int)rawCommandId);
                entry.ShellCommandId = commandId < 0 ? -1 : commandId;
                entry.CanExecute = entry.IsEnabled && commandId > 0;
                entry.ShellCanonicalVerb = commandId > 0 ? ReadCanonicalVerb(contextMenu, commandId) : string.Empty;
                entry.RequiresElevation = string.Equals(entry.ShellCanonicalVerb, "runas", StringComparison.OrdinalIgnoreCase);
            }
            output.Add(entry);
        }
    }

    /// <summary>从原始接口指针查询指定接口，失败返回空。</summary>
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

    /// <summary>取菜单项快捷键说明：标题中制表符之后的部分（系统已去掉不可读的助记符前导符）。</summary>
    private static string ExtractAcceleratorText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        int tabIndex = text.IndexOf('\t');
        if (tabIndex < 0 || tabIndex >= text.Length - 1)
        {
            return string.Empty;
        }
        return text.Substring(tabIndex + 1).Trim();
    }

    /// <summary>取菜单项访问键：单个 &amp; 后的第一个字符；&amp;&amp; 表示字面量 &amp;，不产生访问键。</summary>
    private static string ExtractAccessKey(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        for (int index = 0; index < text.Length - 1; index++)
        {
            if (text[index] != '&')
            {
                continue;
            }
            if (text[index + 1] == '&')
            {
                index++;
                continue;
            }
            return text[index + 1].ToString();
        }
        return string.Empty;
    }

    /// <summary>读取菜单项是否为单选样式勾选（MFT_RADIOCHECK）。</summary>
    private static bool IsRadioCheckItem(IntPtr menuHandle, int index)
    {
        MENUITEMINFO info = new MENUITEMINFO();
        info.cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>();
        info.fMask = MenuItemMaskType;
        if (!GetMenuItemInfo(menuHandle, (uint)index, true, ref info))
        {
            return false;
        }
        return (info.fType & MenuTypeRadioCheck) != 0;
    }

    /// <summary>
    /// 读取菜单项图标并转成 BMP data URI：只处理系统提供真实位图的项，
    /// 所有者绘制（HBMMENU_CALLBACK）与系统内置图标返回空，由网页端按标题兜底显示。
    /// </summary>
    private static string TryReadItemIcon(IntPtr menuHandle, int index)
    {
        MENUITEMINFO info = new MENUITEMINFO();
        info.cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>();
        info.fMask = MenuItemMaskBitmap;
        if (!GetMenuItemInfo(menuHandle, (uint)index, true, ref info))
        {
            return string.Empty;
        }
        IntPtr bitmapHandle = info.hbmpItem;
        if (bitmapHandle == IntPtr.Zero || bitmapHandle.ToInt64() <= 13)
        {
            // 0 表示无图标，1 到 13 是 HBMMENU_* 系统内置图标，-1 是 HBMMENU_CALLBACK（需所有者绘制）
            return string.Empty;
        }
        try
        {
            return ConvertBitmapToDataUri(bitmapHandle);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>把位图句柄转换成 24 位 BMP data URI，供网页端直接放进 img 标签。</summary>
    private static string ConvertBitmapToDataUri(IntPtr bitmapHandle)
    {
        BITMAP bitmap = new BITMAP();
        if (GetObject(bitmapHandle, Marshal.SizeOf<BITMAP>(), out bitmap) == 0)
        {
            return string.Empty;
        }
        int width = bitmap.bmWidth;
        int height = bitmap.bmHeight;
        if (width <= 0 || height <= 0 || width > MaximumIconSize || height > MaximumIconSize)
        {
            return string.Empty;
        }
        BITMAPINFOHEADER header = new BITMAPINFOHEADER();
        header.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        header.biWidth = width;
        header.biHeight = height;
        header.biPlanes = 1;
        header.biBitCount = 24;
        header.biCompression = 0;
        header.biSizeImage = (uint)(width * height * 3);
        byte[] pixels = new byte[width * height * 3];
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return string.Empty;
        }
        try
        {
            if (GetDIBits(screenDc, bitmapHandle, 0, (uint)height, pixels, ref header, DibRgbColors) == 0)
            {
                return string.Empty;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
        int pixelOffset = 14 + 40;
        int fileSize = pixelOffset + pixels.Length;
        byte[] fileBytes = new byte[fileSize];
        fileBytes[0] = (byte)'B';
        fileBytes[1] = (byte)'M';
        WriteInt32(fileBytes, 2, fileSize);
        WriteInt32(fileBytes, 10, pixelOffset);
        WriteInt32(fileBytes, 14, 40);
        WriteInt32(fileBytes, 18, width);
        WriteInt32(fileBytes, 22, height);
        fileBytes[26] = 1;
        fileBytes[28] = 24;
        WriteInt32(fileBytes, 34, pixels.Length);
        Array.Copy(pixels, 0, fileBytes, pixelOffset, pixels.Length);
        return "data:image/bmp;base64," + Convert.ToBase64String(fileBytes);
    }

    /// <summary>按小端序写入 32 位整数（BMP 文件头使用）。</summary>
    private static void WriteInt32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
        target[offset + 2] = (byte)((value >> 16) & 0xFF);
        target[offset + 3] = (byte)((value >> 24) & 0xFF);
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

    /// <summary>带菜单消息转发的右键菜单接口（IContextMenu2），网页端用它触发动筷子菜单填充。</summary>
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
    private static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int apartmentType);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext,
        out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHBindToParent(IntPtr pidl, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder shellFolder, out IntPtr childPidl);

    [DllImport("shell32.dll", EntryPoint = "SHBindToParent")]
    private static extern int SHBindToParentPtr(IntPtr pidl, ref Guid riid,
        out IntPtr shellFolder, out IntPtr childPidl);

    /// <summary>菜单项信息（只读取类型与位图字段）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    /// <summary>GDI 位图基本信息。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    /// <summary>位图信息头（取像素时使用 24 位 BI_RGB）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

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
    private static extern bool GetMenuItemInfo(IntPtr menuHandle, uint item, bool byPosition, ref MENUITEMINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", CharSet = CharSet.Unicode)]
    private static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr deviceContext, IntPtr bitmap, uint startScanLine,
        uint scanLineCount, byte[] bits, ref BITMAPINFOHEADER header, uint usage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
