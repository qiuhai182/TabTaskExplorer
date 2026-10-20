using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 编辑器文件访问抽象：屏蔽本机与远程差异，编辑器标签只依赖本接口读写文本，
/// 因此同一套编辑 / 保存 / 脏标记逻辑可同时服务本机文件与 SSH/SFTP 远程文件。
/// </summary>
public interface IEditorFileAccess
{
    /// <summary>是否为远程文件。</summary>
    bool IsRemote { get; }

    /// <summary>文件完整路径（本机为磁盘路径，远程为远端绝对路径）。</summary>
    string FilePath { get; }

    /// <summary>文件名（含扩展名）。</summary>
    string FileName { get; }

    /// <summary>位置显示文字（本机为所在目录，远程为「连接名:目录」）。</summary>
    string LocationText { get; }

    /// <summary>读取文本内容（含编码探测与二进制 / 超大判定）。</summary>
    TextFileContent Read();

    /// <summary>按指定编码把文本写回。</summary>
    void Write(string text, string encodingName);
}

/// <summary>
/// 本机文件访问：走内核 ITextFileService，编码探测完全交给内核。
/// </summary>
public class LocalEditorFileAccess : IEditorFileAccess
{
    private readonly ITextFileService textFileService;

    /// <inheritdoc />
    public bool IsRemote
    {
        get { return false; }
    }

    /// <inheritdoc />
    public string FilePath { get; private set; }

    /// <inheritdoc />
    public string FileName { get; private set; }

    /// <inheritdoc />
    public string LocationText { get; private set; }

    /// <summary>使用内核文本服务与文件路径创建本机文件访问。</summary>
    public LocalEditorFileAccess(ITextFileService textFileService, string filePath)
    {
        this.textFileService = textFileService;
        this.FilePath = filePath;
        this.FileName = Path.GetFileName(filePath);
        string? directory = Path.GetDirectoryName(filePath);
        this.LocationText = directory ?? string.Empty;
    }

    /// <inheritdoc />
    public TextFileContent Read()
    {
        return this.textFileService.Read(this.FilePath);
    }

    /// <inheritdoc />
    public void Write(string text, string encodingName)
    {
        this.textFileService.Write(this.FilePath, text, encodingName);
    }
}

/// <summary>
/// 远程文件访问：走 IFileContentAccess 按字节读写，再借内核 TextFileCodec 做编码探测。
/// </summary>
public class RemoteEditorFileAccess : IEditorFileAccess
{
    private readonly IFileContentAccess contentAccess;
    private readonly string connectionName;

    /// <inheritdoc />
    public bool IsRemote
    {
        get { return true; }
    }

    /// <inheritdoc />
    public string FilePath { get; private set; }

    /// <inheritdoc />
    public string FileName { get; private set; }

    /// <inheritdoc />
    public string LocationText { get; private set; }

    /// <summary>使用远程内容访问能力、远端路径与连接名创建远程文件访问。</summary>
    public RemoteEditorFileAccess(IFileContentAccess contentAccess, string remotePath, string connectionName)
    {
        this.contentAccess = contentAccess;
        this.FilePath = remotePath;
        this.connectionName = connectionName;
        this.FileName = RemotePathUtility.GetFileName(remotePath);
        this.LocationText = connectionName + ":" + RemotePathUtility.GetParentPath(remotePath);
    }

    /// <inheritdoc />
    public TextFileContent Read()
    {
        byte[] bytes = this.contentAccess.ReadAllBytes(this.FilePath);
        return TextFileCodec.Decode(bytes);
    }

    /// <inheritdoc />
    public void Write(string text, string encodingName)
    {
        byte[] bytes = TextFileCodec.Encode(text, encodingName);
        this.contentAccess.WriteAllBytes(this.FilePath, bytes);
    }
}
