# 文件 / 文件夹视图现代化设计

状态：设计稿（2026-11-03）
适用：`FileListPanel`、`FileTabViewModel`、`MainWindowViewModel` 文件操作
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 详细信息 / 大图标 / 中图标视图 | `FileTabViewModel.SetViewMode`（:445）、`CycleViewMode`（:451）；`FileListPanel.axaml:70-100` | 可用 | 图标视图为矢量文件类型图标，非缩略图 |
| 列头排序、列宽拖动 | `FileListPanel.axaml:29-66`、`ColumnResize.cs`、`FileTabViewModel.ApplyColumnWidths`（:347） | 可用 | 仅名称/修改日期/类型/大小四列 + 勾选/图标列 |
| 勾选式多选 + 框选 | `FileListPanel.axaml.cs:244-504`；`FileTabViewModel.SetCheckedPaths`（:1028） | 可用 | 以勾选框为主，非 Explorer 的 Ctrl/Shift 焦点选择模型 |
| 双击打开、目录进入、文件由系统程序打开 | `FileTabViewModel.OpenEntry`（:706）、`FileOpenPolicy` | 可用 | 资源管理器模式固定走系统默认程序 |
| 当前目录过滤搜索 | `FileTabViewModel.ApplyFilter`（:1290） | 可用 | 仅当前目录；全局搜索是独立 Search 标签 |
| 重命名 / 批量重命名 | `RenameChecked`（:524）、`BatchRenamePlanner`、`BatchRenameDialog` | 可用 | 支持查找替换、前后缀、序号、扩展名 |
| 复制 / 剪切 / 粘贴 | `MainWindowViewModel.CopyCheckedToClipboard`（:1348）、`PasteFromClipboardAsync`（:1438） | 可用 | 系统剪贴板 + 内部剪贴板；冲突处理较简单 |
| 删除到回收站 | `WindowsFileSystemProvider.DeleteToRecycleBin`（:35-61） | 可用 | `SHFileOperation` + `FOF_ALLOWUNDO` |
| 压缩 / 解压 | `FileTabViewModel.CompressSelection`（:806）、`ExtractSelectionTo`（:859）、`SharpArchiveEngine` / `TarArchiveEngine` | 可用 | 压缩包可作为虚拟目录标签打开 |
| 信息窗格 | `FileListPanel.UpdateInfoPane`（:1968）、`FileTabViewModel.ShowInfoPane` | 可用 | 基础元数据 |
| 大目录虚拟化 | `FileListPanel.axaml:72` ListBox | 可用 | 依赖 Avalonia 虚拟化；后台分页读取 `FetchInBackground`（:1137） |
| 缩略图 / 预览 | 无 | 缺口 | README 路线图仍列为未完成 |
| 双栏 / 列视图 / 标签页 | 无（标签页是窗口级） | 缺口 | 无 Directory Opus/Total Commander 式双栏 |
| 文件操作撤销 | 无 | 缺口 | 删除只能去回收站手动恢复 |

主要问题：

