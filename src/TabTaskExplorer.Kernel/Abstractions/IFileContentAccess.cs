namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 文件内容读写能力：支持该能力的文件系统提供者可以按字节读写整个文件。
/// 远程文件在线编辑依赖本接口；实现方应在后台线程被调用。
/// </summary>
public interface IFileContentAccess
{
    /// <summary>读取指定路径的完整文件内容。</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>把完整内容写回指定路径（覆盖）。</summary>
    void WriteAllBytes(string path, byte[] content);
}

/// <summary>
/// 远程权限修改能力：支持该能力的文件系统提供者可以修改文件的 POSIX 权限位。
/// 权限用八进制数表示，例如 0644（所有者读写、其余只读）。
/// </summary>
public interface IRemotePermissionChanger
{
    /// <summary>把指定路径的权限改为给定八进制模式（例如 0o755）。</summary>
    void ChangePermissions(string path, int octalMode);
}
