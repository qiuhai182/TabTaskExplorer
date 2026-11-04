# Web 能力对齐 + 统一任务中心（P0-2 续）

## 1. 目标

### 1.1 Web 端与桌面端能力对齐（必须）

1. **行内缩略图**：Web 文件列表/磁贴按需显示 96 档缩略图，走 `GET /api/thumb?path=…&size=96`，返回 PNG 并带缓存头；未命中或失败时保留类型图标。
2. **列配置共用**：显示/隐藏列、列宽、排序读写同一份 `AppSettings`（`FileVisibleColumns` / `FileColumnWidths` / `FileCompactMode`，新增 `FileSortField` / `FileSortAscending`）。桌面端或 Web 端任一处修改，另一端在下次快照/保存后生效。
3. **快速预览**：Web 端「预览」按钮、右键「预览」、空格键均可打开右侧预览层，支持图片、文本、PDF 提示、音视频占位；Esc 关闭。分类与文本读取复用桌面公共方法，不在 Web 重写一套判定。

### 1.2 统一任务中心（四步，本轮不做压缩/解压/扫描）

1. 本地复制/移动/删除提交到同一任务模型。
2. 冲突策略：跳过 / 覆盖 / 重命名 / 询问（询问在 Web 端弹出四选一；桌面端本轮无询问 UI 时按重命名兜底并记录）。
3. 取消：运行中可取消；复制取消时清理未完成的临时目标文件。
4. 重试：失败项可重试（生成新任务，仅包含失败项）。
5. 远程已有传输队列映射到同一任务模型，不重写远程传输实现；压缩/解压/扫描保持现状。

## 2. 现状与结论（已核对）

- `ThumbnailService` 已支持 96/192/384 磁盘缓存、图片/视频首帧/字体、PDF 回退图标，可直接复用。
- `QuickLookWindow` 的 `DetectKind`、`IsImage`、`ReadTextPreview` 是私有实现，Web 无法复用；需要抽公共分类器。
- `WebServiceHost` 已有统一认证、CSRF、路径白名单 `CheckPath`、`WriteBytes`（支持 Cache-Control），适合新增 `/api/thumb`、`/api/preview`、`/api/filecolumns`。
- `AppSettings` 已有三个文件列表配置；`FileTabViewModel` 内部持有规范化后的 `columnWidths`，Web 直接改设置不会刷新桌面控件，必须经桥接方法应用到所有文件标签。
- `DebugUiState` 顶层只有主题/隐藏/扩展名；`DebugFileRow` 无属性与预览分类字段；`TabSnapshotBuilder.FillFile/FillSearch` 是桌面与 Web 共用快照入口。
- Web 的 `tableColWidths` 仅存在页面内存，`colresize` 动作没有落盘；`viewMenu` 无列设置；文件列表无缩略图；空格当前用于勾选。
- `WebPageHtml.cs` 是静态资源缺失时的内联回退页，与 `Assets/web/*` 重复。本轮不双改内联页，主路径使用嵌入静态资源；回退页缺少新功能属于已知限制。
- 任务中心尚未开始；远程已有 `RemoteTransferPanelViewModel` 队列，可做只读映射/适配。

## 3. 设计决策

### 3.1 公共预览分类器（新文件）

`src/TabTaskExplorer.App/Services/FilePreviewClassifier.cs`

```csharp
public enum FilePreviewKind { Other, Image, Text, Pdf, Audio, Video }

public static class FilePreviewClassifier
{
    public static FilePreviewKind Detect(string path);
    public static bool IsImage(string path);
    public static string ReadTextPreview(string path); // 512 KB 上限 + GBK 回退 + 截断提示
    public static string DescribeUnsupported(FilePreviewKind kind); // PDF/音视频/其他提示
}
```

- 分类扩展名取 `QuickLookWindow` 与 `ThumbnailService` 的并集（视频含 `.mpg/.mpeg`）。
- `QuickLookWindow` 删除私有 `DetectKind`/`IsImage`/`ReadTextPreview`/`QuickLookKind`，改用公共枚举与方法，行为不变。

### 3.2 新 API

