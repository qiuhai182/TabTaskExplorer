using System;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>远程连接协议类型。</summary>
public enum RemoteProtocol
{
    /// <summary>SSH / SFTP 协议（默认端口 22）。</summary>
    Ssh = 0,

    /// <summary>FTP 协议（默认端口 21）。</summary>
    Ftp = 1,
}

/// <summary>远程连接认证方式。</summary>
public enum RemoteAuthType
{
    /// <summary>用户名 + 密码。</summary>
    Password = 0,

    /// <summary>私钥文件。</summary>
    PrivateKey = 1,
}

/// <summary>
/// 远程主机连接配置：连接管理模块的领域模型。
/// 密码等敏感凭据只保存经 ISecretProtector 处理后的密文，模型本身不持有明文。
/// </summary>
public class RemoteConnectionProfile
{
    /// <summary>唯一标识（新增时自动生成）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>连接显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>连接协议。</summary>
    public RemoteProtocol Protocol { get; set; } = RemoteProtocol.Ssh;

    /// <summary>主机地址（IP 或域名）。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>端口号；0 表示使用协议默认端口。</summary>
    public int Port { get; set; }

    /// <summary>登录用户名。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>认证方式。</summary>
    public RemoteAuthType AuthType { get; set; } = RemoteAuthType.Password;

    /// <summary>受保护的凭据密文（Windows 上为 DPAPI 密文；非 Windows 平台为明文降级）。</summary>
    public string ProtectedSecret { get; set; } = string.Empty;

    /// <summary>私钥文件完整路径（认证方式为私钥时使用）。</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>最近修改时间。</summary>
    public DateTime ModifiedTime { get; set; }

    /// <summary>协议默认端口：SSH 为 22，FTP 为 21。</summary>
    public int GetDefaultPort()
    {
        if (this.Protocol == RemoteProtocol.Ftp)
        {
            return 21;
        }
        return 22;
    }

    /// <summary>实际使用端口：未设置时回退到协议默认端口。</summary>
    public int GetEffectivePort()
    {
        if (this.Port > 0)
        {
            return this.Port;
        }
        return this.GetDefaultPort();
    }

    /// <summary>列表展示用的摘要文本：协议 用户名@主机:端口。</summary>
    public string Summary
    {
        get
        {
            string protocolTag = this.Protocol == RemoteProtocol.Ftp ? "FTP" : "SSH";
            return protocolTag + " " + this.Username + "@" + this.Host + ":" + this.GetEffectivePort();
        }
    }
}
