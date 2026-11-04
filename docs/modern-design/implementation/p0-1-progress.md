# Web 端 P0-1 进度（安全基线 / SSE / 前端模块化）

状态：已实测通过
卡点：无运行阻塞；后续 Web 相关改动仍需重新冒烟。
下一步：保持 /api/version、静态资源、会话与 CSRF 的回归冒烟。

本文件是 P0-1 三条任务线的汇总入口，详细改动与证据见：

- [p0-1-1-security.md](p0-1-1-security.md)：会话令牌、CSRF、权限与审计脱敏。
- [p0-1-2-events.md](p0-1-2-events.md)：SSE 事件流与断线重连。
- [p0-1-3-web-modules.md](p0-1-3-web-modules.md)：前端资源拆分与静态资源路由。

## 验证依据

- 浏览器侧已记录：`/api/version` 返回 `staticAssets=true`，六个前端模块全部 200，`/api/session` 正常返回会话与 CSRF 令牌，无 CSRF 的写请求返回 403，事件连接状态为“已连接”。
- 具体记录见 [logging-progress.md](logging-progress.md) 第 7 步与 [capture-progress.md](capture-progress.md) 的浏览器侧验证小节。
