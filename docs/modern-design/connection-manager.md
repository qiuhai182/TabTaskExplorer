# 连接管理（主机 / 凭据 / 分组 / 主机密钥）现代化设计

状态：设计稿（2026-11-03）
适用：`ConnectionManager`、`ConnectionManagerViewModel`、`ConnectionPanel`、`JsonConnectionStore`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 连接增删改查 | `Kernel/Services/ConnectionManager.cs:31-102` | 可用 | JSON 持久化，自动分配 Id/时间戳 |
| 连接模型 | `Kernel/Models/RemoteConnectionProfile.cs:29-98` | 可用 | 协议、主机、端口、用户名、认证方式、私钥、保活 |
| 认证方式 | `RemoteAuthType`（:16-23） | 可用 | 密码 / 私钥 |
| 凭据加密 | `ConnectionManager.SetSecret/GetSecret`（:104-123） | 半成品 | Windows DPAPI；非 Windows 明文 |
| 连通性测试 | `TcpConnectivityTester.cs:11-68` | 半成品 | 仅 TCP 可达，不做协议/认证握手 |
| 导出 / 导入 | `ConnectionManager.ExportTo/ImportFrom`（:131+） | 可用 | JSON；密文原样导出 |
| Xshell 导入 | `XshellSessionImporter`、`ConnectionManagerViewModel.ImportInstalled*`（:126-145） | 可用 | 扫描用户文档/OneDrive 下 NetSarang 配置 |
| 连接列表 UI | `ConnectionPanel.axaml:22-96` | 可用 | 280px 列表 + 编辑表单 |
| 分组 | `RemoteConnectionProfile.Group`（:37-38） | 半成品 | 仅 Xshell 导入时写入，UI 无分组树/筛选 |
| 主机密钥 | 无 | 缺口 | 无指纹/known_hosts 管理界面 |
| 跳板机 / 代理 | 无 | 缺口 | 模型无字段 |
| 连接搜索 / 排序 / 克隆 | 无 | 缺口 | 列表按存储顺序 |
| 凭据重新输入流程 | 半成品 | 半成品 | 解密失败时无专门引导 |

主要问题：

1. **分组字段没有 UI**：模型有 `Group`，但 `ConnectionPanel` 只有扁平列表；导入几十台主机后难以管理。
2. **没有主机密钥管理**：用户看不到指纹、无法预信任/删除旧密钥；与远程连接模块的安全缺口联动。
3. **连通性测试误导**：`TcpConnectivityTester` 只测端口可达，主机可达但认证失败时仍显示“成功”。
4. **凭据跨机器不可用**：导出 JSON 带 DPAPI 密文，换机器后无法解密；UI 没有“重新输入凭据”的清晰流程。
5. **无连接克隆/模板**：同主机多用户、同跳板多目标需要重复填写。
6. **无搜索/排序/最近使用**：列表只按存储顺序，找不到主机。
7. **无字段校验**：主机为空、端口非法、私钥文件不存在、保活为负数等依赖保存时兜底。
8. **无导入预览/冲突策略**：Xshell 一键导入同名覆盖，用户无法选择“跳过/覆盖/另存”。
9. **无审计**：谁导出/导入了连接配置没有记录。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Xshell 会话管理器 | 分组树、搜索、会话属性、跳板、代理、主机密钥 | 分组 + 搜索 + 克隆是基础 |
| SecureCRT | 会话树、全局选项、导入向导、密钥管理 | 导入预览与凭据重输入 |
| WinSCP 站点管理器 | 站点文件夹、搜索、导入/导出、主机密钥缓存 | 站点分组与 known_hosts UI |
| FileZilla 站点管理器 | 站点分组、协议/加密/传输设置、导入导出 | FTP/FTPS 字段组织 |
| OpenSSH config | Host 别名、ProxyJump、IdentityFile | 可支持导入/导出 OpenSSH config |
| Windows Credential Manager | 系统凭据库 | 非 Windows 的凭据管理参考 |

## 3. 推荐方案

### 必做（P0）

1. **分组树 + 搜索 + 排序**
   - 左侧从扁平列表改为分组树（未分组/自定义组），支持拖拽移动、重命名、删除空组；顶部搜索按名称/主机/用户过滤；可按最近使用排序。
   - 接入点：`ConnectionManagerViewModel.Connections` 改为分组集合或增加 `Groups`；`ConnectionPanel.axaml:22-38` 列表改为 TreeView + 搜索框；`RemoteConnectionProfile.Group` 已存在。
   - 验收：50 台主机可两级分组，搜索 1 秒内命中。

2. **连接校验**
   - 保存前校验：名称非空、主机非空、端口 1–65535、私钥路径存在且可读、保活 0–3600、重名提示。
   - 接入点：`ConnectionManagerViewModel.Save`（:85-103）、`ConnectionPanel` 保存按钮；新增 `ConnectionValidator`。

3. **认证/连通性测试升级**
   - 测试分两步：TCP 可达 + 协议握手/认证（SSH `Connect` + `IsConnected`，FTP `Connect`）；返回失败阶段与原因。
   - 接入点：`IConnectivityTester` 增加 `TestProtocol` 或新 `IDeepConnectivityTester`；`ConnectionManager.Test`（:126-129）。
   - 注意：测试会留下连接，需立即断开，避免占用连接池。

