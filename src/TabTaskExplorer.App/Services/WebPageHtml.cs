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
  .connToolbar { padding:6px 2px; display:flex; gap:6px; flex-wrap:wrap; }
  .connForm { padding:8px 2px; display:grid; grid-template-columns:80px minmax(140px,1fr) 80px minmax(140px,1fr); gap:6px 8px; max-width:760px; align-items:center; }
  .connForm label { font-size:12px; opacity:.85; }
  .connForm input, .connForm select { border:1px solid #D5D5D5; border-radius:3px; padding:4px 6px; font:13px 'Segoe UI','Microsoft YaHei'; outline:none; background:#fff; color:inherit; min-width:0; }
  .connForm .wide { grid-column:span 3; }
  body.dark .connForm input, body.dark .connForm select { background:#202020; border-color:#3C3C3C; }
  .pop { position:absolute; top:100%; right:0; margin-top:3px; background:#fff; border:1px solid #DCDCDC; border-radius:6px; box-shadow:0 6px 18px rgba(0,0,0,.14); padding:4px; z-index:30; min-width:360px; max-height:320px; overflow:auto; }
  .pop .item { padding:5px 8px; border-radius:4px; cursor:pointer; font-size:12px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .pop .item:hover { background:#F0F6FF; }
  .pop .empty { padding:6px 8px; color:#808080; font-size:12px; }

  /* ===== 工作区：左侧导航 + 内容 + 详细信息窗格 ===== */
  .workarea { flex:1; display:flex; min-height:0; }
  .sidebar { width:200px; flex:none; border-right:1px solid #E9E9E9; overflow:auto; padding:6px 4px; }
  .navsec { display:flex; align-items:center; gap:6px; margin:8px 6px 4px; color:#2F6FD0; font-weight:500; font-size:12px; }
  .navsec.clickable { cursor:pointer; border-radius:4px; }
  .navsec.clickable:hover { background:#F5F7FA; }
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
  table { border-collapse:collapse; table-layout:fixed; }
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
  td.clip { max-width:200px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
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

  /* ===== 进程监控（与桌面 ProcessPanel 同构：调节行 + 走势图 + 详情区） ===== */
  .procCtrl { display:flex; align-items:center; gap:6px; flex-wrap:wrap; padding:6px 0; font-size:13px; }
  .procCtrl select, .procCtrl input { border:1px solid #D5D5D5; border-radius:3px; padding:3px 6px; font:13px 'Segoe UI','Microsoft YaHei'; outline:none; color:inherit; background:#fff; }
  .procCtrl select:disabled, .procCtrl input:disabled { color:#B0B0B0; background:#F5F5F5; }
  .procCtrl input { width:160px; }
  .trendLegend { display:flex; gap:16px; font-size:11px; color:#808080; padding:6px 0 2px; }
  .trendLegend i { display:inline-block; width:12px; height:4px; border-radius:2px; margin-right:4px; vertical-align:middle; }
  #trendCanvas { width:100%; height:78px; display:block; border:1px solid #E9E9E9; border-radius:4px; }
  .procDetail { border:1px solid #E9E9E9; border-radius:4px; background:#FAFAFA; padding:6px 8px; margin-top:6px; font:12px Consolas,'Microsoft YaHei'; white-space:pre-wrap; min-height:64px; max-height:96px; overflow:auto; color:#404040; }
  body.dark .procCtrl select, body.dark .procCtrl input { background:#202020; border-color:#3C3C3C; }
  body.dark .procDetail { background:#1B1B1B; border-color:#3C3C3C; color:#B8B8B8; }
  body.dark #trendCanvas { border-color:#3C3C3C; }

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
  .brow .reason { color:#E06C55; }
  .encodeCtrl { display:flex; align-items:center; gap:8px; padding:8px 12px; border-bottom:1px solid #E9E9E9; flex-wrap:wrap; font-size:13px; }
  .encodeCtrl select { border:1px solid #D5D5D5; border-radius:3px; padding:4px 6px; font:13px 'Segoe UI','Microsoft YaHei'; outline:none; color:inherit; background:#fff; }
  .encodeCtrl label { display:flex; align-items:center; gap:4px; cursor:pointer; }
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
  body.dark .bmItem:hover, body.dark .btn:hover, body.dark .tbtn:hover, body.dark .seg:hover, body.dark .navitem:hover, body.dark .tile:hover, body.dark .navsec.clickable:hover { background:#2E2E2E; }
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
  body.dark .encodeCtrl select { background:#202020; border-color:#3C3C3C; }
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
    <span class="btn" id="btnEncode" onclick="openEncode()">编码转换</span>
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

  <!-- 编码转换：文件名 / 文件夹名的批量编码转换（源编码 → 目标编码，支持递归子项） -->
  <div class="modal hidden" id="encode">
    <div class="panel small">
      <div class="panelHead">
        <div class="panelTitle">编码转换（共 <span id="encodeCount">0</span> 项）</div>
        <span class="btn" onclick="closeEncode()">关闭</span>
      </div>
      <div class="encodeCtrl">
        <span>源编码</span>
        <select id="encodeFrom" onchange="previewEncode()"></select>
        <span>目标编码</span>
        <select id="encodeTo" onchange="previewEncode()"></select>
        <label><input type="checkbox" id="encodeRecursive" onchange="previewEncode()">递归处理子项</label>
      </div>
      <div class="batchList" id="encodeList"></div>
      <div class="panelFoot">
        <span class="hint" id="encodeFoot">源编码 = 名字当前被误解的编码</span>
        <div class="spacer"></div>
        <span class="btn" onclick="applyEncode()">执行转换</span>
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
// 表头列宽拖动：拖出的列宽按表格键保存，1.5 秒轮询重绘后按此恢复（仅本次页面打开期间有效，不持久化）
let tableColWidths = {};
let colDrag = null;
let suppressClickUntil = 0;

function esc(value) {
  const div = document.createElement('div');
  div.textContent = value == null ? '' : value;
  return div.innerHTML;
}

function escAttr(value) {
  return esc(value).replace(/"/g, '&quot;').replace(/'/g, '&#39;');
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
  setGate('btnEncode', permissions.canCreateDelete);
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
  let body;
  if (argument !== undefined && argument !== null && argument !== '') {
    // 短参数走查询串；长参数（连接表单、批量计划）改走请求体，避免地址栏长度上限
    if (argument.length > 600) { body = argument; } else { params.arg = argument; }
  }
  try {
    const response = await fetch(url('/api/action', params), { method: 'POST', body: body });
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
function navOpen(target, selected) {
  // 二次点击已选中的导航项：回到导航菜单根目录（此电脑）
  if (selected) { return openPath(''); }
  return openPath(target);
}
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
  html += navSection('快捷访问', '&#xE735;', state.navPinned, false);
  html += navSection('此电脑', '&#xE7F8;', state.navPc, true);
  html += '<div class="navsec"><span class="ico">&#xE968;</span>网络</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'openconnections\')"><span class="ico">&#xE968;</span>连接管理</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'opensearch\')"><span class="ico">&#xE721;</span>全局搜索</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'opendisk\')"><span class="ico">&#xE9F4;</span>磁盘视图</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'openprocess\')"><span class="ico">&#xE9F5;</span>进程监控</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'openboard\')"><span class="ico">&#xE9D5;</span>任务看板</div>';
  document.getElementById('sidebar').innerHTML = html;
}

function navSection(title, glyph, items, clickable) {
  // 分组标题可点击时：导航当前文件标签到此电脑根视图（无文件标签时自动新建），与桌面端 OnThisPcHeaderClick 一致
  let html = '<div class="navsec' + (clickable ? ' clickable' : '') + '"'
    + (clickable ? ' onclick="openPath(\'\')" title="导航到此电脑"' : '')
    + '><span class="ico">' + glyph + '</span>' + title + '</div>';
  if (!items || items.length === 0) {
    return html;
  }
  for (const item of items) {
    html += '<div class="navitem' + (item.selected ? ' selected' : '') + '" onclick="navOpen('
      + JSON.stringify(item.path).replace(/"/g, '&quot;') + ',' + (item.selected ? 1 : 0) + ')">'
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
  searchBox.placeholder = isSearch ? '全局搜索（按回车执行）' : '搜索当前目录';
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
    + '|' + permissions.canCreateDelete + '|' + permissions.canModify + '|' + JSON.stringify(tab.rows)
    + '|' + JSON.stringify(Array.from(checked)) + '|' + tab.todo + '|' + tab.inProgress + '|' + tab.done
    + '|' + JSON.stringify(tab.connections) + '|' + tab.selectedConnectionId + '|' + tab.summaryText
    + '|' + tab.resourceText + '|' + JSON.stringify(tab.processRows)
    + '|' + tab.detailText + '|' + tab.affinityInput + '|' + tab.priorityIndex
    + '|' + tab.detailSupported + '|' + tab.trendVersion
    + '|' + tab.sortField + '|' + tab.sortAscending;
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
  } else if (tab.tabType === 'process') {
    html = renderProcess(tab);
  } else {
    html = renderGenericView(tab);
  }
  if (tab.tabType !== 'process' && tab.totalRows && tab.totalRows > tab.rows.length) {
    html += '<div class="hint">仅显示前 ' + tab.rows.length + ' 项，共 ' + tab.totalRows + ' 项</div>';
  }
  // 摘要栏：与桌面一致，显示在内容底部（桌面 FileListPanel 底部摘要）
  if (tab.summaryText) {
    html += '<div class="hint summary">' + esc(tab.summaryText) + '</div>';
  }
  // 进程页：重绘前记录正在输入的控件，重绘后恢复焦点与内容（两秒一次的采样刷新不打断输入）
  let procFocusId = '';
  let procFocusValue = '';
  const activeElement = document.activeElement;
  if (activeElement && (activeElement.id === 'procPriority' || activeElement.id === 'procAffinity')) {
    procFocusId = activeElement.id;
    procFocusValue = activeElement.value;
  }
  document.getElementById('contentMain').innerHTML = html;
  if (tab.tabType === 'process') {
    syncProcessControls(tab, procFocusId, procFocusValue);
    drawProcessTrend(tab);
  }
  applyFocusHighlight();
}

function sortArrow(tab, field) {
  if (tab.sortField !== field) { return ''; }
  return '<span class="arrow">' + (tab.sortAscending ? '&#9650;' : '&#9660;') + '</span>';
}

// ======== 表头列宽（默认按各视图默认像素列宽渲染，总宽小于容器时右侧留白；拖动后沿用拖出的列宽） ========
function tableWidthsFor(key, defaults) {
  const saved = tableColWidths[key];
  if (saved && saved.length === defaults.length) { return saved.slice(); }
  return defaults.slice();
}

function tableOpenHtml(key, defaults) {
  const widths = tableWidthsFor(key, defaults);
  let total = 0;
  let cols = '';
  for (let i = 0; i < widths.length; i++) {
    total += widths[i];
    cols += '<col style="width:' + widths[i] + 'px">';
  }
  //总宽小于容器时表格停在总宽、右侧留白；大于容器时按 max-width 收缩（与桌面端星号列收缩一致）
  return '<table data-colkey="' + key + '" style="width:' + total + 'px;max-width:100%"><colgroup>' + cols + '</colgroup>';
}

function renderTable(tab) {
  const isSearch = tab.tabType === 'search';
  const mode = tab.viewMode || 'Details';
  const colKey = (isSearch ? 'search' : 'file') + ':' + mode;
  let defaults;
  let columns = '';
  if (mode === 'List') {
    defaults = [34, 560];
    columns = '';
  } else if (mode === 'Content') {
    defaults = [34, 560, 140];
    columns = '<th>类型</th>';
  } else if (isSearch) {
    // 全局搜索与桌面一致只有名称 / 修改日期 / 大小三列，不展示类型列
    defaults = [34, 560, 140, 110];
    columns = '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Modified\')">修改日期' + sortArrow(tab, 'Modified') + '</th>'
      + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Size\')">大小' + sortArrow(tab, 'Size') + '</th>';
  } else {
    defaults = [34, 560, 140, 130, 110];
    columns = '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Modified\')">修改日期' + sortArrow(tab, 'Modified') + '</th>'
      + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Type\')">类型' + sortArrow(tab, 'Type') + '</th>'
      + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Size\')">大小' + sortArrow(tab, 'Size') + '</th>';
  }
  let head = '<tr><th></th><th class="sortable" onclick="clickBtn(null,\'sort\',\'Name\')">名称'
    + sortArrow(tab, 'Name') + '</th>' + columns + '</tr>';

  let html = tableOpenHtml(colKey, defaults) + head;
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
      html += '<td>' + esc(row.modified) + '</td>';
      if (!isSearch) { html += '<td>' + esc(row.type) + '</td>'; }
      html += '<td>' + esc(row.size) + '</td>';
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
  let html = tableOpenHtml('disk', [440, 100, 260, 120, 80, 90])
    + '<tr><th class="sortable" onclick="clickBtn(null,\'sort\',\'Name\')">名称' + sortArrow(tab, 'Name') + '</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Type\')">类型' + sortArrow(tab, 'Type') + '</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Size\')">大小' + sortArrow(tab, 'Size') + '</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Free\')">剩余' + sortArrow(tab, 'Free') + '</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Usage\')">使用率' + sortArrow(tab, 'Usage') + '</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'FileSystem\')">文件系统' + sortArrow(tab, 'FileSystem') + '</th></tr>';
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
      // 小三角与文件名点击效果完全一致：同一 diskNav 处理，点击即展开或折叠
      html += '<span class="ico" style="color:#E8A317;cursor:pointer" onclick="diskNav(' + target + ')">'
        + (row.expanded ? '&#9662;' : '&#9656;') + '</span>'
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

// 进程监控：与桌面 ProcessPanel 同构（操作按钮 + 调节行 + 资源摘要 + 进程表格 + 走势图 + 详情区）
function renderProcess(tab) {
  const rows = tab.processRows || [];
  const canAdjust = permissions.canModify && tab.detailSupported;
  let html = '<div class="procBar">'
    + '<button class="btn" onclick="clickBtn(null,\'processrefresh\')">刷新</button> '
    + '<button class="btn' + (permissions.canModify ? '' : ' disabled') + '" onclick="processKill()">结束进程</button></div>';
  // 调节行：优先级下拉 + CPU 亲和性输入（与桌面调节行同构，非 Windows 平台或未放行修改权限时整体灰化）
  html += '<div class="procCtrl"><span>优先级</span><select id="procPriority"' + (canAdjust ? '' : ' disabled') + '>';
  const priorityNames = ['低', '低于标准', '标准', '高于标准', '高', '实时'];
  const priorityValue = (tab.priorityIndex >= 0 && tab.priorityIndex < priorityNames.length) ? tab.priorityIndex : 2;
  for (let priorityIndex = 0; priorityIndex < priorityNames.length; priorityIndex++) {
    html += '<option value="' + priorityIndex + '"' + (priorityIndex === priorityValue ? ' selected' : '') + '>'
      + priorityNames[priorityIndex] + '</option>';
  }
  html += '</select><button class="btn' + (canAdjust ? '' : ' disabled') + '" onclick="processApplyPriority()">应用</button>'
    + '<span>CPU 亲和性</span>'
    + '<input id="procAffinity" type="text" placeholder="核心编号，如 0,1,2" value="' + escAttr(tab.affinityInput || '') + '"'
    + (canAdjust ? '' : ' disabled') + '>'
    + '<button class="btn' + (canAdjust ? '' : ' disabled') + '" onclick="processApplyAffinity()">应用</button></div>';
  if (tab.resourceText) {
    html += '<div class="hint">' + esc(tab.resourceText) + '</div>';
  }
  html += tableOpenHtml('process', [70, 170, 130, 64, 54, 58, 72, 96, 110, 132, 320, 220])
    + '<tr><th class="sortable" onclick="clickBtn(null,\'sort\',\'Cpu\')">处理器' + sortArrow(tab, 'Cpu') + '</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Name\')">进程名称（树）' + sortArrow(tab, 'Name') + '</th>'
    + '<th>用户名</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'ProcessId\')">编号' + sortArrow(tab, 'ProcessId') + '</th>'
    + '<th>线程</th>'
    + '<th>句柄</th>'
    + '<th>优先级</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'Memory\')">内存' + sortArrow(tab, 'Memory') + '</th>'
    + '<th>I/O 读写</th>'
    + '<th>启动时间</th>'
    + '<th>启动命令行</th>'
    + '<th class="sortable" onclick="clickBtn(null,\'sort\',\'WindowTitle\')">窗口标题' + sortArrow(tab, 'WindowTitle') + '</th></tr>';
  if (rows.length === 0) {
    html += '<tr><td class="hint" colspan="12">暂无进程数据，正在刷新……</td></tr>';
  }
  let rowIndex = 0;
  for (const row of rows) {
    // 单击选中进程（与桌面 ListBox 选中一致），选中后可点「结束进程」与调节项
    const displayName = (row.indentText || '') + row.name;
    html += '<tr class="row' + (row.isSelected ? ' checked' : '') + '" data-i="' + rowIndex + '"'
      + ' onclick="processSelect(' + row.processId + ')">'
      + '<td>' + esc(row.cpuText || '—') + '</td>'
      + '<td class="name"><span class="text" title="' + escAttr(displayName) + '">' + esc(displayName) + '</span></td>'
      + '<td class="clip" title="' + escAttr(row.userNameText || '') + '">' + esc(row.userNameText || '—') + '</td>'
      + '<td>' + row.processId + '</td>'
      + '<td>' + esc(row.threadText || '—') + '</td>'
      + '<td>' + esc(row.handleText || '—') + '</td>'
      + '<td>' + esc(row.priorityText || '—') + '</td>'
      + '<td>' + esc(row.memoryText) + '</td>'
      + '<td>' + esc(row.ioText || '—') + '</td>'
      + '<td>' + esc(row.startTimeText || '—') + '</td>'
      + '<td class="clip" title="' + escAttr(row.commandLineText || '') + '">' + esc(row.commandLineText || '—') + '</td>'
      + '<td class="clip" title="' + escAttr(row.windowTitle) + '">' + esc(row.windowTitle) + '</td></tr>';
    rowIndex++;
  }
  html += '</table>';
  // 走势图：与桌面 ProcessTrendChart 同构（三条折线，最近 60 次采样，纵轴 0 到 100）
  html += '<div class="trendLegend">'
    + '<span><i style="background:#2E7D32"></i>系统处理器</span>'
    + '<span><i style="background:#7E57C2"></i>系统内存</span>'
    + '<span><i style="background:#EF6C00"></i>选中进程处理器</span></div>'
    + '<canvas id="trendCanvas" height="78"></canvas>';
  // 详情区：可执行文件、启动命令行、启动时间、进程所有者、优先级、CPU 亲和性（多行只读）
  html += '<div class="procDetail">' + esc(tab.detailText || '') + '</div>';
  return html;
}

// 进程页重绘后回填调节项：先恢复服务端最新值，再恢复用户正在输入的焦点与内容（采样刷新不打断输入）
function syncProcessControls(tab, focusId, focusValue) {
  const priorityBox = document.getElementById('procPriority');
  const affinityBox = document.getElementById('procAffinity');
  if (priorityBox) {
    priorityBox.value = String((tab.priorityIndex >= 0) ? tab.priorityIndex : 2);
  }
  if (affinityBox) {
    affinityBox.value = tab.affinityInput || '';
  }
  if (focusId) {
    const focused = document.getElementById(focusId);
    if (focused) {
      focused.value = focusValue;
      focused.focus();
    }
  }
}

// 走势图画布：背景 + 0/50/100% 网格 + 三条折线（与桌面 ProcessTrendChart.Render 同构）
function drawProcessTrend(tab) {
  const canvas = document.getElementById('trendCanvas');
  if (!canvas || !canvas.getContext) { return; }
  const width = Math.max(canvas.clientWidth, 80);
  const height = 78;
  if (canvas.width !== width) { canvas.width = width; }
  canvas.height = height;
  const context = canvas.getContext('2d');
  const dark = document.body.classList.contains('dark');
  context.clearRect(0, 0, width, height);
  context.fillStyle = dark ? 'rgba(255,255,255,0.05)' : 'rgba(128,128,128,0.09)';
  context.fillRect(0, 0, width, height);
  context.strokeStyle = dark ? 'rgba(255,255,255,0.16)' : 'rgba(128,128,128,0.30)';
  context.lineWidth = 1;
  const gridRows = [1, height / 2, height - 1];
  for (let gridIndex = 0; gridIndex < gridRows.length; gridIndex++) {
    context.beginPath();
    context.moveTo(0, gridRows[gridIndex]);
    context.lineTo(width, gridRows[gridIndex]);
    context.stroke();
  }
  drawTrendSeries(context, width, height, tab.systemCpuTrend, '#2E7D32');
  drawTrendSeries(context, width, height, tab.systemMemoryTrend, '#7E57C2');
  drawTrendSeries(context, width, height, tab.selectedCpuTrend, '#EF6C00');
}

// 画一条折线：横轴按固定 60 点铺满（历史未满时占左侧），纵轴按 0 到 100 映射
function drawTrendSeries(context, width, height, values, color) {
  if (!values || values.length < 2) { return; }
  context.strokeStyle = color;
  context.lineWidth = 1.5;
  context.beginPath();
  for (let index = 0; index < values.length; index++) {
    let percent = values[index];
    if (percent < 0) { percent = 0; }
    if (percent > 100) { percent = 100; }
    const x = width * index / 59;
    const y = height - 2 - (percent / 100) * (height - 4);
    if (index === 0) { context.moveTo(x, y); } else { context.lineTo(x, y); }
  }
  context.stroke();
}

// 取当前选中的进程行；未选中时返回 null
function processSelectedRow() {
  if (!currentTab || currentTab.tabType !== 'process') { return null; }
  const rows = currentTab.processRows || [];
  for (const row of rows) {
    if (row.isSelected) { return row; }
  }
  return null;
}

function processSelect(processId) {
  if (currentTab && currentTab.tabType === 'process') {
    doAction('processselect', processId);
  }
}

function processKill() {
  if (!permissions.canModify) {
    flash('该端口未放行修改权限');
    return;
  }
  const target = processSelectedRow();
  if (!target) {
    flash('请先在列表中选择要结束的进程');
    return;
  }
  doAction('processkill', target.processId);
}

// 应用优先级：把选中进程的编号与下拉框索引回传（如 1234,3）
function processApplyPriority() {
  if (!permissions.canModify) {
    flash('该端口未放行修改权限');
    return;
  }
  if (!currentTab || currentTab.tabType !== 'process' || !currentTab.detailSupported) {
    flash('当前平台不支持优先级调节');
    return;
  }
  const target = processSelectedRow();
  if (!target) {
    flash('请先在列表中选择要调节的进程');
    return;
  }
  const priorityBox = document.getElementById('procPriority');
  doAction('processpriority', target.processId + ',' + (priorityBox ? priorityBox.value : '2'));
}

// 应用 CPU 亲和性：把选中进程的编号与核心编号列表回传（如 1234,0,1,2）
function processApplyAffinity() {
  if (!permissions.canModify) {
    flash('该端口未放行修改权限');
    return;
  }
  if (!currentTab || currentTab.tabType !== 'process' || !currentTab.detailSupported) {
    flash('当前平台不支持亲和性调节');
    return;
  }
  const target = processSelectedRow();
  if (!target) {
    flash('请先在列表中选择要调节的进程');
    return;
  }
  const affinityBox = document.getElementById('procAffinity');
  doAction('processaffinity', target.processId + ',' + (affinityBox ? affinityBox.value : ''));
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

function renderGenericView(tab) {
  // 通用降级视图：桌面端新增标签类型后即使网页端还没有专用渲染器，也能展示标题与关键摘要
  let html = '<div class="hint">「' + esc(tab.title) + '」暂时没有专用网页界面，以下为该视图的摘要信息。</div>';
  const lines = [];
  if (tab.currentPath) { lines.push('当前路径：' + tab.currentPath); }
  if (tab.searchText) { lines.push('关键字：' + tab.searchText); }
  if (tab.resourceText) { lines.push('资源摘要：' + tab.resourceText); }
  if (tab.detailText) { lines.push('详情：' + tab.detailText); }
  if (tab.totalRows) { lines.push('条目数：' + tab.totalRows); }
  for (const line of lines) {
    html += '<div class="hint">' + esc(line) + '</div>';
  }
  return html;
}

let connEditingId = '';

function renderConnections(tab) {
  const items = tab.connections || [];
  let html = '<div class="connToolbar">'
    + '<span class="btn" onclick="connNew()">新建连接</span>'
    + '<span class="btn" onclick="connSave()">保存连接</span>'
    + '<span class="btn" onclick="connDelete()">删除连接</span>'
    + '<span class="btn" onclick="connTest()">测试连接</span>'
    + '<span class="btn" onclick="connImport()">一键导入已安装配置</span>'
    + '<span class="btn" onclick="connOpenFolder()">打开远程文件夹</span>'
    + '<span class="btn" onclick="connOpenTerminal()">打开终端</span>'
    + '</div>';
  html += tableOpenHtml('connections', [220, 200, 460]) + '<tr><th>名称</th><th>分组</th><th>摘要</th></tr>';
  if (items.length === 0) {
    html += '<tr><td class="hint" colspan="3">暂无连接，可点击「新建连接」或「一键导入已安装配置」</td></tr>';
  }
  for (const item of items) {
    html += '<tr class="connRow' + (item.selected ? ' selected' : '') + '"'
      + ' onclick="clickBtn(null,\'connselect\',\'' + escAttr(item.id) + '\')">'
      + '<td>' + esc(item.name) + '</td><td>' + esc(item.group) + '</td><td>' + esc(item.summary) + '</td></tr>';
  }
  html += '</table>';
  const selected = items.find(function (item) { return item.selected; }) || null;
  connEditingId = selected ? selected.id : '';
  const value = selected || {
    id: '', name: '', group: '', protocol: 'SSH', host: '', port: 0, username: '',
    authType: 'Password', privateKeyPath: '', keepAliveSeconds: 15
  };
  html += '<div class="connForm">'
    + '<label>名称</label><input id="connName" class="wide" value="' + escAttr(value.name) + '">'
    + '<label>分组</label><input id="connGroup" value="' + escAttr(value.group) + '">'
    + '<label>协议</label><select id="connProtocol">'
    + '<option value="SSH"' + (value.protocol === 'SSH' ? ' selected' : '') + '>SSH</option>'
    + '<option value="FTP"' + (value.protocol === 'FTP' ? ' selected' : '') + '>FTP</option></select>'
    + '<label>主机</label><input id="connHost" value="' + escAttr(value.host) + '">'
    + '<label>端口</label><input id="connPort" value="' + (value.port > 0 ? value.port : '') + '" placeholder="留空为默认端口">'
    + '<label>用户名</label><input id="connUser" value="' + escAttr(value.username) + '">'
    + '<label>认证</label><select id="connAuth">'
    + '<option value="Password"' + (value.authType === 'Password' ? ' selected' : '') + '>密码</option>'
    + '<option value="PrivateKey"' + (value.authType === 'PrivateKey' ? ' selected' : '') + '>私钥</option></select>'
    + '<label>私钥路径</label><input id="connKey" class="wide" value="' + escAttr(value.privateKeyPath) + '">'
    + '<label>保活秒数</label><input id="connKeep" value="' + escAttr(String(value.keepAliveSeconds)) + '">'
    + '<label>密码</label><input id="connSecret" class="wide" type="password" placeholder="留空保持原密码">'
    + '</div>'
    + '<div class="hint">Xshell / Xftp 导入的连接不含密码与私钥路径，请在上方补填后保存；打开远程文件夹会以新标签页浏览远端目录。</div>';
  return html;
}

function connNew() {
  connEditingId = '';
  const textIds = ['connName', 'connGroup', 'connHost', 'connPort', 'connUser', 'connKey', 'connKeep', 'connSecret'];
  for (const id of textIds) {
    const element = document.getElementById(id);
    if (element) { element.value = ''; }
  }
  const protocol = document.getElementById('connProtocol');
  if (protocol) { protocol.value = 'SSH'; }
  const auth = document.getElementById('connAuth');
  if (auth) { auth.value = 'Password'; }
  const nameBox = document.getElementById('connName');
  if (nameBox) { nameBox.focus(); }
  flash('已切换到新建连接，填写完成后再点「保存连接」');
}

function connFieldValues() {
  function valueOf(id) {
    const element = document.getElementById(id);
    return element ? element.value : '';
  }
  return [
    valueOf('connName'), valueOf('connGroup'), valueOf('connProtocol'), valueOf('connHost'),
    valueOf('connPort'), valueOf('connUser'), valueOf('connAuth'), valueOf('connKey'),
    valueOf('connKeep'), valueOf('connSecret')
  ];
}

function connSave() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限，无法保存连接'); return; }
  const values = connFieldValues();
  doAction('connsave', [connEditingId].concat(values).join('\n'));
}

function connDelete() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限，无法删除连接'); return; }
  if (!connEditingId) { flash('请先在列表中选中要删除的连接'); return; }
  doAction('conndelete', connEditingId);
}

function connImport() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限，无法导入连接'); return; }
  doAction('connimport');
}

function connTest() {
  if (!connEditingId) { flash('请先在列表中选中要测试的连接'); return; }
  flash('正在测试连接，请稍候……');
  doAction('conntest', connEditingId);
}

function connOpenFolder() {
  if (!connEditingId) { flash('请先在列表中选中要打开的连接'); return; }
  doAction('connopenfolder', connEditingId);
}

function connOpenTerminal() {
  doAction('connopenterminal', connEditingId);
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
  // 压缩包：走 openPath 由服务端新开预览标签（与桌面端双击压缩包一致）
  if (/\.(zip|7z|rar|tar|gz|bz2|xz|zst|tgz|tbz2?)$/i.test(row.fullPath)) {
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

// ======== 表头列宽拖动（与桌面端规则一致：中间列此消彼长，末列只吃右侧留白，最小列宽 40 像素） ========
function onTableColResizeDown(event) {
  if (event.button !== 0) { return; }
  const headerCell = event.target.closest('th');
  if (!headerCell) { return; }
  const table = headerCell.closest('table');
  if (!table || !table.hasAttribute('data-colkey')) { return; }
  const headRow = table.rows[0];
  if (!headRow || !headRow.contains(headerCell)) { return; }
  const cellBounds = headerCell.getBoundingClientRect();
  if (event.clientX <= cellBounds.right - 7) {
    // 不在表头右缘 7 像素的拖动区内：不启动列宽拖动，交由原有点击（排序等）处理
    return;
  }
  const cells = Array.prototype.slice.call(headRow.cells);
  const columnIndex = cells.indexOf(headerCell);
  const columns = table.querySelectorAll('colgroup col');
  if (columnIndex < 0 || columnIndex >= columns.length) {
    return;
  }
  const startWidths = [];
  for (let i = 0; i < cells.length; i++) { startWidths.push(cells[i].getBoundingClientRect().width); }
  const parent = table.parentElement;
  let maxTotal = 100000;
  if (parent) {
    const parentStyle = window.getComputedStyle(parent);
    maxTotal = parent.clientWidth - parseFloat(parentStyle.paddingLeft || '0') - parseFloat(parentStyle.paddingRight || '0');
  }
  colDrag = {
    table: table,
    key: table.getAttribute('data-colkey'),
    columns: columns,
    index: columnIndex,
    startX: event.clientX,
    startWidths: startWidths,
    maxTotal: maxTotal,
    moved: false
  };
  document.body.style.cursor = 'col-resize';
  event.preventDefault();
}

function onTableColResizeMove(event) {
  if (!colDrag) { return; }
  if (!document.contains(colDrag.table)) {
    // 轮询重绘替换了表格：改挂到同键的新表格上继续拖动（列宽状态已随重绘带过去）
    const liveTable = document.querySelector('table[data-colkey="' + colDrag.key + '"]');
    if (!liveTable) { onTableColResizeAbort(); return; }
    colDrag.table = liveTable;
    colDrag.columns = liveTable.querySelectorAll('colgroup col');
  }
  const delta = event.clientX - colDrag.startX;
  const widths = colDrag.startWidths.slice();
  const index = colDrag.index;
  if (index === widths.length - 1) {
    // 末列：只允许吃掉右侧留白，不超过容器剩余宽度，不低于 40 像素
    let otherTotal = 0;
    for (let i = 0; i < widths.length - 1; i++) { otherTotal += widths[i]; }
    let width = widths[index] + delta;
    if (width > colDrag.maxTotal - otherTotal) { width = colDrag.maxTotal - otherTotal; }
    if (width < 40) { width = 40; }
    widths[index] = width;
  } else {
    // 中间列：本列变宽多少，右邻列等量变窄，总宽与右侧留白保持不变；右邻列碰到 40 像素下限时回退，保证两列之和守恒
    let width = widths[index] + delta;
    if (width < 40) { width = 40; }
    const changed = width - widths[index];
    widths[index] = width;
    widths[index + 1] = colDrag.startWidths[index + 1] - changed;
    if (widths[index + 1] < 40) {
      widths[index + 1] = 40;
      widths[index] = colDrag.startWidths[index] + colDrag.startWidths[index + 1] - 40;
    }
  }
  let total = 0;
  const saved = [];
  for (let i = 0; i < widths.length; i++) {
    const rounded = Math.round(widths[i]);
    colDrag.columns[i].style.width = rounded + 'px';
    total += rounded;
    saved.push(rounded);
  }
  colDrag.table.style.width = total + 'px';
  tableColWidths[colDrag.key] = saved;
  colDrag.moved = true;
}

function onTableColResizeAbort() {
  colDrag = null;
  document.body.style.cursor = '';
}

function onTableColResizeUp(event) {
  if (!colDrag) { return; }
  const key = colDrag.key;
  const moved = colDrag.moved;
  const widths = tableColWidths[key];
  colDrag = null;
  document.body.style.cursor = '';
  if (!moved || !widths) {
    // 没有真正拖动：保持原有点击行为（表头排序等）
    return;
  }
  // 拖动结束后的这次点击不再冒泡，防止误触表头排序
  suppressClickUntil = performance.now() + 250;
  let widthText = '';
  for (let i = 0; i < widths.length; i++) { widthText += (i === 0 ? '' : ',') + widths[i]; }
  doAction('colresize', key + '=' + widthText);
}

function suppressResizeClick(event) {
  if (performance.now() < suppressClickUntil) {
    event.preventDefault();
    event.stopPropagation();
  }
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
    + (canEdit && row.isDirectory ? '<div class="item" data-ctx="pasteto">粘贴到该文件夹</div>' : '')
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
    else if (kind === 'pasteto') { doAction('pasteto', row.fullPath); }
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

// ======== 编码转换（文件名 / 文件夹名批量编码转换） ========

// 编码表与内核 FileNameEncodingConverter.GetSupportedEncodings 保持一致：[显示名, 代码页]
const ENCODE_CHOICES = [
  ['UTF-8', 65001], ['UTF-16 LE', 1200], ['UTF-16 BE', 1201], ['GBK', 936], ['GB18030', 54936],
  ['Big5', 950], ['Latin-1', 28591], ['Shift_JIS', 932], ['EUC-JP', 51932], ['EUC-KR', 949],
  ['KOI8-R', 20866], ['Windows-1251', 1251], ['Windows-1252', 1252], ['ISO-8859-2', 28592]
];
let encodePaths = [];
let encodeRows = [];

function encodeOptionsHtml(selectedPage) {
  let html = '';
  for (const choice of ENCODE_CHOICES) {
    html += '<option value="' + choice[1] + '"' + (choice[1] === selectedPage ? ' selected' : '') + '>'
      + esc(choice[0]) + '</option>';
  }
  return html;
}

async function openEncode() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限'); return; }
  if (checked.size === 0) { flash('请先勾选要转换编码的条目'); return; }
  encodePaths = [];
  for (const row of currentRows) {
    if (checked.has(row.fullPath)) { encodePaths.push(row.fullPath); }
  }
  document.getElementById('encodeFrom').innerHTML = encodeOptionsHtml(936);
  document.getElementById('encodeTo').innerHTML = encodeOptionsHtml(65001);
  document.getElementById('encodeRecursive').checked = false;
  encodeRows = [];
  renderEncodePreview([]);
  document.getElementById('encode').classList.remove('hidden');
  await previewEncode();
}

// 两步只读预览：向服务端要转换计划（不改动任何文件），仅在页面只读渲染
async function previewEncode() {
  if (encodePaths.length === 0) { return; }
  const fromPage = document.getElementById('encodeFrom').value;
  const toPage = document.getElementById('encodeTo').value;
  const recursive = document.getElementById('encodeRecursive').checked ? '1' : '0';
  let body = fromPage + '\t' + toPage + '\t' + recursive + '\n';
  for (const path of encodePaths) { body += path + '\n'; }
  try {
    const response = await fetch(url('/api/action', { a: 'encodepreview' }), { method: 'POST', body: body });
    const text = await response.text();
    if (!response.ok) { flash(text || '编码转换预览失败'); return; }
    const rows = [];
    for (const line of text.split('\n')) {
      const clean = line.replace(/\r$/, '');
      if (!clean) { continue; }
      const parts = clean.split('\t');
      if (parts.length < 3) { continue; }
      rows.push({ path: parts[0], name: parts[1], reason: parts.slice(2).join('\t') });
    }
    if (rows.length === 0) { flash(text || '没有可预览的转换项'); return; }
    encodeRows = rows;
    renderEncodePreview(rows);
  } catch (error) {
    flash('编码转换预览失败：' + error);
  }
}

function renderEncodePreview(rows) {
  let html = '';
  let executable = 0;
  let failed = 0;
  for (const row of rows) {
    const oldName = row.path.split('\\').pop().split('/').pop();
    const changed = row.name !== oldName;
    if (row.reason) { failed++; } else if (changed) { executable++; }
    const reasonText = row.reason ? '（' + esc(row.reason) + '）' : '';
    html += '<div class="brow"><span class="old" title="' + esc(row.path) + '">' + esc(row.path) + '</span>'
      + '<span>&rsaquo;</span><span class="reason" style="flex:1">' + esc(row.name) + reasonText + '</span></div>';
  }
  document.getElementById('encodeList').innerHTML = html || '<div class="hint">没有可转换的条目</div>';
  document.getElementById('encodeCount').textContent = rows.length;
  document.getElementById('encodeFoot').textContent =
    '共 ' + rows.length + ' 项，可执行 ' + executable + ' 项，无法转换 ' + failed + ' 项';
}

function closeEncode() {
  document.getElementById('encode').classList.add('hidden');
}

async function applyEncode() {
  let plan = '';
  let changed = 0;
  for (const row of encodeRows) {
    if (row.reason) { continue; }
    const oldName = row.path.split('\\').pop().split('/').pop();
    if (row.name === oldName) { continue; }
    plan += row.path + '\t' + row.name + '\n';
    changed++;
  }
  closeEncode();
  if (changed === 0) { flash('没有可执行的转换项'); return; }
  try {
    const response = await fetch(url('/api/action', { a: 'encodeapply' }), { method: 'POST', body: plan });
    const text = await response.text();
    flash(response.ok ? (text || '编码转换已提交') : (text || '编码转换失败'));
  } catch (error) {
    flash('编码转换提交失败：' + error);
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
    // 全局搜索与桌面一致：只在回车或搜索按钮提交时执行，输入过程中不触发搜索
    if (currentTab.tabType === 'search') { return; }
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

document.getElementById('addrInput').addEventListener('blur', function () {
  // 失焦即退出编辑模式：恢复面包屑显示，使路径框继续跟随后续导航更新
  exitAddressEdit();
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
// 表头列宽拖动：按下判定拖动区，移动改列宽，松开记录日志并吞掉随后的点击
document.getElementById('content').addEventListener('mousedown', onTableColResizeDown);
document.addEventListener('mousemove', onTableColResizeMove);
document.addEventListener('mouseup', onTableColResizeUp);
document.addEventListener('click', suppressResizeClick, true);
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
