# 贴图窗口现代化设计

状态：设计稿（2026-11-03）
适用：`PinnedImageWindow`、`PinWindowManager`、`CaptureCoordinator` 贴图入口
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 贴图窗口 | `PinnedImageWindow.axaml.cs:20-140` | 可用 | 置顶、拖动、滚轮缩放、Ctrl+滚轮透明度、旋转/翻转 |
| 工具窗口行为 | `PinnedImageWindow.axaml.cs:60-71`、`WindowHostStyler.MakeAuxiliary` | 可用 | 不抢焦点、不进 Alt+Tab、主窗口隐藏到托盘仍可见 |
| 创建入口 | `CaptureCoordinator.PinClipboardImageAsync`（:1593）、`PinImageFile`（:1634）、`CapturePinRegionAsync`（:381） | 可用 | 截图/剪贴板/历史文件 |
| OCR / 条码 | `PinnedImageWindow.axaml.cs:534-544` | 可用 | 右键识别 |
| 复制/保存/关闭 | 右键菜单/快捷键 | 可用 | PNG 保存 |
| 贴图管理器 | 无 | 缺口 | 每个贴图独立窗口，无列表/批量操作 |
| 持久化 | 无 | 缺口 | 重启后全部消失 |
| 点击穿透 | 无 | 缺口 | 不能临时让鼠标穿透 |
| 虚拟桌面 | 无 | 缺口 | 不跟随/不固定到所有虚拟桌面 |
| 缩略图模式 | 无 | 缺口 | 无小尺寸预览/双击还原 |
| 阴影/边框 | 无 | 缺口 | 无视觉边界 |
| 多屏 DPI | 半成品 | 半成品 | 打开时取屏幕缩放；跨屏拖动不重算 |

主要问题：