均要求已认证会话；GET 不要求 CSRF；路径必须通过 `CheckPath` 白名单。压缩包内路径不生成缩略图/不读本地预览，按类型提示。

1. `GET /api/thumb?path=…&size=96|192|384`
   - `ThumbnailService.GetThumbnailPathAsync(path, size, CancellationToken)`；`size` 缺省 96，越界取最近档位。
   - 成功：`image/png`，`Cache-Control: private, max-age=600`。
   - 不支持/失败/远程路径：`404 text/plain`，前端保留图标。
   - 不要求修改/增删权限。
2. `GET /api/preview?path=…`
   - 返回 JSON：`{ kind, name, sizeText, modifiedText, text, hint, local, supportsThumbnail }`。
   - 本地文件：`FilePreviewClassifier.Detect` + `ReadTextPreview`；图片/音视频/PDF 返回对应提示。
   - 远程/压缩包：分类用同一方法；文本走 `WebWorkspace.ReadFileText` 并截断 512 KB；`local=false`。
3. `GET /api/filecolumns`
   - `{ visibleColumns: "Name,Modified,Type,Size,Attributes", columnWidths: "30,26,480,140,130,110,100", compactMode: false, sortField: "Name", sortAscending: true }`
4. `POST /api/filecolumns`（JSON 同上，可选字段）
   - 校验列标识仅允许 `Name/Modified/Type/Size/Attributes`，宽度最多 7 段、`0..4000`，紧凑/排序布尔。
   - 应用后保存一次；返回 `ok`。
5. 任务中心：
   - `GET /api/tasks`：`{ tasks: [...] }` 快照。
   - `POST /api/tasks`：`{ kind: "copy|move|delete", sources: [...], destination: "...", policy: "skip|overwrite|rename|ask" }`。
   - `POST /api/tasks/{id}/cancel`、`.../retry`。
   - `POST /api/tasks/{id}/conflict`：`{ action: "skip|overwrite|rename|cancel", applyToAll: bool }`。

### 3.3 设置共享与刷新

- `AppSettingsStore.Save` 增加静态写锁，写入临时文件后替换，避免桌面/Web 并发写半截 JSON。
- `AppSettings` 新增 `FileSortField = "Name"`、`FileSortAscending = true`。
- `FileTabViewModel` 新增：
  - `public void ApplyColumnConfiguration(string visibleColumns, string columnWidths, bool compactMode, bool persist)`
  - `public void ApplySortConfiguration(string field, bool ascending, bool persist)`
  - 构造时按设置恢复排序；`SetSortField`/`IsCompactMode`/`ApplyColumnWidths` 统一走这两个方法。
- `IWebWorkspaceBridge` 新增：
  - `FileColumnConfig GetFileColumnConfig(string workspaceId)`（无 workspace 走桌面）
  - `void ApplyFileColumnConfig(string workspaceId, FileColumnConfig config)`
  - `FileTaskSnapshot[] GetFileTasks(string workspaceId)`
  - `string StartFileTask(string workspaceId, FileTaskRequest request)`
  - `bool CancelFileTask(string workspaceId, string id)`
  - `bool RetryFileTask(string workspaceId, string id)`
  - `bool ResolveFileTaskConflict(string workspaceId, string id, string action, bool applyToAll)`
- `MainWindowViewModel`：对全部文件标签应用，`settingsStore.Save` 一次。
- `WebWorkspace`：对工作区内全部文件标签应用，保存一次；`BuildState` 填充 `state.FileVisibleColumns/FileColumnWidths/FileCompactMode/FileSortField/FileSortAscending`，桌面快照同样填充。

### 3.4 Web 前端

- `views.js`
  - `renderTable` 按 `state.fileVisibleColumns` 生成名称/修改日期/类型/大小/属性列；列宽取 `state.fileColumnWidths`（勾选+图标两列合并为第一列，宽度=前两段之和），隐藏列宽 0；紧凑模式加 `compact` 类。
  - 名称单元格：图标 + `<img class="rowThumb" data-thumb data-size="96">`；`iconCategory` 为 `image/video/font` 才创建。
  - `renderTiles` 同样挂缩略图，尺寸档位按视图模式 192/384。
  - `hydrateThumbnails()`：IntersectionObserver 懒加载；`fetch` 带 Bearer，blob URL 缓存 `Map(path|size)`；加载成功隐藏图标，失败移除 img。
