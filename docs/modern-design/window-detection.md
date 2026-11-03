# 窗口识别现代化设计

状态：设计稿（2026-11-03）
适用：`WindowsScreenRegionDetector`、`IScreenRegionDetector`、`RegionSelectionWindow`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 顶层窗口枚举 | `WindowsScreenRegionDetector.Detect`（:103-171） | 可用 | `EnumWindows` + 可见性/最小化过滤 |
| DWM 可见外框 | `TryReadWindowArea`（:410-433） | 可用 | `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` |
| 排除外壳窗口 | `ShellWindowClasses`（:26-35） | 可用 | Progman/WorkerW/任务栏/缩略图 |
| 排除本程序窗口 | `IsOwnProcessWindow`（:348-353） | 可用 | 防止截到自身遮罩 |
| 排除穿透/隐藏窗口 | `IsClickThroughWindow`（:490）、`IsCloakedWindow`（:497） | 可用 | `WS_EX_TRANSPARENT` + DWM cloaked |
| 子窗口枚举 | `AppendChildViews`（:356-405） | 可用 | 每个顶层最多 200 个，最小 32px |
| 鼠标命中链 | `HitTest`（:189-234）、`AppendHitChildChain`（:240-322） | 可用 | 从顶层到最深子窗口，最多 12 层 |
| Tab / 滚轮层级切换 | `RegionSelectionWindow.axaml.cs:164-183/503-534` | 可用 | Shift+Tab 反向；Enter/单击确认 |
| 非 Windows | `NullScreenRegionDetector` | 缺口 | 无窗口预选，只能自由框选 |

主要问题：

