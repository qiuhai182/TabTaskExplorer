# 界面令牌进度：尺寸 / 密度

> 上一轮完成颜色令牌统一（见 `docs/modern-design/ui-parity-desktop-web.md` 第一阶段）。本轮把尺寸 / 密度收进同一套令牌，只准备令牌定义与生成链，**不替换**网页 `app.css` 与桌面 AXAML 布局文件。

## 0. 上一轮（颜色令牌）状态保留

- 状态：已实施，构建 0 错误；深色网页端已实测通过（`/` 200、tokens.css / app.css 200、控制台无报错）。
- 未验：桌面像素级对照、浅色主题切换（切换会改用户持久化设置，故未做）。
- 遗留：`app.css` 中 `.boardCol`、`.boardItem`、`.ideGutter`、`.ideFind`、`.connWarn`、`.taskConflict` 仍有颜色字面量（见 §7 第 4 条）。

## 1. 当前磁盘状态（本轮结束时的落点）

| 文件 | 状态 | 说明 |
|---|---|---|
| `design/tokens/ui-tokens.json` | 完成 | `version: 2`；颜色 26 项 × (light/dark/avalonia)，尺寸 63 项；每项有 `source`（桌面出处）与 `note`；JSON 可正常解析 |
| `tools/gen-design-tokens.ps1` | 完成 | 校验颜色与尺寸两类字段；一次生成 Axaml + tokens.css；回退页只同步颜色块（见 §3）；脚本为 UTF-8 **带 BOM** |
| `src/TabTaskExplorer.App/Themes/Tokens.axaml` | 已生成 | 52 个 `SolidColorBrush`（26×2）+ 52 个 `sys:Double` + 7 个 `Thickness` + 4 个 `CornerRadius` = 63 个尺寸资源；`dotnet build` 通过 |
| `src/TabTaskExplorer.App/Assets/web/tokens.css` | 已生成 | 89 个 `--tte-*`（26 颜色 + 63 尺寸），花括号配平，`:root` 颜色 + 尺寸，`body.dark` 只覆盖颜色 |
| `src/TabTaskExplorer.App/Services/WebPageHtml.cs` | 未改动 | 生成器重跑后该文件零差异（已用 `git diff --stat` 确认） |

本轮未触碰：`Assets/web/app.css`、`index.html`、`views.js`、`panels.js` 及任何桌面 AXAML 布局文件（另一会话正在改网页端结构）。

## 2. 尺寸 / 密度令牌清单（63 项）

命名规则：JSON 名 → Avalonia 资源键 `Token<大驼峰>` → CSS 变量 `--tte-<kebab>`。尺寸与主题无关，深浅两套共用；颜色仍由 ThemeDictionaries 分套。

### 2.1 栏高与外壳（10）

| JSON 名 | Avalonia 键 | CSS 变量 | 值 | 桌面依据 | 备注 |
|---|---|---|---|---|---|
| titleBarHeight | TokenTitleBarHeight | --tte-title-bar-height | 36 | MainWindow.axaml:11,21 | 标题栏 + 窗口铬 |
| tabHeight | TokenTabHeight | --tte-tab-height | 28 | MainWindow.axaml:51 | 网页现值 26，需 +2 |
| bookmarkBarHeight | TokenBookmarkBarHeight | --tte-bookmark-bar-height | 36 | MainWindow.axaml:167,182 | 由 18px 图标 + Fluent 默认按钮高 32 + Margin 2 推得 |
| toolbarHeight | TokenToolbarHeight | --tte-toolbar-height | 36 | MainWindow.axaml:208 | 按 Fluent 按钮默认高 32 推算，**未像素实测** |
| navRowHeight | TokenNavRowHeight | --tte-nav-row-height | 36 | MainWindow.axaml:321,333 | 地址栏 30 + 上下 2 + 边框 |
| statusBarHeight | TokenStatusBarHeight | --tte-status-bar-height | 26 | MainWindow.axaml:566,567 | 12px 文字 + 上下 4 + 上边框 |
| addressBarHeight | TokenAddressBarHeight | --tte-address-bar-height | 30 | MainWindow.axaml:333 | 网页现值 28 |
| navButtonSize | TokenNavButtonSize | --tte-nav-button-size | 28 | MainWindow.axaml:80,91 | 导航 / 图标按钮方形边长 |
| captionButtonWidth | TokenCaptionButtonWidth | --tte-caption-button-width | 40 | MainWindow.axaml:113,121 | 标题栏窗口按钮宽 |
| captionButtonHeight | TokenCaptionButtonHeight | --tte-caption-button-height | 36 | MainWindow.axaml:113,121 | 与标题栏同高 |

### 2.2 内边距（7，Thickness）

| JSON 名 | Avalonia 键 | CSS 变量 | 值 | 桌面依据 | 备注 |
|---|---|---|---|---|---|
| toolbarPadding | TokenToolbarPadding | --tte-toolbar-padding | 6,2 → `2px 6px` | MainWindow.axaml:208 | 左右 6 / 上下 2 |
| bookmarkBarPadding | TokenBookmarkBarPadding | --tte-bookmark-bar-padding | 8,2 → `2px 8px` | MainWindow.axaml:167 | 左右 8 / 上下 2 |
| statusBarPadding | TokenStatusBarPadding | --tte-status-bar-padding | 12,4 → `4px 12px` | MainWindow.axaml:566 | 左右 12 / 上下 4 |
| rowPadding | TokenRowPadding | --tte-row-padding | 2,3 → `3px 2px` | FileTabViewModel.cs:196 | 行内边距 |
| rowPaddingCompact | TokenRowPaddingCompact | --tte-row-padding-compact | 2,1 → `1px 2px` | FileTabViewModel.cs:196 | 紧凑行内边距 |
| controlPadding | TokenControlPadding | --tte-control-padding | 9,4 → `4px 9px` | Fluent Button 模板 | **未像素实测** |
| dialogPadding | TokenDialogPadding | --tte-dialog-padding | 16,16 → `16px` | MessageDialog.axaml:7 | 对话框四边 |

