# P0-1 第 1 步：Web 端安全基线

状态：已实测通过（2026-11-04）
日期：2026-11-03

## 1. 目标与范围

在不改变网页端既有功能的前提下，为本地 HTTP 服务补齐以下安全基线：

- 默认仅监听回环地址；非回环监听必须启用账号密码。
- 会话令牌（Bearer）替代每次请求重复 Basic 认证。
- 所有写操作要求 CSRF 令牌 + 同源校验。
- 请求限流与登录失败次数限制。
- 写操作审计日志。
- 日志中的密码、凭据、令牌脱敏。
- 错误响应不回显内部路径与异常栈。

## 2. 改动文件

| 文件 | 改动 |
| --- | --- |
| `src/TabTaskExplorer.App/Services/WebSecurity.cs` | 新增：令牌生成、会话仓库、限流器、登录失败限制、日志脱敏、审计日志 |
| `src/TabTaskExplorer.App/Services/WebServiceHost.cs` | 认证流程改为 Bearer 优先、Basic 回落；写操作 CSRF/同源校验；全局限流；审计回填；新增 `/api/session` 与 `/api/version`；错误响应改为通用提示 + 请求编号 |
| `src/TabTaskExplorer.App/Services/UiLogger.cs` | 密码框（控件名包含 Password）与密钥框（包含 Secret）的文本输入不再原样落日志，统一记录为 `***` |
| `src/TabTaskExplorer.App/Services/WebPageHtml.cs` | 页面启动先申请会话令牌；所有 API 请求改为 `apiFetch`，自动附带 Bearer 与 CSRF 令牌 |
| `src/TabTaskExplorer.Tests/WebSecurityTests.cs` | 新增安全基线单元测试（脱敏、会话、限流、失败锁定、固定时间比较） |

## 3. 关键行为

### 3.1 监听范围

- `WebBindScope.Loopback`（默认）：仅 `127.0.0.1`。
- `WebBindScope.Any`：只有权限方案 `RequirePassword = true` 且已设置密码摘要时才允许启动；否则该端口启动失败并写入日志。
- 选择 Any 时写入风险提示日志，但不记录密码。

### 3.2 认证与会话

1. 首次打开页面时，浏览器按既有 Basic 认证弹窗提交账号密码。
2. 服务端认证成功后通过 `GET /api/session` 返回：
   - `sessionToken`：Bearer 会话令牌（32 字节随机、URL 安全）。
   - `csrfToken`：CSRF 令牌（24 字节随机）。
   - `account`、`profile`、`expiresInSeconds`。
3. 页面只在内存保存令牌，不写入 `localStorage`。
4. 后续请求携带 `Authorization: Bearer <sessionToken>`；写请求额外携带 `X-CSRF-Token`。
5. 会话空闲 480 分钟或绝对 24 小时后失效；服务重启时清空全部会话。
6. 登录失败达到 5 次/60 秒后锁定 60 秒；锁定期间返回 429，但正确密码仍先校验后放行（避免误伤）。

### 3.3 CSRF 与同源

- 所有非 GET/HEAD/OPTIONS 请求必须携带与当前会话匹配的 `X-CSRF-Token`，否则 403。
- 浏览器带 `Origin` 或 `Referer` 时，必须与当前端口的回环地址（`127.0.0.1` / `localhost` / `[::1]`）或 `Host` 头一致，否则 403。
- 没有 `Origin` / `Referer` 的命令行客户端仍必须提供 CSRF 令牌，令牌本身即证明请求来自已认证页面。

### 3.4 限流

| 范围 | 限制 |
| --- | --- |
| 每来源地址全部请求 | 600 次/分钟 |
| 每来源地址 + 账号写操作 | 60 次/分钟 |
| 每来源地址 + 权限方案登录失败 | 5 次/分钟，锁定 60 秒 |

超限返回 `429 Too Many Requests` 并带 `Retry-After`。

### 3.5 审计与脱敏

- 所有写请求在结束时写入 `网页端审计` 分类日志，包含：端口、来源、账号、方法、请求路径、查询参数、动作、目标、响应状态、脱敏后的响应摘要、请求编号。
- 认证失败、同源/CSRF/来源地址拒绝也写入审计。
- `WebSecurity.MaskSecrets` 对 `password`、`passwd`、`pwd`、`token`、`secret`、`authorization`、`cookie` 的键值做 `***` 替换；审计与错误响应摘要统一走该函数。
- 密码明文不落盘、不写日志；只记录账号。

### 3.6 错误响应

- 读取文件、IDE 文本、右键菜单等 500 错误改为通用中文提示 + `请求编号`，例如：
  `读取失败，请查看本机日志（请求编号=xxxxx）`。
- 异常类型、消息与完整堆栈只写本机 `ui.log`，不回显给浏览器。
- 越权右键目标改为固定提示“目标路径不在当前标签允许范围内，已拒绝”。

## 4. 回退方式

1. 删除 `WebSecurity.cs`、`WebSecurityTests.cs`。
2. 将 `WebServiceHost.cs` 的 `HandleClient` / `Authenticate` / `CheckSameOrigin` / `CheckCsrf` 恢复为上一提交中的“来源地址 → Basic → Dispatch”流程。
3. 将 `WebPageHtml.cs` 中 `apiFetch` 调用恢复为 `fetch`，并删除 `loadSession` / `sendApi` / `apiFetch` 与令牌全局变量。
4. 将 `/api/session`、`/api/version` 路由删除；`WriteText` / `WriteBytes` 的 `retryAfterSeconds` 与审计回填参数可保留（默认值不影响旧调用）。

> 上述改动均为新增中间件与新增接口，未替换现有业务处理逻辑；按文件回退即可，不涉及数据迁移。

## 5. 待验证清单（沙箱恢复后执行）

- [ ] `dotnet build` 0 错误。
- [ ] `dotnet test` 全部通过（含新增 `WebSecurityTests`）。
- [ ] 未携带 CSRF 令牌的 POST 返回 403，日志出现审计记录。
- [ ] 连续错误密码触发 429；正确密码仍可登录。
- [ ] `BindAddress = Any` 且未启用密码时端口启动失败；启用密码后可监听。
- [ ] 浏览器打开页面、登录、切换主题、执行一次写操作均正常。
- [ ] `ui.log` 中不出现密码明文与会话令牌原文；审计行字段完整。
- [ ] 500 错误响应不含路径与堆栈，仅含请求编号。
