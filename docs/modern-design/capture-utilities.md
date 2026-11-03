# 截图辅助工具（取色 / 放大镜 / 条码）现代化设计

状态：设计稿（2026-11-03）
适用：`ScreenColorPickerWindow`、`ColorMagnifierControl`、`BarcodeDetectionService`、`BarcodeResultWindow`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 全屏取色 | `ScreenColorPickerWindow.axaml.cs:19-100`、`CaptureCoordinator.OpenScreenColorPickerAsync`（:1536） | 可用 | 冻结整屏，鼠标移动显示十六进制/RGB，单击复制并关闭，Esc/右键取消 |
| 取色放大镜 | `ColorMagnifierControl.cs`、`ScreenColorPickerWindow.axaml:15` | 可用 | 8× 放大，显示像素网格 |
| 区域框选放大镜 | `RegionSelectionWindow.axaml:26` | 可用 | 框选时辅助像素级定位 |
| 标注编辑器取色 | `CaptureEditorWindow` 取色模式（Ctrl+O） | 可用 | 从当前图像取色 |
| 条码/二维码识别 | `BarcodeDetectionService.cs:29-103`、`BarcodeResultWindow` | 可用 | ZXing.Net，纯托管，跨平台 |
| 条码入口 | `CaptureEditorWindow.axaml.cs:338-352`、`PinnedImageWindow.axaml.cs:534-544` | 可用 | 编辑器/贴图右键识别 |
| 取色历史/调色板 | 无 | 缺口 | 只复制当前颜色 |
| 颜色格式 | 半成品 | 半成品 | 主要十六进制/RGB，无 HSL/CMYK/命名色 |
| 条码批量/多码 | 无 | 缺口 | 一次只解一个结果 |
| 从文件/剪贴板独立识别 | 无 | 缺口 | 只能从编辑器/贴图当前图 |
| 二维码生成 | 无 | 缺口 | 只读不生成 |
| 标尺/像素尺 | 无 | 缺口 | 无屏幕标尺 |

主要问题：

