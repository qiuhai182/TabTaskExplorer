# 全局搜索现代化设计

状态：设计稿（2026-11-03）
适用：`SearchTabViewModel`、`SearchPanel`、`IFileSearchService`、`EverythingIpcSearchService`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 全局搜索标签 | `SearchTabViewModel.cs:18-267`、`SearchPanel.axaml` | 可用 | 独立标签，后台线程搜索，结果回 UI |
| Everything IPC | `Platform.Windows/EverythingIpcSearchService.cs:17-100` | 可用 | `WM_COPYDATA` 查询，毫秒级；Everything 未运行自动回退 |
| 内置遍历回退 | `FileSystemWalkSearchService` | 可用 | 跨平台目录遍历 |
| 查询语法 | `SearchQueryParser.cs:11-119` | 可用 | 空格=与、`|`=或、`!`=排除、引号、`path:`、`case:`、`ext:` |
| 结果排序 | `SearchTabViewModel.SetSortField`（:138） | 可用 | 名称/修改日期/大小 |
| 列宽拖动 | `SearchTabViewModel.ApplyColumnWidths`（:54） | 半成品 | 仅本次运行有效，重启恢复默认 |
| 结果打开 | `SearchPanel` 双击/右键 | 可用 | 打开文件或进入目录 |
| 内容搜索 | 无 | 缺口 | 只搜文件名/路径 |
| 搜索历史/保存搜索 | 无 | 缺口 | 每次重新输入 |
| 高级过滤（大小/日期/类型） | 部分 | 半成品 | 仅 `ext:`，无大小/日期范围 |
| 正则 / 模糊 | 无 | 缺口 | 仅关键字与通配式语法 |
| 搜索进度/取消 | 半成品 | 半成品 | 有分代令牌丢弃旧结果，但无显式取消按钮 |
| 索引状态 | 无 | 缺口 | 不显示 Everything 是否运行/索引规模 |

主要问题：

