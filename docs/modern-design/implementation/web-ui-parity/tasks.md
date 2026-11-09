# 网页端 / 桌面端界面一致性整改（第四轮）· 任务清单

依据：同目录 [spec.md](file:///d:/TabTaskExplorer/docs/modern-design/implementation/web-ui-parity/spec.md)

- [x] t1 桌面端工具栏补「预览」入口
  - 改 `Views/MainWindow.axaml`（`Kind="eye"`，`Click="OnToolbarPreviewClick"`，位置：显示详细信息窗格之后）。
  - 改 `Views/MainWindow.axaml.cs`：取 `SelectedFileTab.GetCheckedRows()`，空选/文件夹给中文提示，文件调 `QuickLookWindow.ShowFor(this, path)`。
  - 验证：构建 0 错误；**运行态点击未实测**（无桌面自动化工具）。

- [x] t2 桌面端工具栏补「任务中心」入口 + 窗口
  - 改 `Views/MainWindow.axaml`（`Kind="board"`，`Click="OnToolbarTaskCenterClick"`）。
  - 新建 `Views/TaskCenterWindow.cs`：ListBox 快照、1 秒刷新、取消/重试/冲突（merge|overwrite|perItem|rename + 本批统一）、关闭；复用 `MainWindowViewModel.GetFileTasks("")/CancelFileTask/RetryFileTask/ResolveFileTaskConflict`。
  - 验证：构建 0 错误；单实例逻辑代码审查通过；**窗口布局与操作未实测**。

- [x] t3 更新 `progress.md`（第 1 部分：桌面入口）
  - 已写入 4.1；实测证据与未实测项见 4.5 / 4.7。

- [x] t4 网页端截图/录屏面板壳
  - `Assets/web/index.html`：启用 `#captureEntry`，新增 `#capturePanel` 弹层。
  - `Assets/web/views.js`：侧栏「截图与录屏」改为 `openCapture()`。
  - `Assets/web/panels.js`：`openCapture/closeCapture/loadCaptureState/renderCapture/captureAction`；分组=截图/截图选项/录屏/产物历史/状态栏；已接=fullscreen|region|pin|pinSave|delete|copy；未接置灰并可见原因；403 单独提示。
  - `Assets/web/boot.js`：Esc 先关截图面板再关设置。
  - 验证：浏览器实测 5 分组 / 42 置灰 / 全屏截图产物 14→15（见 progress.md 4.5）。

- [x] t5 更新 `progress.md`（第 2 部分：截图面板 + 浏览器证据）
  - 已写入 4.2 / 4.5。

- [x] t6 `/api/settings` 后端
  - `Services/WebWorkspace.cs`：`IWebWorkspaceBridge` 增 `GetSettingsJson()` / `ApplySettingsJson(string json)`。
  - `ViewModels/MainWindowViewModel.cs`：白名单值 + `editable` + `locked` 中文原因；POST 校验、UI 线程应用、`SaveSettingsAndApply()`、返回 applied/rejected。
  - `Services/WebServiceHost.cs`：路由 `/api/settings` GET/POST；POST 判 `AllowModify`；`/api/version` 增 `settings=true`。
  - 验证：GET 返回 23 个白名单键；POST 合法/非法/403 均已实测（见 progress.md 4.5）。

- [x] t7 网页设置面板接线
  - `Assets/web/panels.js`：`loadSettings()` 回填；基本/编辑器/截图三组真实可写；快捷键/网页服务/启动项/默认打开方式/媒体/右键管理保持置灰并显示 `locked` 可见文案；403 回滚。
  - 验证：11 分组、7 个「仅桌面」角标；字号 13→14→15 连续保存成功；只读 profile 下 403 且回读真实值。

- [x] t8 更新 `progress.md`（第 3 部分：/api/settings + 设置面板证据）
  - 已写入 4.3 / 4.5 / 4.6。

- [x] t9 收敛 `WebPageHtml.cs` 内联回退页
  - 保留 `Content` + `__PORT__` + `TTE-TOKENS` 块；降级为 ≤200 行诊断页；入口名称/顺序与静态页一致；移除内联业务 JS。
  - 验证：正常页仍走静态资源（浏览器 DOM 含 `capturePanel`，样式表为 `/assets/web/*.css`）；构建 0 错误；回退页真实触发未实测。

- [ ] t10 全量回归与收尾
  - `git status` 已核对：`design/tokens/ui-tokens.json`、`Themes/Tokens.axaml`、`Assets/web/tokens.css` 的改动来自令牌任务，本轮未改；`app.css` 本轮按令牌清单替换（用户本轮明确要求）。
  - 新增：`core.js` 的 403+CSRF 重试修复（见 progress.md 4.6）。
  - 剩余：Ctrl+Tab / Esc / 空格预览 / 任务取消重试等桌面回归，因缺少桌面自动化工具**未实测**。
