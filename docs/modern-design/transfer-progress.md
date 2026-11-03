# 下载 / 进度中心现代化设计

状态：设计稿（2026-11-03）
适用：`RemoteTransferPanelViewModel`、`RemoteTransferJob`、`RemoteTransferServiceBase`、`DiskScanService`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 远程上传/下载队列 | `RemoteTransferPanelViewModel`（:138-201）、`RemoteTransferJob` | 可用 | 方向、远端/本地路径、总字节、已传字节、状态 |
| 暂停 / 继续 / 取消 | `RemoteTransferPanelViewModel.Pause/Resume/Cancel`（:166-183） | 可用 | SFTP/FTP 共用 |
| 断点续传 | `RemoteTransferServiceBase`、`SftpTransferService`、`FtpTransferService` | 可用 | README 标注已实现 |
| 文件夹递归传输 | 传输服务基类 | 可用 | 失败重试 3 次/间隔 3 秒 |
| 进度显示 | `TransferJobRow.Refresh`（:83） | 可用 | 行内进度，无全局汇总 |
| 磁盘扫描进度 | `DiskViewPanelViewModel.OnScanProgress`（:558） | 可用 | 文本进度，无取消 |
| 文件复制/移动进度 | 无 | 缺口 | 粘贴/拖放无进度与取消 |
| 通用 HTTP(S) 下载 | 无 | 缺口 | 只能通过浏览器/外部工具 |
| 统一任务中心 | 无 | 缺口 | 传输、扫描、压缩各自独立 |
| 任务栏进度 / 完成通知 | 无 | 缺口 | 用户切走后无感知 |

主要问题：

1. **进度孤岛**：远程传输有进度，文件复制/移动、压缩/解压、磁盘扫描、OCR 批处理没有统一模型与 UI。
2. **无速度/剩余时间**：只有已传字节/总数，用户不知道还要多久。
3. **无全局任务中心**：多个连接、多个标签的传输队列分散；关闭标签后任务状态不可见。
4. **无任务栏/托盘反馈**：长时间复制或下载时切到别的程序，完成/失败无通知。
5. **无并发/限速**：不能限制同时传输数、带宽、单文件重试策略。
6. **无 HTTP 下载**：产品定位是资源管理器，用户常需“粘贴 URL 下载文件”，目前只能开浏览器。
7. **错误分类弱**：网络断开、权限、磁盘满、文件被占用都归为“失败”，缺少可操作建议。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 文件资源管理器 | 复制/移动进度对话框、冲突处理、暂停/取消 | 文件操作应有系统级进度体验 |
| Internet Download Manager / Free Download Manager | 多线程分段下载、断点续传、限速、队列、浏览器集成 | HTTP 下载的基准能力 |
| Xftp / WinSCP | 传输队列、断点续传、失败重试、速度/剩余时间 | 现有 SFTP/FTP 队列已接近，补速度/ETA/汇总 |
| Windows 任务栏 | `ITaskbarList3.SetProgressValue/SetProgressState` 显示进度 | 长任务应有任务栏进度 |
| Everything | 索引与快速搜索 | 下载完成后可立即索引/定位 |

## 3. 推荐方案

### 必做（P0）

1. **统一进度中心**
   - 内核新增 `IProgressCenter` 与 `ProgressTask`：
     - 字段：Id、标题、类别（Transfer/FileOperation/Scan/Archive/Ocr/Download）、状态（Queued/Running/Paused/Completed/Failed/Canceled）、当前/总量、速度、ETA、源/目标、错误、可暂停/可取消。
     - 方法：`Register`、`Update`、`Complete`、`Fail`、`Cancel`、`Pause`、`Resume`、`Subscribe`。
   - 现有 `RemoteTransferJob` 作为适配器接入；新增 `FileOperationService`、`DiskScanService`、`ArchiveService` 也接入。
   - 接入点：新增 `Kernel/Abstractions/IProgressCenter.cs`；`App/Services/ProgressCenter.cs`；`RemoteTransferPanelViewModel` 改为从中心读取。
   - UI：底部状态栏“任务中心”按钮 + 弹出面板（运行中/已完成/失败三组）。

2. **速度与 ETA**
   - `ProgressTask` 每秒采样已传字节，滑动窗口计算速度；ETA = 剩余/速度。
   - UI 显示“12.3 MB/s · 剩余 00:42”。
   - 接入点：`ProgressCenter`、`TransferJobRow`。

3. **任务栏进度与通知**
   - Windows `ITaskbarList3` 设置主窗口进度；完成/失败用托盘气泡或 toast。
   - 接入点：`Platform.Windows/WindowsTaskbarProgress.cs`；`App.axaml.cs` 托盘；`MainWindow` 初始化注册。
   - 多任务时显示总进度（加权）；全部完成清除。

