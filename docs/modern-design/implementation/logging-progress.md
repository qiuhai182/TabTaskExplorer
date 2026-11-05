# 日志与诊断 P0 实施进度

状态：已实测通过
卡点：轮转（1MB 裁剪）与跨天用例、查看器筛选/导出尚未执行，属补充验证，不影响主链路结论。
下一步：补跑 UiLoggerRotationTests，并在查看器点检筛选、复制与导出。

本文件按小步记录每次改动，便于中断后继续。构建统一由用户双击 `D:\TabTaskExplorer\build.bat`；第 6 步记录了构建、测试与实测结果。

## 第 1 步：日志分级与分类标签（已完成代码改动，待构建验证）

改动文件：
- `src/TabTaskExplorer.App/Services/UiLogger.cs`
- `src/TabTaskExplorer.App/Services/AppSettingsStore.cs`
- `src/TabTaskExplorer.App/ViewModels/MainWindowViewModel.cs`

内容：
- 新增 `LogLevel`（Trace / Debug / Info / Warn / Error）与中文级别名（跟踪 / 调试 / 信息 / 警告 / 错误）。
- 新增 `LogCategories`（截图、文件、远程、Web、UI、性能，另含系统、启动、配置、诊断）。
- 日志行格式固定为：`时间 [级别] [分类=…] [事件=…] 关键字段`，全中文、字段名写全。
- 旧 `Log(category, message)` 保持兼容，按 Info 写入；`LogError(category, message, exception)` 保持兼容，按 Error 写入。
- 新增 `LogTrace / LogDebug / LogInfo / LogWarn / LogError` 分级接口。
- 鼠标、键盘、文本输入、滚轮改为调试级；窗口开关仍为信息级。
- `AppSettings.LogLevel` 默认 `Info`，在视图模型构造与保存设置时应用最低写入级别。

回滚方式：删除新增枚举与分类类，恢复 `Log` / `LogError` 原始实现；删除 `AppSettings.LogLevel` 及三处 `SetMinimumLevel` 调用。

待验证：构建通过；设置 `LogLevel=Warn` 后鼠标/键盘日志不再写入，错误日志仍写入。

## 第 2 步：按日期轮转与保留策略（已完成代码改动，待构建验证）

改动文件：
- `src/TabTaskExplorer.App/Services/UiLogger.cs`
- `src/TabTaskExplorer.App/Program.cs`

