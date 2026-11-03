# 拖放现代化设计

状态：设计稿（2026-11-03）
适用：`FileDropPolicy`、`FileListPanel`、`MainWindow` 窗口级拖放、`CapturePanel` 产物历史
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 目录标签页内拖动（移动/复制） | `FileListPanel.BeginFileDrag`（:555-641） | 可用 | 超过 6px 阈值发起系统拖拽 |
| 产物历史只读拷贝 | `FileDropPolicy.FileDragMode.CopyOnly`（:17-24）、`CapturePanel.axaml.cs` | 可用 | 不受 Shift/Ctrl 影响，源文件不动 |
| 拖放效果协商 | `FileDropPolicy.ResolveEffects`（:273-302） | 可用 | 同卷移动、跨卷复制、Ctrl 复制、Shift 移动 |
| 落点合法性 | `FileDropPolicy.IsDropTargetValid`（:227-268） | 可用 | 拒绝空操作、自身子目录、目标不存在 |
| 窗口级落点 | `MainWindow.EvaluateWindowDrop`（:1556）、`HandleWindowDrop`（:1763） | 可用 | 标签、左侧导航、书签、地址栏 |
| 标签头落点 | `ResolveCopyOnlyTabHeaderDrop`（:1726） | 可用 | 只读拷贝源落到标签头时改为复制 |
| 列表内落点 | `FileListPanel.EvaluateListDrop`（:742） | 可用 | 目录行高亮、粘贴到该文件夹 |
| 压缩包/远程限制 | `FileDropPolicy.CheckSourceTargetMatch`（:143-170） | 可用 | 远程↔本机本期不支持；压缩包不参与 |
| 拖拽缩略图 | 无 | 缺口 | Avalonia 12 自定义拖拽预览受限（历史记录已标注） |
| 拖拽自动滚动 / 悬停展开 | 无 | 缺口 | 长列表拖到边缘不滚动；文件夹不自动展开 |
| 跨窗口标签拖拽 | 半成品 | 半成品 | 仅“拖出成新窗口”，无“拖入已有窗口标签条” |

主要问题：

1. **无拖拽视觉反馈**：拖动多文件时只有系统默认光标，没有数量徽标/缩略图；用户不确定拖了什么、会执行复制还是移动。
2. **无自动滚动**：拖到列表上下边缘不会滚动，长目录内移动文件很困难。
3. **无悬停展开（spring-loaded folders）**：拖到文件夹上不会自动展开进入，必须精确落到目标子目录。
4. **标签头语义模糊**：拖到标签头是“切换目录”还是“复制到该目录”？当前只读拷贝源被解析为复制，普通源行为需明确。
5. **远程↔本机不支持**：SFTP/FTP 标签与本地标签之间不能拖，用户只能用传输队列。
6. **压缩包虚拟目录不参与**：不能把文件拖进 zip，也不能从 zip 拖出到本地。
7. **跨窗口拖拽缺失**：不能把一个窗口的标签拖到另一个窗口的标签条。
8. **无虚拟文件投递**：远程/压缩包条目无法以 `VirtualFileDataObject` 形式拖到 Explorer/聊天工具。
9. **修饰键提示缺失**：Ctrl/Shift 效果没有实时提示，用户容易误移动。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows Explorer | 拖拽缩略图、自动滚动、悬停展开文件夹、Ctrl/Shift 效果提示、拖到标签页切换 | 这些是“肌肉记忆”级体验 |
| Directory Opus | 双栏拖放、拖到标签、拖到按钮/工具栏、队列 | 拖到标签/工具栏可作为快捷操作 |
| Total Commander | 双栏拖放、拖到工具栏、压缩包内拖放 | 压缩包内拖放很有价值但复杂 |
| Files | 双栏拖放、标签拖放 | 双栏是拖放的最佳载体 |
| QTTabBar | 拖到标签切换/捕获窗口 | 标签头 drop 语义可借鉴 |

## 3. 推荐方案

### 必做（P0）

1. **拖拽视觉**
   - 自定义拖拽光标：数量徽标（“3 项”）+ 操作徽标（复制/移动/禁止）；若 Avalonia 12 限制自定义预览，则用 `DragDropEffects` + 悬浮提示窗（拖动时跟随鼠标的小窗）。
   - 接入点：`FileListPanel.BeginFileDrag`、`MainWindow` 拖放处理器；新增 `Views/DragHintWindow.axaml`（无焦点、置顶、鼠标穿透）。
   - 说明：不依赖第三方库；Avalonia 不支持时用跟随窗。

2. **自动滚动 + 悬停展开**
   - 拖动到列表上下 24px 区域时按 16px/帧滚动；悬停文件夹 600ms 自动展开（可设置关闭）。
   - 接入点：`FileListPanel.OnListDragOver`（:647）、`FileListPanel` 定时器；`FileTabViewModel.SelectEntry` 定位。

3. **标签头语义明确**
   - 拖到标签头：默认“切换到该标签并导航到拖入目录”？不合理；建议：
     - 普通源 + 目录标签：复制/移动到该标签当前目录（与拖到列表空白一致）。
     - 按住 Shift：切换并导航到源目录（仅单目录源）。
     - 只读拷贝源：始终复制到该标签目录。
   - UI：标签头高亮 + tooltip“复制到 D:\Projects”。
   - 接入点：`MainWindow.EvaluateWindowDrop`、`ResolveCopyOnlyTabHeaderDrop`、`HandleWindowDrop`。

