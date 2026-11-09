# 网页端 / 桌面端界面一致性整改 进度

原则：**界面必须一致，功能可以不一致。** 结构、区域位置、控件形态（按钮/图标/间距/悬停·选中·禁用态）两端对齐；网页端未实现的能力保留控件并置灰，悬停给出可读说明；已可用功能不因对齐而退化；权限不足（如 `AllowModify=false`）与能力缺失使用不同提示。

约束：不修改 `Assets/web/app.css`（颜色令牌任务占用）、`App.axaml`；不碰 `FileDropPolicy.cs`、`Services/Capture/*`。本轮只改 `Assets/web/*` 与新增文档。

更新时间：2026-11-06

## 1. 两端差异清单（按区域）

| 区域 | 桌面端 | 网页端现状 | 判定 | 整改方式 | 落点文件 |
| --- | --- | --- | --- | --- | --- |
| 标题栏 | Logo、标签、新建、任务看板、截图与录屏、IDE、主题、默认资源管理器、设置、窗口控制 | Logo、标签、新建、任务看板、IDE、主题、模式文字按钮 | 缺「截图与录屏」「设置」；窗口控制属浏览器，不搬 | 补两个入口；截图/录屏置灰（仅桌面端可用），设置打开同构设置面板 | `index.html`、`panels.js` |
| 工具栏 | IDE、查看、复制、剪切、粘贴、新建文件夹、批量改名、编码转换、回收站、显示隐藏、详细信息窗格、刷新 | 同序，另有「预览」「任务中心」 | 桌面少两个入口（桌面任务中心面板未实现） | 网页不删；记入桌面侧待补 | — |
| 导航/地址栏 | 后退、前进、上一级、路径、最近、刷新、收藏、搜索 | 同左 | 一致 | 不改 | — |
| 书签栏 | 快捷访问项、取消固定 | 快捷访问项、右键打开/粘贴/取消固定 | 一致 | 不改 | — |
| 侧栏 | 快捷访问、此电脑、网络（连接管理、全局搜索、文件比对、磁盘视图、进程监控、启动项管理、右键管理、截图与录屏） | 快捷访问、此电脑、网络（连接管理、文件比对、全局搜索、磁盘视图、进程监控、启动项管理、任务看板） | 缺「右键管理」「截图与录屏」；顺序不同 | 补两项并置灰；顺序统一为桌面顺序，任务看板保留在末尾 | `views.js` |
| 内容区/文件列表 | 列、排序、行高、信息窗格 | 同构 | 一致 | 不改 | — |
| 状态栏 | `N 个项目` + 网页服务地址 | 项目数/勾选 + 连接状态 + 服务地址 | 接近 | 不改 | — |
| 设置窗口 | 11 分组：基本设置、快捷操作、文件/文件夹、编辑器、打开方式、媒体、网页端、右键菜单、启动项管理、截图与录屏、关于；快捷操作可增删改 | 无入口、无接口 | 整块缺失 | 建同构面板：基本设置接已有能力（主题/扩展名/隐藏项/紧凑），快捷操作只读展示统一快捷键表，其余分组置灰说明 | `index.html`、`panels.js`、`boot.js` |
| 截图/录屏面板 | 截图、录屏、全局快捷键、产物历史 | 无 | 缺失 | 侧栏/标题栏入口置灰 + 提示「该功能仅桌面端可用」 | `index.html`、`views.js` |
| 任务中心 | 桌面仅冲突对话框 + 重试按钮（完整面板未实现） | 浮层任务中心（本地/远程统一） | 网页领先 | 不改网页；记入桌面侧待补 | — |
| 日志/诊断 | 诊断包生成（设置·关于） | 底部诊断折叠框 | 形态不同 | 设置「关于」分组注明网页诊断位置，生成诊断包按钮置灰 | `panels.js` |
| 快捷键 | `HotkeySetting.CreateDefaultShortcuts()`：应用内 15 条 + 全局 8 条 | `boot.js` 硬编码 Ctrl+T/W/A/C/X/V、F5、Delete、Alt+←/→、IDE S/Shift+S/F/P | 两套互不相干 | 建立单一快捷键表（动作、按键、作用域、网页可用性）；同一表驱动显示与分发；本轮补 Ctrl+Tab / Ctrl+Shift+Tab / F2 | `boot.js`、`panels.js` |
| 尺寸/密度令牌 | Fluent 默认 + 显式 28/22 行高 | `app.css` 566 处 px 字面量 | 两套 | 收进 `design/tokens/ui-tokens.json` → `tokens.css`，再替换 `app.css` | 待颜色令牌任务合并后执行（P2） |

