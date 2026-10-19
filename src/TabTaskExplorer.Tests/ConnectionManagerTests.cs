using System;
using System.Collections.Generic;
using System.IO;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using TabTaskExplorer.Kernel.Storage;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>ConnectionManager 与 JsonConnectionStore 的增删改查、导出导入、凭据回环测试。</summary>
public class ConnectionManagerTests
{
    /// <summary>构造使用临时文件的连接管理器（明文凭据降级实现，跨平台可测）。</summary>
    private static ConnectionManager CreateManager(string tempFile)
    {
        JsonConnectionStore store = new JsonConnectionStore(tempFile);
        PlaintextSecretProtector protector = new PlaintextSecretProtector();
        TcpConnectivityTester tester = new TcpConnectivityTester();
        return new ConnectionManager(store, protector, tester);
    }

    [Fact]
    public void AddAssignsIdAndPersists()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-connections.json");
        try
        {
            ConnectionManager manager = CreateManager(tempFile);

            RemoteConnectionProfile profile = new RemoteConnectionProfile();
            profile.Name = "测试主机";
            profile.Protocol = RemoteProtocol.Ssh;
            profile.Host = "192.168.1.10";
            profile.Username = "root";
            manager.Add(profile);

            Assert.False(string.IsNullOrEmpty(profile.Id));
            Assert.NotEqual(DateTime.MinValue, profile.CreatedTime);

            ConnectionManager reloaded = CreateManager(tempFile);
            IReadOnlyList<RemoteConnectionProfile> all = reloaded.GetAll();
            Assert.Single(all);
            Assert.Equal("测试主机", all[0].Name);
            Assert.Equal("192.168.1.10", all[0].Host);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void UpdateAndDeleteWorkById()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-connections.json");
        try
        {
            ConnectionManager manager = CreateManager(tempFile);

            RemoteConnectionProfile profile = new RemoteConnectionProfile();
            profile.Name = "旧名称";
            profile.Host = "10.0.0.2";
            manager.Add(profile);

            profile.Name = "新名称";
            Assert.True(manager.Update(profile));
            Assert.Equal("新名称", manager.FindById(profile.Id)!.Name);

            Assert.True(manager.Delete(profile.Id));
            Assert.Null(manager.FindById(profile.Id));
            Assert.False(manager.Delete(profile.Id));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SecretRoundTripKeepsPassword()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-connections.json");
        try
        {
            ConnectionManager manager = CreateManager(tempFile);

            RemoteConnectionProfile profile = new RemoteConnectionProfile();
            profile.Name = "凭据回环";
            profile.Host = "example.com";
            manager.Add(profile);

            manager.SetSecret(profile, "p@ssw0rd-明文测试");
            Assert.Equal("p@ssw0rd-明文测试", manager.GetSecret(manager.FindById(profile.Id)!));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportThenImportSkipsDuplicates()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-connections.json");
        string exportFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-export.json");
        try
        {
            ConnectionManager manager = CreateManager(tempFile);

            RemoteConnectionProfile first = new RemoteConnectionProfile();
            first.Name = "主机一";
            first.Host = "192.168.1.1";
            manager.Add(first);

            RemoteConnectionProfile second = new RemoteConnectionProfile();
            second.Name = "主机二";
            second.Host = "192.168.1.2";
            manager.Add(second);

            manager.ExportTo(exportFile);

            // 同一批数据导入：两条编号都重复，应全部跳过
            int importedCount = manager.ImportFrom(exportFile);
            Assert.Equal(0, importedCount);
            Assert.Equal(2, manager.GetAll().Count);

            // 删掉一条后导入：应补回一条
            manager.Delete(first.Id);
            importedCount = manager.ImportFrom(exportFile);
            Assert.Equal(1, importedCount);
            Assert.Equal(2, manager.GetAll().Count);
        }
        finally
        {
            File.Delete(tempFile);
            File.Delete(exportFile);
        }
    }

    [Fact]
    public void PortFallsBackToProtocolDefault()
    {
        RemoteConnectionProfile ssh = new RemoteConnectionProfile();
        ssh.Protocol = RemoteProtocol.Ssh;
        Assert.Equal(22, ssh.GetEffectivePort());

        RemoteConnectionProfile ftp = new RemoteConnectionProfile();
        ftp.Protocol = RemoteProtocol.Ftp;
        Assert.Equal(21, ftp.GetEffectivePort());

        ftp.Port = 2121;
        Assert.Equal(2121, ftp.GetEffectivePort());
    }
}
