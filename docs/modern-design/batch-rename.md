# 批量重命名现代化设计

状态：设计稿（2026-11-03）
适用：`BatchRenameDialog`、`BatchRenamePlanner`、`FileTabViewModel.ApplyBatchRenames`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 查找替换 | `BatchRenamePlanner.BuildNewName`（:42-54） | 可用 | 文件名主体内替换 |
| 前后缀 | `BatchRenameOptions.PrefixText/SuffixText`（:15-18） | 可用 | 加在主体前后 |
| 序号 | `UseSequence/SequenceStart`（:21-24） | 可用 | 追加 `(N)` |
| 扩展名修改 | `NewExtension`（:27）、`BuildNewName`（:59-69） | 可用 | 自动补 `.` |
| 实时预览 | `BatchRenameDialog.axaml(.cs)` | 可用 | 预览新旧名称 |
| 执行 | `FileTabViewModel.ApplyBatchRenames`（:591） | 可用 | 按深度排序后重命名 |
| 正则 | 无 | 缺口 | 只有普通字符串替换 |
| 大小写转换 | 无 | 缺口 | 无大写/小写/首字母大写 |
| 序号补零/位置 | 无 | 缺口 | 只能追加 `(N)`，无 `001`、无“前置/替换原名” |
| 日期/时间模板 | 无 | 缺口 | 无 `{yyyyMMdd}`、文件时间 |
| 冲突检测 | 半成品 | 半成品 | 预览可能提示，但无自动解决策略 |
| 撤销 | 无 | 缺口 | 执行后不能一键还原 |
| 元数据改名 | 无 | 缺口 | 无 EXIF/ID3/标题 |

主要问题：

1. **规则能力弱**：只有查找替换/前后缀/序号/扩展名，无法做“正则捕获组”“大小写规范”“日期前缀”。
2. **序号不灵活**：只能追加 `(1)`，不能补零、指定起始/步长、放在前面或替换原名。
3. **冲突无策略**：目标已存在、大小写仅变更、非法字符时只能逐个失败；没有“自动追加序号/跳过/覆盖”统一策略。
4. **无撤销**：批量改名后无法一键恢复；没有 rename journal。
5. **无预览校验**：非法名称（保留名、结尾点、超长）没有提前标红。
6. **无元数据/日期**：不能按拍摄日期、音乐标签、文档标题命名。
7. **无预设/收藏**：常用规则每次重配。
8. **Web 端能力有限**：网页端批量改名只支持简单文本计划。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Directory Opus Advanced Rename | 通配符/正则、宏、大小写、编号、元数据、脚本、预览（[手册](https://docs.dopus.com/doku.php?id=file_operations:renaming_files:advanced_rename)） | 规则化 + 预览 + 宏 |
| Total Commander Multi-Rename | 查找替换、计数器、大小写、扩展名、正则 | 计数器位置/补零 |
| Bulk Rename Utility | 多面板规则、日期/序号、预览 | 规则组合 |
| ExifTool | 按 EXIF/ID3 元数据重命名 | 元数据模板 |
| PowerRename (PowerToys) | 正则、预览、撤销 | 正则 + 撤销 |

## 3. 推荐方案

### 必做（P0）

1. **规则扩展**
   - 正则查找替换（捕获组 `$1`）、大小写（全大写/全小写/首字母大写/驼峰）、序号补零/步长/位置、日期模板（文件修改时间/当前时间）。
   - 接入点：`BatchRenameOptions` 扩展；`BatchRenamePlanner.BuildNewName`；`BatchRenameDialog` 规则面板。

2. **冲突与非法名校验**
   - 预览标红：目标已存在、保留名（CON/NUL）、非法字符、结尾空格/点、超长；提供策略：跳过/自动追加 `(1)`/覆盖（二次确认）。
   - 接入点：新增 `Kernel/Services/FileNameValidator.cs`；`BatchRenamePlanner`、`BatchRenameDialog`。

3. **撤销（rename journal）**
   - 执行前写 `rename-journal.json`（旧→新），失败自动回滚；成功后保留 7 天，支持“撤销上次批量改名”。
   - 接入点：`FileTabViewModel.ApplyBatchRenames`；新增 `Services/RenameJournal.cs`。

4. **预设**
   - 保存常用规则为预设（名称 + 规则），支持导入导出。
   - 接入点：`AppSettings.BatchRenamePresets`；`BatchRenameDialog`。

5. **预览增强**
   - 显示序号、冲突、非法原因、扩展名变化；支持按列排序/过滤“仅冲突”。
   - 接入点：`BatchRenameDialog` 预览表。

### 可选（P1/P2）

- 元数据模板：EXIF 拍摄日期、相机型号、ID3 标题/艺术家、PDF 标题。
- 随机名/UUID、哈希前缀。
- 批量改扩展名映射表。
- 与编码转换模块联动（先转文件名编码再改名）。
- Web 端完整规则支持。

### 不建议

- 无预览直接执行：必须预览。
- 覆盖同名文件不备份：默认跳过/追加，覆盖需强确认。
- 在 UI 线程重命名大量文件：后台执行 + 进度。

## 4. 交互与视觉

- 对话框：左侧规则区（查找替换/正则/大小写/序号/日期/扩展名），右侧预览表（原名→新名/状态）。
- 规则区用可折叠分组，避免一次展示过多选项。
- 预览：冲突红色、非法橙色、正常绿色；底部“将重命名 N 项，跳过 M 项”。
- 执行：进度条 + 取消；完成后“撤销”按钮保留 7 天。
- 预设：顶部下拉“载入预设/另存为预设”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 规则 | `BatchRenamePlanner.cs`、`BatchRenameOptions` | 正则/大小写/序号/日期 |
| 校验 | 新增 `Kernel/Services/FileNameValidator.cs` | 保留名/非法字符/长度 |
| 撤销 | 新增 `Services/RenameJournal.cs`；`FileTabViewModel.ApplyBatchRenames` | 失败回滚 |
| 预设 | `AppSettings`、`BatchRenameDialog` | JSON |
| 元数据 | `BatchRenamePlanner`（P1） | EXIF/ID3 读取 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 正则 | 灾难性回溯；非法替换 | S/M |
| 冲突/回滚 | 部分成功后回滚失败 | M |
| 元数据 | 第三方解析库/编码 | M |
| 预览性能 | 大量文件渲染 | S |

## 7. 验收标准

1. 正则捕获组替换、大小写、序号补零/位置、日期模板均生效。
2. 预览标红非法名/保留名/冲突，执行时按策略处理。
3. 批量改名失败可按 journal 回滚；成功后 7 天内可撤销。
4. 预设可保存/载入/导入导出。
5. 1000 个文件预览 < 1 秒，执行有进度可取消。
6. 扩展名修改不会产生双点或丢失原名。
7. 与编码转换联动时不产生乱码文件名。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Directory Opus Advanced Rename](https://docs.dopus.com/doku.php?id=file_operations:renaming_files:advanced_rename) | 正则/宏/元数据/预览 | 2026-11-03 |
| [Total Commander](https://www.ghisler.com/) | 多重重命名/计数器 | 2026-11-03 |
| [Bulk Rename Utility](https://www.bulkrenameutility.co.uk/) | 规则组合/日期/序号 | 2026-11-03 |
| [PowerRename](https://learn.microsoft.com/en-us/windows/powertoys/powerrename) | 正则/预览/撤销 | 2026-11-03 |
| [ExifTool](https://exiftool.org/) | 元数据重命名 | 2026-11-03 |
