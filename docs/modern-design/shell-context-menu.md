# 右键菜单 / Shell 集成现代化设计

状态：设计稿（2026-11-03）
适用：`WindowsShellContextMenuReader`、`WindowsShellMenuPopup`、`FileListPanel`、`SettingsWindow` 右键菜单分页
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 系统外壳菜单读取 | `WindowsShellContextMenuReader.cs:11`、`QueryContextMenu`（:93/182/335/497） | 可用 | 通过 `IContextMenu` 让系统按目标构建菜单 |
| 第三方扩展/级联/动态动词 | `ReadMenuWithState`（:540）、`IContextMenu2`（:1052） | 可用 | 支持 `IContextMenu2` 消息转发 |
| 命令执行 | `InvokeWithCanonicalVerb`（:722）、`InvokeWithCommandId`（:746） | 可用 | 规范动词优先，失败回退命令编号 |
| 原生菜单弹出 | `WindowsShellMenuPopup`、`WindowsSystemContextMenuService` | 可用 | Windows 原生 HMENU，键鼠体验接近 Explorer |
| 本程序命令混排 | `FileListPanel.BuildOwnContextCommands`（:1077） | 可用 | 打开/复制/剪切/压缩/解压/重命名/删除/比较/粘贴到文件夹 |
| 降级 Avalonia 菜单 | `FileListPanel.OpenFallbackContextMenu`（:1061） | 可用 | 原生菜单失败/超时回退 |
| 系统菜单项管理页 | `SettingsWindow` 第 7 分页、`SystemContextMenuPolicy` | 可用 | 按文件/桌面/新建/Win11 新版筛选，启停注册表项 |
| 远程/压缩包标签 | `FileListPanel.OnListContextRequested`（:979-984） | 可用 | 明确不加载本地 Shell 菜单 |
| Windows 11 现代菜单（IExplorerCommand） | 无 | 缺口 | 只读展示注册表项，未真正加载现代菜单 |
| 菜单图标/盾牌 | 无 | 缺口 | HMENU 未 owner-draw，图标与 UAC 盾牌可能缺失 |
| Web 端完整 Shell 菜单 | 无 | 缺口 | 网页端只有本程序有限命令 |

主要问题：

1. **同步构建菜单可能阻塞**：`QueryContextMenu` 会加载第三方 Shell 扩展；某些扩展慢/崩溃会卡住右键。
2. **Win11 现代菜单缺失**：Windows 11 新菜单由 `IExplorerCommand` + 包标识提供；当前只能看到经典动词，用户在“显示更多选项”里才能看到部分扩展。
3. **图标与盾牌缺失**：原生菜单项没有 owner-draw 位图，视觉上不如 Explorer；UAC 盾牌可能不显示。
4. **Web 端右键能力弱**：网页端只有复制/粘贴/删除等有限命令，不能调用系统 Shell 动词（解压、用其他程序打开、发送到…）。
5. **菜单项排序/分组不透明**：本程序命令与系统项混排，缺少“常用命令置顶 + 系统项折叠”的层次。
6. **多选/空白处差异**：`BuildOwnContextCommands` 已区分，但第三方扩展在多选时的可用性依赖 Shell，缺少置灰原因说明。
7. **无“在 TabTaskExplorer 中打开”反向集成**：本程序没有注册到 Explorer 右键菜单，无法从系统资源管理器跳到本程序。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 11 文件资源管理器 | 简化主菜单（剪切/复制/重命名/共享/删除）+ “显示更多选项”进入经典菜单（[Microsoft 支持](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows)） | 主菜单常用命令置顶，系统项放二级 |
| IExplorerCommand | Win11 现代菜单扩展，需包标识（打包应用或 sparse package）（[Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)） | 若要支持现代菜单，需 sparse MSIX；成本较高 |
| 经典 Context Menu Handlers | `IContextMenu`/`IContextMenu2`/`IContextMenu3`，注册表注册（[Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)） | 当前实现正确；需加超时与异常隔离 |
| Directory Opus / Total Commander | 自带命令优先、系统菜单作为子菜单、图标丰富 | 采用“本程序命令 + 系统命令子菜单”结构 |
| Files | 现代 Fluent 菜单、标签子菜单、云状态 | 视觉与分组可借鉴 |

## 3. 推荐方案

### 必做（P0）

1. **异步菜单构建 + 超时**
   - 在后台线程构建 HMENU，UI 线程显示加载态；单次构建超时 800ms，超时回退本程序菜单并记录扩展来源。
   - 接入点：`WindowsShellContextMenuReader` 的 `QueryContextMenu` 调用改为 `Task` + 超时；`FileListPanel.OnListContextRequested` 已有 `async` 流程。
   - 注意：HMENU/COM 线程亲和性，需在专用 STA 线程创建与弹出。

2. **图标与盾牌**
   - owner-draw 菜单项：读取 `IShellItemImageFactory` 或 `ExtractIconEx` 图标；UAC 盾牌用 `SHGetStockIconInfo`。
   - 接入点：`WindowsShellMenuPopup`；`SystemContextMenuEntry` 增加 `IconBytes`、`RequiresElevation`。
   - 性能：图标按扩展名缓存，避免每次右键解码。

