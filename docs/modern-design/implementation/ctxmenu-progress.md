# 右键菜单单例与主题修复进度

状态：等你决策
卡点：WinMerge（以及 TortoiseGit / 7-Zip）扩展项枚举不到，是否继续排查 Shell 扩展加载需你拍板；菜单单例与主题修复本身仍待人工实测。
下一步：你确认取舍后，按「待人工验证」4 条做实测（连续右键、失焦关闭、深浅主题、扩展子菜单）。

更新时间：2026-11-04

## 目标

- 全局同一时刻只存在一个右键菜单；新菜单打开前先关闭旧的。
- 失焦、点击窗口外部、按 Esc、切换标签、窗口移动或最小化时立即关闭。
- 不再出现「本程序菜单 + 系统原生菜单」双层并存。
- 菜单背景 / 文字 / 悬停 / 分隔线跟随应用深浅主题。

## 已决定方案

选择 **B：应用内自绘菜单承载 Shell 枚举项**。

理由：

- 原生 `TrackPopupMenuEx` 菜单无法跟随应用主题，且弹窗生命周期难以与应用内状态同步。
- 现有 Avalonia 菜单已经具备多级子菜单、键盘导航、视口翻转与滚动。
- 系统项与第三方扩展仍由 `ISystemContextMenuService.EnumerateShellMenu` 枚举（内部已发送 `WM_INITMENUPOPUP` 触发 Git / TortoiseSVN 懒加载子项），点击后回传命令编号执行。

## 已完成

1. 移除 `FileListPanel` 的 `ContextRequested` 隧道拦截，不再请求原生菜单；右键只走 Avalonia 菜单。
2. 新增 `ContextMenuSingleton`：弱引用保存当前菜单，`PrepareOpen` 关闭旧菜单，`CloseCurrent` 关闭并清空；日志记录打开数量。
3. `FileListPanel` 与 `DiskViewPanel` 的空白处 / 条目菜单 `Opening` 事件均接入单例。
4. `MainWindow` 接入失焦、窗口位置变化、最小化、窗口内点击、Esc 关闭；切换标签时关闭。
5. 新增 `SystemMenuUiBuilder`：系统项渲染保留勾选 / 单选、默认粗体、快捷键、BMP 图标、管理员盾牌与多级子菜单。
6. `App.axaml` 增加 `ContextMenu` / `MenuItem` 主题样式，背景、文字、悬停使用主题字典颜色。
7. 新增 `tools/count-app-windows.ps1`：统计进程可见顶层窗口与菜单弹层窗口数。

## 浏览器侧验证（2026-11-04，使用独立工作区，不影响用户界面）

- 方式：本机 HTTP 调 `/api/session`、`/api/mode?m=independent`、`/api/tabs/new`、`/api/ctxmenu`；未点击用户桌面窗口，测试实例测完关闭。
- `POST /api/ctxmenu` 空白处（目录 `D:\TabTaskExplorer`）：HTTP 200，`systemMenuAvailable=True`，顶层 22 项；递归统计：叶子 32、子菜单 1（新建）、默认项 1、图标 14。证明层级、子菜单、默认标记与图标字段可用。
- 多选（两个文件）请求：旧发布包返回「系统外壳菜单枚举超时（2500 毫秒）」，随后连接被重置，应用进程退出。
- 根因修复（2026-11-04 9:20）：
  1. 把 4 处 `CoInitializeEx` 改为 `OleInitialize` / `OleUninitialize`，满足外壳扩展的 OLE 要求。
  2. 修复背景菜单 PIDL 释放顺序：`SHBindToParent` 返回的 child PIDL 指向父 PIDL 内部，原代码在 `BindToObject` 前就 `FreeCoTaskMem(parent)`，造成 use-after-free / 0xC0000005；两处已改为 `BindToObject` 后再释放。
  3. 条目枚举也传入 `CMF_EXPLORE`，与资源管理器一致。
- 独立控制台复测（不经过 App、不点击桌面）：文件夹条目、文件夹空白处、两个 txt 多选三种枚举全部正常返回且未崩溃：
  - 文件夹条目：顶层 25、叶子 20、子菜单 1、默认 1、图标 5；含 WizTree。
  - 文件夹空白处：顶层 12、叶子 26、子菜单 1、默认 1、图标 15。
  - 两个 txt 多选：顶层 16、叶子 11、默认 1、图标 2；含 ToDesk/TRAE/Code 等扩展。
- WinMerge：注册表存在 `ShellExtension.WinMergeShell`（`C:\Program Files\WinMerge\ShellExtensionX64.dll`），但枚举结果里没有 WinMerge 菜单项（文件夹、空白处、多选均无），因此 `/api/ctxmenu/run` 无法用 WinMerge 验证。7-Zip / TortoiseGit 也未出现在枚举结果，WizTree 和静态注册项正常。
- 新建文件夹命令：控制台直接调 `ExecuteShellMenuCommand` 返回 false、未创建目录；在无窗口的控制台里可能缺少有效 owner，需在应用内复测。

## 构建状态

- 2026-11-04 08:48 修复 `MainWindow` 的 `WindowStateChanged` 不存在（Avalonia 12 改为监听 `WindowState` 属性变化）。
- 同时修复由并行任务引入的 `UiLogger.LogError` 4 参数重载缺失、`LogViewerWindow` 缺少 `Avalonia.Input.Platform`（剪贴板扩展方法）、`SshSessionFactory` 委托类型推断。
- `dotnet build ... -c Release --no-restore`：**exit 0，0 error**（直接调用 dotnet 会触发 `sandbox backend failed`，改用 `Start-Process dotnet ... -Wait` 执行）。
- `dotnet publish ... -o output`：**exit 0**；`output\TabTaskExplorer.exe` 更新时间 2026-11-04 08:48，大小 361,559,664 字节。
- **当前发布阻塞**：并行任务正在修改 `ConnectionPanel.axaml`（9:11），XAML 事件处理器不存在导致 AVLN3000；与本右键任务无关，等其稳定后再发布。
- 发布产物已成功启动为进程 `TabTaskExplorer`（pid 54836），未崩溃；诊断脚本基线：可见顶层窗口数=1，菜单 / 弹层窗口数=0。
- 验证实例已关闭。
- **注意**：另有一个 2026-11-03 14:06 启动的旧实例 `TabXExplorer64`（pid 5680）仍在运行，当前会话结束它时报“Access denied”，需要用户手动退出，否则实际右键测试仍是旧行为。

## 待人工验证

1. 连续快速右键 10 次，始终只有一个菜单。
2. 点击空白处 / 切到其他程序 / Esc 后菜单消失，`tools\count-app-windows.ps1` 输出菜单弹层窗口数=0。
3. 深色与浅色主题下菜单颜色与主界面协调。
4. WinMerge / TortoiseGit 子菜单能展开并执行。


## 下一步

- 人工实测上面 4 条，确认后把结果回填本文件；若出现扩展子菜单为空，再检查 `WM_INITMENUPOPUP` 时机。
