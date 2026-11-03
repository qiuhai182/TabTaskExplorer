# 磁盘视图现代化设计

状态：设计稿（2026-11-03）
适用：`DiskViewPanel`、`DiskViewPanelViewModel`、`DiskScanService`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 驱动器总览 | `DiskViewPanelViewModel.LoadDrives`（:826）、`BuildDriveRow`（:975） | 可用 | 名称、类型、总容量、剩余、使用率、文件系统 |
| 目录占用树 | `DiskViewPanelViewModel.ExpandRow`（:612）、`ApplyChildren`（:667） | 可用 | 懒加载展开，子目录按大小排序 |
| 目录大小统计 | `DiskScanService`、`StartRowBackfill`（:544）、`ApplyBackfill`（:733） | 可用 | 后台扫描 + 进度回调 + 心跳超时 |
| 排序 / 列宽 | `SetSortField`（:152）、`ColumnResize`（`DiskViewPanel.axaml:21-31`） | 可用 | 名称/类型/大小/剩余/使用率/文件系统 |
| 导航与右键 | `NavigateUp`（:345）、`DiskViewPanel.axaml:73-79` | 可用 | 打开、新标签打开、复制路径、粘贴 |
| 扫描进度 | `BuildProgressText`（:1055）、`SummaryText` | 可用 | 文本进度，无总进度条 |
| 树图 / 类型分布 / 大文件排行 | 无 | 缺口 | 目前只有表格树 |
| 扫描排除 / 暂停 / 导出 | 无 | 缺口 | 只能等待或关闭标签 |

主要问题：

