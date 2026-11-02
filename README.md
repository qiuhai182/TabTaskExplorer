???????# TabTaskExplorer

跨平台的 Windows 风格 **资源管理器 + 任务管理器** 二合一桌面应用。

资源管理器对标 TabX Explorer（多标签 + 快捷访问 + 批量改名），并集成 **远程连接、远程终端、本机终端与网络文件夹**；任务侧提供任务看板与进程监控。所有能力容纳在同一个标签容器中。

## 功能列表

### 资源管理器
- 多标签浏览：自由新建 / 关闭 / 切换，可恢复上次会话的标签
- 地址栏导航，后退 / 前进 / 上一级 / 刷新，"此电脑"磁盘总览
- 详细信息 / 大图标 / 中图标视图切换，点击列头排序
- 勾选式多选：重命名、删除（Windows 移入回收站）、复制 / 剪切 / 粘贴
- 目录 / 文件比对：勾选两项即可打开比对标签，目录递归显示相同 / 不同 / 仅单侧 / 较新状态，文件按行对齐并高亮行内差异（WinMerge 逆向成果合并）
- 批量改名：查找替换、前后缀、序号、扩展名修改，实时预览
- 当前目录搜索过滤、信息窗格、右键菜单、双击空白返回上级
- 快捷访问固定、书签栏、此电脑常用位置
- 显示隐藏文件 / 文件扩展名（设置内切换）

### 远程与终端（开发中）
- 连接管理（已完成）：SSH / FTP 主机管理，新增 / 编辑 / 删除 / 测试连接（TCP 可达性），配置导出导入，凭据 DPAPI 加密（Windows），Xshell 会话导入（.xsh / .xshf）
- 远程终端（已完成 v1）：SSH 远程 Shell，窗口尺寸同步到远程 pty，行模式控制台 + 会话日志（参考 Xshell 会话日志），xterm.js 渲染待接入
- 网络文件夹（已完成 v1）：SFTP 远程目录作为文件标签打开，浏览 / 改名 / 删除 / 批量改名全功能复用
- 文件传输（已完成 v1）：SFTP 上传下载队列、64KB 分块、进度显示、暂停 / 继续、断点续传（参考 Xftp 传输队列）
- FTP 协议（已完成 v2）：FTP 网络文件夹与传输队列（FluentFTP），与 SFTP 共用同一套传输体验
- 传输增强（已完成 v2）：文件夹递归传输、失败自动重试（3 次 / 间隔 3 秒），队列逻辑下沉内核模板基类，SFTP / FTP 共享
- 远程文件在线编辑（已完成 v2）：远程文件双击进入编辑器标签，Ctrl+S 直接回传保存到远程
- 终端快捷命令栏（已完成 v2）：预设快捷命令一键下发，断线一键重连按钮
- 每主机保活（已完成 v2）：连接配置支持每主机独立的保活秒数
- SFTP 权限修改（已完成 v2）：右键 chmod 修改远程文件权限
- 全盘搜索（已完成 v2）：Windows 下通过 Everything IPC 毫秒级搜索，Everything 未运行自动回退内置遍历搜索

### 媒体查看器
- 双击图片 / 视频 / 音频即新开标签页就地播放，不再跳转外部程序
- 标签右侧播放列表侧边栏：自动列出同目录同类媒体（可切换为同目录全部媒体），点击即切换，支持折叠
- 图片：滚轮锚点缩放（10%–1600%）、拖拽平移、旋转、适应窗口 / 原尺寸、上一张 / 下一张、大图按上限降采样解码
- 视频：播放 / 暂停、进度拖拽、音量、倍速、全屏、音轨切换、外挂字幕加载、画面截图
- 音频：封面位 + 元数据 + 播放控制，播放结束自动下一项
- 打开方式可配置：内置查看器 / 系统默认程序 / 每次询问（设置中心「媒体」分页）
- 解码后端 LibVLC（Windows 随包分发，Linux / macOS 使用系统 libvlc），引擎缺失时自动降级并提示改用系统程序