- `actions.js`
  - 右键菜单新增「预览」（单文件）。
  - 列设置菜单勾选状态、切换调用 `saveFileColumns()`；列宽拖动结束改为 `saveFileColumns(widths)`（不再调用无落盘的 `colresize`）。
  - 新增 `previewPath(path)`、`closePreview()`、`renderPreview(data)`；文本/图片/PDF/音视频按 kind 渲染；图片用 384 缩略图。
  - 复制/移动/删除入口改为提交 `/api/tasks`；保留旧 `/api/action` 作为远程不兼容时的回退。
- `boot.js`
  - 空格：`focusIndex >= 0` 时打开预览（不再切换勾选；勾选保留点击与 Ctrl+A）。
  - Esc：优先关闭预览层。
- `index.html` / `WebPageHtml.cs`
  - 工具栏新增「预览」；查看菜单新增列设置与紧凑模式；新增预览浮层与任务中心浮层。
  - `WebPageHtml.cs` 本轮标注为回退页不保证新功能（不复制整套新逻辑）。
- `app.css`
  - `.rowThumb`（16×16 行内、圆角、占位隐藏）、`.compact` 行高、`.previewPane`（右侧固定 420px，窄屏全宽）、`.taskPanel`、状态色。
- 任务中心浮层：列表显示类型、源、目标、进度、状态、消息；运行中显示取消；失败显示重试；`ask` 冲突弹四选一与「全部应用」。

### 3.5 任务中心核心（新目录 `Services/Tasks/`）

```csharp
public enum FileTaskKind { Copy, Move, Delete }
public enum FileTaskState { Queued, Running, WaitingForConflict, Completed, Failed, Canceled }
public enum FileConflictPolicy { Skip, Overwrite, Rename, Ask }
public sealed class FileTaskRequest { ... }
public sealed class FileTaskSnapshot { Id, Kind, IsRemote, State, Total, Processed, Failed, Progress, SourceText, DestinationText, Message, CanCancel, CanRetry, ConflictPath }
public sealed class FileTaskCenter
{
    string Submit(FileTaskRequest request);
    bool Cancel(string id);
    bool Retry(string id);
    bool ResolveConflict(string id, string action, bool applyToAll);
    IReadOnlyList<FileTaskSnapshot> Snapshot();
}
public static class LocalFileTaskExecutor
{
    internal static Task RunAsync(FileTaskEntry entry, CancellationToken token);
}
```

- `FileTaskCenter` 每实例一个后台队列（`Channel` 或 `SemaphoreSlim` 串行），任务串行执行，UI 线程外运行。
- 本地执行：`File.Copy`/目录递归复制、`File.Move`/`Directory.Move`、`File.Delete`/`Directory.Delete`。冲突逐项判定：
  - Skip：计数跳过；Overwrite：覆盖；Rename：自动 `name (1).ext`；Ask：状态 `WaitingForConflict` 并等待 `ResolveConflict`，超时 10 分钟按跳过。
  - 取消：检查 `token`；复制删除半成品目标文件；已移动项不回滚，摘要注明。
  - 重试：失败项快照新建任务。
- 远程映射：`RemoteTaskAdapter` 读取 `RemoteTransferPanelViewModel` 队列条目的公开状态，映射为 `FileTaskSnapshot`（`IsRemote=true`）；取消/重试仅在该队列已有公开方法时转发，不重构远程传输。
- 桌面/Web 双入口：`MainWindowViewModel` 与 `WebWorkspace` 各持有 `FileTaskCenter`；`IWebWorkspaceBridge` 按 workspace 选择实例。

## 4. 改动文件清单

