# 滚动长截图（规划中）设计

状态：设计稿（2026-11-03），当前代码零实现
适用：`Kernel/Capture`、`WindowsScreenCaptureService`、`CapturePanel`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 滚动长截图 | 无 | 缺口 | 全仓库无 `ScrollCapture/Stitch/LongScreenshot` 实现（已 Grep 确认） |
| 区域截图基础 | `CaptureCoordinator.SelectRegionAreaAsync`（:931）、`CaptureInternalAsync`（:573） | 可用 | 可复用选区、取消、保存、剪贴板、历史 |
| 多屏/DPI | `WindowsScreenGeometry`、`CapturePixels` | 可用 | 物理坐标裁剪 |
| 窗口识别 | `WindowsScreenRegionDetector` | 可用 | 可确定滚动容器所在窗口 |
| 图像处理 | 无 | 缺口 | 无拼接/相似度/重叠检测代码 |

结论：长截图应作为独立模块从零设计，但复用现有截图流水线（选区、取消、保存、历史、动作链）。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| ShareX | 先截首帧，后续帧与上一帧比较，检测变化后裁掉顶部并把底部追加；支持自动忽略底部 50px、自动忽略底边静态区域；首帧拼接失败自动停止（[Scrolling screenshot](https://getsharex.com/docs/scrolling-screenshot)、[Changelog](https://getsharex.com/changelog)） | “帧差 + 重叠裁剪”是最朴素可靠的路线；“忽略底边”很实用 |
| FastStone Capture | 滚动窗口/网页截图，`Ctrl+Alt+PrtSc`（[官网](https://faststone.org.cn/)） | 入口应给独立快捷键 |
| PicPick | Scrolling Window 模式，可调滚动延时，实时显示进度（[帮助](https://picpick.app/ko/help/)） | 滚动延时与实时预览是必要交互 |
| Screenpresso | 自动模式失败时提供手动模式：用户多次滚动并释放鼠标，工具把连续截图拼接（[手册](https://www.screenpresso.com/download/docs/ScreenpressoHelp.pdf)） | 必须提供手动兜底 |
| Snipaste | 官方功能页未列出滚动截图（截至检索日） | 本项目不能以 Snipaste 为长截图参考 |

## 3. 推荐方案

### 必做（P0）

1. **内核契约**
   - 新增 `IScrollCaptureService`：
     ```csharp
     Task<ScrollCaptureResult> CaptureAsync(ScrollCaptureRequest request, IProgress<ScrollCaptureProgress> progress, CancellationToken token);
     ```
   - `ScrollCaptureRequest`：区域（物理像素）、滚动方向（默认垂直向下）、滚动步长（像素/百分比）、最大帧数、最大高度、帧间隔、是否自动滚动、是否保留固定头/尾。
   - `ScrollCaptureResult`：拼接图字节、总高度、帧数、失败原因、失败帧索引。
   - 接入点：`Kernel/Capture/IScrollCaptureService.cs`；`NullScrollCaptureService` 供非 Windows 回退。

2. **采集与滚动**
   - 采集：优先 WGC 区域帧；回退 GDI。每次滚动后等待 150–300ms 稳定再截帧。
   - 自动滚动：`SendInput` 发送 `MOUSEEVENTF_WHEEL` 到目标窗口（先 `SetForegroundWindow` + 光标移入区域）；步长建议 60%–80% 可视高度。
   - 停止条件：连续 2 帧无变化、达到最大帧数/高度、用户 Esc、目标窗口失焦。
   - 接入点：`Platform.Windows/WindowsScrollCaptureService.cs`；`WindowsScreenCaptureService` 抽出 `CaptureRegionFrame` 复用。

3. **拼接算法**
   - 帧差：灰度化 + 降采样（宽度 256）→ 在重叠带（建议底部 20%–35%）做垂直偏移搜索（SAD/NCC）→ 找最佳偏移。
   - 裁剪：新帧去掉顶部 `offset` 像素，保留底部新增部分；若偏移 < 最小重叠（如 30px），判定失败。
   - 固定头/尾：比较首帧与后续帧的顶部/底部行；若顶部 N 行在多帧几乎不变，视为 sticky header，后续帧裁掉顶部 N；底部同理。
   - 拼接失败：回退“保留上一帧 + 提示用户手动滚动”。
   - 接入点：`Kernel/Services/ScrollStitcher.cs`（纯算法，可单测）；输入 `IReadOnlyList<byte[]>` 灰度帧，输出偏移与拼接计划。

4. **手动模式（兜底）**
   - 用户先框选区域；工具显示悬浮条“滚动后按 Space 截取下一帧，按 Enter 完成，Esc 取消”。
   - 每帧可预览缩略图，支持“删除最后一帧/重拍”。
   - 接入点：新增 `Views/ScrollCaptureOverlay.axaml(.cs)`；复用 `RegionSelectionWindow` 的遮罩与取消输入。

5. **UI 与产物**
   - 截图面板新增“滚动长截图”按钮；全局快捷键可绑定。
   - 结果预览窗口：左侧拼接大图（可缩放/平移），右侧帧列表与“保存/复制/标注/重拍”。
   - 保存到现有产物目录，命名 `scroll-{date}-{time}.png`；写入截图索引（见 `screenshot.md`）。
   - 接入点：`CapturePanel.axaml`、`CaptureCoordinator`、`CapturePaths`、`CaptureHistoryStore`。

### 可选（P1/P2）

- 横向滚动长截图（宽图）。
- 浏览器专用：若目标是 Chromium 且开启远程调试端口，可用 CDP `Page.captureScreenshot` 的 `captureBeyondViewport` 一次成像（不作为默认，涉及调试端口与权限）。
- 智能等待：检测懒加载骨架屏消失、图片解码完成（帧稳定 + 超时）。
- 自动识别滚动容器：用 UIA 找到 `ScrollPattern` 可滚动元素，把滚动发到该元素而非窗口。
- 超长图分块导出（PNG 高度上限/内存保护）。

### 不建议

- 仅靠“整屏像素完全一致”判断滚动结束：动画/视频/时钟会导致永不结束；应结合“重叠匹配失败 + 最大帧数”。
- 用 OCR 文本定位拼接：慢且不稳，像素重叠足够。
- 默认自动滚动所有窗口：部分应用（游戏、远程桌面、受保护内容）会失败，必须允许手动模式。

## 4. 交互与视觉

- 入口：截图面板“滚动长截图”按钮 + 可配置快捷键。
- 选区：与区域截图一致的窗口/子视图预选；确认后进入滚动模式。
- 自动模式：顶部悬浮条“正在滚动 3/20 帧 · 已拼接 1280px · Esc 停止”，实时显示拼接缩略图。
- 手动模式：底部悬浮条“滚动后按 Space 截取，Enter 完成，Esc 取消”；帧计数与重叠质量提示。
- 失败：顶部黄条“第 7 帧未找到重叠（可能已到底部/页面跳动）”，提供“继续尝试 / 完成并保存 / 放弃”。
- 结果：大图预览 + 帧列表；保存后 toast 与历史入口。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 契约 | 新增 `Kernel/Capture/IScrollCaptureService.cs`、`NullScrollCaptureService.cs` | 三平台接口一致 |
| Windows 实现 | 新增 `Platform.Windows/WindowsScrollCaptureService.cs` | WGC/GDI 帧 + SendInput |
| 拼接算法 | 新增 `Kernel/Services/ScrollStitcher.cs` | 纯托管、可单测、无平台依赖 |
| 遮罩/悬浮条 | 新增 `Views/ScrollCaptureOverlay.axaml(.cs)` | 复用取消输入轮询 |
| UI | `CapturePanel.axaml(.cs)`、`CaptureCoordinator.cs` | 按钮/快捷键/历史 |
| 索引 | `Services/CaptureHistoryStore.cs` | 记录帧数、高度、模式 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 拼接算法 | 固定头/尾、懒加载、动画、缩放导致误匹配 | L |
| 自动滚动 | 目标窗口不接收 SendInput、焦点被抢 | M |
| 内存 | 超长图（> 10000px）位图内存暴涨 | M |
| 跨平台 | Linux/macOS 滚动与采集差异 | L |
| 失败兜底 | 用户手动模式体验 | M |

## 7. 验收标准

1. 对 10 页普通网页（无固定头）自动滚动拼接成功，文字无明显错位/重复。
2. 存在固定顶部导航时，拼接结果只出现一次导航条。
3. 页面底部到达后自动停止（不无限滚动）；最大帧数/最大高度生效。
4. 拼接失败时给出失败帧与原因，并可切换手动模式继续。
5. 手动模式 Space 截帧、Enter 完成、Esc 取消均生效；可删除最后一帧。
6. 结果图保存 PNG 可正常打开；10000px 高度不崩溃（按内存上限分块或拒绝）。
7. 全流程可取消，取消后无残留遮罩/临时文件。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [ShareX Scrolling screenshot](https://getsharex.com/docs/scrolling-screenshot) | 帧差、裁顶部、追加底部、停止条件 | 2026-11-03 |
| [ShareX Changelog](https://getsharex.com/changelog) | 忽略底边、自动忽略静态区域、失败自动停止 | 2026-11-03 |
| [FastStone Capture](https://faststone.org.cn/) | 滚动窗口/网页截图入口 | 2026-11-03 |
| [PicPick 帮助](https://picpick.app/ko/help/) | 滚动延时、实时进度、Scrolling Window | 2026-11-03 |
| [Screenpresso 手册](https://www.screenpresso.com/download/docs/ScreenpressoHelp.pdf) | 自动失败后的手动拼接模式 | 2026-11-03 |
| [Windows.Graphics.Capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture) | 区域帧采集 | 2026-11-03 |

注：Snipaste 官方功能页未列出滚动截图，故不作为参考；固定头/尾处理的具体算法为本项目设计，未在公开文档中核实其实现细节。
