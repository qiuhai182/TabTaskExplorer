# 诊断工具（查看器 / 诊断包 / 崩溃转储）现代化设计

状态：设计稿（2026-11-03）
适用：`UiLogger`、`WebServiceHost` 诊断、`DebugUiModels`、启动流程
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| Web 诊断缓冲 | `WebServiceHost.RecordDiagnostic`（:53-70）、`GetDiagnostics`（:73-84） | 可用 | 内存最近 100 条，重启丢失 |
| 诊断接口 | `GET /api/diagnostics`（`WebServiceHost.cs:292`） | 可用 | 网页端诊断面板读取 |
| UI 状态快照 | `DebugUiModels.cs`、`MainWindowViewModel.BuildDebugState`（:2989） | 可用 | 供 Web 镜像/调试 |
| 日志文件 | `UiLogger` | 可用 | 单文件，无级别/查看器 |
| 日志查看器 | 无 | 缺口 | 只能读文件 |
| 诊断包导出 | 无 | 缺口 | 用户报障需手动收集日志/配置/系统信息 |
| 崩溃转储 | 无 | 缺口 | 无 minidump/异常报告 |
| 系统信息采集 | 半成品 | 半成品 | 零散日志，无统一环境快照 |
| 请求 ID | 无 | 缺口 | Web 请求失败难对应日志 |
| 自检 | 无 | 缺口 | FFmpeg/libvlc/WebView/Tesseract 状态无汇总页 |

主要问题：