## 2. 整改顺序

1. **P0 结构占位（本轮）**：标题栏补「截图与录屏（置灰）」「设置」；侧栏补「右键管理」「截图与录屏」并置灰，顺序对齐桌面；设置面板壳（11 分组）+ 基本设置接已有能力 + 快捷操作只读表 + 其余分组置灰说明。
2. **P1 快捷键统一（本轮）**：`boot.js` 顶部建立 `WEB_SHORTCUTS` 表（与桌面动作名一致），设置·快捷操作按此表渲染；补 Ctrl+Tab / Ctrl+Shift+Tab / F2；未实现的条目在表中标注「仅桌面端」，不静默消失。
3. **P2 面板补齐**：截图/录屏面板壳与产物历史（依赖 `Services/Capture`，另一任务占用，暂缓）；设置各分组逐项接 `/api/settings`（需新增接口）；尺寸令牌收口。

## 3. 本轮进度

### 3.1 P0 结构占位（已完成，产物 `2026-11-06 15:55:46` 实测）

- 标题栏：新增「截图与录屏」（置灰 `opacity .45`、`title` 说明、点击 flash「该功能仅桌面端可用」）与「设置」；顺序与桌面一致：任务看板 → 截图与录屏 → IDE → 主题 → 模式 → 设置。窗口控制属浏览器，不搬。
- 侧栏：顺序对齐桌面（连接管理 / 全局搜索 / 文件比对 / 磁盘视图 / 进程监控 / 启动项管理），新增「右键管理」「截图与录屏」并置灰、`title` 与点击提示；「任务看板」保留在末尾（网页已有能力）。
- 设置面板：与桌面同 11 分组；「基本设置」接已有能力（切换主题、显示扩展名、显示隐藏条目、紧凑模式，均写入两端共用配置）；「网页端」只读展示当前地址；其余 8 组保留并置灰，提示「该分组仅桌面端可用：…」。
- 实测：面板打开显示 11 项分组（7 项带「仅桌面」角标）、快捷操作页 25 行、截图分组提示正确、Esc 关闭；侧栏两项 `disabled` 标记与 `title` 正确。

### 3.2 P1 快捷键统一（已完成）

- `boot.js` 新增 `WEB_SHORTCUTS`（25 条：18 条应用内 + 7 条系统全局），动作名与桌面 `HotkeySetting.CreateDefaultShortcuts()` 对齐；设置·快捷操作直接按该表渲染，未实现条目显示「仅桌面端 / 仅 IDE 模式」，不隐藏。
- 新增 `Ctrl+Tab` / `Ctrl+Shift+Tab` 标签循环（对应桌面 NextTab / PreviousTab）；新增 `F2` 重命名焦点行（对应桌面 Rename）。
- 修正：网页标签选中态来自 `tab.selected`（无 `index` 字段），首版 `switchTabBy` 用 `currentTab.index` 导致切换无效，已改为 `findIndex`。
- 实测：标签数 2 时 `Ctrl+Tab`：选中 1 → 0，`Ctrl+Shift+Tab`：0 → 1。

### 3.3 待办（P2）

- 截图/录屏面板壳与产物历史：依赖 `Services/Capture/*`（另一任务占用），本轮仅置灰入口。
- 设置其余分组逐项接接口：需要新增 `/api/settings`（分组、下拉、保存），当前只读/置灰。
- 尺寸与密度令牌：待颜色令牌任务合并后再改 `design/tokens/ui-tokens.json` → `tokens.css` → `app.css`；本轮新增布局使用内联样式，未新增 px 字面量到 `app.css`。
- `WebPageHtml.cs` 内联回退页未同步本轮新增入口（与既有书签/右键缺口一并处理）。
- 桌面端设置窗口未做逐项对照实测（只读代码核对），本轮以网页端实测为准。

## 4. 第四轮（2026-11-09）：桌面入口补齐 + 截图面板 + `/api/settings`

