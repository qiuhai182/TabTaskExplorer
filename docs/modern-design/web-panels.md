# Web 端面板现代化设计

状态：设计稿（2026-11-03）
适用：`WebPageHtml` 各 `renderXxx` 面板、`TabKinds`、`WebWorkspace`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 面板 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 文件 | `WebPageHtml.renderTable/renderTiles`（:1159+） | 可用 | 列表/图标、勾选、排序、面包屑、信息窗格 |
| 搜索 | `renderTable` + `tabType === 'search'` | 可用 | 结果表、列宽、打开 |
| 磁盘 | `renderDiskTable`（:1236） | 可用 | 树表、展开/折叠、导航 |
| 任务看板 | `renderBoard`（:1647） | 可用 | 三列 |
| 连接管理 | `renderConnections`（:1678） | 可用 | 列表/表单/测试/导入导出 |
| 进程 | `renderProcess`（:1395） | 可用 | 操作行、表格、走势图、详情 |
| 启动项 | `renderStartup`（:1743） | 可用 | 系统/自定义、启停/运行/保存 |
| 比对 | `renderCompare`（:1336） | 可用 | 目录/文件 diff |
| IDE | `renderIde`（:2625） | 半成品 | 树/标签/编辑/查找/快速打开，无高亮/远程写回确认 |
| 媒体 | 无 | 缺口 | `TabKinds.Media` 网页端不渲染 |
| 截图/录屏 | 无 | 缺口 | `TabKinds.Capture` 网页端不渲染 |
| 终端/传输/编辑器 | 无 | 缺口 | 归为 `unknown`，显示通用占位 |
| 右键菜单 | 部分 | 半成品 | 本程序有限命令，无系统 Shell 菜单 |

主要问题：

