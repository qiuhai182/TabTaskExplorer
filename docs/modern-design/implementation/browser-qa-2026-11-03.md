# 网页端浏览器实测记录（2026-11-03）

测试地址：`http://127.0.0.1:15919/`  
发布产物：`D:\TabTaskExplorer\output\TabTaskExplorer.exe`（2026-11-03 16:56 发布，版本号 `p0-1-web-modules-68ed9a74ef`）  
浏览器：Chrome 155（DevTools MCP 驱动）  
权限方案：默认方案（匿名访问、允许增删、不允许修改）  
构建/测试：`dotnet publish` 退出码 0；`dotnet test` 通过 213 项，失败 0 项。

## 1. 结论摘要

| 结果 | 数量 | 项目 |
| --- | --- | --- |
| 通过 | 5 | 1 首页与静态资源、3 事件推送、4 右键菜单、5 磁盘视图（修复后）、8 响应式布局 |
| 部分通过 | 1 | 2 认证与安全（会话/CSRF 通过；密码失败锁定未能在当前配置下实测） |
| 失败 | 1 | 6 连接管理（134-uospro 账号 nk8500 认证失败；主机后续 TCP 超时） |
| 不适用 | 1 | 7 截图/录屏页（网页端没有该页面，桌面端专属） |

最严重的三个问题：

1. **远程文件夹 SFTP 连接完全绕过 known_hosts 主机密钥校验**：`SftpFileSystemProvider` 直接 `new SftpClient(info)`，未挂接 `HostKeyReceived`，首次连接不会弹出指纹确认。已修复。
2. **磁盘视图在“此电脑”根视图下所有写操作都被拒绝**：`EnsureDiskDirectory` 先判断根目录为空就返回失败，即使目标就是已展开的可见目录行。已修复。
3. **默认方案下“信任并保存”主机指纹被 403 拒绝**：`connhostkeytrust` / `connhostkeyforget` 被归入“修改权限”，而默认方案只放行增删。已修复。

已修复的其余问题：

- 磁盘视图目录行右键菜单缺少“新建文件夹…”“重命名…”入口（桌面端有、网页端没有）。已补齐。
- 同目录粘贴同名文件夹时返回“成功 1 项”但实际未产生副本（`Copy` 契约是同名覆盖）。未修，记录为已知限制。

## 2. 逐项结果

### 2.1 首页与静态资源：通过

- `GET /api/version` 返回 200：

```json
{"version":"p0-1-web-modules-68ed9a74ef","features":{"session":true,"csrf":true,"events":true,"staticAssets":true}}
```

- 页面加载的资源全部返回 200：`app.css`、`core.js`、`views.js`、`panels.js`、`actions.js`、`ide.js`、`boot.js`，查询串均为 `?v=68ed9a74ef`。
- 控制台无脚本报错；仅有 1 条 403（本次故意发起的不带 CSRF 的负向测试）和 1 条无障碍提示 `No label associated with a form field (count: 60)`，不影响功能。

### 2.2 认证与安全：部分通过

实际状态码：

| 请求 | 状态码 | 提示文案 |
| --- | --- | --- |
| `GET /api/session` | 200 | 返回 `sessionToken`、`csrfToken`、`account`、`profile`、`expiresInSeconds` |
| `POST /api/mode?m=independent`（不带 `X-CSRF-Token`） | 403 | `缺少或错误的 CSRF 令牌` |
| 同上，带正确 `X-CSRF-Token` | 200 | `{"workspaceId":"47e674c2173f429295d2613a187ab866"}` |

未测部分：当前权限方案 `RequirePassword=false`，没有密码校验路径；要验证“连续错误密码触发 429、正确密码仍能登录”，需要临时开启密码并重启服务。本次执行期间 Trae Shell 沙箱后端多次返回 `sandbox backend failed`，无法安全地备份/改写 `settings.json` 并重启两次，故未实测。该逻辑由 `WebSecurityTests` 覆盖，`dotnet test` 213 项全部通过。

### 2.3 事件推送：通过

