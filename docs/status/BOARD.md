<!-- 本文件由脚本生成，不要手改。 -->
# 任务状态看板

生成时间：2026-11-06 14:55
生成命令：powershell -NoProfile -ExecutionPolicy Bypass -File tools\status\Build-StatusBoard.ps1
数据来源：docs\modern-design\implementation 下的 *progress.md

> 本文件由脚本生成，不要手改。状态词表只有五个：待构建 / 待实测 / 已实测通过 / 阻塞 / 等你决策。

## 等你决策

- 暂无

## 阻塞

- **远程连接**：134 主机已恢复且 root/root123 基础连接与 SFTP 已实测通过，但 ProxyJump 无跳板主机可测；nk8500 账号在服务端认证阶段挂起。 [文档](../modern-design/implementation/remote-progress.md)

## 任务总览

统计：等你决策 0 / 阻塞 1 / 待构建 1 / 待实测 5 / 已实测通过 3（共 10 条）

| 任务线 | 状态 | 卡点 | 下一步 | 文档链接 |
| --- | --- | --- | --- | --- |
| 远程连接 | 阻塞 | 134 主机已恢复且 root/root123 基础连接与 SFTP 已实测通过，但 ProxyJump 无跳板主机可测；nk8500 账号在服务端认证阶段挂起。 | 有跳板主机后补逐跳验证并跑 SshJumpChainTests；nk8500 需在 134 服务端修 PAM/账号。 | [remote-progress.md](../modern-design/implementation/remote-progress.md) |
| Web 对齐 + 统一任务中心（含复制/剪切/粘贴、卡死修复） | 待构建 | `WebPageHtml.cs` 内联回退页未同步侧边栏右键与书签数据；桌面冲突对话框、桌面远程标签粘贴未实测；桌面完整任务中心面板未实现。 | 补齐回退页 → 重新发布 → 做桌面冲突对话框与远程标签粘贴验证（完整任务中心面板另行排期）。 | [progress.md](../modern-design/implementation/web-parity-task-center/progress.md) |
| 截图链路 | 待实测 | 1.0–1.9 已进 2026-11-06 10:27 发布包但没有运行时验证；1.10 滚动长截图已编译、未进发布包；WGC 主路径未开始。 | 重新发布 1.10 → 按文末 7 项清单集中做一次桌面验证 → 再接 WGC 主路径。 | [capture-progress.md](../modern-design/implementation/capture-progress.md) |
| 任务栏单图标 | 待实测 | 需你先清理旧快捷方式（否则任务栏仍走旧行为），之后才能确认只剩一个图标。 | 清理旧快捷方式后运行 tools\diagnose-taskbar.ps1，并人工目视确认任务栏分组与图标数。 | [taskbar-progress.md](../modern-design/implementation/taskbar-progress.md) |
| 文件视图 | 待实测 | 曾构建过一次，但后续又有改动；缩略图滚动、空格预览、列配置持久化与键盘选择均未做桌面集中验证。 | 用最新构建做一次约 10 分钟的桌面集中验证，先浏览器冒烟再桌面。 | [fileview-progress.md](../modern-design/implementation/fileview-progress.md) |
| 样式令牌统一 | 待实测 | 已实施、构建 0 错误、深色网页端验证通过；桌面像素级对照与浅色主题切换未验。 | 做桌面深色像素级对照，再验浅色主题切换。 | [theme-tokens-progress.md](../modern-design/implementation/theme-tokens-progress.md) |
| 右键菜单 | 待实测 | WinMerge 根因（MTA 套间）已修复，独立探针三种目标全命中；应用内「多选 10 次 + 新建文件夹」未验，因旧实例 TabXExplorer64（pid 23248）占用单实例互斥且无法结束。 | 你手动退出旧实例后重启 2026-11-06 09:55 产物，做应用内复测与「待人工验证」4 条。 | [ctxmenu-progress.md](../modern-design/implementation/ctxmenu-progress.md) |
| Web 端 P0-1 | 已实测通过 | 无运行阻塞；后续 Web 相关改动仍需重新冒烟。 | 保持 /api/version、静态资源、会话与 CSRF 的回归冒烟。 | [p0-1-progress.md](../modern-design/implementation/p0-1-progress.md) |
| 构建通道（环境） | 已实测通过 | 无；build.bat / dotnet publish 已多次 exit 0（最近 2026-11-06 10:27）。 | 每次发布后回填产物时间与退出码，保持「产物晚于代码改动」的判据。 | [build-channel-progress.md](../modern-design/implementation/build-channel-progress.md) |
| 日志与诊断 | 已实测通过 | 轮转（1MB 裁剪）与跨天用例、查看器筛选/导出尚未执行，属补充验证，不影响主链路结论。 | 补跑 UiLoggerRotationTests，并在查看器点检筛选、复制与导出。 | [logging-progress.md](../modern-design/implementation/logging-progress.md) |

## 变更日志

- 2026-11-06 14:55 截图链路：待构建 → 待实测（1.0–1.9 已进 2026-11-06 10:27 发布包但没有运行时验证）
- 2026-11-06 14:55 样式令牌统一：新增 → 待实测
- 2026-11-06 14:55 右键菜单：等你决策 → 待实测（WinMerge 根因（MTA 套间）已修复）
- 2026-11-06 14:55 构建通道（环境）：阻塞 → 已实测通过（无）
- 2026-11-04 17:02 初始化：共 9 条（等你决策 1 / 阻塞 2 / 待构建 2 / 待实测 2 / 已实测通过 2）
