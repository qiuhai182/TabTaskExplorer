# 托盘菜单现代化设计

状态：设计稿（2026-11-03）
适用：`App.axaml.cs` 托盘图标与菜单、关闭到托盘行为
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 托盘图标常驻 | `App.axaml.cs:252-330` | 可用 | `TrayIcon` 挂到 Application |
| 托盘左键唤起 | `App.axaml.cs:377` | 可用 | 恢复主窗口 |
| 菜单项 | `App.axaml.cs:252-330` | 可用 | 显示窗口、区域截图、贴图截图、取色、剪贴板贴图、打开网页、复制网页地址、开机自启、退出 |
| 关闭到托盘 | `MainWindow.OnWindowClosing`（:130-154）、`AppSettings.CloseToTray` | 可用 | 最后一窗口隐藏 |
| 动态状态 | 半成品 | 半成品 | 仅同步开机自启文字；无录制/传输状态 |
| 最近文件夹 | 无 | 缺口 | 无子菜单 |
| 多窗口列表 | 无 | 缺口 | 只能唤起主窗口 |
| 录制状态 | 无 | 缺口 | 无红点/计时 |
| 任务中心入口 | 无 | 缺口 | 托盘无“任务数” |
| 快速设置 | 无 | 缺口 | 无主题/网页端开关 |
| 通知 | 无 | 缺口 | 无气泡/Toast |

主要问题：

1. **托盘菜单静态**：除开机自启外不反映程序状态；用户不知道是否在录制、是否有传输任务。
2. **只能唤起主窗口**：多窗口时无法选择具体窗口，也不能从托盘新建窗口。
3. **无最近文件夹**：常用路径需要打开主窗口再找。
4. **无任务中心入口**：长任务状态不可见。
5. **无快速设置**：主题、网页端、静默模式要进设置。
6. **无通知/气泡**：完成/失败无法从托盘提示。
7. **关闭到托盘缺少提示**：首次关闭时用户可能以为程序退出；无“下次不再提示”。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 通知区域 | 图标、上下文菜单、气泡通知（[Notification area](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area)） | 托盘菜单/气泡标准 |
| Everything | 托盘常驻、全局热键、选项菜单 | 快速唤起 |
| ShareX | 托盘菜单含截图/录屏/上传/历史/设置 | 动作丰富 |
| Snipaste | 托盘菜单含截图/贴图/设置/退出 | 轻量工具托盘 |
| VS Code | 托盘/后台运行/更新提示 | 状态与更新 |
| Windows 11 托盘 | 隐藏图标溢出区、右键菜单 | 适配 Win11 |

## 3. 推荐方案

### 必做（P0）

1. **状态化托盘**
   - 首行状态（灰色不可点）：“空闲 / 正在录制 00:12 / 2 个任务 / 3 条未读通知”。
   - 图标叠加红点（录制）/橙点（失败）。
   - 接入点：`App.axaml.cs` 托盘菜单构建/刷新；`CaptureCoordinator.RecordingStateChanged`、`ProgressCenter`、`NotificationCenter` 事件。

2. **菜单结构**
   - 打开主窗口 / 新建窗口 / 最近文件夹（10 条）/ 截图 / 录屏 / 贴图 / 取色 / 任务中心（N）/ 通知中心（N）/ 设置 / 退出。
   - 接入点：`App.axaml.cs`、`MainWindowViewModel.RecentPaths`、`OpenPathOnUiThread`。

3. **多窗口**
   - “窗口”子菜单列出全部窗口标题，点击激活；全部关闭。
   - 接入点：`WindowRegistry`、`App.axaml.cs`。

4. **关闭到托盘提示**
   - 首次关闭显示气泡“程序仍在托盘运行，点击图标恢复”；设置“不再提示”。
   - 接入点：`MainWindow.OnWindowClosing`、`AppSettings.TrayHintShown`。

5. **托盘通知**
   - 完成/失败气泡或系统通知；点击跳转；与通知模块共用 `INotificationService`。
   - 接入点：`App.axaml.cs`、`INotificationService`。

### 可选（P1/P2）

- 快速设置子菜单（主题、网页端开关、免打扰、开机自启）。
- 托盘图标动态图标集（空闲/录制/传输/失败）。
- 托盘菜单搜索（P2，不常见）。
- 中键点击托盘新建窗口。

### 不建议

- 托盘常驻多个图标：一个应用一个图标，状态用图标叠加/菜单表达。
- 托盘菜单塞入全部功能：保持 8–12 项，深层用子菜单。
- 关闭到托盘时静默无提示：首次必须提示。

## 4. 交互与视觉

- 托盘图标 16/32px，品牌造型；红点右下角。
- 菜单首行状态灰色不可点；分隔线分组。
- 最近文件夹子菜单：名称 + 路径 tooltip；点击打开/激活。
- 窗口子菜单：窗口标题 + 当前标签；最多 10 个。
- 气泡：标题 + 一行正文；失败不自动消失。
- 关闭提示：气泡/小窗，按钮“知道了/不再提示”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 托盘菜单 | `App.axaml.cs:252-330` | 动态构建/刷新 |
| 状态事件 | `CaptureCoordinator`、`ProgressCenter`、`NotificationCenter` | 订阅/退订 |
| 最近文件夹 | `MainWindowViewModel.RecentPaths`、`OpenPathOnUiThread` | 持久化 |
| 多窗口 | `WindowRegistry` | 标题/激活 |
| 提示 | `AppSettings.TrayHintShown`、`MainWindow` | 首次关闭 |
| 通知 | `INotificationService` | 托盘气泡降级 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 动态菜单 | 事件频繁刷新导致闪烁 | S/M |
| 多窗口 | 窗口生命周期 | S/M |
| 图标叠加 | 多尺寸/DPI | S |
| 通知 | AUMID/权限 | M |

## 7. 验收标准

1. 托盘菜单首行显示当前状态；录制中图标有红点。
2. 最近文件夹子菜单显示 10 条，点击可打开并去重。
3. 多窗口时“窗口”子菜单可激活任一窗口。
4. 任务中心/通知中心显示数量并可打开。
5. 首次关闭到托盘有提示，可“不再提示”。
6. 完成/失败气泡可点击跳转；失败不自动消失。
7. Windows 11 托盘溢出区图标显示正常。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows Notification Area](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area) | 托盘图标/菜单/气泡 | 2026-11-03 |
| [Windows 11 任务栏](https://support.microsoft.com/en-ES/Windows/Experience/Personalization/customize-the-taskbar-in-windows) | 托盘/溢出区 | 2026-11-03 |
| [Everything](https://www.voidtools.com/) | 托盘常驻/热键 | 2026-11-03 |
| [ShareX](https://getsharex.com/) | 托盘动作菜单 | 2026-11-03 |
| [Snipaste](https://www.snipaste.com/) | 轻量托盘菜单 | 2026-11-03 |
