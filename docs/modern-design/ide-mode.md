# IDE 模式现代化设计

状态：设计稿（2026-11-03）
适用：`IdeShellViewModel`、`IdeEditorTabViewModel`、`IdeShellView`、`EditorPanel`
调研：联网检索（2026-11-03）+ 代码盘点 + `docs/ide-mode-design.md`

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 资源管理器 / IDE 模式切换 | `MainWindowViewModel.ToggleIdeMode`（:270）、`MainWindow.axaml.cs:818` | 可用 | 保留标签会话，一键还原 |
| 工作区资源树（懒加载） | `IdeShellViewModel.SetWorkspace`（:255）、`FileTreeItemViewModel` | 可用 | 本地与 SFTP 远程工作区 |
| 多编辑器标签 + 脏标记 + 关闭确认 | `IdeShellViewModel.Editors`（:121）、`CloseEditor`（:535）、`HasDirtyEditors`（:573） | 可用 | 未保存关闭弹确认 |
| 原生编辑器（行号 + 滚动同步） | `IdeEditorTabViewModel`、`EditorPanel.axaml` | 可用 | 采用原生 `TextBox`，无语法高亮（`docs/ide-mode-design.md:58-65`） |
| 快速打开 Ctrl+P | `OpenQuickOpen`（:906）、`ApplyQuickOpenFilter`（:975） | 可用 | 子序列模糊匹配 |
| 当前文件查找 / 替换 | `FindNext`（:588）、`ReplaceCurrent`（:661）、`ReplaceAll`（:688） | 可用 | 基础文本查找，无正则 |
| 工作区搜索 | `SearchWorkspace`（:718）、`SearchWorkspaceCore`（:750） | 可用 | 结果面板 `FindResults` |
| 编码自动探测 | `TextFileCodec.Decode`（:77）、`ITextFileService` | 可用 | BOM → UTF-8 严格 → GBK → Latin-1 |
| 远程文件编辑回传 | `MainWindowViewModel.OnRemoteEditorSaved`（:1119）、`IFileContentAccess` | 可用 | Ctrl+S 回传 SFTP |
| 打开方式规则 | `FileOpenPolicy`、设置“打开方式”分页 | 可用 | 按扩展名优先内置/系统 |
| Web 端 IDE 镜像 | `WebPageHtml.renderIde`（:2625） | 半成品 | 树/标签/编辑区/查找/快速打开均有，但无语法高亮、无远程写回确认 |
| 语法高亮 / LSP / Git / 内置终端 | 无 | 缺口 | 明确不在 v1 范围 |

主要问题：

