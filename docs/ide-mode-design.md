# IDE 模式设计方案（VSCode 风格轻量编辑器）

状态：已实现（M1–M4 全部落地，编辑器控件实测采用方案 B：原生 TextBox + 行号栏）
版本：v1（2026-10-20）

## 1. 目标

在 TabTaskExplorer 内新增「IDE 模式」：一键从资源管理器模式切换为 VSCode 风格的轻量级文本编辑工作区，用于快速查看与编辑文本 / 代码文件。保持轻量——不做调试器、不做插件系统、不做 LSP。

## 2. 入口与切换

- 工具栏新增按钮「IDE 模式」（图标 E943 / E71B，灰字风格与现有工具栏一致），标题栏「工具盒子」旁加同款快捷图标
- 点击后：当前窗口整体切换为 IDE 布局（保留原标签页会话，切回按钮「资源管理器模式」一键还原）
- 两种模式共用：标题栏标签条、状态栏、主题资源、快捷访问数据
- 设置项「默认打开模式」记住上次选择
- 打开文件方式按第 8 节规则：资源管理器模式走系统默认程序，IDE 模式优先内置编辑器

## 3. 布局（对标 VSCode 五区）

```
┌──────────────────────────────────────────────┐
│ 标题栏 + 标签条（现有，右侧加 [资源管理器 ? IDE] 切换）│
├────────┬─────────────────────────────────────┤
│ 资源树  │  编辑器标签条（文件标签，可开多个）      │
│ 侧边栏  │ ┌───────────────────────────────────┐│
│ (220px)│ │  代码编辑区（行号/高亮/当前行）        ││
│ 可折叠  │ │                                   ││
│        │ └───────────────────────────────────┘│
├────────┴─────────────────────────────────────┤
│ 底部面板（可折叠）：输出/查找结果                 │
├──────────────────────────────────────────────┤
│ 状态栏（现有）：行:列 / 编码 / 语言 / N 个项目      │
└──────────────────────────────────────────────┘
```

- 资源树侧边栏：TreeView 懒加载目录树（仅显示文件+文件夹，复用内核 IFileSystemProvider），顶部工作区根路径可切换（默认=切换前浏览的目录）
- 编辑器标签：文件名 + 脏标记圆点 + 关闭 ×；未保存关闭弹确认框

## 4. 组件与数据流

| 组件 | 职责 | 复用 |
| --- | --- | --- |
| `IdeShellViewModel` | 模式容器：资源树 + 编辑器标签集合 + 活动编辑器 | 新增 |
| `FileTreeItemViewModel` | 树节点：懒加载子节点、双击开文件 | 新增 |
| `LocalEditorTabViewModel` | 本地文件编辑（路径、IsDirty、保存） | 参考 `EditorTabViewModel`（远程版已实现 IsDirty/Ctrl+S 保存回传，抽公共基类 `EditorTabViewModelBase`） |
| `IFileContentAccess` | 读/写文本 + 编码探测 | 内核已有（远程实现），新增 `LocalFileContentAccess`（UTF-8 BOM 感知，GBK 回退解码） |
| 编辑器控件 | 行号、语法高亮、当前行高亮 | **AvaloniaEdit**（MIT，Avalonia 官方系） |

### 编辑器控件选型（关键决策）

| 方案 | 优点 | 风险 |
| --- | --- | --- |
| A. AvaloniaEdit + TextMate 高亮 | 行号/折叠/高亮开箱即用，VSCode 观感最接近 | 需验证与 Avalonia 12 兼容（11.x 系列若不兼容则锁定 Avalonia 11.3 或等升级） |
| B. 原生 TextBox（等宽字体+行号栏） | 零依赖零风险 | 无高亮、无折叠 |

**决策**：先做兼容性验证 spike（半天）；可用则 A，不可用则 B 兜底，M1 都能交付。

**落地结果**：为避免 Avalonia 12 与 AvaloniaEdit 的兼容性风险，实测直接采用**方案 B（原生 TextBox + 行号栏）**。
行号栏与编辑器滚动同步通过监听 TextBox 内部 ScrollViewer 的 Offset 实现；代价是**不做语法高亮与代码折叠**。

### 语法高亮范围（M2）

csharp / json / xml / markdown / ini / bat / shell / 常见 Web 三件套；TextMate grammars 内嵌资源包。

**落地结果**：方案 B 无高亮能力，**语法高亮整体不做**；M2 交付「行号栏同步 + Ctrl+P 快速打开」。

## 5. 内核扩展（保持内核纪律）

