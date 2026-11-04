# 任务栏单图标进度

状态：待实测
卡点：需你先清理旧快捷方式（否则任务栏仍走旧行为），之后才能确认只剩一个图标。
下一步：清理旧快捷方式后运行 tools\diagnose-taskbar.ps1，并人工目视确认任务栏分组与图标数。

## 现状

- 设计依据：[taskbar-integration.md](../modern-design/taskbar-integration.md)（AUMID、辅助窗口排除、主窗口唯一入口）。
- 诊断工具：[diagnose-taskbar.ps1](../../../tools/diagnose-taskbar.ps1)，输出可见顶层窗口、任务栏归属、AUMID 与窗口样式。
- 目标：同一进程只保留一个任务栏图标，辅助窗口不单独占位、不污染 Alt+Tab。

## 验证步骤

1. 退出所有 TabTaskExplorer 实例，清理任务栏上指向旧产物的快捷方式。
2. 启动 `output\TabTaskExplorer.exe`，确认任务栏只有一个图标。
3. 打开截图面板、标注编辑器、贴图等辅助窗口，确认任务栏图标数不变。
4. 运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\diagnose-taskbar.ps1`，核对窗口清单与预期一致。