1. **无语法高亮**：代码阅读效率低，尤其与 VS Code/Zed/Fleet 对比明显。
2. **编辑器能力弱**：无多光标、无代码折叠、无括号匹配、无当前行高亮（或很弱）、无 minimap、无 breadcrumb。
3. **预览标签未闭环**：`OpenFile(..., preview)` 已有预览编辑器（:367、`ReplacePreviewEditor`:461），但缺少“单击预览、双击固定、预览标签斜体标题”的完整交互。
4. **编码切换不可见**：状态栏可能显示编码，但没有“以其他编码重新打开”入口；用户遇到乱码只能改文件。
5. **保存冲突未检测**：外部程序修改同一文件后，IDE 内保存会直接覆盖；无 mtime/hash 冲突提示。
6. **查找不支持正则/大小写/全词**：只有基础字符串查找；工作区搜索结果不能批量替换。
7. **无工作区信任模型**：打开任意目录即可执行“用系统程序打开”，缺少 VS Code 式 Workspace Trust 提示（本项目虽不运行代码，但仍有风险）。
8. **远程与本地体验不一致**：远程文件无法调起系统程序；大文件、二进制、权限错误的提示分散。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| VS Code | Quick Open、预览标签、Workspace Trust、查找替换、命令面板（[Tips](https://code.visualstudio.com/docs/editing/tips-and-tricks)、[Workspace Trust](https://code.visualstudio.com/docs/editing/workspaces/workspace-trust)） | 预览标签 + 信任提示是低成本高收益 |
| Zed | 多缓冲区、项目级搜索、协作；编辑多文件如单文件（[Multibuffers](https://zed.dev/docs/multibuffers)） | 工作区搜索结果可升级为可编辑 multibuffer |
| Fleet | 轻量启动、内置终端、Git、运行调试（[Fleet](https://www.jetbrains.com/fleet/)） | 内置终端可复用现有 `TerminalTabViewModel`，不必新写 |
| AvaloniaEdit | Avalonia 系代码编辑器控件（MIT），提供行号/高亮/折叠 | 需先做 Avalonia 12 兼容性 spike；不兼容则用 TextMate 着色 + 自绘 |
| Windows 11 记事本 | 标签页 + 轻量编辑 | 说明“轻量编辑器”不必追求完整 IDE |

## 3. 推荐方案

### 必做（P0）

1. **语法高亮**
   - 首选 AvaloniaEdit（若与 Avalonia 12.1.4 兼容）：替换 `EditorPanel` 内 `TextBox`，保留现有 `IdeEditorTabViewModel` 的文本/脏标记/保存逻辑。
   - 不兼容则采用“TextMate 语法 + 自绘着色层”或“只读高亮 + 透明输入层”，先覆盖 C#/JSON/XML/Markdown/JS/TS/CSS/HTML/SQL/Shell/BAT/INI。
   - 接入点：`Views/EditorPanel.axaml(.cs)`、`ViewModels/IdeEditorTabViewModel.cs`；`IdeShellViewModel.BuildAccess` 不变。
   - 性能：> 1MB 文件自动关闭高亮，仅纯文本；> 5MB 拒绝打开（`TextFileCodec.MaxTextFileBytes`）。

2. **预览标签完整交互**
   - 单击文件 → 预览标签（标题斜体/弱化），双击或编辑后固定；同一时间最多一个预览标签。
   - `Ctrl+Tab` 在编辑器间切换；`Ctrl+W` 关闭当前；`Ctrl+Shift+W` 关闭全部。
   - 接入点：`IdeShellViewModel.OpenFile(..., preview)`、`ReplacePreviewEditor`、`IdeShellView` 标签条样式。

3. **编码切换与保存冲突**
   - 状态栏编码可点击 → 弹出“以 UTF-8 / GBK / UTF-16 / Latin-1 重新打开”；保存时按当前编码写回。
   - 保存前比较磁盘 mtime + 大小（可选哈希）；若外部已改，弹“重新加载 / 覆盖 / 另存为”。
   - 接入点：`ITextFileService` 增加 `ReadWithEncoding` / `WriteWithEncoding`；`IdeEditorTabViewModel.Save`；`EditorFileAccess`。

4. **查找替换增强**
   - 增加大小写敏感、全词、正则开关；工作区搜索支持按文件类型过滤与排除目录。
   - 接入点：`IdeShellViewModel.FindNext/ReplaceCurrent/ReplaceAll/SearchWorkspaceCore`；`FindResultItem` 增加列号与匹配长度。

5. **工作区信任提示**
   - 首次打开非用户主目录的工作区，顶部横幅“你信任此工作区的作者吗？”；受限模式下禁用“用系统程序打开”、脚本执行与远程写入。
   - 接入点：`IdeShellViewModel.SetWorkspace`、`AppSettings` 增加 `TrustedWorkspaces`。

### 可选（P1/P2）

- 内置终端：复用 `TerminalTabViewModel`，在 IDE 底部面板新增“终端”页，支持本地 ConPTY（README 仍列 M4 未完成）。
- 轻量 Git：状态列（M/A/D）、diff 视图、提交；只读优先，不做完整 Git 客户端。
- LSP：仅 C#（Roslyn）与 TypeScript（tsserver）可选接入；必须可关闭，避免启动变慢。
- Multibuffer：把工作区搜索结果做成可编辑多缓冲区（Zed 思路），P2。
- Breadcrumb + minimap + 代码折叠：随 AvaloniaEdit 一并获得。

### 不建议

- 完整调试器（断点/变量/调用栈）：与“轻量编辑器”定位冲突，工作量大。
- 插件市场 / 扩展宿主：安全与维护成本高。
- 内置 AI Agent 面板：本期不做，避免把产品边界扩散。

## 4. 交互与视觉

- 布局保持五区：标题栏标签条 / 左侧资源树 220px / 编辑器 / 底部面板 / 状态栏。
- 编辑器标签：文件名 + 脏点 + 关闭；预览标签标题斜体、无脏点。
- 状态栏：行列、编码（可点击）、语言、文件大小、远程/本地标识。
- 查找面板：顶部 36px，选项行（Aa、全词、正则），结果列表高亮匹配。
- 大文件：> 1MB 显示“高亮已关闭以保持流畅”；> 5MB 显示“用系统程序打开”。
- 空工作区：居中“打开文件夹 / 打开远程工作区”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 高亮控件 | `Views/EditorPanel.axaml(.cs)`、`ViewModels/IdeEditorTabViewModel.cs` | 先 spike，兼容后再替换 |
| 预览标签 | `IdeShellViewModel.cs`、`IdeShellView.axaml` | 复用现有 preview 参数 |
| 编码/冲突 | `Kernel/Abstractions/ITextFileService.cs`、`Kernel/Services/TextFileCodec.cs`、`Services/EditorFileAccess.cs` | 不改远程 `IFileContentAccess` 契约 |
| 查找增强 | `IdeShellViewModel.cs`、`Views/IdeShellView.axaml` | 正则需超时保护，避免灾难性回溯 |
| 信任模型 | `AppSettingsStore.cs`、`IdeShellViewModel.cs` | 只影响危险动作，不阻断阅读 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| AvaloniaEdit 兼容 | Avalonia 12 可能不兼容，需回退方案 | M（spike S） |
| 高亮性能 | 大文件/长行卡顿 | M |
| 编码切换 | 错误编码写回可能损坏文件；需备份/另存 | M |
| 保存冲突 | 远程 mtime 不可靠，需大小+哈希兜底 | S/M |
| 内置终端 | ConPTY 跨平台与 Web 端差异 | L |

## 7. 验收标准

1. 打开 5000 行 C#/JSON 文件，语法高亮在 500ms 内完成，滚动无明显卡顿。
2. 单击文件出现预览标签，双击固定；编辑预览标签自动固定；同时最多一个预览标签。
3. 状态栏可切换编码并正确重载；保存后文件字节与所选编码一致。
4. 外部修改文件后保存，弹出冲突对话框；选择“重新加载”后内容与磁盘一致。
5. 工作区搜索支持大小写/全词/正则与排除目录；结果点击可定位到行与列。
6. 不受信任工作区禁用系统程序打开与远程写入，横幅可切换信任状态。
7. > 5MB 文本拒绝打开并给出“用系统程序打开”入口。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [VS Code Tips and Tricks](https://code.visualstudio.com/docs/editing/tips-and-tricks) | Quick Open、预览标签 | 2026-11-03 |
| [VS Code Workspace Trust](https://code.visualstudio.com/docs/editing/workspaces/workspace-trust) | 信任模型与受限模式 | 2026-11-03 |
| [Zed Multibuffers](https://zed.dev/docs/multibuffers) | 多文件统一编辑面 | 2026-11-03 |
| [JetBrains Fleet](https://www.jetbrains.com/fleet/) | 轻量 IDE 的内置终端/Git/运行 | 2026-11-03 |
| [VS Code 默认快捷键](https://code.visualstudio.com/docs/reference/default-keybindings) | 查找/替换/快速打开键位 | 2026-11-03 |

注：AvaloniaEdit 与 Avalonia 12 的兼容性为本项目待验证项，未在联网资料中确认，文档中标注为“需 spike”。
