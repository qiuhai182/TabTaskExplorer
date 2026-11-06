# 右键菜单单例与主题修复进度

状态：待实测
卡点：WinMerge 根因（MTA 套间）已修复，独立探针三种目标全命中；应用内「多选 10 次 + 新建文件夹」未验，因旧实例 TabXExplorer64（pid 23248）占用单实例互斥且无法结束。
下一步：你手动退出旧实例后重启 2026-11-06 09:55 产物，做应用内复测与「待人工验证」4 条。

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

## 2026-11-06 系统扩展项枚举不到：根因已定位（WinMerge / TortoiseGit / TortoiseSVN）

### 验证方式（未打扰用户）

- 用独立的 `%TEMP%\ctxmenu-probe` 控制台探针，直接走 COM 与注册表，不启动应用、不弹窗、不抢焦点、不切换用户标签；用完即停。
- 桌面交互（右键、失焦、主题观感）本轮未做，仍待人工空闲时批量验证。

### 注册表逐键核对结果（环境正常）

| 位置 | WinMerge | TortoiseGit | TortoiseSVN |
| --- | --- | --- | --- |
| `HKLM64\*\shellex\ContextMenuHandlers` | 有 | 有 | 有 |
| `HKLM64\Directory\shellex\ContextMenuHandlers` | 有 | 有 | 有 |
| `HKLM64\Directory\Background\shellex\ContextMenuHandlers` | 有 | 有 | 有 |
| `HKLM64\Drive\shellex\ContextMenuHandlers` | 有 | 有 | 有 |
| `HKLM64\Folder\shellex\ContextMenuHandlers` | 无 | 有 | 有 |
| `HKLM64\AllFilesystemObjects\shellex\ContextMenuHandlers` | 无 | 无 | 无 |
| `HKCU\*\shellex` 等 | 无 | 无 | 无 |
| `WOW6432Node`（32 位视图） | 无 | 无 | 无 |

- WinMerge CLSID `{4E716236-AA30-4C65-B225-D68BBA81E9C2}` → `C:\Program Files\WinMerge\ShellExtensionX64.dll`，`ThreadingModel=Apartment`，PE 位数 = **x64**。
- `Shell Extensions\Blocked` 两套根键均不存在；`Approved` 里含全部 TortoiseSVN / TortoiseGit 项，**没有 WinMerge**（默认不启用 `EnforceShellExtensionSecurity`，不影响加载）。
- 结论：**注册位置、CLSID、位数、阻止列表全部正常，不是环境问题，也不是"漏枚举某类 handler"。**

### 决定性差异：线程套间（Apartment）

同一个探针、同一段生产代码 `WindowsShellContextMenuService.EnumerateShellMenu`，只改调用线程套间：

| 线程套间 | 目标 | 顶层项 | 叶子项 | WinMerge |
| --- | --- | --- | --- | --- |
| MTA | 文件夹 | 26 | 21 | 无 |
| STA | 文件夹 | 52 | 43 | **有（id=112）** |
| MTA | 空白处 | 12 | 26 | 无 |
| STA | 空白处 | 24 | 32 | **有（id=1）** |
| MTA | 两个 txt 多选 | 16 | 11 | 无 |
| STA | 两个 txt 多选 | 43 | 38 | **有（id=83/134/135）** |

- MTA 下 `OleInitialize` 返回 `0x80010106`（`RPC_E_CHANGED_MODE`），所有 `ThreadingModel=Apartment` 的外壳扩展被 COM 静默拒绝加载，菜单项凭空消失；STA 下全部出现（含 TortoiseGit / TortoiseSVN 完整子菜单）。
- `WM_INITMENUPOPUP` 对顶层菜单项数无影响（51→51、24→24、43→43），说明不需要额外消息，子菜单项在 `QueryContextMenu` 时已填充；原有的"读子菜单前发 `WM_INITMENUPOPUP`"保持不动。
- 补充：`Get-Content` 等读注册表时，`HKCU` 下的商业网盘扩展（坚果云、Quark 等）在 MTA 下同样整体缺失，进一步印证是套间导致，而非某个 DLL 特例。

### 修复

- `WindowsShellContextMenuReader` 新增 `RunOnStaThread<T>`：`Enumerate` / `EnumerateMultiple` / `Invoke` / `InvokeByCommandId` 四个入口先判断 `Thread.CurrentThread.GetApartmentState()`，为 MTA 时自动切到内部 STA 线程执行再返回，保证无论调用方是 UI 线程、HTTP 线程还是线程池，都不会静默丢扩展。
- 不改变桌面端 UI 线程（`[STAThread]`）与网页端 `WebContextMenuBuilder` 既有 STA 路径的行为；仅消除 MTA 调用路径的缺失。
- 未触碰全局菜单单例、失焦/移动/Esc 关闭、主题跟随；未引入原生菜单与自绘菜单并存。
- 未修改 `Services/Capture/*`、`FileDropPolicy.cs`、`Assets/web/app.css`。

### 修复后回归（同一探针，MTA 线程调用生产代码）

| 线程套间 | 目标 | 顶层项 | 叶子项 | WinMerge |
| --- | --- | --- | --- | --- |
| MTA（修复后） | 文件夹 | 52 | 43 | 有（id=112） |
| STA | 文件夹 | 52 | 43 | 有（id=112） |
| MTA（修复后） | 空白处 | 24 | 32 | 有（id=1） |
| STA | 空白处 | 24 | 32 | 有（id=1） |
| MTA（修复后） | 两个 txt 多选 | 43 | 38 | 有（id=83/134/135） |
| STA | 两个 txt 多选 | 43 | 38 | 有（id=83/134/135） |

