# OCR 文字识别现代化设计

状态：设计稿（2026-11-03）
适用：`WindowsOcrService`、`ITextRecognitionService`、`OcrResultWindow`、`CaptureEditorWindow`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| Windows 内置 OCR | `Platform.Windows/Ocr/WindowsOcrService.cs` | 半成品 | 通过 PowerShell 5.1 调 WinRT `Windows.Media.Ocr`；临时 PNG + 结果 TXT |
| 非 Windows 回退 | `CompositionRoot.cs:224` Tesseract 占位服务 | 半成品 | 需用户安装 Tesseract；未安装时不可用 |
| 识别结果模型 | `Kernel/Ocr/ITextRecognitionService.cs` | 可用 | 文本 + `TextRecognitionLine`（文本与矩形） |
| 标注编辑器 OCR | `CaptureEditorWindow.axaml.cs:362-398` | 可用 | 识别当前合成图 |
| 结果窗口 | `OcrResultWindow.axaml(.cs)` | 可用 | 展示文本，复制 |
| 条码/二维码 | `BarcodeDetectionService.cs`、`BarcodeResultWindow` | 可用 | ZXing.Net，跨平台 |
| 贴图 OCR | `PinnedImageWindow` 持有 `ITextRecognitionService` | 可用 | 右键菜单入口（需确认交互） |
| 语言设置 | `AppSettings.OcrLanguage` | 半成品 | 留空=系统语言；无语言包列表 UI |
| 批量 OCR / 可搜索 PDF | 无 | 缺口 | — |
| 屏幕区域直接 OCR | 无 | 缺口 | 必须先截图再识别 |

主要问题：