注：Avalonia `Thickness(水平, 垂直)`，生成器输出 CSS 时按 `上下 左右` 顺序转换，已在脚本内注释。

### 2.3 行高与字号（6）

| JSON 名 | Avalonia 键 | CSS 变量 | 值 | 桌面依据 |
|---|---|---|---|---|
| rowHeight | TokenRowHeight | --tte-row-height | 28 | FileTabViewModel.cs:190 |
| rowHeightCompact | TokenRowHeightCompact | --tte-row-height-compact | 22 | FileTabViewModel.cs:190 |
| fontSizeBody | TokenFontSizeBody | --tte-font-size-body | 13 | app.css:2（桌面正文 14，列表按 13 对齐密度） |
| fontSizeSmall | TokenFontSizeSmall | --tte-font-size-small | 12 | MainWindow.axaml:171,567 |
| fontSizeTiny | TokenFontSizeTiny | --tte-font-size-tiny | 11 | MainWindow.axaml:568 |
| fontSizeLarge | TokenFontSizeLarge | --tte-font-size-large | 15 | app.css:280 |
| lineHeightBody | TokenLineHeightBody | --tte-line-height-body | 18 | app.css:299 |

### 2.4 图标与磁贴（15）

| JSON 名 | Avalonia 键 | CSS 变量 | 值 | 桌面依据 |
|---|---|---|---|---|
| iconSizeSmall | TokenIconSizeSmall | --tte-icon-size-small | 14 | MainWindow.axaml:64,212 |
| iconSizeMedium | TokenIconSizeMedium | --tte-icon-size-medium | 16 | MainWindow.axaml:116,124 |
| iconSizeLarge | TokenIconSizeLarge | --tte-icon-size-large | 18 | MainWindow.axaml:168 |
| iconSizeTab | TokenIconSizeTab | --tte-icon-size-tab | 14 | MainWindow.axaml:64 |
| thumbnailSizeList | TokenThumbnailSizeList | --tte-thumbnail-size-list | 20 | FileListPanel.axaml:123 |
| tileIconSizeExtraLarge | TokenTileIconSizeExtraLarge | --tte-tile-icon-size-extra-large | 64 | FileTabViewModel.cs:534 |
| tileIconSizeLarge | TokenTileIconSizeLarge | --tte-tile-icon-size-large | 48 | 同上 |
| tileIconSizeMedium | TokenTileIconSizeMedium | --tte-tile-icon-size-medium | 36 | 同上 |
| tileIconSizeSmall | TokenTileIconSizeSmall | --tte-tile-icon-size-small | 24 | 同上 |
| tileIconSizeTile | TokenTileIconSizeTile | --tte-tile-icon-size-tile | 40 | 同上 |
| tileWidthExtraLarge | TokenTileWidthExtraLarge | --tte-tile-width-extra-large | 210 | FileTabViewModel.cs:513 |
| tileWidthLarge | TokenTileWidthLarge | --tte-tile-width-large | 130 | 同上 |
| tileWidthMedium | TokenTileWidthMedium | --tte-tile-width-medium | 96 | 同上 |
| tileWidthSmall | TokenTileWidthSmall | --tte-tile-width-small | 64 | 同上 |
| tileIconSize（磁贴缩略图 32） | — | — | 32 | 网页 app.css:114，桌面无对应，暂不建令牌 |

### 2.5 圆角与描边（6）

| JSON 名 | Avalonia 键 | CSS 变量 | 值 | 桌面依据 |
|---|---|---|---|---|
| radiusSmall | TokenRadiusSmall | --tte-radius-small | 3 | MainWindow.axaml:333 |
| radiusMedium | TokenRadiusMedium | --tte-radius-medium | 4 | App.axaml:137,143 |
| radiusLarge | TokenRadiusLarge | --tte-radius-large | 6 | app.css:42（桌面 Fluent 菜单默认 4） |
| radiusXLarge | TokenRadiusXLarge | --tte-radius-x-large | 8 | app.css:170 |
| borderWidth | TokenBorderWidth | --tte-border-width | 1 | App.axaml:104 |
| borderWidthEmphasis | TokenBorderWidthEmphasis | --tte-border-width-emphasis | 2 | app.css:15,265 |
| focusOutlineWidth | TokenFocusOutlineWidth | --tte-focus-outline-width | 1 | app.css:103,113 |
| focusOutlineOffset | TokenFocusOutlineOffset | --tte-focus-outline-offset | -1 | app.css:103 |

### 2.6 间距（6）

| JSON 名 | Avalonia 键 | CSS 变量 | 值 | 桌面依据 |
|---|---|---|---|---|
| spacingXSmall | TokenSpacingXSmall | --tte-spacing-x-small | 2 | MainWindow.axaml:90,208 |
| spacingSmall | TokenSpacingSmall | --tte-spacing-small | 4 | MainWindow.axaml:167 |
| spacingMedium | TokenSpacingMedium | --tte-spacing-medium | 6 | MainWindow.axaml:321 |
| spacingLarge | TokenSpacingLarge | --tte-spacing-large | 8 | FileListPanel.axaml:13 |
| spacingXLarge | TokenSpacingXLarge | --tte-spacing-x-large | 12 | MessageDialog.axaml:7 |
| spacingXXLarge | TokenSpacingXXLarge | --tte-spacing-xx-large | 16 | MessageDialog.axaml:7 |

