# 表格列宽任意拖动 + 界面过宽时右侧留白

## Context（背景）

用户要求：**任何视图的表格标题栏都可以任意拖动列宽；界面太大、列数不够时，右侧留白，不要把列拉伸填满。**
经确认：列宽**不持久化**（仅当前会话生效，重启恢复默认），但**默认列宽必须合理**。

现状问题：
- 桌面端 4 个表格视图（文件列表、全盘搜索、磁盘占用、进程监控）表头与行模板各自写死 `ColumnDefinitions`，含 `*` 星号列——窗口越宽名称列被拉得越宽，右侧永远填满。
- 网页端 `table { width:100% }` 强制撑满内容区，名称列用 `46%` 百分比宽度。
- 两端均无任何列宽拖动能力。

难点：表头 Grid 与行模板 Grid 是**两个独立 Grid**（行在 ListBox 虚拟化 DataTemplate 里），拖表头必须让所有已生成的行同步变宽——Avalonia 自带 GridSplitter 只能在同一个 Grid 内分列，不适用，需自研。

## 核心设计

**两阶段机制：**

1. **默认状态（未拖动过）**：XAML 里保留星号/Auto 列，但给会拉伸的星号列加 `MaxWidth` 上限——窗口越宽星号列停在上限，多余空间自然留白（Grid 像素/触顶列不会分配剩余空间），窄窗口星号列照常收缩不裁剪。
2. **首次拖动后**：行为层用表头各列 Thumb 的实际位置快照出全像素列宽，之后该视图以纯像素列宽显示（Explorer 同款行为）；通过共享绑定同步到表头与所有行。

**同步机制**：新增附加属性 `ColumnResize.Widths`（string，如 `"30,26,480,140,130,110"`），回调里重建 `Grid.ColumnDefinitions`。表头绑定 `ViewModel.ColumnWidths`，行模板通过 `RelativeSource AncestorType=ListBox` 绑到同一属性——拖动时改 VM 属性，表头与全部已生成行同时更新；虚拟化新生成的行绑定时自然取到当前值。

**拖动交互**：行为层在表头 Grid 加载时为每一列右边缘生成一个 `Thumb`（宽 6px，光标 SizeWestEast，ZIndex 高于排序按钮）：
- DragDelta：从各 Thumb 实际边界位置快照当前像素宽 → 调整 `列i += Δ`、`列i+1 -= Δ`，列宽最小 40px；拖最后一列右边缘只增宽该列，且总宽不超过表头宽度（保证只吃留白区，不越界）。
- DragCompleted：调用 VM 记录一条纯中文日志（视图名、列数、最终各列像素宽）。
- VM 不实现接口拿不到 → 定义内核无关的 App 层接口（见文件清单）。

## 默认列宽（合理值 = 星号列上限）

| 视图 | 列定义（含上限） |
|---|---|
| 文件列表 | 30, 26, 名称 `3*` 上限 **560**, 140, 130, 110 |
| 全盘搜索 | Auto(图标21+8), 名称 `*` 上限 **560**, Auto(日期), Auto(大小 MinWidth 70) |
| 磁盘占用 | 名称 `2.6*` 上限 **440**, 100, 大小 `1.7*` 上限 **260**, 120, 80, 90 |
| 进程监控 | 70,170,130,64,54,58,72,96,110,132, 命令行 `2*` 上限 **320**, 窗口标题 `2*` 上限 **220** |

网页端用对应像素值作 colgroup 默认宽（文件 34,560,140,130,110；磁盘 440,100,260,120,80,90；进程 …,320,220；搜索 34,560,140,110；连接管理列按现状 px 化，实现时读 renderConnections 现有 30% 换算）。

## 改动文件

### 桌面端（Avalonia）

1. **新增** `src/TabTaskExplorer.App/Services/ColumnResize.cs`
   - 附加属性 `Widths`（string）：非空时回调 `grid.ColumnDefinitions = new ColumnDefinitions(value)`；空/null 保留 XAML 原始定义。
   - 附加属性 `Enabled`（bool，仅表头用）：Loaded 时按列数生成 Thumb（`Grid.Column=i`、`HorizontalAlignment=Right`、`Width=6`、`Margin="-3,0,0,0"`、ZIndex=10）。
   - Thumb 的 `DragDelta`/`DragCompleted` 处理器：边界快照、Δ 调整、40px 下限、末列不超表宽、调用 VM 接口。
   - 接口 `IColumnWidthViewModel { void ApplyColumnWidths(double[] widths); void LogColumnWidths(); }`（App 层，非内核）。

