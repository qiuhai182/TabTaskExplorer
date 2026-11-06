# 桌面端（Avalonia）与网页端（Assets/web）视觉与交互一致性分析

## 0. 先看结论

### 0.1 最影响观感的三条

1. **默认深色主题的“选中态 / 分隔线 / 书签栏”整体气质不同。**
   桌面选中行是中性深灰 `#37373D`，网页是饱和深蓝 `#1F3B57`；桌面分隔线 `#515151`，网页统一压到 `#323232`；桌面书签栏是 `#3B3B3B`，网页书签栏直接露出页面底色 `#191919`。
   证据：[App.axaml:16](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml#L16)、[App.axaml:22](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml#L22)、[App.axaml:25](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml#L25)、[app.css:265](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Assets/web/app.css#L265)、[app.css:279](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Assets/web/app.css#L279)、[app.css:26](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Assets/web/app.css#L26)。
   由于设置默认主题是 Dark（[AppSettingsStore.cs:40](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/AppSettingsStore.cs#L40)），这是用户打开就能看到的第一差异。

2. **控件密度不同：网页端更“扁”，桌面端更高。**
   网页工具栏按钮是 `padding:3px 9px`、约 24px 高（[app.css:36](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Assets/web/app.css#L36)）；桌面工具栏按钮没有声明任何尺寸，走 `FluentAvaloniaTheme` 默认模板（约 32px，[App.axaml:121](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml#L121)，[MainWindow.axaml:208](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/MainWindow.axaml#L208)）。网页表格行约 26px 且有 1px 行分隔线（[app.css:100](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Assets/web/app.css#L100)）；桌面行高显式 28px、无分隔线（[FileTabViewModel.cs:187](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/ViewModels/FileTabViewModel.cs#L187)、[FileListPanel.axaml:91](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/FileListPanel.axaml#L91)）。同一目录在两端首屏可见行数与工具栏高度都不一样。

3. **强调色体系分裂：网页侧边栏同时出现三种“主色”。**
   网页 `连接管理 / 全局搜索 / 磁盘视图 / 进程监控 / 启动项 / 任务看板` 用琥珀 `#E8A317`，`文件比对` 用蓝色 `#2F6FD0`，`网络 / 快捷访问` 标题又用另一种蓝（[views.js:83-90](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Assets/web/views.js#L83-L90)）。桌面静态导航图标统一用 `AccentGlyphBrush`（`#4A8FD0` / 深色 `#4CC2FF`，[MainWindow.axaml:437-506](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Views/MainWindow.axaml#L437-L506)、[App.axaml:20](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/App.axaml#L20)）。网页端仅 `app.css` 就有 `#4A8FD0`、`#2F6FD0`、`#5AA2F5` 等多种蓝，桌面端只有一个界面强调色。

### 0.2 根因一句话

**两端没有共享设计令牌：桌面端只有 14 个语义画刷（深浅共 21 个唯一色值），网页端把颜色和尺寸写死在 313 处十六进制字面量、566 处 px 字面量里，0 个 CSS 变量；此外网页端还存在第三份内联回退样式副本，所以任何“改一处、两端同步”的诉求都无法自动传导。**

### 0.3 建议先做的一步

**先只做“颜色令牌”的单一事实来源：把 `App.axaml` 现有 14 个语义色抽成 `design/tokens/ui-tokens.json`，生成 `Themes/Tokens.axaml` 和 `Assets/web/tokens.css`，第一阶段只替换两端“外壳”（标题栏、书签栏、状态栏、菜单表面、选中/悬停）的颜色，不动尺寸与布局，不碰截图/连接/右键菜单模块。** 因为颜色替换不引起重排，风险最低，且能立刻消除 67 个“网页独有色值”里最显眼的一批。

取证口径：只读分析（2026-11-05），未修改 `src/`；范围为 `App.axaml`、`Views/*.axaml`、`Assets/web/*`、`WebPageHtml.cs`、`WebStaticAssets.cs`。本机 Shell 可用，统计为脚本全量扫描；**未启动 Web 服务（127.0.0.1:15919 未监听），因此没有做运行时像素截图对比**。文中行高属于按 CSS 计算出的近似值，控件默认高度属于框架默认值，两者均已标注。本文只定义方案，不触碰并行任务（截图、连接、右键菜单）的行为代码。

---

## 1. 取证与统计口径

| 指标 | 桌面端 | 网页端 |
| --- | --- | --- |
| 样式来源 | `App.axaml` 主题字典 + 各 `Views/*.axaml` 局部属性 | `Assets/web/app.css` + `index.html` 内联 SVG + JS 内联颜色 |
| 语义色定义 | 14 个画刷 × Light/Dark（`App.axaml:13-111`） | 无；`:root` 0 次、`var(` 0 次 |
| 唯一色值 | 21 个（两套主题合计） | `app.css` 80 个唯一 6 位 hex，**只有 13 个与桌面令牌重合，67 个仅网页存在** |
| 颜色字面量 | `Views` 内至少 69 处（按 Background/Foreground/BorderBrush/Fill 与 Style Setter 统计，未含 BoxShadow）；约 46 处在截图/图片/媒体/终端等固定暗色视图 | `app.css` 313 处 hex；JS 另有 40 处 |
| 尺寸字面量 | `Views` 内 325 处 Margin/Padding、152 处 FontSize、33 处 CornerRadius | 566 处 px，**54 种不同数值**；96 条 padding 声明、20+ 种组合 |
| 基础字号 | 未在仓库声明，由 FluentAvalonia 主题默认值决定 | `body` 显式 `13px`（`app.css:2`） |
| 控件高度 | 未声明，走 Fluent 模板（约 32px） | 工具栏 `~24px`、地址栏 `28px`、表格行 `~26px` |
| 圆角取值 | 2/3/4/6/8/10 | 2/3/4/5/6/8/9/50% |
| 回归副本 | 无 | `WebPageHtml.Content` 内联整页：205,454 字节，内联 CSS 318 行 / 287 块 / 261 hex |

内联回退副本已经落后于拆分后的正式前端：

- 内联 CSS 缺少 `.taskPanel`、`.previewPane`、`.thumbBox`、`.taskRow` 等选择器；
- 内联脚本缺少 `toggleTaskPanel`、`previewChecked`、`hydrateThumbnails`、`connHops` 等函数；
- 正式 CSS 394 行 / 353 块，内联 318 行 / 287 块。
- 锚点：[WebPageHtml.cs:17](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebPageHtml.cs#L17)（内联整页开始）、[WebPageHtml.cs:25-342](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebPageHtml.cs#L25-L342)（内联 CSS）、[WebPageHtml.cs:593](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebPageHtml.cs#L593)（内联脚本）、[WebStaticAssets.cs:64-87](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebStaticAssets.cs#L64-L87)（资源缺失时回退）。

统计命令（本次实际执行，非估算）：

```powershell
# app.css：hex 总数 313、var() 0、:root 0、规则块 353、px 字面量 566、不同 px 54
$css = Get-Content 'src\TabTaskExplorer.App\Assets\web\app.css' -Raw
([regex]::Matches($css, '#[0-9A-Fa-f]{3,8}')).Count
([regex]::Matches($css, 'var\(')).Count
([regex]::Matches($css, '(?<![0-9])[0-9]+px')).Count
```

---

## 2. 现状对照表

判定口径：**一致** = 值或做法等价；**接近但有差异** = 同一角色、值或实现细节有漂移；**两套完全不同的做法** = 控件体系/结构不同，不能靠改数值对齐。

### 2.1 主题色

| 语义 | 桌面（Light / Dark） | 网页（Light / Dark） | 判定 | 证据 |
| --- | --- | --- | --- | --- |
| 内容区底色 | `#FFFFFF / #191919` | `#FFFFFF / #191919` | 一致 | `App.axaml:15,65`；`app.css:2,264` |
| 标题栏/铬色 | `#F1F1F1 / #212121` | `#F1F1F1 / #212121` | 一致 | `App.axaml:14,64`；`app.css:11,265` |
| 分隔线 | `#EBEBEC / #515151` | 标题栏 `#D9D9D9 / #323232`；其余边框 `#E9E9E9 / #323232` | 接近但有差异（网页把“分隔线”和“细边框”合并了，深色下对比度明显更低） | `App.axaml:16,17,66,67`；`app.css:11,265,268` |
| 细边框 | `#E9E9E9 / #323232` | `#E9E9E9 / #323232`（用于侧栏、状态栏、面板） | 一致（但网页使用范围更广） | `App.axaml:17,67`；`app.css:79,88,163,278` |
| 主文字 | `#1C1C1C / #E8E8E8` | `#1C1C1C / #E8E8E8` | 一致 | `App.axaml:18,68`；`app.css:2,264` |
| 次文字 | `#808080 / #808080` | `#808080 / #808080`，另有 `#8A8A8A/#9A9A9A/#909090/#A0A0A0/#606060/#707070` | 接近但有差异（网页有 10+ 级灰阶） | `App.axaml:19,69`；`app.css:20,32,75,89,119`（`#808080` 共 27 次）、`app.css:280,307,393`（深浅不同的灰） |
| 强调色 | `#4A8FD0 / #4CC2FF` | `#4A8FD0`（UI）+ `#2F6FD0`（链接/目录/分组）+ `#E8A317`（导航图标） | 接近但有差异（网页是 3 套色相） | `App.axaml:20,70`；`app.css:62,80,85,108,170`；`views.js:83-90` |
| 行悬停 | `#F5F7FA / #2E2E2E` | `#F5F7FA / #2E2E2E` | 一致 | `App.axaml:21,71`；`app.css:102,283` |
| 行选中 | `#DCE9F5 / #37373D` | `#E8F1FC / #1F3B57` | 接近但有差异（网页更深、更蓝） | `App.axaml:22,72`；`app.css:103,279` |
| 标签悬停 | `#E4E4E4 / #2E2E2E` | `#E4E4E4 / #2E2E2E` | 一致 | `App.axaml:23,73`；`app.css:16,266` |
| 标签选中 | `#FFFFFF / #191919`，无下划线 | 同底色，**额外有 2px `#4A8FD0` 下划线** | 接近但有差异 | `App.axaml:24,74`；`app.css:17,267` |
| 书签栏 | `#FFFFFF / #3B3B3B` | 无底色，露出 `#FFFFFF / #191919` | 接近但有差异（深色下桌面有一条更亮的栏） | `App.axaml:25,75`；`app.css:26`（未设置 background） |
| 地址栏/搜索边框 | `#DCDDDE / #525252` | `#D5D5D5 / #3C3C3C` | 接近但有差异 | `App.axaml:26,76`；`app.css:54,61,286` |
| 工具栏文字 | `#808080 / #FFFFFF` | 继承正文 `#1C1C1C / #E8E8E8` | 接近但有差异（浅色下桌面是灰字，网页是黑字） | `App.axaml:27,77`；`app.css:2,36`（`.btn` 未设 color） |
| 菜单表面 | `#F1F1F1 / #212121`（`ChromeBrush`） | `#FFFFFF / #242424` | 接近但有差异（浅色下桌面是灰菜单，网页是白菜单） | `App.axaml:125-130`；`app.css:42,270` |
| 菜单悬停 | `#F5F7FA / #2E2E2E` | `#F0F6FF / #2E2E2E` | 接近但有差异（浅色下网页带蓝调） | `App.axaml:134-136`；`app.css:44,271` |
| 侧栏选中项 | 透明底 + 强调色文字 | `#E8F1FC + #1B5FA8` / `#1F3B57` 填充底 | 两套完全不同的做法 | `App.axaml:228-236`；`app.css:85,279` |
| 状态栏背景 | `ChromeBrush`（`#F1F1F1 / #212121`） | 透明，露出内容底色 | 接近但有差异 | `MainWindow.axaml:564-569`；`app.css:163` |
| 看板列 | 透明底 + `#40808080` 边框、圆角 8 | `#F7F8FA` 填充 + `#E9E9E9` 边框、圆角 6 | 两套完全不同的做法 | `TaskBoardPanel.axaml:15,36,58`；`app.css:153` |
| 进程走势线 | `#2E7D32 / #7E57C2 / #EF6C00` | `#2E7D32 / #7E57C2 / #EF6C00` | 一致 | `ProcessTrendChart.cs:32-38`；`panels.js:236-238` |
| 对比行底色 | `alpha 0x22`（约 13%） | `rgba(...,.10)`（10%） | 接近但有差异 | `ComparePanel.axaml:11-46`；`panels.js:5-12` |
| 对比状态呈现 | 左侧 4×16 色条 + 行底色 | 实心彩色胶囊徽标 + 行底色 | 两套完全不同的做法 | `ComparePanel.axaml:108-110`；`panels.js:63-64` |

补充：网页端浅色选中 `#E8F1FC` 在 `app.css` 出现 5 次，深色 `#1F3B57` 出现 3 次；桌面端选中只在主题字典里出现 1 次，说明网页端是“多处手写同一个状态”。

### 2.2 字体、间距、尺寸

| 项目 | 桌面 | 网页 | 判定 | 证据 |
| --- | --- | --- | --- | --- |
| 字体族 | 未声明，由 Fluent 主题提供 | `'Segoe UI','Microsoft YaHei',sans-serif` | 接近但有差异（机制不同） | `App.axaml:120-121`；`app.css:2` |
| 基础字号 | 未声明（框架默认，需实机确认） | `13px` | 接近但有差异（需截图校准） | `app.css:2` |
| 小字号分布 | `FontSize=12` 85 次、`11` 46 次 | `font-size:12px` 33 次、`11px` 20 次 | 一致（两端都以 11/12 为主） | `Views/*.axaml` 统计；`app.css` 统计 |
| 标题栏 | 36px | 36px | 一致 | `MainWindow.axaml:21`；`app.css:11` |
| 标签高度 | 28px（列表项 `Height=28`） | 26px | 接近但有差异 | `MainWindow.axaml:51`；`app.css:15` |
| 工具栏按钮 | Fluent 默认（约 32px，未显式声明） | `padding:3px 9px`，约 24px | 接近但有差异（影响最大） | `MainWindow.axaml:208`；`app.css:36` |
| 地址栏/搜索框 | 地址栏 `MinHeight=30`，搜索框默认 | 两者 `min-height:28` | 接近但有差异 | `MainWindow.axaml:333,372`；`app.css:54,61` |
| 表格行高 | 28px；紧凑 22px | 约 26px；紧凑约 21px | 接近但有差异 | `FileTabViewModel.cs:187-195`；`app.css:100,333` |
| 斑马纹 | 无（仅悬停/选中底色） | 无（仅悬停/选中底色） | 一致 | `App.axaml:138-146`；`app.css:101-103` |
| 表头高度 | Fluent 按钮默认，未显式声明 | 约 26px（`padding:5px 10px`） | 接近但有差异 | `FileListPanel.axaml:43-67`；`app.css:96` |
| 侧栏宽度 | 200px | 200px | 一致 | `MainWindow.axaml:377`；`app.css:79` |
| 信息窗格宽度 | 220px | 240px | 接近但有差异 | `FileListPanel.axaml:188`；`app.css:88` |
| 对话框内边距 | `Margin=16`、`Spacing=12` | `panelHead/Foot` 高 40/44、左右 12px | 接近但有差异 | `MessageDialog.axaml:7`；`app.css:176-178` |
| 间距取值 | 325 处 Margin/Padding，常见 8/4/6 | 566 处 px、54 种数值（3/5/7/9/10/14/18 等） | 两套完全不同的做法（都没有间距标尺） | `Views/*.axaml`；`app.css` |

### 2.3 圆角、边框、阴影

| 项目 | 桌面 | 网页 | 判定 | 证据 |
| --- | --- | --- | --- | --- |
| 圆角标尺 | 2/3/4/6/8/10 | 2/3/4/5/6/8/9/50% | 接近但有差异（网页多出 5/9） | `CornerRadius=` 统计；`app.css` 统计 |
| 输入框圆角 | 地址栏 3 | 3 | 一致 | `MainWindow.axaml:333`；`app.css:54` |
| 按钮圆角 | Fluent 模板 | 4 | 接近但有差异 | `app.css:36,21` |
| 菜单圆角 | Fluent 模板（原生） | 6 | 接近但有差异（实现体系不同） | `App.axaml:125-130`；`app.css:42` |
| 对话框圆角 | 原生窗口（无自绘圆角） | 8px + 遮罩 | 两套完全不同的做法 | `MessageDialog.axaml:1-17`；`app.css:173-175` |
| 表格边框 | 无行分隔线、无表头下边框 | 表头 1px `#E0E0E0`、行 1px `#F2F2F2` | 两套完全不同的做法 | `FileListPanel.axaml:29-73,91-93`；`app.css:96,100` |
| 阴影 | 原生 Flyout 自带阴影；IDE 快速打开自绘 `0 8 24 #66000000` | 菜单 `0 6 18 rgba(0,0,0,.14)`、右键 `.16`、预览/任务面板 `.22` | 接近但有差异（无法逐像素一致） | `IdeShellView.axaml:199`；`app.css:42,72,189,337,363` |
| 滚动条 | 表格滚动条常驻（代码关掉自动隐藏） | 浏览器原生，随系统自动隐藏 | 两套完全不同的做法（技术上不可避免） | `FileListPanel.axaml.cs:2275-2277` |

### 2.4 组件形态

| 组件 | 桌面做法 | 网页做法 | 判定 | 证据 |
| --- | --- | --- | --- | --- |
| 工具栏按钮 | 图标 14 + 文字 12，ghost 悬停底色，全部带图标 | 文字按钮，只有“IDE 模式”带图标，其余纯文字 | 接近但有差异（信息密度不同） | `MainWindow.axaml:210-317`；`index.html:41-84` |
| 标题栏图标按钮 | 无边框、透明底，28×28 或 40×36 | 1px `#B0B0B0` 边框、圆角 4、26×24 | 接近但有差异 | `MainWindow.axaml:80-160`；`app.css:21-23` |
| 侧栏项 | 图标统一蓝色强调，文字 12 | 图标混用蓝/琥珀，文字 12 | 接近但有差异（色相分裂） | `MainWindow.axaml:382-506`；`views.js:83-90` |
| 标签页 | 28px、选中仅换底色 | 26px、选中换底色 + 蓝色下划线 | 接近但有差异 | `MainWindow.axaml:51`；`app.css:15-19` |
| 菜单 | 原生 `MenuFlyout/ContextMenu`，跟随主题字典 | 自绘 `.menu/.pop/.ctx` div，6px 圆角、自定义阴影 | 两套完全不同的做法（不建议强行模仿） | `App.axaml:125-136`；`app.css:42,72,189` |
| 对话框 | 原生窗口，标题栏由系统绘制 | 页面内遮罩 + 面板，40px 头部 / 44px 底部 | 两套完全不同的做法（外层不建议统一） | `MessageDialog.axaml`；`app.css:173-178` |
| 文件列表 | Avalonia `ListBox` + `Border.row`，虚拟化 | HTML `<table>` / 磁贴 div，整页重绘 | 两套完全不同的做法（底层不可避免） | `FileListPanel.axaml:79-139`；`views.js:382-515` |
| 信息窗格 | 220px，无分隔线，标题 + 字段 | 240px，左侧 1px 分隔线，12px 字体 | 接近但有差异 | `FileListPanel.axaml:188-198`；`app.css:88-92` |
| 进程调节行 | `StackPanel Spacing=8` + Fluent `ComboBox/TextBox` | `.procCtrl` flex + 原生 `select/input`；但 `procBar` 类**没有任何 CSS 定义** | 接近但有差异（网页“刷新/结束进程”一行缺 gap/padding） | `ProcessPanel.axaml:10-33`；`panels.js:128`；`app.css` 无 `.procBar` |
| 任务看板 | 透明列 + 25% 灰边框 + 圆角 8；条目无底色 | 灰底列 + 6px 圆角；条目白底 + 边框；空列显示“暂无” | 两套完全不同的做法 | `TaskBoardPanel.axaml:15-77`；`app.css:152-155`；`panels.js:325-337` |
| 连接管理 | 左列表 + 右编辑表单（主从两栏） | 上表格 + 下表单（单列流式） | 两套完全不同的做法（结构差异，建议保留） | `ConnectionPanel.axaml:24-42`；`panels.js:356-407` |
| 对比视图 | 色条 + 行底色 | 彩色胶囊 + 行底色 | 两套完全不同的做法 | `ComparePanel.axaml:108-110`；`panels.js:63-64` |
| IDE 编辑区 | `TreeView` + `ListBox` 标签 + `TextBox`（字号 13、padding 6） | 自绘树节点 + 标签 div + `textarea`（13/1.55、padding 6 8） | 接近但有差异（可接受） | `IdeShellView.axaml:58-137`；`app.css:216-261` |

### 2.5 状态、反馈、图标

| 项目 | 桌面 | 网页 | 判定 | 证据 |
| --- | --- | --- | --- | --- |
| 行悬停 | `Border.row:pointerover` → `RowHoverBrush` | `tr.row:hover` → `#F5F7FA/#2E2E2E` | 一致 | `App.axaml:141-143`；`app.css:102,283` |
| 行选中 | `Border.row.checked` → `RowSelectedBrush` | `tr.row.checked` → `#E8F1FC/#1F3B57` | 接近但有差异 | `App.axaml:144-146`；`app.css:103,279` |
| 行焦点 | 原生 ListBox 焦点框 | `.focus` 1px `#4A8FD0` outline | 接近但有差异 | `app.css:104,114` |
| 输入焦点 | 原生 Fluent 焦点框 | 仅 `border-color:#4A8FD0`，无外圈 | 接近但有差异 | `app.css:62,125,184,245` |
| 键盘可达 | 所有控件原生可 Tab | 33 个 `span.btn` 假按钮、0 个 `tabindex`、仅 7 个原生 `<button>`，无 `:focus-visible` | 两套完全不同的做法（网页是明确缺口） | `index.html` 统计；`app.css` 仅 5 个 `:focus` 选择器 |
| 摘要/统计位置 | `FileListPanel` 底部固定行，始终可见 | 追加在滚动内容末尾 `.hint.summary`，长列表会滚出视口 | 两套完全不同的做法（建议优先修） | `FileListPanel.axaml:202-220`；`views.js:332-335`；`app.css:87` |
| 瞬时提示 | 复用同一摘要行，保留到下一次操作 | 内容区底部 `#flashLine`，6 秒后自动隐藏 | 接近但有差异（生命周期不同） | `index.html:113`；`panels.js:742-773` |
| 空状态 | 列表区留白，摘要显示“目录为空，或无法读取该目录” | 表格内插入“空目录”行，同时摘要也显示服务端文案 | 接近但有差异（位置与文案重复） | `FileTabViewModel.cs:1470`；`views.js:413` |
| 加载状态 | 初始摘要“正在加载……” | 初始内容“正在加载……” | 一致 | `FileTabViewModel.cs:80`；`index.html:111` |
| 主题首帧 | `RequestedThemeVariant="Dark"`，启动即深色 | 先渲染浅色 `body`，等 JS 收到 state 后才加 `body.dark` | 两套完全不同的做法（会白闪） | `App.axaml:5`；`app.css:2`；`views.js:11` |
| 图标几何 | `VectorIconPaths.cs` 24×24 path 表 | `core.js` `UI_PATHS` 同一套 path | 一致（但是手工复制） | `VectorIconPaths.cs:44-85`；`core.js:9-33` |
| 文件类型图标 | Skia 绘制，文件夹前板用渐变 | SVG 绘制，文件夹用纯色 + 白色高光 | 接近但有差异（同形不同质感） | `FileTypeIcon.cs:115-121`；`core.js:103-107` |
| 标题栏图标尺寸 | 12–16px（窗口控制按钮 12–13，工具按钮 15–16） | 12–13px | 接近但有差异 | `MainWindow.axaml:84-159`；`index.html:28-32` |
| 文件夹图标色 | `#F2B23E`（`FileIconCatalog`） | `#F2B23E`（默认） | 一致 | `FileIconCatalog.cs:793`；`views.js:72` |

---

## 3. 差异原因

### 3.1 机制层面的四条

1. **没有设计令牌层，只有“各写各的常量”。**
   桌面端至少有 `App.axaml` 的 14 个语义画刷，网页端 0 个 CSS 变量；同一个“次文字”在网页端演化出 10 多种灰。桌面端 `Views` 里还有 69 处硬编码颜色，说明连桌面内部也不是 100% 令牌化。
2. **控件体系不同：原生控件 vs 手写 HTML。**
   桌面按钮/输入框/菜单/滚动条由 FluentAvalonia 模板渲染；网页是 `span`、`div`、`table` 和浏览器原生表单。焦点环、阴影、滚动条、字体度量天然不同，这部分只能“对齐视觉参数”，不能逐像素相同。
3. **网页端自己还有第二份样式副本。**
   `WebPageHtml.Content` 保留了拆分前的整页内联 CSS/JS（205KB），且已经落后（缺任务中心、预览、缩略图、连接跳板字段）。这意味着“网页端样式”本身就有两个版本，排查差异时容易看错文件。
4. **布局尺寸没有标尺，靠局部数值堆出来。**
   网页 54 种 px 值、96 条 padding；桌面 325 处 Margin/Padding。两端都在用“3/5/7/9/10/14/18”这类非标尺值，导致控件密度和节奏无法收敛。

### 3.2 技术上不可避免 vs 可以消除

| 类别 | 具体项 | 结论 |
| --- | --- | --- |
| 不可避免 | 浏览器窗口没有桌面标题栏的最小化/最大化/关闭按钮；网页没有原生 `ContextMenu`；滚动条样式由系统控制；字体 hinting/抗锯齿不同；原生 `TextBox/ComboBox` 的 1–2px 差异 | 接受差异，只对齐颜色、字号、圆角、内外边距 |
| 不可避免（但可缓解） | 网页首帧主题闪烁（CSS 默认浅色，JS 后加 dark） | 可由服务端在 HTML 上直接输出主题 class，消除白闪 |
| 可以消除 | 67 个网页独有 hex、10+ 级灰阶、3 套蓝色、选中/悬停/标签下划线、书签栏底色、工具栏文字色、菜单表面色、状态栏底色 | 全部收敛到同一份令牌 |
| 可以消除 | 工具栏/输入框/表格行/表头高度差；圆角 5/9/10；间距 3/5/7/9/14/18 | 用尺寸标尺统一，配截图回归 |
| 可以消除 | 摘要栏滚动出视口、网页 `procBar` 无样式、空状态重复、网页假按钮不可 Tab | 属于实现缺口，不是渲染差异 |
| 不建议强行消除 | 网页自绘菜单/弹窗与桌面原生 Flyout/Dialog 的外壳形态 | 成本高、损害可访问性，见第 5 节 |

---

## 4. 统一方案

### 4.1 单一事实来源：令牌 → 双端生成

推荐目录与产物（本阶段只设计，不创建）：

```text
design/
  tokens/
    ui-tokens.json          # 唯一事实来源（颜色/字号/间距/圆角/尺寸/动效）
  icons/
    ui-icons.json           # 24×24 界面图标 path（阶段 4）
    file-icons.json         # 文件类型 symbol path + 默认 accent（阶段 4）
tools/
  token-gen/                # 生成器（建议 .NET 控制台，跨平台）
src/TabTaskExplorer.App/
  Themes/Tokens.axaml       # 生成物（Avalonia ResourceDictionary）
  Assets/web/tokens.css     # 生成物（:root + body.dark）
  Assets/web/tokens.js      # 生成物（图表/图标默认色，阶段 4）
```

生成链：

```text
ui-tokens.json
  ├─ gen-avalonia → Themes/Tokens.axaml → App.axaml 合并 → 所有 Views 的 DynamicResource
  ├─ gen-css      → Assets/web/tokens.css → app.css 中 var(--tte-*)
  └─ gen-js       → Assets/web/tokens.js → core.js / panels.js 中的图表与默认图标色
```

约束：

- **生成物入库，CI 校验“重新生成后无 diff”**；不在运行时解析 JSON，避免 Web 端多一个请求和解析失败分支。
- 桌面端保留现有资源键名（`ChromeBrush`、`ContentBrush`…），生成器按同名输出，`Views` 不用改引用；只把 `App.axaml` 里的字面量搬走。
- 网页端按**角色**替换，不做“按值全局替换”。例如 `#E9E9E9` 既可能是 `border` 也可能是 `separator`，必须按选择器语义映射，否则会把层次抹平。
- 新增语义键建议：`surfaceRaised`（菜单/弹窗/卡片）、`borderStrong`（看板列）、`danger/warning/success`、`focusRing`、`overlayScrim`、`chartSystemCpu/chartSystemMemory/chartSelectedCpu`。现有 14 个键保持不变。

令牌示例（第一版以桌面现有值为准，避免桌面视觉变化）：

```json
{
  "color": {
    "light": {
      "chrome": "#F1F1F1",
      "content": "#FFFFFF",
      "separator": "#EBEBEC",
      "border": "#E9E9E9",
      "textPrimary": "#1C1C1C",
      "textMuted": "#808080",
      "accent": "#4A8FD0",
      "rowHover": "#F5F7FA",
      "rowSelected": "#DCE9F5",
      "tabHover": "#E4E4E4",
      "tabActive": "#FFFFFF",
      "bookmarkBar": "#FFFFFF",
      "inputBorder": "#DCDDDE",
      "toolbarText": "#808080",
      "statusBar": "#F1F1F1",
      "menuSurface": "#F1F1F1",
      "danger": "#D9534F",
      "warning": "#E8A317",
      "success": "#33A852",
      "focusRing": "#4A8FD0"
    },
    "dark": { "chrome": "#212121", "content": "#191919", "separator": "#515151", "border": "#323232", "textPrimary": "#E8E8E8", "textMuted": "#808080", "accent": "#4CC2FF", "rowHover": "#2E2E2E", "rowSelected": "#37373D", "tabHover": "#2E2E2E", "tabActive": "#191919", "bookmarkBar": "#3B3B3B", "inputBorder": "#525252", "toolbarText": "#FFFFFF", "statusBar": "#212121", "menuSurface": "#212121", "danger": "#FF6B6B", "warning": "#E8A317", "success": "#3FA45B", "focusRing": "#4CC2FF" }
  },
  "font": { "sizeXs": 11, "sizeSm": 12, "sizeMd": 13, "sizeLg": 16, "sizeXl": 18 },
  "size": { "titlebar": 36, "control": 26, "controlLarge": 32, "row": 28, "rowCompact": 22, "sidebar": 200, "infoPane": 220 },
  "space": { "s1": 2, "s2": 4, "s3": 6, "s4": 8, "s5": 12, "s6": 16, "s7": 24 },
  "radius": { "xs": 2, "sm": 3, "md": 4, "lg": 6, "xl": 8, "full": 9999 },
  "motion": { "fast": 120, "base": 180, "progress": 200, "slow": 240 }
}
```

映射建议（网页现状 → 令牌）：

| 网页现值 | 令牌 | 说明 |
| --- | --- | --- |
| `#F1F1F1` / `#212121`（titlebar、toolbar 边框） | `chrome` | 与桌面同值 |
| `#FFFFFF` / `#191919` | `content` | 正文/内容底 |
| `#D9D9D9` / `#323232`（titlebar 下边） | `separator` | 深色应改为桌面 `#515151`，这是第一条会“肉眼变明显”的改动 |
| `#E9E9E9` / `#323232`（侧栏、状态栏） | `border` | 与桌面同值 |
| `#E8F1FC` / `#1F3B57` | `rowSelected` | 改为桌面 `#DCE9F5` / `#37373D` |
| `#808080` 及 `#8A8A8A/#9A9A9A/#909090/#A0A0A0` | `textMuted` | 先统一到 `#808080`，对比度问题另列（见 4.3） |
| `#4A8FD0` / `#2F6FD0` | `accent` | 统一为桌面 `#4A8FD0`（深色 `#4CC2FF`）；导航静态图标同色 |
| `#E8A317`（导航图标） | `iconAccentStatic` = `accent` | 仅文件类型图标保留琥珀 `#F2B23E` |
| `#FFFFFF` / `#242424`（menu） | `menuSurface` | 浅色改为 `#F1F1F1`，与桌面菜单一致 |
| `#F0F6FF`（menu hover） | `rowHover` | 去掉浅色蓝调 |
| `#D5D5D5` / `#3C3C3C`（输入） | `inputBorder` | 改为桌面 `#DCDDDE` / `#525252` |
| `1C1C1C` / `#E8E8E8`（.btn 继承） | `toolbarText` | 浅色改为 `#808080`、深色 `#FFFFFF` |

### 4.2 组件级对齐清单

| 组件 | 现状锚点 | 目标（两端同一值） | 主要改动 | 风险/阶段 |
| --- | --- | --- | --- | --- |
| 工具栏按钮 | `MainWindow.axaml:208-317`；`app.css:35-40` | 高 28、左右内边距 10、圆角 4、图标 14 + 间距 5、文字 12；ghost 样式（默认透明，悬停 `rowHover`） | 桌面加 `Button.tool` 样式；网页 `.btn` 显式高度；网页补齐图标 | 中 / 阶段 2 |
| 标题栏图标按钮 | `MainWindow.axaml:80-160`；`app.css:21-23` | 28×28、无边框、透明底、悬停 `rowHover`；标题栏窗口按钮保持 40×36（桌面专属） | 网页去掉 `#B0B0B0` 边框；桌面不变 | 低 / 阶段 1 |
| 输入框/地址栏 | `MainWindow.axaml:333,372`；`app.css:54,61` | 高 30、左右内边距 8、圆角 3、边框 `inputBorder`；焦点 1px `accent` + 2px 外圈 | 网页 28→30；两端焦点规范 | 低 / 阶段 2 |
| 标签页 | `MainWindow.axaml:33-51`；`app.css:15-19` | 高 28、圆角 `4 4 0 0`、左右 10；选中 = `tabActive` 底色 + 2px `accent` 下划线 | 网页 26→28；桌面补下划线 | 中 / 阶段 2 |
| 列表/表格 | `FileListPanel.axaml:79-139`；`app.css:95-104` | 行 28（紧凑 22）、表头 28、无斑马纹、**无行分隔线**、表头保留 1px `border` 下边线；选中 `rowSelected`、悬停 `rowHover` | 网页去掉 `td` 下边框；两端表头统一 28 | 中 / 阶段 2 |
| 菜单/右键菜单 | `App.axaml:125-136`；`app.css:42-45,189-210` | 表面 `menuSurface`、边框 `border`、圆角 6、项高 28、左右 10、悬停 `rowHover` | 只改网页 CSS；桌面保持原生模板 | 低 / 阶段 1 |
| 对话框 | `MessageDialog.axaml`；`app.css:173-178` | 内容内边距 16、间距 12、主/次按钮高 32、最小宽 80、右对齐；网页面板圆角 8、头部/底部 40/44 | 网页 head/foot 左右 12→16、内容 16；桌面按钮显式 MinHeight | 低 / 阶段 2 |
| 信息窗格 | `FileListPanel.axaml:188-198`；`app.css:88-92` | 宽 220、padding 10、12px、右侧 1px `border` 分隔 | 网页 240→220；桌面补左边框或网页去边框（二选一） | 低 / 阶段 2 |
| 状态栏 | `MainWindow.axaml:564-569`；`app.css:163` | 高 28、左右 12、12px `textMuted`、底色 `statusBar`、上边框 `separator` | 网页补底色；桌面不变 | 低 / 阶段 1 |
| 摘要/提示 | `FileListPanel.axaml:202-220`；`views.js:332-335`；`app.css:87` | 固定在内容区底部（不随滚动）；高 28、12px、`textMuted`；瞬时消息替换摘要，下一次状态更新恢复 | 网页把 `summary` 移出滚动容器；去掉 6 秒定时器 | 中 / 阶段 3 |
| 空状态/加载 | `FileTabViewModel.cs:80,1470`；`views.js:413` | 文案统一为“正在加载……”/“目录为空，或无法读取该目录”；不再在表格里重复插入“空目录” | 网页删掉内联空行；摘要保留 | 低 / 阶段 3 |
| 任务看板 | `TaskBoardPanel.axaml:15-77`；`app.css:152-155` | 列：`surfaceRaised` 底 + `borderStrong` 1px + 圆角 6 + padding 8；条目：`content` 底 + `border` 1px + 圆角 4 + padding 6 8；空列不显示“暂无”或两端都显示 | 两端同时改；新增两个语义 token | 中 / 阶段 3 |
| 进程调节行 | `ProcessPanel.axaml:10-33`；`panels.js:128` | 行内 gap 8；按钮/输入/下拉统一 26–28 高 | 补 `.procBar` 的 flex/gap；桌面加统一高度 | 低 / 阶段 2 |
| 对比视图 | `ComparePanel.axaml:108-110`；`panels.js:63-64` | 状态色条或胶囊二者只保留一种；行底色统一 10% 或 13% | 建议保留桌面色条、网页改成色条 | 中 / 阶段 3 |

### 4.3 交互规范

1. **状态语义**：`normal / hover / selected / focus / disabled` 五态固定。
   - 悬停只允许当前指针项高亮，旧项立即移除（桌面已在 `App.axaml:226-236` 这么做，网页天然满足）。
   - 侧栏“当前位置”建议两端统一为“强调色文字 + 2px 左侧指示条”，不再用大面积填充底（网页当前 `#E8F1FC` 填充与桌面 `App.axaml:228-236` 相反）。
   - 标签选中统一为“内容底色 + 2px 强调色下划线”，解决深色下选中标签不够明显的问题。
2. **焦点与键盘**：
   - 网页端 `:focus-visible` 统一 `outline: 2px solid var(--tte-accent); outline-offset: 1px;`；输入框额外 2px 外圈。
   - 行焦点保持 outline，仅网页当前是 1px，建议与桌面原生焦点框视觉强度对齐到 2px。
   - 把网页 33 个 `span.btn` 改为真实 `<button type="button">`，并补 `aria-pressed`（开关类）、`aria-disabled`（禁用类）、`title` 说明禁用原因；这是“键盘可达”的前置条件，不能只改 CSS。
   - Tab 顺序与视觉顺序一致；菜单支持 Esc/方向键（网页已有 `handleContextMenuKeydown`，需覆盖新按钮）。
3. **空状态与加载文案**（两端逐字一致）：
   - 加载中：`正在加载……`
   - 空目录/不可读：`目录为空，或无法读取该目录`
   - 搜索无结果：`没有匹配的搜索结果`
   - 网页独有的“空目录”“暂无”重复行删除，统一由摘要栏承担。
4. **通知与生命周期**：
   - 摘要栏常驻；瞬时消息覆盖摘要，直到下一次状态更新；失败消息不自动消失（桌面当前语义）。
   - 网页去掉 6 秒定时器（`panels.js:763-773`），避免“看没看到全凭手速”。
   - 失败提示仍按项目约定走托盘气泡/日志（桌面），网页用状态栏 + 摘要，不弹系统对话框。
5. **动效**：
   - 只保留必要的 120/180/200/240ms 四档；进度条 200ms linear（网页当前 `.2s linear`，`app.css:379`）。
   - 主题切换、悬停底色不做过渡，避免深浅切换时“半透明混色”。
   - `@media (prefers-reduced-motion: reduce)` 关闭非必要过渡。
6. **主题首帧**：
   - 服务端生成页面时直接输出 `<body class="dark">`（`WebPageHtml.TryBuildPage` 已有 `__VERSION__/__PORT__` 替换位，[WebPageHtml.cs:75-87](file:///d:/TabTaskExplorer/src/TabTaskExplorer.App/Services/WebPageHtml.cs#L75-L87)），不再等 `views.js:11`。
   - 主题状态本身已经共享（`WebWorkspace.cs:2069`、`WebServiceHost.cs:598-603`），无需新增接口。

### 4.4 落地顺序、成本与风险

| 阶段 | 内容 | 主要文件 | 风险 | 成本 |
| --- | --- | --- | --- | --- |
| 0 | 令牌清单与角色映射（本文） | 仅文档 | 无 | S |
| 1 | **颜色令牌**：生成 `Tokens.axaml` / `tokens.css`；先替换标题栏、书签栏、状态栏、菜单表面、选中/悬停 | `design/tokens/*`、`App.axaml`、`app.css` | 低（不重排） | S |
| 1b | 删除/降级 `WebPageHtml` 内联副本，只保留“静态资源缺失”极简页 | `WebPageHtml.cs`、`WebStaticAssets.cs` | 低（需构建验证嵌入资源完整） | S |
| 2 | 尺寸标尺：工具栏/输入框/标签/表格行/表头/对话框按钮/信息窗格 | `MainWindow.axaml`、`FileListPanel.axaml`、`ProcessPanel.axaml`、`app.css`、`panels.js` | 中（会重排，需截图回归） | M |
| 3 | 状态与反馈：标签下划线、侧栏选中、摘要固定、空状态、对比呈现、看板 | `App.axaml`、`app.css`、`views.js`、`panels.js`、`TaskBoardPanel.axaml` | 中（改变习惯） | M |
| 4 | 图标单源：`VectorIconPaths.cs` / `core.js` / `FileTypeIcon` / `FileIconCatalog` 由 `design/icons/*.json` 生成 | 上述 4 文件 + 生成器 | 中（需编译与截图） | M |
| 5 | 可访问性：网页假按钮换真按钮、ARIA、Tab 顺序、`focus-visible` | `index.html`、`core.js`、`actions.js`、`boot.js` | 中高（行为回归） | L |
| 6 | 主题首帧：服务端输出 body class | `WebPageHtml.cs`、`WebStaticAssets.cs`、`index.html` | 低 | S |

先做顺序建议：**0 → 1 → 1b → 6 → 2 → 3 → 4 → 5**。
理由：颜色不改布局，最安全；1b 先消灭“第三份样式”避免边改边漂；6 消除白闪是单点收益；2/3 才动布局与交互；4/5 是结构性收尾。

并行任务边界：阶段 1 只改颜色，不触碰 `ConnectionPanel` 结构、`ContextMenu` 逻辑、`Capture*`；阶段 2 若需改连接面板，必须与并行任务协调后再动。

### 4.5 验收与回归

- 截图矩阵：1200×800 与 1024×700；Light/Dark；资源管理器/IDE；文件详情/图标、任务看板、连接管理、进程监控、文件比对、搜索、网页任务中心/预览层。
- 色值验收：取色器比对的 hex 必须完全相等；控件高度允许 ±1px，原生控件允许 ±2px。
- 自动守卫：
  - `app.css` 中出现 `#[0-9A-Fa-f]{6}` 且不在 `tokens.css` → 失败；
  - `App.axaml` / `Views/*.axaml` 新增颜色字面量 → 失败（截图编辑器等固定暗色视图加入白名单）；
  - JS 生成的 class 在 CSS 中找不到对应规则 → 告警并人工确认（当前已扫描到 `procBar`（确认缺布局）、`rowThumbSlot`、`startupCustomRow`、`summary`（后三者可能有意复用 `.hint`/父级样式））。
- 可访问性：Tab 能走遍工具栏/菜单/对话框按钮；焦点始终可见；12px 文字对比度 ≥ 4.5:1。
  - 现有 `#808080` 在浅色 `#F1F1F1` 上约 3.5:1，不达标；建议令牌 `textMuted` 浅色改用 `#6B6B6B`（约 4.7:1），深浅两端同时改，避免只改一端造成新的不一致。
- 主题切换：两端切换后无白闪、无残留旧色；网页状态栏与桌面状态栏同步。

---

## 5. 不建议做的

1. **不要用网页 CSS 复刻 FluentAvalonia 控件模板**（焦点 adorner、圆角、阴影、按压动画）。收益是“更像”，代价是失去原生键盘/读屏/DPI 行为；统一尺寸与颜色即可。
2. **不要把桌面窗口改成网页式无边框自绘**来对齐圆角/阴影。窗口控制、贴边、任务栏、DPI 都会回归；截图/贴图/终端等固定暗色视图更不应纳入主题令牌。
3. **不要追求 TextBox/ComboBox/ScrollBar 的像素一致**。不同渲染栈 + 系统缩放必然有 1–2px 差异，强行补齐会引入平台特判。
4. **不要按色值做全局替换**。`#E9E9E9` 在网页里同时承担“细边框、分隔线、表头线”，全局替换会把层次抹平；必须按角色改。
5. **不要为了统一而删除桌面原生焦点框**去模仿网页 outline；也不要为了网页“看起来像原生”而放弃真实 `<button>` 语义。
6. **不要复制桌面文件夹图标的渐变色到网页 16px 小图标**。小尺寸下渐变会糊，网页保持纯色更清晰；只需保证 path 几何同源。
7. **不要统一截图编辑器、图片查看器、媒体播放器、终端**。它们是有意固定的暗色沉浸表面（`CaptureEditorWindow.axaml` 19 处、`MediaPanel.axaml` 11 处硬编码色），网页端没有对应视图。
8. **不要为了“菜单一致”而放弃桌面 `ContextMenu`/`IContextMenu` 原生能力**（含系统扩展、盾牌、子菜单）。右键菜单正在并行改造，外观只跟令牌走，不重写交互。

---

## 6. 文件锚点索引

| 文件 | 关注点 |
| --- | --- |
| `src/TabTaskExplorer.App/App.axaml` | 5（默认深色）、13–111（14 个语义画刷 × 2 主题）、120–121（Fluent 主题）、125–136（菜单）、138–198（行/磁贴/选中）、201–206（标签）、208–236（导航按钮与侧栏选中） |
| `src/TabTaskExplorer.App/Views/MainWindow.axaml` | 19–21（标题栏）、33–51（标签条）、165–167（书签栏）、208–317（工具栏）、321–373（导航/地址栏）、377–506（侧栏）、564–569（状态栏） |
| `src/TabTaskExplorer.App/Views/FileListPanel.axaml` | 29–73（表头）、79–139（行列表）、142–183（磁贴）、188–198（信息窗格）、202–220（摘要栏） |
| `src/TabTaskExplorer.App/Views/ProcessPanel.axaml` | 10–33（操作/调节行）、39–93（表头）、96–152（列表/图表/详情/摘要） |
| `src/TabTaskExplorer.App/Views/ComparePanel.axaml` | 7–47（状态色）、89–125（目录列表）、108–110（色条） |
| `src/TabTaskExplorer.App/Views/TaskBoardPanel.axaml` | 15/21/36/42/58/64（列/卡片边框与圆角） |
| `src/TabTaskExplorer.App/Views/ConnectionPanel.axaml` | 24–42（主从两栏） |
| `src/TabTaskExplorer.App/Views/IdeShellView.axaml` | 10–52（顶部）、58–76（资源树）、84–137（标签/编辑区）、141–176（查找）、181–192（状态栏）、196–199（快速打开浮层） |
| `src/TabTaskExplorer.App/ViewModels/FileTabViewModel.cs` | 80（加载文案）、187–195（行高/内边距）、493–529（磁贴尺寸）、1470（空目录文案） |
| `src/TabTaskExplorer.App/Services/AppSettingsStore.cs` | 40（默认 Dark） |
| `src/TabTaskExplorer.App/Services/FileIconCatalog.cs` | 786–805（文件类型默认色） |
| `src/TabTaskExplorer.App/Views/VectorIconPaths.cs` | 44–90（界面图标 path） |
| `src/TabTaskExplorer.App/Views/FileTypeIcon.cs` | 91–121（文件夹/磁盘/文件绘制）、136–171（紧凑/精细文件） |
| `src/TabTaskExplorer.App/Views/ProcessTrendChart.cs` | 26–41（图表色） |
| `src/TabTaskExplorer.App/Assets/web/app.css` | 2（基础字体/底色）、11–19（标题栏/标签）、21–23（图标按钮）、26（书签栏）、35–45（工具栏/菜单）、48–62（地址栏）、79–92（侧栏/信息窗格）、95–104（表格）、111–119（磁贴/提示）、152–163（看板/连接/状态栏）、173–178（弹窗）、189–210（右键菜单）、216–261（IDE）、263–308（深色主题）、337–359（预览）、361–394（任务中心） |
| `src/TabTaskExplorer.App/Assets/web/index.html` | 12–34（标题栏）、28–32（标题栏图标 12/13px）、40–85（工具栏）、88–105（导航/地址栏）、108–116（内容/瞬时提示）、119–124（状态栏） |
| `src/TabTaskExplorer.App/Assets/web/views.js` | 11（主题 class）、332–335（摘要追加）、382–431（表格）、434–488（详情表）、491–515（磁贴） |
| `src/TabTaskExplorer.App/Assets/web/panels.js` | 5–12（对比底色）、128–159（进程页）、184–239（走势图）、325–337（看板）、356–407（连接页）、742–773（状态栏/瞬时提示） |
| `src/TabTaskExplorer.App/Assets/web/core.js` | 8–33（界面图标 path）、97–128（文件图标 SVG）、277–280（主题切换 API） |
| `src/TabTaskExplorer.App/Services/WebPageHtml.cs` | 17（内联整页起点）、25–342（内联 CSS）、593（内联脚本）、75–87（`__VERSION__/__PORT__` 替换） |
| `src/TabTaskExplorer.App/Services/WebStaticAssets.cs` | 14（回退说明）、64–87（资源可用性判定/回退）、140–189（嵌入资源加载与版本哈希） |
| `src/TabTaskExplorer.App/TabTaskExplorer.App.csproj` | 25–32（网页资源嵌入与 LogicalName） |

---

## 7. 一句话总结

两端的差异不是“审美不同”，而是**样式治理方式不同**：桌面端有主题字典但没有尺寸标尺，网页端既没有令牌也没有约束，还多出一份内联旧副本。先统一颜色令牌（不改布局），再统一尺寸与状态，最后才谈图标与无障碍；这样每一步都可回滚、可截图验收，也不会碰到并行中的截图/连接/右键菜单逻辑。
