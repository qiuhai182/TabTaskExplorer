# 任务分解（Web 对齐 + 任务中心）

状态：代码实现完成；T10-T12 因当前环境 Shell `sandbox backend failed` 无法构建/验证，待可用环境补跑。每项完成后更新本文件与 `progress.md`。

## M1 Web 能力对齐

### [x] T1 公共预览分类器
- 新增 `Services/FilePreviewClassifier.cs`：`FilePreviewKind`、`Detect`、`IsImage`、`ReadTextPreview`、`DescribeUnsupported`。
- `Views/QuickLookWindow.cs` 改用公共方法，删除私有枚举/判定/读文本。
- 验收：桌面快速预览行为不变；`ReadTextPreview` 仍为 512 KB + GBK 回退。

### [x] T2 服务端接口
- `WebServiceHost` 增加 `GET /api/thumb`、`GET /api/preview`、`GET/POST /api/filecolumns`、`/api/tasks*`。
- 均走 `CheckPath`、认证；thumb 返回 `image/png` + `Cache-Control: private, max-age=600`，失败 404。
- 验收：curl/浏览器请求 200/403/404 符合预期；路径越权拒绝并记日志。

### [x] T3 快照与设置共享
- `DebugUiState` 增加列/排序字段；`DebugFileRow` 增加 `Attributes`、`PreviewKind`。
- `TabContract.FillFile/FillSearch` 填充属性与分类。
- `AppSettings` 增加 `FileSortField/FileSortAscending`；`AppSettingsStore.Save` 加锁原子写。
- `FileTabViewModel` 增加 `ApplyColumnConfiguration` / `ApplySortConfiguration`，构造恢复排序。
- `IWebWorkspaceBridge` + `MainWindowViewModel` + `WebWorkspace` 实现读写列配置。
- 验收：Web POST 列配置后桌面 `/api/state` 与设置文件一致；桌面改列后 Web 快照变化。

### [x] T4 Web 列配置与缩略图
- `views.js` 按共享列/宽度/紧凑渲染；列设置勾选；缩略图懒加载 + blob 缓存。
- `actions.js` 列切换/列宽拖动保存到 `/api/filecolumns`，移除仅内存的 `colresize`。
- `index.html` 查看菜单与工具栏入口；`app.css` 样式。
- 验收：隐藏/显示列即时生效；拖宽后刷新页面仍保持；96 缩略图显示且失败回退图标。

### [x] T5 Web 快速预览
- `actions.js` 新增 `previewPath/closePreview/renderPreview`；右键「预览」；`boot.js` 空格打开、Esc 关闭。
- 预览层渲染图片（384 缩略图）、文本、PDF 提示、音视频占位。
- 验收：三种入口一致；Esc 关闭；不阻塞列表操作。

## M2 统一任务中心

### [x] T6 任务模型与本地执行器
- 新增 `Services/Tasks/FileTaskCenter.cs`、`LocalFileTaskExecutor.cs`、`RemoteTaskAdapter.cs`。
- 复制/移动/删除 + 冲突四策略 + 取消 + 失败重试。
- 验收：在 `.trae-qa` 内四种冲突策略、取消、重试均有结果；完成后快照状态与摘要正确。

### [x] T7 桥接与 API
- `IWebWorkspaceBridge` 增加任务方法；`MainWindowViewModel` / `WebWorkspace` 按 workspace 选择任务中心。
- `WebServiceHost` 实现任务 REST；权限：提交/取消/重试需 `AllowCreateDelete`。
- 验收：Web 提交任务返回 id；GET 轮询拿到进度；越权 403。

### [x] T8 Web 任务面板与操作接线
- `index.html` 任务中心浮层；`views.js` 渲染进度/状态/冲突；`actions.js` 复制/移动/删除改走任务 API。
- 冲突「询问」弹层四选一 + 全部应用；失败显示重试；运行中显示取消。
- 验收：浏览器完成一次复制、一次冲突、一次取消、一次重试；刷新同步。

### [x] T9 桌面接线与远程映射
- 桌面文件操作入口（先重读相关文件，仅最小增量）改为提交 `FileTaskCenter`；远程队列映射为同一快照。
- 保持右键菜单函数、`FileDropPolicy`、`Capture` 不动。
- 验收：桌面复制/移动/删除产生任务快照；远程队列在 Web 任务面板可见（取消/重试以现有能力为准）。

## M3 构建与验证

### T10 发布
- `stop.bat` 后 publish；文件锁失败重试一次并记录。

### T11 浏览器验证
- 按 `spec.md` 5.1 全项执行；记录用了浏览器、是否影响用户。

### T12 桌面集中验证
- 按 `spec.md` 5.2 一次完成；退出进程。

### T13 文档与收尾
- 更新 `fileview-progress.md`：接口、落地、测试证据、未验证项、风险。
- 输出结论：Web 对齐接口与落地、任务中心进度、改动文件、未验证项。