```csharp
// Kernel/Abstractions/ITextFileService.cs（新增）
public interface ITextFileService
{
    TextFileContent Read(string filePath);      // 内容 + 探测到的编码 + 是否二进制
    void Write(string filePath, string text, Encoding encoding);
}
```

- 默认实现 `DefaultTextFileService`（System.IO + Encoding 探测：BOM → UTF-8 严格校验 → GBK 回退）
- 远程编辑复用现有 `IFileContentAccess`，不动
- 大文件保护：> 5MB 拒绝打开（提示）；二进制（含 \0 判定）改为调用系统默认程序

## 6. 快捷键（并入现有快捷键自定义体系）

Ctrl+S 保存 / Ctrl+Shift+S 全部保存 / Ctrl+F4 关闭编辑器 / Ctrl+P 快速打开（M2，按文件名模糊搜索）/ Ctrl+F 当前文件查找（M3）

## 7. 设置中心新增页「编辑器」

字体（默认 Cascadia Code / 等宽回退）、字号（默认 13）、Tab 宽度（4）、空格替换 Tab、自动换行、显示空白符（M3）、默认打开模式

## 8. 打开文件方式（IDE 与资源管理器分工）

两种模式的打开策略**明确分离**：

- **资源管理器模式**：双击文件一律交给**系统默认程序**（保持原生体验，不劫持关联）。
- **IDE 模式**：默认**优先内置文本编辑器**；只有「不是可编辑文本」（二进制、超过 5MB）或规则指定为系统时才交给系统默认程序。

### 规则模型（内核 `FileOpenPolicy`）

| 概念 | 说明 |
| --- | --- |
| 默认优先级 | `FileOpenDefaultPriority`：Builtin（优先内置，默认）/ System（优先系统） |
| 类型规则 | `FileOpenRules`：扩展名 → 优先级；扩展名填 `*` 表示全部类型 |
| 解析顺序 | 同类型精确规则 → 全部类型规则（`*`）→ 全局默认优先级 |
| 内置回落 | 命中「优先内置」但内容判定为二进制或超限时，本地文件回落到系统默认程序 |

- 支持**按不同类型文件使用不同策略**（例如 `.cs` 优先内置、`.pdf` 优先系统、其余走 `*` 规则）
- 扩展名匹配忽略大小写，用户填写 `txt` 或 `.txt` 均可，保存时统一规整为 `.txt`
- 显式把某类型指定为「优先内置」时，视为用户声明该类型可按文本处理，跳过内置文本扩展名白名单直接尝试解码
- 远程（SFTP）文件无法在本机调起系统默认程序：这类文件若被规则判为系统打开，则在状态栏给出提示

### 设置界面

设置中心新增独立分页「打开方式」：「全部类型默认」下拉 + 「按文件类型单独指定」规则列表（可添加 / 删除，逐条选择优先内置或优先系统）。

## 9. 分期

| 期 | 内容 | 验收 |
| --- | --- | --- |
| M1 | 模式切换 + 资源树 + 本地文件打开/编辑/保存（含编码）+ 脏标记 + 关闭确认 | 在 IDE 模式下双击 .cs → 内置编辑器编辑保存，重启内容正确 |
| M2 | AvaloniaEdit 高亮 + 行号 + 快速打开 Ctrl+P | 高亮渲染正确、Ctrl+P 秒开任意文件 |
| M3 | 查找替换（当前文件 + 输出面板）、显示空白、迷你地图（可选） | — |
| M4 | 远程文件进 IDE：SSH/SFTP 文件在资源树中直接编辑（复用远程会话） | 远程 .sh 编辑保存回传 |

**落地情况**：

| 期 | 状态 | 说明 |
| --- | --- | --- |
| M1 | 已完成 | 模式切换（标题栏图标 + 工具栏「IDE 模式」按钮）、懒加载资源树、本地文本打开/编辑/保存、脏标记圆点、关闭与退出确认；打开方式按第 8 节规则解析 |
| M2 | 已完成（不含高亮） | 行号栏随内容与滚动同步；Ctrl+P 按文件名子序列模糊匹配快速打开 |
| M3 | 已完成（不含空白符渲染） | 当前文件查找/替换/全部替换 + 工作区搜索结果面板；「显示空白符」仅保留设置项，原生 TextBox 不渲染 |
| M4 | 已完成 | 连接面板新增「在 IDE 中打开」，以远程目录为工作区，SFTP 读写在资源树内直接编辑保存回传 |

## 10. 明确不做