4. **主机密钥管理界面**
   - 每台 SSH 主机显示已信任指纹（算法、SHA256、首次信任时间）；支持查看、删除、重新信任；变更时红色告警。
   - 接入点：`KnownHostStore`（远程连接模块新增）；`ConnectionPanel` 增加“主机密钥”区域/对话框。

5. **凭据重输入与导入预览**
   - 解密失败时，列表标记“凭据需重新输入”，打开表单时提示；导出提供“包含密文/不含凭据”两个选项。
   - Xshell 导入先展示预览（新增/更新/同名冲突），支持跳过/覆盖/另存。
   - 接入点：`ConnectionManager.GetSecret`（:116-123）、`ExportTo`（:131+）、`ImportInstalled*`（:126-145）、`ConnectionPanel`。

6. **克隆与模板**
   - 右键“克隆连接”，复制主机/端口/认证方式，名称追加“副本”；支持“从模板新建”。
   - 接入点：`ConnectionManagerViewModel` 新增 `Clone`；`ConnectionPanel` 右键菜单。

### 可选（P1/P2）

- 导入/导出 OpenSSH `~/.ssh/config`（Host/HostName/User/Port/IdentityFile/ProxyJump）。
- 连接配置加密导出（带口令的 AES-GCM 容器），便于跨机迁移。
- 最近连接、使用次数、备注/标签。
- 连接配置同步（WebDAV/S3），默认关闭。
- 批量测试全部连接并生成报告。

### 不建议

- 把密文原样导出当“可迁移备份”：跨机不可解密，必须提供口令加密导出。
- 在 UI 中显示明文密码：只允许“重新输入/清除”，不提供“查看”。
- 用连接名称作为唯一键：必须用 Id，名称可重复。

## 4. 交互与视觉

- 左侧分组树宽 260，支持折叠；节点显示主机数与在线状态点。
- 右侧表单分组卡片：基本信息、认证、连接选项、主机密钥；保存/测试按钮固定底部。
- 搜索框 36px，支持 `name:`、`host:`、`user:` 前缀（P1）。
- 导入向导：文件选择 → 解析结果表 → 冲突策略 → 完成摘要。
- 凭据缺失：黄色横幅“该连接的凭据无法在当前设备解密，请重新输入”。
- 主机密钥：指纹等宽字体显示，复制按钮；“信任/删除”二次确认。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 分组树 | `ConnectionManagerViewModel.cs`、`ConnectionPanel.axaml(.cs)` | 复用 `Group` 字段 |
| 校验 | 新增 `Services/ConnectionValidator.cs`；`ConnectionManagerViewModel.Save` | 返回字段级错误 |
| 深度测试 | `IConnectivityTester`、`TcpConnectivityTester`、`CompositeRemoteSessionFactory` | 协议握手后立即断开 |
| 主机密钥 UI | `ConnectionPanel`、`KnownHostStore` | 与远程连接模块共用 |
| 导入预览 | `ConnectionManagerViewModel.ImportInstalled*`、新增 `ImportPreviewDialog` | 解析与落库分离 |
| 凭据重输入 | `ConnectionManager.GetSecret`、`ConnectionPanel` | 解密异常捕获 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 分组树 | 现有扁平集合与 Web 端快照需同步改 | M |
| 深度测试 | 测试连接占用/认证失败副作用 | S/M |
| 主机密钥 UI | 与 known_hosts 存储耦合 | M |
| 导入预览 | Xshell 文件格式差异 | M |
| OpenSSH config | 语法子集与别名解析 | M |

## 7. 验收标准

1. 连接可分组、拖拽移动、搜索、按最近使用排序；重启后分组保留。
2. 保存非法主机/端口/私钥路径时阻止并定位字段。
3. “测试连接”能区分 TCP 不可达、认证失败、主机密钥不信任。
4. 每台 SSH 主机可查看/删除指纹；变更后连接被阻断。
5. 解密失败时列表与表单明确提示“重新输入凭据”。
6. Xshell 导入前展示新增/更新/冲突数量，并可选择跳过/覆盖/另存。
7. 导出可选“不含凭据”，导入后提示需要补录密码。
8. 克隆连接生成新 Id，修改副本不影响原连接。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [OpenSSH 手册](https://www.openssh.com/manual.html) | known_hosts、ssh_config、ProxyJump | 2026-11-03 |
| [WinSCP 站点管理](https://winscp.net/eng/docs/ui_login) | 站点分组/导入导出 | 2026-11-03 |
| [FileZilla 站点管理器](https://filezilla-project.org/) | FTP/FTPS 站点字段 | 2026-11-03 |
| [Xshell](https://www.netsarang.com/en/xshell/) | 会话管理产品形态 | 2026-11-03 |
| [Windows Credential Manager](https://learn.microsoft.com/en-us/windows/win32/secauthn/credentials-management) | 系统凭据管理 | 2026-11-03 |
