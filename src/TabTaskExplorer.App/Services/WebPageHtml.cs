namespace TabTaskExplorer.App.Services;

/// <summary>
/// 网页端诊断回退页：仅在 Assets/web 下的静态资源缺失、无法嵌入时使用。
/// 正式页面（index.html + app.css + core/views/panels/actions/ide/boot）由 WebStaticAssets 提供；
/// 本文件不再维护第二份完整界面、样式或业务脚本，只保留与静态页同名的入口骨架和诊断信息。
/// 端口占位符 __PORT__ 由服务在响应时替换为实际端口。
/// </summary>
public static class WebPageHtml
{
    /// <summary>
    /// 诊断页：结构入口名称与顺序和 Assets/web/index.html 一致，便于定位缺失资源；
    /// 颜色取自与 tokens.css 同源的 TTE-TOKENS 块（由生成器同步），不在此维护业务逻辑。
    /// </summary>
    public const string Content = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>TabTaskExplorer 网页端 · 静态资源诊断</title>
<style>
  /* ===== 回退页最小样式：只保证结构可读与深浅色一致 ===== */
  /* TTE-TOKENS-BEGIN */
  /* 由 tools/gen-design-tokens.ps1 从 design/tokens/ui-tokens.json 生成，请勿手工编辑。 */
  :root {
    --tte-chrome: #F1F1F1;
    --tte-content: #FFFFFF;
    --tte-separator: #EBEBEC;
    --tte-border: #E9E9E9;
    --tte-text-primary: #1C1C1C;
    --tte-text-muted: #808080;
    --tte-accent: #4A8FD0;
    --tte-row-hover: #F5F7FA;
    --tte-row-selected: #DCE9F5;
    --tte-row-selected-text: #4A8FD0;
    --tte-tab-hover: #E4E4E4;
    --tte-tab-active: #FFFFFF;
    --tte-bookmark-bar: #FFFFFF;
    --tte-input-border: #DCDDDE;
    --tte-input-surface: #FFFFFF;
    --tte-toolbar-text: #808080;
    --tte-status-bar: #F1F1F1;
    --tte-menu-surface: #F1F1F1;
    --tte-surface-raised: #FFFFFF;
    --tte-overlay-scrim: #00000073;
    --tte-row-selected-border: #BBD7F5;
    --tte-on-accent: #FFFFFF;
    --tte-danger: #D9534F;
    --tte-warning: #E8A317;
    --tte-success: #3FA45B;
    --tte-focus-ring: #4A8FD0;
  }

  body.dark {
    --tte-chrome: #212121;
    --tte-content: #191919;
    --tte-separator: #515151;
    --tte-border: #323232;
    --tte-text-primary: #E8E8E8;
    --tte-text-muted: #808080;
    --tte-accent: #4CC2FF;
    --tte-row-hover: #2E2E2E;
    --tte-row-selected: #37373D;
    --tte-row-selected-text: #4CC2FF;
    --tte-tab-hover: #2E2E2E;
    --tte-tab-active: #191919;
    --tte-bookmark-bar: #3B3B3B;
    --tte-input-border: #525252;
    --tte-input-surface: #202020;
    --tte-toolbar-text: #FFFFFF;
    --tte-status-bar: #212121;
    --tte-menu-surface: #212121;
    --tte-surface-raised: #242424;
    --tte-overlay-scrim: #00000073;
    --tte-row-selected-border: #3D5A78;
    --tte-on-accent: #FFFFFF;
    --tte-danger: #FF6B6B;
    --tte-warning: #E8A317;
    --tte-success: #3FA45B;
    --tte-focus-ring: #4CC2FF;
  }

