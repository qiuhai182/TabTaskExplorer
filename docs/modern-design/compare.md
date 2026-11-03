# 目录 / 文件比对现代化设计

状态：设计稿（2026-11-03）
适用：`CompareTabViewModel`、`ComparePanel`、`DirectoryCompareEngine`、`TextDiffEngine`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 比对标签 | `MainWindowViewModel.OpenCompareTab`（:555）、`ComparePanel.axaml` | 可用 | 独立标签，左右路径输入 |
| 目录递归比对 | `DirectoryCompareEngine`、`ApplyDirectoryResult`（:599） | 可用 | 相同/不同/仅左/仅右/较新状态 |
| 文件文本比对 | `TextDiffEngine`、`ApplyFileResult`（:636） | 可用 | 行对齐、行内差异高亮 |
| 差异导航 | `SelectNextDifference`（:451）、`SelectPreviousDifference`（:467） | 可用 | 上下一个差异 |
| 交换左右 | `SwapSides`（:387） | 可用 | 交换路径并重算 |
| 过滤 | `ApplyCurrentFilter`（:709） | 可用 | 按状态筛选 |
| 打开行 | `OpenRow`（:420） | 可用 | 打开文件/进入目录 |
| 差异位置 | `UpdateDifferencePosition`（:801） | 可用 | “第 N / 共 M 处” |
| 忽略规则 | 无 | 缺口 | 无 `.gitignore`/通配排除 |
| 同步/复制差异 | 无 | 缺口 | 只读比对，不能左→右/右→左复制 |
| 二进制/十六进制 | 无 | 缺口 | 只做文本比对 |
| 图片比对 | 无 | 缺口 | 无并排/差异图 |
| 三方合并 | 无 | 缺口 | 仅两方 |
| 报告导出 | 无 | 缺口 | 不能导出 HTML/CSV |
| 编码选择 | 半成品 | 半成品 | 依赖 `TextFileCodec` 自动探测，无手动覆盖 |

主要问题：

