# 系统集成（默认程序 / 文件关联 / 发送到 / 单实例）现代化设计

状态：设计稿（2026-11-03）
适用：`DefaultFileManagerManager`、`AutoStartManager`、`SingleInstanceService`、`Program.cs`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 默认文件管理器 | `Services/DefaultFileManagerManager.cs:10-60` | 半成品 | HKCU `Software\Classes\Directory\shell` 的 `open` 指向本程序 `%V` |
| 开机自启 | `Services/AutoStartManager.cs:10-34` | 可用 | Windows Run 键 / Linux XDG autostart / macOS LaunchAgent，写后回读校验 |
| 单实例 | `Services/SingleInstanceService.cs:17-118` | 可用 | 命名互斥量 + 命名管道转发 `OPEN`/`FOCUS` |
| 进程 AUMID | `Program.cs:17-23`、`WindowHostStyler` | 可用 | 启动前设置，任务栏分组 |
| 系统唤起路径 | `MainWindowViewModel.OpenPathFromSystem`（:2822-2932） | 可用 | 文件→所在目录并选中；跨窗口去重 |
| 文件关联注册 | 无 | 缺口 | 只接管文件夹，不注册文件类型 |
| “发送到”菜单 | 无 | 缺口 | 未创建 `SendTo` 快捷方式 |
| URL 协议 | 无 | 缺口 | 无 `tabtaskexplorer://` |
| 安装器/卸载器 | 无 | 缺口 | README 仍列未完成 |
| 便携模式 | 无 | 缺口 | 配置固定写 `%APPDATA%` |
| 关联恢复/卸载清理 | 半成品 | 半成品 | 关闭默认文件管理器会删键，但无统一卸载清理 |

主要问题：