### 2.7 宽度与面板（12）

| JSON 名 | Avalonia 键 | CSS 变量 | 值 | 桌面依据 | 备注 |
|---|---|---|---|---|---|
| sidebarWidth | TokenSidebarWidth | --tte-sidebar-width | 200 | MainWindow.axaml:377 | 与网页现值一致 |
| infoPaneWidth | TokenInfoPaneWidth | --tte-info-pane-width | 220 | FileListPanel.axaml:188 | 网页现值 240，需下调 |
| searchBoxWidth | TokenSearchBoxWidth | --tte-search-box-width | 200 | MainWindow.axaml:372 | 一致 |
| modalPanelWidth | TokenModalPanelWidth | --tte-modal-panel-width | 900 | SettingsWindow.axaml:5 | 网页现值 1000 |
| modalPanelHeight | TokenModalPanelHeight | --tte-modal-panel-height | 640 | SettingsWindow.axaml:5 | 网页现值 760 |
| dialogWidth | TokenDialogWidth | --tte-dialog-width | 380 | MessageDialog.axaml:4 | |
| modalHeadHeight | TokenModalHeadHeight | --tte-modal-head-height | 40 | app.css:172 | 桌面无同类统一面板 |
| modalFootHeight | TokenModalFootHeight | --tte-modal-foot-height | 44 | app.css:174 | 同上 |
| recentPopupMinWidth | TokenRecentPopupMinWidth | --tte-recent-popup-min-width | 380 | MainWindow.axaml:349 | |
| recentPopupMaxHeight | TokenRecentPopupMaxHeight | --tte-recent-popup-max-height | 320 | MainWindow.axaml:349 | |
| contextMenuMinWidth | TokenContextMenuMinWidth | --tte-context-menu-min-width | 190 | app.css:185 | 桌面菜单取 Fluent 默认 |

> 计数说明：2.3 节 7 行为行高 + 字号，2.4 节 14 项令牌，2.5 节 8 项。总计 63 项尺寸令牌，生成后与 `tokens.css` 中 `--tte-*` 数量一致（26 + 63 = 89）。

## 3. 生成链

```powershell
powershell -ExecutionPolicy Bypass -File tools/gen-design-tokens.ps1
```

输出三处：

1. `Themes/Tokens.axaml`：根级尺寸资源（`sys:Double` / `Thickness` / `CornerRadius`）+ `ThemeDictionaries` 内深浅两套颜色画刷。
2. `Assets/web/tokens.css`：`--tte-*` 变量，`:root`（颜色浅色 + 全部尺寸）、`body.dark`（只覆盖颜色）。
3. `Services/WebPageHtml.cs` 内联回退样式：只同步 `TTE-TOKENS-BEGIN/END` 之间的**颜色块**。

第 3 点为何只同步颜色：回退页是最小可用样式，尺寸仍用固定 px（本轮不允许改该文件）；颜色块必须与 `tokens.css` 同源，尺寸不需要。生成器写入 .cs 时保留 UTF-8 BOM，重跑后该文件零差异（这是幂等的判据）。

## 4. 网页端 px 字面量盘点

基线：`HEAD 359c357`，`app.css` 353 行，SHA-256 前 16 位 `C296A7E4DC96842F`。

- px 出现总数：**566**，分布在 **191 行**（含 4 个 `box-shadow`、`calc()`、`min()/max()` 内的 px）。
- 按属性：`border` 系 52 处、`padding` 系 ~120 处、`font-size` 系 ~60 处、`gap` ~30 处、`border-radius` ~30 处、`height/min-height/max-height` ~60 处、`width/max-width` ~70 处、`margin` ~40 处、`outline` 4 处。

与任务描述里的「313 处」不一致：**本轮实测为 566 处 / 191 行**，且另一会话正在改 `app.css`，数字会继续漂移。因此下表的行号只在上述基线内有效，替换时以「选择器 + 令牌」列为准，不要按行号批量替换。

## 5. `app.css` 替换对照清单（逐行）

图例：令牌列省略 `--tte-` 前缀；`—` 表示本轮不替换（原因见 §7）。

