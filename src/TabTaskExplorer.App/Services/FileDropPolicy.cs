using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 拖拽源声明的放置语义：由发起拖拽的一方决定，落点必须据此协商，
/// 不能在所有共用代码里假定同一种行为。
/// </summary>
public enum FileDragMode
{
    /// <summary>可移动：同卷移动、跨卷复制，Ctrl 强制复制、Shift 强制移动（目录标签页内的文件与文件夹使用）。</summary>
    MoveOrCopy,

    /// <summary>只读拷贝：无论修饰键与目标在哪，都只复制投递，源产物文件保持原样（截图与录屏产物历史使用）。</summary>
    CopyOnly,
}

/// <summary>本程序发起拖拽时携带的进程内载荷：源路径列表（外部程序拖入时没有这个载荷）。</summary>
public class FileDragPayload
{
    /// <summary>被拖动条目的完整路径列表。</summary>
    public List<string> Paths { get; set; } = new List<string>();

    /// <summary>发起方声明的放置语义，默认按目录标签页的移动语义处理。</summary>
    public FileDragMode Mode { get; set; } = FileDragMode.MoveOrCopy;
}

/// <summary>
/// 文件拖放策略：参与范围、落点合法性与拖放效果的统一判定。
/// 全部是纯路径逻辑，三端（Windows / Linux / macOS）行为完全一致。
/// </summary>
public static class FileDropPolicy
{
    /// <summary>进程内自定义数据格式：本程序发起的拖拽携带它，据此与外部拖入区分。</summary>
    public static readonly DataFormat<FileDragPayload> Format =
        DataFormat.CreateInProcessFormat<FileDragPayload>("tabtaskexplorer-file-drag");

    /// <summary>
    /// 最近一次内部拖放的落点动作（移动 / 复制 / 导航），由各落点的拖放处理器写入：
    /// 拖拽结束后发起方据此记录真实结果，导航落点虽然回报「移动」以让系统接受放置，但并没有真的移动。
    /// </summary>
    public static string LastInternalDropAction { get; set; } = string.Empty;

    /// <summary>该提供者下的条目能否发起拖拽（压缩包虚拟目录不参与拖拽）。</summary>
    public static bool CanDragFrom(IFileSystemProvider provider)
    {
        return provider is not ArchiveFileSystemProvider;
    }

    /// <summary>
    /// 该提供者下的条目能否拖到本机路径目标（系统资源管理器、左侧导航、书签、地址栏、其他标签）。
    /// 只有本机文件系统提供者满足；远程（SFTP / FTP）条目只允许在自己标签内部拖放。
    /// </summary>
    public static bool CanDropToExternal(IFileSystemProvider provider)
    {
        return provider is DefaultFileSystemProvider;
    }

    /// <summary>该提供者下的条目能否在自己标签内部拖放（压缩包虚拟目录不参与）。</summary>
    public static bool CanDropInsideTab(IFileSystemProvider provider)
    {
        return provider is not ArchiveFileSystemProvider;
    }

    /// <summary>
    /// 读取拖放内容中的源路径：本程序发起的拖拽优先取进程内载荷（据此与外部拖入区分），
    /// 只有文件项时取各文件项的本地路径；取不到本地路径的项会被跳过。
    /// </summary>
    /// <param name="dataTransfer">拖放事件携带的数据。</param>
    /// <param name="isInternalDrag">是否为本程序发起的拖拽。</param>
    /// <returns>可用的源路径列表（可能为空）。</returns>
    public static List<string> CollectSourcePaths(IDataTransfer dataTransfer, out bool isInternalDrag)
    {
        return CollectSourcePaths(dataTransfer, out isInternalDrag, out FileDragMode _);
    }

