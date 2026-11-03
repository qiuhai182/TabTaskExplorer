# 进程监控现代化设计

状态：设计稿（2026-11-03）
适用：`ProcessPanelViewModel`、`ProcessPanel`、`ProcessTrendChart`、`IProcessProvider`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 进程列表 | `ProcessPanelViewModel.Rows`（:70）、`RefreshInBackground`（:551） | 可用 | 定时刷新，后台采样 |
| CPU / 内存摘要 | `ProcessPanelViewModel.SummaryText`、`ProcessTrendChart` | 可用 | 系统与选中进程走势，最近 60 次采样 |
| 进程树 | `Kernel/Services/ProcessTreeBuilder.cs`、`SelectRowLevel`（:394） | 可用 | 父子缩进 |
| 进程详情 | `IProcessDetailService`、`LoadDetail`（:798） | 可用 | 用户、线程、句柄、优先级、IO、启动时间、命令行 |
| 结束进程 | `TerminateSelected`（:426）、`TerminateInBackground`（:565） | 可用 | 单个结束 |
| 优先级 | `ApplyPrioritySelected`（:443）、`SetPriorityInBackground`（:580） | 可用 | Idle…Realtime |
| CPU 亲和性 | `ApplyAffinitySelected`（:481）、`AffinityMaskUtility` | 可用 | 核心编号/掩码 |
| 前台/可见时刷新 | `SetForeground`（:280）、`NotifyVisible`（:270） | 可用 | 不可见时暂停，降低开销 |
| 进程搜索/过滤 | 无 | 缺口 | 只能滚动找 |
| 结束进程确认 | 半成品 | 半成品 | 需确认是否有二次确认/危险提示 |
| 管理员权限 | 半成品 | 半成品 | 受保护进程操作失败时提示不足 |
| 列配置/持久化 | 无 | 缺口 | 固定列，重启恢复默认 |
| GPU / 网络 / 磁盘细项 | 部分 | 半成品 | IO 字节有，GPU/网络无 |

主要问题：

1. **无搜索/过滤**：几百个进程时定位困难；无“仅用户进程/仅服务/仅高占用”过滤。
2. **列不可配置**：不能隐藏列、调整顺序、按多列排序；列宽不持久化。
3. **危险操作缺少防护**：结束系统关键进程/Realtime 优先级没有强确认与后果说明。
4. **权限提示弱**：结束受保护进程失败时只显示通用错误，没有“以管理员重启”入口。
5. **进程树刷新会重建行**：展开状态/选中项在刷新后可能丢失（`ProcessRow` 每次重建）。
6. **无 GPU/网络/磁盘占用**：现代任务管理器已提供这些维度。
7. **无挂起/恢复、作业对象**：不能暂停进程或按进程树结束。
8. **跨平台差异**：Linux `/proc`、macOS `sysctl` 的字段能力不同，UI 未标注“不可用”。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Windows 任务管理器 | 进程/性能/应用历史、GPU/网络、结束进程树、效率模式 | 搜索、列配置、GPU、进程树 |
| Process Explorer | 句柄/线程/模块、作业对象、挂起/恢复、 VirusTotal | 高级详情与作业对象 |
| Activity Monitor (macOS) | 进程/能耗/网络/磁盘、采样 | 多维度指标 |
| htop / btop | 搜索、过滤、树、排序、杀进程 | 键盘与过滤体验 |
| Sysinternals Autoruns | 启动项关联 | 与启动项模块联动 |

## 3. 推荐方案

### 必做（P0）

1. **搜索与过滤**
   - 顶部搜索框按进程名/PID/用户/命令行过滤；快捷过滤“用户进程/服务/高占用/无响应”。
   - 接入点：`ProcessPanel.axaml`、`ProcessPanelViewModel.ApplyFilter`（新增）。

2. **列配置与持久化**
   - 显示/隐藏列（PID、用户、CPU、内存、磁盘、网络、GPU、线程、句柄、命令行）；列宽持久化。
   - 接入点：`ProcessPanel.axaml` 动态列；`AppSettings.ProcessColumns`。

