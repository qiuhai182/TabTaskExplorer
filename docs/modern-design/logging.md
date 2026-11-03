# 日志服务现代化设计

状态：设计稿（2026-11-03）
适用：`UiLogger`、各模块日志调用
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 单文件日志 | `Services/UiLogger.cs:15-347` | 可用 | `%APPDATA%\TabTaskExplorer\ui.log`，UTF-8，行式 |
| 容量控制 | `UiLogger.MaxFileBytes`（:23） | 可用 | 128MB 上限，超出裁剪到最新 64MB，按行顶出 |
| 分类 | `UiLogger.Log(category, message)` | 可用 | 截图、录屏、程序行为、快捷键、网页端等 |
| 异常 | `LogError`（:59-76） | 可用 | 类型/信息/堆栈，内部异常 3 层 |
| 全局输入 | `AttachWindow`（:92-102） | 可用 | 鼠标/键盘/文本/滚轮/窗口开关 |
| 中文可读 | 全文件 | 可用 | 全参数、无缩写、纯中文（项目约定） |
| 级别 | 无 | 缺口 | 全部 INFO，无法只看错误 |
| 按日期轮转 | 无 | 缺口 | 单文件滚动裁剪，跨天混合 |
| 结构化字段 | 无 | 缺口 | 纯文本，难机器分析 |
| 脱敏 | 无 | 缺口 | 文本输入/密码/令牌可能被记录 |
| 操作关联 ID | 无 | 缺口 | 一次操作多条日志无法串联 |
| 应用内查看器 | 无 | 缺口 | 只能去文件系统找 |
| 崩溃处理 | 无 | 缺口 | 未处理异常可能直接退出 |

主要问题：

1. **隐私风险**：`OnTextInput` 记录所有输入文本，密码框/令牌可能进入日志；分享日志会泄露。
2. **无级别**：鼠标/滚轮/输入日志淹没错误，排查效率低。
3. **单文件竞争/裁剪卡顿**：所有线程追加同一文件；裁剪时复制 64MB 可能造成卡顿。
4. **无关联 ID**：截图、传输、网页请求的多条日志无法串成一次操作。
5. **无应用内查看**：普通用户不知道日志位置；开发者需手动打开。
6. **无结构化输出**：无法统计失败率/耗时/快捷键冲突次数。
7. **无崩溃钩子**：`AppDomain.UnhandledException` 等未统一记录。

## 2. 业界对照

| 产品/规范 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Serilog | 级别、结构化字段、Sink、滚动/保留（[Serilog](https://serilog.net/)） | 人类可读 + 结构化双输出 |
| .NET ILogger | 级别、分类、作用域、结构化参数（[.NET Logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging)） | 与现有分类兼容 |
| OWASP Logging | 不记录敏感信息、记录认证/失败/审计（[Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)） | 密码/令牌脱敏 |
| Windows Event Log | 级别/来源/事件 ID | 致命错误可写事件日志（可选） |
| VS Code Output/Logs | 应用内日志面板/通道 | 查看器体验 |

## 3. 推荐方案

### 必做（P0）

1. **级别与通道**
   - `LogLevel`：Trace/Debug/Info/Warn/Error/Fatal；`LogChannel`：UI/Capture/Transfer/Web/Startup/Remote/Update。
   - 保留 `Log(category, message)` 作为 Info 兼容；新增 `LogDebug/LogWarn/LogError`。
   - 设置项“日志级别”（默认 Info，排查时 Debug）。
   - 接入点：`UiLogger.cs`、`AppSettings.LogLevel`、`SettingsWindow`。

2. **按日期 + 大小轮转**
   - `ui-2026-11-03.log`，单文件 32MB，保留 7 天/总计 256MB；后台裁剪，避免 UI 线程复制大文件。
   - 接入点：`UiLogger.FilePath/TrimOldest` 重构为 `LogRotationPolicy`。

3. **敏感信息脱敏**
   - 密码框/令牌/Authorization/Cookie 不记录原文；路径保留（排障需要），导出时可隐藏用户目录。
   - 接入点：`UiLogger.OnTextInput`、`DescribeSource`、`WebServiceHost` 请求日志。

4. **操作关联 ID**
   - 一次截图/下载/网页请求生成 `operationId`；后续日志带“操作编号=”。
   - 接入点：`UiLogger.BeginOperation(category, name)` 返回 scope；调用方透传。

5. **崩溃钩子**
   - `AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`、Avalonia 异常统一记录；下次启动提示。
   - 接入点：`Program.cs`、`App.axaml.cs`；诊断模块负责 dump/包。

### 可选（P1/P2）

- JSONL 结构化日志（与文本并行）：timestamp/level/channel/operationId/message/data。
- 性能采样：启动耗时、截图耗时、目录加载耗时。
- 可选 Seq/OpenTelemetry Sink（默认关闭，本地优先）。
- 日志签名/完整性（企业排障）。
- 按模块动态开关（截图日志、Web 日志）。

### 不建议

- 默认上传远程日志/遥测：隐私与合规风险，必须 opt-in。
- 记录密码/密钥/完整 Cookie：本地日志也不应。
- 无上限增长：必须轮转/配额。
- 在 UI 线程同步裁剪大日志。

## 4. 交互与视觉

- 日志级别下拉：Error/Warn/Info/Debug/Trace。
- 日志查看器（见 `diagnostics-tools.md`）：通道/级别/关键字过滤，等宽字体，Error 红/Warn 橙。
- 导出：可选“隐藏用户目录/包含设置”。
- 崩溃后：下次启动横幅“上次异常退出，已生成诊断包”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 级别/通道 | `UiLogger.cs`、`AppSettingsStore.cs` | 兼容旧调用 |
| 轮转 | `UiLogger.cs`、新增 `Services/LogRotationPolicy.cs` | 按日期/大小 |
| 脱敏 | `UiLogger.cs`、`WebServiceHost.cs` | 密码框/令牌 |
| 关联 ID | `UiLogger.cs` | AsyncLocal/Scope |
| 崩溃 | `Program.cs`、`App.axaml.cs` | 异常钩子 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 级别改造 | 大量 `Log` 调用需分级 | M |
| 轮转 | 多窗口并发 | S/M |
| 脱敏 | 误伤排障信息 | S/M |
| 关联 ID | 调用面广 | M |

## 7. 验收标准

1. 级别设为 Error 后日志只新增错误/致命记录。
2. 日志按日期分文件，单文件 ≤32MB，保留策略生效。
3. 密码框/Authorization/Cookie 不出现在日志；导出可隐藏用户目录。
4. 一次截图/下载的日志可通过 operationId 串联。
5. 日志写入失败不影响主程序。
6. 裁剪大日志不阻塞 UI（后台执行）。
7. 未处理异常被记录，不静默退出。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Serilog](https://serilog.net/) | 级别/结构化/Sink/滚动 | 2026-11-03 |
| [.NET Logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging) | ILogger/级别/作用域 | 2026-11-03 |
| [OWASP Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html) | 敏感信息/审计/失败记录 | 2026-11-03 |
| [Windows Event Log](https://learn.microsoft.com/en-us/windows/win32/eventlog/event-logging) | 致命错误事件 | 2026-11-03 |
| [VS Code Output](https://code.visualstudio.com/docs/editor/output) | 应用内日志面板 | 2026-11-03 |
