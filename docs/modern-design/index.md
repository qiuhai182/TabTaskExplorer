# 现代化模块设计 · 总览与优先级路线图

状态：设计稿（2026-11-03）
范围：`d:\TabTaskExplorer` 的 **41 个功能模块**
约束：本阶段只写 `docs/`，不改 `src/`，不提交重构；与正在进行的截图 / 右键 / 拖拽任务解耦。

## 1. 结论

- 本轮按“独立界面 + 独立服务/协议”重新盘点，共识别 **41 个模块**：24 个可用、13 个半成品、4 个缺口。
- 上一版 20 个模块遗漏 21 个，主要是把“远程连接”塞进标签/浏览、把比对/压缩/批量改名塞进文件视图、把贴图/标注、任务栏/托盘、日志/诊断、Web 服务/面板各自合并成一份文档。
- 最值得先做的三件事（已按安全风险调整）：
  1. **Web 端事件推送 + 安全基线 + 面板拆分**——所有跨端能力的地基。
  2. **远程连接安全闭环**——主机密钥校验、跨平台凭据加密、FTPS；当前存在明文凭据与中间人风险。
  3. **文件视图 + 统一文件操作/进度**——缩略图、空格预览、列配置、冲突处理。

## 2. 模块清单（41）

| # | 模块 | 文档 | 成熟度 | 优先级 | 主要缺口 | 工作量 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 标签容器与浏览器内核 | [tab-shell-browser-kernel.md](tab-shell-browser-kernel.md) | 半成品 | P0 | 轮询镜像、单体前端、无标签组/休眠、无内嵌浏览器 | M |
| 2 | 文件/文件夹视图 | [file-folder-view.md](file-folder-view.md) | 可用 | P0 | 缩略图、快速预览、列配置、冲突/进度、标准选择 | M |
| 3 | 磁盘视图 | [disk-view.md](disk-view.md) | 可用 | P2 | Treemap、Top 文件、类型分布、取消/排除、导出 | M |
| 4 | IDE 模式 | [ide-mode.md](ide-mode.md) | 可用 | P1 | 语法高亮、编码切换、保存冲突、信任模型 | M |
| 5 | 截图 | [screenshot.md](screenshot.md) | 可用 | P0 | GDI 局限、剪贴板只有文件、窗口选择器/动作链 | M |
| 6 | 录屏 | [screen-recording.md](screen-recording.md) | 可用 | P1 | 系统音频、暂停、崩溃恢复、录制指示 | M/L |
| 7 | 标注编辑器 | [annotation-editor.md](annotation-editor.md) | 可用 | P1 | 对象不可再编辑、文字/序号弱、无旋转/层级 | M |
| 8 | 贴图窗口 | [pin-window.md](pin-window.md) | 可用 | P1 | 无贴图管理/持久化、点击穿透、虚拟桌面、DPI | M |
| 9 | OCR | [ocr.md](ocr.md) | 半成品 | P1 | PowerShell 子进程、语言包 UI、区域 OCR、结果面板 | M |
| 10 | 窗口识别 | [window-detection.md](window-detection.md) | 可用 | P2 | 无 UIA 回退、无跨平台、候选信息弱 | M |
| 11 | 滚动长截图 | [scrolling-long-screenshot.md](scrolling-long-screenshot.md) | 缺口 | P1 | 零实现；拼接/滚动/固定头尾/手动兜底 | L |
| 12 | 远程连接（SSH/SFTP/FTP 会话） | [remote-connection.md](remote-connection.md) | 半成品 | **P0** | 无主机密钥校验、非 Windows 明文凭据、无跳板/FTPS、无连接池 | M/L |
| 13 | 连接管理（主机/凭据/分组） | [connection-manager.md](connection-manager.md) | 半成品 | P1 | 无分组/搜索/克隆、测试仅 TCP、无主机密钥 UI | M |
| 14 | 终端仿真 | [terminal-emulation.md](terminal-emulation.md) | 半成品 | P1 | 行模式、无 VT/xterm、无 resize、无本地终端、多行粘贴无确认 | L |
| 15 | 全局搜索 | [global-search.md](global-search.md) | 可用 | P1 | 无内容搜索、历史/保存搜索、高级过滤、取消 | M |
| 16 | 书签与快速访问 | [bookmarks-quick-access.md](bookmarks-quick-access.md) | 可用 | P1 | 最近访问不持久化、无分组/排序/搜索/失效检测 | M |
| 17 | 工作区与会话恢复 | [workspace-session.md](workspace-session.md) | 半成品 | P1 | 只恢复文件路径、无窗口布局/命名工作区/崩溃恢复 | M |
| 18 | 剪贴板 | [clipboard.md](clipboard.md) | 半成品 | P1 | 无图片格式、无历史、内容类型单一、无粘贴为 | M |
| 19 | 系统集成（默认程序/关联/发送到） | [system-integration.md](system-integration.md) | 半成品 | P1 | 接管范围窄、无文件关联/SendTo/安装器/便携模式 | M |
| 20 | 通知 | [notifications.md](notifications.md) | 缺口 | P1 | 无通知服务/中心/分类/免打扰 | M |
| 21 | 下载/进度 | [transfer-progress.md](transfer-progress.md) | 半成品 | P0 | 进度孤岛、无速度/ETA、任务栏进度、HTTP 下载 | M |
| 22 | 启动项管理 | [startup-manager.md](startup-manager.md) | 可用 | P2 | 影响评分、签名、详情、快照、提权入口 | M |
| 23 | 进程监控 | [process-monitor.md](process-monitor.md) | 可用 | P2 | 搜索/过滤、列配置、危险防护、GPU/网络、树操作 | M |
| 24 | 任务看板 | [task-board.md](task-board.md) | 可用 | P2 | 编辑/优先级/截止/标签、拖拽、搜索/归档、撤销 | M |
| 25 | 媒体查看器 | [media-viewer.md](media-viewer.md) | 可用 | P2 | 缩略图、进度记忆、字幕、硬解、远程/压缩包媒体 | M |
| 26 | 目录/文件比对 | [compare.md](compare.md) | 可用 | P2 | 忽略规则、同步、二进制/图片、报告、编码 | M |
| 27 | 压缩包/归档 | [archive.md](archive.md) | 可用 | P2 | 密码、测试、压缩配置、预览、任务中心接入 | M |
| 28 | 批量重命名 | [batch-rename.md](batch-rename.md) | 可用 | P2 | 正则/大小写/序号补零/日期、冲突策略、撤销 | M |
| 29 | 截图辅助工具（取色/条码） | [capture-utilities.md](capture-utilities.md) | 可用 | P2 | 颜色历史/格式、条码独立入口/多码、二维码生成 | S/M |
| 30 | 右键菜单/Shell 集成 | [shell-context-menu.md](shell-context-menu.md) | 可用 | P1 | 同步构建、无 Win11 现代菜单、图标/盾牌、Web 端弱 | M |
| 31 | 拖放 | [drag-and-drop.md](drag-and-drop.md) | 可用 | P1 | 无拖拽视觉、自动滚动/悬停展开、远程互拖、跨窗口 | M |
| 32 | 快捷键/热键 | [shortcuts-hotkeys.md](shortcuts-hotkeys.md) | 可用 | P1 | 无冲突检测、默认低级钩子、无录制 UI、跨平台弱 | M |
| 33 | 任务栏集成 | [taskbar-integration.md](taskbar-integration.md) | 半成品 | P2 | 无 Jump List、缩略图工具栏、进度/覆盖图标 | M |
| 34 | 托盘菜单 | [tray-menu.md](tray-menu.md) | 可用 | P2 | 静态菜单、无状态/最近/多窗口/任务中心/提示 | S/M |
| 35 | 设置 | [settings.md](settings.md) | 可用 | P1 | 无搜索、导入导出、校验、分页瘦身 | M |
| 36 | Web UI/本地服务 | [web-ui-local-service.md](web-ui-local-service.md) | 半成品 | P0 | 轮询、单体前端、CSRF/令牌/HTTPS/限流/审计缺失 | M/L |
| 37 | Web 端面板 | [web-panels.md](web-panels.md) | 半成品 | P0 | 面板能力无声明、桌面专用无解释、右键弱、响应式/可访问性 | M |
| 38 | 编码转换 | [encoding-conversion.md](encoding-conversion.md) | 可用 | P2 | 仅文件名、无内容转码、冲突/回滚 | M |
| 39 | 日志服务 | [logging.md](logging.md) | 半成品 | P0 | 无级别/轮转/脱敏/关联 ID/崩溃钩子 | M |
| 40 | 诊断工具 | [diagnostics-tools.md](diagnostics-tools.md) | 缺口 | P0 | 无查看器/诊断包/崩溃转储/自检/请求 ID | M |
| 41 | 同步/上传（规划中） | [sync-upload.md](sync-upload.md) | 缺口 | P2 | 无上传目标/同步/冲突/校验/审计 | L |