  /* TTE-TOKENS-END */
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font:var(--tte-font-size-body) 'Segoe UI','Microsoft YaHei',sans-serif; color:var(--tte-text-primary); background:var(--tte-content); height:100vh; display:flex; flex-direction:column; }
  .hidden { display:none !important; }
  .spacer { margin-left:auto; }
  .titlebar { height:var(--tte-title-bar-height); background:var(--tte-chrome); border-bottom:var(--tte-border-width) solid var(--tte-separator); display:flex; align-items:center; padding:0 var(--tte-spacing-large); gap:var(--tte-spacing-medium); flex:none; } .titlebar .logo { width:22px; height:22px; border-radius:var(--tte-radius-small); background:var(--tte-accent); } .titlebar .seg { padding:var(--tte-spacing-xsmall) var(--tte-spacing-medium); }
  .bookmarkbar { min-height:var(--tte-bookmark-bar-height); background:var(--tte-bookmark-bar); border-bottom:var(--tte-border-width) solid var(--tte-border); padding:var(--tte-bookmark-bar-padding); } .toolbar { min-height:var(--tte-toolbar-height); border-bottom:var(--tte-border-width) solid var(--tte-border); padding:var(--tte-toolbar-padding); display:flex; gap:var(--tte-spacing-xsmall); flex-wrap:wrap; } .navrow { min-height:var(--tte-nav-row-height); border-bottom:var(--tte-border-width) solid var(--tte-border); padding:var(--tte-spacing-small) var(--tte-spacing-large); display:flex; gap:var(--tte-spacing-medium); align-items:center; }
  .addr { flex:1; min-height:var(--tte-address-bar-height); border:var(--tte-border-width) solid var(--tte-input-border); border-radius:var(--tte-radius-small); padding:0 var(--tte-spacing-medium); display:flex; align-items:center; color:var(--tte-text-muted); } .workarea { flex:1; display:flex; min-height:0; } .sidebar { width:var(--tte-sidebar-width); border-right:var(--tte-border-width) solid var(--tte-border); padding:var(--tte-spacing-medium); overflow:auto; }
  .navsec { color:var(--tte-accent); font-size:var(--tte-font-size-small); margin:var(--tte-spacing-large) 0 var(--tte-spacing-small); } .navitem { padding:var(--tte-spacing-small); border-radius:var(--tte-radius-medium); font-size:var(--tte-font-size-small); color:var(--tte-text-primary); } .content { flex:1; padding:var(--tte-spacing-xlarge); overflow:auto; } .infoPane { width:var(--tte-info-pane-width); border-left:var(--tte-border-width) solid var(--tte-border); padding:var(--tte-spacing-large); font-size:var(--tte-font-size-small); overflow:auto; }
  .statusbar { min-height:var(--tte-status-bar-height); border-top:var(--tte-border-width) solid var(--tte-separator); background:var(--tte-status-bar); padding:var(--tte-status-bar-padding); font-size:var(--tte-font-size-small); color:var(--tte-text-muted); } .diagTitle { font-size:var(--tte-font-size-large); font-weight:600; margin-bottom:var(--tte-spacing-medium); } .diagBox { border:var(--tte-border-width) solid var(--tte-border); border-radius:var(--tte-radius-large); padding:var(--tte-spacing-large); background:var(--tte-surface-raised); margin-bottom:var(--tte-spacing-large); }
  .diagRow { display:flex; gap:var(--tte-spacing-medium); align-items:center; padding:var(--tte-spacing-small) 0; } code { font-family:Consolas,monospace; }
  .modal { position:fixed; inset:0; background:var(--tte-overlay-scrim); display:flex; align-items:center; justify-content:center; z-index:20; } .panel { background:var(--tte-surface-raised); width:min(var(--tte-modal-panel-width),92vw); height:min(var(--tte-modal-panel-height),88vh); border-radius:var(--tte-radius-xlarge); padding:var(--tte-spacing-xlarge); overflow:auto; }
  .panelHead { display:flex; align-items:center; gap:var(--tte-spacing-large); margin-bottom:var(--tte-spacing-large); } .panelTitle { font-weight:bold; flex:1; } .btn { padding:var(--tte-control-padding); border-radius:var(--tte-radius-medium); border:var(--tte-border-width) solid var(--tte-input-border); cursor:pointer; }