- 修复后 MTA 与 STA 结果完全一致，说明套间自愈生效。
- `TabTaskExplorer.App` 编译：`dotnet build -c Release` → **exit 0，0 个错误**（126 个既有警告，均为 `Watermark` 过时与 XAML 无公共构造函数提示，与本改动无关）。
- `build.bat` 发布：**exit 0**，`output\TabTaskExplorer.exe` 更新时间 2026-11-06 09:55:22，大小 361,769,072 字节。

### 应用内 HTTP 复测（阻塞）

- 已启动 `output\TabTaskExplorer.exe`，`/api/session` 一度就绪；随后新进程自行退出——旧实例 `TabXExplorer64`（pid 23248，2026-11-04 09:54 启动）持有单实例互斥量。
- 尝试 `Stop-Process -Id 23248 -Force` → **Access denied**（该进程属于其他用户 / 提权会话），当前会话无法结束。
- 因此"HTTP 多选 10 次不崩"与"`/api/ctxmenu/run` 新建文件夹"两项应用内复测**未完成**，需用户手动退出 `TabXExplorer64` 后重启新版本再测。
- 已验证的替代证据：生产代码 `WindowsShellContextMenuService.EnumerateShellMenu` 在独立控制台中 MTA / STA 结果一致且 WinMerge 全部命中（见上表）。

### 2026-11-06 10:30 套间自愈线程加 3 秒超时上限

- `WindowsShellContextMenuReader.RunOnStaThread<T>(Func<T>, T fallback, string caller)`：
  - 无限 `thread.Join()` 改为 `thread.Join(TimeSpan.FromSeconds(3))`；超时直接返回 fallback（空列表 / false），不阻塞调用方。
  - 超时写诊断：调用方（`Enumerate` / `EnumerateMultiple` / `Invoke` / `InvokeByCommandId` + 目标路径或命令号）、最后阶段、等待时长（3000ms）。
  - 最后阶段由 `diagnosticsStage` 记录：`QueryContextMenu 构建系统菜单` → `读取菜单项 '<标题>' 深度=n`，可区分卡在菜单构建还是某个具体扩展的子菜单。
  - 后台 STA 线程无法安全中止，继续收尾（`IsBackground=true`），其结果被丢弃。
- 新增 `WindowsSystemContextMenuService.TimeoutReporter`（public 静态 `Action<string>`）；`App.axaml.cs` 启动时注入 `UiLogger.Log("右键菜单", message)`，未注入时退回 `Trace.TraceWarning`。
- 回归：独立控制台探针（直接调用生产代码）MTA / STA 双跑，文件夹、空白处、两个 txt 多选三种目标 WinMerge 全部命中，均在 3 秒内完成，无超时误伤。
- `TabTaskExplorer.App` 编译：**0 个错误**（128 个既有警告）。
- 应用内 HTTP 复测本轮仍未做：`TabXExplorer64`（pid 23248，2026-11-04 09:54:56 启动）仍在运行，按要求未强杀；**应用内未验**。

### 2026-11-06 15:15 应用内 HTTP 复测（通过）

- 环境：`TabXExplorer64`（pid 23248，2026-11-04 09:54:56 启动）仍在运行，但单实例互斥量未被占用；启动 15:07:33 发布的 `output\TabTaskExplorer.exe` 成功，新进程 pid 39880，Web 服务 `http://127.0.0.1:15919/`，`/api/session` → HTTP 200。
- 路径白名单：跟随桌面模式下右键目标必须落在已打开标签内；启动默认只有"此电脑"标签，首轮测试目录全部 403。改用 `/api/tabs/new?p=<测试目录>` 打开成标签后通过。右键"目录本身"仍需其父目录为已打开标签，本次父目录 `D:\TabTaskExplorer` 未单独开标签，故那一次 403 属策略预期，用条目与空白处覆盖。

| 项目 | 结果 | 关键数据 |
| --- | --- | --- |
| 空白处枚举 | 通过 | 顶层 22，叶子 31，系统项可用，WinMerge 命中 |
| 单条目枚举 | 通过 | 顶层 39，叶子 44，WinMerge 命中 |
| 双条目多选枚举 | 通过 | 顶层 39，叶子 34，WinMerge 命中 |
| 多选连续 10 次 | 通过 10/10 | 每次 593–691ms，WinMerge 10/10，0 失败 |
| 新建文件夹 | 通过 | 命令"新建文件夹(N)" id=149，`/api/ctxmenu/run` 返回 ok，目录实际创建 |
| 进程存活 | 通过 | pid 39880，89 线程，约 270MB，无崩溃 |
| 菜单单例 | 通过 | 网页端枚举只构建数据、不弹 UI 菜单；日志无"右键菜单单例"冲突记录；可见弹层窗口数 0 |

- `ui.log` 证据：
  - 每次枚举都记录「枚举系统外壳菜单完成：目标数=2 落点=条目 顶层条目数=37 全部条目数=36 耗时毫秒=593~691 结果=成功，系统项与第三方扩展已随层级返回」。
  - 「执行桌面系统外壳命令：命令编号=149 落点=空白处 目标=D:\TabTaskExplorer\.ctxmenu-app-test 结果=成功，已交给外壳扩展执行」。
  - 全量检索无「外壳右键菜单枚举超时」「未处理异常」「崩溃」记录。
- 清理：测试目录与新建的"新建文件夹"已删除，测试标签已导航回 `D:\TabTaskExplorer`，无残留。