1. **只认 Win32 子窗口**：Chromium/Electron/Java/游戏引擎通常只有一个顶层 HWND，内部“标签、地址栏、内容区”不是 Win32 子窗口，无法逐层预选。
2. **无 UI Automation 回退**：没有使用 `IUIAutomation.ElementFromPoint`，因此无法识别现代应用的内部控件边界。
3. **只支持 Windows**：Linux/macOS 退化为全屏/自由框选，与“跨平台一致”的项目约束不符。
4. **候选信息弱**：候选只有标题/类名/`Kind`，没有进程名、进程图标、窗口类型（浏览器/编辑器/对话框）、是否全屏。
5. **无独立窗口列表**：只能鼠标悬停预选，不能键盘浏览全部窗口或搜索窗口。
6. **菜单/工具提示处理粗糙**：`ShellWindowClasses` 排除了任务栏，但弹出菜单、下拉、tooltip 是否应作为候选未定义；目前依赖 z-order 与可见性。
7. **性能边界**：每次 `Detect` 枚举全部窗口 + 子窗口，最多 1500 候选；在窗口很多时可能有几十毫秒开销，移动鼠标时频繁调用需缓存。
8. **DPI/缩放**：候选坐标为物理像素，遮罩换算在 `RegionSelectionWindow` 内完成；混合 DPI 下子窗口边界可能因 DWM 外框与 `GetWindowRect` 混用而不一致。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Snipaste | 自动检测界面元素区域，鼠标移动实时高亮（[官网](https://www.snipaste.com/)） | 已对齐 Win32 层；需补 UIA 层 |
| Windows 11 截图工具 | 窗口/区域/全屏，系统级选择器 | 系统选择器背后是 WGC + 窗口枚举，可作为兜底 |
| ShareX | 窗口/对象/区域，滚动截图 | 对象选择需要更丰富的控件边界 |
| Windows UI Automation | `IUIAutomation` 可获取控件树、边界、名称（官方 API） | 浏览器/Electron 内部区域的关键 |
| Windows.Graphics.Capture | `GraphicsCapturePicker` 系统窗口选择器（[文档](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)） | 可直接复用系统选择器作为“窗口列表”入口 |

## 3. 推荐方案

### 必做（P0）

1. **UI Automation 回退**
   - 在 Win32 子窗口命中失败或只有顶层时，调用 `IUIAutomation.ElementFromPoint` 获取最深可交互元素，向上回溯到有意义的容器（地址栏、标签页、编辑区、按钮组）。
   - 过滤不可见/离屏/零尺寸元素；限制深度（如 8 层）与候选数（如每窗口 100）。
   - 接入点：`WindowsScreenRegionDetector` 新增 `AppendAutomationViews`；`ScreenRegionCandidate.Kind` 增加 `AutomationView`；`RegionSelectionWindow.DescribeCandidate` 更新。
   - 注意：UIA 调用可能阻塞，需超时/缓存；对浏览器可用 `ElementFromPoint` + ControlType 过滤。

2. **候选信息增强**
   - `ScreenRegionCandidate` 增加 `ProcessId / ProcessName / ProcessIcon / WindowClass / IsTopmost / IsFullscreen`。
   - 遮罩顶部显示“窗口标题 · 进程名 · 层级 2/3”，帮助用户判断。
   - 接入点：`Kernel/Capture/IScreenRegionDetector.cs` 模型；`WindowsScreenRegionDetector` 读取 `GetWindowThreadProcessId` + 进程名。

3. **独立窗口选择器**
   - “选择窗口”面板：网格卡片（缩略图 + 标题 + 进程名 + 尺寸），支持搜索、刷新、键盘上下选择；点击即返回候选。
   - 接入点：新增 `Views/WindowPickerWindow.axaml(.cs)`；复用 `Detect` 的顶层窗口列表；截图/录屏共用。

4. **缓存与性能**
   - 一次框选会话内缓存候选列表；鼠标移动只做命中测试，不重复 `EnumWindows`；窗口创建/销毁/前台变化时刷新（`SetWinEventHook` 可选）。
   - 接入点：`RegionSelectionWindow` 持有 `candidateCache`；`WindowsScreenRegionDetector` 增加 `DetectTopLevel` 与 `HitTest` 分离。

5. **跨平台补齐**
   - macOS：`AXUIElementCopyElementAtPosition` 获取 AX 控件链；Linux：AT-SPI `AtspiAccessible` 命中测试。
   - 无权限/不支持时回退为顶层窗口/自由框选，并在 UI 说明。
   - 接入点：新增 `Platform.MacOS/MacScreenRegionDetector.cs`、`Platform.Linux/LinuxScreenRegionDetector.cs`；`CompositionRoot` 按平台注册。

### 可选（P1/P2）

- 窗口缩略图（DWM thumbnail API 或 WGC 单帧）。
- 窗口类型过滤（仅浏览器/仅编辑器/仅对话框）。
- 记住上次选择的窗口/层级。
- 多显示器下按屏幕分组窗口列表。
- 与 WGC 窗口选择器合并，减少自绘选择器维护。

### 不建议

- 注入目标进程或 Hook 其渲染管线：安全与稳定性风险高。
- 依赖窗口标题猜测控件结构：不可靠，优先 UIA。
- 为每个应用写专用规则：维护成本高，UIA 通用层优先。

## 4. 交互与视觉

- 区域遮罩：顶层窗口实线强调框，子视图虚线；UIA 控件用点线；顶部标签显示名称与层级。
- Tab/滚轮：向下进入更深层，向上回到更大范围；Shift+Tab 反向。
- 窗口选择器：卡片 240×150，缩略图居中，底部标题与进程名；搜索框过滤标题/进程。
- 无 UIA 权限：提示“当前应用不支持控件级识别，已回退到窗口级”。
- 键盘：↑↓ 选择，Enter 确认，Esc 取消。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| UIA 回退 | `Platform.Windows/WindowsScreenRegionDetector.cs` | COM 初始化在 STA/MTA 线程需谨慎 |
| 候选模型 | `Kernel/Capture/IScreenRegionDetector.cs` | 新增字段保持向后兼容 |
| 窗口选择器 | 新增 `Views/WindowPickerWindow.axaml(.cs)` | 与截图/录屏共用 |
| 缓存 | `Views/RegionSelectionWindow.axaml.cs` | 会话级缓存，窗口事件失效 |
| 跨平台 | `Platform.MacOS/MacScreenRegionDetector.cs`、`Platform.Linux/LinuxScreenRegionDetector.cs` | 权限失败回退 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| UIA | COM 线程模型、跨进程调用卡顿、权限 | M |
| 窗口选择器 | 缩略图采集成本 | M |
| 跨平台 | macOS 辅助功能权限、Linux AT-SPI 依赖 | L |
| 缓存 | 窗口变化导致候选过期 | S |

## 7. 验收标准

1. 浏览器（Chromium/Edge）窗口内可预选地址栏、标签条、内容区等至少 3 个层级。
2. Electron 应用（VS Code/微信桌面版）可识别内部主要区域。
3. 窗口选择器列出全部可见顶层窗口，搜索“chrome”能过滤；点击后截图区域正确。
4. 混合 DPI 多屏下窗口边界与遮罩一致，无 1–2px 偏移。
5. UIA 不可用时 200ms 内回退到 Win32/全屏，不卡住遮罩。
6. macOS/Linux 至少支持顶层窗口预选；无权限时有明确提示。
7. 窗口很多（> 100）时移动鼠标仍流畅（> 30fps 更新）。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows.Graphics.Capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture) | 系统窗口选择器与捕获项 | 2026-11-03 |
| [Snipaste](https://www.snipaste.com/) | 界面元素自动检测的产品形态 | 2026-11-03 |
| [Windows 11 截图工具](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots) | 系统级截图入口 | 2026-11-03 |
| [ShareX 文档](https://getsharex.com/docs/) | 窗口/对象/区域选择 | 2026-11-03 |

注：UI Automation 的 `ElementFromPoint` 具体调用与跨进程性能未在本次联网资料中逐条核实，标注为实现阶段需验证项。
