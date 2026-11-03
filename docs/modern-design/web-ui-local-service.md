# Web UI / 本地服务现代化设计

状态：设计稿（2026-11-03）
适用：`WebServiceHost`、`WebWorkspace`、`WebPageHtml`、`WebAccessControl`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 多端口监听 | `WebServiceHost.Start`（:90-112）、`AppSettings.WebPorts` | 可用 | 每端口独立绑定范围与权限方案 |
| 来源 IP / 路径白名单 | `WebAccessControl.IsAddressAllowed`（:18）、`IsPathAllowed`（:43） | 可用 | 支持 CIDR；目录前缀匹配做了 `D:\Data` vs `D:\Database` 防护 |
| Basic 认证 | `WebAccessControl.TryParseBasic`（:80） | 可用 | 账号密码；无令牌/会话 |
| 读写/增删权限 | `WebAccessProfile.AllowModify/AllowCreateDelete` | 可用 | 网页端按钮按权限禁用 |
| 状态镜像 | `GET /api/state`（`WebServiceHost.cs:298`） | 可用 | 全量 `DebugUiState` |
| 动作接口 | `POST /api/action`（:331）、`/api/file`、`/api/write`、`/api/delete` 等 | 可用 | 文件、IDE、比对、连接、启动项、进程 |
| 多工作区 | `WebWorkspace`、`MainWindowViewModel.CreateWorkspace`（:2525） | 可用 | 每窗口一个工作区，支持独立模式 |
| 诊断面板 | `GET /api/diagnostics`（:292） | 可用 | 最近 100 条 UI 异常 |
| 实时推送 | 无 | 缺口 | 固定 1.5 秒轮询（`WebPageHtml.cs:3370`） |
| 前端工程化 | 无 | 缺口 | 单文件 3119 行，内联 CSS/JS |
| CSRF / 会话令牌 | 无 | 缺口 | Basic 认证 + 无 CSRF token |
| HTTPS | 无 | 缺口 | 纯 HTTP |
| 速率限制 / 审计 | 无 | 缺口 | 无请求频率限制、无操作审计日志 |
| Web 端截图/媒体/终端 | 无 | 缺口 | 明确桌面专用 |

主要问题：

1. **轮询模型**：1.5 秒全量快照，标签切换/文件操作反馈延迟；JSON 体积随目录行数增长。
2. **安全边界不足**：
   - Basic 认证在 HTTP 下明文传输（局域网绑定尤其危险）。
   - 无 CSRF token，恶意网页可诱导浏览器向 `127.0.0.1:15919` 发 POST。
   - 无速率限制，暴力尝试密码/高频写操作可能拖垮桌面端。
   - 无操作审计（谁在何时删除了哪个文件）。
3. **单文件前端**：`WebPageHtml` 难以维护、无法复用、无法做单元测试；字符串拼接易引入 XSS（已有 `esc` 但需审计）。
4. **功能不一致**：网页端只有 8 类标签；截图、媒体、终端、传输队列不可用；右键菜单弱。
5. **权限方案 UI 复杂**：端口与方案耦合，错误配置可能导致“本机可访问但局域网不可访问”难以排查。
6. **无版本/缓存控制**：静态页面与 API 同源，浏览器缓存旧页面导致新功能不出现（历史已踩坑）。
7. **自定义 HTTP 服务器**：`NetworkStream` 手写解析（:1023），对分块传输、Keep-Alive、Range、压缩支持有限。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| VS Code Server / code-server | WebSocket 双向通信、令牌认证、HTTPS/反向代理 | Web 端应事件推送 + 令牌 |
| Jupyter | 令牌 + 密码、CSRF、CORS 限制 | 本地服务的安全基线 |
| Windows 11 文件资源管理器 | 本机体验为主，无 Web 端 | 本项目的 Web 端是差异化，不应牺牲安全 |
| Home Assistant | WebSocket + REST、长期令牌、HTTPS | 设备/本地服务常用模式 |
| PWA | 可安装、离线缓存、响应式 | 网页端可 PWA 化，但需谨慎缓存 |
| OWASP ASVS | 认证、会话、CSRF、输入校验、日志 | 作为安全检查清单 |

## 3. 推荐方案

### 必做（P0）

1. **事件推送替代轮询**
   - `GET /api/events` SSE（简单、单向）或 WebSocket（双向）；事件 `stateVersion + patch`。
   - 保留 `/api/state` 首帧；轮询降为 10 秒心跳/断线检测。
   - 接入点：`WebServiceHost.cs`、`WebPageHtml.app.js`、`MainWindowViewModel.BuildDebugState`。
   - 验收：切换标签 < 200ms；空闲无全量 JSON。

2. **安全基线**
   - 会话令牌：登录后发 `HttpOnly + SameSite=Strict` Cookie（或 `Authorization: Bearer`）；所有写操作校验 CSRF token。
   - 速率限制：登录 5 次/分钟/IP；写操作 60 次/分钟/会话。
   - 默认仅绑定 `127.0.0.1`；绑定 `0.0.0.0` 时强制要求密码 + 明确风险提示。
   - 可选 HTTPS（自签证书）或要求反向代理；文档说明。
   - 审计日志：写操作记录来源 IP、账号、动作、路径、结果，进入 `ui.log` 或独立 `web-audit.log`。
   - 接入点：`WebAccessControl`、`WebServiceHost`、`SettingsWindow` 网页端分页、`UiLogger`。