调试器、Git 集成、多光标（依赖控件能力，非目标）；插件市场与 VS Code 扩展兼容见 10.1；代码补全等语言智能见 10.1 的 LSP 观望项。

### 10.1 不实现 VS Code 扩展兼容（决策）

**结论**：不接入 VS Code 插件生态，不实现 Extension Host，不建扩展市场。

理由：

- VS Code 扩展（`.vsix`）是运行在 Node.js Extension Host 进程中的程序，依赖数千个 `vscode.*` API（`TextDocument`、`CodeLens`、`TreeView`、`Webview`、`workspace.fs` 等），与本项目的 Avalonia + .NET 技术栈无任何可复用部分，兼容等于重写一个 Extension Host。
- Extension Host 自带独立进程与 Node 运行时，用户装入带 Webview 或语言服务的插件后内存占用会从几十 MB 升至数百 MB，与「轻量化、低占用」这一硬约束直接冲突。
- VS Code 扩展 API 持续演进，兼容层需要长期跟进，属于持续性技术债。
- 需要扩展生态时应换产品路线（如基于 Code-OSS 二次开发），而不是在轻量编辑器内模拟。

**替代与观望**：

- 编辑器侧的可扩展性优先通过进程内扩展点（文件类型处理器、外部工具集成接口）满足，不引入任何插件运行时。
- 「LSP 客户端」列为**长期观望项**，非近期计划：前置条件是现有轻量编辑器经过实际使用后确认确有语言智能（补全/诊断/跳转）需求。若届时引入，只做 stdio 子进程 + JSON-RPC 的最小客户端，复用 `pylsp` / `clangd` / `rust-analyzer` 等现成语言服务器，不引入 Node 运行时。

## 11. 风险

| 风险 | 缓解 |
| --- | --- |
| AvaloniaEdit 与 Avalonia 12 兼容性未验证 | 已用方案 B（原生 TextBox）规避，不引入外部控件依赖 |
| 编码误判（GBK/UTF-8 混合项目） | BOM 优先 + 严格 UTF-8 校验 + 状态栏显示探测编码可手动切换重载 |
| 与资源管理器模式状态互扰 | 两套 ViewModel 树并存，切换仅改 IsVisible，标签会话各自保持 |
| 打开方式规则配错导致文本文件被外部程序打开 | 规则改动即时可见（设置页规则列表），默认兜底为「优先内置」；二进制/超限文件仍回落系统程序 |

## 12. 合规

UI 风格参考 VSCode 观感（配色/布局），不复制其源码与图标资源；图标用 Segoe MDL2 / FluentUI 系统字体图标。

## 13. 实现落点（代码索引）

| 层 | 文件 | 职责 |
| --- | --- | --- |
| 内核 | `Kernel/Models/TextFileContent.cs` | 文本内容 + 编码名 + 二进制/超大标记 |
| 内核 | `Kernel/Abstractions/ITextFileService.cs` | 本机文本读写的抽象（编码名以字符串表达，便于状态栏显示与持久化） |
| 内核 | `Kernel/Services/TextFileCodec.cs` | 编码探测与编解码（BOM → 严格 UTF-8 → GBK → Latin-1 兜底） |
| 内核 | `Kernel/Services/DefaultTextFileService.cs` | 本机实现（5MB 上限、二进制与大文件保护） |
| 内核 | `Kernel/Services/FileOpenPolicy.cs` | 打开方式规则模型与优先级解析（纯逻辑，跨平台） |
| 应用 | `App/Services/EditorFileAccess.cs` | 统一文件访问：本机走 ITextFileService，远程走 IFileContentAccess |
| 应用 | `App/ViewModels/FileTreeItemViewModel.cs` | 懒加载资源树节点 |
| 应用 | `App/ViewModels/IdeEditorTabViewModel.cs` | 编辑器标签（脏标记、保存、光标行列、编码） |
| 应用 | `App/ViewModels/IdeShellViewModel.cs` | IDE 外壳（工作区、标签集合、查找替换、快速打开、打开方式判定） |
| 应用 | `App/Views/IdeShellView.axaml(.cs)` | IDE 五区视图与行号栏同步、快捷键桥接 |
| 应用 | `App/ViewModels/MainWindowViewModel.cs` | 模式切换、工作区解析、远程 IDE 入口 |
| 应用 | `App/Views/MainWindow.axaml(.cs)` | 标题栏/工具栏切换入口、模式互斥可见性、IDE 快捷键 |
| 应用 | `App/Views/SettingsWindow.axaml(.cs)` | 编辑器页与打开方式页（规则列表增删与提交） |
