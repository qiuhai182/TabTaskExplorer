# 标注编辑器现代化设计

状态：设计稿（2026-11-03）
适用：`CaptureEditorWindow`、`AnnotationModel`、`AnnotationCanvas`、`AnnotationRenderer`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 标注工具 | `CaptureEditorWindow.axaml.cs:621-691` | 可用 | 矩形/椭圆/箭头/画笔/马克笔/高亮/马赛克/模糊/文字/序号/放大镜/橡皮擦 |
| 标注模型 | `Services/Capture/AnnotationModel.cs` | 可用 | `AnnotationTool`、`AnnotationShape`、`AnnotationDocument` |
| 画布/命中 | `AnnotationCanvas.cs`、`AnnotationRenderer.cs` | 可用 | 绘制、撤销栈、取色模式 |
| 撤销/重做/清空 | `CaptureEditorWindow.axaml.cs:269-441` | 可用 | 撤销可恢复清空 |
| 文字 | `InputDialog` + `AnnotationTool.Text` | 可用 | 输入多行文字后放置 |
| 序号 | `AnnotationTool.Number` | 半成品 | 手动输入/绘制，无自动递增 |
| 取色 | `ColorMagnifierControl`、Ctrl+O | 可用 | 从当前图取色 |
| OCR/条码 | `CaptureEditorWindow.axaml.cs:338-398` | 可用 | 识别当前合成图 |
| 复制/保存/另存/贴图 | 编辑器工具栏、Ctrl+C/Ctrl+S/Ctrl+T | 可用 | PNG 保存 |
| 对象再编辑 | 无 | 缺口 | 画完不能选中/移动/缩放/改色/删除 |
| 旋转/层级 | 无 | 缺口 | 无旋转手柄、无置顶/置底 |
| 文字样式 | 半成品 | 半成品 | 有字号，无字体/颜色/背景/对齐 |
| 标注模板 | 无 | 缺口 | 每次重新选颜色/线宽 |

主要问题：

