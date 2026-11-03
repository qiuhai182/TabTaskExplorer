# 压缩包 / 归档现代化设计

状态：设计稿（2026-11-03）
适用：`ArchiveService`、`SharpArchiveEngine`、`TarArchiveEngine`、`ArchiveFileSystemProvider`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 格式探测 | `ArchiveService.Probe`（:49-108）、`ArchiveFormats` | 可用 | 按扩展名 + 魔数校验 |
| 读取/列举 | `ListEntries`（:111-147） | 可用 | ZIP/7Z/RAR/TAR/GZ/BZ2/XZ/Zstd/Ace/Arj/Arc 等 |
| 解压 | `Extract`（:149）、`ExtractEntries`（:156） | 可用 | 进度回调、取消令牌、路径过滤 |
| 创建 | `Create`（:276-305） | 可用 | ZIP/7Z/TAR/单流压缩 |
| 修改 | `AddPaths`（:415）、`RemoveEntries`（:450）、`RenameEntry`（:463）、`AddDirectory`（:477） | 可用 | 仅可修改格式；内部重写 |
| 压缩包当目录 | `ArchiveFileSystemProvider` | 可用 | 虚拟标签，浏览/改名/删除复用 |
| 名称编码 | `ArchiveNameCodec`、`CollectSourceItems`（:596） | 可用 | tar GBK/UTF-8 处理 |
| 路径安全 | `ArchivePathFilter`、`ArchiveRootBoundary` | 可用 | 防目录穿越/越界 |
| 密码/加密 | 无 | 缺口 | 无密码输入、无加密创建 |
| 分卷压缩 | 无 | 缺口 | 无 split volumes |
| 压缩包测试 | 无 | 缺口 | 无“测试完整性” |
| 恢复记录 | 无 | 缺口 | 无 RAR recovery record |
| 压缩配置 UI | 半成品 | 半成品 | 创建时参数有限 |

主要问题：

