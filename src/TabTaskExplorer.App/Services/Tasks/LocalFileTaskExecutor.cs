using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TabTaskExplorer.App.Services.Tasks;

/// <summary>
/// 本地复制/移动/删除执行器：逐项处理冲突、检查取消、复制先写临时文件再落位。
/// 文件夹冲突支持合并 / 替换 / 逐项处理 / 改名共存；替换时删除目标走回收站。
/// </summary>
public static class LocalFileTaskExecutor
{
    private const int BufferSize = 81920;

    internal static async Task RunAsync(FileTaskEntry entry)
    {
        FileTaskRequest request = entry.Request;
        CancellationToken token = entry.Token;
        if (request.Kind != FileTaskKind.Delete && !Directory.Exists(request.DestinationPath))
        {
            throw new DirectoryNotFoundException("目标目录不存在：" + request.DestinationPath);
        }
        foreach (string source in request.Sources)
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                entry.ReportFailure(source, "源文件或目录不存在");
                continue;
            }
            try
            {
                if (request.Kind == FileTaskKind.Delete)
                {
                    DeletePath(source, true);
                    entry.ReportProcessed();
                    continue;
                }
                string target = Path.Combine(request.DestinationPath, Path.GetFileName(source));
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    entry.ReportSkipped();
                    continue;
                }
                bool directory = Directory.Exists(source);
                if (directory)
                {
                    string sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (Path.GetFullPath(target).StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("不能把目录复制到自身子目录：" + target);
                    }
                }

                TargetDecision? decision = await ResolveTargetAsync(entry, target, directory, token).ConfigureAwait(false);
                if (decision == null)
                {
                    entry.ReportSkipped();
                    continue;
                }

                bool merged = false;
                bool mergeAllCopied = true;
                if (directory)
                {
                    if (decision.MergeContents)
                    {
                        merged = true;
                        mergeAllCopied = await MergeDirectoryAsync(source, decision.Path, entry, token).ConfigureAwait(false);
                    }
                    else
                    {
                        await CopyDirectoryAsync(source, decision.Path, token).ConfigureAwait(false);
                    }
                }
                else
                {
                    await CopyFileAsync(source, decision.Path, token).ConfigureAwait(false);
                }

