# 远程连接（SSH / SFTP / FTP 会话层）现代化设计

状态：设计稿（2026-11-03）
适用：`IRemoteSessionFactory`、`SshSessionFactory`、`FtpSessionFactory`、`Sftp/Ftp` 文件系统与传输服务
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 协议分派 | `App/Services/CompositeRemoteSessionFactory.cs:12-45` | 可用 | SSH / FTP 按 `RemoteProtocol` 分派 |
| SSH 会话复用 | `Protocol.Ssh/SshSessionFactory.cs:14-53` | 可用 | 同一连接配置复用一个 `SshClient`，断线后重建 |
| SSH 认证 | `SshSessionFactory.cs:109-137` | 可用 | 密码 + keyboard-interactive 同时提供；私钥 + 口令；重试 3 次、超时 15 秒 |
| 保活 | `SshSessionFactory.cs:103-104`、`SftpFileSystemProvider.cs:54` | 可用 | 按连接配置秒数发送 keepalive |
| SFTP 文件系统 | `Protocol.Ssh/SftpFileSystemProvider.cs:13-100` | 可用 | 实现 `IFileSystemProvider`，远程目录可作为文件标签 |
| SFTP 权限 | `IRemotePermissionChanger`、`SftpFileSystemProvider` | 可用 | 右键 chmod |
| FTP 文件系统 | `Protocol.Ftp/FtpFileSystemProvider.cs` | 可用 | FluentFTP；无终端 |
| SFTP/FTP 传输 | `SftpTransferService`、`FtpTransferService`、`RemoteTransferServiceBase.cs:33-156` | 可用 | 64KB 分块、暂停/继续/取消、断点续传、递归、3 次重试 |
| 凭据保护 | `WindowsSecretProtector.cs`、`PlaintextSecretProtector.cs` | 半成品 | Windows DPAPI；Linux/macOS 明文降级 |
| 主机密钥校验 | 无 | 缺口 | 代码中无 `HostKeyReceived`/known_hosts 处理 |
| 跳板机 / 代理 | 无 | 缺口 | 连接模型无 JumpHost/Proxy 字段 |
| FTPS / TLS | 无 | 缺口 | FTP 为明文连接 |
| 并发会话上限 | 无 | 缺口 | 无全局连接池/限制/排队 |
| 传输任务持久化 | 无 | 缺口 | 队列仅在内存，重启丢失 |

主要问题：