| 行 | 选择器 | 现有 px 声明 | 目标令牌 |
|---|---|---|---|
| 2 | body | font:13px | font-size-body |
| 5 | .ico | width/height:14px | icon-size-small |
| 8 | .diskArrow | width/height:14px；font-size:11px | icon-size-small；font-size-tiny |
| 11 | .titlebar | height:36px；border-bottom:1px；padding:0 8px；gap:6px | title-bar-height；border-width；—；spacing-medium |
| 12 | .logo | width/height:22px | — |
| 14 | .tabs | gap:2px | spacing-x-small |
| 15 | .tab | height/line-height:26px；padding:0 10px；border-radius:4px；border-bottom:2px；gap:6px | tab-height（28）；—；radius-medium；border-width-emphasis；spacing-medium |
| 17 | .tab.active | border-bottom:2px | border-width-emphasis |
| 18 | .tab .x | margin-left:2px；padding:0 3px | spacing-x-small；— |
| 20 | .meta | font-size:12px | font-size-small |
| 21 | .modeBtn, .iconBtn | padding:2px 9px；border:1px；border-radius:4px；font:12px | —（建议 control-padding）；border-width；radius-medium；font-size-small |
| 23 | .iconBtn | width:26px；height:24px | nav-button-size（28） |
| 26 | .bookmarkbar | gap:4px；padding:2px 8px；border-bottom:1px；min-height:28px | spacing-small；bookmark-bar-padding；border-width；bookmark-bar-height（36） |
| 27 | .bmItem | gap:5px；padding:2px 7px；border-radius:4px；font-size:12px | —；—；radius-medium；font-size-small |
| 30 | .bmItem .bmX | padding:0 2px；border-radius:3px | —；radius-small |
| 32 | .bmHint | font-size:12px | font-size-small |
| 35 | .toolbar | gap:2px；padding:3px 8px；border-bottom:1px | spacing-x-small；toolbar-padding（2px 6px）；border-width；另加 toolbar-height |
| 36 | .btn | padding:3px 9px；border-radius:4px | control-padding（4px 9px）；radius-medium |
| 40 | .btn .tick | width:12px | — |
| 42 | .menu | margin-top:3px；border:1px；border-radius:6px；box-shadow；padding:4px；min-width:180px | —；border-width；radius-large；—；spacing-small；— |
| 43 | .menu .item | padding:5px 10px；border-radius:4px；gap:4px；font-size:12px | —；radius-medium；spacing-small；font-size-small |
| 45 | .menu .sep | height:1px；margin:4px 2px | border-width；spacing-small / spacing-x-small |
| 48 | .navrow | gap:6px；padding:4px 8px；border-bottom:1px | spacing-medium；—；border-width；另加 nav-row-height |
| 49 | .navbtns | gap:2px | spacing-x-small |
| 50 | .tbtn | width:28px；height:26px；border-radius:4px | nav-button-size；radius-medium |
| 54 | .addr | border:1px；border-radius:3px；min-height:28px；padding:0 2px | border-width；radius-small；address-bar-height（30）；— |
| 55 | .crumb | gap:2px；padding:2px 4px；min-height:24px | spacing-x-small；—；— |
| 56 | .seg | padding:2px 6px；border-radius:4px | —；radius-medium |
| 58 | .sep | padding:0 1px | — |
| 59 | .addrInput | padding:4px 6px；font:13px | —；font-size-body |
| 61 | .searchBox | width:200px；border:1px；border-radius:3px；padding:5px 8px；font:13px | search-box-width；border-width；radius-small；—；font-size-body |
| 63 | .connToolbar | padding:6px 2px；gap:6px | —；spacing-medium |
| 64 | .connForm | padding:8px 2px；grid 80/140/760；gap:6px 8px | —；—；spacing-medium / spacing-large |
| 65 | .connForm label | font-size:12px | font-size-small |
| 66 | .connForm input/select/textarea | border:1px；border-radius:3px；padding:4px 6px；font:13px | border-width；radius-small；—；font-size-body |
| 69 | .startupCheck | gap:5px | — |
| 71 | .pop | margin-top:3px；border:1px；border-radius:6px；box-shadow；padding:4px；min-width:360px；max-height:320px | —；border-width；radius-large；—；spacing-small；—；recent-popup-max-height |
| 72 | .pop .item | padding:5px 8px；border-radius:4px；font-size:12px | —；radius-medium；font-size-small |
| 74 | .pop .empty | padding:6px 8px；font-size:12px | spacing-medium / spacing-large；font-size-small |
| 78 | .sidebar | width:200px；border-right:1px；padding:6px 4px | sidebar-width；border-width；— |
| 79 | .navsec | gap:6px；margin:8px 6px 4px；font-size:12px | spacing-medium；spacing-large / medium / small；font-size-small |
| 80 | .navsec.clickable | border-radius:4px | radius-medium |
| 82 | .navitem | gap:7px；padding:4px 8px；border-radius:4px；font-size:12px | —；—；radius-medium；font-size-small |
| 86 | .content | padding:6px 10px | — |
| 87 | .infoPane | width:240px；border-left:1px；padding:8px 10px；font-size:12px | info-pane-width（220）；border-width；—；font-size-small |
| 88 | .infoPane h4 | font-size:12px；margin-bottom:6px | font-size-small；spacing-medium |
| 89 | .infoBlock | border-bottom:1px；padding:5px 0 | border-width；— |
| 90 | .infoBlock b | margin-bottom:2px | spacing-x-small |
| 95 | th | border-bottom:1px；padding:5px 10px | border-width；— |
| 98 | th .arrow | font-size:10px；margin-left:3px | —；— |
| 99 | td | padding:4px 10px；border-bottom:1px | —；border-width |
| 103 | tr.row.focus | outline:1px；outline-offset:-1px | focus-outline-width；focus-outline-offset |
| 104 | td.name | gap:7px | — |
| 106 | td.clip | max-width:200px | — |
| 109 | .tiles | gap:6px | spacing-medium |
| 110 | .tile | border:1px；border-radius:5px；padding:8px 4px 6px | border-width；—；— |
| 113 | .tile.focus | outline:1px | focus-outline-width |
| 114 | .tile .ticon | width/height:32px；margin:0 auto 2px | —；spacing-x-small |
| 115 | .tile .tname | font-size:12px；margin-top:4px | font-size-small；spacing-small |
| 116 | .tile .ttype | font-size:11px | font-size-tiny |
| 117 | .tile .ttick | left:4px；top:4px | spacing-small |
| 118 | .hint | margin:6px 0 | spacing-medium |
| 121 | .cmpBar | gap:6px；padding:4px 0 8px | spacing-medium；— |
| 122 | .cmpLbl | font-size:12px | font-size-small |
| 123 | .cmpInput | min-width:180px；border:1px；border-radius:3px；padding:5px 8px；font:13px | —；border-width；radius-small；—；font-size-body |
| 125 | .cmpOpts | gap:10px；font-size:12px；padding:0 0 8px | —；font-size-small；spacing-large |
| 126 | .cmpOpts label | gap:4px | spacing-small |
| 129 | .cmpMeta | font-size:12px；padding:0 0 6px | font-size-small；spacing-medium |
| 130 | .cmpBadge | padding:1px 7px；border-radius:9px；font-size:11px | —；—；font-size-tiny |
| 132 | .cmpLineNo | width:52px；font:12px | —；font-size-small |
| 133 | .cmpText | font:12px | font-size-small |
| 134 | .cmpChanged | border-radius:2px；padding:0 1px | —；— |
| 137 | .procCtrl | gap:6px；padding:6px 0；font-size:13px | spacing-medium；—；font-size-body |
| 138 | .procCtrl select/input | border:1px；border-radius:3px；padding:3px 6px；font:13px | border-width；radius-small；—；font-size-body |
| 140 | .procCtrl input | width:160px | — |
| 141 | .trendLegend | gap:16px；font-size:11px；padding:6px 0 2px | spacing-xx-large；font-size-tiny；— |
| 142 | .trendLegend i | width:12px；height:4px；border-radius:2px；margin-right:4px | —；—；—；spacing-small |
| 143 | #trendCanvas | height:78px；border:1px；border-radius:4px | —；border-width；radius-medium |
| 144 | .procDetail | border:1px；border-radius:4px；padding:6px 8px；margin-top:6px；font:12px；min-height:64px；max-height:96px | border-width；radius-medium；—；spacing-medium；font-size-small；—；— |
| 149 | .board | gap:10px | — |
| 150 | .boardCol | border:1px #E9E9E9；border-radius:6px；padding:8px | border-width（颜色遗留，见 §7）；radius-large；spacing-large |
| 151 | .boardCol h4 | font-size:12px；margin-bottom:6px | font-size-small；spacing-medium |
| 152 | .boardItem | border:1px #E4E4E4；border-radius:4px；padding:6px 8px；margin-bottom:6px；font-size:12px | border-width（颜色遗留）；radius-medium；—；spacing-medium；font-size-small |
| 155 | .connWarn | margin-left:6px；padding:0 5px；border-radius:3px；font-size:11px；line-height:16px；border:1px | spacing-medium；—；radius-small；font-size-tiny；—；border-width |
| 159 | .statusbar | border-top:1px；padding:5px 12px；font-size:12px；gap:8px | border-width；status-bar-padding（4px 12px）；font-size-small；spacing-large；另加 status-bar-height |
| 161 | .connState | gap:5px | — |
| 162 | .connDot | width/height:8px | — |
| 170 | .panel | width:min(1000px,92vw)；height:min(760px,88vh)；border-radius:8px | modal-panel-width（900）；modal-panel-height（640）；radius-x-large |
| 171 | .panel.small | width:min(720px,92vw)；height:min(560px,84vh) | —（建议新增 panel-small 令牌） |
| 172 | .panelHead | height:40px；gap:8px；padding:0 12px；border-bottom:1px | modal-head-height；spacing-large；spacing-x-large；border-width |
| 174 | .panelFoot | height:44px；gap:8px；padding:0 12px；border-top:1px | modal-foot-height；spacing-large；spacing-x-large；border-width |
| 175 | textarea | padding:10px 12px；font:13px | —；font-size-body |
| 176 | .batchList | padding:8px 12px | spacing-large / spacing-x-large |
| 177 | .brow | gap:8px；padding:3px 0 | spacing-large；— |
| 179 | .brow input | border:1px；border-radius:3px；padding:4px 6px；font:13px | border-width；radius-small；—；font-size-body |
| 182 | .encodeCtrl | gap:8px；padding:8px 12px；border-bottom:1px；font-size:13px | spacing-large；spacing-large / x-large；border-width；font-size-body |
| 183 | .encodeCtrl select | border:1px；border-radius:3px；padding:4px 6px；font:13px | border-width；radius-small；—；font-size-body |
| 184 | .encodeCtrl label | gap:4px | spacing-small |
| 185 | .ctx | border:1px；border-radius:6px；box-shadow；padding:4px；min-width:190px；max-height:calc(100vh - 16px) | border-width；radius-large；—；spacing-small；context-menu-min-width；— |
| 186 | .ctx .item | padding:5px 12px；border-radius:4px；font-size:12px | —；radius-medium；font-size-small |
| 189 | .ctxItem | gap:6px；padding:5px 10px；border-radius:4px；font-size:12px | spacing-medium；—；radius-medium；font-size-small |
| 193/194 | .ctxItem .ctxIco | width/height:16px | icon-size-medium |
| 197 | .ctxItem .ctxAcc | font-size:11px；margin-left:16px | font-size-tiny；spacing-xx-large |
| 198 | .ctxItem .ctxArrow | width:12px | — |
| 199/200 | .ctxCheck / .ctxShield | width:14px | icon-size-small |
| 201/202 | .ctxItem .ctxSub / .ctxSub | min-width:190px；border:1px；border-radius:6px；padding:4px | context-menu-min-width；border-width；radius-large；spacing-small |
| 204 | .ctxSep | height:1px；margin:4px 6px | border-width；spacing-small / spacing-medium |
| 205 | .ctxHead | padding:4px 10px 2px；font-size:11px | —；font-size-tiny |
| 206 | .ctxHint | padding:5px 10px；font-size:11px；max-width:260px | —；font-size-tiny；— |
| 207 | .diagBox | padding:4px 12px；border-top:1px；font-size:12px | —；border-width；font-size-small |
| 209 | pre.diag | font:11px；padding:8px；border-radius:6px；max-height:240px；margin-top:6px | font-size-tiny；spacing-large；radius-large；—；spacing-medium |
| 213 | .ideTop | gap:4px；padding:4px 8px；border-bottom:1px | spacing-small；—；border-width |
| 214 | .ideRoot | font-size:12px | font-size-small |
| 216 | .ideTree | width:230px；border-right:1px | —；border-width |
| 217 | .ideTreeHead | font-size:11px；padding:6px 8px 2px | font-size-tiny；— |
| 218 | .ideTreeList | padding:0 4px 8px | — |
| 219 | .ideNode | gap:5px；padding:2px 4px；border-radius:3px；font-size:12px | —；—；radius-small；font-size-small |
| 222 | .ideNode .arrow | width:10px；font-size:9px | —；— |
| 224 | .ideSplit | width:1px | border-width |
| 226 | .ideTabs | gap:2px；padding:3px 4px 0；border-bottom:1px；min-height:30px | spacing-x-small；—；border-width；—（建议 ide-tab-height） |
| 227 | .ideTab | gap:5px；padding:4px 6px 4px 8px；border:1px；border-radius:4px；font-size:12px | —；—；border-width；radius-medium；font-size-small |
| 231 | .ideTab .x | padding:0 3px；border-radius:3px | —；radius-small |
| 234 | .ideGutter | width:54px；padding:6px 8px 6px 0；border-right:1px；font:13px | —；—；border-width；font-size-body |
| 235 | .ideText | padding:6px 8px；font:13px | —；font-size-body |
| 236 | .ideEmpty | font-size:12px；padding:0 20px | font-size-small；— |
| 237 | .ideFind | border-top:1px；max-height:240px | border-width；— |
| 238 | .ideFindRow | gap:6px；padding:6px 8px | spacing-medium；— |
| 239 | .ideFindRow .lbl | font-size:12px | font-size-small |
| 240 | .ideFindRow input | border:1px；border-radius:3px；padding:4px 6px；font:12px；width:170px | border-width；radius-small；—；font-size-small；— |
| 242 | .ideFindStatus | font-size:11px；padding:0 10px 4px | font-size-tiny；— |
| 243 | .ideFindList | padding:0 6px 6px | — |
| 244 | .ideFRes | gap:10px；padding:3px 6px；border-radius:3px；font-size:12px | —；—；radius-small；font-size-small |
| 246 | .ideFRes .ln | font-size:11px | font-size-tiny |
| 247 | .ideStatus | border-top:1px；padding:4px 12px；font-size:12px；gap:8px | border-width；—；font-size-small；spacing-large |
| 248 | .ideStatus .loc | max-width:360px | — |
| 249 | .ideQuick | top:70px；width:560px；border:1px；border-radius:6px；padding:10px；max-height:420px | —；—；border-width；radius-large；—；— |
| 250 | .ideQuick input | border:1px；border-radius:3px；padding:6px 8px；font:12px | border-width；radius-small；—；font-size-small |
| 253 | .ideQItem | gap:10px；padding:4px 6px；border-radius:4px；font-size:12px | —；—；radius-medium；font-size-small |
| 254 | .ideQItem .dir | font-size:11px；max-width:300px | font-size-tiny；— |
| 258 | .idePromptBody | padding:14px 12px；font-size:13px | —；font-size-body |
| 263 | body.dark .ctxSub | box-shadow | — |
| 265 | tr.row.dragTarget | outline:2px；outline-offset:-1px | border-width-emphasis；focus-outline-offset |
| 279 | .hostKeyBox | width:560px；border-radius:8px；padding:16px；box-shadow | —；radius-x-large；spacing-xx-large；— |
| 280 | .hostKeyTitle | font-size:15px；margin-bottom:10px | font-size-large；— |
| 281 | .hostKeyLine | font-size:13px；margin:4px 0 | font-size-body；spacing-small |
| 283/284 | .hostKeyWarn / Hint | margin-top:8px；font-size:12px | spacing-large；font-size-small |
| 285 | .hostKeyBtns | margin-top:14px；gap:8px | —；spacing-large |
| 289 | .thumbBox | width/height:16px；border-radius:2px | icon-size-medium；— |
| 297/298 | .tileThumb / .rowThumbImg | border-radius:3px | radius-small |
| 299 | #contentMain.compact tr.row td | padding-top/bottom:1px；line-height:18px | row-padding-compact；line-height-body |
| 300 | .compact .tile .tname | margin-top:1px | row-padding-compact |
| 303 | .previewPane | right:16px；top:72px；bottom:40px；width:420px；max-width | —（预览层定位值，建议下一轮单独建令牌） |
| 304 | .previewPane | border:1px；border-radius:8px | border-width；radius-x-large |
| 306 | .previewHead | gap:8px；padding:8px 10px；border-bottom:1px | spacing-large；—；border-width |
| 307 | .previewTitle | font-size:13px | font-size-body |
| 308 | .previewClose | width:22px；font-size:16px | —；— |
| 310 | .previewBody | padding:10px | — |
| 311 | .previewMeta | padding:6px 10px；border-top:1px；font-size:11px | —；border-width；font-size-tiny |
| 312 | .previewText | padding:4px；font:12px | spacing-small；font-size-small |
| 315/318 | 预览占位 svg | width/height:64px | tile-icon-size-extra-large |
| 317 | .previewPlaceholder | gap:14px | — |
| 319 | .previewHint | font-size:12px；max-width:330px | font-size-small；— |
| 324 | .taskBadge | font-size:11px；margin-left:3px | font-size-tiny；— |
| 325 | .taskPanel | right:16px；top:72px；bottom:40px；width:440px | —（同预览层，下一轮统一） |
| 326 | .taskPanel | border:1px；border-radius:8px | border-width；radius-x-large |
| 328 | .taskHead | padding:8px 10px；border-bottom:1px | —；border-width |
| 329 | .taskTitle | font-size:13px | font-size-body |
| 330 | .taskList | padding:8px 10px | — |
| 331 | .taskFoot | padding:6px 10px；border-top:1px；font-size:11px | —；border-width；font-size-tiny |
| 332 | .taskRow | border:1px；border-radius:6px；padding:7px 8px；margin-bottom:8px | border-width；radius-large；—；spacing-large |
| 335 | .taskRowHead | gap:6px | spacing-medium |
| 336 | .taskKind | font-size:12px | font-size-small |
| 337 | .taskTag | border:1px；border-radius:3px；padding:0 3px；font-size:10px | border-width；radius-small；—；— |
| 338 | .taskState | font-size:11px | font-size-tiny |
| 339 | .taskPaths | margin-top:4px；font-size:11px | spacing-small；font-size-tiny |
| 340 | .taskBar | height:6px；border-radius:3px；margin-top:5px | —；radius-small；— |
| 344 | .taskMeta | margin-top:4px；font-size:11px | spacing-small；font-size-tiny |
| 345 | .taskActions | gap:6px；margin-top:5px | spacing-medium；— |
| 346 | .taskConflict | margin-top:6px；border-top:1px；padding-top:6px | spacing-medium；border-width；spacing-medium |
| 347 | .taskConflictPath | font-size:11px | font-size-tiny |
| 348 | .taskAll | gap:4px；font-size:11px；margin:4px 0 | spacing-small；font-size-tiny；spacing-small |
| 350 | .taskConflictBtns | gap:5px | — |

