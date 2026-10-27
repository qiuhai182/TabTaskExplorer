# 桌面端右键菜单不显示修复 · 任务清单

- [x] t1 根因判定：结论=**分支 A（ContextMenu 控件主题缺失）**。证据：①探针显示条目菜单/空白处菜单均 `IsOpen=True、Items.Count=12/6、Bounds=0x0`（500ms 后仍 0×0）、逻辑父=Popup → 内容存在但不测量；②E1 ToolTip 悬停新增窗口矩形 1500,158,1556,191 非零且截图可见「最大化」→ 弹窗机制正常，问题仅限 ContextMenu；③非虚拟化的空白处菜单同样 0×0 → 排除分支 B（行模板回收）为主因；④离线字节扫描 FluentAvalonia.dll(3.1.0) 无 ContextMenu 主题、App.axaml 未含 `<FluentTheme/>` → ContextMenu 无模板。E3a/E3b 截图确认右键后无菜单。
- [x] t2 修复：按 t1 结论执行分支 A——[App.axaml](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml) 的 `Application.Styles` 中在 `<styling:FluentAvaloniaTheme />` 之前补 `<FluentTheme />`；未改动 Program.cs / UiLogger.cs / MainWindowViewModel.cs。
- [x] t3 构建与验证：分支 A（[App.axaml](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml) 补 `<FluentTheme />`）落地；构建通过；注入验证——空白处菜单探针 137×145、条目菜单探针 140×337（修复前均 0×0），截图可见菜单；临时探针 LogMenuLayoutProbe 及两处调用已全部删除并重建；`dotnet test` 144/144 通过；最终产物冒烟：右键出菜单、日志无异常、无探针残留。
- t4 用户确认：请用户手动复核桌面端各类右键菜单（文件行/文件夹行/空白处/此电脑/磁盘视图）可见可点、文件夹行粘贴项正确，确认后关闭本缺陷。