约束更新：尺寸/颜色令牌（`design/tokens/ui-tokens.json`、`Tokens.axaml`、`tokens.css`）由另一会话维护，本轮只消费、不改动；`app.css` 按 `theme-tokens-progress.md` 清单逐选择器替换为 `--tte-*` 令牌，未新增 px 字面量。

### 4.1 桌面端补「预览」「任务中心」（已完成，构建 0 错误）

- [MainWindow.axaml](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/MainWindow.axaml)：工具栏「显示详细信息窗格」之后、「刷新」之前新增两个入口，顺序与网页端一致：
  - `Kind="eye"`「预览」，`Click="OnToolbarPreviewClick"`，提示「预览选中文件（空格）」。
  - `Kind="board"`「任务中心」，`Click="OnToolbarTaskCenterClick"`，提示「任务中心（复制 / 移动 / 删除与远程传输）」。
- [MainWindow.axaml.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/MainWindow.axaml.cs)：新增 `taskCenterWindow` 字段与两个 handler。
  - 预览：无标签 / 未勾选 / 文件夹均给出中文提示并写日志；文件调用既有 `QuickLookWindow.ShowFor(this, path)`，不复制预览逻辑。
  - 任务中心：单实例；已打开则 `Activate()`，否则 `new TaskCenterWindow(viewModel).Show(this)`，关闭时置空。
- 新增 [TaskCenterWindow.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/TaskCenterWindow.cs)：纯代码窗口（900×560，1 秒刷新），复用 `MainWindowViewModel.GetFileTasks("") / CancelFileTask / RetryFileTask / ResolveFileTaskConflict`。
  - 分组：顶部统计（共 N / 运行中 / 等待冲突）+ 刷新；中部任务行（类型、状态、进度、源→目标、消息、冲突）；底部取消 / 重试 / 合并 / 替换 / 逐项处理 / 改名共存 + 「本批统一」+ 关闭；底部状态文字。
  - 文件夹冲突才启用「合并 / 逐项处理」，文件冲突只启用「替换 / 改名共存」；操作后写日志并刷新。
- 构建：`dotnet build -c Debug` → **0 错误 / 192 警告**（均为既有警告）。

### 4.2 网页端截图 / 录屏面板壳 + `app.css` 令牌化（已完成）

- `index.html`：`#captureEntry` 去掉置灰与 `aria-disabled`，改为 `openCapture()`；新增 `#capturePanel` 弹层（`modal` / `panel` / `panelHead` / `panelTitle` / `btn` 复用现有类）。
- `views.js`：侧栏「截图与录屏」由 `desktopOnly(...)` 改为 `openCapture()`，不再置灰。
- `panels.js`：新增 `openCapture / closeCapture / loadCaptureState / renderCapture / captureAction`，分组与桌面 `CapturePanel.axaml` 对齐：
  1. 截图：已接 `fullscreen`、`region`、`pin`；主屏 / 滚动长截图 / 活动窗口 / 上次区域 / 屏幕取色置灰，原因「网页端尚未接入该截图方式，请在桌面端使用」。
  2. 截图选项：`pinSave` 可写；自动打开文件夹 / 自动打开标注编辑器 / 包含鼠标指针 / 采集后端置灰，原因「该选项由桌面端截图管线读取，网页端暂不可改」。
  3. 录屏：开始 / 停止 / 目标 / 麦克风 / 帧率质量全部置灰，原因「网页端尚未接入录屏，请在桌面端录制」。
  4. 产物历史：缩略图（`thumbnailUrl`，失败回退图标）+ 文件名 / 时间 / 大小；删除、复制可点；打开、标注置灰说明。每 2 秒刷新，关闭面板即停。
  5. 输出：保存目录、命名模板、格式。
  - `403` 单独提示「当前端口未放行修改权限，无法执行该截图操作」，不与会话内能力缺失混淆。