</style>
</head>
<body>
  <div class="titlebar" id="titleBar">
    <span class="logo" id="logo"></span>
    <span class="seg" id="tabStrip">标签条（静态资源缺失）</span>
    <span class="seg" id="newTab">＋</span>
    <span class="spacer"></span>
    <span class="seg" id="toolboxBtn">任务看板</span>
    <span class="seg" id="captureEntry">截图与录屏</span>
    <span class="seg" id="ideBtn">IDE</span>
    <span class="seg" id="themeBtn">主题</span>
    <span class="seg" id="defaultExplorerBtn">默认资源管理器</span>
    <span class="seg" id="settingsBtn">设置</span>
  </div>
  <div class="bookmarkbar" id="bookmarkBar">书签栏</div>
  <div class="toolbar" id="toolbar">
    <span class="seg" id="btnBack">后退</span>
    <span class="seg" id="btnForward">前进</span>
    <span class="seg" id="btnUp">上一级</span>
    <span class="seg" id="btnRefresh">刷新</span>
    <span class="seg" id="btnNew">新建</span>
    <span class="seg" id="btnDelete">删除</span>
    <span class="seg" id="btnPreview">预览</span>
    <span class="seg" id="btnTaskCenter">任务中心</span>
    <span class="seg" id="btnSearch">搜索</span>
  </div>
  <div class="navrow" id="navRow">
    <span class="addr" id="addr">地址栏</span>
    <span class="addr" id="searchBox" style="flex:none;width:var(--tte-search-box-width)">搜索</span>
  </div>
  <div class="workarea">
    <div class="sidebar" id="sidebar">
      <div class="navsec">此电脑</div>
      <div class="navitem" id="navHome">主目录</div>
      <div class="navitem" id="navDesktop">桌面</div>
      <div class="navitem" id="navDownloads">下载</div>
      <div class="navsec">工具</div>
      <div class="navitem" id="navDisk">磁盘</div>
      <div class="navitem" id="navProcess">进程</div>
      <div class="navitem" id="navCompare">目录比对</div>
      <div class="navitem" id="navIde">IDE 工作区</div>
      <div class="navitem" id="navCapture">截图与录屏</div>
      <div class="navitem" id="navTaskCenter">任务中心</div>
      <div class="navitem" id="navSettings">设置</div>
    </div>
    <div class="content" id="content">
      <div class="diagTitle">静态资源不可用，当前为诊断回退页</div>
      <div class="diagBox">
        <div class="diagRow">页面入口：<code id="diagOrigin"></code></div>
        <div class="diagRow">静态资源：<code id="diagAssets">检查中…</code></div>
        <div class="diagRow">说明：正式界面需要 Assets/web 下的 index.html、app.css、tokens.css 与脚本模块被嵌入构建；回退页不提供文件浏览、预览、任务、截图、设置等业务能力。</div>
        <div class="diagRow"><span class="btn" id="diagReload">重新加载</span></div>
      </div>
    </div>
    <div class="infoPane" id="infoPane">
      <div class="navsec">诊断</div>
      <div class="navitem">本页不加载业务脚本</div>
      <div class="navitem">端口：<code>127.0.0.1:__PORT__</code></div>
    </div>
  </div>
  <div class="statusbar" id="statusBar">
    <span id="connState">静态资源缺失 · 诊断模式</span>
    <span class="spacer"></span>
    <span id="diagMessage">等待检查</span>
  </div>

  <!-- 与静态页同名的弹层骨架：回退页不渲染内容，仅保留入口位置 -->
  <div class="modal hidden" id="settings">
    <div class="panel"><div class="panelHead"><div class="panelTitle">设置</div><span class="btn" id="settingsClose">关闭</span></div>
      <div id="settingsPane">静态资源缺失，设置面板不可用。</div></div>
  </div>
  <div class="modal hidden" id="capturePanel">
    <div class="panel"><div class="panelHead"><div class="panelTitle">截图与录屏</div><span class="btn" id="captureClose">关闭</span></div>
      <div id="captureBody">静态资源缺失，截图面板不可用。</div></div>
  </div>
<script>
  (function () {
    const assets = ['/app.css', '/tokens.css', '/core.js', '/views.js', '/panels.js', '/actions.js', '/ide.js', '/boot.js'];
    const origin = document.getElementById('diagOrigin');
    const assetsText = document.getElementById('diagAssets');
    const message = document.getElementById('diagMessage');
    const hide = id => document.getElementById(id).classList.add('hidden');
    document.getElementById('diagReload').onclick = () => location.reload();
    document.getElementById('settingsClose').onclick = () => hide('settings');
    document.getElementById('captureClose').onclick = () => hide('capturePanel');
    if (origin) { origin.textContent = location.origin; }
    (async function () {
      const missing = [];
      for (const path of assets) {
        try {
          const response = await fetch(path, { method: 'HEAD' });
          if (!response.ok) { missing.push(path); }
        } catch (error) { missing.push(path); }
      }
      if (assetsText) { assetsText.textContent = missing.length ? '缺失：' + missing.join('、') : '可访问（当前仍显示回退页，请检查构建产物）'; }
      if (message) { message.textContent = missing.length ? '请重新构建并部署 Assets/web 后再试' : '静态资源可访问，请刷新重试'; }
    })();
  })();
</script>
</body>
</html>
""";
}
