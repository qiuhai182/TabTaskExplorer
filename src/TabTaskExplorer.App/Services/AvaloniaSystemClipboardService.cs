using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 系统剪贴板服务的 Avalonia 实现：三平台共用同一套代码，不按平台裁剪功能。
/// 写入时用一次 SetDataAsync 同时携带我方状态标记、真实文件列表与各平台剪切标记，
/// 保证本程序与资源管理器等外部程序双向互通；读取时按同样口径还原。
/// </summary>
public class AvaloniaSystemClipboardService : ISystemClipboardService
{
    /// <summary>我方状态标记格式：首行是剪切或复制，其余行是全部路径（含压缩包虚拟路径）。</summary>
    private static readonly DataFormat<string> StateFormat =
        DataFormat.CreateStringApplicationFormat("tabtaskexplorer-cut");

    /// <summary>Windows 系统的首选放置效果格式：值为 2 表示移动（剪切）。</summary>
    private static readonly DataFormat<byte[]> WindowsPreferredDropEffectFormat =
        DataFormat.CreateBytesPlatformFormat("Preferred DropEffect");

    /// <summary>GNOME 系文件管理器的剪贴板标记格式：首行为 cut 或 copy，其余行为文件协议地址。</summary>
    private static readonly DataFormat<string> LinuxGnomeCopiedFilesFormat =
        DataFormat.CreateStringPlatformFormat("x-special/gnome-copied-files");

    /// <summary>KDE 文件管理器的剪切标记格式：值为 1 表示剪切。</summary>
    private static readonly DataFormat<string> LinuxKdeCutSelectionFormat =
        DataFormat.CreateStringPlatformFormat("application/x-kde-cutselection");

    /// <summary>Windows 首选放置效果里的移动标记：DROPEFFECT_MOVE 等于 2（小端 4 字节）。</summary>
    private static readonly byte[] WindowsMoveEffectBytes = { 2, 0, 0, 0 };

    /// <summary>当前环境是否支持系统剪贴板；拿不到主窗口或剪贴板对象时视为不支持。</summary>
    public bool IsSupported
    {
        get
        {
            TopLevel? owner = GetOwnerTopLevel();
            if (owner == null || owner.Clipboard == null)
            {
                UiLogger.Log("系统剪贴板", "查询剪贴板支持情况：主窗口或剪贴板对象=未取到 结果=不支持，上层回退内部剪贴板");
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 把路径列表写入系统剪贴板：真实磁盘路径写成文件格式供外部程序粘贴，
    /// 全部路径写进我方状态标记（压缩包虚拟路径只进状态标记），并附带各平台的剪切标记。
    /// </summary>
    /// <param name="paths">要写入的路径列表（可含压缩包虚拟路径）。</param>
    /// <param name="isCut">true 为剪切（外部粘贴为移动），false 为复制。</param>
    /// <returns>写入成功返回 true，失败或不支持返回 false。</returns>
    public async Task<bool> WriteAsync(IReadOnlyList<string> paths, bool isCut)
    {
        if (OperatingSystem.IsWindows())
        {
            ClipboardPresence probe = await Task.Run(ProbeOnWindowsWorker).ConfigureAwait(false);
            if (!probe.ClipboardAvailable)
            {
                UiLogger.Log("系统剪贴板", "写入剪贴板：剪贴板被其他进程占用 结果=放弃本次写入，仅保留内部剪贴板");
                return false;
            }
        }
        return await Dispatcher.UIThread.InvokeAsync(() => WriteOnUIThreadAsync(paths, isCut));
    }

    /// <summary>把一段纯文本写入系统剪贴板（例如网页端地址），供粘贴到浏览器地址栏等场景使用。</summary>
    /// <param name="text">要写入剪贴板的文本。</param>
    /// <returns>写入成功返回 true，失败或不支持返回 false。</returns>
    public async Task<bool> WriteTextAsync(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            ClipboardPresence probe = await Task.Run(ProbeOnWindowsWorker).ConfigureAwait(false);
            if (!probe.ClipboardAvailable)
            {
                UiLogger.Log("系统剪贴板", "写入文本：剪贴板被其他进程占用 结果=放弃本次写入");
                return false;
            }
        }
        return await Dispatcher.UIThread.InvokeAsync(() => WriteTextOnUIThreadAsync(text));
    }

    /// <summary>
    /// 读取系统剪贴板内容：先读我方状态标记，再读外部文件列表与各平台剪切标记。
    /// </summary>
    /// <returns>读取结果；读取失败时返回 HasTransfer 为 false 的结果，由上层回退内部剪贴板。</returns>
    public async Task<ClipboardContent> ReadAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows 完全在后台 STA 线程用 OLE 读取（最多等 500 毫秒），界面线程绝不等待剪贴板
            return await Task.Run(ReadOnWindowsBackground).ConfigureAwait(false);
        }
        return await Dispatcher.UIThread.InvokeAsync(ReadOnUIThreadAsync);
    }

