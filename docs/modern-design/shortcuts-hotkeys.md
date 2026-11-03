# 快捷键 / 全局热键现代化设计

状态：设计稿（2026-11-03）
适用：`HotkeySetting`、`ShortcutActionCatalog`、`MainWindow` 快捷键分派、`WindowsGlobalHotkeyService`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 应用内快捷键 | `MainWindow.OnWindowKeyDown`（:382-409）、`FindLocalShortcut`（:415） | 可用 | 按 `HotkeySetting` 精确匹配，输入框内屏蔽危险动作 |
| 统一快捷键中心 | `SettingsWindow` 第 1 分页、`AppSettings.Shortcuts` | 可用 | 同一动作可多条绑定，每条独立“应用内/全局” |
| 全局快捷键（Windows） | `WindowsGlobalHotkeyService.cs:11-12`、`SetWindowsHookExW`（:246） | 可用 | 低级键盘钩子 `WH_KEYBOARD_LL`，可覆盖其他程序注册的组合 |
| 动作目录 | `ShortcutActionCatalog` | 可用 | 截图/录屏/标签/文件/IDE/主题等动作名与显示名 |
| 迁移旧配置 | `AppSettings.Hotkeys/GlobalHotkeys` → `Shortcuts` | 可用 | `ShortcutsMigrated` 标记 |
| Linux / macOS 全局热键 | 无 | 缺口 | 仅应用内；README 已说明 |
| 冲突检测 | 无 | 缺口 | 不知道组合是否被其他程序占用 |
| 按键录制 | 部分 | 半成品 | 设置页用手动勾选 Ctrl/Alt/Shift/Win + 主键，非“按下即录制” |
| 冲突提示/自动回退 | 无 | 缺口 | 注册失败只写日志，UI 不显式提示 |
| 每应用配置 | 无 | 缺口 | 全局一套配置 |

主要问题：

1. **低级钩子默认全局拦截**：`WH_KEYBOARD_LL` 会看到所有键盘输入，容易被安全软件误报；且可能影响其他程序（即使只匹配自己的组合，钩子本身仍在链上）。
2. **无冲突检测**：用户设置 `Ctrl+Alt+S` 时不知道已被截图工具/输入法/IDE 占用；触发时可能两个程序同时响应。
3. **系统保留组合无校验**：F12、Ctrl+Alt+Del、Win+L 等无法注册，但 UI 允许保存。
4. **无“测试”按钮**：设置后只能切到别的程序试；失败无反馈。
5. **设置方式落后**：勾选修饰键 + 文本主键，容易输入非法键名；缺少“按下组合自动填入”。
6. **跨平台不一致**：Linux/macOS 全局作用域实际退化为应用内，但 UI 仍显示“全局”，容易误解。
7. **无导入/导出/重置单项**：换机迁移靠手抄。
8. **钩子生命周期**：`ReloadGlobalHotkeys`（`CaptureCoordinator.cs:187`）在设置变化时重建；若重建失败，旧钩子状态可能不一致。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows RegisterHotKey | 系统级注册，失败返回 `false`；F12 保留；同 hWnd/id 可覆盖（[Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)） | 应优先用 RegisterHotKey 做“冲突探测” |
| WH_KEYBOARD_LL | 全局低级钩子，可拦截所有键盘；需 `CallNextHookEx` 放行（[Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/ms997649(v=msdn.10))） | 仅作为“强制覆盖”可选模式，不作为默认 |
| AutoHotkey | 热键冲突检测、`#IfWinActive` 按应用生效、组合键/和弦 | 每应用配置与和弦可借鉴 |
| PowerToys Keyboard Manager | 可视化重映射、冲突提示、按应用 | 按键录制 UI 与冲突提示 |
| VS Code | `keybindings.json` 可导入导出、`when` 上下文 | 作用域上下文（焦点在编辑器/列表） |
| Snipaste | 全局热键可自定义，冲突时提示 | 截图工具的热键体验基准 |

## 3. 推荐方案

### 必做（P0）

1. **冲突检测**
   - 保存全局快捷键前，用 `RegisterHotKey` 探测：成功→可用；失败→`GetLastError` 区分“已被占用/系统保留”；探测后立即 `UnregisterHotKey`。
   - UI 显示“可用 / 被其他程序占用 / 系统保留”，并建议替代组合。
   - 接入点：`WindowsGlobalHotkeyService` 新增 `Probe(string combo)`；`SettingsWindow` 快捷键行增加状态列。
   - 注意：探测与真正注册之间存在竞态，最终以注册结果为准。

2. **注册策略：RegisterHotKey 优先，低级钩子可选**
   - 默认 `RegisterHotKey`；设置项“强制覆盖其他程序”开启时才用 `WH_KEYBOARD_LL`。
   - 钩子模式在设置页明确风险说明（安全软件可能拦截、可能影响其他应用）。
   - 接入点：`IGlobalHotkeyService` 增加 `Mode`；`WindowsGlobalHotkeyService` 双实现；`AppSettings.GlobalHotkeyMode`。