覆盖情况：191 行中含 px 的声明全部列出；其中 `font-size` 系整体可替换，`border` 系统一为 `border-width`，`gap` 系按 2/4/6/8/12/16 落到 spacing 梯度，`border-radius` 按 3/4/6/8 落到 radius 梯度。

## 6. 桌面 AXAML 替换清单（下一轮执行）

AXAML 用无单位数字，替换写法为 `{DynamicResource TokenXxx}`：长度为 `sys:Double`，内边距为 `Thickness`，圆角为 `CornerRadius`。

### 6.1 `Views/MainWindow.axaml`

| 位置 | 属性 | 现值 | 令牌 |
|---|---|---|---|
| 窗口 | ExtendClientAreaTitleBarHeightHint | 36 | TokenTitleBarHeight |
| 标题栏 | Grid Height | 36 | TokenTitleBarHeight |
| 标题栏窗口按钮 | Width / Height | 40 / 36 | TokenCaptionButtonWidth / TokenCaptionButtonHeight |
| 标题栏按钮图标 | Width/Height | 16 | TokenIconSizeMedium |
| 标签栏 | ListBoxItem MinHeight、内部 Grid Height | 28 | TokenTabHeight |
| 标签栏 | StackPanel Spacing | 2 | TokenSpacingXSmall |
| 标签栏 | 标签 Padding | 8,0,4,0 | TokenSpacingLarge（水平） |
| 标签图标 | Width/Height | 14 | TokenIconSizeTab |
| 导航按钮 | Width/Height | 28 | TokenNavButtonSize |
| 书签栏 | StackPanel Margin | 8,2 | TokenBookmarkBarPadding |
| 书签栏 | StackPanel Spacing | 4 | TokenSpacingSmall |
| 书签标题图标 | Width/Height | 18 | TokenIconSizeLarge |
| 工具栏 | StackPanel Margin | 6,2 | TokenToolbarPadding |
| 工具栏按钮图标 | Width/Height | 14 | TokenIconSizeSmall |
| 导航行 | StackPanel Margin | 2,4 | TokenToolbarPadding 派生（建议单独 navRowPadding） |
| 地址栏 | Border MinHeight | 30 | TokenAddressBarHeight |
| 地址栏 | CornerRadius | 3 | TokenRadiusSmall |
| 地址栏内输入 | FontSize | 14 | TokenFontSizeBody |
| 搜索框 | Width | 200 | TokenSearchBoxWidth |
| 最近访问 Popup | MinWidth / MaxHeight | 380 / 320 | TokenRecentPopupMinWidth / TokenRecentPopupMaxHeight |
| 侧栏列 | ColumnDefinition Width | 200 | TokenSidebarWidth |
| 状态栏 | StackPanel Margin | 12,4 | TokenStatusBarPadding |
| 状态栏文字 | FontSize | 12 / 11 | TokenFontSizeSmall / TokenFontSizeTiny |

