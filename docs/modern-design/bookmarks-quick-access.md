# 书签与快速访问现代化设计

状态：设计稿（2026-11-03）
适用：`MainWindowViewModel` 的 `QuickAccessItems` / `ThisPcItems` / `PinnedPaths` / `RecentPaths`、`MainWindow.axaml` 书签栏与导航
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 快捷访问固定 | `MainWindowViewModel.PinPath`（:1325-1342）、`Unpin`（:1345-1351） | 可用 | 路径写入 `AppSettings.PinnedPaths` |
| 书签栏 | `MainWindow.axaml:164-198` | 可用 | 横向图标 + 名称，悬停显示取消固定 |
| 左侧“快捷访问” | `MainWindow.axaml:378-397`、`FillQuickAccessItems`（:3416-3429） | 可用 | 与书签栏同一数据源 |
| 此电脑分组 | `FillThisPcItems`（:3383-3401） | 可用 | 驱动器 + 桌面/文档/图片/音乐/视频/下载 |
| 最近访问 | `RecentPaths`（:491）、`RecordRecent`（:495）、`MainWindow.axaml:337-360` | 半成品 | 仅在内存，重启丢失；数量上限需确认 |
| 拖入创建书签 | `MainWindow.EvaluateWindowDrop`（:1556+） | 可用 | 拖到书签栏区域创建固定 |
| 标签右键添加书签 | `MainWindow.axaml:57`、`OnTabMenuAddToBookmarks`（:2064） | 可用 | 当前标签路径加入固定 |
| 导航高亮 | `MatchNavByPath`（:3431-3453） | 可用 | 精确/前缀匹配，最长者胜 |
| 书签分组/排序/重命名 | 无 | 缺口 | 扁平列表，按添加顺序 |
| 最近访问持久化 | 无 | 缺口 | 内存集合 |
| 书签搜索/备注 | 无 | 缺口 | 只能肉眼找 |
| 失效路径提示 | 半成品 | 半成品 | 打开时若不存在，缺少统一标记 |

主要问题：

1. **书签是扁平列表**：没有分组、排序、拖拽调整、重命名；固定多了以后书签栏溢出。
2. **最近访问不持久化**：`RecentPaths` 只在当前进程；重启后“最近”为空。
3. **无失效检测**：移动/删除的固定路径仍显示，点击才报错；没有“失效书签”标记与批量清理。
4. **无书签搜索**：左侧导航和书签栏都没有过滤框。
5. **拖放语义单一**：拖入只创建固定；没有“拖到分组/排序”的交互。
6. **无导入/导出**：不能从浏览器书签或其他文件管理器迁移。
7. **书签与标签固定混淆**：标签固定（`TogglePinTab`）和路径固定（`PinPath`）是两套概念，UI 未明确区分。
8. **无跨设备同步**：P2，但用户可能期待。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows Explorer 快速访问 | 固定文件夹、最近使用、从快速访问取消固定 | 最近访问应持久化 |
| Edge/Chrome 书签 | 书签栏、书签管理器、文件夹、搜索、导入导出 | 分组 + 搜索 + 管理器 |
| Directory Opus | 收藏夹、按钮栏、标签组、热键 | 收藏可绑定热键 |
| Total Commander | 目录热列表（Ctrl+D）、历史、书签 | 热列表 + 历史 |
| QTTabBar | 书签栏、分组、拖放 | 书签栏交互参考 |
| VS Code | 工作区/最近打开、固定 | 最近列表与固定分离 |

## 3. 推荐方案

### 必做（P0）

1. **最近访问持久化**
   - `AppSettings.RecentPaths`（最多 20 条），启动加载；同一路径去重并置顶；提供“清除最近”。
   - 接入点：`MainWindowViewModel.RecordRecent`（:495）、构造函数加载、`AppSettingsStore`。

2. **书签管理器**
   - 独立对话框/设置页：分组树、拖拽排序、重命名、编辑路径、删除、搜索、失效检测。
   - 分组模型：`BookmarkGroup { Id, Name, Order }` + `Bookmark { Path, Name, GroupId, Order }`；兼容旧 `PinnedPaths`（迁移为“未分组”）。
   - 接入点：`AppSettings.PinnedPaths` → `Bookmarks`；`MainWindowViewModel.FillQuickAccessItems`；`MainWindow.axaml` 书签栏与左侧导航。