1. **只搜文件名**：无法搜文件内容（代码、日志、文档），与“全局搜索”预期不符。
2. **无搜索历史/保存搜索**：常用查询每次重输；无“最近搜索”。
3. **过滤能力弱**：无大小、修改时间、文件类型多选、排除目录；`ext:` 一次只能一个扩展名。
4. **无显式取消**：大目录遍历时只能等或切标签；分代令牌只是丢弃结果，不停止 IO。
5. **列宽不持久化**：重启后恢复默认，与文件视图不一致。
6. **Everything 状态不可见**：用户不知道结果来自索引还是遍历，速度差异无法解释。
7. **结果操作少**：无“在文件标签打开所在目录并选中”“复制路径”“批量删除/压缩”等批量动作（部分依赖文件视图）。
8. **无内容预览**：结果行不能预览命中片段/高亮。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Everything | NTFS 索引、毫秒级文件名搜索、IPC SDK（[Everything SDK](https://www.voidtools.com/en-us/support/everything/sdk/)） | 已有 IPC；应显示索引状态与回退原因 |
| Windows Search | 内容索引、查询语法、属性过滤 | 内容搜索可作为可选索引源 |
| ripgrep | 高速内容搜索、正则、忽略规则（[ripgrep](https://github.com/BurntSushi/ripgrep)） | 内容搜索可复用其算法/二进制 |
| VS Code 搜索 | 包含/排除 glob、大小写/全词/正则、结果树、替换 | 搜索选项与结果组织 |
| Listary / Everything | 即时搜索、最近记录、快速打开 | 搜索历史与即时反馈 |

## 3. 推荐方案

### 必做（P0）

1. **内容搜索**
   - 增加“文件名 / 内容 / 两者”模式；内容搜索用内置流式扫描（文本文件、大小上限、编码探测）或可选 ripgrep 二进制；显示命中行与列。
   - 接入点：`IFileSearchService` 增加 `SearchMode`；新增 `Kernel/Services/ContentSearchService.cs`；`SearchTabViewModel.ExecuteSearch`（:204-238）。
   - 性能：默认限制文件大小 10MB、跳过二进制、支持取消。

2. **搜索历史与保存搜索**
   - 记录最近 20 条查询（可清除）；“保存搜索”命名并固定到侧边栏/搜索页。
   - 接入点：`AppSettings.SearchHistory/SavedSearches`；`SearchTabViewModel`、`SearchPanel`。

3. **高级过滤**
   - 大小范围、修改时间范围、类型多选、包含/排除目录、隐藏文件开关；UI 用可折叠“筛选”面板。
   - 接入点：`FileSearchOptions` 扩展；`SearchQueryParser` 增加 `size:`、`date:`、`type:`；`EverythingIpcSearchService` 映射到 Everything 语法。

4. **取消与进度**
   - 搜索按钮变“停止”；显示已扫描文件数/当前目录；取消令牌贯穿遍历与内容扫描。
   - 接入点：`SearchTabViewModel` 增加 `CancellationTokenSource`；`FileSystemWalkSearchService`/`ContentSearchService` 接收令牌。

5. **结果操作与预览**
   - 右键：在文件标签打开所在目录并选中、复制路径、用系统程序打开、压缩、删除（走统一文件操作）。
   - 选中结果时底部/右侧显示命中片段（内容搜索）或文件信息。
   - 接入点：`SearchPanel.axaml` 右键菜单；`MainWindowViewModel.OpenPathFromSystem` 类似逻辑；`FileOperationService`。

6. **列宽持久化 + 索引状态**
   - 搜索列宽写入设置；状态栏显示“来源：Everything 索引 / 内置遍历 / 内容扫描”。
   - 接入点：`SearchTabViewModel.ApplyColumnWidths`、`AppSettings`；`IFileSearchService.DisplayName` 已有。

### 可选（P1/P2）

- 即时搜索（输入 300ms 后自动执行）。
- 正则/全词/大小写开关。
- 搜索结果分组（按目录/类型/日期）。
- 保存搜索支持相对路径变量（当前目录）。
- Windows Search 索引源（OLE DB）作为第三种后端。

### 不建议

- 默认全盘内容搜索：IO 与耗时不可控，必须显式选择并限制范围。
- 在 UI 线程遍历目录。
- 把 Everything 作为唯一后端：未安装/未运行时必须可回退。

## 4. 交互与视觉

- 顶部搜索框 + 模式下拉（文件名/内容/两者）+ 搜索/停止按钮。
- 筛选面板：大小、日期、类型、目录包含/排除；已启用条件显示 chip。
- 结果表：名称、路径、大小、修改时间、命中（内容搜索显示行号/片段）。
- 状态栏：来源、已扫描数、命中数、耗时。
- 空状态：提示语法示例（`ext:cs path:src !bin`）。
- 保存搜索：侧边栏“已保存搜索”分组，点击即执行。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 内容搜索 | 新增 `Kernel/Services/ContentSearchService.cs`；`IFileSearchService` | 流式读取 + 编码探测 |
| 过滤 | `Kernel/Models/FileSearchOptions.cs`、`SearchQueryParser.cs`、`EverythingIpcSearchService.cs` | 映射到各后端 |
| 取消 | `SearchTabViewModel.cs`、`FileSystemWalkSearchService.cs` | 令牌贯穿 |
| 历史/保存 | `AppSettingsStore.cs`、`SearchTabViewModel.cs` | JSON 持久化 |
| 结果操作 | `SearchPanel.axaml(.cs)`、`MainWindowViewModel.cs` | 复用文件操作服务 |
| 列宽 | `SearchTabViewModel.cs`、`AppSettings` | 与文件视图同机制 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 内容搜索 | 编码/二进制/大文件/取消延迟 | M |
| 语法扩展 | Everything 与内置遍历语义不一致 | M |
| 结果操作 | 批量删除误操作 | S/M |
| 即时搜索 | 频繁触发 IO | S |

## 7. 验收标准

1. 文件名搜索在 Everything 运行时 < 1 秒返回；未运行时回退并显示来源。
2. 内容搜索可在指定目录内命中关键字，显示行号与片段；二进制文件自动跳过。
3. 搜索可随时停止，停止后 1 秒内不再新增结果。
4. 大小/日期/类型/排除目录过滤生效，且与 Everything 语法映射一致。
5. 最近 20 条搜索与保存搜索重启后保留。
6. 搜索结果右键可定位、复制路径、打开、压缩/删除（按权限）。
7. 列宽重启后保留。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Everything SDK](https://www.voidtools.com/en-us/support/everything/sdk/) | IPC 搜索与索引 | 2026-11-03 |
| [ripgrep](https://github.com/BurntSushi/ripgrep) | 内容搜索与忽略规则 | 2026-11-03 |
| [VS Code 搜索](https://code.visualstudio.com/docs/editor/codebasics#_search-across-files) | 包含/排除/正则/结果树 | 2026-11-03 |
| [Windows Search API](https://learn.microsoft.com/en-us/windows/win32/search/windows-search) | 内容索引备选 | 2026-11-03 |
