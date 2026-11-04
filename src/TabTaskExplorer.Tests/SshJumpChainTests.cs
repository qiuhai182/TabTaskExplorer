using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using Renci.SshNet.Common;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using TabTaskExplorer.Kernel.Storage;
using TabTaskExplorer.Protocol.Ssh;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>ProxyJump 跳板链：模型校验、默认端口、逐跳数量上限与错误映射测试。</summary>
public class SshJumpChainTests
{
    private static RemoteJumpHop CreateHop(string host, string user, int port = 0)
    {
        RemoteJumpHop hop = new RemoteJumpHop();
        hop.Host = host;
        hop.Username = user;
        hop.Port = port;
        return hop;
    }

    private static RemoteConnectionProfile CreateProfile(params RemoteJumpHop[] hops)
    {
        RemoteConnectionProfile profile = new RemoteConnectionProfile();
        profile.Name = "目标主机";
        profile.Host = "10.0.0.5";
        profile.Username = "root";
        profile.JumpHops = new List<RemoteJumpHop>(hops);
        return profile;
    }

    [Fact]
    public void EmptyJumpChainMeansDirectConnection()
    {
        RemoteConnectionProfile profile = CreateProfile();

        Assert.False(profile.HasJumpHops);
        Assert.Null(profile.ValidateJumpChain());
        Assert.Equal(string.Empty, profile.JumpSummary);
    }

    [Fact]
    public void JumpHopUsesDefaultPortAndSummary()
    {
        RemoteJumpHop hop = CreateHop("10.0.0.1", "jump", 0);

        Assert.Equal(22, hop.GetEffectivePort());
        Assert.Equal("jump@10.0.0.1:22", hop.Summary);
    }

    [Fact]
    public void JumpSummaryListsHopsInOrder()
    {
        RemoteConnectionProfile profile = CreateProfile(
            CreateHop("10.0.0.1", "a", 2200), CreateHop("10.0.0.2", "b", 22));

        Assert.Equal("a@10.0.0.1:2200 → b@10.0.0.2:22 → root@10.0.0.5:22", profile.JumpSummary);
        Assert.Null(profile.ValidateJumpChain());
    }

    [Fact]
    public void MoreThanThreeHopsIsRejected()
    {
        RemoteConnectionProfile profile = CreateProfile(
            CreateHop("10.0.0.1", "a"), CreateHop("10.0.0.2", "b"),
            CreateHop("10.0.0.3", "c"), CreateHop("10.0.0.4", "d"));

        string? error = profile.ValidateJumpChain();

        Assert.NotNull(error);
        Assert.Contains("最多 3 跳", error);
    }

    [Fact]
    public void MissingHopFieldsIdentifyHopNumber()
    {
        RemoteConnectionProfile missingHost = CreateProfile(CreateHop(string.Empty, "a"));
        Assert.Contains("第 1 跳缺少主机地址", missingHost.ValidateJumpChain());

        RemoteConnectionProfile missingUser = CreateProfile(
            CreateHop("10.0.0.1", "a"), CreateHop("10.0.0.2", string.Empty));
        Assert.Contains("第 2 跳缺少用户名", missingUser.ValidateJumpChain());
    }

    [Fact]
    public void PrivateKeyHopRequiresKeyPath()
    {
        RemoteJumpHop hop = CreateHop("10.0.0.1", "a");
        hop.AuthType = RemoteAuthType.PrivateKey;
        RemoteConnectionProfile profile = CreateProfile(hop);

        Assert.Contains("第 1 跳", profile.ValidateJumpChain());
        Assert.Contains("私钥路径", profile.ValidateJumpChain());

        hop.PrivateKeyPath = "/home/a/.ssh/id_rsa";
        Assert.Null(profile.ValidateJumpChain());
    }

