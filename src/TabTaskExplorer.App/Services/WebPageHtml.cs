namespace TabTaskExplorer.App.Services;

/// <summary>
/// 网页端页面：单文件内嵌，无外部依赖。界面结构与桌面主窗口一致
/// （标签条、书签栏、工具栏、导航地址栏、左侧导航、内容区、状态栏），
/// 行为对齐桌面（排序、搜索、多选、右键菜单、快捷键、视图切换、书签与最近访问），
/// 并按端口权限方案隐藏无权操作的按钮。
/// </summary>
public static class WebPageHtml
{
    /// <summary>页面内容；__PORT__ 由服务在响应时替换为实际端口。</summary>
    public const string Content = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>TabTaskExplorer 网页端</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font:13px 'Segoe UI','Microsoft YaHei',sans-serif; color:#1C1C1C; background:#FFFFFF; height:100vh; display:flex; flex-direction:column; }
  .hidden { display:none !important; }
  .spacer { margin-left:auto; }
  .ico { font-family:'Segoe MDL2 Assets','Segoe Fluent Icons'; font-size:13px; }

  /* ===== 标题栏（与桌面 TabX 标题栏同构） ===== */
  .titlebar { height:36px; background:#F1F1F1; border-bottom:1px solid #D9D9D9; display:flex; align-items:center; padding:0 8px; gap:6px; flex:none; }
  .logo { width:20px; height:20px; border-radius:3px; background:#E8A317; color:#fff; font-size:11px; line-height:20px; text-align:center; font-weight:bold; cursor:pointer; }
  .tabs { display:flex; gap:2px; overflow:hidden; max-width:52%; }
  .tab { height:26px; line-height:26px; padding:0 10px; border-radius:4px 4px 0 0; cursor:pointer; border-bottom:2px solid transparent; white-space:nowrap; display:flex; align-items:center; gap:6px; }
  .tab:hover { background:#E4E4E4; }
  .tab.active { background:#FFFFFF; border-bottom:2px solid #4A8FD0; }
  .tab .x { margin-left:2px; color:#999; padding:0 3px; }
  .tab .x:hover { color:#E81123; }
  .meta { color:#808080; font-size:12px; }
  .modeBtn, .iconBtn { padding:2px 9px; border:1px solid #B0B0B0; border-radius:4px; background:transparent; color:inherit; cursor:pointer; font:12px 'Segoe UI','Microsoft YaHei'; }
  .modeBtn:hover, .iconBtn:hover { border-color:#4A8FD0; color:#4A8FD0; }
  .iconBtn { width:26px; height:24px; padding:0; font-family:'Segoe MDL2 Assets','Segoe Fluent Icons'; font-size:12px; }

  /* ===== 书签栏（与桌面快捷访问固定项同构） ===== */
  .bookmarkbar { display:flex; align-items:center; gap:4px; padding:2px 8px; border-bottom:1px solid #E9E9E9; flex:none; min-height:28px; overflow:hidden; }
  .bmItem { display:flex; align-items:center; gap:5px; padding:2px 7px; border-radius:4px; cursor:pointer; font-size:12px; position:relative; white-space:nowrap; }
  .bmItem:hover { background:#F5F7FA; }
  .bmItem .bmIcon { color:#E8A317; }
  .bmItem .bmX { color:#999; padding:0 2px; border-radius:3px; }
  .bmItem .bmX:hover { background:#FDE7E9; color:#E81123; }
  .bmHint { color:#808080; font-size:12px; }

  /* ===== 工具栏（与桌面工具栏按钮组同构） ===== */
  .toolbar { display:flex; gap:2px; padding:3px 8px; border-bottom:1px solid #E9E9E9; flex-wrap:wrap; flex:none; align-items:center; }
  .btn { padding:3px 9px; border-radius:4px; cursor:pointer; user-select:none; white-space:nowrap; }
  .btn:hover { background:#F5F7FA; }
  .btn.disabled { color:#B0B0B0; cursor:not-allowed; }
  .btn.disabled:hover { background:transparent; }
  .btn .tick { display:inline-block; width:12px; color:#2F6FD0; }
  .dd { position:relative; display:inline-flex; }
  .menu { position:absolute; top:100%; left:0; margin-top:3px; background:#fff; border:1px solid #DCDCDC; border-radius:6px; box-shadow:0 6px 18px rgba(0,0,0,.14); padding:4px; z-index:30; min-width:180px; }
  .menu .item { padding:5px 10px; border-radius:4px; cursor:pointer; white-space:nowrap; display:flex; align-items:center; gap:4px; font-size:12px; }
  .menu .item:hover { background:#F0F6FF; }
  .menu .sep { height:1px; background:#E9E9E9; margin:4px 2px; }

  /* ===== 导航与地址栏（后退 / 前进 / 上一级 + 可编辑路径 + 最近 + 刷新 + 收藏 + 搜索） ===== */
  .navrow { display:flex; align-items:center; gap:6px; padding:4px 8px; border-bottom:1px solid #E9E9E9; flex:none; }
  .navbtns { display:flex; gap:2px; }
  .tbtn { width:28px; height:26px; border:0; background:transparent; border-radius:4px; cursor:pointer; font-family:'Segoe MDL2 Assets','Segoe Fluent Icons'; font-size:13px; color:inherit; }
  .tbtn:hover { background:#F5F7FA; }
  .tbtn.disabled { color:#B0B0B0; cursor:not-allowed; }
  .tbtn.disabled:hover { background:transparent; }
  .addr { position:relative; flex:1; display:flex; align-items:center; border:1px solid #D5D5D5; border-radius:3px; background:#fff; min-height:28px; padding:0 2px; }
  .crumb { flex:1; display:flex; gap:2px; align-items:center; flex-wrap:nowrap; overflow:hidden; padding:2px 4px; cursor:text; min-height:24px; }
  .seg { padding:2px 6px; border-radius:4px; cursor:pointer; white-space:nowrap; }
  .seg:hover { background:#F0F6FF; }
  .sep { color:#A0A0A0; padding:0 1px; }
  .addrInput { flex:1; border:0; outline:none; padding:4px 6px; font:13px 'Segoe UI','Microsoft YaHei'; background:transparent; color:inherit; }
  .addrBtns { display:flex; }
  .searchBox { width:200px; border:1px solid #D5D5D5; border-radius:3px; padding:5px 8px; font:13px 'Segoe UI','Microsoft YaHei'; outline:none; background:#fff; color:inherit; }
  .searchBox:focus, .addrInput:focus { border-color:#4A8FD0; }
  .pop { position:absolute; top:100%; right:0; margin-top:3px; background:#fff; border:1px solid #DCDCDC; border-radius:6px; box-shadow:0 6px 18px rgba(0,0,0,.14); padding:4px; z-index:30; min-width:360px; max-height:320px; overflow:auto; }
  .pop .item { padding:5px 8px; border-radius:4px; cursor:pointer; font-size:12px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .pop .item:hover { background:#F0F6FF; }
  .pop .empty { padding:6px 8px; color:#808080; font-size:12px; }

  /* ===== 工作区：左侧导航 + 内容 + 详细信息窗格 ===== */
  .workarea { flex:1; display:flex; min-height:0; }
  .sidebar { width:200px; flex:none; border-right:1px solid #E9E9E9; overflow:auto; padding:6px 4px; }
  .navsec { display:flex; align-items:center; gap:6px; margin:8px 6px 4px; color:#2F6FD0; font-weight:500; font-size:12px; }
  .navitem { display:flex; align-items:center; gap:7px; padding:4px 8px; border-radius:4px; cursor:pointer; font-size:12px; }
  .navitem:hover { background:#F5F7FA; }
  .navitem.selected { background:#E8F1FC; color:#1B5FA8; }
  .navitem .ico { color:#E8A317; }
  .content { flex:1; overflow:auto; padding:6px 10px; }
  .infoPane { width:240px; flex:none; border-left:1px solid #E9E9E9; overflow:auto; padding:8px 10px; font-size:12px; }
  .infoPane h4 { font-size:12px; margin-bottom:6px; color:#808080; font-weight:normal; }
  .infoBlock { border-bottom:1px solid #F0F0F0; padding:5px 0; }
  .infoBlock b { display:block; font-weight:600; margin-bottom:2px; word-break:break-all; }
  .infoLine { color:#808080; word-break:break-all; }

  /* ===== 列表与图标视图 ===== */
  table { border-collapse:collapse; width:100%; }
  th { text-align:left; font-weight:normal; color:#808080; border-bottom:1px solid #E0E0E0; padding:5px 10px; position:sticky; top:0; background:#fff; z-index:2; }
  th.sortable { cursor:pointer; }
  th.sortable:hover { background:#F5F7FA; }
  th .arrow { color:#2F6FD0; font-size:10px; margin-left:3px; }
  td { padding:4px 10px; border-bottom:1px solid #F2F2F2; }
  tr.row { cursor:default; }
  tr.row:hover { background:#F5F7FA; }
  tr.row.checked { background:#E8F1FC; }
  tr.row.focus { outline:1px solid #4A8FD0; outline-offset:-1px; }
  td.name { display:flex; align-items:center; gap:7px; }
  td.name .text { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .dir { color:#2F6FD0; cursor:pointer; }
  .file { color:#1C1C1C; cursor:pointer; }
  .tiles { display:flex; flex-wrap:wrap; gap:6px; align-content:flex-start; }
  .tile { border:1px solid transparent; border-radius:5px; padding:8px 4px 6px; text-align:center; cursor:default; position:relative; overflow:hidden; }
  .tile:hover { background:#F5F7FA; }
  .tile.checked { background:#E8F1FC; border-color:#BBD7F5; }
  .tile.focus { outline:1px solid #4A8FD0; }
  .tile .ticon { font-family:'Segoe MDL2 Assets','Segoe Fluent Icons'; font-size:32px; line-height:1.1; }
  .tile .tname { font-size:12px; margin-top:4px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .tile .ttype { font-size:11px; color:#808080; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .tile .ttick { position:absolute; left:4px; top:4px; }
  .hint { color:#808080; margin:6px 0; }

  /* ===== 任务看板 / 连接管理 ===== */
  .board { display:flex; gap:10px; align-items:flex-start; }
  .boardCol { flex:1; background:#F7F8FA; border:1px solid #E9E9E9; border-radius:6px; padding:8px; }
  .boardCol h4 { font-size:12px; margin-bottom:6px; color:#606060; }
  .boardItem { background:#fff; border:1px solid #E4E4E4; border-radius:4px; padding:6px 8px; margin-bottom:6px; font-size:12px; }
  .connRow td { cursor:pointer; }
  .connRow.selected { background:#E8F1FC; }

  /* ===== 状态栏 ===== */
  .statusbar { border-top:1px solid #E9E9E9; padding:5px 12px; color:#808080; font-size:12px; flex:none; display:flex; gap:8px; }
  .flash { color:#2F6FD0; }

  /* ===== 弹窗：只读编辑器 / 批量改名 / 右键菜单 / 诊断 ===== */
  .modal { position:fixed; inset:0; background:rgba(0,0,0,.45); display:flex; align-items:center; justify-content:center; z-index:20; }
  .panel { background:#fff; width:min(1000px,92vw); height:min(760px,88vh); border-radius:8px; display:flex; flex-direction:column; overflow:hidden; }
  .panel.small { width:min(720px,92vw); height:min(560px,84vh); }
  .panelHead { height:40px; display:flex; align-items:center; gap:8px; padding:0 12px; border-bottom:1px solid #E9E9E9; }
  .panelTitle { font-weight:bold; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; flex:1; }
  .panelFoot { height:44px; display:flex; align-items:center; gap:8px; padding:0 12px; border-bottom:0; border-top:1px solid #E9E9E9; }
  textarea { flex:1; width:100%; border:0; outline:none; resize:none; padding:10px 12px; font:13px Consolas,'Cascadia Code',monospace; line-height:1.55; tab-size:4; background:#FAFAFA; color:#1C1C1C; }
  .batchList { flex:1; overflow:auto; padding:8px 12px; }
  .brow { display:flex; align-items:center; gap:8px; padding:3px 0; }
  .brow .old { width:44%; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; color:#606060; }
  .brow input { flex:1; border:1px solid #D5D5D5; border-radius:3px; padding:4px 6px; font:13px 'Segoe UI','Microsoft YaHei'; outline:none; color:inherit; background:#fff; }
  .brow input:focus { border-color:#4A8FD0; }
  .ctx { position:fixed; background:#fff; border:1px solid #DCDCDC; border-radius:6px; box-shadow:0 6px 18px rgba(0,0,0,.16); padding:4px; z-index:40; min-width:170px; }
  .ctx .item { padding:5px 12px; border-radius:4px; cursor:pointer; font-size:12px; white-space:nowrap; }
  .ctx .item:hover { background:#F0F6FF; }
  .diagBox { flex:none; padding:4px 12px; border-top:1px solid #E9E9E9; font-size:12px; }
  .diagBox summary { cursor:pointer; color:#808080; }
  pre.diag { background:#11151B; color:#9CDCFE; font:11px Consolas,monospace; padding:8px; border-radius:6px; max-height:240px; overflow:auto; white-space:pre-wrap; margin-top:6px; }

  /* ===== IDE 模式（与桌面 IdeShellView 同构：顶部操作 + 资源树 + 编辑区 + 查找面板 + 状态栏） ===== */
  .ideView { flex:1; display:flex; flex-direction:column; min-height:0; position:relative; }
  .ideTop { display:flex; align-items:center; gap:4px; padding:4px 8px; border-bottom:1px solid #E9E9E9; flex:none; }
  .ideRoot { flex:1; font-size:12px; color:#606060; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .ideBody { flex:1; display:flex; min-height:0; }
  .ideTree { width:230px; flex:none; border-right:1px solid #E9E9E9; display:flex; flex-direction:column; min-height:0; }
  .ideTreeHead { font-size:11px; color:#808080; padding:6px 8px 2px; flex:none; }
  .ideTreeList { flex:1; overflow:auto; padding:0 4px 8px; }
  .ideNode { display:flex; align-items:center; gap:5px; padding:2px 4px; border-radius:3px; font-size:12px; white-space:nowrap; user-select:none; cursor:default; }
  .ideNode:hover { background:#F5F7FA; }
  .ideNode.sel { background:#E8F1FC; }
  .ideNode .arrow { width:10px; flex:none; color:#808080; font-size:9px; text-align:center; }
  .ideNode .nm { overflow:hidden; text-overflow:ellipsis; }
  .ideSplit { width:1px; background:#E9E9E9; flex:none; }
  .ideEditor { flex:1; display:flex; flex-direction:column; min-width:0; min-height:0; }
  .ideTabs { display:flex; align-items:center; gap:2px; padding:3px 4px 0; border-bottom:1px solid #E9E9E9; overflow-x:auto; flex:none; min-height:30px; }
  .ideTab { display:flex; align-items:center; gap:5px; padding:4px 6px 4px 8px; border:1px solid transparent; border-bottom:0; border-radius:4px 4px 0 0; font-size:12px; white-space:nowrap; cursor:default; }
  .ideTab:hover { background:#F5F7FA; }
  .ideTab.active { background:#FFFFFF; border-color:#E9E9E9; }
  .ideTab .x { color:#999; padding:0 3px; border-radius:3px; }
  .ideTab .x:hover { background:#FDE7E9; color:#E81123; }
  .ideMain { flex:1; display:flex; min-height:0; position:relative; }
  .ideGutter { flex:none; width:54px; overflow:hidden; padding:6px 8px 6px 0; text-align:right; background:#F7F7F7; border-right:1px solid #EFEFEF; color:#9A9A9A; font:13px/1.55 Consolas,'Cascadia Code',monospace; white-space:pre; user-select:none; }
  .ideText { flex:1; min-width:0; border:0; outline:none; resize:none; padding:6px 8px; font:13px/1.55 Consolas,'Cascadia Code',monospace; background:#FFFFFF; color:#1C1C1C; white-space:pre; overflow:auto; tab-size:4; }
  .ideEmpty { position:absolute; inset:0; display:flex; align-items:center; justify-content:center; color:#9A9A9A; font-size:12px; pointer-events:none; text-align:center; padding:0 20px; }
  .ideFind { flex:none; border-top:1px solid #E9E9E9; background:#F7F7F7; max-height:240px; display:flex; flex-direction:column; }
  .ideFindRow { display:flex; align-items:center; gap:6px; padding:6px 8px; flex-wrap:wrap; }
  .ideFindRow .lbl { font-size:12px; color:#808080; }
  .ideFindRow input { border:1px solid #D5D5D5; border-radius:3px; padding:4px 6px; font:12px 'Segoe UI','Microsoft YaHei'; width:170px; outline:none; background:#fff; color:inherit; }
  .ideFindRow input:focus { border-color:#4A8FD0; }
  .ideFindStatus { font-size:11px; color:#808080; padding:0 10px 4px; }
  .ideFindList { overflow:auto; padding:0 6px 6px; flex:1; min-height:0; }
  .ideFRes { display:flex; gap:10px; padding:3px 6px; border-radius:3px; font-size:12px; cursor:default; }
  .ideFRes:hover { background:#F0F6FF; }
  .ideFRes .ln { color:#808080; font-size:11px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; flex:1; }
  .ideStatus { border-top:1px solid #E9E9E9; padding:4px 12px; color:#808080; font-size:12px; flex:none; display:flex; gap:8px; align-items:center; }
  .ideStatus .loc { max-width:360px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .ideQuick { position:absolute; top:70px; left:50%; transform:translateX(-50%); width:560px; max-width:92vw; background:#fff; border:1px solid #DCDCDC; border-radius:6px; box-shadow:0 8px 24px rgba(0,0,0,.18); padding:10px; z-index:50; display:flex; flex-direction:column; max-height:420px; }
  .ideQuick input { border:1px solid #D5D5D5; border-radius:3px; padding:6px 8px; font:12px 'Segoe UI','Microsoft YaHei'; outline:none; background:#fff; color:inherit; }
  .ideQuick input:focus { border-color:#4A8FD0; }
  .ideQuickList { margin-top:8px; overflow:auto; }
  .ideQItem { display:flex; gap:10px; padding:4px 6px; border-radius:4px; font-size:12px; cursor:default; }
  .ideQItem .dir { flex:1; text-align:right; color:#808080; font-size:11px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; max-width:300px; }
  .ideQItem:hover, .ideQItem.sel { background:#F0F6FF; }
  .iconBtn.active { border-color:#4A8FD0; color:#4A8FD0; }
  .idePromptBody { padding:14px 12px; font-size:13px; word-break:break-all; }

  /* ===== 深色主题（与桌面深色配色一致） ===== */
  body.dark { background:#191919; color:#E8E8E8; }
  body.dark .titlebar { background:#212121; border-bottom-color:#323232; }
  body.dark .tab:hover { background:#2E2E2E; }
  body.dark .tab.active { background:#191919; }
  body.dark .bookmarkbar, body.dark .toolbar, body.dark .navrow, body.dark .statusbar, body.dark .diagBox { border-color:#323232; }
  body.dark .bmItem:hover, body.dark .btn:hover, body.dark .tbtn:hover, body.dark .seg:hover, body.dark .navitem:hover, body.dark .tile:hover { background:#2E2E2E; }
  body.dark .menu, body.dark .pop, body.dark .ctx { background:#242424; border-color:#3C3C3C; }
  body.dark .menu .item:hover, body.dark .pop .item:hover, body.dark .ctx .item:hover { background:#2E2E2E; }
  body.dark .menu .sep { background:#3C3C3C; }
  body.dark .sidebar, body.dark .infoPane { border-color:#323232; }
  body.dark .navitem.selected, body.dark tr.row.checked, body.dark .tile.checked { background:#1F3B57; }
  body.dark th { color:#8A8A8A; border-bottom-color:#3C3C3C; background:#191919; }
  body.dark th.sortable:hover { background:#2E2E2E; }
  body.dark td { border-bottom-color:#262626; }
  body.dark tr.row:hover { background:#2E2E2E; }
  body.dark .dir { color:#5AA2F5; }
  body.dark .file { color:#E8E8E8; }
  body.dark .addr, body.dark .searchBox, body.dark .brow input { background:#202020; border-color:#3C3C3C; }
  body.dark .tbtn.disabled, body.dark .btn.disabled { color:#5A5A5A; }
  body.dark .panel { background:#202020; }
  body.dark .panelHead, body.dark .panelFoot { border-color:#323232; }
  body.dark textarea { background:#1B1B1B; color:#E8E8E8; }
  body.dark .boardCol { background:#202020; border-color:#323232; }
  body.dark .boardItem { background:#242424; border-color:#3C3C3C; }
  body.dark .infoBlock { border-color:#262626; }
  body.dark .ideTop, body.dark .ideTabs, body.dark .ideStatus { border-color:#323232; }
  body.dark .ideTree, body.dark .ideSplit, body.dark .ideFind { border-color:#323232; background:#1D1D1D; }
  body.dark .ideNode:hover { background:#2E2E2E; }
  body.dark .ideNode.sel { background:#1F3B57; }
  body.dark .ideTab:hover { background:#2E2E2E; }
  body.dark .ideTab.active { background:#191919; }
  body.dark .ideGutter { background:#212121; border-right-color:#323232; color:#7A7A7A; }
  body.dark .ideText { background:#1B1B1B; color:#E8E8E8; }
  body.dark .ideFindRow input, body.dark .ideQuick, body.dark .ideQuick input { background:#202020; border-color:#3C3C3C; }
  body.dark .ideQuick { background:#242424; }
  body.dark .ideFRes:hover, body.dark .ideQItem:hover { background:#2E2E2E; }
  body.dark .ideQItem.sel { background:#1F3B57; }
  body.dark .ideFRes .ln, body.dark .ideQItem .dir { color:#8A8A8A; }
  body.dark .idePromptBody { color:#E8E8E8; }
</style>
</head>
<body>
  <!-- 标题栏：徽标 + 标签条 + 新建标签 + 任务看板 + 主题 + 模式切换 -->
  <div class="titlebar">
    <div class="logo" title="此电脑" onclick="openPathAt(0)">T</div>
    <div class="tabs" id="tabs"></div>
    <span class="iconBtn" title="新建标签" onclick="newTab()">+</span>
    <span class="iconBtn" title="任务看板" onclick="clickBtn(null,'openboard')">&#xE8F4;</span>
    <span class="iconBtn" id="ideIconBtn" title="IDE 模式 / 资源管理器模式" onclick="toggleIdeMode()">&#xE943;</span>
    <div class="spacer"></div>
    <span class="iconBtn" title="切换深浅色主题" onclick="toggleTheme()">&#xE706;</span>
    <button class="modeBtn" id="modeBtn" title="默认使用独立新页面" onclick="toggleMode()">当前网页内容：独立新页面</button>
  </div>

  <!-- 书签栏：桌面快捷访问固定项 -->
  <div class="bookmarkbar" id="bookmarkbar"></div>

  <!-- 工具栏：与桌面工具栏同款按钮组 -->
  <div class="toolbar" id="toolbar">
    <span class="btn" id="btnIde" onclick="toggleIdeMode()" title="切换到 IDE 模式"><span class="ico">&#xE943;</span> IDE 模式</span>
    <div class="dd">
      <span class="btn" onclick="toggleMenu('viewMenu')">查看</span>
      <div class="menu hidden" id="viewMenu">
        <div class="item" data-mode="ExtraLargeIcons" onclick="pickView('ExtraLargeIcons')"><span class="tick"></span>超大图标</div>
        <div class="item" data-mode="LargeIcons" onclick="pickView('LargeIcons')"><span class="tick"></span>大图标</div>
        <div class="item" data-mode="MediumIcons" onclick="pickView('MediumIcons')"><span class="tick"></span>中图标</div>
        <div class="item" data-mode="SmallIcons" onclick="pickView('SmallIcons')"><span class="tick"></span>小图标</div>
        <div class="item" data-mode="List" onclick="pickView('List')"><span class="tick"></span>列表</div>
        <div class="item" data-mode="Details" onclick="pickView('Details')"><span class="tick"></span>详细信息</div>
        <div class="item" data-mode="Tile" onclick="pickView('Tile')"><span class="tick"></span>平铺</div>
        <div class="item" data-mode="Content" onclick="pickView('Content')"><span class="tick"></span>内容</div>
        <div class="sep"></div>
        <div class="item" id="itemExt" onclick="toggleExtensions()"><span class="tick" id="tickExt"></span>显示扩展名</div>
      </div>
    </div>
    <div class="dd">
      <span class="btn" onclick="toggleMenu('copyMenu')">复制</span>
      <div class="menu hidden" id="copyMenu">
        <div class="item" onclick="copyChecked()">复制</div>
        <div class="item" onclick="copyText('dir')">复制文件所在路径</div>
        <div class="item" onclick="copyText('full')">复制文件完整路径</div>
        <div class="item" onclick="copyText('name')">复制文件名称</div>
        <div class="item" onclick="copyText('nameext')">复制文件名称（带扩展名）</div>
      </div>
    </div>
    <span class="btn" id="btnCut" onclick="clickBtn('btnCut','cut')">剪切</span>
    <span class="btn" id="btnPaste" onclick="clickBtn('btnPaste','paste')">粘贴</span>
    <span class="btn" id="btnFolder" onclick="createFolder()">新建文件夹</span>
    <span class="btn" id="btnBatch" onclick="openBatch()">批量改名</span>
    <span class="btn" id="btnRecycle" onclick="deleteChecked()">回收站</span>
    <span class="btn" id="btnHidden" onclick="clickBtn('btnHidden','togglehidden')"><span class="tick" id="tickHiddenBtn"></span>显示隐藏条目</span>
    <span class="btn" id="btnInfoPane" onclick="toggleInfoPane()"><span class="tick" id="tickInfoBtn"></span>显示详细信息窗格</span>
    <span class="btn" id="btnRefresh" onclick="clickBtn('btnRefresh','refresh')">刷新</span>
  </div>

  <!-- 导航与地址栏：后退 / 前进 / 上一级 + 可编辑路径 + 最近访问 + 刷新 + 收藏 + 搜索 -->
  <div class="navrow" id="navrow">
    <div class="navbtns">
      <button class="tbtn" id="btnBack" title="后退" onclick="clickBtn('btnBack','back')">&#xE72B;</button>
      <button class="tbtn" id="btnFwd" title="前进" onclick="clickBtn('btnFwd','forward')">&#xE72A;</button>
      <button class="tbtn" id="btnUp" title="上一级" onclick="goUp()">&#xE74A;</button>
    </div>
    <div class="addr" id="addr">
      <div class="crumb" id="crumb" title="点击空白处编辑路径"></div>
      <input class="addrInput hidden" id="addrInput" placeholder="输入路径后按回车跳转">
      <div class="addrBtns">
        <button class="tbtn" id="recentBtn" title="最近访问" onclick="toggleRecent()">&#xE81C;</button>
        <button class="tbtn" title="刷新 (F5)" onclick="clickBtn(null,'refresh')">&#xE72C;</button>
        <button class="tbtn" id="btnPin" title="收藏当前文件夹到书签" onclick="clickBtn('btnPin','pin')">&#xE735;</button>
      </div>
      <div class="pop hidden" id="recentPop"></div>
    </div>
    <input class="searchBox" id="searchBox" placeholder="搜索当前目录">
  </div>

  <!-- 工作区：左侧导航 + 内容区 + 详细信息窗格 -->
  <div class="workarea" id="workarea">
    <div class="sidebar" id="sidebar"></div>
    <div class="content" id="content">
      <div id="contentMain"><div class="hint">正在加载……</div></div>
      <!-- 瞬时提示：与桌面摘要栏同位（内容区底部），不占用状态栏 -->
      <div class="hint flash hidden" id="flashLine"></div>
    </div>
    <div class="infoPane hidden" id="infoPane"></div>
  </div>

  <!-- 状态栏：项目数 / 勾选数 + 服务地址 -->
  <div class="statusbar" id="statusbar">
    <span id="statusLeft">准备就绪</span>
    <span class="spacer"></span>
    <span id="statusRight"></span>
  </div>

  <!-- ===== IDE 模式视图（与桌面 IdeShellView 同构） ===== -->
  <div class="ideView hidden" id="ideView">
    <!-- 顶部：工作区路径 + 保存 / 全部保存 / 查找 / 快速打开 / 刷新 -->
    <div class="ideTop">
      <span class="ico" style="color:#E8A317">&#xE8B7;</span>
      <span class="ideRoot" id="ideRoot" title=""></span>
      <span class="btn" id="ideBtnSave" title="保存 (Ctrl+S)" onclick="clickIdeSave('ideBtnSave','idesave')"><span class="ico" style="color:#808080">&#xE74E;</span> 保存</span>
      <span class="btn" id="ideBtnSaveAll" title="全部保存 (Ctrl+Shift+S)" onclick="clickIdeSave('ideBtnSaveAll','idesaveall')"><span class="ico" style="color:#808080">&#xE8F4;</span> 全部保存</span>
      <span class="btn" id="ideBtnFind" title="查找 (Ctrl+F)" onclick="ideOpenFind()"><span class="ico" style="color:#808080">&#xE721;</span> 查找</span>
      <span class="btn" id="ideBtnQuick" title="快速打开 (Ctrl+P)" onclick="clickIdeBtn('ideBtnQuick','idequickopen')"><span class="ico" style="color:#808080">&#xE71E;</span> 快速打开</span>
      <span class="btn" id="ideBtnRefresh" title="刷新工作区" onclick="clickIdeBtn('ideBtnRefresh','iderefresh')"><span class="ico" style="color:#808080">&#xE72C;</span> 刷新</span>
    </div>
    <!-- 中部：资源树 + 分隔条 + 编辑区 -->
    <div class="ideBody">
      <div class="ideTree">
        <div class="ideTreeHead">资源管理器</div>
        <div class="ideTreeList" id="ideTreeList"></div>
      </div>
      <div class="ideSplit"></div>
      <div class="ideEditor">
        <div class="ideTabs" id="ideTabs"></div>
        <div class="ideMain">
          <div class="ideGutter" id="ideGutter">1</div>
          <textarea id="ideText" spellcheck="false" class="ideText hidden"></textarea>
          <div class="ideEmpty" id="ideEmpty">从左侧资源树双击文件即可开始编辑，Ctrl+P 快速打开，Ctrl+F 查找。</div>
        </div>
        <div class="ideFind hidden" id="ideFind">
          <div class="ideFindRow">
            <span class="lbl">查找</span>
            <input id="ideFindBox" placeholder="查找内容">
            <input id="ideReplaceBox" placeholder="替换为">
            <span class="btn" onclick="ideFindNext('0')">上一个</span>
            <span class="btn" onclick="ideFindNext('1')">下一个</span>
            <span class="btn" id="ideBtnReplace" onclick="clickIdeBtn('ideBtnReplace','idereplace')">替换</span>
            <span class="btn" id="ideBtnReplaceAll" onclick="clickIdeBtn('ideBtnReplaceAll','idereplaceall')">全部替换</span>
            <span class="btn" onclick="clickIdeBtn(null,'idesearchws')">在工作区搜索</span>
          </div>
          <div class="ideFindStatus" id="ideFindStatus"></div>
          <div class="ideFindList" id="ideFindList"></div>
        </div>
      </div>
    </div>
    <!-- 瞬时提示：IDE 模式下的操作反馈（与资源管理器模式内容区底部提示同构） -->
    <div class="hint flash hidden" id="ideFlashLine" style="padding:2px 10px;flex:none"></div>

    <!-- 底部：IDE 状态栏 -->
    <div class="ideStatus">
      <span id="ideSolution"></span>
      <span class="spacer"></span>
      <span class="loc" id="ideLocation"></span>
      <span id="ideCaret"></span>
      <span id="ideEncoding"></span>
    </div>
    <!-- 快速打开浮层（Ctrl+P） -->
    <div class="ideQuick hidden" id="ideQuick">
      <input id="ideQuickInput" placeholder="输入文件名（支持模糊匹配），回车打开，Esc 关闭">
      <div class="ideQuickList" id="ideQuickList"></div>
    </div>
  </div>

  <!-- 关闭脏编辑器的三分支确认（保存 / 不保存 / 取消） -->
  <div class="modal hidden" id="idePrompt">
    <div class="panel small" style="height:auto">
      <div class="panelHead">
        <div class="panelTitle" id="idePromptTitle">保存更改</div>
      </div>
      <div class="idePromptBody" id="idePromptBody"></div>
      <div class="panelFoot">
        <span class="btn" onclick="idePromptChoice('save')">保存</span>
        <span class="btn" onclick="idePromptChoice('dont')">不保存</span>
        <div class="spacer"></div>
        <span class="btn" onclick="idePromptChoice('cancel')">取消</span>
      </div>
    </div>
  </div>

  <!-- 诊断信息：网页端调试折叠项（桌面端无此视图，默认收起，不占用内容区） -->
  <details class="diagBox">
    <summary>诊断信息（UI 线程未处理异常，最多保留 100 条）</summary>
    <pre class="diag" id="diag">（暂无）</pre>
  </details>

  <!-- 只读 / 可写文件编辑器 -->
  <div class="modal hidden" id="editor">
    <div class="panel">
      <div class="panelHead">
        <div class="panelTitle" id="editorTitle"></div>
        <span class="btn" onclick="closeEditor()">关闭</span>
      </div>
      <textarea id="editorText" spellcheck="false"></textarea>
      <div class="panelFoot">
        <span class="hint" id="editorHint"></span>
        <div class="spacer"></div>
        <span class="btn" id="editorSave" onclick="saveFile()">保存修改</span>
      </div>
    </div>
  </div>

  <!-- 批量改名：对勾选条目逐行输入新名称 -->
  <div class="modal hidden" id="batch">
    <div class="panel small">
      <div class="panelHead">
        <div class="panelTitle">批量改名（共 <span id="batchCount">0</span> 项）</div>
        <span class="btn" onclick="closeBatch()">关闭</span>
      </div>
      <div class="batchList" id="batchList"></div>
      <div class="panelFoot">
        <span class="hint">保持原名的行不会改名</span>
        <div class="spacer"></div>
        <span class="btn" onclick="applyBatch()">应用改名</span>
      </div>
    </div>
  </div>

  <!-- 行右键菜单（与桌面列表右键菜单同构） -->
  <div class="ctx hidden" id="ctx"></div>

  <div class="main" style="display:none"></div>

<script>
const PORT_LABEL = '127.0.0.1:__PORT__';
const VIEW_MODES = ['ExtraLargeIcons', 'LargeIcons', 'MediumIcons', 'SmallIcons', 'List', 'Details', 'Tile', 'Content'];
const ICON_MODES = ['ExtraLargeIcons', 'LargeIcons', 'MediumIcons', 'SmallIcons', 'Tile'];

let workspaceId = '';
let permissions = { canModify: false, canCreateDelete: false, profile: '' };
let state = null;
let currentRows = [];
let currentTab = null;
let checked = new Set();
let focusIndex = -1;
let anchorIndex = -1;
let crumbPaths = [];
let currentPath = '';
let editingPath = '';
let batchPaths = [];
let pollTimer = null;
let searchTimer = null;
let lastContentSignature = '';
let lastMessage = '';
let lastMessageTime = 0;

function esc(value) {
  const div = document.createElement('div');
  div.textContent = value == null ? '' : value;
  return div.innerHTML;
}

function url(path, params) {
  const query = new URLSearchParams(params || {});
  if (workspaceId) { query.set('w', workspaceId); }
  const suffix = query.toString();
  return suffix ? path + '?' + suffix : path;
}

function flash(text) {
  lastMessage = text;
  lastMessageTime = Date.now();
  renderStatus();
}

async function loadPermissions() {
  try {
    const response = await fetch('/api/permissions');
    permissions = await response.json();
    applyPermissions();
  } catch (error) { /* 权限信息不可用时按最小权限处理 */ }
}

function setGate(id, allowed) {
  const element = document.getElementById(id);
  if (element) { element.classList.toggle('disabled', !allowed); }
}

function applyPermissions() {
  setGate('btnFolder', permissions.canCreateDelete);
  setGate('btnCut', permissions.canCreateDelete);
  setGate('btnPaste', permissions.canCreateDelete);
  setGate('btnBatch', permissions.canCreateDelete);
  setGate('btnRecycle', permissions.canCreateDelete);
  setGate('btnHidden', permissions.canModify);
  setGate('btnPin', permissions.canModify);
  setGate('itemExt', permissions.canModify);
  setGate('ideBtnSave', permissions.canModify);
  setGate('ideBtnSaveAll', permissions.canModify);
  setGate('ideBtnReplace', permissions.canModify);
  setGate('ideBtnReplaceAll', permissions.canModify);
}

function closeMenus() {
  const menus = document.querySelectorAll('.menu');
  for (const menu of menus) { menu.classList.add('hidden'); }
  document.getElementById('recentPop').classList.add('hidden');
}

function toggleMenu(id) {
  const menu = document.getElementById(id);
  const willShow = menu.classList.contains('hidden');
  closeMenus();
  if (willShow) { menu.classList.remove('hidden'); }
}

function toggleRecent() {
  const pop = document.getElementById('recentPop');
  const willShow = pop.classList.contains('hidden');
  closeMenus();
  if (willShow) { renderRecent(); pop.classList.remove('hidden'); }
}

function toggleTheme() {
  const next = (state && state.theme === 'Light') ? 'Dark' : 'Light';
  fetch(url('/api/theme', { t: next }), { method: 'POST' }).then(refresh).catch(refresh);
}

// 按当前模式刷新标题栏按钮文字与提示：跟随桌面=镜像主界面，独立=独立新页面
function updateModeButton(followDesktop) {
  const button = document.getElementById('modeBtn');
  button.textContent = '当前网页内容：' + (followDesktop ? '镜像主界面' : '独立新页面');
  button.title = '默认使用独立新页面';
}

async function toggleMode() {
  if (workspaceId) {
    workspaceId = '';
    updateModeButton(true);
    await refresh();
    return;
  }
  try {
    const response = await fetch('/api/mode?m=independent', { method: 'POST' });
    const data = await response.json();
    workspaceId = data.workspaceId || '';
    updateModeButton(false);
  } catch (error) { /* 创建失败保持镜像主界面 */ }
  await refresh();
}

// 首次加载默认进入独立新页面；失败时回落镜像主界面（仅首载执行一次，手动切回后不再自动切回）
async function initMode() {
  try {
    const response = await fetch('/api/mode?m=independent', { method: 'POST' });
    const data = await response.json();
    if (data.workspaceId) {
      workspaceId = data.workspaceId;
      updateModeButton(false);
      return;
    }
  } catch (error) { /* 创建失败回落镜像主界面 */ }
  updateModeButton(true);
}

async function refresh() {
  try {
    const response = await fetch(url('/api/state'));
    if (response.status === 410) {
      workspaceId = '';
      updateModeButton(true);
      return;
    }
    if (response.status === 403 && !workspaceId) {
      // 权限方案限定了可访问目录，镜像主界面不可用：自动切到独立新页面，由路径白名单限定浏览范围
      const modeResponse = await fetch('/api/mode?m=independent', { method: 'POST' });
      const modeData = await modeResponse.json();
      workspaceId = modeData.workspaceId || '';
      updateModeButton(false);
      flash('当前权限方案限定了可访问目录，已自动切换到独立新页面');
      return;
    }
    state = await response.json();
    render();
  } catch (error) {
    flash('网页端服务连接中断，正在重试……');
  }
  try {
    const diagResponse = await fetch('/api/diagnostics');
    const items = await diagResponse.json();
    document.getElementById('diag').textContent = items.length ? items.join('\n\n────────\n\n') : '（暂无）';
  } catch (error) { /* 诊断拉取失败忽略 */ }
}

async function post(path, params) {
  try { await fetch(url(path, params), { method: 'POST' }); } catch (error) { /* 网络失败由下一次轮询反映 */ }
  await refresh();
}

async function doAction(action, argument) {
  const params = { a: action };
  if (argument !== undefined && argument !== null && argument !== '') { params.arg = argument; }
  try {
    const response = await fetch(url('/api/action', params), { method: 'POST' });
    const text = await response.text();
    if (!response.ok) {
      flash(text || ('操作失败，状态码=' + response.status));
      return;
    }
    if (text) { flash(text); }
  } catch (error) {
    flash('网页端服务连接中断，正在重试……');
    return;
  }
  await refresh();
}

function clickBtn(id, action, argument) {
  const element = id ? document.getElementById(id) : null;
  if (element && element.classList.contains('disabled')) {
    flash('该端口未放行该操作，权限方案=' + permissions.profile);
    return;
  }
  closeMenus();
  doAction(action, argument);
}

function openPath(target) { return post('/api/open', { p: target }); }
function openPathAt(index) { return post('/api/open', { p: crumbPaths[index] || '' }); }
function selectTab(index) { return post('/api/select', { i: index }); }
function closeTab(index) { return post('/api/tabs/close', { i: index }); }
function newTab() { return post('/api/tabs/new', { p: '' }); }
function goUp() { closeMenus(); return post('/api/up', {}); }
function diskNav(target) { return post('/api/disk/nav', { p: target || '' }); }

// ======== 渲染 ========

function render() {
  if (!state) { return; }
  document.body.className = (state.theme === 'Light') ? '' : 'dark';
  updateModeButton(state.followDesktop);
  renderTabs();
  applyIdeLayout(!!state.isIdeMode);
  if (state.isIdeMode) {
    // IDE 模式：隐藏资源管理器布局，渲染 IDE 外壳（与桌面 IdeShellView 同构）
    currentTab = state.tabs.find(function (tab) { return tab.selected; });
    renderIde();
    renderStatus();
    return;
  }
  renderBookmarks();
  renderSidebar();
  currentTab = state.tabs.find(function (tab) { return tab.selected; });
  if (!currentTab) {
    currentRows = [];
    checked = new Set();
    focusIndex = -1;
    document.getElementById('crumb').innerHTML = '';
    document.getElementById('contentMain').innerHTML = '<div class="hint">没有打开的标签页，点上方「+」创建一个</div>';
    lastContentSignature = '';
    renderToolbarState();
    renderInfoPane();
    renderStatus();
    return;
  }
  currentPath = currentTab.currentPath || '';
  currentRows = currentTab.rows || [];
  syncCheckedFromState();
  renderCrumb(currentTab);
  renderContent(currentTab);
  renderToolbarState();
  renderInfoPane();
  renderStatus();
}

function renderTabs() {
  let html = '';
  for (const tab of state.tabs) {
    html += '<div class="tab' + (tab.selected ? ' active' : '') + '" onclick="selectTab(' + tab.index + ')">'
      + '<span class="ico">&#xE8B7;</span><span>' + esc(tab.title) + '</span>';
    if (!state.followDesktop) {
      html += '<span class="x" onclick="event.stopPropagation();closeTab(' + tab.index + ')">&times;</span>';
    }
    html += '</div>';
  }
  document.getElementById('tabs').innerHTML = html;
}

function renderBookmarks() {
  const bar = document.getElementById('bookmarkbar');
  if (!state.navPinned || state.navPinned.length === 0) {
    bar.innerHTML = '<span class="ico" style="color:#E8A317">&#xE8B7;</span>'
      + '<span class="bmHint">可拖动常用文件 / 文档 / 文件夹到此，创建成书签，以便能快速找到并打开</span>';
    return;
  }
  let html = '';
  for (const item of state.navPinned) {
    html += '<div class="bmItem" onclick="openPath(' + JSON.stringify(item.path).replace(/"/g, '&quot;') + ')">'
      + '<span class="ico bmIcon">&#xE8B7;</span><span>' + esc(item.name) + '</span>'
      + '<span class="bmX" title="取消固定" onclick="event.stopPropagation();clickBtn(null,\'unpin\','
      + JSON.stringify(item.path).replace(/"/g, '&quot;') + ')">&times;</span></div>';
  }
  bar.innerHTML = html;
}

function renderSidebar() {
  let html = '';
  html += navSection('快捷访问', '&#xE735;', state.navPinned);
  html += navSection('此电脑', '&#xE7F8;', state.navPc);
  html += '<div class="navsec"><span class="ico">&#xE968;</span>网络</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'openconnections\')"><span class="ico">&#xE968;</span>连接管理</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'opensearch\')"><span class="ico">&#xE721;</span>全局搜索</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'opendisk\')"><span class="ico">&#xE9F4;</span>磁盘视图</div>';
  document.getElementById('sidebar').innerHTML = html;
}

function navSection(title, glyph, items) {
  let html = '<div class="navsec"><span class="ico">' + glyph + '</span>' + title + '</div>';
  if (!items || items.length === 0) {
    return html;
  }
  for (const item of items) {
    html += '<div class="navitem' + (item.selected ? ' selected' : '') + '" onclick="openPath('
      + JSON.stringify(item.path).replace(/"/g, '&quot;') + ')">'
      + '<span class="ico">' + (item.iconGlyph ? esc(item.iconGlyph) : '&#xE8B7;') + '</span>'
      + '<span>' + esc(item.name) + '</span></div>';
  }
  return html;
}

function renderRecent() {
  const pop = document.getElementById('recentPop');
  const items = state.recentPaths || [];
  if (items.length === 0) {
    pop.innerHTML = '<div class="empty">暂无最近访问</div>';
    return;
  }
  let html = '';
  for (const path of items) {
    html += '<div class="item" onclick="openPath(' + JSON.stringify(path).replace(/"/g, '&quot;') + ');closeMenus()">'
      + esc(path) + '</div>';
  }
  pop.innerHTML = html;
}

function renderToolbarState() {
  const isFile = currentTab && currentTab.tabType === 'file';
  const isSearch = currentTab && currentTab.tabType === 'search';
  const canNavigate = isFile || (currentTab && currentTab.tabType === 'disk');
  setGate('btnBack', !!(currentTab && currentTab.canGoBack));
  setGate('btnFwd', !!(currentTab && currentTab.canGoForward));
  setGate('btnUp', !!canNavigate);
  setGate('btnBack2', true);
  document.getElementById('btnUp').classList.toggle('disabled', !canNavigate);

  const searchBox = document.getElementById('searchBox');
  searchBox.disabled = !(isFile || isSearch);
  if (document.activeElement !== searchBox) {
    searchBox.value = (currentTab && currentTab.searchText) ? currentTab.searchText : '';
  }

  const mode = (currentTab && currentTab.viewMode) ? currentTab.viewMode : 'Details';
  const modeItems = document.querySelectorAll('#viewMenu .item[data-mode]');
  for (const item of modeItems) {
    item.querySelector('.tick').innerHTML = (item.getAttribute('data-mode') === mode) ? '&#10003;' : '';
  }
  document.getElementById('tickExt').innerHTML = state.showFileExtensions ? '&#10003;' : '';
  document.getElementById('tickHiddenBtn').innerHTML = state.showHiddenFiles ? '&#10003;' : '';
  document.getElementById('tickInfoBtn').innerHTML = (currentTab && currentTab.showInfoPane) ? '&#10003;' : '';
  document.getElementById('btnHidden').classList.toggle('disabled', !permissions.canModify);
}

function syncCheckedFromState() {
  const next = new Set();
  if (currentTab && (currentTab.tabType === 'file' || currentTab.tabType === 'search')) {
    for (const row of currentTab.rows) {
      if (row.isSelected) { next.add(row.fullPath); }
    }
  }
  checked = next;
  if (focusIndex >= currentTab.rows.length) { focusIndex = currentTab.rows.length - 1; }
  if (anchorIndex >= currentTab.rows.length) { anchorIndex = focusIndex; }
}

function renderContent(tab) {
  const signature = tab.index + '|' + tab.tabType + '|' + tab.viewMode + '|' + tab.showInfoPane
    + '|' + permissions.canCreateDelete + '|' + JSON.stringify(tab.rows)
    + '|' + JSON.stringify(Array.from(checked)) + '|' + tab.todo + '|' + tab.inProgress + '|' + tab.done
    + '|' + tab.connections + '|' + tab.summaryText;
  if (signature === lastContentSignature) { applyFocusHighlight(); return; }
  lastContentSignature = signature;

  let html = '';
  if (tab.tabType === 'file') {
    html = (ICON_MODES.indexOf(tab.viewMode) >= 0) ? renderTiles(tab) : renderTable(tab);
  } else if (tab.tabType === 'search') {
    html = renderTable(tab);
  } else if (tab.tabType === 'disk') {
    html = renderDiskTable(tab);
  } else if (tab.tabType === 'board') {
    html = renderBoard(tab);
  } else if (tab.tabType === 'connections') {
    html = renderConnections(tab);
  } else {
    html = '<div class="hint">「' + esc(tab.title) + '」是桌面端专用视图（终端 / 传输 / 编辑器等），网页端暂不展示其内容。</div>';
  }
  if (tab.totalRows && tab.totalRows > tab.rows.length) {
    html += '<div class="hint">仅显示前 ' + tab.rows.length + ' 项，共 ' + tab.totalRows + ' 项</div>';
  }
  // 摘要栏：与桌面一致，显示在内容底部（桌面 FileListPanel 底部摘要）
  if (tab.summaryText) {
    html += '<div class="hint summary">' + esc(tab.summaryText) + '</div>';
  }
  document.getElementById('contentMain').innerHTML = html;
  applyFocusHighlight();
}

function sortArrow(tab, field) {
  if (tab.sortField !== field) { return ''; }
  return '<span class="arrow">' + (tab.sortAscending ? '&#9650;' : '&#9660;') + '</span>';
}

function renderTable(tab) {
  const isSearch = tab.tabType === 'search';
  const mode = tab.viewMode || 'Details';
  let columns = '';
  if (mode === 'List') {
    columns = '';
  } else if (mode === 'Content') {
    columns = '<th>类型</th>';
  } else {
    columns = '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Modified\')">修改日期' + sortArrow(tab, 'Modified') + '</th>'
      + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Type\')">类型' + sortArrow(tab, 'Type') + '</th>'
      + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Size\')">大小' + sortArrow(tab, 'Size') + '</th>';
  }
  let head = '<tr><th style="width:34px"></th><th class="sortable" style="width:46%" onclick="clickBtn(null,\'sort\',\'Name\')">名称'
    + sortArrow(tab, 'Name') + '</th>' + columns + '</tr>';

  let html = '<table>' + head;
  if (!tab.rows || tab.rows.length === 0) {
    html += '<tr><td class="hint" colspan="6">' + (isSearch ? '没有匹配的搜索结果' : '空目录') + '</td></tr>';
  }
  let rowIndex = 0;
  for (const row of tab.rows) {
    const path = row.fullPath;
    const selected = checked.has(path);
    html += '<tr class="row' + (selected ? ' checked' : '') + '" data-i="' + rowIndex + '">'
      + '<td><input type="checkbox" data-check="' + rowIndex + '"' + (selected ? ' checked' : '') + '></td>'
      + '<td class="name"><span class="ico" style="color:' + esc(row.iconColor || '#9AA0A6') + '">'
      + esc(row.iconGlyph || '&#xE7C3;') + '</span>'
      + '<span class="text ' + (row.isDirectory ? 'dir' : 'file') + '" title="' + esc(path) + '">' + esc(row.name) + '</span></td>';
    if (mode === 'Details') {
      html += '<td>' + esc(row.modified) + '</td><td>' + esc(row.type) + '</td><td>' + esc(row.size) + '</td>';
    } else if (mode === 'Content') {
      html += '<td>' + esc(row.type) + '</td>';
    }
    html += '</tr>';
    rowIndex++;
  }
  html += '</table>';
  return html;
}

function renderTiles(tab) {
  const mode = tab.viewMode;
  let width = 96;
  if (mode === 'ExtraLargeIcons') { width = 210; }
  else if (mode === 'LargeIcons') { width = 130; }
  else if (mode === 'SmallIcons') { width = 64; }
  let html = '<div class="tiles">';
  let rowIndex = 0;
  for (const row of tab.rows) {
    const selected = checked.has(row.fullPath);
    html += '<div class="tile' + (selected ? ' checked' : '') + '" data-i="' + rowIndex + '" style="width:' + width + 'px">'
      + '<input type="checkbox" class="ttick" data-check="' + rowIndex + '"' + (selected ? ' checked' : '') + '>'
      + '<div class="ticon" style="color:' + esc(row.iconColor || '#9AA0A6') + '">' + esc(row.iconGlyph || '&#xE7C3;') + '</div>'
      + '<div class="tname" title="' + esc(row.fullPath) + '">' + esc(row.name) + '</div>';
    if (mode === 'Tile') { html += '<div class="ttype">' + esc(row.type) + '</div>'; }
    html += '</div>';
    rowIndex++;
  }
  html += '</div>';
  return html;
}

function renderDiskTable(tab) {
  let html = '<table><tr><th style="width:32%">名称</th><th>类型</th><th style="width:120px">大小</th>'
    + '<th style="width:130px">剩余</th><th style="width:90px">使用率</th><th style="width:110px">文件系统</th></tr>';
  if (!tab.rows || tab.rows.length === 0) {
    html += '<tr><td class="hint" colspan="6">暂无磁盘数据，正在扫描……</td></tr>';
  }
  let rowIndex = 0;
  for (const row of tab.rows) {
    // 磁盘视图：文件夹单击原地展开或收起（不进入下一级），文件不响应（与桌面端单击行为一致）
    const target = JSON.stringify(row.fullPath).replace(/"/g, '&quot;');
    const indent = (row.level || 0) * 16;
    html += '<tr class="row" data-i="' + rowIndex + '"><td class="name" style="padding-left:' + indent + 'px">';
    if (row.isDirectory) {
      html += '<span class="ico" style="color:#E8A317">' + (row.expanded ? '&#9662;' : '&#9656;') + '</span>'
        + '<span class="dir" onclick="diskNav(' + target + ')">' + esc(row.name) + '</span>';
    } else {
      html += '<span class="ico"></span><span>' + esc(row.name) + '</span>';
    }
    html += '</td><td>' + esc(row.type) + '</td><td>' + esc(row.size) + '</td>'
      + '<td>' + esc(row.free || '-') + '</td><td>' + esc(row.usage || '-') + '</td>'
      + '<td>' + esc(row.fileSystem || '-') + '</td></tr>';
    rowIndex++;
  }
  html += '</table>';
  return html;
}

function renderBoard(tab) {
  function column(title, items) {
    let html = '<div class="boardCol"><h4>' + title + '（' + items.length + '）</h4>';
    for (const item of items) { html += '<div class="boardItem">' + esc(item) + '</div>'; }
    if (items.length === 0) { html += '<div class="hint">暂无</div>'; }
    return html + '</div>';
  }
  return '<div class="board">'
    + column('待办', tab.todo || [])
    + column('进行中', tab.inProgress || [])
    + column('已完成', tab.done || [])
    + '</div>';
}

function renderConnections(tab) {
  let html = '<table><tr><th style="width:30%">名称</th><th>摘要</th></tr>';
  const items = tab.connections || [];
  if (items.length === 0) { html += '<tr><td class="hint" colspan="2">暂无连接，可在桌面端添加</td></tr>'; }
  for (const item of items) {
    const selected = item[0] === tab.selectedConnection;
    html += '<tr class="connRow' + (selected ? ' selected' : '') + '"><td>' + esc(item[0]) + '</td><td>' + esc(item[1]) + '</td></tr>';
  }
  html += '</table><div class="hint">连接管理为只读展示，建立连接等操作请在桌面端进行。</div>';
  return html;
}

function renderCrumb(tab) {
  const crumb = document.getElementById('crumb');
  if (tab.tabType === 'disk') {
    crumb.innerHTML = '<span class="seg" onclick="diskNav(\'\')">磁盘视图</span>'
      + (currentPath ? '<span class="sep">&rsaquo;</span><span class="hint">' + esc(currentPath) + '</span>' : '');
    return;
  }
  if (tab.tabType !== 'file') {
    crumb.innerHTML = '<span class="seg">' + esc(tab.title) + '</span>';
    crumbPaths = [];
    return;
  }
  crumbPaths = [''];
  let html = '<span class="seg" onclick="openPathAt(0)">此电脑</span>';
  if (currentPath) {
    let acc = '';
    for (const part of currentPath.replace(/\\+$/, '').split('\\')) {
      if (!part) { continue; }
      if (part.endsWith(':')) {
        acc = part + '\\';
        crumbPaths.push(acc);
        html += '<span class="sep">&rsaquo;</span><span class="seg" onclick="openPathAt(' + (crumbPaths.length - 1) + ')">' + esc(part) + '</span>';
        continue;
      }
      acc = acc.replace(/\\+$/, '') + '\\' + part;
      crumbPaths.push(acc);
      html += '<span class="sep">&rsaquo;</span><span class="seg" onclick="openPathAt(' + (crumbPaths.length - 1) + ')">' + esc(part) + '</span>';
    }
  }
  crumb.innerHTML = html;
}

function renderInfoPane() {
  const pane = document.getElementById('infoPane');
  const show = currentTab && currentTab.showInfoPane
    && (currentTab.tabType === 'file' || currentTab.tabType === 'search');
  pane.classList.toggle('hidden', !show);
  if (!show) { return; }
  let html = '<h4>详细信息</h4>';
  let count = 0;
  for (const row of currentRows) {
    if (!checked.has(row.fullPath)) { continue; }
    count++;
    if (count > 20) { break; }
    html += '<div class="infoBlock"><b>' + esc(row.name) + '</b>'
      + '<div class="infoLine">类型：' + esc(row.type) + '</div>'
      + '<div class="infoLine">大小：' + esc(row.size || '-') + '</div>'
      + '<div class="infoLine">修改日期：' + esc(row.modified || '-') + '</div>'
      + '<div class="infoLine">完整路径：' + esc(row.fullPath) + '</div></div>';
  }
  if (count === 0) { html += '<div class="hint">未勾选条目，勾选后在此显示详细信息。</div>'; }
  else if (count > 20) { html += '<div class="hint">仅显示前 20 项</div>'; }
  pane.innerHTML = html;
}

function renderStatus() {
  if (!state) { return; }
  let left = '';
  if (currentTab) {
    const total = currentTab.totalRows || (currentTab.rows ? currentTab.rows.length : 0);
    if (currentTab.tabType === 'file' || currentTab.tabType === 'search' || currentTab.tabType === 'disk') {
      left = total + ' 个项目';
    } else {
      left = currentTab.title;
    }
  } else {
    left = '没有打开的标签页';
  }
  // 状态栏左侧与桌面一致：仅显示项目数 / 标签名，提示语走内容区底部瞬时提示
  document.getElementById('statusLeft').textContent = left;
  document.getElementById('statusRight').textContent = PORT_LABEL + ' · '
    + (state.followDesktop ? '镜像主界面' : '独立新页面') + ' · 方案 ' + permissions.profile
    + ' · 快照 ' + state.generatedTime;

  const flashLine = document.getElementById('flashLine');
  const ideFlashLine = document.getElementById('ideFlashLine');
  if (lastMessage && Date.now() - lastMessageTime < 6000) {
    flashLine.textContent = lastMessage;
    flashLine.classList.remove('hidden');
    ideFlashLine.textContent = lastMessage;
    ideFlashLine.classList.remove('hidden');
  } else {
    flashLine.textContent = '';
    flashLine.classList.add('hidden');
    ideFlashLine.textContent = '';
    ideFlashLine.classList.add('hidden');
  }
}

// ======== 选择、右键菜单与行操作 ========

function checkedPaths() {
  return Array.from(checked);
}

function sendCheck() {
  return doAction('checkset', checkedPaths().join('\n'));
}

function rowRange(from, to) {
  const start = Math.min(from, to);
  const end = Math.max(from, to);
  const paths = [];
  for (let index = start; index <= end && index < currentRows.length; index++) {
    paths.push(currentRows[index].fullPath);
  }
  return paths;
}

function selectRow(index, additive, range) {
  const row = currentRows[index];
  if (!row) { return; }
  const path = row.fullPath;
  if (range && anchorIndex >= 0) {
    checked = new Set(rowRange(anchorIndex, index));
  } else if (additive) {
    if (checked.has(path)) { checked.delete(path); } else { checked.add(path); }
  } else if (checked.size === 1 && checked.has(path)) {
    // 已是单选该行：保持选择不变
  } else {
    checked = new Set([path]);
  }
  focusIndex = index;
  if (!range) { anchorIndex = index; }
  refreshSelectionDom();
  sendCheck();
}

function refreshSelectionDom() {
  const rows = document.querySelectorAll('#content tr.row[data-i], #content .tile[data-i]');
  for (const rowElement of rows) {
    const index = Number(rowElement.getAttribute('data-i'));
    const row = currentRows[index];
    if (!row) { continue; }
    rowElement.classList.toggle('checked', checked.has(row.fullPath));
    const box = rowElement.querySelector('input[type=checkbox]');
    if (box) { box.checked = checked.has(row.fullPath); }
  }
  applyFocusHighlight();
  renderInfoPane();
  renderStatus();
}

function applyFocusHighlight() {
  const rows = document.querySelectorAll('#content tr.row[data-i], #content .tile[data-i]');
  for (const rowElement of rows) {
    rowElement.classList.toggle('focus', Number(rowElement.getAttribute('data-i')) === focusIndex);
  }
}

function openTarget(row) {
  if (!row) { return; }
  if (row.isDirectory) {
    if (currentTab && currentTab.tabType === 'disk') { diskNav(row.fullPath); return; }
    openPath(row.fullPath);
    return;
  }
  openFile(row.fullPath);
}

function onContentClick(event) {
  if (event.target.matches('input[type=checkbox]')) { return; }
  const rowElement = event.target.closest('[data-i]');
  if (!rowElement || !currentRows.length) { return; }
  if (currentTab.tabType === 'disk') { focusIndex = Number(rowElement.getAttribute('data-i')); applyFocusHighlight(); return; }
  selectRow(Number(rowElement.getAttribute('data-i')), event.ctrlKey || event.metaKey, event.shiftKey);
}

function onContentDoubleClick(event) {
  if (event.target.matches('input[type=checkbox]')) { return; }
  // 磁盘视图：单击已切换展开或收起，双击不再重复切换（与桌面端单击行为一致）
  if (currentTab && currentTab.tabType === 'disk') { return; }
  const rowElement = event.target.closest('[data-i]');
  if (!rowElement) { return; }
  openTarget(currentRows[Number(rowElement.getAttribute('data-i'))]);
}

function onContentCheck(event) {
  if (!event.target.matches('input[data-check]')) { return; }
  const index = Number(event.target.getAttribute('data-check'));
  const row = currentRows[index];
  if (!row) { return; }
  if (event.target.checked) { checked.add(row.fullPath); } else { checked.delete(row.fullPath); }
  focusIndex = index;
  anchorIndex = index;
  refreshSelectionDom();
  sendCheck();
}

function onContentContextMenu(event) {
  const rowElement = event.target.closest('[data-i]');
  if (!rowElement || !currentRows.length) { return; }
  event.preventDefault();
  const index = Number(rowElement.getAttribute('data-i'));
  const row = currentRows[index];
  if (!row) { return; }
  if ((currentTab.tabType === 'file' || currentTab.tabType === 'search') && !checked.has(row.fullPath)) {
    checked = new Set([row.fullPath]);
    focusIndex = index;
    anchorIndex = index;
    refreshSelectionDom();
    sendCheck();
  }
  showContextMenu(event, row);
}

function showContextMenu(event, row) {
  const ctx = document.getElementById('ctx');
  const canEdit = permissions.canCreateDelete;
  ctx.innerHTML = '<div class="item" data-ctx="open">打开</div>'
    + '<div class="item" data-ctx="newtab">在新标签打开</div>'
    + '<div class="item" data-ctx="copy">复制</div>'
    + (canEdit ? '<div class="item" data-ctx="cut">剪切</div>' : '')
    + (canEdit ? '<div class="item" data-ctx="rename">重命名</div>' : '')
    + (permissions.canModify ? '<div class="item" data-ctx="chmod">修改权限</div>' : '')
    + (canEdit ? '<div class="item" data-ctx="delete">删除</div>' : '');
  ctx.classList.remove('hidden');
  const left = Math.min(event.clientX, window.innerWidth - 190);
  const top = Math.min(event.clientY, window.innerHeight - 200);
  ctx.style.left = left + 'px';
  ctx.style.top = top + 'px';
  ctx.onclick = function (menuEvent) {
    const item = menuEvent.target.closest('[data-ctx]');
    if (!item) { return; }
    const kind = item.getAttribute('data-ctx');
    ctx.classList.add('hidden');
    if (kind === 'open') { openTarget(row); }
    else if (kind === 'newtab') {
      if (row.isDirectory) { post('/api/tabs/new', { p: row.fullPath }); } else { openFile(row.fullPath); }
    }
    else if (kind === 'copy') { doAction('copy'); }
    else if (kind === 'cut') { clickBtn('btnCut', 'cut'); }
    else if (kind === 'rename') { renameEntry(row.fullPath); }
    else if (kind === 'chmod') { changePermissions(row.fullPath); }
    else if (kind === 'delete') { deleteEntry(row.fullPath); }
  };
}

function hideContextMenu() {
  document.getElementById('ctx').classList.add('hidden');
}

// ======== 地址栏编辑、工具栏动作 ========

function enterAddressEdit() {
  const input = document.getElementById('addrInput');
  const crumb = document.getElementById('crumb');
  if (!input.classList.contains('hidden')) { return; }
  if (!currentTab || currentTab.tabType !== 'file') { return; }
  input.value = currentPath;
  input.classList.remove('hidden');
  crumb.classList.add('hidden');
  input.focus();
  input.select();
}

function exitAddressEdit() {
  document.getElementById('addrInput').classList.add('hidden');
  document.getElementById('crumb').classList.remove('hidden');
}

function pickView(mode) {
  closeMenus();
  doAction('viewmode', mode);
}

function toggleExtensions() {
  if (!permissions.canModify) {
    flash('该端口未放行修改权限，无法切换扩展名显示');
    return;
  }
  closeMenus();
  doAction('toggleextensions');
}

function toggleInfoPane() {
  doAction('infopane', (currentTab && currentTab.showInfoPane) ? '0' : '1');
}

function copyChecked() {
  closeMenus();
  doAction('copy');
}

async function copyText(variant) {
  closeMenus();
  const paths = checkedPaths();
  if (paths.length === 0) { flash('请先勾选要复制的条目'); return; }
  const texts = [];
  for (const path of paths) {
    const clean = path.replace(/\\+$/, '');
    const slash = Math.max(clean.lastIndexOf('\\'), clean.lastIndexOf('/'));
    const name = slash >= 0 ? clean.substring(slash + 1) : clean;
    const dot = name.lastIndexOf('.');
    if (variant === 'dir') { texts.push(slash >= 0 ? clean.substring(0, slash) : clean); }
    else if (variant === 'full') { texts.push(path); }
    else if (variant === 'name') { texts.push(dot > 0 ? name.substring(0, dot) : name); }
    else { texts.push(name); }
  }
  const payload = texts.join('\n');
  try {
    await navigator.clipboard.writeText(payload);
    flash('已复制 ' + texts.length + ' 项文本到剪贴板');
  } catch (error) {
    const helper = document.createElement('textarea');
    helper.value = payload;
    document.body.appendChild(helper);
    helper.select();
    document.execCommand('copy');
    document.body.removeChild(helper);
    flash('已复制 ' + texts.length + ' 项文本到剪贴板');
  }
}

function createFolder() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限'); return; }
  if (!currentTab || currentTab.tabType !== 'file') { flash('请先进入目标文件夹'); return; }
  const name = window.prompt('新建文件夹名称');
  if (!name) { return; }
  post('/api/folder', { p: currentPath, n: name });
}

function renameEntry(target) {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限'); return; }
  const current = target.split('\\').pop().split('/').pop();
  const name = window.prompt('新名称', current);
  if (!name || name === current) { return; }
  post('/api/rename', { p: target, n: name });
}

// 修改远程权限（与桌面右键菜单「修改权限」同构）：八进制 000~777。
function changePermissions(target) {
  if (!permissions.canModify) { flash('该端口未放行修改权限'); return; }
  const mode = window.prompt('请输入八进制权限（如 644、755）：', '644');
  if (!mode || !mode.trim()) { return; }
  doAction('chmod', target + '\n' + mode.trim());
}

function deleteEntry(target) {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限'); return; }
  if (!window.confirm('确认删除：' + target + '？')) { return; }
  post('/api/delete', { p: target });
}

function deleteChecked() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限'); return; }
  if (checked.size === 0) { flash('请先勾选要删除的条目'); return; }
  if (!window.confirm('确认把勾选的 ' + checked.size + ' 项移入回收站？')) { return; }
  doAction('deletechecked');
}

function openBatch() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限'); return; }
  if (checked.size === 0) { flash('请先勾选要批量改名的条目'); return; }
  batchPaths = [];
  let html = '';
  for (const row of currentRows) {
    if (!checked.has(row.fullPath)) { continue; }
    batchPaths.push(row.fullPath);
    html += '<div class="brow"><span class="old" title="' + esc(row.fullPath) + '">' + esc(row.name) + '</span>'
      + '<span>&rsaquo;</span><input data-i="' + (batchPaths.length - 1) + '" value="' + esc(row.name) + '"></div>';
  }
  document.getElementById('batchCount').textContent = batchPaths.length;
  document.getElementById('batchList').innerHTML = html;
  document.getElementById('batch').classList.remove('hidden');
}

function closeBatch() {
  document.getElementById('batch').classList.add('hidden');
}

async function applyBatch() {
  const inputs = document.querySelectorAll('#batchList input');
  let plan = '';
  let changed = 0;
  for (const input of inputs) {
    const path = batchPaths[Number(input.getAttribute('data-i'))];
    const newName = input.value.trim();
    if (!path || !newName) { continue; }
    const oldName = path.split('\\').pop().split('/').pop();
    if (newName === oldName) { continue; }
    plan += path + '\t' + newName + '\n';
    changed++;
  }
  closeBatch();
  if (changed === 0) { flash('没有需要改名的条目'); return; }
  try {
    const response = await fetch(url('/api/action', { a: 'batchrename' }), { method: 'POST', body: plan });
    const text = await response.text();
    flash(response.ok ? (text || '批量改名已提交') : (text || '批量改名失败'));
  } catch (error) {
    flash('批量改名提交失败：' + error);
  }
  await refresh();
}

// ======== 只读 / 可写编辑器 ========

async function openFile(target) {
  editingPath = target;
  const textarea = document.getElementById('editorText');
  const saveButton = document.getElementById('editorSave');
  document.getElementById('editorTitle').textContent = target;
  document.getElementById('editorHint').textContent = permissions.canModify ? '该端口已放行修改权限' : '只读：该端口未放行修改权限';
  textarea.readOnly = !permissions.canModify;
  saveButton.className = 'btn' + (permissions.canModify ? '' : ' disabled');
  textarea.value = '正在读取……';
  document.getElementById('editor').className = 'modal';
  try {
    const response = await fetch(url('/api/file', { p: target, w: workspaceId }));
    if (!response.ok) {
      textarea.value = await response.text();
      return;
    }
    const data = await response.json();
    textarea.value = data.text;
  } catch (error) {
    textarea.value = '读取失败：' + error;
  }
}

function closeEditor() {
  document.getElementById('editor').className = 'modal hidden';
  editingPath = '';
}

async function saveFile() {
  if (!permissions.canModify) { flash('该端口未放行修改权限'); return; }
  if (!editingPath) { return; }
  const content = document.getElementById('editorText').value;
  try {
    const response = await fetch(url('/api/write', { p: editingPath }), { method: 'POST', body: content });
    document.getElementById('editorHint').textContent = response.ok ? '已保存' : ('保存失败：' + await response.text());
  } catch (error) {
    document.getElementById('editorHint').textContent = '保存失败：' + error;
  }
}

// ======== IDE 模式（与桌面 IdeShellView 同构） ========

let ideTreeNodes = [];
let ideEditorList = [];
let ideQuickItems = [];
let ideFindResults = [];
let ideSelectedPath = '';
let ideLoadedIndex = -1;
let ideLoadedToken = '';
let ideLocalDirty = false;
let idePushPending = false;
let idePushTimer = null;
let ideFetching = false;
let ideFindTimer = null;
let ideReplaceTimer = null;
let ideQuickTimer = null;
let ideAppliedSelStart = -1;
let ideAppliedSelLength = 0;
let idePendingCaretLine = 0;
let idePendingCloseIndex = -1;
let ideQuickWasOpen = false;
let ideTreeSignature = '';
let ideTabsSignature = '';
let ideFindSignature = '';
let ideQuickSignature = '';

function toggleIdeMode() { doAction('ide'); }

function escAttr(value) { return esc(value).replace(/"/g, '&quot;'); }

function clickIdeBtn(id, action, argument) {
  const element = id ? document.getElementById(id) : null;
  if (element && element.classList.contains('disabled')) {
    flash('该端口未放行该操作，权限方案=' + permissions.profile);
    return;
  }
  // 替换类动作先把输入框里的最新文字同步给服务端，避免防抖窗口内用到旧文字
  if (action === 'idereplace' || action === 'idereplaceall') { clickIdeReplace(action); return; }
  doAction(action, argument);
}

async function clickIdeReplace(action) {
  const findValue = document.getElementById('ideFindBox').value;
  const replaceValue = document.getElementById('ideReplaceBox').value;
  if (ideFindTimer) { clearTimeout(ideFindTimer); ideFindTimer = null; }
  if (ideReplaceTimer) { clearTimeout(ideReplaceTimer); ideReplaceTimer = null; }
  if (!state || !state.ide || findValue !== (state.ide.findText || '')) { await doAction('idefindtext', findValue); }
  if (!state || !state.ide || replaceValue !== (state.ide.replaceText || '')) { await doAction('idereplacetext', replaceValue); }
  await doAction(action, '');
}

// 文本令牌：与服务端 DebugIdeState.ComputeTextToken（FNV-1a）保持一致
function ideComputeToken(text) {
  let hash = 2166136261;
  for (let i = 0; i < text.length; i++) {
    hash = (hash ^ text.charCodeAt(i)) >>> 0;
    hash = Math.imul(hash, 16777619) >>> 0;
  }
  return text.length + '-' + hash.toString(16).padStart(8, '0');
}

// 布局切换：IDE 模式隐藏资源管理器布局，反之亦然（标题栏标签条两种模式均保留）
function applyIdeLayout(on) {
  const explorerIds = ['bookmarkbar', 'toolbar', 'navrow', 'workarea', 'statusbar'];
  for (const id of explorerIds) {
    document.getElementById(id).classList.toggle('hidden', on);
  }
  document.getElementById('ideView').classList.toggle('hidden', !on);
  document.getElementById('ideIconBtn').classList.toggle('active', on);
  document.getElementById('btnIde').classList.toggle('active', on);
}

function renderIde() {
  const ide = state.ide || {};
  renderIdeTree(ide);
  renderIdeTabs(ide);
  renderIdeEditor(ide);
  renderIdeFind(ide);
  renderIdeQuick(ide);
  renderIdeStatus(ide);
}

// 资源树：按深度缩进拍平节点；单击选中，双击目录展开、双击文件打开
function renderIdeTree(ide) {
  const root = document.getElementById('ideRoot');
  root.textContent = ide.workspaceRootText || '';
  root.title = ide.workspaceRootText || '';
  ideTreeNodes = ide.tree || [];
  let html = '';
  if (ide.isWorkspaceLoading) {
    html = '<div class="hint">正在加载工作区……</div>';
  } else if (ideTreeNodes.length === 0) {
    html = '<div class="hint">工作区暂无内容</div>';
  } else {
    for (let i = 0; i < ideTreeNodes.length; i++) {
      const node = ideTreeNodes[i];
      const selected = node.fullPath === ideSelectedPath ? ' sel' : '';
      const arrow = node.isDirectory ? (node.isExpanded ? '&#xE70D;' : '&#xE76C;') : '';
      html += '<div class="ideNode' + selected + '" data-i="' + i + '" style="padding-left:' + (4 + node.depth * 14) + 'px"'
        + ' title="' + escAttr(node.fullPath) + '">'
        + '<span class="arrow">' + arrow + '</span>'
        + '<span class="ico" style="color:' + escAttr(node.iconColor || '#9AA0A6') + '">' + esc(node.iconGlyph || '') + '</span>'
        + '<span class="nm">' + esc(node.name) + '</span></div>';
    }
  }
  // 与内容区一致：内容未变不重建 DOM，避免轮询打断鼠标操作
  if (ideTreeSignature !== html) {
    document.getElementById('ideTreeList').innerHTML = html;
    ideTreeSignature = html;
  }
}

// 编辑器标签条：DisplayTitle 已含脏标记「●」，点击切换，点 × 走三分支关闭确认
function renderIdeTabs(ide) {
  ideEditorList = ide.editors || [];
  let html = '';
  for (const editor of ideEditorList) {
    const active = editor.index === ide.activeEditorIndex ? ' active' : '';
    html += '<div class="ideTab' + active + '" data-i="' + editor.index + '" title="' + escAttr(editor.toolTipText) + '">'
      + '<span>' + esc(editor.displayTitle) + '</span>'
      + '<span class="x" data-close="' + editor.index + '" title="关闭">&times;</span></div>';
  }
  if (ideTabsSignature !== html) {
    document.getElementById('ideTabs').innerHTML = html;
    ideTabsSignature = html;
  }
}

// 编辑器区：无激活编辑器显示空态提示，否则同步文本令牌并应用查找选区
function renderIdeEditor(ide) {
  const textarea = document.getElementById('ideText');
  const gutter = document.getElementById('ideGutter');
  const empty = document.getElementById('ideEmpty');
  const index = typeof ide.activeEditorIndex === 'number' ? ide.activeEditorIndex : -1;
  if (index < 0) {
    textarea.classList.add('hidden');
    empty.classList.remove('hidden');
    gutter.textContent = '1';
    ideLoadedIndex = -1;
    ideLoadedToken = '';
    ideLocalDirty = false;
    ideAppliedSelStart = -1;
    return;
  }
  empty.classList.add('hidden');
  textarea.classList.remove('hidden');
  textarea.readOnly = !permissions.canModify || !!ide.activeIsReadOnly;
  if (index !== ideLoadedIndex) {
    // 切换编辑器：旧编辑器如有未推送内容先补推（按旧下标），再清空等待新全文
    if (ideLocalDirty && ideLoadedIndex >= 0 && !idePushPending) {
      idePushText(ideLoadedIndex, textarea.value);
    }
    ideLoadedIndex = index;
    ideLoadedToken = '';
    ideLocalDirty = false;
    ideAppliedSelStart = -1;
    if (idePushTimer) { clearTimeout(idePushTimer); idePushTimer = null; }
    textarea.value = '';
    gutter.textContent = '1';
  }
  ideMaybeFetch(ide);
  ideApplySelection(ide);
  ideApplyPendingCaret();
  ideUpdateGutter();
}

// 令牌不一致且本地无未推送内容时，按需拉取激活编辑器全文
function ideMaybeFetch(ide) {
  if (!ide || typeof ide.activeEditorIndex !== 'number' || ide.activeEditorIndex < 0) { return; }
  if (ideLocalDirty || idePushPending || ideFetching) { return; }
  if (ide.activeTextToken && ide.activeTextToken !== ideLoadedToken) {
    ideFetchText(ide.activeEditorIndex);
  }
}

async function ideFetchText(index) {
  const startedIndex = index;
  ideFetching = true;
  try {
    const response = await fetch(url('/api/ide/text', { i: index }));
    if (response.ok) {
      const data = await response.json();
      // 仅当仍是同一激活编辑器且本地无未推送编辑时才采纳
      if (data.index === ideLoadedIndex && !ideLocalDirty && !idePushPending) {
        const textarea = document.getElementById('ideText');
        if (textarea.value !== data.text) {
          const hadFocus = document.activeElement === textarea;
          const start = textarea.selectionStart;
          const end = textarea.selectionEnd;
          textarea.value = data.text;
          if (hadFocus) {
            const max = data.text.length;
            textarea.setSelectionRange(Math.min(start, max), Math.min(end, max));
          } else {
            // 未聚焦时光标回到开头（与桌面打开文件后的初始位置一致），随后由查找选区接管
            textarea.setSelectionRange(0, 0);
          }
          ideAppliedSelStart = -1;
          ideAppliedSelLength = 0;
        }
        ideLoadedToken = data.token;
        ideUpdateGutter();
        ideUpdateCaret();
        // 全文更新后重放查找选区（替换 / 外部修改会改变令牌触发重取）
        if (state && state.ide) { ideApplySelection(state.ide); ideApplyPendingCaret(); }
      }
    }
  } catch (error) { /* 拉取失败：下一次轮询重试 */ }
  ideFetching = false;
  // 拉取期间切换了编辑器则补一次判断
  if (state && state.ide && state.isIdeMode && state.ide.activeEditorIndex !== startedIndex) {
    ideMaybeFetch(state.ide);
  }
}

// 防抖把本地编辑推回服务端（动作 idetext，请求体格式「编辑器下标\n全文」）
function ideSchedulePush() {
  if (idePushTimer) { clearTimeout(idePushTimer); }
  idePushTimer = setTimeout(function () { ideFlushPush(); }, 600);
}

function ideFlushPush() {
  if (idePushTimer) { clearTimeout(idePushTimer); idePushTimer = null; }
  if (!ideLocalDirty || idePushPending || ideLoadedIndex < 0) { return; }
  idePushText(ideLoadedIndex, document.getElementById('ideText').value);
}

// 保存前立即推送本地未同步的编辑，避免防抖窗口内保存到旧内容
async function ideFlushPushNow() {
  if (idePushTimer) { clearTimeout(idePushTimer); idePushTimer = null; }
  if (!ideLocalDirty || idePushPending || ideLoadedIndex < 0) { return; }
  await idePushText(ideLoadedIndex, document.getElementById('ideText').value);
}

// 保存按钮 / 保存快捷键入口：先推送本地编辑再触发保存动作（权限门与 clickIdeBtn 一致）
async function clickIdeSave(id, action) {
  const element = id ? document.getElementById(id) : null;
  if (element && element.classList.contains('disabled')) {
    flash('该端口未放行该操作，权限方案=' + permissions.profile);
    return;
  }
  await ideFlushPushNow();
  await doAction(action, '');
}

async function idePushText(index, text) {
  idePushPending = true;
  try {
    const response = await fetch(url('/api/action', { a: 'idetext' }), { method: 'POST', body: index + '\n' + text });
    if (response.ok) {
      const body = (await response.text()).trim();
      let token = ideComputeToken(text);
      if (/^[0-9]+-[0-9a-f]{8}$/.test(body)) { token = body; }
      if (ideLoadedIndex === index) {
        ideLoadedToken = token;
        ideLocalDirty = false;
      }
    } else {
      // 推送失败：放弃本地未同步内容，回退到服务端权威文本
      flash('编辑器文本同步失败：' + (await response.text()));
      if (ideLoadedIndex === index) { ideLocalDirty = false; }
    }
  } catch (error) {
    flash('编辑器文本同步失败，网络中断');
    if (ideLoadedIndex === index) { ideLocalDirty = false; }
  }
  idePushPending = false;
  if (state && state.ide && state.isIdeMode) { ideMaybeFetch(state.ide); }
}

// 行号栏：与编辑区同字体行高，滚动时同步偏移
function ideUpdateGutter() {
  const textarea = document.getElementById('ideText');
  const gutter = document.getElementById('ideGutter');
  if (textarea.classList.contains('hidden')) { return; }
  const lines = textarea.value.split('\n').length;
  let text = '';
  for (let i = 1; i <= lines; i++) { text += i + '\n'; }
  if (gutter.textContent !== text) { gutter.textContent = text; }
  gutter.scrollTop = textarea.scrollTop;
}

// 查找选区：服务端给出位置后套到编辑区并滚动到可见（焦点在查找框时不清焦点，便于连续回车）
function ideApplySelection(ide) {
  const start = typeof ide.findSelectionStart === 'number' ? ide.findSelectionStart : -1;
  if (start < 0) { ideAppliedSelStart = -1; return; }
  const length = ide.findSelectionLength || 0;
  if (ideAppliedSelStart === start && ideAppliedSelLength === length) { return; }
  const textarea = document.getElementById('ideText');
  if (textarea.classList.contains('hidden')) { return; }
  if (ideLoadedIndex !== ide.activeEditorIndex) { return; }
  if (start > textarea.value.length) { return; }
  ideAppliedSelStart = start;
  ideAppliedSelLength = length;
  if (document.activeElement === document.body || document.activeElement === textarea) { textarea.focus(); }
  textarea.setSelectionRange(start, Math.min(start + length, textarea.value.length));
  const lineHeight = parseFloat(getComputedStyle(textarea).lineHeight) || 20;
  const lineTop = (textarea.value.substring(0, start).split('\n').length - 1) * lineHeight;
  const viewTop = textarea.scrollTop;
  const viewHeight = textarea.clientHeight;
  if (lineTop < viewTop) { textarea.scrollTop = lineTop; }
  else if (lineTop + lineHeight > viewTop + viewHeight - 8) { textarea.scrollTop = lineTop + lineHeight - viewHeight + 8; }
}

// 待定位行：搜索结果双击后记录目标行，文本就绪时把光标移到该行行首（与桌面 MoveCaretToLine 一致）
function ideApplyPendingCaret() {
  if (idePendingCaretLine <= 0) { return; }
  if (!state || !state.isIdeMode || !state.ide) { return; }
  const textarea = document.getElementById('ideText');
  if (textarea.classList.contains('hidden')) { return; }
  if (ideLoadedIndex !== state.ide.activeEditorIndex) { return; }
  // 正在拉取或尚未拿到全文时不套用，避免按空内容算出错误位置
  if (ideFetching || !ideLoadedToken) { return; }
  const lines = textarea.value.split('\n');
  let offset = 0;
  let lineIndex = 0;
  for (; lineIndex < idePendingCaretLine - 1 && lineIndex < lines.length; lineIndex++) {
    offset += lines[lineIndex].length + 1;
  }
  if (offset > textarea.value.length) { offset = textarea.value.length; }
  idePendingCaretLine = 0;
  if (document.activeElement === document.body || document.activeElement === textarea) { textarea.focus(); }
  textarea.setSelectionRange(offset, offset);
  const lineHeight = parseFloat(getComputedStyle(textarea).lineHeight) || 20;
  const lineTop = Math.min(lineIndex, lines.length - 1) * lineHeight;
  const viewTop = textarea.scrollTop;
  const viewHeight = textarea.clientHeight;
  if (lineTop < viewTop) { textarea.scrollTop = lineTop; }
  else if (lineTop + lineHeight > viewTop + viewHeight - 8) { textarea.scrollTop = lineTop + lineHeight - viewHeight + 8; }
  ideUpdateCaret();
}

// 查找面板：显示状态、关键字、替换文字、状态提示与工作区搜索结果
function renderIdeFind(ide) {
  const panel = document.getElementById('ideFind');
  panel.classList.toggle('hidden', !ide.showFindPanel);
  const findBox = document.getElementById('ideFindBox');
  if (document.activeElement !== findBox && findBox.value !== (ide.findText || '')) { findBox.value = ide.findText || ''; }
  const replaceBox = document.getElementById('ideReplaceBox');
  if (document.activeElement !== replaceBox && replaceBox.value !== (ide.replaceText || '')) { replaceBox.value = ide.replaceText || ''; }
  document.getElementById('ideFindStatus').textContent = ide.findStatusText || '';
  ideFindResults = ide.findResults || [];
  let html = '';
  for (let i = 0; i < ideFindResults.length; i++) {
    const item = ideFindResults[i];
    html += '<div class="ideFRes" data-i="' + i + '" title="' + escAttr(item.filePath + ' 行 ' + item.lineNumber) + '">'
      + '<span>' + esc(item.summary) + '</span><span class="ln">' + esc(item.lineText) + '</span></div>';
  }
  if (html === '' && ide.showFindPanel && !ide.isWorkspaceLoading) {
    html = '<div class="hint">（暂无结果，输入关键字后点「在工作区搜索」）</div>';
  }
  if (ideFindSignature !== html) {
    document.getElementById('ideFindList').innerHTML = html;
    ideFindSignature = html;
  }
}

// 快速打开浮层：显示状态、关键字与候选列表
function renderIdeQuick(ide) {
  const quick = document.getElementById('ideQuick');
  const open = !!ide.showQuickOpen;
  quick.classList.toggle('hidden', !open);
  const input = document.getElementById('ideQuickInput');
  if (document.activeElement !== input && input.value !== (ide.quickOpenText || '')) { input.value = ide.quickOpenText || ''; }
  if (open && !ideQuickWasOpen) { input.focus(); input.select(); }
  ideQuickWasOpen = open;
  ideQuickItems = ide.quickOpenItems || [];
  let html = '';
  for (let i = 0; i < ideQuickItems.length; i++) {
    const item = ideQuickItems[i];
    html += '<div class="ideQItem" data-i="' + i + '" title="' + escAttr(item.filePath) + '">'
      + '<span>' + esc(item.fileName) + '</span><span class="dir">' + esc(item.directoryText) + '</span></div>';
  }
  if (html === '' && open) { html = '<div class="hint">（无匹配文件）</div>'; }
  if (ideQuickSignature !== html) {
    document.getElementById('ideQuickList').innerHTML = html;
    ideQuickSignature = html;
  }
}

// 光标行列：文本已就绪时本地计算，否则回退到服务端位置文字
function ideCaretText(ide) {
  const textarea = document.getElementById('ideText');
  if (typeof ide.activeEditorIndex === 'number' && ide.activeEditorIndex >= 0
      && ideLoadedIndex === ide.activeEditorIndex && !textarea.classList.contains('hidden')) {
    const before = textarea.value.substring(0, textarea.selectionStart).split('\n');
    return '行 ' + before.length + '，列 ' + (before[before.length - 1].length + 1);
  }
  return ide.activeLocation || '';
}

function ideUpdateCaret() {
  if (!state || !state.isIdeMode || !state.ide) { return; }
  document.getElementById('ideCaret').textContent = ideCaretText(state.ide);
}

// IDE 状态栏：输出状态 / 当前文件 / 光标行列 / 编码
function renderIdeStatus(ide) {
  document.getElementById('ideSolution').textContent = ide.solutionStatusText || (ide.isWorkspaceLoading ? '正在加载工作区……' : '就绪');
  document.getElementById('ideLocation').textContent = ide.activeLocation || '';
  document.getElementById('ideCaret').textContent = ideCaretText(ide);
  document.getElementById('ideEncoding').textContent = ide.activeEncoding || '';
}

// 查找框输入：防抖同步关键字到服务端
function ideScheduleFindText() {
  if (ideFindTimer) { clearTimeout(ideFindTimer); }
  ideFindTimer = setTimeout(function () {
    ideFindTimer = null;
    doAction('idefindtext', document.getElementById('ideFindBox').value);
  }, 350);
}

// 替换框输入：防抖同步替换文字到服务端（独立定时器，避免与查找框互相取消）
function ideScheduleReplaceText() {
  if (ideReplaceTimer) { clearTimeout(ideReplaceTimer); }
  ideReplaceTimer = setTimeout(function () {
    ideReplaceTimer = null;
    doAction('idereplacetext', document.getElementById('ideReplaceBox').value);
  }, 350);
}

// 快速打开输入：防抖同步过滤关键字
function ideScheduleQuickText() {
  if (ideQuickTimer) { clearTimeout(ideQuickTimer); }
  ideQuickTimer = setTimeout(function () {
    ideQuickTimer = null;
    doAction('idequicktext', document.getElementById('ideQuickInput').value);
  }, 300);
}

async function ideOpenFind() {
  await doAction('idefindpanel', '1');
  const box = document.getElementById('ideFindBox');
  box.focus();
  box.select();
}

function ideFindNext(forward) { doAction('idefindnext', forward === '1' || forward === true ? '1' : '0'); }

// 在编辑器清单中按下标查找条目
function ideFindEditorIn(list, index) {
  for (const item of list) {
    if (item.index === index) { return item; }
  }
  return null;
}

// 关闭脏编辑器：脏则弹三分支确认（保存 / 不保存 / 取消），干净直接关闭
async function ideRequestClose(index) {
  // 关闭前取一次最新状态，避免轮询间隔内脏标记过期导致未确认就丢弃更改
  let editor = null;
  try {
    const fresh = await (await fetch('/api/state')).json();
    if (fresh && fresh.ide) { editor = ideFindEditorIn(fresh.ide.editors, index); }
  } catch (error) { /* 拉取失败时回落到本地缓存 */ }
  if (!editor) { editor = ideFindEditorIn(ideEditorList, index); }
  // 本地有尚未推送给服务端的编辑，同样视为脏（防抖窗口内不得直接关闭）
  const localDirty = ideLocalDirty && index === ideLoadedIndex;
  if ((!editor || !editor.isDirty) && !localDirty) { doAction('ideclose', String(index)); return; }
  idePendingCloseIndex = index;
  document.getElementById('idePromptTitle').textContent = '保存更改';
  let targetName = '当前编辑器';
  if (editor) { targetName = editor.toolTipText || editor.displayTitle; }
  document.getElementById('idePromptBody').textContent = '是否保存对 ' + targetName + ' 的更改？';
  document.getElementById('idePrompt').classList.remove('hidden');
}

async function idePromptChoice(choice) {
  const index = idePendingCloseIndex;
  idePendingCloseIndex = -1;
  document.getElementById('idePrompt').classList.add('hidden');
  if (index < 0 || choice === 'cancel') { return; }
  if (choice === 'save') {
    // 保存目标编辑器前，若其内容尚未推送给服务端则立即推送，避免保存到旧内容
    if (index === ideLoadedIndex) { await ideFlushPushNow(); }
    await doAction('idesave', String(index));
    if (!permissions.canModify) { return; }
  }
  await doAction('ideclose', String(index));
}

// ======== 快捷键（与桌面主窗口快捷键一致） ========

function isTyping(element) {
  return element && (element.tagName === 'INPUT' || element.tagName === 'TEXTAREA');
}

function moveFocus(step, extend) {
  if (!currentRows.length) { return; }
  let next = focusIndex + step;
  if (next < 0) { next = 0; }
  if (next >= currentRows.length) { next = currentRows.length - 1; }
  if (extend && anchorIndex >= 0) {
    checked = new Set(rowRange(anchorIndex, next));
  } else {
    checked = new Set([currentRows[next].fullPath]);
  }
  focusIndex = next;
  refreshSelectionDom();
  sendCheck();
}

function handleKeyDown(event) {
  const target = event.target;
  if (event.key === 'Escape') {
    // IDE 模式浮层优先关闭：确认弹窗 → 快速打开 → 查找面板
    if (!document.getElementById('idePrompt').classList.contains('hidden')) { idePromptChoice('cancel'); return; }
    if (state && state.isIdeMode) {
      if (!document.getElementById('ideQuick').classList.contains('hidden')) { doAction('idequickclose'); return; }
      if (!document.getElementById('ideFind').classList.contains('hidden')) { doAction('idefindpanel', '0'); return; }
    }
    hideContextMenu();
    closeMenus();
    exitAddressEdit();
    if (!document.getElementById('batch').classList.contains('hidden')) { closeBatch(); }
    else if (!document.getElementById('editor').classList.contains('hidden')) { closeEditor(); }
    return;
  }

  const ctrl = event.ctrlKey || event.metaKey;
  if (state && state.isIdeMode) {
    // IDE 模式快捷键（与桌面 IdeShellView 一致）：保存 / 全部保存 / 查找 / 快速打开 / 刷新
    if (ctrl) {
      const key = event.key.toLowerCase();
      if (key === 's' && event.shiftKey) { event.preventDefault(); clickIdeSave('ideBtnSaveAll', 'idesaveall'); return; }
      if (key === 's') { event.preventDefault(); clickIdeSave('ideBtnSave', 'idesave'); return; }
      if (key === 'f') { event.preventDefault(); ideOpenFind(); return; }
      if (key === 'p') { event.preventDefault(); clickIdeBtn('ideBtnQuick', 'idequickopen'); return; }
      // 标签条在 IDE 模式下仍保留，新建 / 关闭标签快捷键与桌面一致
      if (key === 't') { event.preventDefault(); newTab(); return; }
      if (key === 'w') { event.preventDefault(); if (currentTab && !state.followDesktop) { closeTab(currentTab.index); } return; }
    }
    if (event.key === 'F5' && !isTyping(target)) { event.preventDefault(); clickIdeBtn('ideBtnRefresh', 'iderefresh'); }
    return;
  }
  if (isTyping(target)) { return; }

  if (ctrl && event.key.toLowerCase() === 't') { event.preventDefault(); newTab(); return; }
  if (ctrl && event.key.toLowerCase() === 'w') {
    event.preventDefault();
    if (currentTab && !state.followDesktop) { closeTab(currentTab.index); }
    return;
  }
  if (ctrl && event.key.toLowerCase() === 'a') {
    event.preventDefault();
    if (currentTab && (currentTab.tabType === 'file' || currentTab.tabType === 'search')) { doAction('checkall'); }
    return;
  }
  if (ctrl && event.key.toLowerCase() === 'c') { event.preventDefault(); doAction('copy'); return; }
  if (ctrl && event.key.toLowerCase() === 'x') { event.preventDefault(); clickBtn('btnCut', 'cut'); return; }
  if (ctrl && event.key.toLowerCase() === 'v') { event.preventDefault(); clickBtn('btnPaste', 'paste'); return; }
  if (event.altKey && event.key === 'ArrowLeft') { event.preventDefault(); clickBtn('btnBack', 'back'); return; }
  if (event.altKey && event.key === 'ArrowRight') { event.preventDefault(); clickBtn('btnFwd', 'forward'); return; }
  if (event.key === 'F5') { event.preventDefault(); clickBtn(null, 'refresh'); return; }
  if (event.key === 'Delete') { event.preventDefault(); deleteChecked(); return; }

  if (currentTab && (currentTab.tabType !== 'file' && currentTab.tabType !== 'search')) { return; }
  if (event.key === 'ArrowDown') { event.preventDefault(); moveFocus(1, event.shiftKey); }
  else if (event.key === 'ArrowUp') { event.preventDefault(); moveFocus(-1, event.shiftKey); }
  else if (event.key === ' ' && focusIndex >= 0) {
    event.preventDefault();
    const row = currentRows[focusIndex];
    if (row) {
      if (checked.has(row.fullPath)) { checked.delete(row.fullPath); } else { checked.add(row.fullPath); }
      anchorIndex = focusIndex;
      refreshSelectionDom();
      sendCheck();
    }
  } else if (event.key === 'Enter' && focusIndex >= 0) {
    event.preventDefault();
    openTarget(currentRows[focusIndex]);
  }
}

// ======== 事件挂接与启动 ========

function scheduleSearch() {
  if (searchTimer) { clearTimeout(searchTimer); }
  const value = document.getElementById('searchBox').value;
  searchTimer = setTimeout(function () {
    if (!currentTab) { return; }
    if ((currentTab.searchText || '') === value) { return; }
    doAction('search', value);
  }, 400);
}

document.addEventListener('click', function (event) {
  if (event.target.closest('.dd') || event.target.closest('#recentBtn') || event.target.closest('#recentPop')) { return; }
  closeMenus();
  if (!event.target.closest('#ctx')) { hideContextMenu(); }
});

document.getElementById('crumb').addEventListener('click', function (event) {
  if (event.target === event.currentTarget) { enterAddressEdit(); }
});

document.getElementById('addrInput').addEventListener('keydown', function (event) {
  if (event.key === 'Enter') {
    const value = document.getElementById('addrInput').value.trim();
    exitAddressEdit();
    if (value) { openPath(value); }
    event.preventDefault();
  } else if (event.key === 'Escape') {
    exitAddressEdit();
    event.preventDefault();
  }
});

document.getElementById('searchBox').addEventListener('input', scheduleSearch);
document.getElementById('searchBox').addEventListener('keydown', function (event) {
  if (event.key === 'Enter') {
    event.preventDefault();
    if (searchTimer) { clearTimeout(searchTimer); }
    doAction('search', document.getElementById('searchBox').value);
  }
});

document.getElementById('content').addEventListener('click', onContentClick);
document.getElementById('content').addEventListener('dblclick', onContentDoubleClick);
document.getElementById('content').addEventListener('change', onContentCheck);
document.getElementById('content').addEventListener('contextmenu', onContentContextMenu);
document.addEventListener('keydown', handleKeyDown);

document.getElementById('editorText').addEventListener('keydown', function (event) {
  if (event.key === 'Tab') {
    event.preventDefault();
    const textarea = event.target;
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    textarea.value = textarea.value.substring(0, start) + '    ' + textarea.value.substring(end);
    textarea.selectionStart = textarea.selectionEnd = start + 4;
  }
});

// ======== IDE 模式事件挂接 ========

// 资源树：单击选中；双击目录展开 / 收起，双击文件打开
document.getElementById('ideTreeList').addEventListener('click', function (event) {
  const element = event.target.closest('[data-i]');
  if (!element) { return; }
  const node = ideTreeNodes[Number(element.getAttribute('data-i'))];
  if (!node) { return; }
  ideSelectedPath = node.fullPath;
  const nodes = document.querySelectorAll('#ideTreeList .ideNode');
  for (const item of nodes) { item.classList.remove('sel'); }
  element.classList.add('sel');
});

document.getElementById('ideTreeList').addEventListener('dblclick', function (event) {
  const element = event.target.closest('[data-i]');
  if (!element) { return; }
  const node = ideTreeNodes[Number(element.getAttribute('data-i'))];
  if (!node || node.isPlaceholder) { return; }
  if (node.isDirectory) { doAction('idetree', node.fullPath); }
  else { doAction('ideopen', node.fullPath); }
});

// 编辑器标签：点标签切换激活，点 × 走关闭流程（脏内容弹三分支确认）
document.getElementById('ideTabs').addEventListener('click', function (event) {
  const closeElement = event.target.closest('[data-close]');
  if (closeElement) { ideRequestClose(Number(closeElement.getAttribute('data-close'))); return; }
  const tabElement = event.target.closest('[data-i]');
  if (!tabElement) { return; }
  const index = Number(tabElement.getAttribute('data-i'));
  const activeIndex = (state && state.ide) ? state.ide.activeEditorIndex : -1;
  if (index !== activeIndex) { doAction('ideselect', String(index)); }
});

// 工作区搜索结果：双击打开对应文件并跳到行
document.getElementById('ideFindList').addEventListener('dblclick', function (event) {
  const element = event.target.closest('[data-i]');
  if (!element) { return; }
  const item = ideFindResults[Number(element.getAttribute('data-i'))];
  if (!item) { return; }
  idePendingCaretLine = item.lineNumber;
  doAction('idefindresult', item.filePath + '\n' + item.lineNumber);
});

// 快速打开：双击候选打开；回车提交首项（无候选时按输入文字打开），方向下选中首项
document.getElementById('ideQuickList').addEventListener('dblclick', function (event) {
  const element = event.target.closest('[data-i]');
  if (!element) { return; }
  const item = ideQuickItems[Number(element.getAttribute('data-i'))];
  if (!item) { return; }
  doAction('idequickcommit', item.filePath);
});

document.getElementById('ideQuickInput').addEventListener('input', ideScheduleQuickText);
document.getElementById('ideQuickInput').addEventListener('keydown', function (event) {
  if (event.key === 'Enter') {
    event.preventDefault();
    event.stopPropagation();
    if (ideQuickItems.length > 0) { doAction('idequickcommit', ideQuickItems[0].filePath); }
    else { doAction('idequickcommit', event.target.value); }
  } else if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    doAction('idequickclose');
  } else if (event.key === 'ArrowDown') {
    event.preventDefault();
    const first = document.querySelector('#ideQuickList .ideQItem');
    if (first) { first.classList.add('sel'); }
  }
});

// 查找面板：回车 = 下一个，Esc = 关闭面板；关键字与替换文字防抖同步
document.getElementById('ideFindBox').addEventListener('input', ideScheduleFindText);
document.getElementById('ideFindBox').addEventListener('keydown', function (event) {
  if (event.key === 'Enter') {
    event.preventDefault();
    event.stopPropagation();
    ideFindNext('1');
  } else if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    doAction('idefindpanel', '0');
  }
});
document.getElementById('ideReplaceBox').addEventListener('input', ideScheduleReplaceText);

// 编辑区：编辑防抖推送 + 行号同步 + 光标行列刷新；Tab 插入空格
document.getElementById('ideText').addEventListener('input', function () {
  ideUpdateGutter();
  ideUpdateCaret();
  if (ideLoadedIndex < 0 || document.getElementById('ideText').readOnly) { return; }
  ideLocalDirty = true;
  ideSchedulePush();
});
document.getElementById('ideText').addEventListener('scroll', function () {
  document.getElementById('ideGutter').scrollTop = document.getElementById('ideText').scrollTop;
});
document.getElementById('ideText').addEventListener('keyup', ideUpdateCaret);
document.getElementById('ideText').addEventListener('click', ideUpdateCaret);
document.getElementById('ideText').addEventListener('keydown', function (event) {
  if (event.key === 'Tab') {
    event.preventDefault();
    const textarea = event.target;
    if (textarea.readOnly) { return; }
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    textarea.value = textarea.value.substring(0, start) + '    ' + textarea.value.substring(end);
    textarea.selectionStart = textarea.selectionEnd = start + 4;
    ideLocalDirty = true;
    ideUpdateGutter();
    ideSchedulePush();
  }
});

loadPermissions().then(initMode).then(refresh);
pollTimer = setInterval(refresh, 1500);
</script>
</body>
</html>
""";
}