1. **取色无历史/调色板**：连续取多个颜色只能重复操作；无法保存常用色。
2. **颜色格式单一**：设计/开发常需要 HEX、RGB、HSL、HSV、CMYK；没有格式切换。
3. **条码识别入口受限**：只能对编辑器/贴图当前图识别；不能对文件、剪贴板、屏幕区域直接识别。
4. **无多码识别**：一张图有多个二维码时只返回一个。
5. **无二维码生成**：无法把文本/URL 生成二维码贴到屏幕或保存。
6. **无独立工具面板**：取色、条码、放大镜分散在截图/标注/贴图各处。
7. **无屏幕标尺/像素测量**：UI 对齐/截图测量需要外部工具。
8. **无剪贴板颜色格式**：复制 HEX 后无法选择“复制 RGB/HSL”。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Snipaste | 截图取色器、像素级移动、放大镜、贴图 | 取色体验基准 |
| PicPick | 取色器、调色板、屏幕标尺、量角器 | 调色板/标尺 |
| ShareX | 取色工具、二维码生成/识别 | 条码/二维码 |
| ZXing.Net | 纯托管条码/二维码读写（[ZXing.Net](https://github.com/micjahn/ZXing.Net)） | 现有识别；可加生成 |
| Windows 放大镜 | 屏幕放大、跟随鼠标 | 放大镜体验 |
| ColorPic / Just Color Picker | 取色历史、多格式、调色板 | 颜色历史/格式 |

## 3. 推荐方案

### 必做（P0）

1. **取色历史与调色板**
   - 记录最近 20 个颜色（HEX），支持固定/删除/复制；调色板可保存为 JSON。
   - 接入点：`ScreenColorPickerWindow`、新增 `Services/ColorHistoryStore.cs`；`CaptureEditorWindow` 取色共用。
   - UI：取色窗口底部横向色块；点击复制。

2. **颜色格式切换**
   - 显示 HEX/RGB/HSL/HSV；设置默认格式；复制按钮按当前格式写入剪贴板。
   - 接入点：`ScreenColorPickerWindow`、`ColorMagnifierControl`、`AppSettings.ColorFormat`。

3. **条码/二维码独立入口**
   - “扫描图片/剪贴板/屏幕区域”：选择文件、读取剪贴板图片或框选屏幕，识别后展示文本/格式/位置。
   - 多码识别：返回所有结果列表。
   - 接入点：`BarcodeDetectionService` 增加 `DecodeAll`；新增 `Views/BarcodeScannerWindow.axaml`；`CapturePanel` 入口。

4. **二维码生成**
   - 输入文本/URL，生成二维码；可复制图片/保存 PNG/贴到屏幕。
   - 接入点：`BarcodeDetectionService` 增加 `Encode`（ZXing Writer）；`BarcodeResultWindow` 增加“生成”页签。

5. **统一“截图工具”面板**
   - 截图标签页增加“取色 / 扫条码 / 生成二维码 / 屏幕标尺”入口，减少入口分散。
   - 接入点：`CapturePanel.axaml`、`CaptureTabViewModel`。

6. **剪贴板颜色/条码结果**
   - 取色复制支持“HEX/RGB/HSL”；条码结果支持复制文本/打开链接（URL 安全确认）。
   - 接入点：`ISystemClipboardService.WriteTextAsync`；`BarcodeResultWindow`。

### 可选（P1/P2）

- 屏幕标尺/量角器（PicPick 式）。
- 调色板导入（Adobe `.aco`、GIMP `.gpl`）。
- 条码批量文件扫描（目录）。
- 颜色对比度/无障碍检查（WCAG）。
- OCR 与条码结果合并到“识别中心”。

### 不建议

- 自研条码解码：继续用 ZXing.Net。
- 自动打开条码中的 URL：必须先确认，防钓鱼。
- 取色窗口常驻：按需唤起，避免遮挡。

## 4. 交互与视觉

- 取色：全屏冻结，鼠标位置放大镜 128×128，底部色块历史；单击复制并关闭。
- 格式切换：HEX/RGB/HSL 三个小按钮；当前值等宽字体。
- 条码扫描：拖入图片/选择文件/“从剪贴板”/“框选屏幕”；结果列表显示格式、文本、位置。
- 二维码生成：文本输入 + 尺寸/纠错级别 + 预览 + 复制/保存/贴图。
- 屏幕标尺：半透明尺子，可旋转，显示像素/厘米（按 DPI）。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 颜色历史 | 新增 `Services/ColorHistoryStore.cs`；`ScreenColorPickerWindow` | JSON 持久化 |
| 颜色格式 | `ScreenColorPickerWindow`、`ColorMagnifierControl`、`AppSettings` | 转换函数 |
| 条码扫描 | `BarcodeDetectionService`、新增 `BarcodeScannerWindow.axaml` | 文件/剪贴板/屏幕 |
| 二维码生成 | `BarcodeDetectionService.Encode`、`BarcodeResultWindow` | ZXing Writer |
| 工具面板 | `CapturePanel.axaml`、`CaptureTabViewModel` | 入口统一 |
| 剪贴板 | `ISystemClipboardService.WriteTextAsync` | 格式文本 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 颜色历史 | 存储/隐私（颜色无敏感） | S |
| 条码多码 | 识别性能/重复结果 | S/M |
| 二维码生成 | 尺寸/纠错/中文编码 | S |
| 屏幕标尺 | DPI/旋转/多屏 | M |
| 入口统一 | 截图面板拥挤 | S |

## 7. 验收标准

1. 取色窗口显示 HEX/RGB/HSL，单击复制当前格式；Esc 取消。
2. 最近 20 个颜色保存，重启后保留；可固定/删除。
3. 可从文件、剪贴板、屏幕区域扫描条码/二维码，返回文本与格式。
4. 一张图多个二维码时列出全部结果。
5. 输入文本可生成二维码，复制/保存/贴图正常。
6. 条码 URL 打开前有确认提示。
7. 截图标签页可直达取色/条码/二维码工具。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Snipaste](https://www.snipaste.com/) | 取色器/放大镜/贴图 | 2026-11-03 |
| [PicPick](https://picpick.app/) | 取色器/调色板/屏幕标尺 | 2026-11-03 |
| [ZXing.Net](https://github.com/micjahn/ZXing.Net) | 条码/二维码读写 | 2026-11-03 |
| [ShareX](https://getsharex.com/) | 取色/二维码工具 | 2026-11-03 |
| Windows 放大镜（Windows 11 内置） | 屏幕放大交互 | 2026-11-03（官方内置功能，无单独文档链接） |