1. **诊断数据分散**：日志文件、Web 内存缓冲、设置、显示器/依赖版本各处，用户无法一键提供。
2. **无应用内查看器**：普通用户找不到 `ui.log`；开发者也要手动打开/搜索。
3. **Web 诊断重启即丢**：100 条内存缓冲，崩溃后无法追溯。
4. **无崩溃转储**：异常退出后只有日志，缺少堆栈/线程/内存快照。
5. **无请求 ID**：网页端 500/失败无法与日志精确对应。
6. **无环境自检**：FFmpeg、libvlc、WebView、Tesseract、OCR 语言包是否可用，没有汇总页。
7. **无诊断包脱敏选项**：路径/用户名/主机名可能泄露。
8. **无“一键复现信息”**：版本、构建号、操作系统、DPI、显示器布局未统一记录。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| VS Code | Output/Logs 通道、`Developer: Open Logs Folder`、诊断报告 | 应用内查看器 + 导出 |
| Windows Error Reporting | 崩溃转储、报告上传（可选）（[WER](https://learn.microsoft.com/en-us/windows/win32/wer/windows-error-reporting)） | minidump |
| ProcDump / dotnet-dump | 按需抓取 dump（[ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump)） | 崩溃/卡死转储 |
| Chrome/Edge | `chrome://diagnostics`、`chrome://crashes` | 自检页 |
| .NET Diagnostics | 运行时指标/事件/转储（[dotnet-diagnostics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/)） | 性能/崩溃 |
| OWASP | 日志脱敏/审计 | 诊断包脱敏 |

## 3. 推荐方案

### 必做（P0）

1. **应用内日志查看器**
   - 设置“关于/诊断”页打开；实时 tail、通道/级别/关键字过滤、暂停滚动、复制、导出。
   - 接入点：新增 `Views/LogViewerWindow.axaml(.cs)`；`UiLogger` 增加 5000 条内存环形缓冲。

2. **诊断包导出**
   - 一键打包：最近 7 天日志、设置（脱敏）、系统信息（OS/版本/架构/显示器/DPI/依赖版本）、Web 诊断、最近崩溃、当前标签状态。
   - 输出 `output/diagnostics-<timestamp>.zip` 或用户选择目录；提供“隐藏用户目录/隐藏主机名”。
   - 接入点：新增 `Services/DiagnosticsBundle.cs`；`SettingsWindow` 按钮。

3. **崩溃转储**
   - 未处理异常/致命错误时写 minidump（Windows）或核心转储（其他平台尽力）；记录构建号、线程、模块。
   - 下次启动检测并提示“上次异常退出，是否打开诊断包”。
   - 接入点：`Program.cs`、`App.axaml.cs`；`WindowsErrorReporting`/`MiniDumpWriteDump`；非 Windows 用 `dotnet-dump` 可选。

4. **Web 诊断持久化**
   - `RecordDiagnostic` 同时写 `diagnostics.jsonl`（滚动 1MB）；`/api/diagnostics` 读文件；请求生成 `requestId` 并回传响应头。
   - 接入点：`WebServiceHost.cs`、`Assets/web/app.js`。

5. **环境自检页**
   - 汇总：版本/构建、OS、FFmpeg 路径/版本、libvlc、WebView、Tesseract/OCR 语言包、Web 端口/监听、磁盘空间、日志路径。
   - 每项“可用/不可用/需配置”，提供跳转设置。
   - 接入点：新增 `Services/EnvironmentDiagnostics.cs`；`SettingsWindow` 诊断分页。

### 可选（P1/P2）

- 性能诊断：启动耗时、目录加载、截图耗时、UI 卡顿采样。
- 线程转储/句柄泄漏检测（Windows）。
- 诊断包上传（用户显式选择，默认关闭）。
- “安全模式”启动（禁用扩展/全局钩子/Web 服务）用于排障。

### 不建议

- 默认自动上传诊断包：隐私风险。
- 诊断包包含密码/令牌/完整路径：必须脱敏选项。
- 在 UI 线程生成大 zip：后台执行 + 进度。

## 4. 交互与视觉

- 诊断页：环境卡片（绿色/黄色/红色）+ 日志查看器入口 + 导出诊断包。
- 日志查看器：顶部通道/级别/搜索；列表等宽字体；底部“复制选中/导出”。
- 导出对话框：范围（7/30 天）、脱敏选项、目标目录。
- 崩溃提示：启动横幅“上次异常退出”，按钮“打开诊断包/忽略”。
- Web 错误页：显示 requestId，提示“在诊断包中搜索该编号”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 查看器 | 新增 `Views/LogViewerWindow.axaml(.cs)`；`UiLogger` 环形缓冲 | 实时 tail |
| 诊断包 | 新增 `Services/DiagnosticsBundle.cs` | zip + 脱敏 |
| 崩溃 | `Program.cs`、`App.axaml.cs`、`MiniDumpWriteDump` | 平台差异 |
| Web 诊断 | `WebServiceHost.cs`、`Assets/web/app.js` | requestId/JSONL |
| 自检 | 新增 `Services/EnvironmentDiagnostics.cs` | 依赖/版本 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 查看器 | 大日志读取性能 | M |
| 诊断包 | zip 体积/脱敏遗漏 | M |
| 崩溃 dump | 平台/权限/文件大小 | M |
| Web requestId | 所有响应/错误路径改造 | S/M |
| 自检 | 依赖探测误报 | S |

## 7. 验收标准

1. 日志查看器可实时 tail、过滤、暂停、复制、导出。
2. 诊断包包含日志/脱敏设置/系统信息/Web 诊断/崩溃记录，zip 可打开。
3. 模拟未处理异常后生成 dump，下次启动提示。
4. Web 失败响应带 requestId，可在日志/诊断包中检索到。
5. 环境自检正确显示 FFmpeg/libvlc/WebView/Tesseract/OCR 语言包状态。
6. 导出可选隐藏用户目录/主机名，默认脱敏。
7. 生成诊断包不阻塞 UI，显示进度。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows Error Reporting](https://learn.microsoft.com/en-us/windows/win32/wer/windows-error-reporting) | 崩溃转储/报告 | 2026-11-03 |
| [ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump) | 按需 dump | 2026-11-03 |
| [.NET Diagnostics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/) | 运行时诊断/转储 | 2026-11-03 |
| [VS Code Output/Logs](https://code.visualstudio.com/docs/editor/output) | 应用内日志/诊断 | 2026-11-03 |
| [OWASP Logging](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html) | 脱敏/审计 | 2026-11-03 |