3. **按键录制 UI**
   - 点击“录制”后捕获下一次组合（支持单键、Ctrl/Alt/Shift/Win、媒体键），Esc 取消；非法组合（仅修饰键、系统保留）即时提示。
   - 接入点：`SettingsWindow` 快捷键表格；新增 `HotkeyCaptureBox` 控件。

4. **状态可见**
   - 设置页每行显示“已注册/未注册/冲突/仅应用内”；主窗口状态栏或托盘提示“全局快捷键 2 个冲突”。
   - 接入点：`CaptureCoordinator.HotkeyStatusText`（:96）、`SettingsWindow`。

5. **跨平台语义修正**
   - Linux/macOS：全局作用域显示为“应用内（当前平台不支持全局）”，或隐藏全局勾选；接入 X11 `XGrabKey` / macOS `CGEventTap` 作为 P1。
   - 接入点：`IGlobalHotkeyService.IsSupported`、`SettingsWindow`。

6. **导入/导出/重置单项**
   - 快捷键配置导出 JSON；支持恢复默认（已有“恢复默认快捷键”按钮，补充单项重置）。
   - 接入点：`SettingsWindow`、`AppSettingsStore`。

### 可选（P1/P2）

- 按应用生效（`#IfWinActive` 思路）：前台进程为某应用时启用一组快捷键。
- 和弦快捷键（Ctrl+K Ctrl+C）。
- 快捷键冲突扫描：列出所有已占用组合（需要枚举系统热键，能力有限）。
- 媒体键/鼠标侧键支持。
- 命令面板（Ctrl+Shift+P）统一执行动作，减少热键数量。

### 不建议

- 默认启用低级键盘钩子：安全软件误报、隐私争议、性能影响。
- 拦截并吞掉所有按键：只处理匹配组合，其余必须 `CallNextHookEx` 放行。
- 在 UI 线程同步等待钩子回调：回调必须快速返回。

## 4. 交互与视觉

- 快捷键表：动作名、当前组合（键帽样式）、作用域、状态、录制/清除按钮。
- 冲突行：橙色背景 + “被 XX 占用”提示 + “换一个”按钮。
- 录制中：输入框显示“按下组合…”，Esc 取消；已按下的修饰键实时高亮。
- 全局模式说明：首次开启“强制覆盖”弹确认，说明风险。
- 状态栏：仅在有冲突时显示警告图标，避免常驻噪音。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 冲突探测 | `Platform.Windows/WindowsGlobalHotkeyService.cs`、`IGlobalHotkeyService` | Probe 后立即注销 |
| 双模式 | `IGlobalHotkeyService`、`AppSettings`、`SettingsWindow` | RegisterHotKey / 低级钩子 |
| 录制控件 | 新增 `Views/HotkeyCaptureBox.cs`；`SettingsWindow.axaml` | 捕获 KeyDown，阻止默认输入 |
| 状态 | `CaptureCoordinator.cs`、`MainWindow` 状态栏 | 注册结果事件 |
| 跨平台 | `Platform.Linux/LinuxGlobalHotkeyService.cs`、`Platform.MacOS/MacGlobalHotkeyService.cs`（P1） | X11/CGEventTap |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 冲突探测 | 探测与注册竞态；系统保留组合识别 | S/M |
| 低级钩子 | 安全软件、UAC 安全桌面、性能 | M |
| 录制 UI | 输入法/媒体键/键盘布局 | S/M |
| 跨平台全局热键 | X11 Wayland 差异、macOS 辅助功能权限 | L |

## 7. 验收标准

1. 保存全局快捷键时显示“可用/被占用/系统保留”，被占用时不能保存或明确降级为应用内。
2. 默认模式下其他程序已注册的组合不会同时触发（本程序注册失败即提示）。
3. 开启“强制覆盖”后低级钩子模式可触发，日志记录模式与按键。
4. 录制 UI 能捕获 Ctrl+Alt+S、F9、媒体键；Esc 取消；非法组合即时提示。
5. Linux/macOS 不再显示“全局”可用，或明确标注“应用内”。
6. 快捷键配置可导出/导入，单项可恢复默认。
7. 设置变更后无需重启即可生效，失败有可见提示。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) | 注册失败语义、F12 保留 | 2026-11-03 |
| [WH_KEYBOARD_LL 低级钩子](https://learn.microsoft.com/en-us/previous-versions/ms997649(v=msdn.10)) | 全局键盘监听与 CallNextHookEx | 2026-11-03 |
| [PowerToys Keyboard Manager](https://learn.microsoft.com/en-us/windows/powertoys/keyboard-manager) | 可视化重映射/冲突 | 2026-11-03 |
| [AutoHotkey 文档](https://www.autohotkey.com/docs/) | 按应用热键、和弦 | 2026-11-03 |
| [VS Code 键位绑定](https://code.visualstudio.com/docs/getstarted/keybindings) | 导入导出与上下文 | 2026-11-03 |
