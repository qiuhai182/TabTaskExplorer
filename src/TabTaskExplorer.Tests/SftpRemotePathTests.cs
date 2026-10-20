using TabTaskExplorer.Protocol.Ssh;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>SFTP 远端路径规整规则测试。</summary>
public class SftpRemotePathTests
{
    /// <summary>空路径规整为根目录。</summary>
    [Theory]
    [InlineData("", "/")]
    [InlineData(" ", "/")]
    [InlineData("/", "/")]
    public void Normalize_ReturnsRootForEmptyInputs(string input, string expected)
    {
        Assert.Equal(expected, SftpFileSystemProvider.NormalizeRemotePath(input));
    }

    /// <summary>相对路径补根斜杠，反斜杠统一为正斜杠，结尾斜杠去除。</summary>
    [Theory]
    [InlineData("home/user", "/home/user")]
    [InlineData("/home/user/", "/home/user")]
    [InlineData("\\home\\user", "/home/user")]
    [InlineData("//home//user//", "/home/user")]
    public void Normalize_CanonicalizesRemotePath(string input, string expected)
    {
        Assert.Equal(expected, SftpFileSystemProvider.NormalizeRemotePath(input));
    }

    /// <summary>父目录取倒数第二段；根目录父目录为自身。</summary>
    [Theory]
    [InlineData("/home/user", "/home")]
    [InlineData("/home", "/")]
    [InlineData("/", "/")]
    public void Parent_ReturnsParentDirectory(string input, string expected)
    {
        Assert.Equal(expected, SftpFileSystemProvider.GetParentRemotePath(input));
    }

    /// <summary>名称取最后一段。</summary>
    [Theory]
    [InlineData("/home/user", "user")]
    [InlineData("/var", "var")]
    public void FileName_ReturnsLastSegment(string input, string expected)
    {
        Assert.Equal(expected, SftpFileSystemProvider.GetRemoteFileName(input));
    }

    /// <summary>根目录拼接不产生双斜杠。</summary>
    [Theory]
    [InlineData("/", "a.txt", "/a.txt")]
    [InlineData("/home", "a.txt", "/home/a.txt")]
    [InlineData("/home/", "a.txt", "/home/a.txt")]
    public void Join_CombinesDirectoryAndName(string directory, string name, string expected)
    {
        Assert.Equal(expected, SftpFileSystemProvider.JoinRemotePath(directory, name));
    }
}