> 成熟度口径：可用=核心路径已落地；半成品=有实现但缺关键能力或体验明显不完整；缺口=无实现或只有规划。

## 3. 优先级路线图

### 批次 A：安全与地基（P0）

1. **Web 事件推送 + 安全基线 + 面板拆分**
   - `tab-shell-browser-kernel.md`（SSE/增量、标签注册表）、`web-ui-local-service.md`（令牌/CSRF/限流/审计）、`web-panels.md`（能力声明/组件化）。
2. **远程连接安全闭环**
   - `remote-connection.md`：主机密钥校验/known_hosts、跨平台凭据（DPAPI/Keychain/libsecret）、FTPS、连接池。
   - 这是本轮新增的最高优先安全项；跳板机/Agent 放 P1。
3. **日志与诊断**
   - `logging.md`（级别/轮转/脱敏/关联 ID）、`diagnostics-tools.md`（查看器/诊断包/崩溃转储）。
4. **统一进度中心**
   - `transfer-progress.md`：`IProgressCenter` + 文件操作 + 任务栏进度。

### 批次 B：核心体验（P0）

5. **文件视图 + 文件操作**
   - `file-folder-view.md`：缩略图、Space 预览、列配置、`IFileOperationService`、标准选择。
6. **截图链路**
   - `screenshot.md`：WGC、剪贴板图片、窗口选择器、动作链、元数据索引。
