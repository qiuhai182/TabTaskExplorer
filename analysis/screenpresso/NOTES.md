# Screenpresso 逆向工作笔记（环境 / 方法 / 复现）

本目录独立于其他逆向线（例如 Snipaste），脚本与日志均只落在 `analysis\screenpresso`，未修改 `tools\rea` 或 `output` 下的既有文件。

## 1. 环境结论

| 项 | 值 |
| --- | --- |
| OS | Windows，x64 |
| REA | 6.7.0（`C:\Users\QiuHai\AppData\Roaming\npm\node_modules\rea-agents`） |
| Node | `C:\Program Files\nodejs\node.exe`（26.3.1） |
| Ghidra | `C:\tools\ghidra_12.1.4_PUBLIC`（12.1.4） |
| JDK | `C:\tools\jdk-21`（Temurin 21.0.12.1） |
| ILSpy | `C:\Users\QiuHai\.dotnet\tools\ilspycmd.exe`（11.1.0.9782） |
| FFmpeg | 与本轮无关（Screenpresso 自带 VFW/MF，不走 FFmpeg） |

`rea doctor` 结果：Ghidra 全项 healthy；Hopper/IDA/JADX/Kuna 未配置。Ghidra 的 Windows P0 只接受**原生** PE；受管 PE 会被拒绝，错误码 `managed_target_unsupported`。

## 2. 终端坑与稳定启动方式

1. **沙箱后端间歇故障**：Trae Shell 会连续返回 `sandbox backend failed`，连 `Get-Date` 也失败；`dangerouslyDisableSandbox` 也可能无效。这是执行后端问题，不是权限问题。
2. **前台 Shell 子进程会被回收**：前台 `Start-Process` 启动的 Ghidra/分析进程会随命令结束被 Job Object 杀掉。
3. **并行 WMI 会互相阻塞**：两个 `Invoke-CimMethod Win32_Process Create` 同时执行时，两个都可能长时间不返回；必须串行。
4. **可用组合（本轮验证）**：
   - 后台 Shell 命令 + `Start-Process powershell.exe -File <worker>` + 父进程 `Start-Sleep` 保活；
   - 或 `Start-Worker.ps1`（WMI → PowerShell → Start-Process → 工作脚本），一次只发一个任务；
   - 轮询日志与 `.exitcode` 文件，不依赖 Shell 返回值。
5. **Ghidra 超时**：REA 默认启动时限 330,000 ms；`Run-Rea-Screenpresso.ps1` 提升到 3,600,000 ms，`GHIDRA_HEADLESS_MAXMEM=4G`。本次 167 KB 原生 DLL 分析 59 秒完成；18 MB 受管 PE 在导入阶段即被拒绝，不需要长等待。

## 3. 目标定位

```powershell
# 注册表 App Paths
Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Screenpresso.exe'
# 运行进程
Get-Process Screenpresso | Select-Object Id,Path,ProductVersion
# 安装目录
Get-ChildItem 'C:\Users\QiuHai\AppData\Local\Learnpulse\Screenpresso'
```

安装目录可直接读取，无需管理员权限；仍在 `C:\Users\QiuHai\.cache\rea-screenpresso-target` 建立了工作副本，避免 Ghidra 的 mutable-path 准入限制。

## 4. 脚本说明

### `Invoke-StaticProbe.ps1`

纯 PowerShell，不依赖外部工具。读取 PE/CLR/资源/导入导出/调试目录/Rich 头/字符串/关键字。

关键参数：

- `-TargetPath`：目标文件
- `-OutDir`：证据输出目录
- `-SkipStrings`：只做 PE/CLR 快速取证（18 MB 主程序建议先用这个）

产出：`pe-info.md`、`imports.txt`、`exports.txt`、`resources.txt`、`manifest.xml`、`version-info.txt`、`debug-directory.txt`、`clr-header.txt`、`rich-header.txt`、`strings-ascii.txt`、`strings-utf16.txt`、`keyword-hits.md`、`static-probe-summary.json`、`static-probe.log`、`static-probe.exitcode`。