    [Fact]
    public void ConnectRejectsMoreThanThreeHopsBeforeNetworkAccess()
    {
        RemoteConnectionProfile profile = CreateProfile(
            CreateHop("10.0.0.1", "a"), CreateHop("10.0.0.2", "b"),
            CreateHop("10.0.0.3", "c"), CreateHop("10.0.0.4", "d"));
        List<SshJumpHopCredential> hops = new List<SshJumpHopCredential>();
        foreach (RemoteJumpHop hop in profile.JumpHops)
        {
            hops.Add(new SshJumpHopCredential(hop, "secret"));
        }

        ArgumentException error = Assert.Throws<ArgumentException>(delegate
        {
            SshJumpChain.Connect(profile, "secret", hops, null, null);
        });
        Assert.Contains("最多 3 跳", error.Message);
    }

    [Fact]
    public void FailureStageMappingDistinguishesAuthenticationAndNetwork()
    {
        Assert.Equal("认证", SshJumpChain.MapFailureStage(new SshAuthenticationException("认证失败")));
        Assert.Equal("超时", SshJumpChain.MapFailureStage(new SshOperationTimeoutException("超时")));
        Assert.Equal("网络", SshJumpChain.MapFailureStage(new SocketException(10061)));
        Assert.Equal("连接", SshJumpChain.MapFailureStage(new InvalidOperationException("状态异常")));
    }

    [Fact]
    public void RemoteHopExceptionCarriesHopIndexAndElapsed()
    {
        Exception inner = new SshAuthenticationException("认证失败");
        RemoteHopException error = new RemoteHopException(2, "10.0.0.2", 2222, "认证", 1234, inner);

        Assert.Equal(2, error.HopIndex);
        Assert.Equal("10.0.0.2", error.Host);
        Assert.Equal(2222, error.Port);
        Assert.Equal("认证", error.Stage);
        Assert.Equal(1234, error.ElapsedMilliseconds);
        Assert.Contains("第 2 跳", error.Message);
        Assert.Contains("10.0.0.2:2222", error.Message);
        Assert.Contains("1234 毫秒", error.Message);
    }

    [Fact]
    public void HostKeyExceptionsAnnotateHopNumber()
    {
        HostKeyUnknownException unknown = new HostKeyUnknownException("10.0.0.1", 22, "ssh-ed25519",
            "AAA", null, 1);
        HostKeyMismatchException mismatch = new HostKeyMismatchException("10.0.0.2", 22, "ssh-ed25519",
            "AAA", "BBB", null, 2);

        Assert.Equal(1, unknown.HopIndex);
        Assert.Contains("第 1 跳", unknown.Message);
        Assert.Equal(2, mismatch.HopIndex);
        Assert.Contains("第 2 跳", mismatch.Message);
    }

    [Fact]
    public void ConnectionManagerRejectsInvalidJumpChain()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-connections.json");
        try
        {
            ConnectionManager manager = new ConnectionManager(new JsonConnectionStore(tempFile),
                new PlaintextSecretProtector(), new TcpConnectivityTester());
            RemoteConnectionProfile profile = CreateProfile(
                CreateHop("10.0.0.1", "a"), CreateHop("10.0.0.2", "b"),
                CreateHop("10.0.0.3", "c"), CreateHop("10.0.0.4", "d"));

            ArgumentException error = Assert.Throws<ArgumentException>(delegate
            {
                manager.Add(profile);
            });
            Assert.Contains("最多 3 跳", error.Message);

            profile.JumpHops.RemoveAt(3);
            manager.Add(profile);
            Assert.Single(manager.GetAll());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void JumpSecretsAreEncryptedAndReadBackPerHop()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-connections.json");
        try
        {
            ConnectionManager manager = new ConnectionManager(new JsonConnectionStore(tempFile),
                new PlaintextSecretProtector(), new TcpConnectivityTester());
            RemoteConnectionProfile profile = CreateProfile(CreateHop("10.0.0.1", "a"));
            manager.Add(profile);

            manager.SetJumpSecret(profile, 0, "hop-password");
            manager.Update(profile);

            Assert.Equal("hop-password", manager.GetJumpSecret(profile, 0));
            Assert.Equal("hop-password", manager.GetJumpSecret(manager.GetAll()[0], 0));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