1. **标注是“一次性”的**：`AnnotationShape` 只保存创建参数，画完即固化；不能选中后移动/缩放/旋转/改色/删除。
2. **无对象层级**：多个标注重叠无法调整 z-order；无锁定/分组。
3. **文字不可再编辑**：输入后只能撤销重画；无字体、颜色、背景、对齐。
4. **序号不智能**：无自动递增、起始值、重新编号、统一样式。
5. **无旋转**：箭头/矩形/文字不能旋转；Snipaste 已支持多数标注旋转。
6. **无模板/样式收藏**：常用红色箭头、黄色高亮每次重配。
7. **大图性能**：标注层与底图同步重绘，超大图可能卡顿。
8. **快捷键不完整**：无 Delete 删除选中、方向键微调、Ctrl+D 复制对象。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Snipaste | 矩形/椭圆/线条/箭头/铅笔/马克笔/文字；马赛克/高斯模糊/橡皮擦/撤销重做；旋转/二次编辑（[官网](https://www.snipaste.com/)、[更新](https://www.snipaste.com/download.html)） | 对象编辑/旋转/样式 |
| Greenshot | 矩形/箭头/高亮/模糊/文字，轻量编辑器（[帮助](https://getgreenshot.org/help/zh-cn/)） | 轻量工具集 |
| Screenpresso | 矢量编辑器：箭头/高亮/气泡/矩形/椭圆（[手册](https://www.screenpresso.com/download/docs/ScreenpressoHelp.pdf)） | 气泡/样式 |
| Windows 11 截图工具 | 笔/荧光笔/橡皮擦/裁剪 | 快速标注模式 |
| ShareX | 图像编辑器 + 动作链 | 标注后动作 |

## 3. 推荐方案

### 必做（P0）

1. **对象模型升级**
   - `AnnotationShape` 增加 `Id/ZIndex/IsLocked/IsSelected/Style`；`AnnotationDocument` 维护选择集、命中测试、z-order。
   - 支持选中、拖动、8 向缩放、旋转（矩形/箭头/文字）、改色、删除、复制/粘贴、置顶/置底。
   - 接入点：`AnnotationModel.cs`、`AnnotationCanvas.cs`、`AnnotationRenderer.cs`、`CaptureEditorWindow.axaml(.cs)`。

2. **文字与序号增强**
   - 文字：双击重新编辑、字体族/字号/颜色/背景/对齐/自动换行。
   - 序号：自动递增、起始值、重新编号、圆形/方形样式。
   - 接入点：`AnnotationShape` 增加 `TextStyle/NumberValue`；`CaptureEditorWindow` 工具选项条。

3. **旋转与样式**
   - 矩形/箭头/文字支持旋转手柄；箭头样式（直线/曲线/双头）；线宽/虚线/透明度。
   - 接入点：`AnnotationRenderer`、`AnnotationCanvas`。

4. **快捷键与批量**
   - Delete 删除选中、Ctrl+D 复制、方向键微调（Shift 10px）、Ctrl+A 全选、Esc 取消选择。
   - 接入点：`CaptureEditorWindow.OnWindowKeyDown`（:410-460）。

5. **模板/样式收藏**
   - 保存常用样式（颜色/线宽/字号/箭头）为预设；顶部下拉快速切换。
   - 接入点：`AppSettings.AnnotationPresets`；`CaptureEditorWindow`。

### 可选（P1/P2）

- 气泡/标注框、放大镜边框样式、聚光灯/遮罩。
- 标注分组/组合。
- 标注层导出 SVG/JSON（可再次编辑）。
- 大图分块渲染/降采样预览。
- 快速标注模式（无工具栏，笔/高亮/箭头三键切换）。

### 不建议

- 完整矢量绘图应用（路径编辑/布尔运算/图层）。
- 把标注直接烧录到底图后不可撤销：保留文档模型。
- 依赖系统画图程序：跨平台不一致。

## 4. 交互与视觉

- 顶部工具栏：选择/矩形/椭圆/箭头/画笔/马克笔/高亮/马赛克/模糊/文字/序号/放大镜/橡皮擦/取色。
- 右侧：颜色、线宽、字号、箭头样式、透明度。
- 选中：8 个缩放手柄 + 旋转手柄；Delete 删除；方向键微调。
- 文字：双击内联编辑；空文本自动删除。
- 序号：单击自动编号；工具选项可设起始值/重置。
- 状态栏：当前工具、坐标、颜色、缩放。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 对象模型 | `AnnotationModel.cs` | 可序列化，便于模板/撤销 |
| 命中/手柄 | `AnnotationCanvas.cs` | 纯几何，不依赖控件 |
| 渲染 | `AnnotationRenderer.cs` | 旋转/阴影/文字样式 |
| 快捷键 | `CaptureEditorWindow.axaml.cs` | 选中操作 |
| 预设 | `AppSettings`、`CaptureEditorWindow` | JSON |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 对象编辑 | 坐标变换/旋转/缩放易错 | M/L |
| 文字样式 | 字体回退/跨平台渲染 | M |
| 大图性能 | 重绘卡顿 | M |
| 预设 | 版本兼容 | S |

## 7. 验收标准

1. 已画形状可选中、移动、缩放、旋转、改色、删除；撤销/重做覆盖。
2. 文字双击可编辑；空文本自动删除；样式持久化。
3. 序号自动递增，可设起始值并一键重编号。
4. Delete/Ctrl+D/方向键等快捷键生效。
5. 预设可保存/切换，重启后保留。
6. 5000×3000 图片标注操作无明显卡顿（>20fps）。
7. 标注文档可撤销到初始状态。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Snipaste](https://www.snipaste.com/) | 标注工具/贴图 | 2026-11-03 |
| [Snipaste 更新](https://www.snipaste.com/download.html) | 旋转/二次编辑/样式 | 2026-11-03 |
| [Greenshot 帮助](https://getgreenshot.org/help/zh-cn/) | 轻量标注 | 2026-11-03 |
| [Screenpresso 手册](https://www.screenpresso.com/download/docs/ScreenpressoHelp.pdf) | 矢量标注 | 2026-11-03 |
| [Windows 11 截图工具](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots) | 快速标注 | 2026-11-03 |