    /// <summary>
    /// 读取拖放内容中的源路径，并取出拖拽源声明的放置语义：
    /// 外部程序拖入时没有进程内载荷，按系统规则处理（可移动或复制）。
    /// </summary>
    /// <param name="dataTransfer">拖放事件携带的数据。</param>
    /// <param name="isInternalDrag">是否为本程序发起的拖拽。</param>
    /// <param name="dragMode">拖拽源声明的放置语义。</param>
    /// <returns>可用的源路径列表（可能为空）。</returns>
    public static List<string> CollectSourcePaths(IDataTransfer dataTransfer, out bool isInternalDrag,
        out FileDragMode dragMode)
    {
        isInternalDrag = false;
        dragMode = FileDragMode.MoveOrCopy;
        List<string> sourcePaths = new List<string>();
        if (dataTransfer == null)
        {
            return sourcePaths;
        }
        FileDragPayload? payload = dataTransfer.TryGetValue(Format);
        if (payload != null && payload.Paths != null && payload.Paths.Count > 0)
        {
            isInternalDrag = true;
            dragMode = payload.Mode;
            foreach (string path in payload.Paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    sourcePaths.Add(path);
                }
            }
            return sourcePaths;
        }
        IStorageItem[]? files = dataTransfer.TryGetFiles();
        if (files == null)
        {
            return sourcePaths;
        }
        foreach (IStorageItem file in files)
        {
            string? localPath = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
            {
                sourcePaths.Add(localPath);
            }
        }
        return sourcePaths;
    }

    /// <summary>
    /// 判断源条目与目标提供者是否匹配，返回空字符串表示允许接收：
    /// 本机目标只接收本机真实存在的源路径（远程源与非文件内容拒绝）；
    /// 远程标签只接收本程序内部发起、且本机不存在的远程源路径（本期不做本机与远程互拖）；
    /// 压缩包虚拟目录一律不参与。
    /// </summary>
    /// <param name="sourcePaths">拖放内容中的源路径。</param>
    /// <param name="isInternalDrag">是否为本程序发起的拖拽。</param>
    /// <param name="targetProvider">目标标签的文件系统提供者。</param>
    /// <returns>空字符串表示允许，否则为中文拒绝原因。</returns>
    public static string CheckSourceTargetMatch(IReadOnlyList<string> sourcePaths, bool isInternalDrag,
        IFileSystemProvider targetProvider)
    {
        if (targetProvider == null)
        {
            return "目标标签没有文件系统提供者";
        }
        if (targetProvider is ArchiveFileSystemProvider)
        {
            return "压缩包虚拟目录不参与拖放";
        }
        if (targetProvider is DefaultFileSystemProvider)
        {
            return CheckLocalTargetSources(sourcePaths);
        }
        if (!isInternalDrag)
        {
            return "外部程序的条目不能拖入远程标签";
        }
        foreach (string sourcePath in sourcePaths)
        {
            if (File.Exists(sourcePath) || Directory.Exists(sourcePath))
            {
                return "本机条目不能拖入远程标签（本期不支持本机与远程互拖）：" + sourcePath;
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// 判断源条目能否进入本机目标目录：每条源路径都必须是本机真实存在的文件或目录。
    /// 左侧导航、书签这类没有标签提供者的本机落点复用本方法。
    /// </summary>
    /// <param name="sourcePaths">拖放内容中的源路径。</param>
    /// <returns>空字符串表示允许，否则为中文拒绝原因。</returns>
    public static string CheckLocalTargetSources(IReadOnlyList<string> sourcePaths)
    {
        foreach (string sourcePath in sourcePaths)
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                return "源条目不是本机真实存在的路径：" + sourcePath;
            }
        }
        return string.Empty;
    }

    /// <summary>判断两个路径是否指向同一个目录（去尾部分隔符，忽略大小写）。</summary>
    public static bool IsSameDirectory(string left, string right)
    {
        string leftTrimmed = TrimDirectorySeparators(left);
        string rightTrimmed = TrimDirectorySeparators(right);
        if (leftTrimmed.Length == 0 || rightTrimmed.Length == 0)
        {
            return false;
        }
        return string.Equals(leftTrimmed, rightTrimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断目标目录是否就是源目录本身，或位于源目录内部（把文件夹拖进它自己的子目录属于非法落点）。</summary>
    public static bool IsSelfOrDescendant(string sourceDirectory, string targetDirectory)
    {
        string source = TrimDirectorySeparators(sourceDirectory);
        string target = TrimDirectorySeparators(targetDirectory);
        if (source.Length == 0 || target.Length == 0)
        {
            return false;
        }
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // 加上分隔符后再比较前缀，避免 "D:\ab" 被误判为 "D:\a" 的子目录
        string sourceWithSeparator = source + Path.DirectorySeparatorChar;
        string sourceWithAltSeparator = source + Path.AltDirectorySeparatorChar;
        return target.StartsWith(sourceWithSeparator, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(sourceWithAltSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断一次拖放落点是否可以接收这些源条目：
    /// 目标目录非空、（本地判定时）目标目录确实存在、目标不是任何源条目的所在目录（空操作）、
    /// 也不把文件夹拖进它自己的子目录。
    /// </summary>
    public static bool IsDropTargetValid(IReadOnlyList<string> sourcePaths, string targetDirectory, bool targetExists)
    {
        if (sourcePaths == null || sourcePaths.Count == 0)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return false;
        }
        if (!targetExists)
        {
            return false;
        }
        foreach (string sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }
            // 源条目所在目录就是目标目录：移动是空操作、复制会自我重复，一律拒绝
            string? parentDirectory = null;
            try
            {
                parentDirectory = Path.GetDirectoryName(sourcePath);
            }
            catch (Exception)
            {
                parentDirectory = null;
            }
            if (!string.IsNullOrEmpty(parentDirectory) && IsSameDirectory(parentDirectory, targetDirectory))
            {
                return false;
            }
            // 只有本机真实目录才能探测；源与目标同列表时必然是兄弟关系，不会误判
            if (Directory.Exists(sourcePath) && IsSelfOrDescendant(sourcePath, targetDirectory))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 计算拖放效果：默认同卷移动、跨卷复制；按住 Ctrl 强制复制，按住 Shift 强制移动（Ctrl 优先）。
    /// </summary>
    public static DragDropEffects ResolveEffects(string sourcePath, string targetDirectory, KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            return DragDropEffects.Copy;
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            return DragDropEffects.Move;
        }
        if (IsSameVolume(sourcePath, targetDirectory))
        {
            return DragDropEffects.Move;
        }
        return DragDropEffects.Copy;
    }

    /// <summary>
    /// 按拖拽源声明的语义计算拖放效果：只读拷贝源一律复制（不看修饰键、不看卷），
    /// 其余源沿用同卷移动、跨卷复制与 Ctrl / Shift 覆盖规则。
    /// </summary>
    public static DragDropEffects ResolveEffects(string sourcePath, string targetDirectory, KeyModifiers modifiers,
        FileDragMode dragMode)
    {
        if (dragMode == FileDragMode.CopyOnly)
        {
            return DragDropEffects.Copy;
        }
        return ResolveEffects(sourcePath, targetDirectory, modifiers);
    }

    /// <summary>把拖拽源语义转成中文说明，用于日志。</summary>
    public static string DescribeDragMode(FileDragMode dragMode)
    {
        if (dragMode == FileDragMode.CopyOnly)
        {
            return "只读拷贝（源文件不移动、不改名、不删除）";
        }
        return "移动或复制";
    }

    /// <summary>把本机路径转成系统存储项（拖拽时携带文件项用，外部程序据此收到 CF_HDROP 文件列表），
    /// 路径不存在或转换出错时记日志并返回空。</summary>
    /// <param name="owner">所属顶层窗口，用于取系统存储提供者。</param>
    /// <param name="path">条目的本机完整路径。</param>
    /// <returns>系统存储项；失败时为空。</returns>
    public static async Task<IStorageItem?> TryCreateStorageItemAsync(TopLevel owner, string path)
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
        }
        catch (Exception error)
        {
            UiLogger.LogError("程序行为", "拖拽准备：条目路径=" + path + " 结果=失败，转换系统存储项时出错", error);
        }
        return null;
    }

    /// <summary>把路径转成文件协议 URI：本身不是合法绝对文件路径时补上 file:// 前缀。</summary>
    public static Uri CreateFileUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && uri.IsFile)
        {
            return uri;
        }
        return new Uri("file://" + path);
    }

    /// <summary>判断两个路径是否位于同一个卷（根路径相同视为同卷，取不到根时按跨卷处理）。</summary>
    public static bool IsSameVolume(string sourcePath, string targetDirectory)
    {
        try
        {
            string? sourceRoot = Path.GetPathRoot(sourcePath);
            string? targetRoot = Path.GetPathRoot(targetDirectory);
            if (string.IsNullOrEmpty(sourceRoot) || string.IsNullOrEmpty(targetRoot))
            {
                return false;
            }
            return string.Equals(TrimDirectorySeparators(sourceRoot), TrimDirectorySeparators(targetRoot),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>把拖放效果转成中文说明，用于日志。</summary>
    public static string DescribeEffects(DragDropEffects effects)
    {
        if ((effects & DragDropEffects.Move) != 0 && (effects & DragDropEffects.Copy) != 0)
        {
            return "移动或复制";
        }
        if ((effects & DragDropEffects.Move) != 0)
        {
            return "移动";
        }
        if ((effects & DragDropEffects.Copy) != 0)
        {
            return "复制";
        }
        if ((effects & DragDropEffects.Link) != 0)
        {
            return "创建快捷方式";
        }
        if (effects == DragDropEffects.None)
        {
            return "禁止放置";
        }
        return effects.ToString();
    }

    /// <summary>把修饰键转成中文说明，用于日志。</summary>
    public static string DescribeModifiers(KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.None)
        {
            return "无";
        }
        List<string> names = new List<string>();
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            names.Add("Ctrl");
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            names.Add("Shift");
        }
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            names.Add("Alt");
        }
        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            names.Add("Meta");
        }
        if (names.Count == 0)
        {
            return modifiers.ToString();
        }
        return string.Join("+", names);
    }

    /// <summary>去掉路径尾部分隔符（盘根 "C:\" 与 Unix 根 "/" 保留为统一形式，避免自身递归）。</summary>
    private static string TrimDirectorySeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }
        char separator = Path.DirectorySeparatorChar;
        char altSeparator = Path.AltDirectorySeparatorChar;
        string? root = null;
        try
        {
            root = Path.GetPathRoot(path);
        }
        catch (Exception)
        {
            root = null;
        }
        string rootTrimmed = string.Empty;
        if (!string.IsNullOrEmpty(root))
        {
            rootTrimmed = root.TrimEnd(separator, altSeparator);
        }
        string trimmed = path.TrimEnd(separator, altSeparator);
        if (trimmed.Length == 0)
        {
            // 路径全部是分隔符，例如 "/"，此时以根路径为准
            if (string.IsNullOrEmpty(root))
            {
                return path;
            }
            return rootTrimmed.Length == 0 ? root : rootTrimmed;
        }
        if (rootTrimmed.Length > 0 && string.Equals(trimmed, rootTrimmed, StringComparison.OrdinalIgnoreCase))
        {
            // 盘根：让 "C:\" 与 "C:/" 视为同一目录
            return rootTrimmed;
        }
        return trimmed;
    }
}
