# 剪贴板现代化设计

状态：设计稿（2026-11-03）
适用：`ISystemClipboardService`、`AvaloniaSystemClipboardService`、`ClipboardPastePolicy`、文件复制/剪切
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 跨平台剪贴板写入 | `App/Services/AvaloniaSystemClipboardService.cs:22-118` | 可用 | 一次写入状态标记 + 文件列表 + 平台剪切标记 |
| 平台剪切标记 | `AvaloniaSystemClipboardService.cs:25-41` | 可用 | Windows `Preferred DropEffect`、GNOME、KDE |
| 读取与判定 | `Kernel/Services/ClipboardPastePolicy.cs:41-82` | 可用 | 状态标记 → 外部文件列表 → 内部剪贴板 |
| 文本写入 | `AvaloniaSystemClipboardService.WriteTextAsync`（:70-76） | 可用 | 网页地址等 |
| 清空 | `ClearAsync`（:87-118） | 可用 | 剪切落地后清空 |
| 内部剪贴板 | `MainWindowViewModel.SetClipboardPaths`（:1451）、`WriteToSystemClipboard` | 可用 | 系统剪贴板不可用时回退 |
| 图片剪贴板 | 无 | 缺口 | 截图只写文件路径，未写 `image/png` |
| 剪贴板历史 | 无 | 缺口 | 无历史记录、固定、搜索 |
| 富文本/HTML/RTF | 无 | 缺口 | 仅文件列表与纯文本 |
| 隐私控制 | 无 | 缺口 | 无密码/敏感内容过滤 |

主要问题：

