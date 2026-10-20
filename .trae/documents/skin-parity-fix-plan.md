# 皮肤对齐修正计划：App.axaml / MainWindow.axaml 对齐 TabX 官方皮肤参数

## 一、摘要

已从 TabX 官方安装目录皮肤资源（`C:\Program Files (x86)\TabXExplorer\skin\`，深色包解压于 `C:\Users\QiuHai\AppData\Local\Temp\TabXDark\`）逐项比对出 **7 项差异**。深浅色布局尺寸完全一致，差异只在色值与少量尺寸，全部为样式层面修改，涉及 `App.axaml` 与 `MainWindow.axaml` 两个文件，不动任何事件、绑定与 CroppedBitmap 资源。

## 二、7 项差异清单（比对结论）

| # | 差异项 | 当前值 | TabX 官方值 |
|---|--------|--------|-------------|
| 1 | 深色分隔线颜色 | #323232 | #FF515151（mainwnd.xml 分隔线） |
| 2 | 书签栏背景（深色缺独立色） | 复用 ContentBrush #191919 | #FF3B3B3B（mainwnd.xml 书签栏 bkcolor） |
| 3 | 工具栏文字颜色 | TextMutedBrush #8A8A8A | #FFFFFFFF（ToolBar textcolor） |
| 4 | 提示文字颜色 | #8A8A8A | #FF808080（font6） |
| 5 | 标签标题最大宽度 | MaxWidth 220 | 246（tab_item.xml textpadding 32,0,25,0 + maxwidth 246） |
| 6 | 标题栏分隔线高 / TabStrip 顶边距 | 高 20；Padding 2,4,0,0 | 高 24；顶边距 5 |
| 7 | 浅色分隔线颜色 | #D9D9D9 | #FFEBEBEC |

## 三、改动明细

### 3.1 App.axaml — Dark 字典
- `SeparatorBrush`：#323232 → **#515151**
- `TextMutedBrush`：#8A8A8A → **#808080**
- 新增 `BookmarkBarBrush`：**#3B3B3B**
- 新增 `AddressBarBorderBrush`：**#525252**（nav_bar.xml address_band bordercolor）
- 新增 `ToolbarTextBrush`：**#FFFFFF**
- `SubtleBorderBrush` 保持 #323232 不动

### 3.2 App.axaml — Light 字典
- `SeparatorBrush`：#D9D9D9 → **#EBEBEC**
- 新增 `BookmarkBarBrush`：**#FFFFFF**
- 新增 `AddressBarBorderBrush`：**#DCDDDE**
- 新增 `ToolbarTextBrush`：**#808080**

### 3.3 MainWindow.axaml（九处）
1. 行 7：`Height="760"` → `Height="800"`（mainwnd 窗口 1200,800）
2. 行 30：标题栏分隔线 `Height="20"` → `Height="24"`
3. 行 99：标题栏分隔线 `Height="20"` → `Height="24"`
4. 行 34：TabStrip `Padding="2,4,0,0"` → `Padding="0,5,0,0"`
5. 行 63：标签标题 `MaxWidth="220"` → `MaxWidth="246"`
6. 书签栏 Border（行 170 段）：`Background="{DynamicResource ContentBrush}"` → `{DynamicResource BookmarkBarBrush}`；`BorderBrush="{DynamicResource SubtleBorderBrush}"` → `{DynamicResource SeparatorBrush}`
7. 书签栏提示文字：`FontSize="11"` → `FontSize="12"`
8. 工具栏十个**文字** TextBlock（查看/复制/剪切/粘贴/新建文件夹/批量改名/回收站/显示隐藏条目/显示详细信息窗格/刷新）各加 `Foreground="{DynamicResource ToolbarTextBrush}"`；十个**图标** TextBlock 保持 TextMutedBrush 不动
9. 地址栏 Border `BorderBrush="{DynamicResource SubtleBorderBrush}"` → `{DynamicResource AddressBarBorderBrush}`

### 3.4 明确不改
- 搜索框：边框 #DCDDDE、文字 #6C6C6C、宽度 200——深色包也保留浅色值，属官方行为
- 新建标签钮 28x28 热区、所有事件/绑定/CroppedBitmap、SubtleBorderBrush 色值本身

### 3.5 编码安全
两文件均已有 BOM，全部使用 Edit 工具修改，不新建源码文件。

## 四、验证步骤
1. 确认无运行实例锁定输出文件（必要时先停旧实例）
2. `.\build.bat`（PowerShell 语法，cwd=D:\TabTaskExplorer；MSB4166 为瞬时故障，重试即可）
3. 经浏览器调试镜像 http://127.0.0.1:15919 验证深浅色切换效果（/api/theme），遵守「用户主界面不空闲」限制

## 五、假设与决策
- 深色 mainwnd.xml 书签栏 bkcolor 双#笔误 `#ff#3B3B3B` 取 #FF3B3B3B
- 深浅色布局一致，无需单独建布局差异
- 工具栏十个文字统一挂新画刷 ToolbarTextBrush
- TextMutedBrush 统一对齐官方 #808080
