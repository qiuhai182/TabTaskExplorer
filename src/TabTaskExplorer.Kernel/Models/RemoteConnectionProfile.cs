using System;
using System.Collections.Generic;

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
/// 跳板机节点：每一跳独立保存主机、端口、账号、认证方式与受保护凭据。
/// 密码/私钥口令密文与主连接使用同一套 ISecretProtector。
/// </summary>
public class RemoteJumpHop
{
    /// <summary>跳板机主机地址。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>跳板机 SSH 端口；0 表示 22。</summary>
    public int Port { get; set; }

    /// <summary>跳板机登录用户名。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>跳板机认证方式。</summary>
    public RemoteAuthType AuthType { get; set; } = RemoteAuthType.Password;

    /// <summary>跳板机受保护凭据密文。</summary>
    public string ProtectedSecret { get; set; } = string.Empty;

    /// <summary>跳板机私钥路径（认证方式为私钥时使用）。</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>跳板机实际端口：未设置时回退 22。</summary>
    public int GetEffectivePort()
    {
        return this.Port > 0 ? this.Port : 22;
    }

    /// <summary>列表展示摘要。</summary>
    public string Summary
    {
        get { return this.Username + "@" + this.Host + ":" + this.GetEffectivePort(); }
    }

    /// <summary>复制一份跳板节点，用于编辑草稿与保存前回滚。</summary>
    public RemoteJumpHop Clone()
    {
        RemoteJumpHop copy = new RemoteJumpHop();
        copy.Host = this.Host;
        copy.Port = this.Port;
        copy.Username = this.Username;
        copy.AuthType = this.AuthType;
        copy.ProtectedSecret = this.ProtectedSecret;
        copy.PrivateKeyPath = this.PrivateKeyPath;
        return copy;
    }
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

    /// <summary>连接分组（从 Xshell / Xftp 导入时取会话所在分组，其他来源默认为空串）。</summary>
    public string Group { get; set; } = string.Empty;

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

    /// <summary>最多允许配置的跳板机数量。</summary>
    public const int MaxJumpHops = 3;

    /// <summary>
    /// 跳板链：按列表顺序逐跳连接，最后一跳建立到目标主机的通道。
    /// 空集合表示直连；最多 <see cref="MaxJumpHops"/> 跳。
    /// </summary>
    public List<RemoteJumpHop> JumpHops { get; set; } = new List<RemoteJumpHop>();

    /// <summary>是否配置了跳板链。</summary>
    public bool HasJumpHops
    {
        get { return this.JumpHops != null && this.JumpHops.Count > 0; }
    }

    /// <summary>跳板链摘要：跳板1 → 跳板2 → 目标；直连时为空字符串。</summary>
    public string JumpSummary
    {
        get
        {
            if (!this.HasJumpHops)
            {
                return string.Empty;
            }
            string text = string.Empty;
            foreach (RemoteJumpHop hop in this.JumpHops)
            {
                if (text.Length > 0)
                {
                    text += " → ";
                }
                text += hop.Summary;
            }
            return text + " → " + this.Username + "@" + this.Host + ":" + this.GetEffectivePort();
        }
    }

    /// <summary>
    /// 校验跳板链并返回错误说明；通过时返回 null。桌面与网页保存前共用同一套规则。
    /// verifyPrivateKeyFiles 为 true 时额外检查本机私钥文件是否存在（仅界面保存时启用，导入历史配置不强制）。
    /// </summary>
    public string? ValidateJumpChain(bool verifyPrivateKeyFiles = false)
    {
        if (!this.HasJumpHops)
        {
            return null;
        }
        if (this.JumpHops.Count > MaxJumpHops)
        {
            return "第 " + (MaxJumpHops + 1) + " 跳起超出上限：跳板机最多 " + MaxJumpHops
                + " 跳，当前为 " + this.JumpHops.Count + " 跳";
        }
        for (int index = 0; index < this.JumpHops.Count; index++)
        {
            RemoteJumpHop hop = this.JumpHops[index];
            string prefix = "第 " + (index + 1) + " 跳";
            if (hop == null)
            {
                return prefix + "配置为空";
            }
            if (string.IsNullOrWhiteSpace(hop.Host))
            {
                return prefix + "缺少主机地址";
            }
            if (string.IsNullOrWhiteSpace(hop.Username))
            {
                return prefix + "缺少用户名";
            }
            if (hop.Port < 0 || hop.Port > 65535)
            {
                return prefix + "端口超出范围：" + hop.Port;
            }
            if (hop.AuthType == RemoteAuthType.PrivateKey)
            {
                if (string.IsNullOrWhiteSpace(hop.PrivateKeyPath))
                {
                    return prefix + "使用私钥认证但未填写私钥路径";
                }
                if (verifyPrivateKeyFiles && !File.Exists(hop.PrivateKeyPath))
                {
                    return prefix + "私钥文件不存在：" + hop.PrivateKeyPath;
                }
            }
        }
        return null;
    }

    /// <summary>SSH 保活间隔（秒）；0 表示关闭保活，默认 15 秒（参考 Xshell 会话保活选项）。</summary>
    public int KeepAliveSeconds { get; set; } = 15;

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