1. **只读比对**：发现差异后不能“复制到左侧/右侧”，需要切到文件视图手动操作。
2. **无忽略规则**：`.git`、`bin`、`obj`、`node_modules` 会淹没结果；不能按扩展名/大小排除。
3. **二进制/图片不可比**：exe、zip、图片只能显示“不同”，无法看差异。
4. **无报告导出**：无法把比对结果交给同事/存档。
5. **编码不可手动选择**：GBK/UTF-8 误判时行内差异会乱码。
6. **大文件风险**：文本比对无大小上限/虚拟化，超大文件可能卡顿。
7. **无三方合并/冲突解决**：代码合并场景不够。
8. **无目录同步计划**：不能生成“将执行哪些复制/删除”的预览。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| WinMerge | 目录/文件比对、过滤、合并、三方、报告（[WinMerge](https://winmerge.org/)） | 过滤 + 合并 + 报告是标准能力 |
| Beyond Compare | 文件夹同步、规则、二进制/图片/十六进制、三方合并 | 多类型比对与会话 |
| Meld | 目录/文件/三方、版本控制集成 | 三方合并 UI |
| Total Commander | 同步目录、比较文件 | 同步计划 |
| VS Code diff | 行内 diff、并排/内联、忽略空白 | 文本 diff 体验 |

## 3. 推荐方案

### 必做（P0）

1. **忽略规则**
   - 目录比对支持默认忽略（`.git`、`bin`、`obj`、`node_modules`、`.vs`）与自定义 glob/扩展名；UI 可勾选。
   - 接入点：`DirectoryCompareEngine` 增加 `CompareOptions.Excludes`；`CompareTabViewModel.ComputeInBackground`（:561）。

2. **差异复制/同步**
   - 选中差异行右键“复制到左侧/右侧”“删除左侧/右侧”；先预览操作计划，确认后执行（走统一文件操作）。
   - “同步目录”生成计划：新增/覆盖/删除，逐项勾选后执行。
   - 接入点：`CompareTabViewModel`、`DirectoryCompareRow`；`IFileOperationService`。

3. **二进制 / 十六进制比对**
   - 二进制按块比较，显示首个差异偏移与十六进制视图；大文件分块读取。
   - 接入点：新增 `Kernel/Services/BinaryCompareEngine.cs`；`ComparePanel` 十六进制视图。

4. **图片比对**
   - 并排显示 + 差异高亮/滑动对比；支持缩放同步。
   - 接入点：`ComparePanel` 图片模式；`ImageViewerControl` 复用。

5. **报告导出**
   - 导出 HTML（带左右/状态/差异行）与 CSV；输出到用户选择目录。
   - 接入点：`CompareTabViewModel` 新增 `ExportReport`；`Services/CompareReportWriter.cs`。

6. **编码与大小保护**
   - 文本比对前可选择编码（自动/UTF-8/GBK/UTF-16）；超过 5MB 提示“仅显示差异摘要/用外部工具”。
   - 接入点：`TextDiffEngine`、`ReadTextSide`（:867）、`TextFileCodec`。

### 可选（P1/P2）

- 三方合并（左/右/基线）与冲突标记。
- 与 Git 集成（`git diff` 输出解析）。
- 目录同步预设/定时同步。
- 忽略空白/行尾差异开关。
- 会话保存（左右路径、过滤、滚动位置）。

### 不建议

- 默认自动同步/删除：必须先预览计划并确认。
- 在 UI 线程比对大目录：保持后台 + 取消。
- 自研完整 diff 算法：可复用现有引擎/LCS，先补功能。

## 4. 交互与视觉

- 顶部：左右路径、交换、重算、过滤、同步、导出。
- 目录表：名称、左状态、右状态、左大小/时间、右大小/时间；状态色（相同灰/不同黄/仅左蓝/仅右绿/较新橙）。
- 文件 diff：并排双栏，行号、差异行底色、行内字符高亮；上下差异按钮。
- 二进制：十六进制三栏（偏移/左/右），差异字节红色。
- 图片：并排 + 滑动分割线 + 差异热区。
- 同步计划：树形列表，复选框，底部“将复制 N/删除 M”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 忽略规则 | `DirectoryCompareEngine`、`CompareTabViewModel` | glob 匹配 |
| 同步 | `CompareTabViewModel`、`IFileOperationService` | 计划预览 |
| 二进制 | 新增 `Kernel/Services/BinaryCompareEngine.cs` | 分块比较 |
| 图片 | `ComparePanel.axaml(.cs)` | 复用图片控件 |
| 报告 | 新增 `Services/CompareReportWriter.cs` | HTML/CSV |
| 编码 | `TextDiffEngine`、`TextFileCodec` | 手动覆盖 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 同步 | 误删/覆盖，需强确认与备份 | M |
| 二进制/图片 | 大文件内存/渲染 | M |
| 三方合并 | 冲突算法复杂 | L |
| 报告 | 大量差异行 HTML 体积 | S/M |

## 7. 验收标准

1. 默认忽略 `.git/bin/obj/node_modules`，结果数量明显下降；自定义排除生效。
2. 差异行可复制到另一侧，执行前显示计划，完成后状态刷新。
3. 二进制文件显示首个差异偏移与十六进制差异。
4. 两张图片可并排/滑动对比，缩放同步。
5. 导出 HTML/CSV 可在浏览器/Excel 打开，内容与界面一致。
6. GBK 文本手动选编码后不再乱码；>5MB 文件有保护提示。
7. 目录比对可取消，取消后 1 秒内停止 IO。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [WinMerge](https://winmerge.org/) | 目录/文件/三方/报告 | 2026-11-03 |
| [Beyond Compare](https://www.scootersoftware.com/) | 文件夹同步/二进制/图片 | 2026-11-03 |
| [Meld](https://meldmerge.org/) | 三方合并 | 2026-11-03 |
| [VS Code Diff](https://code.visualstudio.com/docs/editor/versioncontrol#_diff-editor) | 行内 diff/并排 | 2026-11-03 |
| [Total Commander](https://www.ghisler.com/) | 同步目录 | 2026-11-03 |
