# 通知现代化设计

状态：设计稿（2026-11-03）
适用：托盘/任务栏、截图/录屏/传输/任务中心
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 托盘菜单 | `App.axaml.cs:252-330` | 可用 | 显示窗口、截图、贴图、取色、网页、开机自启、退出 |
| 托盘左键唤起 | `App.axaml.cs:377` | 可用 | 恢复主窗口 |
| 状态栏文字 | `MainWindowViewModel` 状态栏 | 可用 | 截图/传输/网页端状态 |
| 系统通知（Toast） | 无 | 缺口 | 无 `INotificationService`，无 Toast/气泡 |
| 通知中心/历史 | 无 | 缺口 | 关闭后无法回看 |
| 通知分类/免打扰 | 无 | 缺口 | 无设置 |
| 点击跳转 | 无 | 缺口 | 无“打开文件夹/打开任务”动作 |
| 失败持久提示 | 无 | 缺口 | 失败只在面板/状态栏显示 |

主要问题：

1. **完成/失败无系统通知**：截图保存、录屏完成、传输完成/失败、批量操作失败，用户切到其他程序后无感知。
2. **无通知历史**：即使有状态栏文字，切换标签/重启后消失。
3. **无分类与免打扰**：未来若加通知，容易变成噪音；缺少“仅失败/全部/关闭”设置。
4. **无点击动作**：通知不能直接打开产物文件夹、任务详情、设置页。
5. **托盘菜单承担了通知职责**：用户需要主动打开托盘才能看到状态。
6. **跨平台差异未抽象**：Windows Toast、macOS Notification Center、Linux libnotify 需要统一接口。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows App SDK App Notifications | Toast、操作按钮、通知中心、AUMID/包标识（[App notifications](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/)） | Windows 首选 Toast；未打包需 AUMID+快捷方式 |
| Windows 11 通知设置 | 按应用开关、免打扰、优先级 | 分类与免打扰 |
| macOS UserNotifications | 通知中心、操作、权限 | 跨平台抽象 |
| Linux libnotify | 桌面通知、动作 | 跨平台抽象 |
| ShareX | 完成/上传/失败通知，可配置 | 截图/传输通知粒度 |
| VS Code | 通知中心 + 状态栏 + Toast 分级 | 应用内通知中心 |

## 3. 推荐方案

### 必做（P0）

1. **统一通知服务**
   - `INotificationService`：`Notify(title, body, level, category, actions, silent)`；平台实现 Windows Toast / macOS UserNotifications / Linux libnotify；托盘气泡作为降级。
   - 分类：Capture、Recording、Transfer、FileOperation、Update、Security。
   - 级别：Info/Success/Warning/Error；Error 默认持久（不自动消失）。
   - 接入点：新增 `Kernel/Abstractions/INotificationService.cs`、`Platform.Windows/WindowsNotificationService.cs` 等；`CompositionRoot` 注册。

2. **关键事件接入**
   - 截图保存/复制完成、录屏完成/失败、传输完成/失败、批量复制/删除失败、连接断开、主机密钥变更。
   - 接入点：`CaptureCoordinator.CaptureFinished`、`RemoteTransferPanelViewModel`、`IFileOperationService`、`RemoteConnectionPool`。

3. **点击动作**
   - 通知携带 action：`open-folder`、`open-task`、`open-settings`、`retry`；点击后唤起主窗口并执行。
   - 接入点：`App.axaml.cs` 激活处理；`MainWindowViewModel.OpenPathOnUiThread`（:2558）。

4. **通知中心（应用内）**
   - 主窗口右侧/底部弹出面板，保留最近 100 条；支持筛选分类、清除、重试。
   - 接入点：新增 `Services/NotificationCenter.cs`、`Views/NotificationCenterWindow.axaml`；状态栏铃铛图标 + 未读数。

5. **设置与免打扰**
   - 设置页“通知”：总开关、分类开关、仅失败、免打扰时段、声音。
   - 接入点：`AppSettings`、`SettingsWindow` 新增“通知”分页。

### 可选（P1/P2）

- 通知分组（同一任务多次进度合并）。
- 进度通知（传输/录屏任务栏 + 通知内进度）。
- 通知历史持久化（JSON，重启保留）。
- 勿扰模式跟随系统专注助手。
- 邮件/Webhook 通知（企业场景，P2）。

### 不建议

- 每个鼠标操作都通知：噪音。
- 默认开启所有分类：首次启动只开“完成/失败”。
- 在通知中显示完整路径/密码/主机名：注意隐私，敏感信息可省略。
- 依赖未注册 AUMID 的 Toast：未打包/未安装时会静默失败，必须有托盘气泡降级。

## 4. 交互与视觉

- Toast：标题 + 一行正文 + 图标；Success 绿色、Warning 橙色、Error 红色。
- 操作按钮最多 2 个（打开文件夹/重试）。
- 应用内通知中心：卡片列表，未读圆点，分类 chip，底部“全部已读/清空”。
- 状态栏铃铛：有未读时显示数字徽标。
- 免打扰：设置时段内只保留 Error，其他静默进中心。
- 录屏/传输：同一任务只保留最新一条，避免刷屏。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 通知抽象 | 新增 `Kernel/Abstractions/INotificationService.cs` | 分类/级别/动作 |
| Windows Toast | 新增 `Platform.Windows/WindowsNotificationService.cs` | AUMID/快捷方式；失败降级托盘 |
| macOS/Linux | 新增 `MacNotificationService`、`LinuxNotificationService` | UserNotifications/libnotify |
| 事件接入 | `CaptureCoordinator`、`RemoteTransferPanelViewModel`、`IFileOperationService` | 统一调用 |
| 通知中心 | 新增 `NotificationCenter.cs`、`NotificationCenterWindow.axaml` | 最近 100 条 |
| 设置 | `AppSettings`、`SettingsWindow` | 分类/免打扰 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| Windows Toast | 未打包注册、AUMID、权限 | M |
| 跨平台 | 三套 API 差异 | M |
| 通知中心 | 与托盘/状态栏重复 | S/M |
| 免打扰 | 时间窗口与系统专注模式 | S |
| 点击动作 | 窗口未启动/最小化时激活 | S/M |

## 7. 验收标准

1. 截图/录屏完成弹出通知，点击可打开产物文件夹。
2. 传输/批量操作失败弹出 Error 通知，不自动消失，可重试。
3. 通知中心保留最近 100 条，支持分类筛选/清除/已读。
4. 设置可关闭某分类；免打扰时段只保留 Error。
5. Windows 未安装（无 AUMID）时 Toast 失败自动降级为托盘气泡，不静默丢失。
6. 同一传输任务的多次进度合并为一条，避免刷屏。
7. 通知内容不包含密码/完整凭据；路径可按设置隐藏。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows App SDK App Notifications](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/) | Toast/操作/通知中心 | 2026-11-03 |
| Windows 11 通知设置（系统内置） | 按应用开关/免打扰 | 2026-11-03（官方内置功能，无单独文档链接） |
| [Apple UserNotifications](https://developer.apple.com/documentation/usernotifications) | macOS 通知 | 2026-11-03 |
| [libnotify](https://gitlab.gnome.org/GNOME/libnotify) | Linux 桌面通知 | 2026-11-03 |
| [ShareX 通知](https://getsharex.com/docs/) | 截图/上传/失败通知 | 2026-11-03 |
