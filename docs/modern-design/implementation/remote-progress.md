# 远程连接线进度（进行中）

状态：阻塞
卡点：134 主机已恢复且 root/root123 基础连接与 SFTP 已实测通过，但 ProxyJump 无跳板主机可测；nk8500 账号在服务端认证阶段挂起。
下一步：有跳板主机后补逐跳验证并跑 SshJumpChainTests；nk8500 需在 134 服务端修 PAM/账号。

最后更新：2026-11-04

## P1-1 跳板机 / ProxyJump（代码已完成，未实测）

- 模型：`RemoteConnectionProfile.JumpHops`（最多 3 跳），每跳独立 host/port/user/auth/受保护凭据/私钥路径；`ValidateJumpChain()` 统一校验，桌面与网页保存共用。
- 凭据：`ConnectionManager.SetJumpSecret/GetJumpSecret` 与主凭据使用同一 `ISecretProtector`，密文落盘，日志不记录密码。
- 链路：`SshJumpChain` 使用 `ForwardedPortLocal("127.0.0.1", 0, nextHost, nextPort)` 逐跳串联，最后一跳转发目标 host:port；终端、远程文件夹、SFTP 传输共用同一链路工厂。
- 主机密钥：每跳独立按 hop.Host/hop.Port 校验，异常与确认对话框标注“第 N 跳”；目标主机仍按 profile.Host 校验。
- 失败与重试：每跳连接超时 15 秒、网络类失败重试 1 次，认证/主机密钥失败不重试；记录每跳尝试次数与耗时；失败抛出 `RemoteHopException`（含第几跳、主机、阶段、耗时、原因）。
- 界面：桌面连接面板新增“经跳板连接”开关与跳板链增删/更新/上移/下移/凭据编辑；网页连接管理新增 `跳板链` 文本域（每行 `主机|端口|用户名|认证方式|私钥路径|密码`），快照返回跳板字段与“已保存凭据”标记。
- 单元测试：`src/TabTaskExplorer.Tests/SshJumpChainTests.cs`（默认端口、摘要、≤3 跳、缺字段定位、私钥路径、逐跳错误映射、主机密钥异常跳序、ConnectionManager 拒绝非法链、跳板凭据回环）。
- 实测状态：本机没有可用的跳板主机，且 134-uospro 仍不可达；仅完成代码与单元测试，未做真实逐跳连网验证。
- XAML 已修（CheckBox 的 Checked/Unchecked 改为 IsCheckedChanged）、C# 错误已修（json/tasksJson 重名、UIOption 拼写），待用户构建验证。
- 跳板链校验：>3 跳、某跳主机/用户名/端口/私钥路径缺失均提示“第 N 跳”；界面保存时额外检查私钥文件是否存在；目标主机为空提示“目标主机地址”。
- 网页跳板链文本域：跳过空行、各字段去首尾空格；字段不足、主机/端口/用户名/认证方式/私钥路径错误均返回“第 X 行（第 N 跳）：字段名”提示。
- 构建状态：Shell 工具后端持续返回 `sandbox backend failed`，无法执行 `dotnet build` / `dotnet test`；上一轮构建日志中 AVLN2000 已消失，剩余 3 个 C# 错误即上述已修项；最终 exit code 与 `SshJumpChainTests` 通过数待用户手动运行 `D:\TabTaskExplorer\build.bat` 后回填。

## 当前结论：134-uospro 网络不可达

- 本机 PowerShell 实测 `Test-NetConnection 192.168.1.134 -Port 22`：
  - `WARNING: TCP connect to (192.168.1.134 : 22) failed`
  - `TcpTestSucceeded = False`
- 判定：主机掉线或网络中断。当前无法复测登录，也不能据此判断密码是否错误。
- 2026-11-03 的可用证据（主机在线时）：
  - 系统 OpenSSH 强制 `PreferredAuthentications=password` 使用 `nk8500` + 测试密码成功（输出 `AUTH_OK`、`uid=1000(nk8500)`）
  - 随后 `sftp` 登录成功并可列出 `/`，说明当时账号、密码、SFTP 子系统均正常
  - 因此网页端当时出现的“密码被拒”不能归因为主机侧账号密码；更可能是客户端认证方式/构建差异，需主机恢复后用带 `ssh -vv` 的对比复测确认
- SSH.NET 侧尝试顺序（代码现状）：`PasswordAuthenticationMethod` → `KeyboardInteractiveAuthenticationMethod`，服务器选择哪种即用哪种；OpenSSH 侧已确认 password 可用。

## 已完成（P0 第 2 项）

- 桌面端：未知主机指纹确认、密钥变更硬阻断、信任后重试、主机密钥查看/删除入口
- 网页端：`pendingHostKey` 结构化状态 + `connhostkeytrust` / `connhostkeyforget` / `connhostkeycancel` 动作 + 确认对话框
- `known_hosts`：按 host:port + key 类型原子写入，并发加锁
- 审计日志：`远程连接审计`

## 改动文件

- `src/TabTaskExplorer.Protocol.Ssh/SshSessionFactory.cs`
- `src/TabTaskExplorer.Kernel/Services/KnownHostStore.cs`
- `src/TabTaskExplorer.Kernel/Services/HostKeyExceptions.cs`
- `src/TabTaskExplorer.App/ViewModels/MainWindowViewModel.cs`
- `src/TabTaskExplorer.App/Services/WebWorkspace.cs`
- `src/TabTaskExplorer.App/Services/WebServiceHost.cs`
- `src/TabTaskExplorer.App/Services/DebugUiModels.cs`
- `src/TabTaskExplorer.App/Assets/web/panels.js`、`views.js`、`app.css`
- `src/TabTaskExplorer.App/Views/ConnectionPanel.axaml(.cs)`

## 构建状态（2026-11-04）

- `D:\TabTaskExplorer\build.bat` 已在终端执行：dotnet publish 输出 `TabTaskExplorer.App -> D:\TabTaskExplorer\output\`，无 CS/MSB/NU 错误
- 警告约 100 条，均为既有 nullable/CA/Avalonia 警告
- 脚本 `EXITCODE=1` 与发布成功不一致，需单独核对脚本尾部返回码（不影响输出产物生成）

## 待验证

- 134 主机恢复后：首次连接出现指纹确认 → 信任后成功列出目录 → 再次连接不提示
- 认证方式对比：主机恢复后用 `ssh -vv` 记录服务器允许的方法与实际成功方法
- 构建脚本尾部返回码与“发布成功”不一致，需核对脚本 `PUBLISH_RESULT`/`exit /b` 逻辑

## 下一步

1. 构建并运行 `SshJumpChainTests`；有跳板主机时补真实逐跳连网验证
2. P1-2 SSH 密钥管理
3. P1-3 传输队列持久化
4. P1-4 限速（全局/单任务，SFTP 与 FTP）
5. P1-5 终端 resize、多行粘贴确认、长输出性能、重连提示
