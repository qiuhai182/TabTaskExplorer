# 桌面端右键菜单不显示 · 缺陷修复规格

## 1. 缺陷现象

用户报告：「大量桌面端右键，均没触发菜单」——桌面上对文件行、文件夹行、空白处等大量位置按右键，右键菜单均不显示。

## 2. 已确认的证据（本轮排查已固化，不需重复实验获取）

1. **日志链路正常**（`C:\Users\QiuHai\AppData\Roaming\TabTaskExplorer\ui.log`）：
   - 右键按下/松开均被 UiLogger 记录（今日 92 次右键按下、78 次菜单 Opening、0 异常）。
   - `OnSystemMenuOpening` 两种分支都执行过：
     - 成功分支：`右键菜单打开：行路径=D:\.pnpm-store 条目类型=目录 系统菜单项注入数=37 …结果=成功`（2026-10-27 10:01:57.761）。
     - 失败分支：`右键菜单打开：行路径=未取到 条目类型=未知 结果=失败，菜单未关联列表条目，未添加系统菜单项`（大量出现）。
   - 代码中无任何 `e.Cancel=true` / `IsOpen=false` 逻辑（全仓已核实）。
2. **决定性窗口证据**：原生注入真右键（`SetCursorPos` + `mouse_event(0x0008/0x0010)`）后，进程**新建唯一顶层窗口**：
   - 类名 `Avalonia-{guid}`，`style=0x96000000`（WS_POPUP|WS_VISIBLE），`ex=0x80`（WS_EX_TOOLWINDOW），`vis=True`；
   - `GetWindowRect = 0,0,0,0`（零尺寸），持续 3 秒以上不变；
   - **UI Automation**：`Window → Group`，`BoundingRectangle = Empty`，无子控件——弹窗窗口已创建，但内容未物化/测量为 0。
3. **视觉证据**：右键后 400ms / 1200ms / 2500ms 三张全屏截图，均无任何菜单可见。
4. **左键对照**：左键点击行也会新增 2 个零尺寸 `Avalonia-{guid}` 弹窗窗口（疑为 ToolTip 弹窗）→ 疑似**所有弹窗类（ToolTip/ContextMenu）都是零尺寸**，即系统性弹窗测量/内容附加问题，而非单一菜单逻辑问题。
5. **代码事实**：
   - [FileListPanel.axaml](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/FileListPanel.axaml) L16-25 空白处菜单、L84-99 与 L137-152 行菜单均有完整静态菜单项（打开/复制/剪切/粘贴/…），**不存在空菜单导致 0×0 的可能**。
   - [App.axaml](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml) L122 仅有 `<styling:FluentAvaloniaTheme />`，未显式声明 `<FluentTheme/>`。
   - [TabTaskExplorer.App.csproj](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/TabTaskExplorer.App.csproj) L30-33：Avalonia 12.1.4 + FluentAvaloniaUI 3.1.0。
   - 未提交改动 3 个文件（Program.cs 唤起上下文日志、UiLogger.cs 滚轮日志、MainWindowViewModel.cs 窗口 Show 修复）与本缺陷无关，**修复时不得误动**。
6. **环境干扰（测试时必须规避）**：
   - Computer Use 的 `click button:"right"` 到达程序后变左键（工具缺陷，不用它做右键验证）。
   - 沙箱命令结束会回收本命令启动的子进程 → **启动 + 交互 + 采集必须在同一条 Shell 命令内完成**（`dangerouslyDisableSandbox` 无效，已实测）。
   - 用户可能正在本机实操，注入测试选择空闲时机，日志按时间戳归属自己的注入事件。

## 3. 根因判定（任务 t1，必须先做，判定树预先固定）

在 `FileListPanel.axaml.cs` 的 `OnSystemMenuOpening` / `OnBackgroundMenuOpening` 末尾加**临时**诊断日志（修复完成后随 t2 一并清理），每次 Opening 记录：
- `menu.Items.Count`、`menu.IsOpen`、`menu.Bounds` 尺寸、`menu` 所在逻辑树父链是否可达可视树。