4. **文件操作接入**
   - `IFileOperationService`（见 `file-folder-view.md`）向进度中心报告每个文件/字节进度，支持取消。
   - 接入点：`MainWindowViewModel.PasteFromClipboardAsync`、`TransferPathsToDirectory`。

5. **错误分类与重试**
   - 统一错误码：网络、认证、权限、磁盘满、文件占用、路径过长、目标已存在。
   - 每个任务显示“重试 / 打开目标 / 复制错误信息”。
   - 接入点：`ProgressTask.ErrorKind`；传输/文件操作服务映射。

### 可选（P1/P2）

- HTTP(S) 下载管理器：
  - 入口：地址栏粘贴 URL、右键“下载链接”、新建下载对话框。
  - 能力：重定向、Range 断点续传、并发分段（可关）、Cookie/Authorization、限速、文件名从 `Content-Disposition` 推断。
  - 接入点：新增 `Kernel/Abstractions/IDownloadService.cs`、`App/Services/HttpDownloadService.cs`；文件视图新增“下载到当前目录”。
- 传输并发与限速设置（全局/每连接）。
- 下载完成后自动打开所在目录/加入索引。
- 任务历史持久化（重启后保留已完成记录，运行中任务恢复需谨慎）。

### 不建议

- BitTorrent/磁力链接：与产品定位不符，安全和合规成本高。
- 浏览器扩展集成（接管浏览器下载）：跨平台与商店审核复杂。
- 多线程分段作为默认：对服务器压力大，先做单流断点续传 + 可选分段。

## 4. 交互与视觉

- 状态栏右侧“任务中心（3）”按钮，运行时显示旋转图标；点击弹出面板。
- 任务行：图标（上传/下载/复制/扫描）、标题、进度条、速度/ETA、暂停/取消。
- 完成任务 3 秒后自动折叠到“已完成”，失败任务保持展开并红色标记。
- 任务栏进度：不确定总量时用 `Indeterminate`。
- 通知：完成“截图已保存/下载完成”，失败“下载失败：磁盘空间不足”，点击跳转。
- 下载对话框：URL、保存目录、文件名、Referer/Cookie（高级）、立即/排队。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 进度中心 | 新增 `Kernel/Abstractions/IProgressCenter.cs`、`App/Services/ProgressCenter.cs` | 线程安全，事件合并到 UI 线程 |
| 传输适配 | `RemoteTransferPanelViewModel.cs`、`Kernel/Services/RemoteTransferServiceBase.cs` | 不破坏现有暂停/续传 |
| 任务栏 | 新增 `Platform.Windows/WindowsTaskbarProgress.cs`；`App.axaml.cs` | 非 Windows 空实现 |
| 文件操作 | 新增 `IFileOperationService`（见文件视图） | 与进度中心共用取消令牌 |
| HTTP 下载 | 新增 `IDownloadService`、`HttpDownloadService`（P1） | Range/重定向/限速 |
| 通知 | `App.axaml.cs` 托盘 | 失败不自动消失 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 进度中心 | 多线程更新、事件风暴、UI 行数膨胀 | M |
| 任务栏 | Win32 互操作、窗口句柄生命周期 | S |
| 文件操作接入 | 复制/移动进度精度（字节 vs 文件数） | M |
| HTTP 下载 | 重定向/证书/断点/限速边界 | M/L |
| 任务恢复 | 半完成文件校验与清理 | M |

## 7. 验收标准

1. 远程传输、文件复制、磁盘扫描都出现在同一任务中心，状态/进度/速度一致。
2. 任务中心可暂停/继续/取消；取消后 2 秒内停止并释放句柄。
3. Windows 任务栏显示总进度；全部完成/失败后状态清除。
4. 完成/失败通知可点击跳转到目标目录或任务详情。
5. 网络断开、权限拒绝、磁盘满分别显示不同错误文案与建议。
6. HTTP 下载支持 Range 续传：中断后继续，文件大小与服务器一致。
7. 关闭传输标签后任务仍在中心可见（不丢失）。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| Xftp（NetSarang 产品，传输队列/续传/速度） | 传输队列、断点续传、速度 | 2026-11-03（官网抓取为空，仅作产品形态对照） |
| [WinSCP 文档](https://winscp.net/eng/docs/start) | 传输队列与续传 | 2026-11-03 |
| [Windows 任务栏进度 API（ITaskbarList3）](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-itaskbarlist3) | 任务栏进度显示 | 2026-11-03 |
| [Everything SDK](https://www.voidtools.com/en-us/support/everything/sdk/) | 下载完成后索引/定位 | 2026-11-03 |

注：IDM/FDM 的具体实现细节未在官方文档完整披露，本文只采用其公开产品能力作为对照。
