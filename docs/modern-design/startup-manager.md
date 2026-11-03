# 启动项管理现代化设计

状态：设计稿（2026-11-03）
适用：`StartupManagerViewModel`、`WindowsStartupItemProvider`、`StartupPanel`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 系统启动项枚举 | `WindowsStartupItemProvider.Enumerate`（:41-67） | 可用 | HKCU/HKLM Run/RunOnce、Wow6432Node、启动文件夹、计划任务、Win32 服务 |
| 启用/禁用 | `SetEnabled`（:75-95）、`SetRegistryRunEnabled`（:379）、`SetServiceEnabled`（:497） | 可用 | 禁用时备份原值/启动类型，支持恢复 |
| 权限提示 | `ToggleHint`（服务项）、`isAdministrator` | 可用 | 无管理员权限时提示 |
| 自定义启动项 | `AppSettings.CustomStartupItems`、`StartupItemLauncher`、`BootSessionIdentifier` | 可用 | 程序/快捷方式/脚本/网页，延迟秒数 |
| 开机后首次启动拉起 | `MainWindowViewModel.RunCustomStartupItemsOnBoot`（:629） | 可用 | 按开机会话去重 |
| 标签页 + 设置入口 | `StartupManagerViewModel`（:249）、`SettingsWindow` 第 8 分页 | 可用 | 统一一份管理界面 |
| Web 端镜像 | `WebPageHtml.renderStartup`（:1714） | 可用 | 系统项/自定义项、启停、运行、删除 |
| 启动影响评分 | 无 | 缺口 | 无“启动耗时/CPU/磁盘”评估 |
| 发布者/签名信息 | 无 | 缺口 | 只显示命令行，无法判断来源可信度 |
| 一键恢复/撤销 | 半成品 | 半成品 | 禁用有备份，但无“全部恢复/历史记录” |

主要问题：

1. **只显示“是什么”，不显示“影响多大”**：用户无法判断哪些启动项该禁用。Windows 任务管理器有“启动影响”（高/中/低），本程序没有。
2. **无发布者/签名**：恶意或捆绑启动项与正常程序无法区分。
3. **无延迟管理**：系统项不能设置延迟（自定义项有 `DelaySeconds`）；开机瞬间仍被全部拉起。
4. **提权流程弱**：HKLM/服务需要管理员，但只提示“需要管理员权限”，没有“以管理员重启/单独提权”入口。
5. **无计划任务详情**：只列名称/命令，缺少触发器、上次运行结果、下次运行时间。
6. **无启动项快照/对比**：安装软件后新增了哪些启动项，无法对比。
7. **无安全建议**：不区分“系统必需/厂商更新/用户程序/未知”，也没有“疑似捆绑”提示。
8. **Web 端权限受限**：只读/受限方案下启停按钮禁用，但没有解释原因。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 任务管理器“启动”页 | 名称、发布者、状态、启动影响（高/中/低）；右键打开文件位置/在线搜索 | 发布者 + 影响评分是基准 |
| Autoruns（Sysinternals） | 全量启动项（Run/服务/任务/驱动/Explorer 插件），可跳转注册表/文件，支持 Verified/Unknown | 覆盖面与“打开位置”很重要 |
| Windows 设置“启动应用” | 只列 UWP/部分 Win32，开关式管理 | 简洁开关体验 |
| CCleaner 启动项 | 启用/禁用/删除、延迟 | 延迟管理 |
| Startup Delayer | 按延迟分批启动 | 延迟可减少开机峰值 |
| Windows 任务计划程序 | 触发器、条件、上次结果 | 计划任务需要详情页 |

## 3. 推荐方案

### 必做（P0）

1. **启动影响评分**
   - 采集：启动项对应进程的 CPU/磁盘/内存峰值（可在开机后采样，或用户点击“评估”触发一次）；无进程数据时按“未知”。
   - 评分：高/中/低/未知；展示“CPU 峰值、磁盘读、启动耗时（可选）”。
   - 接入点：`StartupManagerViewModel` 增加 `EvaluateImpactAsync`；复用 `IProcessProvider` / `IProcessDetailService`；`StartupItem` 增加 `ImpactLevel`、`ImpactDetail`。
   - 注意：不要常驻监控；按需评估并缓存 7 天。

2. **发布者与签名**
   - 读取可执行文件签名/公司名/产品名；显示“Microsoft / 未知发布者 / 无签名”。
   - 接入点：`WindowsStartupItemProvider` 增加 `Publisher`、`IsSigned`；`StartupRow` 展示。
   - 风险：签名校验需要 WinTrust API，注意性能（批量延迟加载）。