2. **4 个视图 VM**（[FileListPanelViewModel.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/ViewModels/FileListPanelViewModel.cs)、SearchPanelViewModel、DiskViewPanelViewModel、ProcessPanelViewModel）：
   - 新增 `ColumnWidths` 属性（默认 `string.Empty` = 用 XAML 默认列宽）+ 变更通知。
   - 实现 `ApplyColumnWidths`（拼接像素串并触发通知）与 `LogColumnWidths`（`UiLogger.Log("程序行为", "进程监控表头列宽拖动完成：列数=12 调整后列宽=70,170,… 像素")`，视图名各 VM 自带，纯中文无缩写）。

3. **4 个视图 XAML**（FileListPanel / SearchPanel / DiskViewPanel / ProcessPanel .axaml）：
   - 表头与行模板的 `ColumnDefinitions="…"` 改为元素形式，星号列加 `MaxWidth`（值见上表），**其余完全不变**。
   - 表头 Grid 追加 `svc:ColumnResize.Enabled="True"` 与 `svc:ColumnResize.Widths="{Binding ColumnWidths}"`。
   - 行模板 Grid 追加 `svc:ColumnResize.Widths="{Binding DataContext.ColumnWidths, RelativeSource={RelativeSource AncestorType=ListBox}}"`（与文件列表现有 `AncestorType=ListBox` 绑定写法一致）。
   - XAML 根部按需补 `xmlns:svc="using:TabTaskExplorer.App.Services"`。

4. **清单确认**：实现时 grep ConnectionPanel / RemoteTransferPanel / SettingsWindow 是否存在带表头的表格行，有则同样接入，无则跳过（探索初判：连接面板为左右分栏列表、无排序表头）。

### 网页端（WebPageHtml.cs 单文件）

5. CSS 第 99 行 `table { border-collapse:collapse; width:100%; }` → 去掉 `width:100%`；表格宽度由 colgroup 像素总和决定（总和 < 容器即右侧留白，> 容器由 `.content` 的 `overflow:auto` 出横向滚动）。
6. `renderTable`（文件/搜索，含 Details/Content/List 三种 mode 列组合）、`renderDiskTable`、`renderProcess`、`renderConnections`：输出 `<colgroup>`，宽度取 JS 状态 `tableColWidths[key]`，无记录则用上表默认值；同时给 `<table>` 设置 `style="width:总和px"` 与 `data-colkey`。
7. JS 拖动（风格对齐现有 `contentMain` 上的事件委托）：
   - 一次性委托 `contentMain` 的 `mousedown`：命中 `thead th` 且 `offsetX > 宽度-7` 进入拖动；记录起始各列宽。
   - `mousemove` 实时改 colgroup 各 col 宽 + table 总宽 + 写回状态（1.5 秒重绘后 render 函数从状态恢复，拖动结果在刷新中保持）。
   - `mouseup` 结束；随后 200ms 内的 `click` 用捕获阶段 `stopPropagation` 吞掉，避免拖完误触发排序（现有排序是 th 上的 inline onclick）。
   - 最小列宽 40px；末列拖动只增宽、以容器右缘为界。
8. 状态键：`file:Details` / `file:List` / `file:Content` / `search` / `disk` / `process` / `connections`（mode 不同列数不同，分开记忆，本会话内有效）。

## 约束遵守

- 分层：全部改动在 App 层（桌面）与 App 层单文件（网页），内核与平台层零改动。
- 双端一致：默认列宽两端同值、拖动行为两端同款（末列吃留白、40px 下限、拖完不误触排序）。
- 日志：拖动完成记一条纯中文完整日志（视图、列数、全部列宽），失败/提前返回分支（VM 类型不符、表头未加载）同样记录。
- 新增/含中文文件 UTF-8 BOM；沿用现有 if 链风格，少 lambda。
- 不持久化：不写设置存储；`ColumnWidths` 初始为空即默认。

## 验证

1. `& "d:\TabTaskExplorer\stop.bat"; & "d:\TabTaskExplorer\build.bat"` → 构建通过；`dotnet test` → 137 项全过。
2. **浏览器优先**（项目规则）http://127.0.0.1:15919/ ：
   - 放大窗口：文件/磁盘/进程表右侧留白、列不拉伸（截图确认）；
   - 拖表头列边界（Chrome DevTools Input 拖拽）：相邻两列此消彼长、最小 40px、拖最后一列吃留白区；
   - 拖动后等待 1.5 秒自动刷新：列宽保持；拖完点击表头：排序仍正常（未误触）；
   - 控制台无错误。
3. 桌面端：构建运行后人工/自动化拖一次文件列表与进程表头（自动化拖拽若不可靠则按既往惯例交用户手动确认），检查 ui.log 出现列宽完成日志。