### 截图与录屏（Screenpresso 逆向成果合并）
- 全屏 / 主屏 / 活动窗口 / 区域截图，区域截图使用全屏遮罩框选，支持 3 / 5 / 10 秒延迟与「上次区域」复用
- 截图自动保存为 PNG，按设置复制文件到系统剪贴板
- 屏幕录制：全屏 / 区域 / 活动窗口，帧率 24 / 30 / 60 可选，H.264（MP4）编码，可包含鼠标指针与麦克风
- 录屏依赖 FFmpeg：自动查找程序目录、tools 目录与系统 PATH，也可在设置中心手动指定
- 全局快捷键：Windows 使用 RegisterHotKey 在任意程序前台触发截图与录屏，按键可在设置中心自定义
- 截图 / 录屏完成后自动新建（或复用）产物文件夹标签页，产物历史支持双击打开、复制路径与删除

### 任务管理
- 任务看板：待办 / 进行中 / 已完成三列流转，本地持久化
- 进程监控：进程列表与 CPU / 内存摘要

### 设置中心
- 统一快捷键中心：任意功能都可以绑定任意多条组合，每条可独立选择「应用内 / 全局」作用域
- 全局快捷键在 Windows 上使用系统级键盘钩子，可覆盖其他程序注册的快捷键；Ctrl+Alt+Del、Win+L 等系统保留组合无法覆盖。Linux / macOS 目前仅支持应用内快捷键
- 截图与录屏：产物目录、FFmpeg 路径、延迟 / 帧率 / 画质 / 麦克风
- 开机启动（Windows / Linux / macOS）、接管文件夹打开方式（Windows）
- 工具栏 / 书签栏显隐、恢复上次会话标签

## 技术栈

| 层 | 技术 |
| --- | --- |
| UI | Avalonia 12 + FluentAvalonia（WinUI 3 观感，默认深色） |
| 内核 | .NET 10 类库，平台无关抽象（文件系统 / 进程 / 系统指标 / 任务存储 / 远程连接 / 终端会话） |
| 远程协议 | SSH.NET（SSH / SFTP）· FluentFTP（FTP） |
| 终端 | ConPTY（Windows）· PTY（Unix）· WebView + xterm.js 渲染 |
| 平台适配 | Windows（Win32 / 性能计数器）· Linux（/proc）· macOS（sysctl） |
| 持久化 | JSON（设置 / 任务 / 书签 / 会话 / 连接配置） |
| 测试 | xUnit |

## 构建与运行

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download) 后：

```bash
git clone https://github.com/<your-username>/TabTaskExplorer.git
cd TabTaskExplorer
dotnet run --project src/TabTaskExplorer.App
```

运行单元测试：

```bash
dotnet test TabTaskExplorer.sln
```

### 打包（Windows）

仓库根目录提供一键构建脚本，产物输出到 `output\`：

```bat
build.bat        :: 框架依赖单文件 output\TabTaskExplorer.exe（需安装 .NET 10 运行时）
build.bat full   :: 自包含单文件（无需运行时，体积较大）

start.bat        :: 启动应用（已在运行时提示）
stop.bat         :: 停止应用（应用关闭即缩到托盘，故为强制结束）
restart.bat      :: 重启应用
```

### 浏览器调试镜像

应用启动时自动在 `http://127.0.0.1:15919/` 开启一个仅绑定本机回环地址的调试镜像服务：浏览器打开后看到的页面与程序界面**实时同构**（标签条、文件列表、任务看板、连接管理全部同步，1.5 秒刷新），点击镜像页中的标签还可以反向切换程序界面——借鉴 TabX Explorer 浏览器内核 UI 的调试体验。

## 架构

内核提供跨平台一致的标准接口，应用层只依赖抽象接口，平台与协议差异收敛在适配工程——同一份代码在三个平台上编译行为一致。

