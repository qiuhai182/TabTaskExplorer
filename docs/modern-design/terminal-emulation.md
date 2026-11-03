# 终端仿真（远程终端 / 本地终端）现代化设计

状态：设计稿（2026-11-03）
适用：`TerminalTabViewModel`、`TerminalPanel`、`SshTerminalSession`、`ITerminalSession`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| SSH 终端会话 | `SshTerminalSession.cs:12-140` | 可用 | `ShellStream`，`xterm-256color`，UTF-8 输出 |
| 会话抽象 | `Kernel/Abstractions/ITerminalSession.cs:3-29` | 可用 | Output/Closed/Start/WriteInput/Resize/Close |
| 行模式输入 | `TerminalTabViewModel.SendInput`（:104-114）、`TerminalPanel.axaml:34-38` | 可用 | 输入框回车发送整行 |
| 输出缓冲 | `TerminalTabViewModel.OutputText`（:52-63） | 可用 | 上限 40 万字符，超出丢弃最旧 |
| ANSI 清理 | `TerminalTabViewModel.ansiPattern`（:19-22） | 半成品 | 用正则剥离 CSI/OSC，文本展示，不是终端仿真 |
| 会话日志 | `TerminalTabViewModel.ToggleLogging`（:147-165）、`FlushPendingLogLines`（:218-245） | 可用 | 每秒批量写文件，参考 Xshell 会话日志 |
| 快捷命令 | `TerminalTabViewModel.QuickCommands`（:48）、`TerminalPanel.axaml:14-15` | 可用 | 下拉一键发送 |
| 重连 / 断开 | `TerminalTabViewModel.RequestReconnect`（:125-133）、`TerminalPanel.axaml:17-18` | 可用 | 重连事件由主窗口重建会话 |
| 尺寸同步 | `SshTerminalSession.Resize`（:74-81） | 半成品 | 会话提供 Resize，但 `TerminalPanel` 未调用，尺寸固定 |
| 本地终端 | 无 | 缺口 | README M4（ConPTY/PTY）仍未完成 |
| xterm.js 渲染 | 无 | 缺口 | README 标注“xterm.js 渲染待接入” |
| 多行粘贴确认 | 无 | 缺口 | 输入框直接发送整行 |
| 选择/复制/搜索 | 无 | 缺口 | 只读 TextBox，无终端选区与回滚搜索 |
| 分屏/多会话 | 无 | 缺口 | 每连接一个终端标签 |

主要问题：