1. **截图不能作为图片粘贴**：`ISystemClipboardService.WriteAsync(paths)` 只写文件列表；聊天工具/网页输入框得到“文件”而不是图片。
2. **无剪贴板历史**：复制过的路径/文本无法回溯；重复复制需要重新操作。
3. **内容类型单一**：不支持图片、HTML、RTF、URL 书签；`ClipboardContent` 只围绕文件路径。
4. **无隐私保护**：历史功能若加入，必须排除密码框/敏感应用；当前连“清空”都需手动。
5. **跨程序兼容测试不足**：Windows/macOS/Linux 各文件管理器的剪切标记已覆盖，但图片/文本格式未验证。
6. **无“粘贴为”**：粘贴文件时不能选择“复制/移动/创建快捷方式”；无“粘贴文本到当前目录为文件”。
7. **无大文件/大量文件提示**：复制 10GB 文件夹没有容量提示。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 剪贴板历史 | `Win+V` 历史、固定、同步、清除（[Microsoft 支持](https://support.microsoft.com/en-us/windows/using-the-clipboard-in-windows)） | 历史/固定/清除是标准体验 |
| Windows 剪贴板格式 | CF_HDROP、CF_DIB、HTML Format、RTF 等（[Clipboard Formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats)） | 图片/HTML 需要多格式写入 |
| Ditto / ClipboardFusion | 历史搜索、固定、分组、同步 | 历史管理 |
| Directory Opus | 复制/剪切队列、粘贴为、文件剪贴板 | “粘贴为”与队列 |
| macOS Finder | 文件复制、图片/文本多格式、通用剪贴板 | 多格式兼容 |

## 3. 推荐方案

### 必做（P0）

1. **图片剪贴板**
   - `ISystemClipboardService` 增加 `WriteImageAsync(byte[] png, string? filePath)`；Windows 写 `CF_DIB`/PNG + 文件列表，macOS/Linux 写图片 + 文件 URI。
   - 截图/贴图/标注保存后调用；设置项“复制图片/复制文件/两者”。
   - 接入点：`AvaloniaSystemClipboardService`、`CaptureCoordinator`、`PinnedImageWindow`。

2. **统一内容模型**
   - `ClipboardContent` 扩展为 `Files / Text / Image / Html / Rtf`，读取时按优先级选择；粘贴到文件视图只接受 `Files`，粘贴到编辑器接受 `Text`。
   - 接入点：`Kernel/Models/ClipboardContent.cs`、`ClipboardPastePolicy`、`MainWindowViewModel.PasteFromClipboardAsync`。

3. **剪贴板历史（可选开启）**
   - 记录最近 50 条（文本/图片/文件），支持固定、搜索、删除、清空；默认关闭，首次开启提示隐私风险。
   - 排除密码框内容（`TextBox.PasswordChar`）、标记为敏感的应用/窗口。
   - 接入点：新增 `Services/ClipboardHistoryService.cs`；`UiLogger` 不记录历史内容；设置页开关。

4. **“粘贴为”**
   - 右键粘贴菜单：粘贴（默认）/ 粘贴为快捷方式 / 粘贴并替换 / 粘贴文本为文件（P1）。
   - 接入点：`FileListPanel` 右键、`MainWindowViewModel` 粘贴逻辑、`FileDropPolicy.ResolveEffects`。

5. **容量与数量提示**
   - 复制/剪切前统计文件数与总大小；超过阈值（如 1GB 或 1000 项）提示确认。
   - 接入点：`MainWindowViewModel.CopyCheckedToClipboard/CutCheckedToClipboard`（:1348/1354）。

### 可选（P1/P2）

- 剪贴板历史跨设备同步（Windows 云剪贴板/自建同步），默认关闭。
- 图片 OCR/条码识别入口（复用 OCR 模块）。
- 历史内容预览与格式转换（图片转 PNG、HTML 转文本）。
- 拖放与剪贴板统一：拖拽源也可写入剪贴板。

### 不建议

- 默认开启剪贴板历史：隐私风险，必须 opt-in。
- 记录密码/令牌/验证码：即使本地也不应。
- 只支持文件路径：会持续造成“截图粘贴为文件”的体验问题。
- 自建跨平台剪贴板协议：优先系统能力。

## 4. 交互与视觉

- 复制/剪切后状态栏提示“已复制 3 项（12.4 MB）”。
- 粘贴菜单：粘贴、粘贴为快捷方式、粘贴到该文件夹（目录行）。
- 历史面板：侧边栏/弹窗，卡片显示类型图标、内容摘要、时间、固定按钮；搜索框。
- 图片历史：缩略图 120×80；文本历史：前两行。
- 隐私提示：首次开启历史时说明“将记录复制的文本/图片/文件路径，密码框内容不记录”。
- 清空：二次确认，提供“仅清空未固定项”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 图片剪贴板 | `ISystemClipboardService`、`AvaloniaSystemClipboardService` | Windows CF_DIB/PNG |
| 内容模型 | `Kernel/Models/ClipboardContent.cs`、`ClipboardPastePolicy` | 类型优先级 |
| 历史 | 新增 `Services/ClipboardHistoryService.cs`、`Views/ClipboardHistoryWindow.axaml` | 本地存储、加密可选 |
| 粘贴为 | `FileListPanel.axaml(.cs)`、`MainWindowViewModel` | 复用文件操作 |
| 容量提示 | `MainWindowViewModel` 复制/剪切入口 | 后台统计 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 图片格式 | 各平台 DIB/PNG 差异、大图内存 | M |
| 历史 | 隐私、存储增长、敏感内容识别 | M |
| 内容模型 | 现有调用方全部按路径处理 | M |
| 粘贴为 | 与拖放效果语义一致 | S/M |

## 7. 验收标准

1. 截图后可在聊天工具/网页输入框直接粘贴为图片；资源管理器粘贴为文件。
2. 复制 1000 个文件/1GB 前有容量提示，可取消。
3. 剪贴板历史默认关闭；开启后记录文本/图片/文件，密码框内容不记录。
4. 历史支持固定、搜索、删除、清空未固定项；重启后保留。
5. 文件视图粘贴只接受文件；编辑器粘贴接受文本；类型不匹配时有明确提示。
6. “粘贴为快捷方式”在 Windows 生成 `.lnk`，其他平台生成符号链接或提示不支持。
7. 剪切落地后系统剪贴板清空，与 Explorer 行为一致。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows 剪贴板历史](https://support.microsoft.com/en-us/windows/using-the-clipboard-in-windows) | 历史/固定/清除 | 2026-11-03 |
| [Windows Clipboard Formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats) | CF_HDROP/DIB/HTML/RTF | 2026-11-03 |
| [Ditto](https://ditto-cp.sourceforge.io/) | 历史搜索/固定 | 2026-11-03 |
| [Directory Opus 手册](https://docs.dopus.com/) | 复制队列/粘贴为 | 2026-11-03 |
| [macOS 通用剪贴板](https://support.apple.com/en-us/HT209460) | 跨设备/多格式 | 2026-11-03 |
