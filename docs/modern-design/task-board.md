# 任务看板现代化设计

状态：设计稿（2026-11-03）
适用：`TaskBoardViewModel`、`TaskBoardPanel`、`ITaskStore`、`JsonTaskStore`
调研：联网检索（2026-11-03）+ 代码盘点

## 1. 现状

| 能力 | 代码位置 | 成熟度 | 说明 |
| --- | --- | --- | --- |
| 三列看板 | `TaskBoardViewModel.cs:14-126`、`TaskBoardPanel.axaml` | 可用 | 待办/进行中/已完成 |
| 新建任务 | `TaskBoardViewModel.CreateTask`（:75-87） | 可用 | 只输入标题 |
| 状态流转 | `MoveTask`（:90-103） | 可用 | 完成时记录 `CompletedTimeUtc` |
| 删除任务 | `DeleteTask`（:106-112） | 可用 | 立即持久化 |
| 持久化 | `JsonTaskStore`、`TaskItem` | 可用 | JSON 文件 |
| Web 端镜像 | `WebPageHtml.renderBoard`（:1647） | 可用 | 三列只读/操作 |
| 编辑标题/描述 | 无 | 缺口 | 只能新建/删除 |
| 截止日期/优先级/标签 | 无 | 缺口 | `TaskItem` 字段有限 |
| 拖拽流转 | 半成品 | 半成品 | 需确认是否支持拖拽；当前多为按钮 |
| 搜索/过滤/归档 | 无 | 缺口 | 所有任务常驻三列 |
| 提醒/通知 | 无 | 缺口 | 无到期提醒 |
| 撤销 | 无 | 缺口 | 删除不可恢复 |

主要问题：

1. **任务模型太薄**：`TaskItem` 只有标题/状态/完成时间，缺描述、优先级、截止日期、标签、所属项目。
2. **无法编辑**：建错标题只能删除重建。
3. **无拖拽**：状态流转依靠按钮/菜单，不符合看板直觉。
4. **无搜索/过滤**：任务多了以后找不到；无“我的/今天/逾期”视图。
5. **无归档/完成折叠**：已完成列无限增长。
6. **无提醒**：截止日期即使加了也没有通知；与通知模块未联动。
7. **删除不可撤销**：误删无法恢复。
8. **Web 端能力有限**：只能做基本新增/移动/删除，缺少编辑与筛选。

## 2. 业界对照

| 产品/能力 | 做法 | 对本项目启示 |
| --- | --- | --- |
| Trello | 卡片拖拽、标签、截止日期、 checklist、归档 | 看板基础模型 |
| GitHub Projects | 表格/看板、字段、筛选、分组 | 字段与筛选 |
| Focalboard | 看板/表格/画廊、属性、模板（本仓库参考） | 多视图与属性 |
| Plane | 周期、模块、优先级、订阅 | 项目维度 |
| Microsoft To Do | 我的一天、提醒、步骤、重复 | 个人任务体验 |
| Kanboard | 泳道、WIP 限制、搜索 | WIP 与过滤 |

## 3. 推荐方案

### 必做（P0）

1. **任务模型扩展**
   - 增加 `Description / Priority / DueDate / Tags / ProjectId / Order / CreatedTime / UpdatedTime / Archived`。
   - 接入点：`Kernel/Models/TaskItem.cs`、`JsonTaskStore`（兼容旧 JSON）。

2. **编辑与详情**
   - 双击卡片打开详情抽屉：标题、描述、优先级、截止日期、标签、所属项目；自动保存。
   - 接入点：`TaskBoardViewModel` 新增 `UpdateTask`；`TaskBoardPanel.axaml` 详情面板。

3. **拖拽流转**
   - 卡片在三列间拖拽，落点高亮；顺序在列内可调整并持久化 `Order`。
   - 接入点：`TaskBoardPanel.axaml.cs` 拖放；`MoveTask` 增加目标索引。

4. **搜索/过滤/归档**
   - 顶部搜索框；过滤“今天到期/逾期/高优先级/标签”；完成列默认折叠，支持归档。
   - 接入点：`TaskBoardViewModel` 增加 `VisibleTodo/InProgress/Done` 集合；`TaskBoardPanel`。

5. **删除撤销**
   - 删除后 5 秒内可撤销（内存快照），或移入“已归档”而非硬删除。
   - 接入点：`DeleteTask`、`JsonTaskStore`。

### 可选（P1/P2）

- 截止日期提醒（复用通知模块）。
- 重复任务/子任务/依赖。
- 多项目/泳道/WIP 限制。
- 日历视图/表格视图。
- 导入导出 CSV/Markdown；与外部看板（Focalboard/Plane）同步（P2）。
- Web 端完整编辑与筛选。

### 不建议

- 做成完整项目管理（甘特/资源/工时）：与“轻量任务看板”定位冲突。
- 默认硬删除：至少归档/撤销。
- 在 UI 线程全量重写 JSON：任务量大时后台保存。

## 4. 交互与视觉

- 三列等宽，列头显示数量；卡片显示标题、优先级色条、截止日期、标签 chip。
- 拖拽：卡片半透明，落点列高亮，插入位置显示横线。
- 详情抽屉：右侧 360px，标题可编辑，字段分组。
- 逾期：红色日期；今天：橙色；完成：勾选 + 删除线。
- 空列：虚线框 + “添加任务”。
- 已完成列折叠为一行“已完成（N）”。

## 5. 技术实现要点

| 改动 | 文件/服务 | 说明 |
| --- | --- | --- |
| 模型 | `Kernel/Models/TaskItem.cs`、`JsonTaskStore` | 兼容旧字段 |
| 编辑 | `TaskBoardViewModel.cs`、`TaskBoardPanel.axaml` | 自动保存防抖 |
| 拖拽 | `TaskBoardPanel.axaml.cs` | 列内排序 |
| 过滤 | `TaskBoardViewModel.cs` | 可见集合 |
| 撤销 | `TaskBoardViewModel.cs` | 内存快照/归档 |
| 提醒 | `INotificationService` | 到期检查定时器 |

## 6. 风险与工作量

| 项 | 风险 | 工作量 |
| --- | --- | --- |
| 模型迁移 | 旧 JSON 缺字段 | S |
| 拖拽排序 | 与按钮流转冲突 | M |
| 提醒 | 后台定时器/系统休眠 | S/M |
| Web 端 | 快照字段扩展 | M |

## 7. 验收标准

1. 卡片可拖拽到其他列/列内排序，重启后顺序保留。
2. 双击可编辑标题/描述/优先级/截止日期/标签，自动保存。
3. 搜索与“今天/逾期/高优先级”过滤生效。
4. 已完成列可折叠/归档，归档任务可恢复。
5. 删除任务 5 秒内可撤销。
6. 到期任务在通知中心/系统通知提醒（P1）。
7. Web 端与桌面任务字段/操作一致。

## 8. 调研来源

| 来源 | 用途 | 查证日期 |
| --- | --- | --- |
| [Trello](https://trello.com/guide) | 看板卡片/标签/截止日期 | 2026-11-03 |
| [GitHub Projects](https://docs.github.com/en/issues/planning-and-tracking-with-projects) | 字段/筛选/视图 | 2026-11-03 |
| [Focalboard](https://www.focalboard.com/) | 看板/表格/属性 | 2026-11-03 |
| [Plane](https://plane.so/) | 周期/优先级/模块 | 2026-11-03 |
| [Microsoft To Do](https://support.microsoft.com/en-us/todo) | 提醒/步骤/重复 | 2026-11-03 |