- `boot.js`：Esc 顺序改为「任务中心 → 截图面板 → 设置」。
- `app.css`：
  - 按 `theme-tokens-progress.md` 逐选择器替换尺寸令牌：`px` 字面量 **566 → 242 处**；`--tte-*` 使用 **69 / 89 个已定义令牌**，CSS 变量闭包检查 0 未定义。
  - 令牌覆盖范围外的 5 类刻意保留：组件固有尺寸（logo 22、缩略图 32、`ideGutter` 54、`ideTree` 230、`.panel.small` 等）、非 4 梯度间距（5/7/9/10/14/18/20）、阴影、历史颜色字面量、10px/9px 字号。
  - `#contentMain.compact tr.row td` 的 `padding-top/bottom:1px` 保留字面量（`row-padding-compact` 为 `1px 2px` 简写，拆成纵向长写会改变横向内边距）；`line-height` 已替换为 `--tte-line-height-body`。
  - 新增截图 / 设置行样式（`.cap*`、`.set*`）全部使用令牌，无新增 px。
- `node --check`：`panels.js`、`boot.js`、`views.js` 语法通过。

### 4.3 `/api/settings` 与设置面板接线（已完成）

- [WebWorkspace.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebWorkspace.cs)：`IWebWorkspaceBridge` 新增 `GetSettingsJson()` / `ApplySettingsJson(string json)`。
- [MainWindowViewModel.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/ViewModels/MainWindowViewModel.cs)：
  - `GetSettingsJson()`：返回 `values`（23 个白名单键）、`editable`、`locked`（6 组中文原因：shortcuts / web / startup / openwith / media / shellmenu）。
  - `ApplySettingsJson()`：逐键白名单校验（布尔 / 枚举 / 范围：字号 8–48、制表 1–8、截图延迟 0–30、字体 ≤200 字符），未知键与越界值进 `rejected` 并附中文原因；通过项在 UI 线程赋值，主题同时应用，最后 `SaveSettingsAndApply()` 一次；日志只记键名数量，不记敏感值。
- [WebServiceHost.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebServiceHost.cs)：
  - `/api/settings` GET / POST 路由；POST 先判 `profile.AllowModify`，未放行返回 403「该端口未放行修改权限」；空 body 返回 400「设置格式不正确」。
  - `/api/version` 的 `features` 增加 `settings = true`。
- `panels.js`：`openSettings()` 触发 `loadSettings()`；读取失败显示「读取设置失败：<原因>」与「重新读取」，不显示假值。
  - 可写分组：基本（主题 / 扩展名 / 隐藏条目 / 紧凑 / 托盘 / 双击关闭 / 工具栏 / 书签栏 / 恢复标签）、编辑器（字体 / 字号 / 制表宽度 / 插入空格 / 自动换行 / 显示空白）、截图（贴图保存 / 自动打开目录 / 自动打开编辑器 / 含指针 / 延迟 / 后端）。
  - 只读分组：文件/文件夹（桌面专属）、快捷操作（快捷键表 + 桌面端修改原因）、打开方式 / 媒体 / 网页端 / 右键菜单 / 启动项（`locked` 中文原因 + 置灰按钮）、关于（诊断包置灰 + 网页诊断入口）。
  - POST 403 时提示「当前端口未放行修改权限，无法修改设置」并回读真实值；`rejected` 逐条展示中文原因。

### 4.4 内联回退页收敛（已完成）

- [WebPageHtml.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebPageHtml.cs)：**4034 行 → 200 行**。
- 保留：`Content` 常量、`__PORT__` 占位符、`TTE-TOKENS-BEGIN/END` 令牌块（与 `tokens.css` 同源）。
- 入口名称与顺序与静态页一致（标题栏 / 书签栏 / 工具栏 / 导航栏 / 侧栏 / 内容区 / 信息窗格 / 状态栏，`#settings` 与 `#capturePanel` 骨架保留）。
- 移除全部内联业务 CSS / JS；改为诊断页：探测 `/app.css`、`/tokens.css`、`core/views/panels/actions/ide/boot.js` 是否可访问，显示缺失列表与「重新加载」。

### 4.5 实测证据（2026-11-09 09:37 - 09:44，产物 `output\TabTaskExplorer.exe`，端口 15919）

复现步骤：启动 `output\TabTaskExplorer.exe` → Chrome 打开 `http://127.0.0.1:15919/` → 点标题栏「截图与录屏」/ 侧栏「截图与录屏」→ 关闭后再点「设置」。

