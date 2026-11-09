# 网页端 / 桌面端界面一致性整改（第四轮）

状态：待批准（Spec Mode）
日期：2026-11-06
上游进度：`docs/modern-design/implementation/web-ui-parity/progress.md`（P0 结构占位、P1 快捷键统一已完成）

## 1. 本轮目标

原则不变：**界面必须一致，功能可以缺**。缺失能力必须显式置灰 + 可读中文原因，不允许整块缺失，也不允许已可用功能退化。

本轮只做三件事：

1. 桌面端补「预览」「任务中心」两个入口（网页端已有，桌面端缺失）。
2. 网页端补齐「截图 / 录屏」面板壳（桌面端有 `CapturePanel.axaml`，网页端整块缺失）。
3. 新增 `/api/settings`，把网页设置面板中能接的分组接上；同时收敛 `WebPageHtml.cs` 内联回退页，消除第三份业务副本。

## 2. 硬约束（不得违反）

- 不修改：`design/tokens/ui-tokens.json`、`src/TabTaskExplorer.App/Resources/Tokens.axaml`、`src/TabTaskExplorer.App/Assets/web/tokens.css`、`src/TabTaskExplorer.App/Assets/web/app.css`、`src/TabTaskExplorer.App/App.axaml`。
- 不新增 px 字面量到 `app.css`；网页新样式只复用现有类（`panel` / `panelHead` / `panelTitle` / `btn` / `navitem` / `modal`）与已有 CSS 变量。
- 不碰 `FileDropPolicy.cs`、`Services/Capture/*`。
- 不修改 `QuickLookWindow`、`FileTaskCenter`、`FilePreviewClassifier` 的业务语义，只做调用。
- 日志不打印明文密码；未实测项在进度文档标注「未实测」。

## 3. 现状核对（已读代码，结论）

### 3.1 桌面端