1. **只有表格树，没有空间可视化**：用户难以一眼看出“哪个目录最占空间”，需要逐层展开。
2. **扫描为全量遍历**：`DiskScanService` 逐目录 `Enumerate`，大磁盘/网络盘耗时长；无 NTFS MFT/USN 快速路径，无排除规则（如 `Windows\WinSxS`、回收站、系统卷影）。
3. **无文件类型维度**：无法回答“空间被视频/缓存/安装包占了多少”。
4. **无 Top-N 大文件**：定位大文件需要反复展开。
5. **无导出/分享**：无法把扫描结果导出 CSV/JSON 做离线分析。
6. **进度反馈弱**：只有文本，没有取消按钮、没有已扫描文件数/当前路径。
7. **驱动器合并逻辑复杂**：`MergeDriveRows` 保留旧行以避免闪烁，但容量变化时可能短时不一致。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 存储设置 | 按类别（应用/文档/图片/视频）汇总，提供清理入口 | “类型分布 + 清理建议”比纯目录树更易理解 |
| WizTree / TreeSize | NTFS MFT 直读 + Treemap + Top 文件 | MFT 快速扫描是 Windows 大盘的性能关键；Treemap 是标准可视化 |
| WinDirStat | Treemap + 扩展名图例 + 文件列表联动 | Treemap 与列表联动、按扩展名着色 |
| Directory Opus | 内置同步、重复文件、文件夹大小列 | 目录大小可作为文件视图列，与磁盘视图共用扫描器 |
| Total Commander | 双栏 + 目录大小统计 + 压缩包浏览 | 扫描结果可回填到文件视图 |
| Everything | 索引式毫秒搜索（[Everything SDK](https://www.voidtools.com/en-us/support/everything/sdk/)） | 已有 `EverythingIpcSearchService`，可把索引能力用于“大文件快速筛选” |

## 3. 推荐方案

### 必做（P0）

1. **Treemap 视图切换**
   - 顶部增加“列表 / 树图”切换；树图按面积=大小、颜色=类型/深度，支持点击钻取、悬停显示路径与大小、右键“在文件标签打开”。
   - 接入点：`DiskViewPanel.axaml` 增加 `TreemapControl`（自绘 `Control`，避免引入图表库）；`DiskViewPanelViewModel` 暴露 `BuildTreemap(root, maxDepth=4)`。
   - 只渲染可见矩形，最多 2000 个块；避免一次性为全盘生成节点。

2. **Top-N 大文件 + 类型分布**
   - 扫描完成后输出：前 100 大文件、按扩展名/类别聚合（视频、图片、文档、压缩包、安装包、缓存、其他）。
   - UI：磁盘视图底部两个可折叠面板；点击条目定位到所在目录。
   - 接入点：`DiskScanService` 增加 `ScanSummary`（大文件最小堆 + 扩展名字典）；`DiskViewPanelViewModel.ApplyCompleted`（:592）消费。

3. **扫描控制与排除规则**
   - 增加“暂停 / 继续 / 取消”按钮；扫描前可配置排除目录（默认排除 `$Recycle.Bin`、`System Volume Information`、`WinSxS`、`node_modules`、`.git`）。
   - 接入点：`DiskScanService` 增加 `CancellationToken` 与 `DiskScanOptions.Exclusions`；`DiskViewPanelViewModel.ReleaseCurrentScan`（:1016）已有清理入口。

4. **导出扫描结果**
   - 导出 CSV（路径、类型、大小、修改时间）与 JSON 摘要；输出到 `output/` 或用户选择目录。
   - 接入点：`DiskViewPanel` 工具栏新增“导出”；`DiskScanService` 保留原始行或流式写入。

5. **进度条与当前路径**
   - `ProgressBar` + “已扫描 N 个文件 / 当前目录”；避免只在状态文字里滚动。
   - 接入点：`DiskScanService.DiskScanProgress` 增加 `CurrentPath`、`FileCount`；`DiskViewPanelViewModel.OnScanProgress`（:558）。

### 可选（P1/P2）

- NTFS MFT 快速扫描（Windows）：读取 `$MFT` 解析文件记录，秒级完成；需管理员权限，失败回退遍历。
- 重复文件查找：按大小分组 + 首尾哈希 + 全量哈希；与文件视图共用。
- 清理建议：回收站、下载目录、临时文件、旧安装包；只做“建议 + 打开位置”，不自动删除。
- 磁盘健康：SMART/剩余寿命（Windows `MSFT_StorageReliabilityCounter`），只读展示。
- 网络盘/远程目录占用：通过 `IFileSystemProvider` 复用，但需限制深度与并发。

### 不建议

- 在内核层引入第三方 Treemap 图表库：自绘 2000 个矩形足够，依赖越少越好。
- 自动清理/自动删除：风险高，先做建议与手动确认。
- 实时文件系统监控（USN 日志）作为默认：资源占用与权限问题，作为可选高级功能。

## 4. 交互与视觉

- 顶部：路径输入 + 打开/上一级/刷新 + 视图切换（列表/树图）+ 扫描/取消 + 导出。
- 摘要：总大小、文件数、目录数、扫描耗时、排除规则数。
- 树图配色：按顶层目录分配色相，深度调整明度；悬停高亮并显示 tooltip（路径、大小、占比）。
- 表格：大小右对齐、使用率用细进度条、文件系统用弱化文本。
- 底部面板：Top 文件列表（名称、大小、路径、修改时间）与类型分布（横向条形）。
- 空/无权限状态：明确“需要管理员权限才能扫描该位置”，提供“以管理员重启”入口。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| Treemap | `Views/DiskViewPanel.axaml`、新增 `Views/TreemapControl.cs`、`DiskViewPanelViewModel.cs` | 纯 Avalonia 自绘，按可见区裁剪 |
| 扫描摘要 | `Services/DiskScanService.cs`、`Kernel/Models/DiskUsageSnapshot.cs` | 大文件最小堆 + 扩展名聚合 |
| 取消/排除 | `DiskScanService.cs`、`DiskViewPanelViewModel.cs`、`AppSettingsStore.cs` | 取消令牌贯穿 enumerate |
| 导出 | `DiskViewPanel.axaml(.cs)`、新增 `Services/DiskReportExporter.cs` | CSV UTF-8 BOM，Excel 可直接打开 |
| MFT | 新增 `Platform.Windows/NtfsMftScanner.cs`（P2） | 失败必须无损回退 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| Treemap | 大量矩形绘制性能；极端目录层级 | M |
| 扫描摘要 | 内存占用（大文件堆可控，扩展名聚合可控） | S/M |
| 取消/排除 | 现有扫描递归需全面接入令牌 | M |
| MFT | 权限、NTFS 版本、解析错误；需要大量测试 | L |
| 重复文件 | 哈希 IO 大，需限速与缓存 | M |

## 7. 验收标准

1. 100 万文件目录扫描可取消，取消后 2 秒内停止 IO 并释放 UI。
2. 树图在 2000 个矩形内拖动/缩放流畅；点击块可钻取到下一层。
3. 扫描完成展示 Top 100 大文件与类型分布；点击条目可跳转并选中文件。
4. 排除规则生效，扫描结果摘要写明已排除目录。
5. 导出 CSV 可被 Excel 正确打开（中文不乱码），行数与扫描结果一致。
6. 无权限目录不导致整次扫描失败，摘要中单独统计“不可访问目录数”。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Everything SDK](https://www.voidtools.com/en-us/support/everything/sdk/) | 索引式搜索与 IPC 能力 | 2026-11-03 |
| [Total Commander](https://www.ghisler.com/) | 目录大小、双栏、压缩包浏览 | 2026-11-03 |
| [Directory Opus 功能页](https://directory-opus.com/) | 同步、重复文件、查看器 | 2026-11-03 |
| [Windows 文件资源管理器](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows) | 存储/文件管理入口的组织方式 | 2026-11-03 |

注：WizTree / WinDirStat 的具体实现细节未在官方文档中完整披露，本方案只采用“MFT + Treemap + Top 文件”这一公开的产品形态，不声称核对其内部算法。