再执行三个对照实验（同一条命令内：启动 → 注入 → 窗口枚举 + 截图 → 读日志）：

| 实验 | 操作 | 判定 |
|---|---|---|
| E1 | 悬停工具栏按钮 1.5 秒（ToolTip） | ToolTip 也 0×0 → **全局弹窗问题**；正常 → 局部 |
| E2 | 右键标签页 / 左侧导航项（MainWindow、导航栏的 ContextMenu） | 同样 0×0 → 全局；正常 → 仅文件列表行菜单 |
| E3 | 详情视图某一行右键（走成功分支） | 0×0 + `Items.Count>0` → 内容附加后被卸载；`Items.Count=0` → 菜单项未进弹窗 |

判定结论必须落到下述三个修复分支之一，并把判定结果写进 t2 的执行记录。

## 4. 修复方案（按 t1 判定二选一或组合，改动点已定死）

**分支 A · 全局弹窗问题（E1/E2 均异常）**——主题组合缺失控制主题：
- 改 [App.axaml](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml) L121-123 的 `Application.Styles`：在 `<styling:FluentAvaloniaTheme />` 之前补 `<FluentTheme />`（Avalonia.Themes.Fluent 已在 csproj 引用），保证 ContextMenu/MenuItem/ToolTip/ScrollBar 控件主题齐备。
- 若补后与 FluentAvaloniaTheme 冲突（控件样式重复/报资源键冲突），按两者并存的正确顺序调整，并以验证结果为准。

**分支 B · 仅文件列表行菜单异常（E1/E2 正常，E3 异常）**——菜单实例在虚拟化行模板内、打开期被卸载：
- 把行菜单从 DataTemplate 内的 `<Border.ContextMenu>`（[FileListPanel.axaml](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/FileListPanel.axaml) L84-99、L137-152）上移为 `ListHost`（或 RowList/TileList）**单实例 ContextMenu**；`OnSystemMenuOpening` 改为用 `e.Source`/`e.GetPosition` 命中行来取 `FileRow`（同时消除「行路径=未取到」失败分支，使该分支也能正常显示菜单）。
- 保留静态菜单项、`SystemMenuItemsHost` 占位与 `MenuPasteToFolder` 粘贴项的既有行为（与原计划的粘贴入口口径一致）。

若 t1 判定同时命中 A 与 B（全局异常 + 行菜单另有问题），按 A → 复验 → 仍异常再 B 顺序执行，避免一次改动两处导致无法归因。

## 5. 验证步骤（任务 t3，全部通过才算完成）

1. `'' | & d:\TabTaskExplorer\build.bat` 全量构建（编码自愈 + 发布）通过，无 U+FFFD 中止。
2. `dotnet test` ≥ 144 通过（基线 144，不得回退）。
3. 自动验证（同一条命令内完成）：启动程序 → 空闲时机注入右键（文件行成功分支 + 此电脑视图/空白处失败分支）→ 断言：
   - 新增弹窗窗口 `GetWindowRect` **宽高非 0**；
   - 截图中可见菜单（人工/模型读图确认）；
   - ui.log 对应时间戳有 `结果=成功` 且无异常栈。
4. ToolTip 悬停截图为可见（分支 A 的回归确认）。
5. 修复后清理 t1 的临时诊断日志；未提交的 Program.cs / UiLogger.cs / MainWindowViewModel.cs 三处他人改动保持原样。
6. 最终由用户手动复核：桌面端各类右键菜单可见、可点、粘贴项在文件夹行显示。

## 6. 需要的批准

- 批准本规格后按 tasks.md 顺序执行：t1 根因判定 → t2 修复 → t3 构建与验证 → t4 用户确认。
- 分支 A / B 的选择权在 t1 判定结果，不在执行前猜测；若 t1 结论与两分支均不符，停下来向用户汇报新证据并请求补充批准，不擅自扩大改动。
