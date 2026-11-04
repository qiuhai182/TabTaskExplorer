# 文件/文件夹视图体验升级（P0-2）进度

状态：待实测
卡点：曾构建过一次，但后续又有改动；缩略图滚动、空格预览、列配置持久化与键盘选择均未做桌面集中验证。
下一步：用最新构建做一次约 10 分钟的桌面集中验证，先浏览器冒烟再桌面。

日期：2026-11-04  
范围：桌面端文件列表视图。网页端（`Assets/web/*`）未改动。

## 0. 构建、发布与验证纪律

- 发布：`stop.bat` 后执行 `build.bat` 的等价发布命令，`dotnet publish` 退出码 0，产物 `output\TabTaskExplorer.exe` 更新时间 2026-11-04 09:03，可在 output 直接运行。
- 说明：`build.bat` 末尾有 `pause`，非交互环境无法直接跑完；首次直接调用因 `Directory.Build.props` 的源码编码归一化目标遇到文件占用返回 exit 1，重试后 exit 0。当前 PowerShell 禁用了 `cmd /c`，故使用与 `build.bat` 相同的 publish 命令。
- 浏览器验证：已启动发布产物，使用独立浏览器上下文验证 `/api/version`、`/api/session`、`/api/state`、`/api/open`、静态资源与控制台；验证后关闭页面并执行 `stop.bat` 退出进程。
- 桌面专属项（缩略图滚动性能、空格预览、字体/视频首帧）未在本轮执行：按纪律需约定一次集中占用桌面的时间，避免抢焦点和操作主窗口。
- 用户影响：浏览器验证期间应用窗口短暂启动，未点击或键入主窗口；验证结束已退出进程、关闭测试页。

## 1. 缩略图（异步 + 磁盘缓存）— 代码已落地，待桌面验证

| 文件 | 改动 |
| --- | --- |
| `Services/ThumbnailService.cs` | 新增。缓存键=路径+文件大小+修改时间+档位；档位 96/192/384；磁盘缓存 `%LOCALAPPDATA%\TabTaskExplorer\thumbnails`；内存命中 + 失败短期缓存；并发生成上限 3；启动清理 30 天未访问或超过 512MB 的缓存。 |
| `Views/ThumbnailView.cs` | 新增控件。目录/远程/未命中时显示类型图标；命中或异步生成后显示真实缩略图；销毁时取消任务并释放位图。 |
| `Views/FileListPanel.axaml` | 详细信息 20×20、图标/平铺按 `TileIconSize` 使用 `ThumbnailView`；只替换图标控件，未改右键菜单。 |
| `ViewModels/MainWindowViewModel.cs` | 两个构造函数调用 `ThumbnailService.Configure(Settings.FfmpegPath)`。 |
| `TabTaskExplorer.App.csproj` | 显式声明 `SkiaSharp 3.119.4`（字体预览用）。 |

类型支持：图片（png/jpg/jpeg/bmp/gif/webp/tif/tiff）、视频首帧（mp4/mkv/avi/mov/wmv/flv/webm/m4v/ts/mpg/mpeg，FFmpeg 10 秒超时）、字体（ttf/otf/ttc，Skia 渲染“Aa”与字体名）。  
PDF：无跨平台渲染器，未实现，回退类型图标。

## 2. 空格预览（Quick Look）— 代码已落地，待桌面验证

| 文件 | 改动 |
| --- | --- |
| `Views/QuickLookWindow.cs` | 新增。图片、文本（前 512KB，UTF-8/GB18030 回退）、音频/视频（LibVLC + `MediaSurfaceHost`）预览；PDF 与未知类型显示明确提示。 |
| `Views/FileListPanel.axaml.cs` | 新增键盘入口：空格打开/关闭，Esc 关闭；输入框内不拦截。未触碰右键菜单函数。 |

窗口属性：`ShowInTaskbar=false`、`ShowActivated=false`、`CenterOwner`，不抢焦点、不进任务栏。  
PDF 首屏：未实现（无跨平台渲染器）。

## 3. 列配置与持久化 — 代码已落地，待桌面验证

| 文件 | 改动 |
| --- | --- |
| `Services/AppSettingsStore.cs` | 新增 `FileVisibleColumns`、`FileColumnWidths`、`FileCompactMode`。 |
| `ViewModels/FileTabViewModel.cs` | 新增列可见性属性、`SetColumnVisible`、紧凑模式与行高、列宽规范化与持久化；`AttributesText/AttributesOrder`；属性列排序。 |
| `Views/FileListPanel.axaml` | 表头与行新增“属性”列；各列 `IsVisible` 绑定；行高/内边距绑定；摘要栏新增“紧凑模式”与“列设置”菜单。 |
| `Views/FileListPanel.axaml.cs` | 新增列设置点击处理与属性列排序箭头；未触碰右键菜单函数。 |
| `ViewModels/FileTabViewModel.cs` 构造调用点 | `MainWindowViewModel`、`WebWorkspace` 传入 `AppSettingsStore`，列配置/列宽即时保存。 |