- 工具栏：[MainWindow.axaml](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/MainWindow.axaml#L207-L318) 现有 12 个按钮，**无预览、无任务中心**。
- 快捷键：`FileListPanel` 空格/ Esc 已调用 `QuickLookWindow.ShowFor(owner, path)`（[FileListPanel.axaml.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/FileListPanel.axaml.cs#L222-L255)），预览能力完整。
- 任务能力：`MainWindowViewModel` 持有 `fileTaskCenter`，公开 `GetFileTasks(string workspaceId)`（L2684，空 id = 桌面任务 + 远程传输映射）、`CancelFileTask`（L2829）、`RetryFileTask`（L2849）、`ResolveFileTaskConflict`（L2869）。
- 数据模型：`FileTaskSnapshot` 含 `State/Progress/Total/Processed/Failed/SourceText/DestinationText/Message/CanCancel/CanRetry/ConflictPath/ConflictName/ConflictIsDirectory`。
- 图标：`VectorIconPaths` 已支持 `eye`、`board`，可直接用。

### 3.2 网页端截图

- 后端**已存在**：`GET /api/capture/state`、`POST /api/capture/action`（[WebServiceHost.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebServiceHost.cs#L1211-L1242)）。
- 桥接**已存在**：`IWebWorkspaceBridge.GetCaptureStateJson()` / `ExecuteCaptureAction(action, argument)`。
- 已支持动作：`fullscreen`、`region`、`pin`、`pinSave`、`delete`、`copy`。
- 状态字段：`outputDirectory`、`fileNameTemplate`、`format`、`pinSave`、`copyToClipboard`、`openFolderAfter`、`openEditorAfter`、`count`、`items[]`（`fileName/fullPath/sizeBytes/sizeText/modifiedTime/thumbnailUrl`）。
- 结论：**缺的是前端面板壳，不是后端**。录屏在网页端无桥接（`ExecuteCaptureAction` 无 record 分支），必须置灰。

### 3.3 网页端设置

- 上一轮已在 `index.html` 增加 `#settings` 弹层，`panels.js` 有 11 个分组，其中 8 组带「仅桌面」角标。
- 后端**没有** `/api/settings`；`IWebWorkspaceBridge` 无设置读写方法。
- `AppSettings` 可用字段（节选）：`Theme`、`ShowFileExtensions`、`ShowHiddenFiles`、`FileCompactMode`、`CloseToTray`、`DoubleClickTabClose`、`ShowToolbar`、`ShowBookmarkBar`、`LogLevel`、`RestoreLastTabs`、`DefaultOpenMode`、`EditorFontFamily/EditorFontSize/EditorTabWidth/EditorInsertSpaces/EditorWordWrap/EditorShowWhitespace`、`CapturePinShotSaveFile/CaptureOpenFolderAfter/CaptureOpenEditorAfter/CaptureIncludeCursor/CaptureDelaySeconds/CaptureBackend`。

### 3.4 内联回退页

- [WebPageHtml.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebPageHtml.cs) 共 3773 行，是完整界面的第三份副本（HTML + CSS + JS 全量复制）。
- 仅当 `WebStaticAssets` 嵌入资源缺失时由 `WebServiceHost` 回退使用（[WebServiceHost.cs](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebServiceHost.cs#L515)）。
- 文件头注释已写明「待构建与浏览器验证通过后可整体删除本常量」，但本次不删除常量，改为**降级为最小诊断页**，避免第三份业务逻辑继续分叉。

## 4. 设计

### 4.1 桌面端：预览 + 任务中心

**文件 1：`src/TabTaskExplorer.App/Views/MainWindow.axaml`**

在工具栏「显示详细信息窗格」之后、「刷新」之前插入两个按钮，沿用现有 `<Button Classes="nav tool">` 结构：

```xml
<Button Classes="nav tool" Click="OnToolbarPreviewClick" ToolTip.Tip="预览选中文件（空格）">
  <StackPanel Orientation="Horizontal" Spacing="5">
    <views:VectorIcon Kind="eye" Width="14" Height="14" Thickness="1.6"
                      IconBrush="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
    <TextBlock Text="预览" VerticalAlignment="Center" Foreground="{DynamicResource ToolbarTextBrush}" />
  </StackPanel>
</Button>
<Button Classes="nav tool" Click="OnToolbarTaskCenterClick" ToolTip.Tip="任务中心（复制 / 移动 / 删除与远程传输）">
  ... Kind="board" ... TextBlock Text="任务中心" ...
</Button>
```

**文件 2：`src/TabTaskExplorer.App/Views/MainWindow.axaml.cs`**

新增字段：

```csharp
private Views.TaskCenterWindow? taskCenterWindow;
```

新增方法（放在 `OnToolbarInfoPaneClick` 之后）：

- `OnToolbarPreviewClick`
  1. `FileTabViewModel? tab = this.ViewModel?.SelectedFileTab;` 为空 → 日志 + `ShowMessage("当前没有文件标签，无法预览")`，返回。
  2. `List<FileRow> rows = tab.GetCheckedRows();` 为空 → `tab.ShowMessage("请先勾选要预览的文件")` + 日志，返回。
  3. 取第一个；`Directory.Exists(row.FullPath)` → `tab.ShowMessage("预览仅支持文件，文件夹请直接双击打开")`，返回。
  4. `QuickLookWindow.ShowFor(this, row.FullPath)`；日志记录路径与勾选数量。
- `OnToolbarTaskCenterClick`
  1. `MainWindowViewModel? vm = this.ViewModel;` 为空 → 日志返回。
  2. 已有窗口且可见 → `Activate()`；否则 `this.taskCenterWindow = new TaskCenterWindow(vm); this.taskCenterWindow.Closed += …置 null; this.taskCenterWindow.Show(this);`
  3. 日志记录「打开桌面任务中心」。

**文件 3（新建）：`src/TabTaskExplorer.App/Views/TaskCenterWindow.cs`**

纯代码窗口（无 XAML），复用 `FileTaskCenter` 快照，不写第二套任务逻辑：

- 尺寸 900×560，`WindowStartupLocation=CenterOwner`，`Title="任务中心"`。
- 顶部：标题 + 「刷新」按钮 + 「共 N 个任务 / 运行中 X / 等待冲突 Y」统计。
- 中部：`ListBox`（单行模板：状态徽标文字 + 类型（复制/移动/删除，远程标注）+ 源 → 目标 + 进度 `Processed/Total (Progress%)` + `Message`），空列表显示「暂无任务」。
- 底部操作区：
  - 「取消任务」→ `vm.CancelFileTask(string.Empty, id)`，仅 `CanCancel` 时可用。
  - 「重试失败项」→ `vm.RetryFileTask(string.Empty, id)`，仅 `CanRetry` 时可用。
  - 冲突区：选中任务 `ConflictPath` 非空时启用，四个按钮「合并 / 替换 / 逐项处理 / 改名共存」分别传 `merge / overwrite / perItem / rename`，勾选「本批统一」→ `applyToAll=true`；按钮可用性由 `ConflictIsDirectory` 决定（文件冲突只显示「替换 / 改名共存」）。
  - 「关闭」。
- 刷新：`DispatcherTimer` 1 秒；窗口 `Closed` 时停止定时器。所有调用在 UI 线程直接执行。
- 操作结果写入窗口底部状态文字（成功/失败均为中文，失败原因取返回值 false 时给「任务可能已结束，请刷新后重试」）。
- 日志前缀 `任务中心`：打开、取消、重试、冲突处理、刷新异常。

### 4.2 网页端：截图 / 录屏面板壳

**文件 1：`Assets/web/index.html`**

- 标题栏 `#captureEntry`：去掉 `aria-disabled` 与置灰内联样式，`onclick="openCapture()"`，`title="截图与录屏"`。
- 新增弹层（与 `#settings` 同级，放在其后）：

```html
<div class="modal hidden" id="capturePanel">
  <div class="panel">
    <div class="panelHead">
      <div class="panelTitle">截图与录屏</div>
      <span class="btn" onclick="closeCapture()">关闭</span>
    </div>
    <div id="captureBody" style="flex:1;min-height:0;overflow:auto"></div>
  </div>
</div>
```

**文件 2：`Assets/web/views.js`**

- 侧栏「截图与录屏」由 `desktopOnly('截图与录屏')` 改为 `openCapture()`，移除置灰样式与 title。

**文件 3：`Assets/web/panels.js`（新增约 180 行）**

- `openCapture()`：显示弹层 → `loadCaptureState()` → 启动 2 秒轮询。
- `closeCapture()`：隐藏弹层 → 清轮询。
- `loadCaptureState()`：`fetch('/api/capture/state', { credentials: 'same-origin' })`，失败时在面板内显示可读原因（不是静默失败），成功缓存到 `captureState` 并 `renderCapture()`。
- `renderCapture()` 按桌面 `CapturePanel.axaml` 的分组与顺序渲染：
  1. **截图**：`全屏截图`(fullscreen)、`区域截图`(region)、`贴图截图`(pin)、延迟下拉（置灰，说明「网页端固定使用桌面设置的延迟」）。
     - 置灰：`主屏截图`、`滚动长截图`、`活动窗口`、`上次区域`、`屏幕取色`、`贴图（剪贴板）`；统一说明「网页端尚未接入该截图方式，请在桌面端使用」。
  2. **截图选项**：`贴图截图保存产物`（可写，`pinSave` on/off）；`完成后自动打开产物文件夹`、`截图后自动打开标注编辑器`、`包含鼠标指针`、`截图采集后端` 置灰，说明「该选项由桌面端截图管线读取，网页端暂不可改」。
  3. **录屏**：`开始/停止录制`、`录制目标`、`麦克风`、`帧率/质量` 全部置灰，说明「网页端尚未接入录屏，请在桌面端录制」。
  4. **产物历史**：标题 + 计数 + `刷新`（重新 load）；列表每项显示缩略图（`/api/thumb?path=…`，失败回退图标）、文件名、时间、大小；行内操作 `删除`(delete)、`复制到剪贴板`(copy)；`打开` 与 `标注此图` 置灰，说明「产物在桌面本机，网页端不打开本地窗口」。
  5. **状态栏**：显示保存目录、命名模板、格式（来自 state）。
- `captureAction(action, arg)`：`POST /api/capture/action?a=…&arg=…`（带 CSRF 头，复用 `core.js` 现有 `postAction`/`fetch` 封装），返回 JSON 的 `message` 用 `flash()` 展示，随后 1.5 秒刷新状态。
- 权限不足（403）与能力缺失（置灰）分开提示：403 → `flash('当前端口未放行修改权限，无法执行该截图操作')`。

**文件 4：`Assets/web/boot.js`**

- `handleKeyDown` 的 Esc 分支：先关 `#capturePanel`，再关 `#settings`。
- 轮询不放进全局 refresh 循环，由面板自身 2 秒定时器负责，关闭即停，避免后台开销。

### 4.3 `/api/settings`

**文件 1：`Services/WebWorkspace.cs`（接口部分）**

`IWebWorkspaceBridge` 新增两个方法：

```csharp
/// <summary>读取网页可编辑的设置项（白名单）与只读分组原因（JSON 文本）。</summary>
string GetSettingsJson();

/// <summary>应用网页提交的设置项，返回 { ok, message, applied[], rejected[] }（JSON 文本）。</summary>
string ApplySettingsJson(string json);
```

**文件 2：`ViewModels/MainWindowViewModel.cs`**

- `GetSettingsJson()` 返回：

```json
{
  "values": {
    "theme": "Dark", "showFileExtensions": true, "showHiddenFiles": false,
    "fileCompactMode": false, "closeToTray": true, "doubleClickTabClose": true,
    "showToolbar": true, "showBookmarkBar": true, "logLevel": "Info",
    "restoreLastTabs": false, "defaultOpenMode": "Explorer",
    "editorFontFamily": "...", "editorFontSize": 13, "editorTabWidth": 4,
    "editorInsertSpaces": true, "editorWordWrap": false, "editorShowWhitespace": false,
    "capturePinShotSaveFile": true, "captureOpenFolderAfter": true,
    "captureOpenEditorAfter": false, "captureIncludeCursor": true,
    "captureDelaySeconds": 0, "captureBackend": "auto"
  },
  "editable": ["theme", "showFileExtensions", "..."],
  "locked": {
    "shortcuts": "快捷键在桌面端配置，网页端只读展示",
    "web": "网页服务地址与端口需在桌面端修改并重启服务",
    "startup": "启动项需要写入系统注册表 / 启动目录，仅桌面端可用",
    "openwith": "默认打开方式需要系统文件关联，仅桌面端可用",
    "media": "播放器设置由桌面内置播放器读取，网页端不使用",
    "shellmenu": "系统右键菜单项仅桌面端可枚举与启停"
  }
}
```

- `ApplySettingsJson(json)`：`JsonDocument` 逐键白名单校验（布尔 / 枚举字符串 / 整数范围 `0..30` / 字体大小 `8..48` / 制表宽度 `1..8`），未知键与越界值进 `rejected`（附中文原因）；通过项在 UI 线程赋值 → `SaveSettingsAndApply()` → 返回 `applied` 列表。
- `theme` 走 `ApplyTheme`（已有），`defaultOpenMode` 只允许 `Explorer / Ide`。
- 应用后同步刷新网页端可见的 `state`（`showFileExtensions` 等已在快照中，无需额外处理）。

**文件 3：`Services/WebServiceHost.cs`**

- 在 `/api/filecolumns` 之后新增：

```csharp
if (path == "/api/settings" && (request.Method == "GET" || request.Method == "POST"))
{
    this.HandleSettings(stream, request, profile, bridge);
    return;
}
```

- `HandleSettings`：GET 直接返回 `bridge.GetSettingsJson()`；POST 先判 `profile.AllowModify`（403 文案「该端口未放行修改权限」），再交给 `bridge.ApplySettingsJson(body)`，返回 JSON；解析失败 400「设置格式不正确」；写日志（键名列表，不含值敏感信息）。
- `/api/version` 的 `features` 增加 `settings = true`。

**文件 4：`Assets/web/panels.js`**

- `openSettings()` 时 `loadSettings()`（GET `/api/settings`），把 `values` 回填到各分组控件；失败时分组显示「读取设置失败：<原因>」，不显示假值。
- 可接分组改为真实控件并即时 POST：
  - 基本：主题、显示扩展名、显示隐藏文件、紧凑模式、关闭到托盘、双击关闭标签、显示工具栏、显示书签栏、日志级别、恢复上次标签、默认打开模式。
  - 编辑器：字体、字号、制表宽度、插入空格、自动换行、显示空白（写入后提示「新打开的文件生效」）。
  - 截图：贴图截图保存产物、完成后打开文件夹、截图后打开编辑器、包含鼠标指针、延迟秒数、采集后端。
  - 文件：列显示 / 列宽 / 排序继续走既有 `/api/filecolumns`，不重复接。
- 仍置灰分组：快捷键、网页服务、启动项、默认打开方式、媒体、右键管理；每组顶部显示一行可见的 `panelHint` 样式说明（不只用 title），文案取 `/api/settings` 的 `locked`。
- 权限不足：POST 返回 403 时 `flash` 中文原因，并把控件回滚到读取值。

### 4.4 `WebPageHtml.cs` 内联回退页

- 保留 `Content` 常量与 `__PORT__` 占位符（`WebServiceHost` 依赖）。
- 将 3773 行完整副本**降级为最小诊断页**（目标 ≤ 200 行）：
  - 保留 `<title>TabTaskExplorer 网页端</title>`、`TTE-TOKENS-BEGIN/END` 令牌块（生成器仍可同步）、深浅色可读。
  - 顶部横幅固定文案：「内联回退页：网页静态资源（Assets/web）不可用，界面与功能可能不完整。」
  - 标题栏入口与侧栏条目**名称、顺序与静态页一致**（新建、任务看板、截图与录屏、IDE、主题、设置 / 快捷访问、此电脑、网络、连接管理、全局搜索、文件比对、磁盘视图、进程监控、启动项管理、右键管理、截图与录屏、任务看板）；点击统一弹出说明「该入口需要静态资源，请重新构建或在桌面端修复 Assets/web 后刷新」。
  - 保留一个 `GET /api/state` 连通性探测按钮与结果显示，便于判断服务本体是否正常。
  - 删除内联页中的业务 JS（文件列表渲染、任务中心渲染、快捷键、设置弹层等），从此只有静态资源一份业务实现。
- 构建后验证：正常产物走静态资源（`/` 返回 index.html 内容含 `id="capturePanel"`）；回退页仅在 `WebStaticAssets.IsAvailable=false` 时出现，采用临时重命名校验或代码审查确认，不破坏正常路径。

## 5. 实施顺序

1. 4.1 桌面入口（可独立构建验证）。
2. 4.2 网页截图面板壳。
3. 4.3 `/api/settings` + 设置面板接线。
4. 4.4 回退页收敛。
5. 每步完成后追加 `progress.md`（状态头、改动文件、实测证据、未实测项）。

## 6. 验证

### 6.1 构建

```
dotnet publish src\TabTaskExplorer.App\TabTaskExplorer.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none -o output
```

要求：退出码 0，无新增编译错误；只允许既有警告。

### 6.2 桌面端（人工 / 日志）

- 勾选一个文件 → 工具栏「预览」→ 快速预览窗出现，标题为文件名。
- 不勾选 → 「请先勾选要预览的文件」。
- 工具栏「任务中心」→ 窗口出现，列表与网页任务中心条数一致；执行一次复制任务可看到进度刷新；取消后状态变「已取消」。
- 「任务中心」重复点击不产生第二个窗口。

### 6.3 网页端（chrome-devtools，127.0.0.1:15919）

- 标题栏相机图标与侧栏「截图与录屏」均可打开面板；Esc 关闭。
- 面板分组顺序：截图 → 截图选项 → 录屏 → 产物历史 → 状态栏。
- 点击「全屏截图」→ 收到「截图任务已提交」提示；产物历史在数秒内出现新条目（或给出可读失败原因）。
- 录屏区按钮全部置灰且可见原因文案；点击不产生请求。
- 设置面板：修改「显示扩展名」→ POST `/api/settings` 返回 200，刷新页面后状态保持，桌面端设置面板同步变化。
- 无修改权限的端口（若可构造）POST 返回 403 中文提示（未构造则标注未实测）。
- 证据记录：页面 DOM 摘要（面板分组标题、置灰按钮数量、`locked` 文案）、`/api/settings` 响应片段、产物历史条数变化。

### 6.4 回归

- 既有功能不退化：Ctrl+Tab 切换、设置弹层 Esc、任务中心取消/重试、预览空格。
- `app.css` / `tokens.css` / `Tokens.axaml` / `ui-tokens.json` 的 diff 必须为空（构建前用 `git status` 核对）。

## 7. 风险与取舍

| 风险 | 处理 |
| --- | --- |
| 桌面任务中心新增窗口带来第二套 UI 代码 | 只做快照展示 + 调用既有 VM 方法，不含任务算法；后续可抽 XAML |
| 网页截图动作依赖桌面前台/多显示器 | 面板显示「截图任务已提交」，失败原因来自现有日志与 state，不伪造成功 |
| `/api/settings` 写入后桌面控件未刷新 | 统一走 `SaveSettingsAndApply()`，与桌面设置页同一路径 |
| 回退页降级导致静态资源缺失时不可用 | 回退页仅用于诊断，正常构建 `WebStaticAssets` 始终可用；保留连通性探测 |
| 令牌文件被误改 | 构建前 `git status` 核对，四个文件出现改动即停止并回报 |

## 8. 验收标准

- [ ] 桌面工具栏存在「预览」「任务中心」，功能按 6.2 通过。
- [ ] 网页截图面板壳存在且结构对齐桌面，未接能力置灰并有可见原因。
- [ ] `/api/settings` GET/POST 生效，设置面板可接分组真实可用，其余置灰原因来自接口。
- [ ] `WebPageHtml.cs` 不再包含第二套业务实现，入口/结构名称与静态页一致。
- [ ] 四个令牌相关文件与 `app.css` 无改动。
- [ ] `progress.md` 已按 4.1 → 4.4 分批更新。
