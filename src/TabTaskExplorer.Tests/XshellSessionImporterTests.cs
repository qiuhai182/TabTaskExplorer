using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using TabTaskExplorer.Kernel.Storage;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>Xshell / Xftp 会话文件解析与已安装配置一键导入测试。</summary>
public class XshellSessionImporterTests
{
    /// <summary>构造使用临时文件的连接管理器（明文凭据降级实现，跨平台可测）。</summary>
    private static ConnectionManager CreateManager(string tempFile)
    {
        JsonConnectionStore store = new JsonConnectionStore(tempFile);
        PlaintextSecretProtector protector = new PlaintextSecretProtector();
        TcpConnectivityTester tester = new TcpConnectivityTester();
        return new ConnectionManager(store, protector, tester);
    }

    /// <summary>创建一个临时 NetSarang 配置根目录（Documents/NetSarang Computer）。</summary>
    private static string CreateHome(string tempRoot)
    {
        string home = Path.Combine(tempRoot, "home");
        Directory.CreateDirectory(Path.Combine(home, "Documents", "NetSarang Computer"));
        return home;
    }

    /// <summary>在指定版本与产品目录下写入会话文件，返回文件完整路径。</summary>
    private static string WriteSession(string home, string version, string kind, string relativePath, string content, Encoding encoding)
    {
        string directory = Path.Combine(home, "Documents", "NetSarang Computer", version, kind, "Sessions");
        Directory.CreateDirectory(directory);
        string filePath = Path.Combine(directory, relativePath);
        string? parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        File.WriteAllText(filePath, content, encoding);
        return filePath;
    }

    [Fact]
    public void ParseXshReadsAuthSectionAndDefaultsPort()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-xshell");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string home = CreateHome(tempRoot);
            string content = "[CONNECTION]\nHost=10.0.0.5\nPort=0\nProtocol=SSH\n"
                + "[CONNECTION:AUTHENTICATION]\nUserName=root\n";
            string filePath = WriteSession(home, "7", "Xshell", "web01.xsh", content, Encoding.Unicode);

            XshellSessionEntry entry = XshellSessionImporter.ParseSessionFile(filePath);
            Assert.NotNull(entry.Profile);
            RemoteConnectionProfile profile = entry.Profile!;
            Assert.Equal("web01", profile.Name);
            Assert.Equal(RemoteProtocol.Ssh, profile.Protocol);
            Assert.Equal("10.0.0.5", profile.Host);
            Assert.Equal(22, profile.Port);
            Assert.Equal("root", profile.Username);
            Assert.Equal(RemoteAuthType.Password, profile.AuthType);
            Assert.Equal(string.Empty, entry.SkipReason);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ParseXfpReadsConnectionSectionOnly()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-xftp");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string home = CreateHome(tempRoot);
            // [Proxy] 节同样含 Host 与 UserName，解析必须只取 [Connection] 节
            string content = "[Connection]\nHost=127.0.0.1\nPort=0\nProtocol=0\nUserName=ftpuser\n"
                + "[Proxy]\nHost=proxy.example.com\nUserName=proxyuser\n";
            string filePath = WriteSession(home, "7", "Xftp", "ftp01.xfp", content, Encoding.UTF8);

            XshellSessionEntry entry = XshellSessionImporter.ParseSessionFile(filePath);
            Assert.NotNull(entry.Profile);
            RemoteConnectionProfile profile = entry.Profile!;
            Assert.Equal("ftp01", profile.Name);
            Assert.Equal(RemoteProtocol.Ftp, profile.Protocol);
            Assert.Equal("127.0.0.1", profile.Host);
            Assert.Equal(21, profile.Port);
            Assert.Equal("ftpuser", profile.Username);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void UnsupportedProtocolIsSkipped()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-telnet");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string home = CreateHome(tempRoot);
            string content = "[CONNECTION]\nHost=10.0.0.9\nProtocol=TELNET\n";
            string filePath = WriteSession(home, "7", "Xshell", "telnet01.xsh", content, Encoding.UTF8);

            XshellSessionEntry entry = XshellSessionImporter.ParseSessionFile(filePath);
            Assert.Null(entry.Profile);
            Assert.Equal("（暂不支持的会话类型）", entry.SkipReason);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void HigherVersionOverridesSameNameAndKeepsSubGroup()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-versions");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string home = CreateHome(tempRoot);
            string contentLow = "[CONNECTION]\nHost=1.1.1.1\nProtocol=SSH\n[CONNECTION:AUTHENTICATION]\nUserName=root\n";
            string contentHigh = "[CONNECTION]\nHost=2.2.2.2\nProtocol=SSH\n[CONNECTION:AUTHENTICATION]\nUserName=root\n";
            WriteSession(home, "6", "Xshell", "web01.xsh", contentLow, Encoding.UTF8);
            WriteSession(home, "7", "Xshell", Path.Combine("生产", "web01.xsh"), contentHigh, Encoding.UTF8);

            XshellDiscoveryResult discovery = XshellSessionImporter.DiscoverInstalledSessions(home);
            Assert.Single(discovery.Sessions);
            Assert.Equal("2.2.2.2", discovery.Sessions[0].Profile!.Host);
            Assert.Equal("生产", discovery.Sessions[0].Group);
            Assert.Single(discovery.RootsScanned);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ImportInstalledKeepsSecretAndIdForSameName()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-upsert");
        string tempFile = Path.Combine(tempRoot, "connections.json");
        Directory.CreateDirectory(tempRoot);
        try
        {
            ConnectionManager manager = CreateManager(tempFile);
            RemoteConnectionProfile existing = new RemoteConnectionProfile();
            existing.Name = "web01";
            existing.Host = "旧地址";
            manager.Add(existing);
            manager.SetSecret(existing, "旧密码");

            string home = CreateHome(tempRoot);
            string content = "[CONNECTION]\nHost=10.0.0.7\nPort=22\nProtocol=SSH\n[CONNECTION:AUTHENTICATION]\nUserName=root\n";
            WriteSession(home, "7", "Xshell", "web01.xsh", content, Encoding.UTF8);

            XshellImportSummary summary = manager.ImportInstalledXshellSessions(home);
            Assert.Equal(0, summary.Imported);
            Assert.Equal(1, summary.Updated);
            Assert.Single(summary.Names);

            IReadOnlyList<RemoteConnectionProfile> all = manager.GetAll();
            Assert.Single(all);
            Assert.Equal(existing.Id, all[0].Id);
            Assert.Equal("10.0.0.7", all[0].Host);
            Assert.Equal("旧密码", manager.GetSecret(all[0]));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
