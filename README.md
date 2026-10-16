# TabTaskExplorer

跨平台的 Windows 风格 **资源管理器 + 任务管理器** 二合一桌面应用。

资源管理器对标 TabX Explorer（多标签 + 快捷访问 + 批量改名），并集成 **远程连接、远程终端、本机终端与网络文件夹**；任务侧提供任务看板与进程监控。所有能力容纳在同一个标签容器中。

## 功能列表

### 资源管理器
- 多标签浏览：自由新建 / 关闭 / 切换，可恢复上次会话的标签
- 地址栏导航，后退 / 前进 / 上一级 / 刷新，"此电脑"磁盘总览
- 详细信息 / 大图标 / 中图标视图切换，点击列头排序
- 勾选式多选：重命名、删除（Windows 移入回收站）、复制 / 剪切 / 粘贴
- 批量改名：查找替换、前后缀、序号、扩展名修改，实时预览
- 当前目录搜索过滤、信息窗格、右键菜单、双击空白返回上级
- 快捷访问固定、书签栏、此电脑常用位置
- 显示隐藏文件 / 文件扩展名（设置内切换）

### 远程与终端（开发中）
- 连接管理：SSH / FTP 主机管理，新增 / 编辑 / 删除 / 测试连接，配置导出导入
- 远程终端：SSH 远程 Shell，窗口尺寸同步到远程 pty，xterm.js 渲染
- 本机终端：PowerShell / bash 直连，Windows ConPTY 与 Unix PTY 双实现
- 网络文件夹：SFTP 远程目录作为文件标签打开，浏览 / 改名 / 删除 / 批量改名全功能复用
- 文件传输：上传下载进度、暂停 / 继续、断点续传、任务队列

### 任务管理
- 任务看板：待办 / 进行中 / 已完成三列流转，本地持久化
- 进程监控：进程列表与 CPU / 内存摘要

### 设置中心
- 全部快捷键自定义，一键恢复默认
- 开机启动（Windows / Linux / macOS）、接管文件夹打开方式（Windows）
- 工具栏 / 书签栏显隐、恢复上次会话标签

## 技术栈

| 层 | 技术 |
| --- | --- |
| UI | Avalonia 12 + FluentAvalonia（WinUI 3 观感，默认深色） |
| 内核 | .NET 10 类库，平台无关抽象（文件系统 / 进程 / 系统指标 / 任务存储 / 远程连接 / 终端会话） |
| 远程协议 | SSH.NET（SSH / SFTP）· FluentFTP（FTP，规划中） |
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

## 架构

内核提供跨平台一致的标准接口，应用层只依赖抽象接口，平台与协议差异收敛在适配工程——同一份代码在三个平台上编译行为一致。

```text
src/
  TabTaskExplorer.Kernel             # 内核：领域模型 + 抽象接口（零平台依赖）
  TabTaskExplorer.Platform.Windows   # Windows 实现（回收站 / 性能计数器 / Win32 / ConPTY）
  TabTaskExplorer.Platform.Linux     # Linux 实现（/proc / PTY）
  TabTaskExplorer.Platform.MacOS     # macOS 实现（sysctl / PTY）
  TabTaskExplorer.Protocol.Ssh       # SSH / SFTP 协议适配：远程终端与网络文件夹（规划中）
  TabTaskExplorer.Protocol.Ftp       # FTP 协议适配（规划中）
  TabTaskExplorer.App                # Avalonia UI 与应用层
  TabTaskExplorer.Tests              # 单元测试
```

关键设计：远程目录是 `IFileSystemProvider` 的又一个实现（SFTP 适配），因此远程文件夹打开为普通文件标签后，批量改名、搜索、信息窗格等既有能力**全部自动复用**；本机终端与远程终端共用 `ITerminalSession` 抽象，渲染统一走 WebView + xterm.js。

## 路线图

### 远程与终端
- [ ] M1 连接管理：主机配置、凭据加密存储、导出导入
- [ ] M2 远程终端：SSH Shell + xterm.js 渲染 + 尺寸同步
- [ ] M3 网络文件夹：SFTP 文件提供者接入文件标签，传输队列
- [ ] M4 本机终端：ConPTY / PTY 接入统一终端面板
- [ ] M5 增强：FTP 协议、断点续传、密钥管理

### 界面与体验
- [ ] 文件类型彩色图标与图片缩略图
- [ ] 自绘标题栏（标签页融入标题栏）
- [ ] 鼠标拖拽移动 / 复制文件

### 发布
- [ ] 打包发布（自包含单文件 / 安装器）

## 参与贡献

欢迎 Issue 与 PR。提交代码前请先运行 `dotnet test` 确认全部通过。

## 致谢

- TabX Explorer —— 资源管理器交互对标（闭源，仅参考体验）
- SSH Tools —— 远程终端与文件传输的功能设计参考
- [FluentAvalonia](https://github.com/amwx/FluentAvalonia) · [SSH.NET](https://github.com/sshnet/SSH.NET) · [xterm.js](https://github.com/xtermjs/xterm.js)

## 开源许可

本项目基于 [MIT License](LICENSE) 开源。