3. **前端模块化**
   - `Assets/web/`：`index.html`、`app.css`、`app.js`、`views/*.js`、`api.js`、`state.js`。
   - `WebServiceHost` 提供静态资源 + 版本号（`/api/version`），HTML 引用带 hash，避免缓存旧页。
   - 接入点：`WebPageHtml.cs` 拆分；`WebServiceHost` 静态文件路由。
   - 约束：无 npm 构建，纯 ES module + 浏览器原生能力。

4. **HTTP 服务器健壮性**
   - 补齐 Keep-Alive、Content-Length、Chunked、Range（媒体/大文件）、gzip/br（可选）、请求体上限、超时。
   - 或评估迁移到 `Kestrel`（ASP.NET Core 托管），减少手写解析风险；但会引入 ASP.NET 依赖，需权衡。
   - 接入点：`WebServiceHost.cs` 的 `WebEndpoint`/`HttpRequest.Read`。

5. **功能一致性分级**
   - `ITabView` 增加 `WebSupport`（Full/Summary/None）；网页端对 None 显示“桌面专用”卡片 + 说明。
   - 右键菜单：先做本程序命令，再按权限放行系统菜单（见 `shell-context-menu.md`）。
   - 接入点：`TabContract.cs`、`WebPageHtml`、`WebWorkspace`。

### 可选（P1/P2）

- PWA：可安装、离线壳、推送（本地服务推送需权限）。
- 文件上传分块 + 断点续传（配合传输中心）。
- 多用户/多会话（不同权限方案对应不同账号）。
- Web 端截图预览（只读展示产物，不执行采集）。
- 反向代理/隧道（Tailscale/Cloudflare Tunnel）文档，不内置。
- API 版本化（`/api/v1/...`）与 OpenAPI 描述。

### 不建议

- 在公网直接暴露服务：默认禁止，必须用户显式配置并承担风险。
- 在网页端执行任意 Shell 命令：只允许白名单动作。
- 用 Basic 认证 + HTTP 绑定局域网作为默认：必须改为令牌 + HTTPS/密码 + 明确警告。
- 继续扩展单体 `WebPageHtml`：维护成本会指数上升。

## 4. 交互与视觉

- 网页端布局与桌面同构，但顶部增加“桌面专用/只读”徽标。
- 连接状态：右上角圆点（已连接/重连中/离线）；断线自动重连并提示。
- 权限提示：按钮禁用时 tooltip“当前权限方案不允许删除”。
- 移动端：< 900px 时标签条横向滚动、侧边栏抽屉化、表格横向滚动。
- 登录页：简洁品牌 + 密码 + “仅在可信网络使用”提示。
- 错误页：显示错误码、请求 ID，便于对照日志。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| SSE/WS | `WebServiceHost.cs`、`Assets/web/app.js` | 事件合并、断线重连 |
| 令牌/CSRF | `WebAccessControl.cs`、`WebServiceHost.cs` | Cookie + token 双校验 |
| 限流/审计 | `WebServiceHost.cs`、新增 `Services/WebAuditLog.cs` | 按 IP/会话 |
| 前端拆分 | `WebPageHtml.cs` → `Assets/web/*` | 先 CSS 后 JS |
| 静态资源/版本 | `WebServiceHost.cs`、`Assets/web/index.html` | hash 查询参数 |
| HTTP 健壮性 | `WebServiceHost.cs` 或迁移 Kestrel | 需评估依赖 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| SSE/WS | 多端口/多工作区隔离、代理缓冲 | M |
| 安全改造 | 认证迁移可能锁死用户；需兼容旧密码 | M |
| 前端拆分 | 迁移期功能回归 | M/L |
| HTTP 服务器 | 手写解析边界多；Kestrel 引入依赖 | M/L |
| PWA/上传 | 缓存与权限 | M |

## 7. 验收标准

1. 网页端标签切换/目录切换 < 200ms 可见；空闲时无 1.5 秒全量请求。
2. 未携带 CSRF token 的写请求被拒绝（403），日志记录来源与动作。
3. 连续 10 次错误密码后触发限流；正确密码可登录。
4. 默认仅 `127.0.0.1` 可访问；绑定局域网时设置页有红色风险提示且必须启用密码。
5. 所有写操作进入审计日志（时间、IP、账号、动作、路径、结果）。
6. 浏览器强制刷新后加载新版本页面；旧缓存不会导致功能缺失。
7. 大文件下载/预览支持 Range；请求体超限返回 413 而非崩溃。
8. 网页端对桌面专用标签显示明确占位，不出现空白/报错。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [VS Code Server](https://code.visualstudio.com/docs/remote/vscode-server) | WebSocket/令牌/远程访问模式 | 2026-11-03 |
| [Jupyter Server 安全](https://jupyter-server.readthedocs.io/en/latest/operators/security.html) | 令牌、CSRF、CORS | 2026-11-03 |
| [OWASP ASVS](https://owasp.org/www-project-application-security-verification-standard/) | 认证/会话/输入校验/日志基线 | 2026-11-03 |
| [PWA 文档](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps) | 可安装/离线/缓存 | 2026-11-03 |
| [Home Assistant WebSocket API](https://developers.home-assistant.io/docs/api/websocket/) | 本地服务事件推送 | 2026-11-03 |