行为：隐藏列宽度置 0，显示时恢复默认宽度；列宽拖动结果写回设置；勾选框列与图标列始终显示。

## 4. 统一文件操作与进度中心 — 未开始

现状：远程传输已有队列、取消、断点重试（`RemoteTransferServiceBase` / `RemoteTransferPanelViewModel`），本地复制/移动/删除/压缩/解压仍走各自的同步或消息框路径，没有统一任务中心与冲突策略。  
结论：该子系统跨内核、远程、本地与 UI 多层，需要单独一轮设计与实现，本轮未做半成品接入，避免与现有右键菜单/拖拽改动冲突。

## 5. 资源管理器式键盘与选择 — 代码已落地，待桌面验证

| 文件 | 改动 |
| --- | --- |
| `Views/FileListPanel.axaml.cs` | 新增鼠标选择语义（普通单击单选、Ctrl 切换、Shift 连选）；方向键/Home/End 移动焦点并滚动到可见；F2 重命名、Delete 删除、Ctrl+C/X/V、Ctrl+A；空格与 Esc 接管快速预览。 |
| `ViewModels/FileTabViewModel.cs` | 新增 `RefreshSelectionSummary`，选择变化后刷新摘要栏。 |

未触碰 `OnListContextRequested`、`OpenFallbackContextMenu` 等右键菜单函数，也未改 `Services/FileDropPolicy.cs`。

## 6. 待验证清单（桌面集中验证）

1. 缩略图：大图目录滚动是否流畅；图片/视频/字体首次生成与二次缓存命中；缓存目录文件生成；未命中前图标占位。
2. 空格预览：图片、文本、PDF 提示、音频、视频；Esc/空格关闭；窗口不进任务栏、不抢焦点。
3. 列配置：5 列显示/隐藏、属性列内容、列宽拖动后重启恢复、紧凑模式行高与持久化。
4. 键盘选择：单击/Ctrl/Shift、方向键、Home/End、F2、Delete、Ctrl+C/X/V、Ctrl+A。
5. 操作自检：本次未改复制/移动/删除/压缩实现，仅改选择与键盘入口，需回归确认原有右键与拖拽不受影响。

## 7. 浏览器侧验证结果（发布产物）

| 检查 | 结果 |
| --- | --- |
| `/api/version` | 200，`staticAssets=true`，版本 `p0-1-web-modules-68ed9a74ef` |
| 静态资源 | CSS 与 6 个 JS 全部 200，控制台无 error/warn |
| `/api/session` | 200，返回会话令牌与 CSRF 令牌 |
| `/api/state` | 200，初始“此电脑”标签 2 行 |
| `/api/open` | 200，打开 `D:\TabTaskExplorer\output` 后 302 行，文件列表正常 |
| 事件连接 | 状态栏“已连接” |
| 缩略图 / 列设置 | Web 端均不存在（Web 使用独立模板，未实现桌面端缩略图、列配置、快速预览） |

结论：Web 端基线未回归；P0-2 新增能力为桌面端专属，浏览器无法验证其显示与交互。

## 8. 列设置勾选态修复

- 原因：`MenuFlyout` 内容不继承按钮的 `DataContext`，`IsChecked="{Binding ...}"` 取不到视图模型。
- 修复：菜单项改为命名控件，移除绑定；按钮 `Click` 与面板加载时调用 `RefreshColumnMenuChecks` 按当前配置写入勾选态；点击菜单项后再显式写回 `IsChecked`。

## 9. 统一任务中心下一轮计划（仅本地复制/移动/删除 + 冲突策略 + 取消）

1. 内核新增 `FileOperationCenter` 模型：任务编号、类型、源/目标、进度、状态、错误、取消令牌；远程传输复用同一任务模型（现有 `RemoteTransferJob` 映射为同一视图数据）。
2. 本地复制/移动/删除先接入：后台分块复制、可取消、失败记录与重试；冲突策略枚举 `跳过/覆盖/重命名/询问`，默认“询问”，在 UI 中一次性选择并应用于本批。
3. 进度中心 UI：复用传输面板样式，显示排队/进行中/完成/失败、总进度、单项进度、取消与重试按钮、完成汇总；远程与本地同一列表。
4. 不一次性重构压缩/解压/扫描；先让复制/移动/删除走通，再按同一接口接入。
5. 测试：浏览器验证 `/api/*` 状态与操作后刷新同步；桌面集中验证进度观感与取消/冲突交互。

## 10. 回退方式

1. 删除 `ThumbnailService.cs`、`ThumbnailView.cs`、`QuickLookWindow.cs`。
2. `FileListPanel.axaml` 恢复 `ThumbnailView` 为 `FileTypeIcon`，删除属性列、列设置与紧凑模式 UI。
3. `FileListPanel.axaml.cs` 删除键盘选择与列设置处理方法（保留右键菜单原有代码）。
4. `FileTabViewModel` 删除列配置/属性/选择摘要新增成员，恢复构造函数参数。
5. `AppSettingsStore` 删除三个新字段；`MainWindowViewModel`/`WebWorkspace` 恢复构造调用。
6. csproj 删除 SkiaSharp 显式引用。
