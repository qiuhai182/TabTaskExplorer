# 任务栏集成现代化设计

状态：设计稿（2026-11-03）
适用：`Program.cs`、`WindowHostStyler`、`WindowRegistry`、`MainWindow`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 进程 AUMID | `Program.cs:17-23`、`WindowHostStyler.AppUserModelId`（:15）、`WindowsWindowHostService` | 可用 | 启动前设置，任务栏归入同一应用分组 |
| 辅助窗口排除 | `WindowHostStyler.MakeAuxiliary`（:36-81）、`WindowRegistry` | 可用 | `ShowInTaskbar=false` + `WS_EX_TOOLWINDOW`，不进 Alt+Tab |
| 主窗口唯一入口 | `App.axaml.cs:50`、`WindowRegistry` | 可用 | 多窗口时主窗口保持任务栏按钮 |
| 多窗口 | `WindowRegistry` | 可用 | 窗口列表/主窗口提升 |
| Jump List | 无 | 缺口 | 任务栏右键无“最近文件夹/常用任务” |
| 缩略图工具栏 | 无 | 缺口 | 悬停缩略图无截图/任务中心按钮 |
| 任务栏进度 | 无 | 缺口 | 传输/复制/扫描无进度 |
| 覆盖图标 | 无 | 缺口 | 录制中无红点、失败无标记 |
| 安装器 AUMID 一致性 | 无 | 缺口 | 快捷方式/安装器未落地 |

主要问题：

1. **只用了“分组”能力**：没有 Jump List、缩略图工具栏、进度、覆盖图标，任务栏体验与成熟桌面应用差距明显。
2. **长任务不可见**：用户切到其他程序后不知道传输/复制/扫描进度。
3. **录制状态不可见**：任务栏没有红点/覆盖图标，只有托盘和面板。
4. **AUMID 与安装器未统一**：固定到任务栏/通知可能分组异常。
5. **多窗口无任务栏区分**：所有窗口同一 AUMID，任务栏只显示一个分组；缩略图预览无法区分内容。
6. **无 Jump List 最近项**：最近文件夹/截图目录需要打开程序才能看到。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| AppUserModelID | 进程/窗口/快捷方式一致，任务栏分组/固定（[Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/appids)） | 安装器必须写同一 AUMID |
| ITaskbarList3 | 进度、缩略图工具栏、覆盖图标、Jump List（[Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-itaskbarlist3)） | 任务栏现代化标准 API |
| Windows 任务栏设置 | 合并按钮/标签显示（[Microsoft 支持](https://support.microsoft.com/en-ES/Windows/Experience/Personalization/customize-the-taskbar-in-windows)） | 多窗口行为受系统设置影响 |
| Alt+Tab / 多任务策略 | Edge 标签可出现在 Alt+Tab（受策略控制）（[Multitasking CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-multitasking)） | 辅助窗口继续排除 |
| VS Code | 任务栏进度/通知/最近工作区 | 任务栏状态化 |
| Everything | 托盘常驻 + 热键 | 快速唤起 |

## 3. 推荐方案

### 必做（P0）

1. **Jump List**
   - Tasks：新建窗口、截图、录屏、任务中心、设置；Recent：最近 10 个文件夹、最近截图目录。
   - 接入点：新增 `Platform.Windows/WindowsTaskbarIntegration.cs`；`MainWindowViewModel.RecentPaths`；`App.axaml.cs` 启动注册。
   - 注意：Jump List 需要 AUMID 与快捷方式一致；未安装时可能不显示。

2. **缩略图工具栏**
   - 主窗口缩略图下方按钮：截图、贴图截图、任务中心、切换 IDE。
   - 接入点：`WindowsTaskbarIntegration`、`MainWindow` 句柄；回调到 `CaptureCoordinator`/`MainWindowViewModel`。

3. **任务栏进度**
   - 复用 `IProgressCenter` 总进度（传输/复制/扫描/压缩）；不确定总量用 Indeterminate。
   - 接入点：`WindowsTaskbarIntegration`、`ProgressCenter` 事件；非 Windows 空实现。

4. **覆盖图标**
   - 录制中红色圆点；有失败任务橙色感叹号；空闲清除。
   - 接入点：`WindowsTaskbarIntegration`、`CaptureCoordinator.RecordingStateChanged`。

5. **AUMID 一致性**
   - 安装器/快捷方式写入与 `WindowHostStyler.AppUserModelId` 相同值；未安装运行时降级不显示 Jump List。
   - 接入点：安装器清单、`WindowHostStyler`；系统集成模块。

### 可选（P1/P2）

- 多窗口缩略图标题/描述（`ITaskbarList3.SetThumbnailTooltip`）。
- 任务栏徽章（Win32 用覆盖图标替代）。
- 固定到任务栏引导。
- 任务栏分组内窗口预览（DWM 缩略图）。

### 不建议

- 第三方任务栏工具栏/Band：Windows 11 不支持，且与 Explorer 耦合。
- 修改用户任务栏合并设置。
- 为每个辅助窗口创建任务栏按钮：会污染 Alt+Tab/任务栏。

## 4. 交互与视觉

- Jump List：任务分组在上、最近文件夹在下；图标与主程序一致。
- 缩略图工具栏：16×16 单色图标，悬停 tooltip。
- 进度：绿色/黄色（暂停）/红色（错误）；完成后清除。
- 覆盖图标：红点（录制）、橙色感叹号（失败）；直径 16px。
- 缩略图预览：主窗口标题 + 当前标签名。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 任务栏 API | 新增 `Platform.Windows/WindowsTaskbarIntegration.cs`；`Kernel/Abstractions/ITaskbarIntegration.cs` | Jump List/Thumbnail/Progress/Overlay |
| 进度 | `Services/ProgressCenter.cs`、`WindowsTaskbarIntegration` | 总进度 |
| 覆盖图标 | `CaptureCoordinator`、`WindowsTaskbarIntegration` | 图标资源 |
| AUMID | `WindowHostStyler.cs`、安装器 | 一致性 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| Jump List | AUMID/快捷方式；未安装不显示 | M |
| 缩略图工具栏 | Win32 消息回调/句柄 | M |
| 覆盖图标 | 图标切换/DPI | S |
| 进度 | 多任务加权 | S |

## 7. 验收标准

1. 固定到任务栏后 Jump List 显示任务与最近文件夹。
2. 缩略图工具栏按钮可触发截图/任务中心/IDE。
3. 传输/复制运行时任务栏显示进度，结束后清除。
4. 录制中显示红点，停止后恢复。
5. 有失败任务时显示橙色感叹号，处理完后清除。
6. 辅助窗口不进入 Alt+Tab；主窗口只有一个任务栏按钮。
7. 安装后 Jump List/固定分组正常（AUMID 一致）。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [AppUserModelIDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids) | 分组/固定/最近列表 | 2026-11-03 |
| [ITaskbarList3](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-itaskbarlist3) | 进度/缩略图工具栏/覆盖图标 | 2026-11-03 |
| [自定义任务栏](https://support.microsoft.com/en-ES/Windows/Experience/Personalization/customize-the-taskbar-in-windows) | 合并按钮 | 2026-11-03 |
| [Multitasking CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-multitasking) | Alt+Tab 行为 | 2026-11-03 |
| [VS Code](https://code.visualstudio.com/) | 任务栏进度/最近 | 2026-11-03 |
