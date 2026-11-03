# 编码转换（encoding-switcher 相关）现代化设计

状态：设计稿（2026-11-03）
适用：`EncodeConvertDialog`、`FileNameEncodingConverter`、`TextFileCodec`、`ArchiveNameCodec`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 文件名编码转换 | `EncodeConvertDialog.axaml.cs`、`FileTabViewModel.PreviewEncodeConvert`（:653）、`ApplyBatchRenames`（:591） | 可用 | 源/目标编码下拉、递归、实时预览、原因列 |
| 编码列表 | `FileNameEncodingConverter.GetSupportedEncodings` | 可用 | 代码页选择（默认 GBK 936 → UTF-8 65001） |
| 文本内容自动探测 | `TextFileCodec.Decode`（:77） | 可用 | BOM → UTF-8 严格 → GBK → Latin-1 |
| 文本编码写回 | `ITextFileService` / `EditorFileAccess` | 可用 | IDE 保存按探测/选择编码 |
| 压缩包内文件名编码 | `ArchiveNameCodec`、`TarArchiveEngine` | 可用 | tar 的 GBK/UTF-8 处理（LC_ALL=C 语义，历史已总结） |
| 独立“encoding-switcher”模块 | 无 | 缺口 | 全仓库 Grep 无此命名；现有能力分散在文件名转换与文本编解码 |
| 文件内容转码 UI | 无 | 缺口 | 只能逐个在编辑器里改编码保存 |
| 冲突/回滚 | 半成品 | 半成品 | 预览有“无法转换”原因，但无自动改名、无回滚日志 |
| 自动检测建议 | 无 | 缺口 | 用户需自己选源编码 |

主要问题：

