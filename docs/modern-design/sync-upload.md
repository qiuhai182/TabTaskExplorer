# 同步 / 上传现代化设计（规划中）

状态：设计稿（2026-11-03），当前应用无上传/同步实现
适用：未来 `IUploadService` / `ISyncService`、任务中心、连接管理
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 应用内上传 | 无 | 缺口 | 仅远程传输队列（SFTP/FTP），无 HTTP/WebDAV/S3 上传 |
| 应用内同步 | 无 | 缺口 | 无目录同步/双向同步/冲突解决 |
| 远程传输 | `RemoteTransferServiceBase`、`Sftp/FtpTransferService` | 可用 | 上传/下载/暂停/续传/重试 |
| 截图后上传 | 无 | 缺口 | 截图动作链没有“上传”步骤 |
| 仓库脚本 | `upLoad.bat` / `upLoad.sh` | 非应用功能 | 仅用于本仓库发布，不是产品能力 |
| 云盘占位/同步引擎 | 无 | 缺口 | 未实现 Cloud Filter API |
| 凭据 | `ISecretProtector` | 半成品 | Windows DPAPI，非 Windows 明文 |

主要问题（若要做）：

1. **无通用上传目标**：只有 SFTP/FTP；不能上传到 HTTP/WebDAV/S3/图床。
2. **无同步模型**：没有本地→远程的单向同步、双向同步、冲突策略。
3. **无冲突解决**：同名文件覆盖/跳过/保留两者/较新优先没有统一 UI。
4. **无断点/校验**：远程传输有断点，但 HTTP 上传无 Range/分块/ETag 校验。
5. **无加密/隐私策略**：上传目标凭据、传输加密、敏感文件排除未设计。
6. **无审计**：谁上传了什么、去了哪里没有记录。
7. **无配额/限速**：大文件上传没有限速/并发控制。
8. **与截图/录屏动作链未集成**：产物不能自动上传并复制链接。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| rclone | 多后端（S3/WebDAV/SFTP/HTTP）、sync/copy/check、限速（[rclone](https://rclone.org/docs/)） | 后端抽象/同步语义 |
| Syncthing | 双向同步、冲突文件、版本控制（[Syncthing](https://syncthing.net/)） | 冲突策略 |
| Nextcloud/WebDAV | WebDAV 上传、分块、版本（[RFC 4918](https://datatracker.ietf.org/doc/html/rfc4918)） | WebDAV 后端 |
| AWS S3 API | 分块上传、ETag、预签名 URL（[S3 API](https://docs.aws.amazon.com/AmazonS3/latest/API/Welcome.html)） | 大文件分块 |
| ShareX | 上传目标、URL 复制、历史（[ShareX](https://getsharex.com/)） | 截图后上传动作链 |
| Greenshot | 外部命令/上传目标 | 动作链插件化 |

## 3. 推荐方案

### 必做（P0，若启动该模块）

1. **上传目标抽象**
   - `IUploadTarget`：`UploadAsync(path, remoteName, progress, ct)`、`TestAsync`、`GetPublicUrl`；实现 HTTP PUT/POST、WebDAV、S3、SFTP/FTP（复用现有）。
   - 目标配置存 `AppSettings.UploadTargets`；凭据走 `ISecretProtector`。
   - 接入点：新增 `Kernel/Abstractions/IUploadTarget.cs`、`App/Services/Upload/*`；`SettingsWindow` 新增“上传”分页。

2. **截图/录屏动作链接入**
   - 动作链增加“上传到…”，成功后复制 URL/ Markdown/HTML 到剪贴板；失败进入通知中心。
   - 接入点：`CaptureCoordinator` 动作链（见 `screenshot.md`）、`IProgressCenter`。

3. **单向同步（本地→远程）**
   - 选择本地目录与目标、包含/排除规则、删除策略（不删除/镜像删除）、冲突策略（跳过/覆盖/保留两者/较新优先）。
   - 先 dry-run 生成计划，确认后执行；任务中心显示进度。
   - 接入点：新增 `ISyncService`、`SyncPlan`；复用 `IFileSystemProvider`/`IProgressCenter`。

4. **校验与断点**
   - 上传后校验大小/哈希/ETag；HTTP 分块/断点；失败重试与续传。
   - 接入点：`UploadTarget`、`RemoteTransferServiceBase`。

5. **审计与隐私**
   - 上传/同步记录（目标、路径、大小、结果、时间）；排除密码/密钥/系统目录；目标凭据加密。
   - 接入点：`UiLogger` 审计通道、`SettingsWindow` 排除规则。

### 可选（P1/P2）

- 双向同步 + 冲突副本（Syncthing 式）。
- 云盘占位文件（Cloud Filter API）——工作量大，独立立项。
- 图床/对象存储预签名 URL。
- 限速/并发/带宽调度。
- 团队共享目标/权限。

### 不建议

- 默认开启自动同步：隐私/误删风险，必须显式配置 + dry-run。
- 双向同步作为首个版本：冲突复杂，先做单向。
- 把仓库 `upLoad.bat` 当产品上传功能：它是发布脚本，不应暴露给用户。
- 在日志中记录上传凭据/URL 签名。

## 4. 交互与视觉

- 设置“上传目标”：卡片列表（名称/类型/主机/测试状态），新增/编辑/删除/测试。
- 截图动作链：勾选“上传到 <目标>”，上传后 toast“已复制链接”。
- 同步向导：选择本地/远程 → 规则 → dry-run 计划（新增/覆盖/删除/跳过）→ 执行。
- 任务中心：上传/同步任务，进度/速度/ETA/取消。
- 失败：通知 + 重试；冲突文件列表可逐个处理。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 上传抽象 | 新增 `IUploadTarget`、`UploadTargetStore` | HTTP/WebDAV/S3/SFTP |
| 动作链 | `CaptureCoordinator`、`IProgressCenter` | 上传步骤 |
| 同步 | 新增 `ISyncService`、`SyncPlan` | dry-run/冲突 |
| 校验 | `UploadTarget`、哈希/ETag | 断点 |
| 审计 | `UiLogger` | 目标/路径/结果 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 上传后端 | 认证/证书/分块差异 | L |
| 同步 | 冲突/删除/大目录 | L |
| 凭据 | 跨平台加密 | M |
| 隐私 | 误传敏感文件 | M |
| 云占位 | Cloud Filter API 复杂 | L |

## 7. 验收标准

1. 可配置 HTTP/WebDAV/S3/SFTP 上传目标并测试连接。
2. 截图动作链可上传并复制 URL；失败通知可重试。
3. 单向同步先 dry-run，计划与实际执行一致；可取消。
4. 同名冲突按策略处理；冲突文件可逐个解决。
5. 上传后大小/哈希校验通过；大文件可分块/续传。
6. 审计日志记录目标/路径/大小/结果，不含凭据。
7. 默认不同步/不上传，所有目标需用户显式配置。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [rclone](https://rclone.org/docs/) | 多后端/同步语义/限速 | 2026-11-03 |
| [Syncthing](https://syncthing.net/) | 双向同步/冲突 | 2026-11-03 |
| [WebDAV RFC 4918](https://datatracker.ietf.org/doc/html/rfc4918) | WebDAV 上传 | 2026-11-03 |
| [AWS S3 API](https://docs.aws.amazon.com/AmazonS3/latest/API/Welcome.html) | 分块/ETag | 2026-11-03 |
| [ShareX](https://getsharex.com/) | 截图后上传动作链 | 2026-11-03 |
| [Cloud Filter API](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/) | 云占位（P2） | 2026-11-03 |