| 文件 | 改动 |
| --- | --- |
| `src/.../Services/FilePreviewClassifier.cs` | 新增公共分类、文本预览、提示文案 |
| `src/.../Views/QuickLookWindow.cs` | 改用公共分类器，删除私有判定 |
| `src/.../Services/WebServiceHost.cs` | 新增 thumb/preview/filecolumns/tasks 路由与处理 |
| `src/.../Services/DebugUiModels.cs` | 顶层列/排序字段；行属性；任务快照 |
| `src/.../Services/TabContract.cs` | 填充属性列与预览分类 |
| `src/.../Services/AppSettingsStore.cs` | 新排序字段；Save 加锁原子写 |
| `src/.../Services/WebWorkspace.cs` | 实现桥接新方法；快照填充列配置；持有任务中心 |
| `src/.../ViewModels/MainWindowViewModel.cs` | 实现桥接；应用列配置到全部文件标签；持有任务中心 |
| `src/.../ViewModels/FileTabViewModel.cs` | 公共应用方法；排序设置持久化；构造恢复 |
| `src/.../Services/Tasks/*.cs` | 新增任务中心、本地执行器、远程适配 |
| `Assets/web/index.html` | 预览按钮、列设置、预览层、任务中心 |
| `Assets/web/views.js` | 列渲染、缩略图、任务中心渲染 |
| `Assets/web/actions.js` | 预览、列设置、任务提交、冲突 |
| `Assets/web/boot.js` | 空格预览、Esc |
| `Assets/web/app.css` | 缩略图、紧凑、预览层、任务面板 |
| `Services/WebPageHtml.cs` | 仅注明回退限制（不复制新逻辑） |

硬约束：不改 `FileListPanel.axaml.cs` 右键菜单函数、`Services/FileDropPolicy.cs`、`Services/Capture/*`、`Platform.Windows/WindowsScreenCaptureService.cs`。`FileListPanel.axaml.cs` 如需接线任务中心，只做最小增量并先重读。

## 5. 验证

### 5.1 浏览器优先（Chrome DevTools MCP，`http://127.0.0.1:15919/`）

1. 首页/静态资源/`/api/version` 正常，控制台无新错误。
2. 打开含图片目录：`/api/thumb?size=96` 返回 200 `image/png` 且 `Cache-Control: private, max-age=600`；图片渲染，失败行保留图标；滚动时按需加载。
3. 列设置：隐藏「大小」后表头消失；`/api/filecolumns` 与 `/api/state` 的 `fileVisibleColumns` 一致；拖动列宽后 GET 返回新宽度；紧凑模式行高变化。
4. 预览：工具栏/右键/空格打开；图片、文本（512 KB 提示）、PDF 提示、音视频占位均正确；Esc 关闭；切换文件复用同一层。
5. 任务中心：在 `D:\TabTaskExplorer\.trae-qa\` 内复制/移动/删除；冲突 skip/overwrite/rename/ask 分别验证；取消大文件复制；失败重试；刷新后任务状态同步。
6. 不抢焦点、不操作主窗口、不切用户正在看的标签；测试后停止服务进程。

### 5.2 桌面专属集中一次

缩略图滚动流畅度、空格预览（任务栏/焦点/Esc）、列菜单勾选态、紧凑模式、任务中心进度观感。一次进程内完成，完成后立即退出。

### 5.3 构建

优先 `stop.bat` → `dotnet publish ... -o output`；若文件锁/编码归一化 exit 1，重试一次并如实记录；不把 build.bat 当作可自动执行的可靠路径。

## 6. 风险与限制

- `WebPageHtml.cs` 回退页不包含新功能（静态资源正常时无影响）。
- 远程任务仅映射现有队列；若队列没有取消/重试公开方法，则这两个能力对远程不可用，如实标注。
- 桌面端 `Ask` 冲突策略本轮无弹窗入口，兜底为重命名并在任务消息中说明；Web 端四选一完整。
- 列宽/列配置并发写入通过锁与临时文件替换降低风险，但仍以最后一次保存为准。
- 缩略图生成受 FFmpeg/字体可用性影响；PDF 仍回退图标。
- 发布时输出目录可能被占用。

## 7. 审批

请确认以上范围与决策后再实施。重点确认：
1. 列排序纳入共享 `AppSettings`（会改变桌面「每个标签独立排序」为全局默认排序）。
2. 桌面 Ask 冲突本轮无弹窗、按重命名兜底。
3. WebPageHtml 内联回退页本轮不同步。