3. **危险操作防护**
   - 结束进程二次确认，显示进程名/PID/用户/是否系统进程；Realtime 优先级红色警告；批量结束走进程树。
   - 接入点：`TerminateSelected`、`ApplyPrioritySelected`；新增确认对话框。

4. **权限与提权**
   - 失败时区分“拒绝访问/进程不存在/受保护”，提供“以管理员重启本程序”。
   - 接入点：`ProcessPanelViewModel`、`ProcessDetail` 错误分类；`Program` 提权参数。

5. **刷新不丢状态**
   - 按 PID 复用 `ProcessRow`，保留展开/选中/排序；仅更新字段。
   - 接入点：`ReorderRowsInPlace`（:336）、`ApplySample`（:668）。

6. **进程树操作**
   - 右键“结束进程树”“挂起/恢复”（Windows 需 Job Object/NtSuspendProcess，其他平台尽力）。
   - 接入点：`ProcessTreeBuilder`、`IProcessDetailService` 增加方法；平台实现。

### 可选（P1/P2）

- GPU 占用（Windows PDH/GPU Engine 计数器）、网络速率（ETW/`GetExtendedUdpTable`）。
- 效率模式/资源限制（Windows EcoQoS）。
- 进程快照对比、导出 CSV。
- 服务/容器/虚拟机分组。
- 历史曲线持久化与告警阈值。

### 不建议

- 内核驱动级监控：安全与签名成本高。
- 默认结束进程树：误杀风险，必须确认。
- 在 UI 线程轮询进程：保持后台采样。

## 4. 交互与视觉

- 顶部：搜索、过滤 chip、刷新间隔、暂停刷新、结束进程、优先级/亲和性。
- 表头：可拖动列宽、点击排序、右键选择列。
- 行：高 CPU/内存用橙色/红色强调；系统进程图标。
- 详情抽屉：右侧 360px，分组显示基本信息/IO/命令行。
- 走势图：系统 CPU/内存/选中进程三条线，0/50/100% 网格。
- 危险确认：红色标题“结束系统进程？” + 进程信息 + 取消默认焦点。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 过滤 | `ProcessPanelViewModel.cs`、`ProcessPanel.axaml` | 保留原始集合 |
| 列配置 | `AppSettings`、`ProcessPanel.axaml` | 动态列 |
| 状态保留 | `ProcessPanelViewModel.cs` | PID 复用行 |
| 进程树操作 | `IProcessDetailService`、`Platform.Windows/WindowsProcessDetailService.cs` | Job Object |
| 提权 | `Program.cs`、`ProcessPanelViewModel.cs` | 管理员重启 |
| GPU/网络 | 新增 `Platform.Windows/WindowsProcessCounters.cs`（P1） | PDH/ETW |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 状态保留 | 行复用与排序冲突 | M |
| 进程树操作 | 平台 API/权限 | M |
| GPU/网络 | 计数器兼容性/性能 | M/L |
| 提权 | 重启丢失状态 | S |

## 7. 验收标准

1. 搜索进程名/PID/用户/命令行 1 秒内过滤；清空恢复全量。
2. 列可显示/隐藏/拖动，重启后保留。
3. 结束系统进程/Realtime 优先级有强确认，默认取消。
4. 刷新 10 次后展开状态与选中项保持不变。
5. 权限失败提示可区分原因，并提供管理员重启入口。
6. 结束进程树只影响目标树，日志记录每个 PID。
7. Linux/macOS 不可用字段显示“当前平台不支持”，不显示错误值。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| Windows 任务管理器（Windows 11 内置） | 进程/性能/GPU 产品形态 | 2026-11-03（官方内置功能，无单独文档链接） |
| [Process Explorer](https://learn.microsoft.com/en-us/sysinternals/downloads/process-explorer) | 句柄/作业对象/挂起 | 2026-11-03 |
| [.NET Process API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process) | 进程信息/结束/优先级 | 2026-11-03 |
| [htop](https://htop.dev/) | 搜索/过滤/树 | 2026-11-03 |
| [Activity Monitor](https://support.apple.com/guide/activity-monitor/welcome/mac) | 多维度指标 | 2026-11-03 |