> 主程序完整字符串共 778,299 条 ASCII / 168,315 条 UTF-16，关键字矩阵跑得很慢；实际报告主要用 `-SkipStrings` + 定向 `Grep`。`keyword-hits.md` 只在编解码器上完整生成。

### `Run-Rea-Screenpresso.ps1`

与 `tools\rea\Run-Rea.ps1` 相同的命令语义，但：

- 独立锁：`analysis\screenpresso\ghidra\rea.lock`
- 独立日志目录
- 默认目标/快照指向 Screenpresso
- 超时 1 小时、堆 4G

支持 `analyze / search / xrefs / decompile / function / trace / doctor / version`。

### `Start-Worker.ps1`

WMI 启动游离 PowerShell，写入 `<TaskName>.task.json`（含进程号、实际命令行、返回码）。

注意：`WorkerArguments` 以空格拆分，路径不能含空格；如需带空格的模式，请改写为数组参数或避免空格。

### `Invoke-All.ps1`

入口封装（`probe / rea-analyze / rea-search / rea-doctor`）。当前仍是早期版本，建议直接调用前三个脚本。

## 5. 本轮实际执行顺序

1. `Invoke-StaticProbe.ps1 -SkipStrings` 取主程序 PE/CLR/资源/导入导出。
2. `Invoke-StaticProbe.ps1` 完整取字符串（只跑到字符串落盘；关键字矩阵因耗时被中止）。
3. 同样流程取 `ScreenpressoCapture.exe` 与 `ScreenpressoCodec.dll`（含 `SysWOW64` 的 32 位镜像）。
4. `ilspycmd -l c` / `--list-resources` 取 .NET 类型与资源清单（主程序 2,326 类，Capture 1,725 类）。
5. `Run-Rea-Screenpresso.ps1 -Command analyze` 分析 `screenpresso_codec.dll`：428 procedures / 406 strings，59 秒。
6. `-Command search -Pattern 'SPV1|vidc|SetSpriteMode|DriverProc'`：只命中导出名（SPV1 是代码立即数，不是 Ghidra 定义字符串）。
7. `-Command decompile -Address 0x1800067e0`（DriverProc）与 `0x1800074d0`（ICM_COMPRESS_GET_FORMAT）取得伪代码，确认 `0x31565053` = `SPV1`。
8. 定向字符串检索（`Grep`）补齐录屏/热键/剪贴板/系统集成/分享证据。

## 6. 已知限制

- `keyword-hits.md` 是在二进制噪声上做正则统计，包含明显误报（例如随机字节命中 `Qt5`、`CEF`、`TForm`）；报告中只采用与上下文一致、且经定向检索复核的条目（如 `VideoForWindows`、`SPV1`、`RegisterHotKey`）。
- `ilspycmd -t <type>` 与 `--dump-table` 在本机对该 18 MB 程序集长时间不返回（`-l c` / `--list-resources` 正常），因此本轮方法级证据以元数据字符串 + 类型清单为主；未强杀进程前已确认不是参数错误。
- Ghidra 的 `search` 只检索已定义字符串；`SPV1`/`vidc` 位于代码立即数，需通过反编译确认。
- 未做动态调试/内存 dump；所有行为参数（默认热键、默认码率、GIF 生成路径）仍属推断或未证实。
- 安装包/更新器/许可校验（`fum.bin`、ActivationKey）未深入分析。

## 7. 快速复查入口

- 主程序结构：`evidence\pe-info.md`、`evidence\clr-header.txt`、`evidence\debug-directory.txt`、`evidence\imports.txt`
- 主程序类型：`evidence\ilspy-types-main.txt`
- 捕获组件类型：`evidence\ilspy-types-capture.txt`
- 字符串：`evidence\strings-ascii.txt`、`evidence\strings-utf16.txt`
- 编解码器：`evidence\codec-dll\`、`ghidra\rea-decompile-20261103-102103.log`、`ghidra\rea-decompile-20261103-102306.log`
- 分析快照：`ghidra\screenpresso_codec.snapshot`