    /// <summary>
    /// 清空系统剪贴板。剪切落地成功后调用，与资源管理器一致：剪切的文件粘贴走后不再保留在剪贴板里。
    /// </summary>
    /// <returns>清空成功返回 true，失败或不支持返回 false。</returns>
    public async Task<bool> ClearAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            ClipboardPresence probe = await Task.Run(ProbeOnWindowsWorker).ConfigureAwait(false);
            if (!probe.ClipboardAvailable)
            {
                UiLogger.Log("系统剪贴板", "清空剪贴板：剪贴板被其他进程占用 结果=放弃本次清空");
                return false;
            }
        }
        return await Dispatcher.UIThread.InvokeAsync(ClearOnUIThreadAsync);
    }

    /// <summary>
    /// 轻量探测剪贴板是否含文件 / 文件夹：只取文件列表与我方状态标记，不读文件内容，也不写成功日志（由调用方按变化记录）。
    /// </summary>
    public Task<ClipboardPresence> ProbeAsync()
    {
        // Windows：完全在后台线程用 Win32 读取 CF_HDROP，OpenClipboard 最多重试 200 毫秒，绝不进 UI 线程。
        if (OperatingSystem.IsWindows())
        {
            return Task.Run(ProbeOnWindowsWorker);
        }
        // 其他平台：剪贴板实现通常不出现 OpenClipboard 独占阻塞，保留 UI 线程实现但加 250 毫秒超时兜底。
        return ProbeOnUiThreadWithTimeoutAsync();
    }

    /// <summary>在界面线程上清空系统剪贴板。</summary>
    private static async Task<bool> ClearOnUIThreadAsync()
    {
        TopLevel? owner = GetOwnerTopLevel();
        IClipboard? clipboard = owner == null ? null : owner.Clipboard;
        if (clipboard == null)
        {
            UiLogger.Log("系统剪贴板", "清空剪贴板：主窗口或剪贴板对象=未取到 结果=失败，未清空");
            return false;
        }
        try
        {
            await clipboard.ClearAsync();
            await clipboard.FlushAsync();
            UiLogger.Log("系统剪贴板", "清空剪贴板：结果=成功，系统剪贴板已清空");
            return true;
        }
        catch (Exception error)
        {
            UiLogger.LogError("系统剪贴板", "清空剪贴板：结果=失败", error);
            return false;
        }
    }

    /// <summary>在界面线程上执行剪贴板写入。</summary>
    private static async Task<bool> WriteOnUIThreadAsync(IReadOnlyList<string> paths, bool isCut)
    {
        if (paths == null || paths.Count == 0)
        {
            UiLogger.Log("系统剪贴板", "写入剪贴板：路径列表=空 结果=失败，未写入任何内容");
            return false;
        }
        TopLevel? owner = GetOwnerTopLevel();
        IClipboard? clipboard = owner == null ? null : owner.Clipboard;
        if (owner == null || clipboard == null)
        {
            UiLogger.Log("系统剪贴板", "写入剪贴板：主窗口或剪贴板对象=未取到 结果=失败，上层回退内部剪贴板");
            return false;
        }

        try
        {
            DataTransfer transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(StateFormat, BuildStateValue(paths, isCut)));

            int fileItemCount = 0;
            foreach (string path in paths)
            {
                IStorageItem? storageItem = await TryCreateStorageItemAsync(owner, path);
                if (storageItem != null)
                {
                    transfer.Add(DataTransferItem.CreateFile(storageItem));
                    fileItemCount++;
                }
            }

            AddPlatformCutMarkers(transfer, paths, isCut);

            await clipboard.SetDataAsync(transfer);
            // 刷新后程序退出时外部程序仍能读到剪贴板内容（其他平台为空操作）
            await clipboard.FlushAsync();

            UiLogger.Log("系统剪贴板", "写入剪贴板：路径总数=" + paths.Count + " 路径列表=" + string.Join("、", paths)
                + " 方式=" + (isCut ? "剪切" : "复制") + " 写入真实文件项数=" + fileItemCount
                + " 结果=成功，系统剪贴板已更新");
            return true;
        }
        catch (Exception error)
        {
            UiLogger.LogError("系统剪贴板", "写入剪贴板：路径总数=" + paths.Count
                + " 方式=" + (isCut ? "剪切" : "复制") + " 结果=失败，上层回退内部剪贴板", error);
            return false;
        }
    }

    /// <summary>在界面线程上把纯文本写入系统剪贴板。</summary>
    private static async Task<bool> WriteTextOnUIThreadAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            UiLogger.Log("系统剪贴板", "写入文本：文本内容=空 结果=失败，未写入任何内容");
            return false;
        }
        TopLevel? owner = GetOwnerTopLevel();
        IClipboard? clipboard = owner == null ? null : owner.Clipboard;
        if (clipboard == null)
        {
            UiLogger.Log("系统剪贴板", "写入文本：主窗口或剪贴板对象=未取到 结果=失败，未写入任何内容");
            return false;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            // 刷新后程序退出时外部程序仍能读到剪贴板内容（其他平台为空操作）
            await clipboard.FlushAsync();
            UiLogger.Log("系统剪贴板", "写入文本：文本长度=" + text.Length + " 文本内容=" + text
                + " 结果=成功，系统剪贴板已更新");
            return true;
        }
        catch (Exception error)
        {
            UiLogger.LogError("系统剪贴板", "写入文本：文本长度=" + text.Length + " 文本内容=" + text
                + " 结果=失败，未写入任何内容", error);
            return false;
        }
    }

    /// <summary>在界面线程上执行剪贴板读取。</summary>
    private static async Task<ClipboardContent> ReadOnUIThreadAsync()
    {
        ClipboardContent content = new ClipboardContent();
        TopLevel? owner = GetOwnerTopLevel();
        IClipboard? clipboard = owner == null ? null : owner.Clipboard;
        if (owner == null || clipboard == null)
        {
            UiLogger.Log("系统剪贴板", "读取剪贴板：主窗口或剪贴板对象=未取到 结果=失败，上层回退内部剪贴板");
            return content;
        }

        try
        {
            using (IAsyncDataTransfer? transfer = await clipboard.TryGetDataAsync())
            {
                if (transfer == null)
                {
                    UiLogger.Log("系统剪贴板", "读取剪贴板：系统剪贴板=空 结果=成功，无可读内容，上层回退内部剪贴板");
                    return content;
                }
                content.HasTransfer = true;

                string? stateValue = await transfer.TryGetValueAsync(StateFormat);
                if (!string.IsNullOrEmpty(stateValue))
                {
                    ParseStateValue(stateValue, content);
                }

                IStorageItem[]? files = await transfer.TryGetFilesAsync();
                if (files != null)
                {
                    List<string> systemPaths = new List<string>();
                    foreach (IStorageItem file in files)
                    {
                        string? localPath = file.TryGetLocalPath();
                        if (!string.IsNullOrEmpty(localPath))
                        {
                            systemPaths.Add(localPath);
                        }
                    }
                    content.SystemFiles = systemPaths;
                }

                content.SystemCutIndicated = await ReadPlatformCutIndicatedAsync(transfer);

                UiLogger.Log("系统剪贴板", "读取剪贴板：状态标记路径数=" + content.StatePaths.Count
                    + " 状态标记路径列表=" + (content.StatePaths.Count == 0 ? "（无）" : string.Join("、", content.StatePaths))
                    + " 状态标记方式=" + (content.StateIsCut ? "剪切" : "复制")
                    + " 外部文件数=" + content.SystemFiles.Count
                    + " 外部文件列表=" + (content.SystemFiles.Count == 0 ? "（无）" : string.Join("、", content.SystemFiles))
                    + " 系统剪切标记=" + (content.SystemCutIndicated ? "是" : "否")
                    + " 结果=成功");
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("系统剪贴板", "读取剪贴板：结果=失败，上层回退内部剪贴板", error);
        }
        return content;
    }

    /// <summary>非 Windows 平台：UI 线程探测加 250 毫秒超时，超时按不可用处理，避免调用方等待。</summary>
    private static async Task<ClipboardPresence> ProbeOnUiThreadWithTimeoutAsync()
    {
        Task<ClipboardPresence> probe = Dispatcher.UIThread.InvokeAsync(ProbeOnUIThreadAsync);
        Task completed = await Task.WhenAny(probe, Task.Delay(250)).ConfigureAwait(false);
        if (completed != probe)
        {
            return new ClipboardPresence { ClipboardAvailable = false, FormatDescription = "探测超时" };
        }
        return await probe.ConfigureAwait(false);
    }

    private sealed class ReadHolder
    {
        internal ClipboardContent? Value;
    }

    /// <summary>Windows 后台读取剪贴板：专用 STA 线程 + 最多 500 毫秒等待，超时返回空内容。</summary>
    private static ClipboardContent ReadOnWindowsBackground()
    {
        ReadHolder holder = new ReadHolder();
        Thread worker = new Thread(new ThreadStart(delegate
        {
            holder.Value = ReadWithOleOnSta();
        }));
        worker.IsBackground = true;
        worker.Name = "剪贴板读取";
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!worker.Join(500))
        {
            UiLogger.Log("系统剪贴板", "读取剪贴板：后台 OLE 读取超时 等待毫秒=500 结果=按空剪贴板处理");
            return new ClipboardContent();
        }
        return holder.Value ?? new ClipboardContent();
    }

    /// <summary>在 STA 线程上用 OLE 读取文件列表、我方状态标记与剪切标记，不接触界面线程。</summary>
    private static ClipboardContent ReadWithOleOnSta()
    {
        ClipboardContent content = new ClipboardContent();
        int initResult = OleInitialize(IntPtr.Zero);
        try
        {
            object dataObject;
            if (OleGetClipboard(out dataObject) != 0 || dataObject == null)
            {
                return content;
            }
            System.Runtime.InteropServices.ComTypes.IDataObject? obj =
                dataObject as System.Runtime.InteropServices.ComTypes.IDataObject;
            if (obj == null)
            {
                return content;
            }
            content.HasTransfer = true;

            List<string> files = new List<string>();
            IntPtr drop = TryGetHandle(obj, CF_HDROP, out byte[]? dropBytes);
            if (drop != IntPtr.Zero)
            {
                uint count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
                for (uint index = 0; index < count; index++)
                {
                    StringBuilder buffer = new StringBuilder(4096);
                    if (DragQueryFileW(drop, index, buffer, 4096) > 0)
                    {
                        files.Add(buffer.ToString());
                    }
                }
            }
            content.SystemFiles = files;

            byte[]? effect = TryGetBytes(obj, RegisterClipboardFormatW("Preferred DropEffect"));
            content.SystemCutIndicated = effect != null && effect.Length >= 4 && BitConverter.ToUInt32(effect, 0) == 2;

            byte[]? stateBytes = TryGetBytes(obj, RegisterClipboardFormatW("tabtaskexplorer-cut"));
            if (stateBytes != null)
            {
                TryParseStateBytes(stateBytes, content);
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("系统剪贴板", "后台 OLE 读取剪贴板失败", error);
        }
        finally
        {
            if (initResult >= 0)
            {
                OleUninitialize();
            }
        }
        return content;
    }

    private static IntPtr TryGetHandle(System.Runtime.InteropServices.ComTypes.IDataObject obj, uint format,
        out byte[]? bytes)
    {
        bytes = null;
        System.Runtime.InteropServices.ComTypes.FORMATETC formatEtc =
            new System.Runtime.InteropServices.ComTypes.FORMATETC
        {
            cfFormat = (short)format,
            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
        };
        System.Runtime.InteropServices.ComTypes.STGMEDIUM medium;
        try
        {
            obj.GetData(ref formatEtc, out medium);
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
        try
        {
            bytes = TryReadGlobalBytes(medium.unionmember);
            return medium.unionmember;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static byte[]? TryGetBytes(System.Runtime.InteropServices.ComTypes.IDataObject obj, uint format)
    {
        IntPtr handle = TryGetHandle(obj, format, out byte[]? bytes);
        return handle == IntPtr.Zero ? null : bytes;
    }

    private static byte[]? TryReadGlobalBytes(IntPtr handle)
    {
        try
        {
            UIntPtr size = GlobalSize(handle);
            int length = (int)size.ToUInt64();
            if (length <= 0 || length > 1024 * 1024)
            {
                return null;
            }
            byte[] buffer = new byte[length];
            Marshal.Copy(handle, buffer, 0, length);
            return buffer;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>解析我方状态标记字节（Avalonia 写入的自定义格式可能是 UTF-16 或 UTF-8）。</summary>
    private static void TryParseStateBytes(byte[] bytes, ClipboardContent content)
    {
        string text = Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF').TrimEnd('\0');
        if (!text.StartsWith("cut", StringComparison.Ordinal) && !text.StartsWith("copy", StringComparison.Ordinal))
        {
            text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').TrimEnd('\0').Trim();
        }
        if (text.StartsWith("cut\n", StringComparison.Ordinal) || text.StartsWith("copy\n", StringComparison.Ordinal))
        {
            ParseStateValue(text, content);
        }
    }

    /// <summary>
    /// Windows 后台探测：专用 STA 线程走 OLE 剪贴板（兼容资源管理器延迟渲染的 CF_HDROP），
    /// 调用方最多等 200 毫秒；超时按不可用返回，界面线程完全不受影响。
    /// </summary>
    private static ClipboardPresence ProbeOnWindowsWorker()
    {
        ProbeHolder holder = new ProbeHolder();
        Thread worker = new Thread(new ThreadStart(delegate
        {
            holder.Value = ProbeWithOleOnSta();
        }));
        worker.IsBackground = true;
        worker.Name = "剪贴板探测";
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!worker.Join(200))
        {
            return new ClipboardPresence
            {
                ClipboardAvailable = false,
                FormatDescription = "剪贴板探测超时（其他进程可能正持有剪贴板）",
            };
        }
        return holder.Value ?? new ClipboardPresence
        {
            ClipboardAvailable = false,
            FormatDescription = "剪贴板探测失败",
        };
    }

    private sealed class ProbeHolder
    {
        internal ClipboardPresence? Value;
    }

    /// <summary>在 STA 线程上用 OLE 读取剪贴板文件列表；退出前释放 OLE。</summary>
    private static ClipboardPresence ProbeWithOleOnSta()
    {
        ClipboardPresence presence = new ClipboardPresence();
        int initResult = OleInitialize(IntPtr.Zero);
        try
        {
            object dataObject;
            if (OleGetClipboard(out dataObject) != 0 || dataObject == null)
            {
                presence.FormatDescription = "空";
                return presence;
            }
            System.Runtime.InteropServices.ComTypes.IDataObject? obj =
                dataObject as System.Runtime.InteropServices.ComTypes.IDataObject;
            if (obj == null)
            {
                presence.FormatDescription = "剪贴板格式不可读";
                return presence;
            }
            int fileCount = TryGetDropCount(obj);
            bool hasState = TryHasFormat(obj, RegisterClipboardFormatW("tabtaskexplorer-cut"));
            presence.FileCount = fileCount;
            presence.HasFileData = hasState || fileCount > 0;
            presence.FormatDescription = presence.HasFileData
                ? (fileCount > 0 ? "文件列表（CF_HDROP）" : "应用状态标记（文件路径）")
                : "非文件（文本或图片）";
        }
        catch (Exception error)
        {
            presence.FormatDescription = "读取失败";
            UiLogger.LogError("系统剪贴板", "后台探测剪贴板失败", error);
        }
        finally
        {
            if (initResult >= 0)
            {
                OleUninitialize();
            }
        }
        return presence;
    }

    private static int TryGetDropCount(System.Runtime.InteropServices.ComTypes.IDataObject obj)
    {
        System.Runtime.InteropServices.ComTypes.FORMATETC format =
            new System.Runtime.InteropServices.ComTypes.FORMATETC
        {
            cfFormat = (short)CF_HDROP,
            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
        };
        System.Runtime.InteropServices.ComTypes.STGMEDIUM medium;
        try
        {
            obj.GetData(ref format, out medium);
        }
        catch (Exception)
        {
            return 0;
        }
        try
        {
            return (int)DragQueryFileW(medium.unionmember, 0xFFFFFFFF, null, 0);
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static bool TryHasFormat(System.Runtime.InteropServices.ComTypes.IDataObject obj, uint format)
    {
        if (format == 0)
        {
            return false;
        }
        System.Runtime.InteropServices.ComTypes.FORMATETC query =
            new System.Runtime.InteropServices.ComTypes.FORMATETC
        {
            cfFormat = (short)format,
            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
        };
        try
        {
            return obj.QueryGetData(ref query) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>探测剪贴板文件数据；纯文本 / 图片不视为可粘贴文件。</summary>
    private static async Task<ClipboardPresence> ProbeOnUIThreadAsync()
    {
        ClipboardPresence presence = new ClipboardPresence();
        TopLevel? owner = GetOwnerTopLevel();
        IClipboard? clipboard = owner == null ? null : owner.Clipboard;
        if (clipboard == null)
        {
            presence.FormatDescription = "剪贴板不可用";
            return presence;
        }
        try
        {
            using (IAsyncDataTransfer? transfer = await clipboard.TryGetDataAsync())
            {
                if (transfer == null)
                {
                    presence.FormatDescription = "空";
                    return presence;
                }
                int stateCount = 0;
                string? stateValue = await transfer.TryGetValueAsync(StateFormat);
                if (!string.IsNullOrEmpty(stateValue))
                {
                    string[] lines = stateValue.Split('\n');
                    stateCount = lines.Length > 1 ? lines.Length - 1 : 0;
                }
                IStorageItem[]? files = await transfer.TryGetFilesAsync();
                int fileCount = files == null ? 0 : files.Length;
                presence.FileCount = Math.Max(stateCount, fileCount);
                presence.HasFileData = presence.FileCount > 0;
                presence.FormatDescription = presence.HasFileData
                    ? (fileCount > 0 ? "文件列表（CF_HDROP）" : "应用状态标记（文件路径）")
                    : "非文件（文本或图片）";
            }
        }
        catch (Exception error)
        {
            presence.FormatDescription = "读取失败";
            UiLogger.LogError("系统剪贴板", "探测剪贴板文件数据失败", error);
        }
        return presence;
    }

    /// <summary>组装我方状态标记文本：首行是剪切或复制，其余行是全部路径。</summary>
    private static string BuildStateValue(IReadOnlyList<string> paths, bool isCut)
    {
        List<string> lines = new List<string>();
        lines.Add(isCut ? "cut" : "copy");
        foreach (string path in paths)
        {
            lines.Add(path);
        }
        return string.Join("\n", lines);
    }

    /// <summary>解析我方状态标记文本：还原剪切 / 复制方式与路径列表，格式不对时保持为空。</summary>
    private static void ParseStateValue(string stateValue, ClipboardContent content)
    {
        string[] lines = stateValue.Split('\n');
        if (lines.Length < 2)
        {
            UiLogger.Log("系统剪贴板", "解析状态标记：内容=" + stateValue.Replace("\n", " | ")
                + " 结果=失败，行数不足，状态标记路径保持为空");
            return;
        }
        string mode = lines[0].Trim();
        if (mode != "cut" && mode != "copy")
        {
            UiLogger.Log("系统剪贴板", "解析状态标记：首行=" + mode + " 结果=失败，非剪切或复制标记，状态标记路径保持为空");
            return;
        }
        content.StateIsCut = mode == "cut";
        List<string> paths = new List<string>();
        for (int index = 1; index < lines.Length; index++)
        {
            string path = lines[index].Trim();
            if (path.Length > 0)
            {
                paths.Add(path);
            }
        }
        content.StatePaths = paths;
    }

    /// <summary>
    /// 追加各平台的剪切标记，让外部程序粘贴时按移动处理：
    /// Windows 写首选放置效果，Linux 写 GNOME 与 KDE 的标记，macOS 系统本身没有文件剪切语义（外部一律复制）。
    /// </summary>
    private static void AddPlatformCutMarkers(DataTransfer transfer, IReadOnlyList<string> paths, bool isCut)
    {
        if (OperatingSystem.IsWindows())
        {
            if (isCut)
            {
                transfer.Add(DataTransferItem.Create(WindowsPreferredDropEffectFormat, WindowsMoveEffectBytes));
            }
            UiLogger.Log("系统剪贴板", "附加平台剪切标记：平台=Windows 剪切标记=" + (isCut ? "首选放置效果=移动" : "未写入（按复制处理）")
                + " 结果=成功");
            return;
        }
        if (OperatingSystem.IsLinux())
        {
            List<string> fileUris = new List<string>();
            foreach (string path in paths)
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    fileUris.Add(new Uri(Path.GetFullPath(path)).AbsoluteUri);
                }
            }
            string gnomeValue = (isCut ? "cut" : "copy") + "\n" + string.Join("\n", fileUris);
            transfer.Add(DataTransferItem.Create(LinuxGnomeCopiedFilesFormat, gnomeValue));
            if (isCut)
            {
                transfer.Add(DataTransferItem.Create(LinuxKdeCutSelectionFormat, "1"));
            }
            UiLogger.Log("系统剪贴板", "附加平台剪切标记：平台=Linux 剪切标记=" + (isCut ? "GNOME 标记=cut 且 KDE 标记=1" : "GNOME 标记=copy")
                + " 计入标记的文件地址数=" + fileUris.Count + " 结果=成功");
            return;
        }
        UiLogger.Log("系统剪贴板", "附加平台剪切标记：平台=macOS 剪切标记=无（系统没有文件剪切语义，外部程序一律按复制粘贴） 结果=成功");
    }

    /// <summary>读取当前平台外部程序留下的剪切标记；没有标记时返回 false。</summary>
    private static async Task<bool> ReadPlatformCutIndicatedAsync(IAsyncDataTransfer transfer)
    {
        if (OperatingSystem.IsWindows())
        {
            byte[]? effectBytes = await transfer.TryGetValueAsync(WindowsPreferredDropEffectFormat);
            if (effectBytes == null || effectBytes.Length < 4)
            {
                return false;
            }
            uint effect = BitConverter.ToUInt32(effectBytes, 0);
            return effect == 2;
        }
        if (OperatingSystem.IsLinux())
        {
            string? gnomeValue = await transfer.TryGetValueAsync(LinuxGnomeCopiedFilesFormat);
            if (!string.IsNullOrEmpty(gnomeValue))
            {
                string firstLine = gnomeValue.Split('\n')[0].Trim();
                if (firstLine == "cut")
                {
                    return true;
                }
                if (firstLine == "copy")
                {
                    return false;
                }
            }
            string? kdeValue = await transfer.TryGetValueAsync(LinuxKdeCutSelectionFormat);
            return !string.IsNullOrEmpty(kdeValue) && kdeValue.Trim() == "1";
        }
        return false;
    }

    /// <summary>把本机路径转成系统存储项；路径不存在或转换出错时记日志并返回空。</summary>
    private static async Task<IStorageItem?> TryCreateStorageItemAsync(TopLevel owner, string path)
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
            UiLogger.Log("系统剪贴板", "路径转系统存储项：路径=" + path + " 结果=失败，磁盘上不存在该项，该项不写入系统文件列表");
        }
        catch (Exception error)
        {
            UiLogger.LogError("系统剪贴板", "路径转系统存储项：路径=" + path + " 结果=失败，该项不写入系统文件列表", error);
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

    /// <summary>取承载系统剪贴板的顶层窗口：桌面端用主窗口，取不到时返回空。</summary>
    private static TopLevel? GetOwnerTopLevel()
    {
        if (Application.Current == null)
        {
            return null;
        }
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    /// <summary>Windows 文件拖放格式 CF_HDROP（Win32 常量 15）。</summary>
    private const uint CF_HDROP = 15;

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();

    [DllImport("ole32.dll")]
    private static extern int OleGetClipboard([MarshalAs(UnmanagedType.Interface)] out object dataObject);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);
}