1. **PowerShell 子进程开销大**：每次识别启动 PowerShell、写临时文件、Base64 脚本，冷启动约数百毫秒到秒级；高频截图 OCR 体验差。
2. **依赖 PowerShell 5.1**：部分精简系统/受限环境没有；`IsAvailable` 直接判不可用。
3. **临时文件清理不彻底**：异常路径可能残留 `%TEMP%\tabtaskexplorer-ocr`。
4. **语言能力不透明**：用户不知道系统安装了哪些 OCR 语言包；`OcrLanguage` 填错时只返回失败。
5. **结果能力弱**：只展示整段文本，无法按行选择、无法叠加原文框、无法保留段落/单词结构。
6. **无区域 OCR 快捷入口**：不能“框选屏幕 → 直接出文字”，必须走截图→编辑器→OCR。
7. **无批处理**：不能对产物目录/选中图片批量 OCR 并写入索引。
8. **无隐私说明**：Windows OCR/Tesseract 均为本地，但 UI 未说明“不上传云端”。
9. **Tesseract 依赖外部安装**：路径查找、版本/语言包校验缺失。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 11 截图工具 Text Actions | 截图后一键 OCR，支持选择复制、复制全部、快速脱敏邮箱/电话；全程本地（[Microsoft 支持](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots)） | “截图后 OCR + 选择复制 + 脱敏”是基准体验 |
| Windows.Media.Ocr | 系统离线 OCR，`OcrEngine` 支持 `AvailableRecognizerLanguages`、`MaxImageDimension`（[OcrEngine](https://learn.microsoft.com/en-us/uwp/api/Windows.Media.Ocr.OcrEngine)） | 应改为进程内调用，并枚举语言包 |
| Windows App SDK 文本识别 | 面向 NPU 的新 OCR API，更快更准，但仅部分设备（[文档](https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition)） | P1 可选加速路径，不作为唯一依赖 |
| Tesseract | 跨平台开源 OCR，需语言包（`eng`、`chi_sim` 等） | Linux/macOS 主路径；Windows 作为回退 |
| Snipaste | 截图后文字识别/取色 | 入口应与截图/贴图统一 |

## 3. 推荐方案

### 必做（P0）

1. **进程内 WinRT OCR**
   - 用 CsWinRT/Windows SDK 直接调用 `OcrEngine.TryCreateFromLanguage` / `TryCreateFromUserProfileLanguages`，避免 PowerShell 子进程。
   - 保留 PowerShell 实现作为回退（系统/运行时异常时）。
   - 接入点：`WindowsOcrService.cs` 重写 `RecognizeAsync`；`ITextRecognitionService` 契约不变。
   - 性能目标：1080p 图片识别 < 500ms（不含解码）。

2. **语言包枚举与选择**
   - `ITextRecognitionService` 增加 `GetLanguages()`；设置页下拉显示“系统已安装：中文简体/英文…”，并标注“未安装的语言需在系统设置中添加”。
   - 接入点：`Kernel/Ocr/ITextRecognitionService.cs`、`SettingsWindow.axaml(.cs)`、`AppSettings.OcrLanguage`。

3. **区域 OCR 快捷入口**
   - 新增全局快捷键“区域 OCR”：框选后直接识别并把文本写入剪贴板，同时保留截图产物（可选）。
   - 接入点：`CaptureCoordinator` 新增 `CaptureOcrRegionAsync`；复用 `SelectRegionAreaAsync`；`ShortcutActionCatalog` 增加动作。

4. **结果面板增强**
   - 左侧原图（带识别框），右侧按行文本；支持点选行高亮、Ctrl+A 全选、复制选中/全部、搜索、导出 TXT/JSON。
   - 接入点：`OcrResultWindow.axaml(.cs)`、`TextRecognitionLine` 已含矩形。

5. **隐私与失败提示**
   - UI 明示“本地识别，不上传云端”；语言包缺失、图片过小/过大、无文本时给出具体原因。
   - 接入点：`WindowsOcrService.DescribeAvailability`、`OcrResultWindow`。
   - 注意：`Windows.Media.Ocr` 对过小图片可能无结果，需按 `MaxImageDimension` 放大/缩放。

### 可选（P1/P2）

- 批量 OCR：对选中图片/产物目录批量识别，结果写入 `captures.index.jsonl` 与 sidecar `.txt`。
- 可搜索 PDF：OCR 文本层 + 原图，导出 PDF（需要 PDF 库）。
- 脱敏：一键遮盖邮箱/电话/身份证号（参考 Snipping Tool Quick Redact）。
- 翻译：本地/在线可选，默认关闭。
- Windows App SDK NPU OCR：设备支持时自动优先。
- 条码结果与 OCR 合并到同一“识别中心”。

### 不建议

- 默认使用云端 OCR：隐私与离线可用性不符。
- 在 UI 线程同步识别：必须后台线程 + 取消。
- 自研 OCR 模型：成本高，先用系统/Tesseract。

## 4. 交互与视觉

- 截图编辑器工具栏“文字识别”按钮；识别中显示进度条，可取消。
- 区域 OCR：遮罩顶部提示“框选后自动识别并复制”。
- 结果窗口：左图右文，识别框用强调色细线；行悬停高亮；底部“复制全部 / 复制选中 / 导出 / 重新识别”。
- 语言选择：设置页下拉 + “打开系统语言设置”链接（Windows）。
- 失败：不弹大对话框，使用结果窗口内联提示 + “重试/切换语言”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 进程内 OCR | `Platform.Windows/Ocr/WindowsOcrService.cs` | CsWinRT；保留 PowerShell 回退 |
| 语言枚举 | `Kernel/Ocr/ITextRecognitionService.cs`、`SettingsWindow` | 返回语言标签与 BCP-47 |
| 区域 OCR | `CaptureCoordinator.cs`、`ShortcutActionCatalog.cs`、`SettingsWindow` | 复用选区与取消 |
| 结果面板 | `Views/OcrResultWindow.axaml(.cs)` | 行选择 + 框叠加 |
| 批量/索引 | `Services/CaptureHistoryStore.cs`（截图模块新增） | OCR 状态写入索引 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 进程内 WinRT | CsWinRT 版本、运行时兼容、XAML/WinRT 初始化 | M |
| 语言包 | 系统语言包缺失；不同版本 API 差异 | S |
| 区域 OCR | 与截图取消/多屏逻辑交织 | S/M |
| 结果面板 | 大量行的渲染性能 | S/M |
| 批量 OCR | 长任务进度与取消 | M |

## 7. 验收标准

1. 1080p 中文截图识别 < 500ms，结果行框位置基本准确。
2. 设置页能列出系统已安装 OCR 语言；切换语言后识别结果变化正确。
3. 区域 OCR 快捷键可框选并直接复制文本；Esc 取消无残留。
4. 结果面板支持按行选择复制、全选、导出 TXT/JSON；识别框与原图对齐。
5. 无文本/图片过小/语言包缺失时给出明确中文原因。
6. 识别过程中取消，任务在 1 秒内停止且不写临时文件。
7. 临时目录在正常与异常路径都不残留超过 24 小时的文件。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows.Media.Ocr.OcrEngine](https://learn.microsoft.com/en-us/uwp/api/Windows.Media.Ocr.OcrEngine) | 系统 OCR 能力、语言与图片尺寸 | 2026-11-03 |
| [Windows App SDK 文本识别](https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition) | NPU OCR 新 API 的定位 | 2026-11-03 |
| [Windows 11 截图工具](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots) | Text Actions、选择复制、脱敏 | 2026-11-03 |
| [Snipaste](https://www.snipaste.com/) | 截图后识别入口 | 2026-11-03 |

注：CsWinRT 在 .NET 10 / Avalonia 12 下的具体接入方式需实现阶段验证；本文不声称已完成兼容性测试。