                if (request.Kind == FileTaskKind.Move)
                {
                    if (merged && !mergeAllCopied)
                    {
                        // 合并过程中有同名项被跳过：保留源，避免剪切丢失被跳过的内容
                        UiLogger.Log("任务中心", "剪切合并未删除源：源=" + source + " 目标=" + decision.Path
                            + " 原因=合并时有同名项被跳过 结果=源保留");
                        entry.ReportSkipped();
                        continue;
                    }
                    DeletePath(source, false);
                }
                entry.ReportProcessed();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                entry.ReportFailure(source, error.Message);
            }
        }
    }

    /// <summary>顶层目标的处理决定：Path 为实际写入路径；MergeContents 表示目标已存在目录并选择合并。</summary>
    private sealed class TargetDecision
    {
        internal string Path = string.Empty;
        internal bool MergeContents;
    }

    /// <summary>返回顶层目标决定；跳过返回 null，取消抛 OperationCanceledException。</summary>
    private static async Task<TargetDecision?> ResolveTargetAsync(FileTaskEntry entry, string target, bool directory,
        CancellationToken token)
    {
        bool targetDirectory = Directory.Exists(target);
        bool targetExists = targetDirectory || File.Exists(target);
        if (!targetExists)
        {
            return new TargetDecision { Path = target };
        }

        FileConflictPolicy policy = entry.Request.ConflictPolicy;
        if (policy == FileConflictPolicy.Skip)
        {
            return null;
        }
        if (policy == FileConflictPolicy.Overwrite)
        {
            DeletePath(target, true);
            return new TargetDecision { Path = target };
        }
        if (policy == FileConflictPolicy.Rename)
        {
            return new TargetDecision { Path = FindAvailableName(target, directory) };
        }
        if (policy == FileConflictPolicy.Merge || policy == FileConflictPolicy.PerItem)
        {
            if (directory && targetDirectory)
            {
                return new TargetDecision { Path = target, MergeContents = true };
            }
            return new TargetDecision { Path = FindAvailableName(target, directory) };
        }

        // Ask：文件夹冲突四项，文件冲突两项；关闭对话框按取消处理
        FileConflictAction action = await entry.RequestResolutionAsync(target, directory && targetDirectory, token)
            .ConfigureAwait(false);
        if (action == FileConflictAction.Skip)
        {
            return null;
        }
        if (action == FileConflictAction.Overwrite)
        {
            DeletePath(target, true);
            return new TargetDecision { Path = target };
        }
        if (action == FileConflictAction.Rename)
        {
            return new TargetDecision { Path = FindAvailableName(target, directory) };
        }
        if (action == FileConflictAction.Merge || action == FileConflictAction.PerItem)
        {
            if (directory && targetDirectory)
            {
                return new TargetDecision { Path = target, MergeContents = true };
            }
            return new TargetDecision { Path = FindAvailableName(target, directory) };
        }
        throw new OperationCanceledException(token);
    }

    /// <summary>合并目录时处理文件夹内同名项：返回目标决定，跳过返回 null。</summary>
    private static async Task<TargetDecision?> ResolveNestedAsync(FileTaskEntry entry, string target,
        bool sourceIsDirectory, CancellationToken token)
    {
        bool targetDirectory = Directory.Exists(target);
        bool targetExists = targetDirectory || File.Exists(target);
        if (!targetExists)
        {
            return new TargetDecision { Path = target };
        }
        bool directoryConflict = sourceIsDirectory && targetDirectory;
        FileConflictAction action = await entry.RequestResolutionAsync(target, directoryConflict, token)
            .ConfigureAwait(false);
        if (action == FileConflictAction.Skip)
        {
            return null;
        }
        if (action == FileConflictAction.Overwrite)
        {
            DeletePath(target, true);
            return new TargetDecision { Path = target };
        }
        if (action == FileConflictAction.Merge || action == FileConflictAction.PerItem)
        {
            if (directoryConflict)
            {
                return new TargetDecision { Path = target, MergeContents = true };
            }
            return new TargetDecision { Path = FindAvailableName(target, sourceIsDirectory) };
        }
        if (action == FileConflictAction.Rename)
        {
            return new TargetDecision { Path = FindAvailableName(target, sourceIsDirectory) };
        }
        throw new OperationCanceledException(token);
    }

    private static string FindAvailableName(string target, bool directory)
    {
        string? parent = Path.GetDirectoryName(target);
        string baseName = directory ? Path.GetFileName(target) : Path.GetFileNameWithoutExtension(target);
        string extension = directory ? string.Empty : Path.GetExtension(target);
        for (int index = 1; index < 10000; index++)
        {
            string candidate = Path.Combine(parent ?? string.Empty, baseName + " (" + index + ")" + extension);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException("无法生成不重复的目标名称：" + target);
    }

    private static async Task CopyFileAsync(string source, string target, CancellationToken token)
    {
        string temporary = target + ".ttex-tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[BufferSize];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                }
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
        }
        catch
        {
            TryDeleteFile(temporary);
            throw;
        }
    }

    /// <summary>把源目录整体复制到不存在的目标目录（先写临时目录再落位）。</summary>
    private static async Task CopyDirectoryAsync(string source, string target, CancellationToken token)
    {
        string temporary = target + ".ttex-tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(temporary);
            await CopyDirectoryContentsAsync(source, temporary, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            Directory.Move(temporary, target);
        }
        catch
        {
            TryDeleteDirectory(temporary);
            throw;
        }
    }

    private static async Task CopyDirectoryContentsAsync(string source, string target, CancellationToken token)
    {
        foreach (string file in Directory.EnumerateFiles(source))
        {
            token.ThrowIfCancellationRequested();
            string destination = Path.Combine(target, Path.GetFileName(file));
            await CopyFileDirectAsync(file, destination, token).ConfigureAwait(false);
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            token.ThrowIfCancellationRequested();
            string destination = Path.Combine(target, Path.GetFileName(directory));
            Directory.CreateDirectory(destination);
            await CopyDirectoryContentsAsync(directory, destination, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 递归合并源目录到已存在的目标目录：两边独有内容都保留；同名文件按「替换 / 改名共存」询问，
    /// 同名文件夹按「合并 / 替换 / 逐项处理 / 改名共存」询问。返回是否所有同名项都已完成复制（有跳过则 false）。
    /// </summary>
    private static async Task<bool> MergeDirectoryAsync(string source, string target, FileTaskEntry entry,
        CancellationToken token)
    {
        Directory.CreateDirectory(target);
        bool allCopied = true;
        int copiedCount = 0;
        foreach (string file in Directory.EnumerateFiles(source))
        {
            token.ThrowIfCancellationRequested();
            string destination = Path.Combine(target, Path.GetFileName(file));
            TargetDecision? resolved = await ResolveNestedAsync(entry, destination, false, token).ConfigureAwait(false);
            if (resolved == null)
            {
                allCopied = false;
                entry.ReportNestedProgress(file, copiedCount);
                continue;
            }
            await CopyFileAsync(file, resolved.Path, token).ConfigureAwait(false);
            copiedCount++;
            entry.ReportNestedProgress(file, copiedCount);
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            token.ThrowIfCancellationRequested();
            string destination = Path.Combine(target, Path.GetFileName(directory));
            bool destinationIsDirectory = Directory.Exists(destination);
            bool destinationExists = destinationIsDirectory || File.Exists(destination);
            if (!destinationExists)
            {
                await CopyDirectoryAsync(directory, destination, token).ConfigureAwait(false);
                copiedCount++;
                entry.ReportNestedProgress(directory, copiedCount);
                continue;
            }

            TargetDecision? resolved = await ResolveNestedAsync(entry, destination, true, token).ConfigureAwait(false);
            if (resolved == null)
            {
                allCopied = false;
                entry.ReportNestedProgress(directory, copiedCount);
                continue;
            }
            if (resolved.MergeContents)
            {
                bool nestedAllCopied = await MergeDirectoryAsync(directory, resolved.Path, entry, token).ConfigureAwait(false);
                allCopied = allCopied && nestedAllCopied;
            }
            else
            {
                await CopyDirectoryAsync(directory, resolved.Path, token).ConfigureAwait(false);
                copiedCount++;
            }
            entry.ReportNestedProgress(directory, copiedCount);
        }
        return allCopied;
    }

    private static async Task CopyFileDirectAsync(string source, string target, CancellationToken token)
    {
        using FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[BufferSize];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }

    /// <summary>删除路径；recycle 为真且运行在 Windows 时优先移入回收站，失败回落永久删除。</summary>
    private static void DeletePath(string path, bool recycle)
    {
        if (recycle && OperatingSystem.IsWindows())
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    return;
                }
                if (File.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    return;
                }
            }
            catch (Exception)
            {
                // 回收站不可用时回落永久删除
            }
        }
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
            return;
        }
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 取消清理失败不覆盖原始异常
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception)
        {
            // 取消清理失败不覆盖原始异常
        }
    }
}