3. **打开位置与详情**
   - 右键“打开文件所在位置 / 打开注册表项 / 打开任务计划 / 打开服务”。
   - 计划任务显示触发器、上次结果、下次运行；服务显示启动类型、状态、描述。
   - 接入点：`StartupPanel.axaml` 右键菜单；`StartupManagerViewModel` 详情面板。

4. **提权入口**
   - 检测非管理员：顶部横幅“部分系统项需要管理员权限”，提供“以管理员重启本程序”。
   - 不要为单个操作弹 UAC 多次；统一重启提权。
   - 接入点：`WindowsStartupItemProvider` 能力位；`MainWindow`/`Program` 重启参数；`StartupManagerViewModel` 横幅。

5. **快照与对比**
   - 每次打开启动项管理时保存快照（时间 + 条目哈希）；提供“与上次对比”，高亮新增/删除/命令变化。
   - 接入点：`AppSettings` 或独立 `startup-snapshots.json`；`StartupManagerViewModel.ApplyEnumeratedItems`（:487）。

### 可选（P1/P2）

- 延迟启动：为系统项设置延迟（需要本程序接管启动时机并不现实；可生成计划任务/启动脚本，需明确风险）。
- 安全建议：基于签名、路径（Temp/AppData 可疑）、命令行（PowerShell 编码命令）给“建议禁用/谨慎”。
- 一键恢复：恢复所有由本程序禁用的项（已有备份数据）。
- 导出报告 CSV/JSON。
- Web 端完整详情（计划任务/服务）。

### 不建议

- 直接删除启动项（不可逆）：默认只禁用；删除需二次确认 + 备份。
- 自动“优化”并禁用未知项：可能破坏系统/安全软件。
- 接管系统启动顺序（替换 Winlogon/Shell）：高风险，不做。

## 4. 交互与视觉

- 顶部：刷新、评估影响、快照对比、以管理员重启（按需显示）。
- 系统项表：名称、发布者、来源、范围、状态、影响、操作（启停/打开位置）。
- 自定义项表：名称、类型、目标、延迟、状态、运行/编辑/删除。
- 影响列：高=红、中=橙、低=绿、未知=灰；悬停显示采样详情。
- 详情抽屉：右侧 360px，显示命令行、签名、注册表路径、计划任务触发器。
- 对比视图：新增绿色、删除灰色、变更黄色。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 影响评分 | `StartupManagerViewModel.cs`、`Kernel/Models/StartupItem.cs` | 按需评估 + 缓存 |
| 签名/发布者 | `Platform.Windows/WindowsStartupItemProvider.cs` | WinTrust 校验，失败标记未知 |
| 详情/打开位置 | `StartupPanel.axaml(.cs)`、`StartupManagerViewModel.cs` | 计划任务用 `schtasks`/COM |
| 提权 | `Program.cs`、`MainWindow.axaml`、`StartupManagerViewModel.cs` | 重启参数 `--elevated` |
| 快照 | 新增 `Services/StartupSnapshotStore.cs` | JSON，保留最近 20 次 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 影响评分 | 采样干扰用户；无权限进程读不到 | M |
| 签名校验 | 联网吊销检查慢；离线缓存 | M |
| 提权 | 重启丢失未保存状态；单实例冲突 | S/M |
| 计划任务详情 | 不同 Windows 版本字段差异 | M |
| 快照 | 条目 Id 稳定性 | S |

## 7. 验收标准

1. 系统项列表显示发布者/签名状态，未知发布者明确标记。
2. 点击“评估影响”后，常见启动项显示高/中/低，且不造成明显卡顿。
3. 右键可打开文件位置、注册表项、计划任务、服务。
4. 非管理员时顶部提示并可一键以管理员重启；提权后 HKLM/服务可启停。
5. 快照对比能列出新增/删除/命令变化，颜色区分正确。
6. 禁用/恢复后注册表/服务状态与备份一致；异常时给出原因。
7. Web 端在只读权限下按钮禁用并显示权限说明。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| Windows 任务管理器（Windows 11 内置，启动页） | 启动影响、发布者、启停 | 2026-11-03（官方内置功能，无单独文档链接） |
| [Autoruns for Windows（Sysinternals）](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns) | 全量启动项与打开位置 | 2026-11-03 |
| [Windows 任务计划程序](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-start-page) | 触发器/上次结果 | 2026-11-03 |
| [Startup Delayer](https://www.r2.com.au/page/products/show/startup-delayer/) | 延迟启动产品形态 | 2026-11-03 |

注：任务管理器支持链接为通用入口，具体“启动影响”文案以本机 Windows 版本为准；本文不声称逐项核对。