1. **没有主机密钥校验**：`SshSessionFactory` 未订阅 `HostKeyReceived`，无法发现中间人攻击或主机密钥变更；这是远程连接最严重的安全缺口。
2. **非 Windows 凭据明文落盘**：`PlaintextSecretProtector` 直接返回明文，`connections.json` 中 `ProtectedSecret` 实际是明文密码/私钥口令。
3. **无跳板机/代理**：只能直连目标主机，内网场景不可用。
4. **FTP 明文**：没有 FTPS（显式/隐式 TLS），局域网之外不可接受。
5. **并发与重连策略粗**：同一配置的终端复用一条 SSH 连接；SFTP 文件标签、传输任务各自新建连接；无连接数上限、无失败退避、无“连接健康度”展示。
6. **传输队列不持久化**：关闭标签/重启后任务与断点信息丢失；无全局速度/ETA。
7. **远程文件操作边界**：本机↔远程拖放不支持；远程条目不能写入压缩包；远程文件无法调用系统默认程序。
8. **无连接审计**：谁在何时连接了哪台主机、执行了哪些危险操作，没有独立审计日志。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| OpenSSH | `known_hosts` 首次信任、变更告警、`StrictHostKeyChecking`、ProxyJump（[OpenSSH 手册](https://www.openssh.com/manual.html)） | 主机密钥校验与跳板机是 SSH 客户端基线 |
| SSH.NET | `HostKeyReceived` 事件、私钥/密码/keyboard-interactive、KeepAlive（[SSH.NET](https://github.com/sshnet/SSH.NET)） | 现有认证正确；需补主机密钥事件 |
| Xshell / SecureCRT | 会话管理、主机密钥、跳板、代理、标签终端、日志、快捷命令 | 连接管理与终端体验对标 |
| WinSCP | 双栏远程浏览、传输队列、断点续传、FTPS/SFTP、主机密钥缓存（[WinSCP 文档](https://winscp.net/eng/docs/start)） | 传输与主机密钥管理 |
| FileZilla | FTP/FTPS/SFTP、站点管理、队列、限速 | FTP 安全与队列体验 |
| Windows DPAPI | 当前用户作用域加密，附应用熵（[DPAPI 文档](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection)） | Windows 现有实现可保留 |
| macOS Keychain / libsecret | 平台凭据库 | 非 Windows 的替代方案 |

## 3. 推荐方案

### 必做（P0）

1. **主机密钥校验与 known_hosts**
   - 首次连接展示 SHA256 指纹，用户确认后写入 `known_hosts`（OpenSSH 兼容格式或应用自有 JSON）。
   - 密钥变更时阻断连接，展示旧/新指纹，要求显式删除旧记录后才允许继续。
   - 接入点：`SshSessionFactory.CreateClient` 订阅 `HostKeyReceived`；新增 `Kernel/Services/KnownHostStore.cs`；`ConnectionManagerViewModel` 增加“主机密钥”页。
   - 验收：首次连接必须确认；变更密钥默认拒绝。

2. **跨平台凭据保护**
   - Windows 保留 DPAPI；macOS 用 Keychain；Linux 用 libsecret/Secret Service；无系统库时用 AES-GCM + 用户主密钥（文件权限 600），禁止明文。
   - 接入点：`ISecretProtector` 新增 `MacKeychainSecretProtector`、`LinuxSecretServiceProtector`；`CompositionRoot` 按平台注册；迁移旧明文配置（首次加载时加密并标记）。
   - 验收：`connections.json` 中不再出现明文密码/口令。

3. **连接健康与重连**
   - 统一 `IRemoteConnection` 状态：Connecting/Connected/Degraded/Disconnected；心跳失败阈值、指数退避重连、手动重连。
   - 终端、文件标签、传输队列共享同一连接状态事件；UI 显示状态点。
   - 接入点：`SshSessionFactory`、`SftpFileSystemProvider`、`RemoteTransferServiceBase`；新增 `RemoteConnectionState` 模型。

4. **FTPS / 显式 TLS**
   - FTP 连接增加 `UseTls`、证书校验策略；默认显式 TLS（端口 21 + AUTH TLS），失败时明确提示而非静默降级。
   - 接入点：`RemoteConnectionProfile` 增加 `FtpSecurity`；`FtpSessionFactory.CreateClient` 配置 `FtpClient.Config.EncryptionMode`。

5. **连接池与并发限制**
   - 每配置最多 N 条连接（默认 4），终端/文件/传输各自申请；超出排队并显示“等待连接”。
   - 接入点：新增 `RemoteConnectionPool`；`SshSessionFactory.GetClient` 改为租借/归还。

6. **传输任务持久化与审计**
   - 队列快照（任务、偏移、状态）写入 `transfers.json`；重启后可恢复未完成任务（需校验远端文件大小/ETag）。
   - 连接、认证失败、主机密钥变更、chmod、删除等写入独立审计日志。
   - 接入点：`RemoteTransferServiceBase`、`CaptureHistoryStore` 类似的 `TransferStore`；`UiLogger` 增加 `RemoteAudit` 通道。

### 可选（P1/P2）

- 跳板机/ProxyJump：多跳 SSH 隧道；连接模型增加 `JumpHostId`，用 `SshClient` 转发或 SSH.NET 代理。
- SSH Agent 转发、密钥生成/导入、证书认证。
- SOCKS5/HTTP 代理。
- 传输限速、并发数、失败策略可视化。
- 远程文件与本机拖放（复制语义 + 传输队列）。
- 连接分组、标签页内多会话、会话模板。

### 不建议

- 自动接受未知/变更主机密钥（默认必须确认）。
- 在非 Windows 平台继续明文保存凭据。
- 自研 SSH/FTP 加密协议栈：使用成熟库并正确配置。
- 默认允许远程条目执行本机系统程序。

## 4. 交互与视觉

- 连接卡片：协议徽标、`user@host:port`、分组、状态点（绿/黄/红）、最近使用时间。
- 首次连接：指纹对话框（SHA256、算法、主机:端口），按钮“信任并保存 / 取消”。
- 密钥变更：红色阻断对话框，必须“删除旧密钥并重新信任”。
- 传输队列：方向图标、文件、进度、速度/ETA、暂停/继续/取消。
- 连接失败：区分 DNS、端口拒绝、认证失败、主机密钥、超时，给出下一步建议。
- 状态栏：当前连接数/上限、传输任务数。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 主机密钥 | 新增 `Kernel/Services/KnownHostStore.cs`；`SshSessionFactory.cs` | `HostKeyReceived` 事件 + 指纹 |
| 凭据 | 新增 `Platform.MacOS/MacKeychainSecretProtector.cs`、`Platform.Linux/LinuxSecretServiceProtector.cs`；`ISecretProtector` | 旧明文迁移 |
| 连接池 | 新增 `Kernel/Services/RemoteConnectionPool.cs`；`SshSessionFactory.cs` | 租借/归还/上限 |
| FTPS | `Kernel/Models/RemoteConnectionProfile.cs`、`FtpSessionFactory.cs` | 显式 TLS + 证书策略 |
| 任务持久化 | 新增 `Kernel/Storage/JsonTransferStore.cs`；`RemoteTransferServiceBase.cs` | 恢复需校验远端 |
| 审计 | `UiLogger`、`RemoteTransferServiceBase`、`SftpFileSystemProvider` | 独立通道 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 主机密钥 | known_hosts 格式/算法兼容；首次信任 UX | M |
| 跨平台凭据 | Keychain/libsecret 依赖、权限、迁移失败 | M |
| 连接池 | 现有代码多处直接 new 客户端，改造面广 | M/L |
| FTPS | 证书校验与自签场景 | S/M |
| 任务恢复 | 远端文件变化导致续传错误 | M |
| 跳板机 | SSH.NET 多跳实现复杂 | L |

## 7. 验收标准

1. 首次连接弹出指纹确认；未确认不能建立会话。
2. 主机密钥变更时连接被阻断，删除旧记录后可重新信任。
3. `connections.json` 在 Windows/macOS/Linux 均不出现明文密码/私钥口令。
4. FTPS 连接成功；普通 FTP 在启用 TLS 后不会静默降级。
5. 每配置并发连接不超过上限，超出显示“等待连接”。
6. 关闭并重启程序后，未完成传输任务可恢复或明确标记不可恢复。
7. 连接/认证/密钥变更/删除操作进入审计日志。
8. 连接失败提示能区分 DNS、端口、认证、主机密钥、超时。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [OpenSSH 手册](https://www.openssh.com/manual.html) | known_hosts、StrictHostKeyChecking、ProxyJump | 2026-11-03 |
| [SSH.NET](https://github.com/sshnet/SSH.NET) | HostKeyReceived、认证方式、KeepAlive | 2026-11-03 |
| [WinSCP 文档](https://winscp.net/eng/docs/start) | 传输队列、断点续传、主机密钥 | 2026-11-03 |
| [DPAPI 使用](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection) | Windows 凭据加密 | 2026-11-03 |
| [FileZilla](https://filezilla-project.org/) | FTPS/SFTP、站点管理 | 2026-11-03 |
| [Xshell](https://www.netsarang.com/en/xshell/) | 会话/跳板/终端产品形态 | 2026-11-03 |