### 6.2 `Views/FileListPanel.axaml`

| 位置 | 属性 | 现值 | 令牌 |
|---|---|---|---|
| 信息窗格 | Border Width | 220 | TokenInfoPaneWidth |
| 信息窗格 | Padding | 10 | TokenSpacingLarge |
| 信息窗格 | StackPanel Spacing | 6 | TokenSpacingMedium |
| 缩略图（详细信息视图） | Width/Height | 20 | TokenThumbnailSizeList |
| 列表表头 | Margin / FontSize | 0,0,0,4 / 12 | TokenSpacingSmall / TokenFontSizeSmall |

行高与行内边距**不在 AXAML**：来源是 `FileTabViewModel.RowHeight`（28 / 22）与 `RowPadding`（2,3 / 2,1）。替换方式（下一轮）：在 ViewModel 构造时从 `Application.Current.Resources` 读取 `TokenRowHeight` / `TokenRowHeightCompact` / `TokenRowPadding` / `TokenRowPaddingCompact`，保留现有属性名以免牵连绑定。

磁贴宽与图标边长同理来自 `FileTabViewModel` 的 `TileWidth` / `TileIconSize`，按视图模式映射到 `TokenTileWidth*` / `TokenTileIconSize*`。

### 6.3 对话框 / 窗口