1. **列固定且不可配置**：`FileListPanel.axaml:32-39` 写死 6 列；无尺寸、时长、EXIF、所有者、云状态等常用列，也无法“应用到所有文件夹”。
2. **图标视图没有缩略图**：`FileTypeIcon` 仅按扩展名画矢量图标，图片/视频/PDF 文件夹的浏览效率明显低于 Explorer、Files、Directory Opus。
3. **缺少快速预览**：内置媒体查看器已存在（`MediaTabViewModel`），但列表没有 Space 预览；用户必须双击开新标签。
4. **选择模型不标准**：勾选式多选对批量操作友好，但与 Ctrl/Shift 连选、Ctrl+A、方向键扩展选择不一致；`OnPointerPressedForDrag` 自定义框选替代了原生选择。
5. **文件操作缺少冲突与进度**：`PasteFromClipboardAsync` 直接复制/移动；同名文件、只读、跨卷、长路径、权限拒绝的错误提示不完整；无统一进度与取消。
6. **无“每文件夹视图”记忆**：列宽、排序、视图模式未按目录持久化；用户在不同目录反复调整。
7. **重命名校验不足**：未统一处理 Windows 保留名（CON/NUL）、结尾空格/点、非法字符、大小写仅变更。
8. **信息窗格弱**：只有基础字段，缺少图片尺寸/EXIF、音频标签、PDF 页数、哈希等。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows Explorer | 新简化右键菜单、常用命令置顶、云文件状态列（[Microsoft 支持](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows)） | 右键菜单“常用命令优先 + 更多选项”分层；云状态应有独立列 |
| Directory Opus | 双窗格 + 多标签 + 高级重命名 + 内置查看器（[Tabs 手册](https://docs.dopus.com/doku.php?id=basic_concepts:the_lister:tabs)、[Advanced Rename](https://docs.dopus.com/doku.php?id=file_operations:renaming_files:advanced_rename)） | 高级重命名已具备；下一步是双栏与查看器侧栏 |
| Total Commander | 双栏、缩略图、自定义列、压缩包当目录（[官网](https://www.ghisler.com/)） | 自定义列与压缩包虚拟目录是刚需，可先做列配置 |
| Double Commander | 双栏、多标签、多重重命名、内置查看器（F3）、自定义列（[文档](https://doublecmd.github.io/doc/en/help.html)） | F3/Space 预览应成为标配 |
| Files | 双栏、彩色标签、列视图、标签侧边栏（[Files](https://files.community/)） | 彩色标签/评分可作为 P2 的轻量组织能力 |
| QTTabBar | 标签增强、窗口捕获、工具栏自动隐藏（[手册](https://qttabbar.wdfiles.com/local--files/documents/options_tab.html)） | 标签增强思路可借鉴，但本项目已自绘标签条 |

## 3. 推荐方案

### 必做（P0）

1. **缩略图服务**
   - 内核新增 `IThumbnailProvider`：`Task<ThumbnailResult> GetAsync(path, size, ct)`；返回位图字节、失败原因、是否走缓存。
   - Windows 实现：`IShellItemImageFactory::GetImage`（系统缩略图，覆盖 Office/PDF/视频）+ WIC 解码图片；Linux/macOS 用各自缩略图服务或纯解码回退。
   - 应用层 `ThumbnailCache`：内存 LRU（200 项）+ 磁盘缓存（`%APPDATA%\TabTaskExplorer\thumbs`，键为路径+大小+修改时间）；后台优先级队列，可见区优先。
   - 接入点：`FileListPanel.axaml` 图标列改为 `ThumbnailImage` 控件；`FileTabViewModel` 在 `ApplyFetchedRows` 后触发可见区缩略图请求；`FileTypeIcon` 保留为回退。
   - 注意：不要在主线程解码；不要在滚动时同步请求。

2. **快速预览（Space / F3）**
   - 复用 `MediaTabViewModel` 的图片/视频/音频解码，文本用 `ITextFileService`，压缩包列出条目，PDF 可选系统预览。
   - 交互：空格切换右侧预览栏（宽 320，可拖拽），再按空格或 Esc 关闭；预览栏不新开标签。
   - 接入点：`FileListPanel.axaml.cs` 新增 `OnPreviewKey`；`MainWindowViewModel` 提供 `PreviewSelection`；`MediaPanel` 抽出可复用显示控件。
   - 大文件保护：文本 > 5MB、图片 > 配置上限时显示“按系统程序打开”。

3. **列配置 + 每文件夹视图记忆**
   - 新增 `FolderViewSettings`（`ViewMode / SortField / SortAscending / ColumnWidths / VisibleColumns`），存 `AppSettings.FolderViews`（路径规范化后作键，最多 200 条）。
   - 列选择器：右键表头 → “选择列”；支持名称、修改日期、类型、大小、尺寸、时长、创建日期、访问日期、所有者、云状态。
   - 接入点：`FileListPanel.axaml` 列定义改为动态 `ItemsControl`；`ColumnResize` 绑定到设置；`FileTabViewModel.ApplyFolderSettings`。

4. **统一文件操作服务（冲突 / 进度 / 取消）**
   - 内核新增 `IFileOperationService`：`Copy/Move/Delete` 输入源、目标、冲突策略（覆盖/跳过/保留两者/重命名）、进度回调、取消令牌。
   - Windows 实现优先用 `IFileOperation`（系统进度与冲突 UI、回收站、权限错误统一）；其他平台用托管实现 + 自有冲突对话框。
   - 接入点：`MainWindowViewModel.PasteFromClipboardAsync`、`TransferPathsToDirectory`、`FileTabViewModel.DeleteChecked/Compress/Extract`；新增 `FileOperationDialog` 显示队列进度。
   - 与“下载/进度”模块共用 `IProgressCenter`（见 `transfer-progress.md`）。

5. **标准键盘与选择模型**
   - 保留勾选框，但增加 Explorer 兼容：Ctrl 点击切换、Shift 点击区间、Ctrl+A、方向键移动焦点、Shift+方向键扩展、Esc 清除。
   - 快捷键：F2 重命名、Delete 删除、Ctrl+C/X/V、Alt+Enter 属性、Space 预览、F5 刷新、Backspace 上一级。
   - 接入点：`FileListPanel.axaml.cs` 指针与键盘处理；`FileTabViewModel` 增加 `FocusedRow` 与 `SelectionAnchor`。

6. **重命名与删除健壮性**
   - 统一 `FileNameValidator`（内核）：保留名、非法字符、结尾空格/点、长度、大小写冲突、目标已存在。
   - 删除前对只读/正在使用/无权限给出可操作提示；批量删除显示数量与回收站说明。
   - 接入点：`FileTabViewModel.RenameChecked/RenameRow`、`BatchRenamePlanner`、`WindowsFileSystemProvider`。

### 可选（P1/P2）

- 双栏视图：一个窗口左右两个文件列表，共享标签条；`MainWindow` 增加 `DualPane` 布局，`FileTabViewModel` 支持两个实例。
- 列视图（Miller columns），参考 Files；适合深层目录浏览。
- 彩色标签 / 评分 / 备注：存 sidecar JSON 或 NTFS ADS；先做标签，不做全文检索。
- 云文件状态列与按需水合：读取 `CfGetPlaceholderState`，显示“仅在线/已下载/待上传”；不实现同步引擎。
- 文件哈希、重复文件查找：与磁盘视图共用扫描器。

### 不建议

- Ribbon 工具栏：与现有紧凑工具栏风格冲突。
- 完整插件系统：维护成本高，先用内置能力覆盖 80% 场景。
- 在列表内嵌入完整编辑器：继续用标签页打开。

## 4. 交互与视觉

- 行高 28；图标 16（详细视图）/ 48（大图标）/ 32（中图标）；缩略图 96×96（大图标）。
- 悬停行显示浅色底；选中行使用强调色左边条 + 淡底；勾选框始终可见但弱化。
- 表头：列边界 4px 热区；排序列显示箭头；右键表头弹出列选择器。
- 预览栏：右侧 320px，顶部文件名 + 大小 + 修改时间，底部“用系统程序打开 / 在内置查看器打开”。
- 空目录：居中插画 + “新建文件夹 / 粘贴”两个按钮。
- 操作冲突对话框：左侧源/目标对比，右侧“覆盖 / 跳过 / 保留两者 / 全部应用”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 缩略图 | 新增 `Kernel/Abstractions/IThumbnailProvider.cs`；`Platform.Windows/WindowsThumbnailProvider.cs`；`App/Services/ThumbnailCache.cs` | 平台差异收敛在内核抽象后 |
| 预览 | `Views/FileListPanel.axaml(.cs)`、`Views/MediaPanel.axaml`、`Services/Media/*` | 复用媒体解码，避免第二套播放器 |
| 列配置 | `Services/AppSettingsStore.cs`、`ViewModels/FileTabViewModel.cs`、`Views/FileListPanel.axaml` | 路径键需规范化，避免大小写重复 |
| 文件操作 | 新增 `Kernel/Abstractions/IFileOperationService.cs`；`Platform.Windows/WindowsFileOperationService.cs`；`App/Views/FileOperationDialog.axaml` | 与回收站、权限、长路径统一 |
| 键盘选择 | `Views/FileListPanel.axaml.cs`、`ViewModels/FileTabViewModel.cs` | 不改现有勾选逻辑，增量叠加 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 缩略图 | Shell 扩展崩溃/卡死；缓存膨胀；远程与压缩包无缩略图 | M |
| 预览 | 视频解码器生命周期；大文件内存 | M |
| 列配置 | 动态列与现有固定列布局冲突，需回归列宽/滚动条对齐 | M |
| 文件操作服务 | `IFileOperation` 跨线程/STA；非 Windows 行为差异 | M/L |
| 双栏 | 标签模型与布局大改 | L |

## 7. 验收标准

1. 含 1000 张图片的目录滚动流畅（60fps 目标），缩略图异步加载且命中缓存后 < 50ms 显示。
2. Space 预览图片 < 300ms、文本 < 200ms；Esc 关闭后焦点回到列表。
3. 列选择器可增删列，列宽与可见列按文件夹持久化，重启后恢复。
4. 复制 1000 个文件显示统一进度、可取消；同名冲突可选择覆盖/跳过/保留两者，且“全部应用”生效。
5. Ctrl/Shift 选择、Ctrl+A、F2、Delete、Alt+Enter 与 Explorer 习惯一致；勾选式批量操作仍可用。
6. 重命名非法名称、保留名、大小写冲突时给出明确中文提示且不落盘。
7. 删除只读/占用文件时提示原因，不出现静默失败。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows 文件资源管理器](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows) | 新右键菜单、常用命令分层 | 2026-11-03 |
| [Directory Opus Tabs](https://docs.dopus.com/doku.php?id=basic_concepts:the_lister:tabs) | 多标签 + 双栏组织 | 2026-11-03 |
| [Directory Opus Advanced Rename](https://docs.dopus.com/doku.php?id=file_operations:renaming_files:advanced_rename) | 批量重命名能力边界 | 2026-11-03 |
| [Total Commander](https://www.ghisler.com/) | 双栏、缩略图、自定义列、压缩包目录 | 2026-11-03 |
| [Double Commander 帮助](https://doublecmd.github.io/doc/en/help.html) | 标签、查看器（F3）、自定义列 | 2026-11-03 |
| [Files](https://files.community/) | 双栏、彩色标签、列视图 | 2026-11-03 |
