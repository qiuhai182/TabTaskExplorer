// TabTaskExplorer 网页端视图模块：标签条、导航、文件列表、表格、磁贴与磁盘视图。

// TabTaskExplorer 网页端渲染模块：标签条、文件/磁盘/进程/比对/看板/连接/启动项/编辑器等面板。

// ======== 渲染 ========

function render() {
  if (!state) { return; }
  if (state.notice && state.notice !== lastNotice) { flash(state.notice); lastNotice = state.notice; }
  if (!state.notice) { lastNotice = ''; }
  document.body.className = (state.theme === 'Light') ? '' : 'dark';
  updateModeButton(state.followDesktop);
  renderTabs();
  renderTasks();
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
  renderHostKeyDialog(state.pendingHostKey);
}

function renderTabs() {
  let html = '';
  for (const tab of state.tabs) {
    html += '<div class="tab' + (tab.selected ? ' active' : '') + '" onclick="selectTab(' + tab.index + ')">'
      + '<span class="fIco" style="width:14px;height:14px">' + fileIcon('folder', '#F2B23E', 14) + '</span><span>' + esc(tab.title) + '</span>';
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
    bar.innerHTML = '<span class="ico">' + uiIcon('bookmark', 16, '#E8A317') + '</span>'
      + '<span class="bmHint">可拖动常用文件 / 文档 / 文件夹到此，创建成书签，以便能快速找到并打开</span>';
    return;
  }
  let html = '';
  for (const item of state.navPinned) {
    html += '<div class="bmItem" onclick="openPath(' + JSON.stringify(item.path).replace(/"/g, '&quot;') + ')">'
      + '<span class="fIco" style="width:15px;height:15px">' + fileIcon(item.iconCategory || 'folder', item.iconColor || '#F2B23E', 15) + '</span><span>' + esc(item.name) + '</span>'
      + '<span class="bmX" title="取消固定" onclick="event.stopPropagation();clickBtn(null,\'unpin\','
      + JSON.stringify(item.path).replace(/"/g, '&quot;') + ')">&times;</span></div>';
  }
  bar.innerHTML = html;
}

function renderSidebar() {
  let html = '';
  html += navSection('快捷访问', 'bookmark', state.navPinned, false);
  html += navSection('此电脑', 'monitor', state.navPc, true);
  html += '<div class="navsec"><span class="ico">' + uiIcon('globe', 14, '#2F6FD0') + '</span>网络</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'openconnections\')"><span class="ico">' + uiIcon('globe', 15, '#E8A317') + '</span>连接管理</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'opencompare\')"><span class="ico">' + uiIcon('compare', 15, '#2F6FD0') + '</span>文件比对</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'opensearch\')"><span class="ico">' + uiIcon('search', 15, '#E8A317') + '</span>全局搜索</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'opendisk\')"><span class="ico">' + uiIcon('donut', 15, '#E8A317') + '</span>磁盘视图</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'openprocess\')"><span class="ico">' + uiIcon('activity', 15, '#E8A317') + '</span>进程监控</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'openstartup\')"><span class="ico">' + uiIcon('power', 15, '#E8A317') + '</span>启动项管理</div>'
    + '<div class="navitem" onclick="clickBtn(null,\'openboard\')"><span class="ico">' + uiIcon('board', 15, '#E8A317') + '</span>任务看板</div>';
  document.getElementById('sidebar').innerHTML = html;
}

function navSection(title, iconKind, items, clickable) {
  // 分组标题可点击时：导航当前文件标签到此电脑根视图（无文件标签时自动新建），与桌面端 OnThisPcHeaderClick 一致
  let html = '<div class="navsec' + (clickable ? ' clickable' : '') + '"'
    + (clickable ? ' onclick="openPath(\'\')" title="导航到此电脑"' : '')
    + '><span class="ico">' + uiIcon(iconKind, 14, '#2F6FD0') + '</span>' + title + '</div>';
  if (!items || items.length === 0) {
    return html;
  }
  for (const item of items) {
    html += '<div class="navitem' + (item.selected ? ' selected' : '') + '" onclick="navOpen('
      + JSON.stringify(item.path).replace(/"/g, '&quot;') + ',' + (item.selected ? 1 : 0) + ')">'
      + '<span class="fIco" style="width:15px;height:15px">' + fileIcon(item.iconCategory || 'folder', item.iconColor || '#F2B23E', 15) + '</span>'
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
  document.getElementById('tickCompact').innerHTML = state.fileCompactMode ? '&#10003;' : '';
  const visibleColumns = parseFileColumns(state.fileVisibleColumns);
  const tickMap = {
    Name: 'tickColName', Modified: 'tickColModified', Type: 'tickColType',
    Size: 'tickColSize', Attributes: 'tickColAttributes'
  };
  for (const name in tickMap) {
    const tick = document.getElementById(tickMap[name]);
    if (tick) { tick.innerHTML = visibleColumns.indexOf(name) >= 0 ? '&#10003;' : ''; }
  }
  document.getElementById('btnHidden').classList.toggle('disabled', !permissions.canModify);
  const pasteButton = document.getElementById('btnPaste');
  if (pasteButton) {
    const clipboardHasFiles = !!(state && state.clipboardHasFiles);
    const canPaste = permissions.canCreateDelete && clipboardHasFiles;
    pasteButton.classList.toggle('disabled', !canPaste);
    pasteButton.setAttribute('data-disabled-reason', canPaste ? '' : (permissions.canCreateDelete
      ? '剪贴板为空或没有文件，粘贴不可用'
      : '该端口未放行该操作，权限方案=' + permissions.profile));
  }
}

// ======== 共享列配置与缩略图 ========

const FILE_COLUMN_DEFS = [
  { id: 'Name', label: '名称', widthIndex: 2, defaultWidth: 480, sortable: true },
  { id: 'Modified', label: '修改日期', widthIndex: 3, defaultWidth: 140, sortable: true },
  { id: 'Type', label: '类型', widthIndex: 4, defaultWidth: 130, sortable: true },
  { id: 'Size', label: '大小', widthIndex: 5, defaultWidth: 110, sortable: true },
  { id: 'Attributes', label: '属性', widthIndex: 6, defaultWidth: 100, sortable: true }
];

const DEFAULT_FILE_WIDTHS = [30, 26, 480, 140, 130, 110, 100];

function parseFileColumns(text) {
  const names = [];
  for (const part of (text || '').split(',')) {
    const name = part.trim();
    if (['Name', 'Modified', 'Type', 'Size', 'Attributes'].indexOf(name) >= 0 && names.indexOf(name) < 0) {
      names.push(name);
    }
  }
  return names;
}

function parseFileWidths(text) {
  const widths = DEFAULT_FILE_WIDTHS.slice();
  const parts = (text || '').split(',');
  for (let i = 0; i < widths.length && i < parts.length; i++) {
    const value = Number(parts[i]);
    if (isFinite(value) && value >= 0 && value <= 4000) { widths[i] = value; }
  }
  return widths;
}

function thumbSupported(row) {
  return row && !row.isDirectory && ['image', 'video', 'font'].indexOf(row.iconCategory) >= 0;
}

function thumbBoxHtml(row, size, extraClass, iconSize) {
  const pixels = iconSize || 16;
  if (!thumbSupported(row)) {
    return '<span class="fIco">' + fileIcon(row.iconCategory, row.iconColor, pixels) + '</span>';
  }
  return '<span class="thumbBox ' + (extraClass || '') + '" data-thumb-path="' + escAttr(row.fullPath)
    + '" data-thumb-size="' + size + '" title="' + escAttr(row.fullPath) + '">'
    + fileIcon(row.iconCategory, row.iconColor, pixels) + '</span>';
}

const thumbObjectUrls = new Map();
const thumbFailed = new Set();
let thumbObserver = null;

function hydrateThumbnails(root) {
  const scope = root || document;
  const boxes = Array.prototype.slice.call(scope.querySelectorAll('.thumbBox[data-thumb-path]'));
  for (const box of boxes) {
    if (box.__thumbStarted) { continue; }
    box.__thumbStarted = true;
    if (!('IntersectionObserver' in window)) { loadThumbnail(box); continue; }
    if (!thumbObserver) {
      thumbObserver = new IntersectionObserver(function (entries) {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            thumbObserver.unobserve(entry.target);
            loadThumbnail(entry.target);
          }
        }
      }, { rootMargin: '120px' });
    }
    thumbObserver.observe(box);
  }
}

async function loadThumbnail(box) {
  const path = box.getAttribute('data-thumb-path');
  const size = Number(box.getAttribute('data-thumb-size')) || 96;
  const key = size + '|' + path;
  if (thumbFailed.has(key)) { box.classList.add('thumbFailed'); return; }
  let objectUrl = thumbObjectUrls.get(key);
  if (!objectUrl) {
    const params = new URLSearchParams({ path: path, size: String(size) });
    if (workspaceId) { params.set('w', workspaceId); }
    try {
      const response = await apiFetch('/api/thumb?' + params.toString());
      if (!response.ok) { throw new Error('thumb ' + response.status); }
      const blob = await response.blob();
      objectUrl = URL.createObjectURL(blob);
      thumbObjectUrls.set(key, objectUrl);
    } catch (error) {
      thumbFailed.add(key);
      box.classList.add('thumbFailed');
      return;
    }
  }
  if (!document.contains(box)) { return; }
  const image = document.createElement('img');
  image.className = 'rowThumbImg';
  image.alt = '';
  image.onload = function () { box.classList.add('thumbLoaded'); };
  image.onerror = function () { box.classList.add('thumbFailed'); };
  image.src = objectUrl;
  box.appendChild(image);
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
    + '|' + (state.fileVisibleColumns || '') + '|' + (state.fileColumnWidths || '') + '|' + (state.fileCompactMode ? '1' : '0')
    + '|' + permissions.canCreateDelete + '|' + permissions.canModify + '|' + JSON.stringify(tab.rows)
    + '|' + JSON.stringify(Array.from(checked)) + '|' + tab.todo + '|' + tab.inProgress + '|' + tab.done
    + '|' + JSON.stringify(tab.connections) + '|' + tab.selectedConnectionId + '|' + tab.summaryText
    + '|' + tab.resourceText + '|' + JSON.stringify(tab.processRows)
    + '|' + tab.detailText + '|' + tab.affinityInput + '|' + tab.priorityIndex
    + '|' + tab.detailSupported + '|' + tab.trendVersion
    + '|' + JSON.stringify(tab.startupRows) + '|' + JSON.stringify(tab.customStartupRows)
    + '|' + tab.startupEnvironmentText + '|' + tab.summaryText
    + '|' + tab.sortField + '|' + tab.sortAscending
    + '|' + JSON.stringify(tab.compareRows) + '|' + tab.compareMode + '|' + tab.leftPathInput + '|' + tab.rightPathInput
    + '|' + tab.compareShowOnlyDifferences + '|' + tab.compareIncludeSubDirectories + '|' + tab.compareFileContent
    + '|' + tab.compareDifferencePosition + '|' + tab.leftMetaText + '|' + tab.rightMetaText;
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
  } else if (tab.tabType === 'startup') {
    html = renderStartup(tab);
  } else if (tab.tabType === 'compare') {
    html = renderCompare(tab);
  } else {
    html = renderGenericView(tab);
  }
  const listedRowCount = (tab.tabType === 'compare')
    ? (tab.compareRows || []).length
    : ((tab.rows || []).length);
  if (tab.tabType !== 'process' && tab.totalRows && tab.totalRows > listedRowCount && listedRowCount > 0) {
    html += '<div class="hint">仅显示前 ' + listedRowCount + ' 项，共 ' + tab.totalRows + ' 项</div>';
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
  if (state.fileCompactMode && (tab.tabType === 'file' || tab.tabType === 'search')) {
    document.getElementById('contentMain').classList.add('compact');
  } else {
    document.getElementById('contentMain').classList.remove('compact');
  }
  if (tab.tabType === 'process') {
    syncProcessControls(tab, procFocusId, procFocusValue);
    drawProcessTrend(tab);
  }
  hydrateThumbnails(document.getElementById('contentMain'));
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
  if (!isSearch && mode === 'Details') {
    return renderFileDetailsTable(tab);
  }
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
      + '<td class="name"><span class="rowThumbSlot">' + thumbBoxHtml(row, 96) + '</span>'
      + '<span class="text ' + (row.isDirectory ? 'dir' : 'file') + '" title="' + esc(path) + '">' + esc(row.name) + '</span></td>';
    if (mode === 'Content') {
      html += '<td>' + esc(row.type) + '</td>';
    }
    html += '</tr>';
    rowIndex++;
  }
  html += '</table>';
  return html;
}

// 详细信息：列可见性与列宽直接使用桌面共享设置；勾选框与图标列固定，其余列可拖动。
function renderFileDetailsTable(tab) {
  const widths = parseFileWidths(state.fileColumnWidths);
  const visible = parseFileColumns(state.fileVisibleColumns);
  const webWidths = [widths[0], widths[1]];
  let columns = '<th data-fixed style="width:' + widths[0] + 'px"></th>'
    + '<th data-fixed style="width:' + widths[1] + 'px"></th>';
  for (const def of FILE_COLUMN_DEFS) {
    if (visible.indexOf(def.id) < 0) { continue; }
    const width = Math.max(40, widths[def.widthIndex] || def.defaultWidth);
    webWidths.push(width);
    if (def.sortable) {
      columns += '<th class="sortable" onclick="clickBtn(null,\'sort\',\'' + def.id + '\')">' + def.label
        + sortArrow(tab, def.id) + '</th>';
    } else {
      columns += '<th>' + def.label + '</th>';
    }
  }
  let total = 0;
  let colGroup = '';
  for (const width of webWidths) {
    total += width;
    colGroup += '<col style="width:' + width + 'px">';
  }
  let html = '<table data-colkey="file:Details" style="width:' + total + 'px;max-width:100%"><colgroup>'
    + colGroup + '</colgroup><tr>' + columns + '</tr>';
  if (!tab.rows || tab.rows.length === 0) {
    html += '<tr><td class="hint" colspan="' + (2 + visible.length) + '">空目录</td></tr>';
  }
  let rowIndex = 0;
  for (const row of tab.rows) {
    const path = row.fullPath;
    const selected = checked.has(path);
    html += '<tr class="row' + (selected ? ' checked' : '') + '" data-i="' + rowIndex + '">'
      + '<td><input type="checkbox" data-check="' + rowIndex + '"' + (selected ? ' checked' : '') + '></td>'
      + '<td class="iconCell">' + thumbBoxHtml(row, 96) + '</td>';
    for (const def of FILE_COLUMN_DEFS) {
      if (visible.indexOf(def.id) < 0) { continue; }
      if (def.id === 'Name') {
        html += '<td class="name"><span class="text ' + (row.isDirectory ? 'dir' : 'file')
          + '" title="' + esc(path) + '">' + esc(row.name) + '</span></td>';
      } else if (def.id === 'Modified') {
        html += '<td>' + esc(row.modified) + '</td>';
      } else if (def.id === 'Type') {
        html += '<td>' + esc(row.type) + '</td>';
      } else if (def.id === 'Size') {
        html += '<td>' + esc(row.size) + '</td>';
      } else if (def.id === 'Attributes') {
        html += '<td class="attrs">' + esc(row.attributes || '') + '</td>';
      }
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
  let iconSize = 40;
  if (mode === 'ExtraLargeIcons') { width = 210; iconSize = 64; }
  else if (mode === 'LargeIcons') { width = 130; iconSize = 48; }
  else if (mode === 'MediumIcons') { width = 96; iconSize = 36; }
  else if (mode === 'SmallIcons') { width = 64; iconSize = 24; }
  let html = '<div class="tiles">';
  let rowIndex = 0;
  for (const row of tab.rows) {
    const selected = checked.has(row.fullPath);
    html += '<div class="tile' + (selected ? ' checked' : '') + '" data-i="' + rowIndex + '" style="width:' + width + 'px">'
      + '<input type="checkbox" class="ttick" data-check="' + rowIndex + '"' + (selected ? ' checked' : '') + '>'
      + '<div class="ticon" style="width:' + iconSize + 'px;height:' + iconSize + 'px;margin:0 auto 2px">'
      + thumbBoxHtml(row, mode === 'ExtraLargeIcons' ? 384 : (mode === 'LargeIcons' ? 192 : 96), 'tileThumb', iconSize)
      + '</div>'
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
    html += '<tr class="row" data-i="' + rowIndex + '" draggable="true"'
      + ' ondragstart="onDiskDragStart(event)" ondragover="onDiskDragOver(event)"'
      + ' ondrop="onDiskDrop(event)" ondragend="onDiskDragEnd(event)"><td class="name" style="padding-left:' + indent + 'px">';
    if (row.isDirectory) {
      // 小三角与文件名点击效果完全一致：同一 diskNav 处理，点击即展开或折叠
      html += '<span class="diskArrow" onclick="diskNav(' + target + ')">'
        + (row.expanded ? '&#9662;' : '&#9656;') + '</span>'
        + '<span class="fIco" style="width:16px;height:16px">' + fileIcon(row.iconCategory, row.iconColor, 16) + '</span>'
        + '<span class="dir" onclick="diskNav(' + target + ')">' + esc(row.name) + '</span>';
    } else {
      html += '<span class="diskArrow" style="visibility:hidden">&#9656;</span>'
        + '<span class="fIco" style="width:16px;height:16px">' + fileIcon(row.iconCategory, row.iconColor, 16) + '</span>'
        + '<span>' + esc(row.name) + '</span>';
    }
    html += '</td><td>' + esc(row.type) + '</td><td>' + esc(row.size) + '</td>'
      + '<td>' + esc(row.free || '-') + '</td><td>' + esc(row.usage || '-') + '</td>'
      + '<td>' + esc(row.fileSystem || '-') + '</td></tr>';
    rowIndex++;
  }
  html += '</table>';
  return html;
}

// ===== 磁盘视图拖拽（浏览器内移动或复制，绝不删除源文件） =====

let diskDragPaths = [];

function onDiskDragStart(event) {
  if (!currentTab || currentTab.tabType !== 'disk') { event.preventDefault(); return; }
  const element = event.target.closest('tr[data-i]');
  const row = element ? currentRows[Number(element.getAttribute('data-i'))] : null;
  if (!row || row.type === '驱动器') { event.preventDefault(); return; }
  diskDragPaths = [row.fullPath];
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'copyMove';
    event.dataTransfer.setData('text/plain', row.fullPath);
    event.dataTransfer.setData('application/x-tabtask-paths', JSON.stringify(diskDragPaths));
  }
  flash('拖拽中：' + row.name + '（按住 Ctrl 为复制，直接拖为移动）');
}

function onDiskDragOver(event) {
  if (!diskDragPaths.length) { return; }
  const element = event.target.closest('tr[data-i]');
  const row = element ? currentRows[Number(element.getAttribute('data-i'))] : null;
  if (!row || !row.isDirectory) {
    if (event.dataTransfer) { event.dataTransfer.dropEffect = 'none'; }
    return;
  }
  event.preventDefault();
  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = (event.ctrlKey || event.altKey) ? 'copy' : 'move';
  }
  if (element) { element.classList.add('dragTarget'); }
}

function onDiskDragEnd(event) {
  diskDragPaths = [];
  const rows = document.querySelectorAll('#content tr.row.dragTarget');
  for (const row of rows) { row.classList.remove('dragTarget'); }
}

function onDiskDrop(event) {
  if (!diskDragPaths.length) { return; }
  const element = event.target.closest('tr[data-i]');
  const row = element ? currentRows[Number(element.getAttribute('data-i'))] : null;
  if (!row || !row.isDirectory) { return; }
  event.preventDefault();
  const isCopy = event.ctrlKey || event.altKey;
  const payload = [row.fullPath].concat(diskDragPaths).join('\n');
  diskDragPaths = [];
  if (element) { element.classList.remove('dragTarget'); }
  doAction(isCopy ? 'diskcopyto' : 'diskmoveto', payload);
}