1. **不是真正的终端仿真**：ANSI 序列被正则剥离后再显示，vim/top/彩色提示符/进度条/光标控制全部失效；只能做“行模式控制台”。
2. **无法交互式输入**：只能整行发送，无法发送单键、Ctrl+C、方向键、Tab 补全、密码提示；`keyboard-interactive` 认证也不经过终端。
3. **窗口尺寸不同步**：`Resize` 存在但 UI 不调用，远端 `top`/`vim` 按错误列宽渲染。
4. **多行粘贴无确认**：粘贴多行命令会直接执行，危险；缺少 bracketed paste 与“检测到多行，是否粘贴”提示。
5. **无本地终端**：ConPTY/PTY 未接入，无法在本程序内执行本地命令。
6. **无回滚搜索/复制**：长输出只能肉眼找；无法选中复制。
7. **无终端内链接/文件路径识别**：URL、文件路径不可点击。
8. **无终端会话恢复**：断线重连会新建 shell，历史输出保留但远端状态丢失。
9. **无分屏/标签组**：多主机操作需要来回切标签。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| xterm.js | 浏览器级终端仿真，支持 VT/xterm 序列、选区、链接、主题、插件（[xterm.js](https://xtermjs.org/)） | README 已规划；应作为终端渲染层 |
| Windows ConPTY | 伪控制台会话，支持本地 shell 与终端应用（[ConPTY](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)） | 本地终端标准路径 |
| Xshell | 标签终端、会话日志、快捷命令、多窗口、VT 兼容 | 功能对标 |
| SecureCRT | 终端仿真、标签、脚本、会话管理 | 仿真完整度参考 |
| Windows Terminal | 多标签/分屏、GPU 渲染、配置文件、默认终端 | 本地终端体验参考 |
| Bracketed Paste | 终端与 shell 协商多行粘贴，避免误执行（xterm 控制序列） | 必须实现的安全交互 |

## 3. 推荐方案

### 必做（P0）

1. **接入 xterm.js 渲染**
   - 用 WebView（Windows WebView2，macOS WKWebView，Linux WebKitGTK）承载 xterm.js；`ITerminalSession` 输出写入 `term.write`，输入通过 `term.onData` 回传 `WriteInput`。
   - 保留现有 `TerminalTabViewModel` 的日志/快捷命令/重连逻辑，输出缓冲改为 xterm scrollback。
   - 接入点：`TerminalPanel.axaml` 替换只读 TextBox；新增 `Services/TerminalWebViewHost`；`TerminalTabViewModel` 增加 `WriteRaw/Input` 通道。
   - 注意：WebView 依赖与跨平台差异需要封装在 `Platform.*`，App 层不直接引用。

2. **原始输入与 resize**
   - `term.onData` 直接发送字节（含 Ctrl 组合、方向键、Tab）；面板尺寸变化调用 `Resize(cols, rows)`，并同步到 `SshTerminalSession.Resize`。
   - 接入点：`TerminalPanel.axaml.cs` 监听 `SizeChanged`；`TerminalTabViewModel` 新增 `ResizeTerminal`。

3. **多行粘贴保护**
   - 启用 bracketed paste；粘贴内容含换行时弹确认“检测到 N 行，是否粘贴？”，可选择“逐行确认/全部粘贴/取消”。
   - 接入点：xterm.js `attachCustomKeyEventHandler`/`onPaste`；`TerminalTabViewModel` 增加 `ConfirmMultilinePaste` 回调。
   - 验收：粘贴 `rm -rf` 多行必须确认。

4. **本地终端**
   - Windows 用 ConPTY，Linux/macOS 用 PTY，新增 `LocalTerminalSession : ITerminalSession`；与 SSH 终端共用面板、日志、快捷命令。
   - 接入点：`Platform.Windows/ConPtyTerminalSession.cs`、`Platform.Linux/UnixPtyTerminalSession.cs`、`Platform.MacOS/UnixPtyTerminalSession.cs`；`MainWindowViewModel.OpenLocalTerminalTab`。
   - 这是 README M4 的收尾。

5. **复制/搜索/链接**
   - xterm 选区复制、右键粘贴、Ctrl+Shift+F 搜索回滚、URL/路径可点击（`WebLinksAddon`）。
   - 接入点：`TerminalPanel` 与 xterm addons；`TerminalTabViewModel` 暴露当前选中文本。

### 可选（P1/P2）

- 分屏（水平/垂直）与终端标签组。
- 会话模板/启动命令、登录脚本。
- 终端主题、字体、光标样式、透明度。
- ZMODEM/SFTP 拖放上传（终端内 `rz/sz` 兼容）。
- 会话录制为 asciinema cast。
- 本地终端配置文件（PowerShell/bash/zsh）。

### 不建议

- 继续用“正则剥离 ANSI + TextBox”作为唯一终端：无法支持交互式程序。
- 自研完整 VT 解析器：优先 xterm.js。
- 在终端内直接执行未确认的多行粘贴。
- 把 SSH 密码提示放到终端输入框（应使用独立认证对话框）。

## 4. 交互与视觉

- 终端区域：等宽字体（Cascadia Code/Consolas/DejaVu Sans Mono），深色背景，16 色/256 色主题。
- 顶部工具栏：连接状态、发送、清屏、日志开关、快捷命令、重连、断开、分屏（P1）。
- 底部状态：`user@host:port`、编码、行列、连接时长。
- 粘贴确认：居中对话框显示前 5 行与总行数，默认“取消”。
- 断线：红色横幅“连接已断开”，按钮“重连/关闭”；日志继续保留。
- 本地终端：标题显示 shell 名称与当前目录（OSC 7 可解析）。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| xterm.js | `Views/TerminalPanel.axaml(.cs)`、新增 `Services/TerminalWebViewHost.cs`、`Assets/terminal/*` | WebView 按平台封装 |
| 原始输入 | `TerminalTabViewModel.cs`、`SshTerminalSession.cs` | `onData` → `WriteInput` |
| Resize | `TerminalPanel.axaml.cs`、`TerminalTabViewModel.cs` | 行列计算按字体尺寸 |
| 多行粘贴 | xterm addon、`TerminalTabViewModel.cs` | bracketed paste |
| 本地终端 | 新增 `Platform.Windows/ConPtyTerminalSession.cs`、`Platform.Linux/UnixPtyTerminalSession.cs` | 复用 `ITerminalSession` |
| 日志 | `TerminalTabViewModel.cs` | 保留现有批量写盘 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| xterm.js + WebView | 三平台 WebView 差异、打包体积、输入法 | L |
| 本地终端 | ConPTY/PTY 生命周期、窗口尺寸、信号 | M/L |
| 多行粘贴 | 与 bracketed paste 兼容性 | S |
| 终端主题/字体 | 跨平台渲染差异 | S |
| 分屏 | 布局与焦点管理 | M |

## 7. 验收标准

1. `vim`、`top`、彩色提示符、进度条在终端中正常渲染与交互。
2. 方向键、Tab、Ctrl+C、Ctrl+D 可发送；窗口缩放后远端行列同步。
3. 粘贴多行命令时弹出确认；选择“取消”不发送任何字符。
4. 本地终端可启动 PowerShell/bash/zsh，执行命令与颜色正常。
5. 终端内可选中复制、右键粘贴、搜索回滚、点击 URL。
6. 断线后重连，历史输出保留，新会话可继续输入。
7. 会话日志文件内容与终端输出一致（去除控制序列或保留原文可配置）。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [xterm.js](https://xtermjs.org/) | 终端仿真、addons、选区/链接 | 2026-11-03 |
| [Windows ConPTY](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session) | 本地终端伪控制台 | 2026-11-03 |
| [Xterm Control Sequences](https://invisible-island.net/xterm/ctlseqs/ctlseqs.html) | VT/xterm 序列与 bracketed paste | 2026-11-03 |
| [Xshell](https://www.netsarang.com/en/xshell/) | 标签终端/日志/快捷命令 | 2026-11-03 |
| [Windows Terminal](https://learn.microsoft.com/en-us/windows/terminal/) | 本地终端体验 | 2026-11-03 |