1. **无密码支持**：加密 ZIP/7Z/RAR 无法打开；创建时不能设密码。
2. **无完整性测试**：压缩包损坏时只在解压中途报错，不能提前检测。
3. **压缩参数不可见**：压缩级别、固实压缩、分卷、排除规则没有 UI；用户只能用默认。
4. **大压缩包内存/性能**：部分实现可能整包重写；无“流式追加”提示。
5. **无恢复记录/分卷**：大文件备份场景不足。
6. **压缩包内编辑边界**：可改名/删除，但新增/拖入的 UI 不完整；压缩包内文件不能直接外部编辑。
7. **无压缩包预览**：图片/文本需先解压。
8. **无批量压缩任务**：多目录压缩没有队列/进度中心接入。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| 7-Zip | 7z/ZIP/TAR 等、加密、分卷、固实、测试、密码 | 密码/测试/分卷是标准能力 |
| WinRAR | RAR/ZIP、恢复记录、分卷、修复 | 恢复记录/修复 |
| Bandizip | 多格式、预览、分卷、密码 | 压缩包预览 |
| Windows 资源管理器 | ZIP 当文件夹、解压全部/所选 | 虚拟目录体验 |
| SharpCompress | .NET 多格式读写（[SharpCompress](https://github.com/adamhathcock/sharpcompress)） | 现有引擎 |

## 3. 推荐方案

### 必做（P0）

1. **密码支持**
   - 打开加密压缩包时弹密码框；创建时可设密码与加密算法（ZIP AES-256/7z AES）；密码不写入日志。
   - 接入点：`ArchiveOptions` 增加 `Password`；`SharpArchiveEngine`/`TarArchiveEngine`；`ArchiveService` 传递。

2. **完整性测试**
   - “测试压缩包”读取全部条目/CRC，输出损坏条目列表；大包可取消并显示进度。
   - 接入点：新增 `ArchiveService.Test`；`ArchiveFileSystemProvider` 右键菜单。

3. **压缩配置 UI**
   - 压缩对话框：格式、级别（存储/快速/标准/极限）、固实、分卷大小、密码、排除规则、是否保留路径。
   - 接入点：新增 `Views/CompressDialog.axaml(.cs)`；`FileTabViewModel.CompressSelection`（:806）。

4. **压缩包预览**
   - 选中条目显示文本前 N 行/图片缩略图/大小/CRC；不解压到磁盘（流式读取）。
   - 接入点：`ArchiveFileSystemProvider` 增加 `OpenRead`；`FileListPanel` 预览栏。

5. **任务中心接入**
   - 压缩/解压/测试作为 `ProgressTask`，支持取消、速度/ETA、失败重试。
   - 接入点：`IProgressCenter`、`ArchiveService` 进度回调。

6. **拖入/拖出压缩包**
   - 文件拖到压缩包标签 = 追加（可修改格式）；从压缩包拖出到本地 = 解压临时文件后复制。
   - 接入点：`FileDropPolicy`、`ArchiveService.AddPaths`；`MainWindow` 拖放。

### 可选（P1/P2）

- 分卷压缩/合并、恢复记录（依赖格式）。
- 压缩包修复（RAR/ZIP）。
- 压缩包内直接编辑文本（临时解压 + 保存回写，需冲突检测）。
- 批量压缩队列、压缩预设。
- 压缩包格式转换（ZIP↔7Z↔TAR）。

### 不建议

- 自研压缩算法：使用 SharpCompress/7-Zip。
- 默认极限压缩：CPU 时间长，默认“标准”。
- 在日志中记录密码/明文密钥。
- 无提示地整包重写大压缩包：必须显示进度与风险。

## 4. 交互与视觉

- 压缩对话框：格式下拉、级别滑块、密码（可选）、分卷、排除规则、预估大小。
- 压缩包标签：路径面包屑、条目表、工具栏“解压/测试/添加/删除/重命名”。
- 密码框：标题显示压缩包名，密码错误提示“密码错误或压缩包损坏”。
- 测试结果：成功/损坏条目数，损坏项可定位。
- 预览栏：文本前 50 行、图片缩略图、二进制十六进制前 256 字节。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 密码 | `ArchiveOptions`、`SharpArchiveEngine`、`TarArchiveEngine` | 不写日志 |
| 测试 | `ArchiveService`、`ArchiveFileSystemProvider` | CRC/条目遍历 |
| 压缩 UI | 新增 `CompressDialog`；`FileTabViewModel` | 参数透传 |
| 预览 | `ArchiveFileSystemProvider`、`FileListPanel` | 流式读取 |
| 任务中心 | `ArchiveService`、`IProgressCenter` | 进度/取消 |
| 拖放 | `FileDropPolicy`、`MainWindow` | 追加/解压 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 密码 | 格式/算法差异；密码错误与损坏难区分 | M |
| 测试 | 大包耗时/取消 | S/M |
| 预览 | 大文本/图片解码 | S |
| 分卷/恢复 | 格式支持有限 | L |
| 拖入 | 整包重写风险 | M |

## 7. 验收标准

1. 加密 ZIP/7Z/RAR 可输入密码打开；错误密码有明确提示。
2. 创建压缩包可设密码/级别/分卷；产物可被 7-Zip 正常打开。
3. “测试压缩包”能列出损坏条目，且可取消。
4. 压缩包内文本/图片可预览，不产生永久临时文件。
5. 压缩/解压/测试出现在任务中心，可取消并显示进度。
6. 拖文件到压缩包标签可追加（可修改格式），不可修改格式明确提示。
7. 路径穿越条目（`../`）被拒绝并记录。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [7-Zip](https://7-zip.org/) | 加密/分卷/测试/固实 | 2026-11-03 |
| [WinRAR](https://www.rarlab.com/) | 恢复记录/修复/分卷 | 2026-11-03 |
| [Bandizip](https://www.bandisoft.com/bandizip/) | 压缩包预览 | 2026-11-03 |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | .NET 多格式读写 | 2026-11-03 |
| [Windows 资源管理器 ZIP](https://support.microsoft.com/en-US/Windows/Experience/FileExplorer/file-explorer-in-windows) | ZIP 当文件夹 | 2026-11-03 |