- 构建/发布：`dotnet publish ... -c Release -r win-x64` 成功；进程监听 `127.0.0.1:15919`；`GET /api/version` → `features.settings = True`。
- 走静态资源（非诊断回退页）：页面存在 `#capturePanel`、`#settings`，`typeof openCapture === 'function'`、`typeof loadSettings === 'function'`，样式表为 `/assets/web/tokens.css?v=…` 与 `/assets/web/app.css?v=…`。
- 截图面板：`#capturePanel` 正常打开，`.capGroup` **5 个**（截图 / 截图选项 / 录屏 / 产物历史（14）/ 输出）；可点按钮 3 个（全屏截图 / 区域截图 / 贴图截图，另有刷新、复制到剪贴板、删除）；置灰按钮 **42 个**，`title` 分三类可读原因：
  - 「网页端尚未接入该截图方式，请在桌面端使用」（主屏 / 滚动长截图 / 活动窗口 / 上次区域 / 屏幕取色）
  - 「该选项由桌面端截图管线读取，网页端暂不可改」（自动打开文件夹 / 标注编辑器 / 含指针 / 采集后端）
  - 「网页端尚未接入录屏，请在桌面端录制」（录屏 5 项）
- 截图写入（临时放行修改权限时）：点「全屏截图」→ 产物历史 14 → **15**，最新项 `截图_20261109_094014.png`；说明 `POST /api/capture/action?a=fullscreen` 与产物刷新均通。
- 设置面板：`#settingsNav` **11 个分组**，其中 7 个带「仅桌面」角标（快捷操作 / 文件/文件夹 / 打开方式 / 媒体 / 网页端 / 右键菜单 / 启动项管理）；快捷操作页 25 行快捷键表；「网页端」分组显示 `当前地址：http://127.0.0.1:15919`。
- 设置写入（临时放行修改权限时）：编辑器字号 13 → 14 → 15，两次连续保存均返回「已应用 1 项设置」且回填为新值，无需手动刷新；随后改回 13。
- 只读 profile（`AllowModify=false`，当前默认）：字号改 20 被拒，提示「当前端口未放行修改权限，无法修改设置」，回读仍为 13（未显示假成功）；截图动作仍可执行（截图不写用户文件，不受 AllowModify 限制），与「权限不足 / 能力缺失」分开提示一致。

### 4.6 实测中发现并修复：CSRF 令牌失效被误报为权限不足

- 现象：保存设置后服务重启监听，旧会话/CSRF 令牌失效；下一次写请求返回 **403「缺少或错误的 CSRF 令牌」**，而 `panels.js` 把所有 403 显示为「当前端口未放行修改权限」，日志审计亦只见 CSRF 拒绝。
- 修复：[core.js](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Assets/web/core.js) 的 `apiFetch` 在 403 时读取响应体，若含 `CSRF` 则清空令牌、重新 `GET /api/session` 后**重试一次**（服务端在 CSRF 校验阶段即拒绝，未执行写操作，重试安全）；真实权限 403 不重试、仍显示权限文案。
- 复测：放行修改权限后，连续两次保存（14 → 15）均成功；审计日志可见 09:42:01、09:42:07 各一次 CSRF 拒绝，随后重试成功。

### 4.7 状态还原与未实测清单

- 权限已还原：`%APPDATA%\TabTaskExplorer\settings.json` 的 `WebAccessProfiles[0].AllowModify` 已由临时 `true` 改回 **false**，备份 `settings.json.bak-tte` 已删除；`EditorFontSize=13`、`CaptureDelaySeconds=0`，应用已用最终发布包重启（只读 profile）。
- 令牌口径复核：`app.css` 现 **242 处 px / 377 行**，使用 `--tte-*` **69 / 89**，变量闭包 0 未定义；未改 `design/tokens/ui-tokens.json`、`Themes/Tokens.axaml`、`Assets/web/tokens.css`（工作区中这三处改动来自令牌任务，非本轮）。
- 未实测（缺少桌面自动化工具，仅代码 + 构建验证）：
  - 桌面端工具栏「预览」「任务中心」的点击行为与任务中心窗口布局/刷新；
  - 任务取消 / 重试 / 冲突处理的界面操作；
  - Ctrl+Tab、空格预览等桌面回归；
  - 诊断回退页的真实触发（需移除 `Assets/web` 静态资源）。