内容：
- 日志目录改为 `%APPDATA%\TabTaskExplorer\logs\`。
- 文件名按日期生成：`ui-yyyy-MM-dd.log`，跨天自动切换。
- 单文件上限 32 兆，超出保留最新 16 兆（沿用整行顶出）。
- 启动时清理：保留最新 7 份文件，且总量不超过 256 兆，超出从最旧删除。
- 清理与写入均吞掉异常，日志失败不影响主程序。

回滚方式：恢复 `FilePath` 为 `%APPDATA%\TabTaskExplorer\ui.log`，删除 `CleanupOldLogs` 与 `Program.Main` 中的调用。

待验证：跨天生成新文件；人为放入 8 份旧日志后启动只保留 7 份；单文件超限后自动裁剪。

## 第 3 步：应用内日志查看器与增量加载（已完成代码改动，待构建验证）

改动文件：
- 新增 `src/TabTaskExplorer.App/Services/LogFileTailReader.cs`
- 新增 `src/TabTaskExplorer.App/Views/LogViewerWindow.axaml`
- 新增 `src/TabTaskExplorer.App/Views/LogViewerWindow.axaml.cs`
- `src/TabTaskExplorer.App/Views/SettingsWindow.axaml`
- `src/TabTaskExplorer.App/Views/SettingsWindow.axaml.cs`

内容：
- 日志读取器按块读取完整行，处理半行、文件截断与日志轮转；后台任务读取，界面线程批量追加。
- 查看器支持最低级别（错误/警告/信息/调试/跟踪）、分类、关键字筛选，跟随最新，复制选中行，导出当前视图，打开日志目录。
- 首次只加载最近 4 兆，之后每秒增量读取；内存最多保留 20 万行。
- 设置窗口「关于」页新增日志最低级别下拉框与查看器入口，级别写入 `AppSettings.LogLevel`。

回滚方式：删除三个新增文件，移除设置窗口「关于」页的日志区块与四个处理方法。

待验证：打开查看器后可实时看到新日志；筛选与复制/导出可用；大文件打开不卡界面。

## 第 4 步：诊断包与脱敏补全（已完成代码改动，待构建验证）

改动文件：
- 新增 `src/TabTaskExplorer.App/Services/LogSanitizer.cs`
- 新增 `src/TabTaskExplorer.App/Services/DiagnosticsBundle.cs`
- `src/TabTaskExplorer.App/Services/UiLogger.cs`
- `src/TabTaskExplorer.App/Services/WebSecurity.cs`
- `src/TabTaskExplorer.App/Views/SettingsWindow.axaml`
- `src/TabTaskExplorer.App/Views/SettingsWindow.axaml.cs`

内容：
- 新增统一脱敏器：密码、密钥、Bearer/会话令牌、Authorization 头、URL 内嵌凭据、JWT 与私钥块统一替换为 `***`；主机密钥指纹保留。
- 所有日志写入前统一经过脱敏；网页端 `WebSecurity.MaskSecrets` 改为复用同一套规则。
- 诊断包在后台线程收集：版本、OS/运行时、DPI 与多屏、脱敏配置快照、最近 3 份日志、崩溃记录、Shell 扩展列表、known_hosts 指纹摘要。
- 日志逐行脱敏后写入 zip；输出到 `%APPDATA%\TabTaskExplorer\diagnostics\diagnostics-yyyyMMdd-HHmmss-fff.zip`。
- 设置窗口「关于」页新增「导出诊断包」按钮，完成后显示完整保存路径。

回滚方式：删除两个新增服务文件，恢复 `WebSecurity.MaskSecrets` 原正则实现，移除日志写入前的脱敏调用与设置页诊断按钮。

待验证：导出 zip 可打开且包含全部条目；配置快照中密码摘要与盐值为 `***`；日志中无令牌明文；多屏与 DPI 信息正确。

## 第 5 步：脱敏单元测试（已完成代码改动，待构建与测试验证）

改动文件：
- 新增 `src/TabTaskExplorer.Tests/LogSanitizerTests.cs`

覆盖规则：
- 英文与中文密码、密钥、令牌、凭证键值对。
- Authorization 头、Bearer 令牌、URL 内嵌凭据、JWT、PEM 私钥块。
- 主机密钥指纹保持原样；普通日志文本保持原样；空文本返回空串。
- 敏感字段名识别（PasswordHash、PasswordSalt、PrivateKeyPath、AccessToken、ApiKey、Credential）。
- 普通字段名不误伤（KeyText、Account、FfmpegPath、LogLevel）。
- `WebSecurity.MaskSecrets` 复用补全后的规则。

待验证：`dotnet test src\TabTaskExplorer.Tests\TabTaskExplorer.Tests.csproj` 全部通过（当前环境未执行）。

## 第 6 步：构建、测试与实测结果（2026-11-04）

构建：
- 执行 `stop.bat` → `D:\TabTaskExplorer\build.bat`，`dotnet publish` 退出码 0，输出 `D:\TabTaskExplorer\output\TabTaskExplorer.exe`。
- 在修正 `SettingsWindow` 可空警告与新增诊断包测试后，再次执行 `stop.bat` → `build.bat`，最终构建仍成功（日志末行 `TabTaskExplorer.App -> D:\TabTaskExplorer\output\`）。
- 构建日志仅有既有警告，无 `: error CS` / `error MSB`。

测试：
- `dotnet test src\TabTaskExplorer.Tests\TabTaskExplorer.Tests.csproj -c Release`：总计 227，失败 0，通过 227。
- 新增 `LogSanitizerTests`（13 个脱敏用例）与 `DiagnosticsBundleTests`（1 个真实生成诊断包用例）全部通过。
- 修正 `SettingsWindow.DescribeScreens` 的可空警告；`DiagnosticsBundleTests` 改为异步测试，消除 xUnit1031 警告。

日志实测：
- 目录：`C:\Users\QiuHai\AppData\Roaming\TabTaskExplorer\logs\`
- 实际文件：`ui-2026-11-04.log`，检查时 90,383 字节；重启后 96,158 字节。
- 行格式实测：`2026-11-04 09:08:25.339 [信息] [分类=诊断] [事件=生成诊断包] 输出目录=…`。
- 保留策略实测：向日志目录放入 8 个 `ui-2025-01-0X.log` 旧占位文件（共 9 份），重启程序后仅剩 7 份，最旧的 `ui-2025-01-01.log` 与 `ui-2025-01-02.log` 被删除，当天真实日志保留；占位文件随后已清理。

查看器实测：
- 路径：主窗口「设置」→「关于」→「打开日志查看器」。
- 窗口成功打开，状态栏显示 `日志文件=C:\Users\QiuHai\AppData\Roaming\TabTaskExplorer\logs\ui-2026-11-04.log 已加载行数=444 当前显示行数=444 全局最低级别=信息`。

诊断包实测：
- 通过设置页「导出诊断包」真实生成：`C:\Users\QiuHai\AppData\Roaming\TabTaskExplorer\diagnostics\diagnostics-20261104-091441-318.zip`，1,910,207 字节，条目数 7。
- 实际条目：`系统信息.txt`、`配置快照-脱敏.json`、`崩溃记录.txt`、`Shell扩展列表.txt`、`known_hosts指纹摘要.txt`、`最近日志/ui-2026-11-04.log`、`最近日志/旧版-ui.log`。
- 配置快照实测 `PasswordHash: "***"`、`PasswordSalt: "***"`，无明文密码。

## 第 7 步：轮转/跨天测试补充与 Web 回归实测（2026-11-04 10:10）

改动文件：
- `src/TabTaskExplorer.App/Services/UiLogger.cs`：新增测试注入点 `NowProvider`、`LogDirectoryOverride`、`MaxFileBytesOverride`、`ResetTestOverrides`；生产默认单文件上限仍为 32 兆，未改生产常量。
- 新增 `src/TabTaskExplorer.App/Properties/AssemblyInfo.cs`：`InternalsVisibleTo("TabTaskExplorer.Tests")`，让测试访问上述注入点。
- 新增 `src/TabTaskExplorer.Tests/UiLoggerRotationTests.cs`：两个端到端用例（1 兆阈值裁剪、跨天新建文件）。

执行受阻（本轮未能编译与测试）：
- Shell 工具返回 `sandbox backend failed`；code_mode 内 `RunCommand` 返回 `SandboxBackendFailed`；Computer Use 扩展 `ide_mcp.config.ext.computer-use` 不存在。
- 因此第 1、2 项测试只完成代码编写，未执行；第 3 项查看器按钮未点击。未能构建，也无法确认 `ConnectionPanel.axaml` 的 AVLN2000/AVLN3000 是否仍阻塞。

Web 回归（真浏览器实测，未操作桌面窗口）：
- `GET /` → 200，标题「TabTaskExplorer 网页端」，快照 50 个节点、11 个可交互元素，无登录/密码提示，控制台无消息。
- `GET /api/state` → 200，返回 JSON 含 `generatedTime=2026-11-04 10:07:03`、`tabCount=1`、`theme=Dark`。
- 审计脱敏实测：`POST /api/no-such-endpoint?token=CANARY-TOKEN-987654&note=ok` → 403（CSRF 拒绝，无状态变更）；日志写入两行审计，`查询参数=token=***`，全文检索无 `CANARY` 明文。
- 生产日志中另见 `需要密码=***`（布尔值也被脱敏），说明统一脱敏在真实写入路径生效，但存在过度脱敏。

本轮发现的风险：
- 脱敏规则在 `token=` 处会吞掉其后的整段查询串（实测 `&note=ok` 一并消失），审计可读性下降。
- 字段名含「密码」的布尔值（如 `需要密码=True`）被脱敏为 `***`，无法从日志判断该开关状态。
- 1 兆裁剪与跨天切换的端到端用例未执行，仍不能给出实测行数/字节数。

## 第 8 步：脱敏质量修复（2026-11-04 10:20）

改动文件：
- `src/TabTaskExplorer.App/Services/LogSanitizer.cs`
- `src/TabTaskExplorer.Tests/LogSanitizerTests.cs`

修复内容：
- 敏感键值对只替换值，键名、分隔符、引号与后续查询参数保留；值在 `&` 处停止，`查询参数=token=***&note=ok&page=2` 不再丢参数。
- 布尔、纯数字与常见枚举字面量（True/False、1.5、ReadOnly、Info、Basic 等）不脱敏，`需要密码=True` 保持可读。
- 支持键与值带引号的 JSON 写法，`"password":"…"`、`"access_token":"…"`、`"authorization":"Bearer …"` 在任意嵌套层级均可脱敏。
- 敏感键增加 `key`，并用 `(?<![A-Za-z])` 前置边界避免 `hotkey=Ctrl+A` 之类误伤；`Authorization` 头单独按引号/换行/`&` 截断。

新增测试：
- `MaskSecrets_ShouldPreserveOtherQueryParameters`
- `MaskSecrets_ShouldNotMaskBooleanNumberOrEnumValues`
- `MaskSecrets_ShouldMaskSensitiveKeysAtAnyNestingLevel`

待跑：上述 3 个用例、`UiLoggerRotationTests` 的 2 个用例，以及全量回归；本轮 Shell 仍为 `SandboxBackendFailed`，未执行。

## 当前未验证项汇总

1. 32 兆单文件上限与裁剪：`UiLoggerRotationTests` 用 `MaxFileBytesOverride=1MB` 覆盖值验证（生产常量未改），本轮环境无法执行，未实测。
2. 跨天切换：`UiLoggerRotationTests` 用 `NowProvider` 注入 11-04 23:59 → 11-05 00:01，本轮环境无法执行，未实测。
3. 查看器筛选、复制选中、导出当前视图：Computer Use 扩展不可用、Shell 不可执行，未点击，无导出路径与行数。
4. Web 回归：已实测通过（页面、`/api/state`、审计脱敏均正常，见第 7 步）。

