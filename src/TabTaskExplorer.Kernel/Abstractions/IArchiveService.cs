using System;
using System.Collections.Generic;
using System.Threading;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 压缩包服务：探测、列出、解压、创建与修改压缩包。
/// 内核跨平台实现（SharpCompress 托管引擎 + 自研 tar 原始字节引擎），
/// 界面与其他层只依赖本接口，不接触具体引擎。
/// </summary>
public interface IArchiveService
{
    /// <summary>文件路径是否具有已知压缩包扩展名（双击拦截判定，不读文件内容）。</summary>
    bool IsArchivePath(string path);

    /// <summary>
    /// 探测压缩包格式（扩展名 + 文件头魔数校验）。
    /// 文件不存在、扩展名未知或内容与扩展名不符时返回 null。
    /// </summary>
    ArchiveFormatInfo? Probe(string archivePath);

    /// <summary>列出压缩包全部条目（含目录；目录名以 '/' 结尾的条目归一化为 IsDirectory）。</summary>
    IReadOnlyList<ArchiveEntry> ListEntries(string archivePath, ArchiveListOptions? options = null);

    /// <summary>解压压缩包到指定目录（进度逐条上报，可取消）。</summary>
    void Extract(string archivePath, ArchiveExtractOptions options,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>解压指定条目（归档内相对路径列表）到指定目录。</summary>
    void ExtractEntries(string archivePath, IReadOnlyList<string> entryNames, ArchiveExtractOptions options,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>创建新压缩包（已存在时覆盖），来源可为文件与文件夹（文件夹递归打包）。</summary>
    void Create(string archivePath, IReadOnlyList<string> sourcePaths, ArchiveCreateOptions options,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向已有压缩包追加文件 / 文件夹（修改压缩包）。
    /// destinationInnerDirectory 指定包内目标子目录（'/' 分隔，空或 null 表示放到包根），
    /// 供文件视图"粘贴进压缩包文件夹"使用。
    /// </summary>
    void AddPaths(string archivePath, IReadOnlyList<string> sourcePaths,
        string? destinationInnerDirectory = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>从压缩包删除条目（文件夹条目连同其下全部条目一并删除）。</summary>
    void RemoveEntries(string archivePath, IReadOnlyList<string> entryNames,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>重命名压缩包内条目（保持数据与其余元数据不变）。</summary>
    void RenameEntry(string archivePath, string entryName, string newName,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>在压缩包内创建空文件夹条目（文件夹视图"新建文件夹"用）。</summary>
    void AddDirectory(string archivePath, string directoryInnerPath,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 压缩包目录边界的导航约束：文件标签页在压缩包根目录时禁止再向上跳转。
/// </summary>
public interface IArchiveNavigationBoundary
{
    /// <summary>给定路径是否正好是该压缩包的根（压缩包文件本身）。</summary>
    bool IsArchiveRoot(string path);
}