1. **面板能力没有统一声明**：`TabKinds.All` 只列 8 类，媒体/截图/终端/传输等直接落 `unknown`，用户看到空白/通用占位。
2. **同构但不完全一致**：文件/搜索/磁盘/进程/启动项基本同构；IDE 缺高亮/远程写回；比对缺图片/二进制。
3. **桌面专用面板无解释**：用户不知道为什么媒体/截图/终端在网页端不可用。
4. **右键菜单弱**：网页端只有有限命令，不能调用系统 Shell 动词（解压、用其他程序打开）。
5. **响应式/可访问性不足**：宽表格在窄屏横向滚动；键盘操作/读屏标签不完整。
6. **状态刷新依赖 1.5 秒轮询**：面板切换/操作反馈延迟（见 `web-ui-local-service.md`）。
7. **错误态不统一**：权限不足/路径不存在/远程断线时提示不一致。
8. **面板代码仍在单体 `WebPageHtml`**：新增面板成本高。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| VS Code Server | 完整 IDE 面板、WebSocket、状态同步（[VS Code Server](https://code.visualstudio.com/docs/remote/vscode-server)） | 面板能力声明 + 事件推送 |
| Jupyter | 文件浏览器/Notebook/终端面板、权限（[Jupyter](https://jupyter.org/)） | 权限与面板隔离 |
| Home Assistant | 面板/卡片、WebSocket、移动端适配 | 面板组件化 |
| PWA | 可安装、离线壳、响应式（[PWA](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps)） | 移动端体验 |
| WCAG | 键盘/标签/对比度（[WCAG](https://www.w3.org/WAI/standards-guidelines/wcag/)） | 可访问性 |

## 3. 推荐方案

### 必做（P0）

1. **面板能力注册表**
   - `TabKindDescriptor` 增加 `WebSupport`（Full/Summary/ReadOnly/None）与 `WebReason`；网页端按能力渲染而不是 `unknown`。
   - 接入点：`Services/TabContract.cs`、`WebPageHtml.renderContent`（:1089-1106）。
   - 验收：媒体/截图/终端显示“桌面专用：需在本机使用”，不是空白。

2. **面板拆分**
   - `Assets/web/views/file.js`、`search.js`、`disk.js`、`process.js`、`startup.js`、`connections.js`、`compare.js`、`ide.js`、`board.js`。
   - 接入点：`WebPageHtml` 只保留入口/布局；`WebServiceHost` 静态资源；与 `tab-shell-browser-kernel.md` 的前端拆分统一实施。

3. **面板一致性矩阵**
   - 文件/搜索/磁盘：选择、排序、列宽、右键、信息窗格对齐桌面。
   - 进程/启动项：操作按钮、权限提示、危险确认对齐桌面。
   - IDE：先只读+保存，明确“无语法高亮/无终端”。
   - 比对：目录/文件先对齐，图片/二进制标注“桌面专用”。
   - 接入点：`TabSnapshotFiller`、`WebPageHtml`、`WebWorkspace.ExecuteAction`。

4. **统一错误/空状态**
   - 权限不足、路径不存在、远程断线、桌面专用四类标准卡片；带 requestId（诊断模块）。
   - 接入点：`WebPageHtml` 通用 `renderEmptyState`；`WebServiceHost` 错误响应。

5. **响应式与可访问性**
   - <900px：侧边栏抽屉、表格横向滚动、按钮图标化；全部交互可键盘到达；表格有 `scope`/`aria-label`。
   - 接入点：`Assets/web/app.css`、`WebPageHtml` 模板。

### 可选（P1/P2）

- 媒体面板只读预览（缩略图/播放远程产物，不采集）。
- 截图/录屏产物历史只读浏览。
- 终端 Web 面板（xterm.js + WebSocket），需权限方案显式开启。
- 传输队列只读/暂停/取消。
- 面板主题跟随桌面、PWA 安装。

### 不建议

- 在网页端提供屏幕采集/录屏/贴图：依赖本机屏幕，跨端不一致。
- 在网页端执行任意 Shell 命令/系统菜单：权限与安全边界（见 `shell-context-menu.md`）。
- 为每个面板写独立 HTML 文件而不共享状态层：维护成本高。

## 4. 交互与视觉

- 顶部标签条与桌面同构；桌面专用标签显示“桌面”徽标。
- 文件/搜索/磁盘：表头、勾选、右键、信息窗格与桌面一致。
- 进程/启动项：危险操作二次确认；权限不足按钮禁用 + tooltip。
- IDE：编辑器只读高亮缺失时用等宽文本；状态栏显示“网页端简化模式”。
- 空状态：图标 + 一句解释 + 可选“在桌面端打开”。
- 移动端：底部工具栏、侧边栏抽屉、表格横向滚动。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 能力注册 | `TabContract.cs`、`WebPageHtml.renderContent` | Full/Summary/None |
| 面板拆分 | `Assets/web/views/*.js`、`WebPageHtml.cs` | 与前端拆分统一 |
| 一致性 | `TabSnapshotFiller`、`WebWorkspace` | 快照字段补齐 |
| 错误态 | `WebPageHtml`、`WebServiceHost` | requestId |
| 响应式 | `Assets/web/app.css` | 断点/键盘 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 面板拆分 | 迁移期双实现 | M/L |
| 能力注册 | 所有标签需补描述 | M |
| 一致性 | 桌面/网页字段漂移 | M |
| 响应式 | 宽表格/移动端 | M |
| 终端 Web | 安全/权限/WebSocket | L |

## 7. 验收标准

1. 每个标签类型在网页端有明确能力标识；桌面专用显示解释卡片。
2. 文件/搜索/磁盘/进程/启动项/比对面板操作与桌面一致（允许的范围内）。
3. 权限不足/路径不存在/远程断线显示标准错误卡片 + requestId。
4. 900px 宽度下可完整操作（抽屉/横向滚动）。
5. 键盘可到达主要按钮/表格行；读屏标签完整。
6. 面板切换不依赖 1.5 秒轮询（事件推送后 <200ms）。
7. 新增面板只需加一个 `views/*.js` + 能力描述，不改主流程。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [VS Code Server](https://code.visualstudio.com/docs/remote/vscode-server) | Web IDE 面板/状态同步 | 2026-11-03 |
| [Jupyter](https://jupyter.org/) | 文件/终端面板与权限 | 2026-11-03 |
| [Home Assistant WebSocket](https://developers.home-assistant.io/docs/api/websocket/) | 面板事件推送 | 2026-11-03 |
| [PWA](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps) | 移动端/可安装 | 2026-11-03 |
| [WCAG](https://www.w3.org/WAI/standards-guidelines/wcag/) | 可访问性 | 2026-11-03 |