```text
src/
  TabTaskExplorer.Kernel             # 内核：领域模型 + 抽象接口（零平台依赖）
  TabTaskExplorer.Platform.Windows   # Windows 实现（回收站 / 性能计数器 / Win32 / ConPTY）
  TabTaskExplorer.Platform.Linux     # Linux 实现（/proc / PTY）
  TabTaskExplorer.Platform.MacOS     # macOS 实现（sysctl / PTY）
  TabTaskExplorer.Protocol.Ssh       # SSH / SFTP 协议适配：远程终端 / 网络文件夹 / 传输队列
  TabTaskExplorer.Protocol.Ftp       # FTP 协议适配（网络文件夹 / 传输队列）
  TabTaskExplorer.App                # Avalonia UI 与应用层
  TabTaskExplorer.Tests              # 单元测试
```

关键设计：远程目录是 `IFileSystemProvider` 的又一个实现（SFTP 适配），因此远程文件夹打开为普通文件标签后，批量改名、搜索、信息窗格等既有能力**全部自动复用**；本机终端与远程终端共用 `ITerminalSession` 抽象，渲染统一走 WebView + xterm.js。

## 路线图

### 远程与终端
- [x] M1 连接管理：主机配置、凭据加密存储（Windows DPAPI，其他平台留空降级）、导出导入
- [x] M2 远程终端（v1）：SSH Shell + 行模式控制台 + 会话日志 + pty 尺寸同步（xterm.js 渲染待接入）
- [x] M3 网络文件夹（v1）：SFTP 文件提供者接入文件标签，传输队列 + 断点续传
- [ ] M4 本机终端：ConPTY / PTY 接入统一终端面板
- [x] M5 增强（已完成）：FTP 协议（FluentFTP 网络文件夹 + 传输）、Xshell 会话导入（.xsh / .xshf）、文件夹递归传输与失败重试、远程文件在线编辑、终端快捷命令与断线重连、每主机保活、SFTP 权限修改、Everything 全盘搜索
- [ ] 密钥管理：SSH 密钥对生成与托管（.xfpf 字段映射已完成，见 sshtools 参考模块分析报告）

### 界面与体验
- [x] IDE 模式（v1）：VSCode 风格轻量编辑工作区——模式一键切换、懒加载资源树、本地/远程（SFTP）文本编辑与保存回传、编码自动探测（BOM → UTF-8 → GBK）、脏标记与关闭确认、行号栏、Ctrl+P 快速打开、当前文件查找替换与工作区搜索结果面板；不做语法高亮、调试器、LSP、Git（见 `docs/ide-mode-design.md`）
- [x] 打开文件方式（v1）：资源管理器模式始终用系统默认程序；IDE 模式可设置「全部类型默认优先级」与「按文件类型规则」，逐类型指定优先内置编辑器或优先系统默认程序（规则页在设置中心「打开方式」）
- [x] 内置媒体查看器（v1）：图片 / 视频 / 音频标签页播放 + 同目录播放列表侧边栏，详见 `docs/media-viewer-design.md`
- [ ] 文件类型彩色图标与图片缩略图
- [ ] 自绘标题栏（标签页融入标题栏）
- [ ] 鼠标拖拽移动 / 复制文件

### 发布
- [x] build.bat 一键构建脚本（Windows，输出到 output）
- [ ] 安装器与多平台发布包

## 参与贡献

欢迎 Issue 与 PR。提交代码前请先运行 `dotnet test` 确认全部通过。

## 致谢

- TabX Explorer —— 资源管理器交互对标（闭源，仅参考体验）
- SSH Tools —— 远程终端与文件传输的功能设计参考
- [FluentAvalonia](https://github.com/amwx/FluentAvalonia) · [SSH.NET](https://github.com/sshnet/SSH.NET) · [xterm.js](https://github.com/xtermjs/xterm.js)

## 开源许可

本项目基于 [MIT License](LICENSE) 开源。