1. **命名易误解**：“编码转换”实际只改文件名，用户可能以为会转文件内容；反之亦然。
2. **无内容转码入口**：批量把 GBK 文本转为 UTF-8、统一换行符、控制 BOM，目前只能逐文件打开编辑器另存。
3. **无自动检测**：`FileNameEncodingConverter` 需要用户选源编码；选错会产生乱码文件名（预览可发现，但试错成本高）。
4. **无冲突策略**：转换后同名、目标已存在、非法字符时只标记原因，不能“自动追加序号/跳过/覆盖”。
5. **无回滚**：批量重命名中途失败会留下部分已改、部分未改的状态；没有操作日志/撤销。
6. **换行与 BOM 不可控**：内容写回按原编码，但没有“LF/CRLF/CR 统一”“BOM 添加/移除”选项。
7. **压缩包文件名编码无 UI**：tar/zip 内文件名乱码时，用户只能解压后转换再压缩。
8. **编码列表固定**：没有“最近使用/常用预设”，也没有系统代码页搜索。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| convmv | 专门转换文件名编码，支持递归、预览、`--notest` 试运行（[convmv](https://www.j3e.de/linux/convmv/)） | 文件名转换应明确“只改名字”，并提供 dry-run |
| iconv | 文件内容编码转换，支持 `//TRANSLIT`、BOM/换行由外部处理 | 内容转码独立工具 |
| Notepad++ / VS Code | “以编码重新打开/另存为编码”，支持 BOM、换行符 | 编辑器内应可见可切换 |
| Directory Opus / Total Commander | 批量重命名支持编码/字符集转换 | 与批量重命名整合 |
| 7-Zip | 压缩包内文件名编码选项（`-mcu`/代码页） | 压缩包场景需要编码参数 |
| Python `chardet` / `charset-normalizer` | 统计式编码检测 | 可作为“建议源编码” |

## 3. 推荐方案

### 必做（P0）

1. **明确拆分两个动作**
   - “文件名编码转换（encoding-switcher）”与“文件内容转码”分别入口、分别说明；不再共用一个模糊名称。
   - 接入点：`MainWindow.OnEncodeConvertClick`（:2448）、`FileListPanel` 右键菜单、`EncodeConvertDialog` 标题/说明。
   - 文案示例：“仅重命名，不修改文件内容”。

2. **源编码自动建议**
   - 对文件名：用统计检测（字节频率/常见 GBK 双字节区间）给出“可能为 GBK/UTF-8”建议；对内容：用 `chardet` 类算法或启发式。
   - 接入点：`FileNameEncodingConverter.BuildPlan` 增加 `SuggestSourceEncoding`；`TextFileCodec` 增加 `DetectEncoding`。
   - 注意：自动建议必须可覆盖，不能静默使用。

3. **冲突策略与原子性**
   - 预览表增加“目标冲突”列；支持策略：跳过 / 自动追加 `(1)` / 覆盖（需确认）。
   - 执行前写 `rename-journal.json`（旧路径→新路径），失败时按日志回滚；成功后保留 7 天。
   - 接入点：`FileTabViewModel.ApplyBatchRenames`、`EncodeConvertDialog`、新增 `Services/RenameJournal.cs`。

4. **内容转码对话框**
   - 选择文件/目录（递归）、目标编码、BOM（保留/添加/移除）、换行（保留/LF/CRLF）、输出（原地/另存目录）。
   - 先 dry-run 预览前 20 行 + 检测编码 + 预计影响文件数；确认后批量执行，进度进任务中心。
   - 接入点：新增 `Views/ContentTranscodeDialog.axaml(.cs)`、`Services/ContentTranscoder.cs`；复用 `ITextFileService`。
   - 安全：原地转码前自动备份到 `.tabtaskexplorer-backup/<timestamp>/` 或要求“另存目录”。

5. **压缩包文件名编码**
   - 解压/压缩时提供“文件名编码”选项；tar 保持现有 `LC_ALL=C` 语义，zip 使用 `-mcu` 或重写中央目录。
   - 接入点：`ArchiveOptions`、`SharpArchiveEngine`、`TarArchiveEngine`、`ArchiveNameCodec`。

### 可选（P1/P2）

- 编码预设（“GBK→UTF-8”“UTF-8 BOM 移除”“Windows 换行→LF”）。
- 批量转换图片/音频元数据编码（EXIF/IPTC/ID3）——与文件内容转码分开。
- 编码检测模型（ML）提升准确率。
- 转换报告导出 CSV。
- 与批量重命名合并为一个“批量工具”入口。

### 不建议

- 静默自动转码：必须预览 + 确认。
- 用文件名编码转换去“修复”文件内容乱码：两者不相关，应在 UI 明确区分。
- 无备份地原地覆盖：必须备份或另存。

## 4. 交互与视觉

- 文件名转换对话框：源编码、目标编码、递归、冲突策略；预览表列：原路径、新名称、检测建议、冲突、原因。
- 内容转码对话框：文件列表、目标编码、BOM/换行、输出位置、预览区（前 20 行）、开始按钮。
- 进度：任务中心显示“转码 12/100”，可取消；失败文件列表可导出。
- 结果：成功/跳过/失败三组统计；提供“打开备份目录/回滚”。
- 危险提示：原地覆盖时红色文字“将修改原文件，已自动备份到 …”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 拆分入口 | `MainWindow.axaml.cs`、`FileListPanel.axaml(.cs)` | 两个菜单项/两个对话框 |
| 自动建议 | `FileNameEncodingConverter.cs`、`TextFileCodec.cs` | 启发式，允许覆盖 |
| 冲突/回滚 | `FileTabViewModel.cs`、新增 `Services/RenameJournal.cs` | 日志落盘，失败回滚 |
| 内容转码 | 新增 `Views/ContentTranscodeDialog.axaml(.cs)`、`Services/ContentTranscoder.cs` | dry-run + 备份 |
| 压缩包 | `Kernel/Models/ArchiveOptions.cs`、`SharpArchiveEngine.cs`、`TarArchiveEngine.cs` | 编码参数透传 |
| 进度 | `IProgressCenter`（进度模块） | 批量转码接入 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 文件名转换 | 非法字符/保留名/大小写冲突；回滚失败 | M |
| 内容转码 | 误判编码导致二次损坏；大文件内存 | M |
| 备份 | 磁盘空间；清理策略 | S |
| 压缩包编码 | zip/tar 差异；7-Zip 兼容 | M/L |
| 自动检测 | 准确率有限，需明确“建议” | S/M |

## 7. 验收标准

1. “文件名编码转换”对话框明确写“仅重命名，不修改内容”，预览可 dry-run。
2. 源编码选错时预览显示乱码/原因，不执行；自动建议可一键采用。
3. 目标同名冲突按策略处理（跳过/追加序号/覆盖确认），执行后无重名。
4. 批量重命名中途失败可按 journal 回滚到原始名称。
5. 内容转码支持 GBK→UTF-8、BOM 添加/移除、CRLF↔LF；dry-run 预览前 20 行正确。
6. 原地转码自动备份；失败文件列入报告且原文件未损坏。
7. 压缩包解压时可选择文件名编码，中文名不再乱码。
8. 批量转码进度出现在任务中心，可取消。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [convmv](https://www.j3e.de/linux/convmv/) | 文件名编码转换、递归、试运行 | 2026-11-03 |
| [GNU iconv](https://www.gnu.org/software/libiconv/) | 内容编码转换能力 | 2026-11-03 |
| [VS Code 编码支持](https://code.visualstudio.com/docs/editor/codebasics#_file-encoding-support) | 重新打开/另存编码、BOM | 2026-11-03 |
| [7-Zip 命令行](https://7-zip.org/download.html) | 压缩包文件名代码页 | 2026-11-03 |
| [charset-normalizer](https://github.com/Ousret/charset_normalizer) | 编码检测思路 | 2026-11-03 |

注：`encoding-switcher` 在本仓库中不存在同名模块（Grep 无结果），本文按“文件名编码转换 + 文本编解码”现状整理，并将其作为该模块的正式命名建议。
