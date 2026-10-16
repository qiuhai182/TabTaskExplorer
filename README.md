# TabTaskExplorer

跨平台的 Windows 风格 **资源管理器 + 任务管理器** 二合一桌面应用。

资源管理器对标 TabX Explorer（多标签 + 快捷访问 + 批量改名），任务侧提供任务看板与进程监控，全部容纳在同一个标签容器中。

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
| 内核 | .NET 10 类库，平台无关抽象（文件系统 / 进程 / 系统指标 / 任务存储） |
| 平台适配 | Windows（Win32 / 性能计数器）· Linux（/proc）· macOS（sysctl） |
| 持久化 | JSON（设置 / 任务 / 书签 / 会话） |
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

内核提供跨平台一致的标准接口，应用层只依赖抽象接口，平台差异收敛在适配工程——同一份代码在三个平台上编译行为一致。

```text
src/
  TabTaskExplorer.Kernel             # 内核：领域模型 + 抽象接口（零平台依赖）
  TabTaskExplorer.Platform.Windows   # Windows 实现（回收站 / 性能计数器 / Win32）
  TabTaskExplorer.Platform.Linux     # Linux 实现（/proc）
  TabTaskExplorer.Platform.MacOS     # macOS 实现（sysctl）
  TabTaskExplorer.App                # Avalonia UI 与应用层
  TabTaskExplorer.Tests              # 单元测试
```

## 路线图

- [ ] 文件类型彩色图标与图片缩略图
- [ ] 自绘标题栏（标签页融入标题栏）
- [ ] 鼠标拖拽移动 / 复制文件
- [ ] 网络位置与云盘集成
- [ ] 打包发布（自包含单文件 / 安装器）

## 参与贡献

欢迎 Issue 与 PR。提交代码前请先运行 `dotnet test` 确认全部通过。

## 开源许可

本项目基于 [MIT License](LICENSE) 开源。