| 文件 | 属性 | 现值 | 令牌 |
|---|---|---|---|
| MessageDialog.axaml | Width | 380 | TokenDialogWidth |
| MessageDialog.axaml | Margin | 16 | TokenDialogPadding |
| MessageDialog.axaml | StackPanel Spacing | 12 | TokenSpacingXLarge |
| SettingsWindow.axaml | Width / Height | 900 / 640 | TokenModalPanelWidth / TokenModalPanelHeight |

### 6.4 本轮不纳入

`IdeShellView.axaml`（91 处尺寸属性）、`StartupPanel.axaml`（63）、`ConnectionPanel.axaml`（56）、`ProcessPanel.axaml`（50）本轮不替换：这些是面板内部布局，需先确认与网页对应面板的结构一致，否则容易在密度上互相打架。建议作为第三阶段单独处理。

## 7. 本轮刻意不纳入令牌的项

1. **组件专属尺寸**：`.logo` 22、磁贴缩略图 32、`.cmpLineNo` 52、`.ideGutter` 54、`.ideTree` 230、`.ideQuick` 560×420、`.hostKeyBox` 560、`.panel.small` 720×560、`.previewPane` 420 / `.taskPanel` 440 与它们的 72/40/16 定位值。这些是单点布局常量，先沉淀到"组件级令牌"再替换更安全。
2. **非 4 梯度的间距**：5 / 7 / 9 / 10 / 14 / 18 / 20（`gap:5px`、`padding:5px 10px`、`gap:10px` 等）。本轮不硬凑到 spacing 梯度，避免下一轮替换时顺带改变视觉；如需统一，建议在第三阶段单独做一次"间距收敛"。
3. **阴影**：`0 6px 18px rgba(...)`、`0 12px 40px rgba(...)`。属于 elevation，不是密度，建议下一轮建 `elevation-1/2` 令牌（含模糊半径与透明度）。
4. **遗留颜色字面量**：`app.css` 仍有 `.boardCol` `#E9E9E9`、`.boardItem` `#E4E4E4`、`.ideGutter` `#EFEFEF`、`.ideFind` `#E9E9E9`、`.connWarn` `#F0D39A`、`.taskConflict` `#E0C878` 等未走令牌（上一轮范围内未覆盖）。本轮不能改 `app.css`，下一轮清理时统一替换为 `--tte-border` / `--tte-warning`。
5. **字号 10 / 9**：`.cmpBadge` 10px、`.taskTag` 10px、`.ideNode .arrow` 9px 保留，避免缩小到看不见。

