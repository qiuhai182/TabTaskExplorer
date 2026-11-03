# P0-1 第 2 步：事件推送（SSE）

状态：代码已落地，**待编译与浏览器实测验证**
日期：2026-11-03

## 1. 选型结论

选择 **SSE（Server-Sent Events）**，不选 WebSocket。

理由：

1. 本场景只需要服务端 → 客户端单向推送；不需要客户端通过同一连接发指令（写操作仍走现有 REST 接口）。
2. 现有 HTTP 服务是手写 `NetworkStream` 解析。SSE 只需新增一个 GET 长连接和分块输出；WebSocket 需要额外实现握手、帧解析、掩码、Ping/Pong、关闭帧，风险与代码量明显更高。
3. 浏览器原生 `EventSource` 自带断线自动重连；WebSocket 需要自行实现退避重连。
4. SSE 是纯文本，便于日志排查；事件格式简单，后续可平滑增加 `event` 类型。
5. 保留低频 `/api/state` 兜底刷新，SSE 异常时页面仍可恢复。

## 2. 消息格式

连接地址：`GET /api/events?w=<工作区编号>&token=<会话令牌>`。

- `w` 为空表示跟随桌面主窗口；有值表示独立工作区。
- `token` 仅用于 `/api/events`：`EventSource` 不能设置 `Authorization` 头，因此通过查询串传递；日志审计对 `token` 做脱敏。
- 响应：`200 OK`，`Content-Type: text/event-stream`，`Transfer-Encoding: chunked`。

### 2.1 首帧 / 重连补发

```text
event: snapshot
id: 1
data: {"version":1,"scope":"desktop","types":["snapshot"],"state":{...DebugUiState...}}
```

### 2.2 状态变化

```text
event: state
id: 2
data: {"version":2,"scope":"desktop","types":["tabs","files"],"state":{...DebugUiState...}}
```

`types` 取值：`tabs`、`files`、`tasks`、`connections`、`process`、`startup`、`ide`、`theme`、`snapshot`。

### 2.3 心跳

```text
: 心跳 2026-11-03 15:00:00
```

每 20 秒一次，用于保持连接与发现断线。

### 2.4 错误

```text
event: error
data: {"code":410,"message":"工作区不存在或已过期，请刷新页面"}
```

发送后服务端关闭该连接；浏览器按 `EventSource` 策略重连。

## 3. 覆盖范围

状态差异按分区比较，变化时推送：

| 分区 | 覆盖内容 |
| --- | --- |
| `tabs` | 标签数量、标题、选中状态、路径、排序、视图、勾选数、摘要 |
| `files` | 当前文件标签路径、条目总数、可见行列表 |
| `tasks` | 任务看板的待办 / 进行中 / 已完成 |
| `connections` | 连接管理列表、当前选中连接 |
| `process` | 进程列表、CPU / 内存走势、选中进程 |
| `startup` | 系统启动项与自定义启动项 |
| `ide` | IDE 模式、资源树、编辑器标签、查找与快速打开 |
| `theme` | 主题、隐藏文件、扩展名、导航、最近访问、提示 |

## 4. 服务端实现

| 文件 | 改动 |
| --- | --- |
| `src/TabTaskExplorer.App/Services/WebEventHub.cs` | 新增：订阅队列、后台比较线程、分区差异、快照补发、错误关闭 |
| `src/TabTaskExplorer.App/Services/WebServiceHost.cs` | 启动/停止事件中枢；新增 `/api/events` 路由与分块传输；写操作完成后请求立即比较 |

要点：

- 后台线程默认 1 秒比较一次；写操作完成会立即唤醒比较，缩短操作反馈延迟。
- 新连接和断线重连都收到 `snapshot`；已连接页面只在分区变化时收到全量状态，空闲时不重复发送 JSON。
- 工作区被清理时推送 `410` 错误并关闭连接。
- 每个连接队列最多保留 64 条消息，慢客户端只保留最新消息。

## 5. 前端实现

| 文件 | 改动 |
| --- | --- |
| `src/TabTaskExplorer.App/Services/WebPageHtml.cs`（内联回退副本） | 新增 `connectEvents`、`applyEventState`、`setConnState`；启动改为 `loadSession → loadPermissions → initMode → connectEvents → refresh` |
| `src/TabTaskExplorer.App/Assets/web/core.js` / `boot.js` | 同上逻辑的静态资源正式版本 |
| `src/TabTaskExplorer.App/Assets/web/index.html` / `app.css` | 状态栏新增连接状态圆点（绿=已连接、黄=重连中、红=离线） |

兜底刷新：

```text
每 10 秒检查一次；仅当 EventSource 未处于 OPEN 状态时请求 /api/state
```

因此正常连接时不会再有 1.5 秒全量轮询；断线或推送不可用时自动恢复。

## 6. 回退方式

1. 将 `boot.js` 启动段恢复为：
   `loadPermissions().then(initMode).then(refresh); pollTimer = setInterval(refresh, 1500);`
2. 删除 `connectEvents` / `applyEventState` / `setConnState` 与连接状态 DOM（可选）。
3. 删除 `WebServiceHost.cs` 中 `/api/events` 路由、`WebEventHub` 字段与启动/停止调用。
4. 删除 `WebEventHub.cs`。
5. 旧 `/api/state` 接口未改动，可直接恢复轮询。

## 7. 待验证清单（沙箱恢复后执行）

- [ ] `dotnet build` 0 错误、`dotnet test` 通过。
- [ ] 浏览器打开页面后状态栏显示“已连接”，网络面板可见 `/api/events` 长连接。
- [ ] 切换标签 / 打开目录 / 新建文件夹后 1 秒内收到 `state` 推送并刷新界面。
- [ ] 空闲 30 秒不产生 `/api/state` 请求，仅有 SSE 心跳。
- [ ] 停止服务再启动，页面自动重连并收到 `snapshot`，界面恢复。
- [ ] 独立工作区关闭 / 过期后收到 `410` 错误并提示刷新。
- [ ] 多端口、多工作区互不串台。