1. **默认文件管理器接管范围窄**：只写 `Directory\shell`，不处理 `Directory\Background`（文件夹空白处）、驱动器、库；也不校验是否被系统/其他程序覆盖。
2. **无文件类型关联**：用户不能把 `.txt`/`.md`/图片等默认交给本程序；只能手动在系统设置里选。
3. **无“发送到”集成**：右键“发送到 → TabTaskExplorer”缺失。
4. **无安装器**：AUMID、快捷方式、文件关联、卸载清理都靠手工；固定到任务栏可能分组异常。
5. **无便携模式**：绿色版仍写用户 AppData，U 盘/多机使用不便。
6. **单实例协议能力弱**：只支持打开路径/聚焦；无法从系统唤起“新建终端/打开设置”。
7. **无关联状态检测**：设置页勾选“默认文件管理器”后，若注册表被改，UI 仍显示已启用。
8. **无卸载清理**：删除程序后注册表/启动项/关联可能残留。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 默认应用 | 设置 → 默认应用，按协议/文件类型选择（[Microsoft 支持](https://support.microsoft.com/en-us/windows/change-default-apps-in-windows)） | 文件关联应引导系统设置或写 HKCU |
| Windows `SendTo` | `%APPDATA%\Microsoft\Windows\SendTo` 放快捷方式 | 轻量集成，无需管理员 |
| Windows 文件关联 | HKCU `Software\Classes\.ext` + `ProgID` + `shell\open\command` | 仅当前用户，可回滚 |
| AppUserModelID | 快捷方式与进程一致，任务栏分组/固定（[AppUserModelIDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids)） | 安装器必须写入同一 AUMID |
| VS Code / Git | 安装器可选“添加到 PATH/右键菜单/文件关联” | 安装选项化 |
| Everything | 便携模式、服务/托盘、URL 协议 | 便携与协议集成 |

## 3. 推荐方案

### 必做（P0）

1. **默认文件管理器健壮化**
   - 同时处理 `Directory\shell`、`Directory\Background\shell`、驱动器根；写入前备份原值，关闭时精确恢复；检测被覆盖状态。
   - 接入点：`DefaultFileManagerManager`；`SettingsWindow` 基本设置分页。
   - 注意：不能写 HKLM（需要管理员），只做当前用户。

2. **“发送到”快捷方式**
   - 在 `%APPDATA%\Microsoft\Windows\SendTo` 创建 `TabTaskExplorer.lnk`，目标为本程序；关闭设置时删除。
   - 接入点：`DefaultFileManagerManager` 或新增 `WindowsShellIntegrationService`；设置页开关。

3. **文件关联（可选类型）**
   - 提供“常用类型”多选（文本/图片/压缩包），写 HKCU `Software\Classes\.ext` 与 ProgID；或提供“打开 Windows 默认应用设置”深链。
   - 接入点：新增 `WindowsFileAssociationService`；`SettingsWindow` 新增“文件关联”区域。
   - 安全：只改用户级、可一键恢复；不抢系统关键类型（`.exe`/`.bat`/`.ps1`）。

4. **安装器与卸载清理**
   - 安装器写入 AUMID、开始菜单/桌面快捷方式、可选文件关联/SendTo；卸载时删除注册表项、启动项、SendTo、缓存（保留用户设置可选）。
   - 接入点：打包工程/安装脚本；`DefaultFileManagerManager`、`AutoStartManager`、`WindowHostStyler` 提供清理方法。

5. **关联状态检测**
   - 设置页打开时读取注册表实际状态，显示“已启用/已被其他程序覆盖/未启用”；提供“重新应用”。
   - 接入点：`DefaultFileManagerManager.IsEnabled`；`SettingsWindow`。

6. **便携模式**
   - 检测程序目录下 `portable.flag` 或 `data` 文件夹，配置/日志/连接/任务改写到程序目录；启动时提示。
   - 接入点：`AppSettingsStore`、`UiLogger.FilePath`、`JsonConnectionStore`、`JsonTaskStore`；新增 `AppPaths` 服务。

### 可选（P1/P2）

- URL 协议 `tabtaskexplorer://open?path=...`，用于网页/脚本唤起。
- 右键“在 TabTaskExplorer 中打开”（见 `shell-context-menu.md` 的 sparse MSIX）。
- 开机自启延迟/仅登录时启动选项。
- 与 Windows 11 默认应用设置页深链（`ms-settings:defaultapps`）。

### 不建议

- 静默抢占所有文件关联：只允许用户显式选择常用类型。
- 写 HKLM/全局关联：需要管理员且影响所有用户。
- 卸载时删除用户数据：默认保留，提示用户选择。
- 依赖未签名的安装包写 AUMID：会导致任务栏分组/通知异常。

## 4. 交互与视觉

- 设置“系统集成”卡片：默认文件管理器、发送到、文件关联、开机自启、便携模式。
- 每项显示实际状态（已启用/被覆盖/不可用）与“应用/恢复”按钮。
- 文件关联：类型列表 + 复选框 + “仅当前用户”说明；危险类型置灰。
- 便携模式：标题栏/关于页显示“便携模式”徽标。
- 卸载：安装器提供“保留设置/删除全部数据”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 默认程序 | `DefaultFileManagerManager.cs` | Background/驱动器/状态检测 |
| SendTo | 新增 `Platform.Windows/WindowsSendToIntegration.cs` | `.lnk` 创建/删除 |
| 文件关联 | 新增 `Platform.Windows/WindowsFileAssociationService.cs` | HKCU + ProgID |
| 便携模式 | 新增 `Services/AppPaths.cs`；`AppSettingsStore`、`UiLogger`、存储类 | 统一数据根 |
| 安装/卸载 | 打包工程/脚本 | AUMID/快捷方式/清理 |
| 单实例协议 | `SingleInstanceService.cs` | 与工作区模块共用 JSON 协议 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 默认程序 | 注册表被其他程序覆盖、恢复不精确 | M |
| 文件关联 | 用户误选关键类型；回滚 | M |
| SendTo | 快捷方式创建失败/权限 | S |
| 安装器 | 签名、AUMID、升级/卸载 | L |
| 便携模式 | 多处硬编码 AppData 路径 | M |

## 7. 验收标准

1. 启用默认文件管理器后，文件夹与文件夹空白处双击/右键均由本程序打开；关闭后恢复原状。
2. `SendTo` 出现 TabTaskExplorer，右键“发送到”可打开多个文件所在目录。
3. 文件关联仅修改用户选择的类型，且可一键恢复；关键脚本类型不可选。
4. 设置页显示关联实际状态，被覆盖时提示“重新应用”。
5. 安装后任务栏固定/通知分组正常（AUMID 一致）；卸载后无残留注册表/启动项。
6. 便携模式下配置/日志/连接/任务写在程序目录，不写 AppData。
7. 旧配置升级后默认程序/自启状态不丢失。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Windows 默认应用](https://support.microsoft.com/en-us/windows/change-default-apps-in-windows) | 文件关联/默认程序 | 2026-11-03 |
| [AppUserModelIDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids) | 快捷方式/AUMID/任务栏分组 | 2026-11-03 |
| [Windows Shell 关联](https://learn.microsoft.com/en-us/windows/win32/shell/fa-progids) | ProgID/文件关联 | 2026-11-03 |
| [VS Code 安装选项](https://code.visualstudio.com/docs/setup/windows) | 安装器集成选项 | 2026-11-03 |
| [Everything](https://www.voidtools.com/) | 便携模式/协议集成 | 2026-11-03 |