1. **贴图不可管理**：没有贴图列表、分组、批量关闭、全部置顶；用户只能逐个右键。
2. **重启即丢失**：文件来源贴图不持久化位置/缩放/透明度；内存贴图更无法恢复。
3. **无点击穿透**：贴图覆盖在界面上时无法临时让鼠标点到下层。
4. **无虚拟桌面支持**：贴图只在当前桌面，切换桌面后消失/不可见。
5. **跨屏 DPI 模糊**：`OnWindowOpened` 只按初始屏幕缩放计算；拖到不同 DPI 屏幕后尺寸/清晰度不重算。
6. **无缩略图模式**：大贴图占屏，无法缩成小预览。
7. **无阴影/边框**：贴图与背景边界不清。
8. **无“贴图组”**：多张贴图不能一起移动/缩放/关闭。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Snipaste | 贴图回贴屏幕、透明度、旋转、缩略图、多虚拟桌面、贴图组（[官网](https://www.snipaste.com/)、[更新](https://www.snipaste.com/download.html)） | 贴图管理/虚拟桌面/缩略图 |
| ShareX | 贴图/固定到屏幕、图像编辑器 | 贴图入口 |
| Windows 照片 | 图片查看/缩放 | 缩放体验 |
| PixPin | 贴图、标注、OCR、长截图 | 贴图工具产品形态 |
| CleanShot X (macOS) | 贴图、标注、滚动截图 | 贴图管理/快捷操作 |

## 3. 推荐方案

### 必做（P0）

1. **贴图管理器**
   - `PinBoardService` 单例：记录所有贴图窗口（Id、图像源、位置、大小、缩放、透明度、置顶、虚拟桌面、是否文件来源）。
   - 托盘/贴图标签页提供列表：缩略图、位置、批量关闭、全部置顶、全部穿透。
   - 接入点：`PinWindowManager.cs` 从静态工厂升级为服务；`App.axaml.cs` 托盘菜单；`MainWindowViewModel` 暴露入口。

2. **持久化（文件来源）**
   - 仅对文件来源贴图保存 `PinnedImageState`（路径、位置、缩放、透明度、置顶、旋转）；启动时可恢复；内存贴图明示不持久化。
   - 接入点：`AppSettings.PinnedImages`；`PinBoardService` 启动恢复；`PinnedImageWindow` 关闭时保存。

3. **点击穿透**
   - 右键/快捷键切换 `WS_EX_TRANSPARENT`（Windows）或等价能力；开启后鼠标点击下层，仍可通过托盘/管理器关闭。
   - 接入点：`PinnedImageWindow`、`Platform.Windows/WindowsWindowHostService`。

4. **虚拟桌面**
   - Windows `IVirtualDesktopManager` 查询/移动；设置“仅在当前桌面/所有桌面”。
   - 接入点：`PinnedImageWindow`、新增 `Platform.Windows/VirtualDesktopService`；非 Windows 降级提示。

5. **跨屏 DPI**
   - 拖动结束/显示器变化时按目标屏幕 `Scaling` 重新计算 `baseWidth/baseHeight/zoom`，保持物理像素清晰。
   - 接入点：`PinnedImageWindow.PositionChanged`（:79）、`Screens.Changed`（:80）。

6. **缩略图模式/阴影**
   - 双击切换缩略图（25%）；可选 1px 边框 + 轻阴影；右键菜单开关。
   - 接入点：`PinnedImageWindow`、`PinnedImageWindow.axaml`。

### 可选（P1/P2）

- 贴图组：多张贴图作为一组移动/缩放/关闭。
- 贴图内重新标注（打开标注编辑器并回写）。
- 贴图透明度快捷手势（Alt+滚轮）。
- 贴图置顶优先级/层级管理。
- 贴图边缘吸附/对齐辅助线。

### 不建议

- 把贴图做成独立进程：窗口生命周期/托盘协同复杂。
- 默认持久化内存贴图：无法可靠恢复，且可能含敏感内容。
- 依赖系统贴图 API：跨平台不一致。

## 4. 交互与视觉

- 贴图：拖动移动、滚轮缩放、Ctrl+滚轮透明度、右键菜单（复制/保存/标注/OCR/置顶/穿透/缩略图/关闭）。
- 管理器：缩略图网格 + 位置/大小；多选批量关闭；全部置顶开关。
- 缩略图模式：25% 尺寸，双击还原；角标显示“缩略图”。
- 穿透：边缘显示虚线提示；tooltip“点击穿透中，使用托盘关闭”。
- 阴影：可选 8px 模糊、20% 黑；边框 1px 半透明。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 管理器 | `PinWindowManager.cs`、新增 `Services/Capture/PinBoardService.cs` | 弱引用窗口列表 |
| 持久化 | `AppSettings`、`PinnedImageWindow` | 仅文件来源 |
| 穿透 | `PinnedImageWindow`、`WindowsWindowHostService` | `WS_EX_TRANSPARENT` |
| 虚拟桌面 | 新增 `Platform.Windows/VirtualDesktopService.cs` | COM 接口 |
| DPI | `PinnedImageWindow.axaml.cs` | 目标屏重算 |
| 缩略图 | `PinnedImageWindow.axaml(.cs)` | 尺寸切换 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 管理器 | 多窗口生命周期/弱引用 | M |
| 虚拟桌面 | Windows 版本/COM 接口变化 | M |
| 穿透 | 开启后无法点击，需托盘兜底 | S |
| DPI | 混合 DPI 拖拽 | S/M |
| 持久化 | 显示器变化/路径失效 | S |

## 7. 验收标准

1. 贴图管理器列出全部贴图，可批量关闭/全部置顶/全部穿透。
2. 文件来源贴图重启后恢复位置/缩放/透明度；内存贴图有明确提示。
3. 点击穿透开启后鼠标可点下层，托盘可关闭贴图。
4. 贴图可固定到所有虚拟桌面/仅当前桌面。
5. 跨屏拖动后清晰度与尺寸正确。
6. 双击进入缩略图模式，再次双击还原。
7. 阴影/边框可开关，视觉边界清晰。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Snipaste](https://www.snipaste.com/) | 贴图/透明度/旋转 | 2026-11-03 |
| [Snipaste 更新](https://www.snipaste.com/download.html) | 虚拟桌面/缩略图/贴图组 | 2026-11-03 |
| [ShareX](https://getsharex.com/) | 贴图/固定到屏幕 | 2026-11-03 |
| [PixPin](https://pixpin.cn/) | 贴图/OCR/长截图 | 2026-11-03 |
| [CleanShot X](https://cleanshot.com/) | macOS 贴图产品形态 | 2026-11-03 |