4. **拖放修饰键提示**
   - 拖动时若按 Ctrl/Shift，提示“复制/移动”；落点非法时提示原因（如“不能移动到自身子目录”）。
   - 接入点：`FileDropPolicy.DescribeEffects` 已有文字；`DragHintWindow` 显示。

5. **跨窗口标签拖拽**
   - 允许把标签拖到另一个窗口的标签条并插入；源窗口关闭空标签后不退出。
   - 接入点：`MainWindow.OnTabStripPointerReleased`（:1007）、`WindowRegistry`、`MainWindowViewModel.AttachTab/DetachTab`。

### 可选（P1/P2）

- 远程↔本机拖放：拖放时自动创建传输任务（上传/下载），进度进入任务中心；需要明确“复制而非移动”。
- 压缩包内拖放：向 zip 添加文件（需重写压缩包或追加模式）、从 zip 拖出到本地（先解压临时文件）。
- `VirtualFileDataObject`：远程/压缩包条目拖到 Explorer/聊天工具时按需物化临时文件，完成后清理。
- 拖到工具栏按钮（如“压缩”“邮件发送”）作为快捷动作。
- 双栏拖放（随双栏视图一起做）。

### 不建议

- 依赖 Explorer 的 OLE 拖放实现（`IDropTarget` 注册）：跨平台不一致。
- 对远程条目做“移动”语义：网络中断会造成源丢失，统一为复制。
- 在拖拽过程中执行耗时 IO（如计算目录大小）：拖拽开始必须轻量。

## 4. 交互与视觉

- 拖动开始：源行半透明；跟随提示窗显示“3 项 · 复制”。
- 合法目录：强调色边框 + 淡底；非法：红色边框 + 禁止图标 + 原因。
- 拖到标签：标签头高亮；tooltip“复制到 <目录>”。
- 自动滚动：边缘渐隐提示；滚动速度可随停留时间加速。
- 悬停展开：文件夹行右侧出现“展开”小箭头，600ms 后进入。
- 跨窗口：目标标签条显示插入位置竖线。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 拖拽提示窗 | 新增 `Views/DragHintWindow.axaml(.cs)`；`FileListPanel.axaml.cs`、`MainWindow.axaml.cs` | 无焦点、鼠标穿透、跟随 |
| 自动滚动/悬停 | `FileListPanel.axaml.cs`、`FileTabViewModel.cs` | 定时器随 DragLeave 停止 |
| 标签语义 | `MainWindow.axaml.cs`、`FileDropPolicy.cs` | 统一效果协商，日志写真实动作 |
| 跨窗口 | `MainWindow.axaml.cs`、`Services/WindowRegistry.cs`、`MainWindowViewModel.cs` | 标签对象跨窗口迁移需清理事件订阅 |
| 远程互拖 | `MainWindowViewModel.TransferPathsToDirectory`（:1563）、`RemoteTransferPanelViewModel` | 复制语义 + 任务中心 |
| 虚拟文件 | 内核新增 `IVirtualFileDropService`（P2） | 临时文件生命周期 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 拖拽提示窗 | 多屏/DPI、跟随延迟、鼠标穿透 | S/M |
| 自动滚动/悬停 | 与现有框选/拖拽状态冲突 | M |
| 标签语义 | 用户预期不一致，需要清晰提示 | S |
| 跨窗口 | 标签生命周期/事件解绑 | M |
| 远程互拖 | 传输失败/半完成文件 | M |
| 压缩包拖放 | 压缩包重写、编码、大文件 | L |

## 7. 验收标准

1. 拖动多文件时显示数量与复制/移动提示；非法落点显示原因。
2. 拖到列表上下边缘自动滚动，速度稳定；悬停文件夹 600ms 自动展开。
3. 拖到目录标签头按约定复制/移动到该目录，日志记录真实动作；Shift 导航语义生效。
4. 能把标签拖到另一窗口标签条并插入，源窗口状态正确。
5. 远程标签拖到本地标签创建下载任务并出现在任务中心（P1 验收）。
6. 产物历史拖动始终为复制，源文件 mtime/内容不变。
7. 拖拽取消/失败后无残留高亮、无临时文件。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows 文件资源管理器](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows) | 拖放/标签交互基线 | 2026-11-03 |
| [Directory Opus Tabs](https://docs.dopus.com/doku.php?id=basic_concepts:the_lister:tabs) | 标签与双栏拖放 | 2026-11-03 |
| [Total Commander](https://www.ghisler.com/) | 双栏/压缩包拖放 | 2026-11-03 |
| [Files](https://files.community/) | 双栏拖放产品形态 | 2026-11-03 |
| [QTTabBar 选项](https://qttabbar.wdfiles.com/local--files/documents/options_tab.html) | 标签行为与窗口捕获 | 2026-11-03 |

注：Avalonia 12 自定义拖拽预览的限制来自本项目历史记录（`docs/` 与记忆），未在官方文档中逐条核实；方案已给出跟随提示窗的替代实现。