3. **菜单分层**
   - 第一层：本程序常用命令（打开/复制/剪切/粘贴/重命名/删除/压缩/解压/比较）。
   - 第二层：“系统菜单”子菜单（第三方扩展、发送到、属性等）。
   - 接入点：`BuildOwnContextCommands` 与 `AppendSystemMenuItems` 顺序调整；`FileListPanel.axaml` 菜单结构调整。

4. **Web 端右键补齐（需拍板）**
   - 方案 A（推荐）：网页端提供本程序完整命令 + 系统菜单的“只读枚举”，点击系统项由桌面端执行（HTTP 动作），返回结果。
   - 方案 B：网页端只保留本程序命令，系统项显示“请在桌面端使用”。
   - 接入点：`WebPageHtml` 右键菜单渲染；`WebServiceHost` 新增 `/api/contextmenu`；`WebWorkspace.ExecuteAction` 增加 `shellmenu` 动作。
   - 风险：远程执行 Shell 动词的权限与安全边界，需要权限方案显式放行。

5. **“在 TabTaskExplorer 中打开”**
   - 通过 sparse MSIX + `IExplorerCommand` 注册文件夹/文件夹背景右键命令；点击后用单实例管道唤起本程序。
   - 接入点：新增打包工程/清单；`SingleInstanceService` 已有唤起路径；`MainWindowViewModel.OpenPathFromSystem`。
   - 注意：需要签名与安装器；未打包运行时不可用，需降级提示。

### 可选（P1/P2）

- 支持 `IExplorerCommand` 现代菜单项的枚举与调用（只读展示 + 执行）。
- 菜单搜索（输入关键字过滤）。
- 自定义菜单项（用户配置外部命令，参考 Greenshot External Command）。
- 菜单项使用频率排序（本程序命令）。
- 深色/浅色菜单主题与系统一致。

### 不建议

- 注入 Explorer 进程或 Hook 其菜单：稳定性/安全/合规风险高。
- 替换系统全局右键菜单：影响其他应用，不做。
- 在 UI 线程同步等待 Shell 扩展：必须异步 + 超时。

## 4. 交互与视觉

- 主菜单：常用命令图标 + 文字，危险命令（删除）红色；分隔线分组。
- 系统菜单子项：保留原生层级与图标；过深子菜单（> 3 层）折叠为“更多…”。
- 多选：标题显示“复制 3 项”；不可用项置灰并在 tooltip 说明原因。
- 加载态：右键后 100ms 内显示骨架菜单，系统项加载完成后再插入。
- Web 端：菜单宽度 220，图标 16，键盘 ↑↓/Enter/Esc 可用；系统项带“桌面执行”徽标。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 异步/超时 | `WindowsShellContextMenuReader.cs`、`WindowsSystemContextMenuService.cs` | 专用 STA 线程 + 取消 |
| 图标/盾牌 | `WindowsShellMenuPopup.cs`、`Kernel/Models/SystemContextMenuEntry.cs` | owner-draw + 缓存 |
| 菜单分层 | `FileListPanel.axaml.cs`、`FileListPanel.axaml` | 不改命令执行逻辑 |
| Web 菜单 | `WebPageHtml.cs`、`WebServiceHost.cs`、`WebWorkspace.cs` | 权限校验必须覆盖 |
| 反向集成 | 新增 sparse MSIX 工程、`SingleInstanceService.cs` | 需签名/安装器 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 异步菜单 | COM 线程模型、HMENU 生命周期 | M |
| 图标/盾牌 | 图标提取失败、DPI | S/M |
| Web 系统菜单 | 远程执行 Shell 的安全边界 | M |
| IExplorerCommand | 打包/签名/安装复杂度 | L |
| 反向集成 | 安装器与注册表清理 | M/L |

## 7. 验收标准

1. 右键文件/文件夹/空白处 100ms 内出现菜单骨架，系统项 800ms 内加载完成或超时降级。
2. 第三方 Shell 扩展（7-Zip、Git、云盘）出现在“系统菜单”子菜单并可执行。
3. 菜单项显示图标；需要提权的项显示盾牌。
4. 多选时本程序命令数量/文案正确；不可用项有原因提示。
5. Web 端右键可执行本程序命令；系统项按权限方案放行/拒绝，并记录日志。
6. “在 TabTaskExplorer 中打开”安装后出现在 Explorer 文件夹右键，点击可唤起并定位。
7. 任一 Shell 扩展崩溃/卡死不会导致本程序无响应。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows 11 文件资源管理器右键菜单](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows) | 简化菜单 + 显示更多选项 | 2026-11-03 |
| [IExplorerCommand / 打包应用上下文菜单](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer) | Win11 现代菜单与 sparse package | 2026-11-03 |
| [Creating Shortcut Menu Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers) | IContextMenu 经典扩展 | 2026-11-03 |
| [SHFileOperationW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shfileoperationw) | 回收站删除语义 | 2026-11-03 |