7. **标签能力**
   - `tab-shell-browser-kernel.md`：标签组、最近关闭、搜索、休眠；垂直标签可选。

### 批次 C：连接与工作流（P1）

8. **远程连接体验**
   - `connection-manager.md`（分组/搜索/深度测试/主机密钥 UI）、`terminal-emulation.md`（xterm.js、resize、多行粘贴、本地终端）。
9. **工作流**
   - `global-search.md`、`bookmarks-quick-access.md`、`workspace-session.md`、`clipboard.md`、`system-integration.md`、`notifications.md`。
10. **创作工具**
   - `annotation-editor.md`、`pin-window.md`、`ocr.md`、`scrolling-long-screenshot.md`。
11. **交互基础**
   - `shell-context-menu.md`、`drag-and-drop.md`、`shortcuts-hotkeys.md`、`settings.md`（等当前右键/拖拽任务收尾后实施）。

### 批次 D：完善（P2）

12. `disk-view.md`、`window-detection.md`、`startup-manager.md`、`process-monitor.md`、`task-board.md`、`media-viewer.md`、`compare.md`、`archive.md`、`batch-rename.md`、`capture-utilities.md`、`taskbar-integration.md`、`tray-menu.md`、`encoding-conversion.md`、`sync-upload.md`。

## 4. 依赖关系