- 页面加载后 `EventSource` 连接 `/api/events?w=...&token=...` 返回 200，先收到 `snapshot`，状态栏显示“已连接”。
- 切换标签、新建标签、双击进入 `C:\` 后均收到 `state` 事件，实测延迟 5 ms、8 ms、239 ms，均小于 1 秒。
- 空闲 31 秒：`/api/state` 请求计数保持 4 → 4（无轮询）；直接读取 SSE 流的 23 秒内收到 1 次 `: 心跳 ...` 注释。
- 主动关闭并重建 `EventSource` 后 2.5 秒内收到 `open` + `snapshot`，状态恢复“已连接”。

### 2.4 右键菜单：通过

- 文件夹 `D:\TabTaskExplorer\output\avdecomp`：两层分组“本程序命令 / 系统与扩展菜单”，共 59 项；`/api/ctxmenu` 返回 200，`systemMenuAvailable=true`，TortoiseGit 子菜单 36 项。
- 单个文件 `app-err.txt`：目标数 1，本程序命令为 打开/在新标签打开/复制/剪切/复制完整路径/重命名/删除/在资源管理器打开。
- 多选（勾选 `app-err.txt` + `app-out.txt`）后右键：`targetPaths=2`，菜单显示“复制（2 项）”“删除（2 项）”“比较所选两项”。
- 空白处右键：本程序命令为 刷新/返回上一级/粘贴/新建文件夹/排序。
- TortoiseGit 子菜单可展开；“Show log”点击后 `/api/ctxmenu/run` 返回 200、响应体 `ok`（`commandId=112`），系统实际拉起了 `TortoiseGitProc`（PID 28776、29836）。
- WinMerge 点击后 `WinMergeU` 被拉起（PID 14872，窗口标题 `WinMerge - [选择文件或文件夹]`）。

已知问题：当 `TortoiseGitProc` 窗口存在时，下一次右键曾出现 `系统外壳菜单枚举超时（2500 毫秒内未返回），可能是某个外壳扩展无响应`；关闭该进程后恢复正常。属于外壳扩展耗时导致的超时提示，未修改 2500 毫秒阈值。

### 2.5 磁盘视图：修复后通过

| 操作 | 结果 | 实测提示 |
| --- | --- | --- |
| 新建文件夹 | 成功 | `已新建文件夹：qa-disk-20261103` |
| 重命名 | 成功 | `已重命名：qa-renamed-test` |
| 删除 | 成功 | `已删除到回收站：qa-renamed-test` |
| 复制 + 粘贴到另一个文件夹 | 成功 | `粘贴完成：成功 1 项，失败 0 项`，目标目录实际出现副本 |
| 拖拽（Ctrl 复制） | 成功 | `拖放完成（复制）：成功 1 项，失败 0 项`，目标目录实际出现副本 |
| 文件列表视图同步 | 成功 | 磁盘视图新建后，同目录文件标签无需手动刷新即出现 `qa-disk-20261103` |

`C:\` 根目录写入未被拒绝：实测创建 `C:\qa-c-root-test` 成功（Windows 默认 ACL 允许当前用户在此创建文件夹），已立即删除。该现象不是权限缺陷，也不能作为“拒绝写入”的验证；若需强制拒绝，应改用需要管理员权限的路径或收紧权限方案。

同目录粘贴同名文件夹：返回“成功 1 项”，但源与目标同路径，`Copy` 按契约同名覆盖，实际没有产生“副本”，属于误导性成功提示，未修。

### 2.6 连接管理：失败（环境与凭据）

- `134-uospro`：账号 `nk8500`，认证方式 Password。打开远程文件夹返回失败提示：

```text
打开远程文件夹失败：Permission denied (password).
```

  日志：`[网页端工作区] 打开远程文件夹失败：连接=134-uospro 服务器=192.168.1.134 端口=22 认证方式=Password 异常类型=SshAuthenticationException 异常信息=Permission denied (password).`

- 主机密钥首次确认：修复前已知主机文件不存在，但 SFTP 直接进入认证阶段，**没有弹出指纹确认**（说明校验被绕过）。修复后首次连接已弹出“确认主机指纹”对话框，指纹 `cIhG+LXadOVuJKVOkwCSIb9g9utSV0DraVoQhaN8wlA`（算法 `ssh-ed25519`）。
- 点击“信任并保存”曾返回 403 `该端口未放行修改权限`；修复权限归类后，`connhostkeytrust`/`connhostkeyforget` 均返回 200（当前无待确认项时提示“当前没有待确认的主机密钥”）。
- 后续再次连接时网络已不可达：`134-uospro` 与 `135-kylin` 的“测试连接”均返回 `测试失败：连接超时（超过 5000 毫秒）`；打开远程文件夹返回 `Connection failed to establish within 15000 milliseconds.` 因此“信任后连接成功、再次连接不再提示”只能验证到指纹确认与信任写入阶段，无法完成端到端连接。

### 2.7 截图/录屏页：不适用

网页端静态资源与 `WebPageHtml.cs` 中均无“截图/录屏/产物”页面，`WebWorkspace` 也没有对应视图；该页面为桌面端专属。浏览器内无法执行产物历史多选、双击打开、拖拽导出，未做任何网页端临时实现。

### 2.8 响应式布局：通过

- 1024×768：页面无横向溢出（`scrollWidth=1024`），工具栏、标签条、状态栏完整；表头列宽未被拉伸，中文无截断。
- 1024 宽度下右键菜单被限制在视口内：`left=778, right=1018, top=10, bottom=762`，`max-height=752px`、`overflow-y=auto`（59 项可滚动）；TortoiseGit 子菜单自动向左展开，`right=785`，未越界。
- 1440×900：页面无滚动溢出；表格宽度 974、内容区 1205，右侧留空（符合“列宽不足容器宽度时留空”的工程约定）；中文文案完整。

## 3. 代码修复

| 文件 | 修复内容 |
| --- | --- |
| `src/TabTaskExplorer.Protocol.Ssh/SftpFileSystemProvider.cs` | 注入 `KnownHostStore`，连接前挂接 `HostKeyReceived`；未信任抛 `HostKeyUnknownException`，指纹变更抛 `HostKeyMismatchException` |
| `src/TabTaskExplorer.Protocol.Ssh/SftpTransferService.cs` | 传输连接同样执行 known_hosts 校验 |
| `src/TabTaskExplorer.Protocol.Ssh/SshSessionFactory.cs` | 创建文件系统与传输服务时传入 `KnownHostStore` |
| `src/TabTaskExplorer.App/Services/WebWorkspace.cs` | `EnsureDiskDirectory` 改为先接受当前磁盘视图内可见的目录行，再检查根目录 |
| `src/TabTaskExplorer.App/Services/WebServiceHost.cs` | 主机密钥信任/删除旧记录改归“新建与删除”权限 |
| `src/TabTaskExplorer.App/Assets/web/actions.js` | 磁盘视图目录行新增“新建文件夹…”；单目标行新增“重命名…” |
| `src/TabTaskExplorer.App/Services/WebPageHtml.cs` | 内联回退页面同步上述两项菜单改动 |

回退方式：

1. 删除 `SftpFileSystemProvider` / `SftpTransferService` 的 `KnownHostStore` 参数与 `HostKeyReceived` 回调，`SshSessionFactory` 恢复两参数构造。
2. `EnsureDiskDirectory` 恢复“先判断根目录为空即返回失败”的顺序。
3. `WebServiceHost` 将 `connhostkeytrust` / `connhostkeyforget` 移回 `needModify`。
4. `actions.js` 与 `WebPageHtml.cs` 删除“新建文件夹…”与“重命名…”两个菜单项。

## 4. 仍待修 / 待验证

- 密码失败锁定与 429 需要在 `RequirePassword=true` 的临时配置下实测；本次因沙箱后端故障未能执行。
- `134-uospro` 的 `Permission denied (password)` 需要用户确认密码或改为 keyboard-interactive 可用凭据；网络恢复后再验证“信任后连接成功、再次连接不再提示”。
- 同目录粘贴同名文件夹应改为生成“副本”或明确提示“目标与源相同”，避免返回成功但无变化。
- 外壳菜单枚举在第三方扩展忙时可能超过 2500 毫秒；可考虑提高超时或改为后台枚举，本次未调整。
- 网页端没有截图/录屏页，若产品要求与桌面端功能一致，需要单独立项实现，不能通过浏览器实测补齐。