## 8. 验证记录

| 项 | 结果 |
|---|---|
| 生成脚本 | 跑通（多次），输出「26 个颜色 × 2 套主题，63 个尺寸」 |
| 幂等性 | 重跑后 `WebPageHtml.cs` `git diff --stat` 为空（颜色块与上次完全一致） |
| JSON 校验 | 解析通过；颜色 26、尺寸 63；每项含 `type/value/avalonia`，`thickness` 必须为两个数字 |
| Axaml 产物 | 52 `SolidColorBrush`、52 `sys:Double`、7 `Thickness`、4 `CornerRadius`；无 XML 结构错误 |
| CSS 产物 | 89 个 `--tte-*` 定义；`:root` + `body.dark` 两个块，花括号配平 |
| 变量闭包 | `app.css` 引用 25 个变量，**0 个未定义**；63 个尺寸变量当前未被引用（本轮不替换，符合预期） |
| 构建 | `dotnet build src\TabTaskExplorer.App` → **0 错误**，192 警告；警告类型为 CS8600/CS8604/CS0618/AVLN5001/AVLN3001/NU1510 等，无一条指向 `Tokens.axaml` |
| 网页运行态 | **未验证**：`app.css` 本轮未消费尺寸变量，另一会话正在改网页结构，启动应用会与其构建/端口冲突，故未启动页面与控制台检查 |
| 桌面运行态 | **未验证**：桌面 AXAML 本轮未引用尺寸资源，且无桌面截图能力；`sys:Double` 资源在编译期已被 XAML 编译器解析（构建通过），但运行时绑定效果未做像素确认 |

## 9. 下一轮建议顺序

1. 先替换网页端"外壳层"：`.titlebar` / `.tab` / `.bookmarkbar` / `.toolbar` / `.navrow` / `.statusbar`，确认六条栏高变化（26→28、28→30、28→36 等）后截图对齐桌面。
2. 再替换列表密度：`th` / `td` / `tr.row` / `.tile`，行高 28 / 紧凑 22 与 `row-padding` 同时生效，避免只改一半导致行高跳动。
3. 然后替换圆角与描边、字号梯度（`font-size` 只有 4 档，可一次性完成）。
4. 最后处理 `.panel` 尺寸（1000×760 → 900×640）与信息窗格宽度（240 → 220），这两项会改变视觉占比，单独一轮便于对照。
5. 桌面端按 §6 清单替换，与网页同轮验证。