```text
Web 事件流 / TabKindDescriptor
   ├── Web 面板拆分与安全
   ├── 标签组/休眠
   └── 各标签 Web 能力声明

远程连接安全
   ├── 连接管理（主机密钥 UI/深度测试）
   ├── 终端仿真（会话/重连）
   └── SFTP/FTP 传输

IProgressCenter
   ├── 文件操作
   ├── 远程传输
   ├── 压缩/解压
   ├── 磁盘扫描
   ├── 批量 OCR/转码
   └── 同步/上传（P2）

截图选区 + WGC
   ├── 滚动长截图
   ├── 区域 OCR
   ├── 窗口选择器（截图/录屏共用）
   ├── 标注/贴图
   └── 截图辅助工具（取色/条码）

通知服务
   ├── 截图/录屏完成
   ├── 传输/文件操作失败
   ├── 任务看板到期
   └── 托盘菜单状态

剪贴板内容模型
   ├── 截图图片粘贴
   ├── 文件复制/剪切
   └── 编辑器文本粘贴
```

## 5. 需要拍板的取舍

| 决策 | 选项 | 建议 | 影响 |
| --- | --- | --- | --- |
| 远程主机密钥策略 | 首次信任 / 自动接受 / 手动预置 | 首次信任 + 变更阻断 | 安全 P0 |
| 非 Windows 凭据 | 明文 / AES-GCM 主密钥 / 系统密钥库 | Keychain/libsecret，失败回退 AES-GCM，禁止明文 | 安全 P0 |
| 跳板机/Agent | 本期 / P1 | P1（ProxyJump/Agent） | 内网场景 |
| Web 完整右键/系统菜单 | A 本程序命令 + 系统菜单只读枚举；B 仅本程序命令 | A，权限方案显式放行并审计 | 安全/实现量 M |
| 内嵌浏览器 | 是 / 否 | 否；确需时仅 Windows 做“网页预览” | 体积/跨平台 |
| 全局热键默认 | RegisterHotKey / 低级钩子 | RegisterHotKey 默认，钩子显式开启 | 安全软件误报 |
| Windows 采集后端 | GDI / WGC | WGC 主路径，GDI 回退 | HDR/遮挡/性能 |
| 本地 HTTP 服务 | 手写 / Kestrel | 先补健壮性与安全，P2 评估 Kestrel | 依赖/维护 |
| 文件操作 | 自绘 / IFileOperation | Windows 用 IFileOperation + 自有进度中心 | 系统冲突 UI |
| 长截图范围 | 仅手动 / 自动+手动 | 自动 + 手动兜底，先手动 MVP | 工作量 L |
| 同步/上传 | 本期 / P2 | P2；先做单向 + dry-run，不做双向 | 风险/工作量 L |

## 6. 实施纪律

- 所有模块保持“内核抽象 + 平台实现 + 应用装配”的分层；平台差异不得进入 App 层。
- 新增能力优先复用现有服务：`IFileSystemProvider`、`IProcessProvider`、`ITextFileService`、`IFileContentAccess`、`IClipboardService`、`IProgressCenter`、`INotificationService`。
- 所有交互与失败分支继续写中文全参数日志；新增日志需脱敏密码/令牌。
- 每个批次结束必须构建 + 单元测试 + Windows 实机验证；Web 端改动需强制刷新验证缓存。
- 不在本阶段改动 `src/`；实施时若与正在进行的截图/右键/拖拽任务冲突，先合并当前任务再按文档接入点落地。

## 7. 文档索引

- 总览：[index.md](index.md)（本文）
- 差距清单：[GAP.md](GAP.md)
- 模块文档：见第 2 节表格中的 41 个链接。
- 既有设计：[../ide-mode-design.md](../ide-mode-design.md)、[../media-viewer-design.md](../media-viewer-design.md)

## 8. 调研与标注说明

- 联网检索日期：2026-11-03；每条结论在模块文档末尾列出来源与用途。
- 未能联网核实的实现细节（AvaloniaEdit 与 Avalonia 12 兼容性、WASAPI loopback、UIA 跨进程性能、WGC HDR 表现等）已在对应文档标注“需实现阶段验证”，未写成已查证结论。
- 产品内部算法（WizTree/WinDirStat/IDM/rclone 等）未公开，文档只采用其公开产品形态作为对照。
- `encoding-switcher` 在本仓库中无同名模块；按“文件名编码转换 + 文本编解码”现状归入编码转换模块。