3. **失效书签标记**
   - 启动/打开管理器时批量检查存在性（后台、限流）；失效项灰色 + 警告图标；提供“移除失效项”。
   - 接入点：`BookmarkManagerViewModel`；`File.Exists/Directory.Exists` 后台检查。

4. **书签搜索与溢出**
   - 书签栏支持横向滚动/“更多”下拉；管理器支持按名称/路径过滤。
   - 接入点：`MainWindow.axaml` 书签栏 `ItemsControl` 改为可滚动 + 更多菜单；管理器搜索框。

5. **区分“标签固定”与“路径书签”**
   - 标签固定：仅影响标签条排序/保留；路径书签：进入快捷访问。UI 文案与图标区分，日志也区分。
   - 接入点：`TogglePinTab`（:1295）、`PinPath`（:1325）；`MainWindow.axaml` 菜单文案。

### 可选（P1/P2）

- 导入浏览器书签 HTML/JSON、Total Commander 热列表。
- 书签绑定快捷键（Ctrl+1..9）。
- 书签备注/颜色/图标。
- 书签同步（WebDAV/S3），默认关闭。
- 智能书签：最近常用路径自动建议固定。

### 不建议

- 把书签存进注册表：继续 JSON，便于迁移。
- 自动删除失效书签：只标记，由用户确认。
- 用路径字符串作为唯一键：规范化后再比较，保留原始显示名。

## 4. 交互与视觉

- 书签栏：高度 32，图标 16，名称最多 120px；溢出显示“更多 ▾”。
- 左侧导航：分组标题 + 列表；右键“在新标签打开/复制路径/从快捷访问移除”。
- 管理器：左分组树、右书签表；底部“新增分组/导入/导出/移除失效项”。
- 失效项：灰色文字 + 橙色感叹号，tooltip“路径不存在”。
- 拖拽：书签可拖到分组；文件/文件夹拖到书签栏创建书签（已有）。
- 最近访问：下拉列表显示路径 + 时间，支持“固定到书签”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 数据模型 | `AppSettingsStore.cs` 新增 `Bookmarks/BookmarkGroups/RecentPaths` | 兼容旧 `PinnedPaths` |
| 迁移 | `MainWindowViewModel` 构造函数 | 首次加载迁移 |
| 管理器 | 新增 `Views/BookmarkManagerWindow.axaml(.cs)`、`ViewModels/BookmarkManagerViewModel.cs` | 复用文件存在性检查 |
| 书签栏 | `MainWindow.axaml:164-198` | 滚动 + 更多 |
| 导航 | `MainWindowViewModel.FillQuickAccessItems`（:3416） | 分组渲染 |
| 最近 | `RecordRecent`（:495） | 持久化 + 去重 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 数据迁移 | 旧 `PinnedPaths` 与新模型并存 | S |
| 管理器 | 拖拽排序/分组交互 | M |
| 失效检测 | 大量网络路径检查卡顿 | S/M |
| 书签栏溢出 | 现有布局宽度计算 | S |

## 7. 验收标准

1. 最近访问重启后保留，最多 20 条，重复路径置顶。
2. 书签可分组、拖拽排序、重命名；重启后顺序与分组保留。
3. 书签管理器支持搜索与失效标记，可批量移除失效项。
4. 书签栏溢出显示“更多”，不挤压标签条。
5. 标签固定与路径书签在 UI/日志中明确区分。
6. 旧 `PinnedPaths` 自动迁移，不丢失。
7. 拖文件/文件夹到书签栏可创建书签，重复路径不重复添加。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows 文件资源管理器](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows) | 快速访问/固定/最近 | 2026-11-03 |
| [Chrome 书签管理](https://support.google.com/chrome/answer/96816) | 书签管理器/分组/导入导出 | 2026-11-03 |
| [Directory Opus 手册](https://docs.dopus.com/) | 收藏夹/按钮栏 | 2026-11-03 |
| [QTTabBar 手册](https://qttabbar.wdfiles.com/local--files/documents/options_tab.html) | 书签栏交互 | 2026-11-03 |
| [VS Code 最近打开](https://code.visualstudio.com/docs/editing/getting-started) | 最近列表与固定 | 2026-11-03 |
