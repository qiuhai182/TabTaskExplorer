<!-- 本文件由脚本生成，不要手改。 -->
# 任务状态看板

生成时间：2026-11-04 17:03
生成命令：powershell -NoProfile -ExecutionPolicy Bypass -File tools\status\Build-StatusBoard.ps1
数据来源：docs\modern-design\implementation 下的 *progress.md

> 本文件由脚本生成，不要手改。状态词表只有五个：待构建 / 待实测 / 已实测通过 / 阻塞 / 等你决策。

## 等你决策

- **右键菜单**：WinMerge（以及 TortoiseGit / 7-Zip）扩展项枚举不到，是否继续排查 Shell 扩展加载需你拍板；菜单单例与主题修复本身仍待人工实测。 [文档](../modern-design/implementation/ctxmenu-progress.md)

## 阻塞

- **构建通道（环境）**：Trae Shell 沙箱后端故障，无法在本会话执行构建或测试。 [文档](../modern-design/implementation/build-channel-progress.md)
- **远程连接**：134 主机（192.168.1.134:22）TCP 不可达，ProxyJump 无跳板主机可测。 [文档](../modern-design/implementation/remote-progress.md)

## 任务总览

统计：等你决策 1 / 阻塞 2 / 待构建 2 / 待实测 2 / 已实测通过 2（共 9 条）

| 任务线 | 状态 | 卡点 | 下一步 | 文档链接 |
| --- | --- | --- | --- | --- |
| 构建通道（环境） | 阻塞 | Trae Shell 沙箱后端故障，无法在本会话执行构建或测试。 | 沙箱恢复后执行 stop.bat → build.bat；或由你手动构建并回填日志路径与退出码。 | [build-channel-progress.md](../modern-design/implementation/build-channel-progress.md) |
| 右键菜单 | 等你决策 | WinMerge（以及 TortoiseGit / 7-Zip）扩展项枚举不到，是否继续排查 Shell 扩展加载需你拍板；菜单单例与主题修复本身仍待人工实测。 | 你确认取舍后，按「待人工验证」4 条做实测（连续右键、失焦关闭、深浅主题、扩展子菜单）。 | [ctxmenu-progress.md](../modern-design/implementation/ctxmenu-progress.md) |
| 远程连接 | 阻塞 | 134 主机（192.168.1.134:22）TCP 不可达，ProxyJump 无跳板主机可测。 | 主机恢复后复测指纹确认与逐跳连接；先跑 SshJumpChainTests 并回填构建结论。 | [remote-progress.md](../modern-design/implementation/remote-progress.md) |
| Web 对齐 + 统一任务中心（含复制/剪切/粘贴、卡死修复） | 待构建 | 本轮改动未编译（含复制/剪切/粘贴冲突策略、剪贴板探测移出 UI 线程、UiWatchdog、托盘退出看门狗）；桌面完整任务中心面板尚未实现。 | stop.bat → build.bat 后，按 §7 八条验收清单做浏览器与桌面验证。 | [progress.md](../modern-design/implementation/web-parity-task-center/progress.md) |
| 截图链路 | 待构建 | 剪贴板双格式、默认不复制、主窗口避让、失败气泡、完成后激活等改动未编译；滚动长截图与 WGC 主路径未开始。 | 构建后集中做一次桌面验证（剪贴板粘贴、主窗口避让、失败气泡），再排期 WGC 与滚动长截图。 | [capture-progress.md](../modern-design/implementation/capture-progress.md) |
| 任务栏单图标 | 待实测 | 需你先清理旧快捷方式（否则任务栏仍走旧行为），之后才能确认只剩一个图标。 | 清理旧快捷方式后运行 tools\diagnose-taskbar.ps1，并人工目视确认任务栏分组与图标数。 | [taskbar-progress.md](../modern-design/implementation/taskbar-progress.md) |
| 文件视图 | 待实测 | 曾构建过一次，但后续又有改动；缩略图滚动、空格预览、列配置持久化与键盘选择均未做桌面集中验证。 | 用最新构建做一次约 10 分钟的桌面集中验证，先浏览器冒烟再桌面。 | [fileview-progress.md](../modern-design/implementation/fileview-progress.md) |
| Web 端 P0-1 | 已实测通过 | 无运行阻塞；后续 Web 相关改动仍需重新冒烟。 | 保持 /api/version、静态资源、会话与 CSRF 的回归冒烟。 | [p0-1-progress.md](../modern-design/implementation/p0-1-progress.md) |
| 日志与诊断 | 已实测通过 | 轮转（1MB 裁剪）与跨天用例、查看器筛选/导出尚未执行，属补充验证，不影响主链路结论。 | 补跑 UiLoggerRotationTests，并在查看器点检筛选、复制与导出。 | [logging-progress.md](../modern-design/implementation/logging-progress.md) |

## 变更日志

- 2026-11-04 17:02 初始化：共 9 条（等你决策 1 / 阻塞 2 / 待构建 2 / 待实测 2 / 已实测通过 2）
