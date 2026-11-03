// TabTaskExplorer 网页端交互模块：选择、右键菜单、地址栏、批量改名、编码转换与只读/可写编辑器。

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
  if (!currentTab) { return; }
  if (currentTab.tabType !== 'file' && currentTab.tabType !== 'search' && currentTab.tabType !== 'disk') { return; }
  const rowElement = event.target.closest('[data-i]');
  const row = (rowElement && currentRows.length) ? (currentRows[Number(rowElement.getAttribute('data-i'))] || null) : null;
  const isFileTab = currentTab.tabType === 'file' || currentTab.tabType === 'search';
  if (isFileTab && row && !checked.has(row.fullPath)) {
    checked = new Set([row.fullPath]);
    focusIndex = Number(rowElement.getAttribute('data-i'));
    anchorIndex = focusIndex;
    refreshSelectionDom();
    sendCheck();
  }
  event.preventDefault();
  showContextMenu(event, row, event.shiftKey);
}

function showContextMenu(event, row, shiftKey) {
  const tab = currentTab;
  const isDisk = tab.tabType === 'disk';
  const background = !row;
  const hasDirectory = !!(tab.currentPath || '').length;
  let targetPaths = [];
  if (background) {
    if (hasDirectory) { targetPaths = [tab.currentPath]; }
  } else if (isDisk) {
    targetPaths = [row.fullPath];
  } else {
    targetPaths = Array.from(checked).filter(Boolean);
    if (targetPaths.indexOf(row.fullPath) < 0) { targetPaths = [row.fullPath]; }
  }
  ctxMenuToken++;
  const token = ctxMenuToken;
  ctxMenuState = {
    row: row,
    background: background,
    tabType: tab.tabType,
    targetPaths: targetPaths,
    folder: hasDirectory ? tab.currentPath : '',
    shift: !!shiftKey,
    token: token,
    focus: null
  };
  ctx.innerHTML = '';
  ctx.classList.remove('hidden');
  const oldSubmenus = document.querySelectorAll('.ctxSub');
  for (const submenu of oldSubmenus) {
    if (submenu.parentElement === document.body) { submenu.remove(); }
  }
  const ownHead = document.createElement('div');
  ownHead.className = 'ctxHead';
  ownHead.textContent = '本程序命令';
  ctx.appendChild(ownHead);
  appendContextItems(ctx, buildOwnContextItems(tab, row, background, targetPaths, !!shiftKey));
  if (targetPaths.length === 0) {
    const hint = document.createElement('div');
    hint.className = 'ctxHint';
    hint.textContent = '当前视图没有具体目录，系统项不可用；请打开某个磁盘或文件夹后再使用系统菜单';
    ctx.appendChild(hint);
    positionRootContextMenu(ctx, event.clientX, event.clientY, false);
    focusFirstContextItem();
    return;
  }
  const divider = document.createElement('div');
  divider.className = 'ctxSep';
  ctx.appendChild(divider);
  const systemHead = document.createElement('div');
  systemHead.className = 'ctxHead';
  systemHead.textContent = '系统与扩展菜单';
  ctx.appendChild(systemHead);
  const host = document.createElement('div');
  host.id = 'ctxSystemHost';
  host.innerHTML = '<div class="ctxHint">正在加载系统菜单与第三方扩展……</div>';
  ctx.appendChild(host);
  positionRootContextMenu(ctx, event.clientX, event.clientY, false);
  focusFirstContextItem();
  loadSystemContextMenu(host, token, targetPaths, background);
}

let ctxMenuState = null;
let ctxMenuToken = 0;

function loadSystemContextMenu(host, token, targetPaths, background) {
  apiFetch(url('/api/ctxmenu'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ paths: targetPaths, background: background })
  }).then(async function (response) {
    const text = await response.text();
    if (!ctxMenuState || ctxMenuState.token !== token) { return; }
    if (!response.ok) {
      host.innerHTML = '<div class="ctxHint">系统项不可用：' + esc(text || ('状态码 ' + response.status)) + '</div>';
      return;
    }
    let result = null;
    try { result = JSON.parse(text); } catch (error) { result = null; }
    if (!result) {
      host.innerHTML = '<div class="ctxHint">系统项不可用：返回内容无法解析</div>';
      return;
    }
    host.innerHTML = '';
    if (!result.systemMenuAvailable) {
      host.innerHTML = '<div class="ctxHint">' + esc(result.systemMenuNote || '系统项不可用') + '</div>';
      return;
    }
    const items = result.items || [];
    if (items.length === 0) {
      host.innerHTML = '<div class="ctxHint">系统外壳没有返回任何菜单项</div>';
      return;
    }
    appendContextItems(host, items);
    const ctx = document.getElementById('ctx');
    if (ctx) { positionRootContextMenu(ctx, ctx.__ctxX || 0, ctx.__ctxY || 0, true); }
  }).catch(function (error) {
    if (!ctxMenuState || ctxMenuState.token !== token) { return; }
    host.innerHTML = '<div class="ctxHint">系统项不可用：' + esc('请求失败 ' + error) + '</div>';
  });
}

function buildOwnContextItems(tab, row, background, targetPaths, shiftKey) {
  const items = [];
  const canEdit = permissions.canCreateDelete;
  const isDisk = tab.tabType === 'disk';
  const suffix = targetPaths.length > 1 ? '（' + targetPaths.length + ' 项）' : '';
  if (background) {
    items.push({ title: '刷新', action: 'refresh', enabled: true });
    items.push({ title: '返回上一级', action: 'up', enabled: !!tab.currentPath });
    items.push({ sep: true });
    items.push({ title: '粘贴', action: 'pastebg', enabled: canEdit && !!tab.currentPath });
    items.push({ title: '新建文件夹…', action: 'newfolder', enabled: canEdit && !!tab.currentPath });
    if (isDisk) {
      items.push({ title: '新建文件…', action: 'newfile', enabled: canEdit && !!tab.currentPath });
    }
    items.push({ sep: true });
    items.push({
      title: '排序', enabled: true, children: [
        { title: '按名称排序', action: 'sort', arg: 'Name', enabled: true },
        { title: '按修改日期排序', action: 'sort', arg: 'Modified', enabled: !isDisk },
        { title: '按类型排序', action: 'sort', arg: 'Type', enabled: true },
        { title: '按大小排序', action: 'sort', arg: 'Size', enabled: true }
      ]
    });
    return items;
  }
  items.push({ title: '打开', action: 'open', enabled: true });
  items.push({ title: '在新标签打开', action: 'newtab', enabled: targetPaths.length === 1 });
  items.push({ sep: true });
  items.push({ title: '复制' + suffix, action: 'copy', enabled: targetPaths.length > 0 && !(isDisk && row && row.type === '驱动器') });
  items.push({ title: '剪切' + suffix, action: 'cut', enabled: canEdit && targetPaths.length > 0 && !(isDisk && row && row.type === '驱动器') });
  items.push({ title: '复制完整路径', action: 'copypath', enabled: targetPaths.length > 0 });
  if (targetPaths.length === 1 && !(isDisk && row && row.type === '驱动器')) {
    // 重命名与新建文件夹原来只在桌面端菜单里，网页端缺少入口导致无法完成基础文件操作
    items.push({ title: '重命名…', action: 'rename', enabled: canEdit });
  }
  items.push({ sep: true });
  items.push({ title: shiftKey ? '永久删除' + suffix : '删除' + suffix, action: 'delete', enabled: canEdit && targetPaths.length > 0 && !(isDisk && row && row.type === '驱动器') });
  items.push({ title: '在资源管理器打开', action: 'openinmanager', enabled: targetPaths.length === 1 });
  if (targetPaths.length === 1 && row && row.isDirectory && !(isDisk && row.type === '驱动器')) {
    items.push({ title: '粘贴到该文件夹', action: 'pasteto', enabled: canEdit });
    if (isDisk) {
      // 磁盘视图的目录行也要能直接新建子文件夹：此前只能对根目录空白处操作，
      // 而此电脑根视图没有根目录，导致磁盘视图完全无法新建
      items.push({ title: '新建文件夹…', action: 'newfolder', enabled: canEdit });
    }
  }
  if (!isDisk && targetPaths.length === 2) {
    items.push({ title: '比较所选两项', action: 'compare', enabled: true });
  }
  return items;
}

function appendContextItems(container, items) {
  for (const item of items) {
    container.appendChild(buildContextItemElement(item));
  }
}

function buildContextItemElement(item) {
  if (item.sep) {
    const separator = document.createElement('div');
    separator.className = 'ctxSep';
    return separator;
  }
  const element = document.createElement('div');
  element.className = 'ctxItem';
  if (item.enabled === false) { element.classList.add('disabled'); }
  if (item.children && item.children.length) { element.classList.add('hasSub'); }
  element.setAttribute('tabindex', '-1');
  const check = document.createElement('span');
  check.className = 'ctxCheck';
  check.textContent = item.checked ? (item.radio ? '●' : '✓') : '';
  element.appendChild(check);
  const icon = document.createElement('span');
  icon.className = 'ctxIco';
  if (item.icon) {
    const image = document.createElement('img');
    image.src = item.icon;
    image.alt = '';
    icon.appendChild(image);
  } else if (!item.source || item.source === 'own') {
    icon.textContent = '·';
    icon.style.color = '#B0B0B0';
  }
  element.appendChild(icon);
  const label = document.createElement('span');
  label.className = 'ctxLabel';
  appendAccessKeyLabel(label, item.title || '', item.accessKey || '');
  element.appendChild(label);
  if (item.shortcut) {
    const shortcut = document.createElement('span');
    shortcut.className = 'ctxAcc';
    shortcut.textContent = item.shortcut;
    element.appendChild(shortcut);
  }
  if (item.shield) {
    const shield = document.createElement('span');
    shield.className = 'ctxShield';
    shield.textContent = '盾';
    shield.title = '需要管理员权限';
    element.appendChild(shield);
  }
  if (item.isDefault) { element.style.fontWeight = 'bold'; }
  if (item.children && item.children.length) {
    const arrow = document.createElement('span');
    arrow.className = 'ctxArrow';
    arrow.textContent = '›';
    element.appendChild(arrow);
    const submenu = document.createElement('div');
    submenu.className = 'ctxSub';
    submenu.__parentItem = element;
    appendContextItems(submenu, item.children);
    element.__submenu = submenu;
    element.addEventListener('mouseenter', function () {
      element.__hoverTimer = setTimeout(function () { openContextSubmenu(element); }, 150);
    });
    element.addEventListener('mouseleave', function () {
      if (element.__hoverTimer) { clearTimeout(element.__hoverTimer); element.__hoverTimer = null; }
      element.__leaveTimer = setTimeout(function () {
        element.classList.remove('open');
        if (element.__submenu) { element.__submenu.classList.remove('openSub'); }
      }, 200);
    });
    submenu.addEventListener('mouseenter', function () {
      if (element.__leaveTimer) { clearTimeout(element.__leaveTimer); element.__leaveTimer = null; }
    });
    element.addEventListener('click', function (event) {
      if (element.classList.contains('disabled')) { return; }
      event.stopPropagation();
      if (element.classList.contains('open')) {
        element.classList.remove('open');
        submenu.classList.remove('openSub');
      } else {
        openContextSubmenu(element);
      }
    });
  } else {
    element.addEventListener('click', function (event) {
      event.stopPropagation();
      if (element.classList.contains('disabled')) { return; }
      activateContextItem(item);
    });
  }
  element.__item = item;
  return element;
}

function appendAccessKeyLabel(label, title, accessKey) {
  if (!accessKey) { label.textContent = title; return; }
  const index = title.indexOf(accessKey);
  if (index < 0) { label.textContent = title; return; }
  label.textContent = title.substring(0, index);
  const underlined = document.createElement('u');
  underlined.textContent = title.substring(index, index + 1);
  label.appendChild(underlined);
  label.appendChild(document.createTextNode(title.substring(index + 1)));
}

function openContextSubmenu(element) {
  const siblings = element.parentElement ? element.parentElement.children : [];
  for (const sibling of siblings) {
    if (sibling !== element && sibling.classList && sibling.classList.contains('ctxItem')) {
      sibling.classList.remove('open');
      if (sibling.__submenu) { sibling.__submenu.classList.remove('openSub'); }
    }
  }
  element.classList.add('open');
  const submenu = element.__submenu;
  if (!submenu) { return; }
  if (submenu.parentElement !== document.body) { document.body.appendChild(submenu); }
  submenu.classList.add('openSub');
  const itemRect = element.getBoundingClientRect();
  submenu.style.left = '0px';
  submenu.style.top = '0px';
  const rect = submenu.getBoundingClientRect();
  let left = itemRect.right - 2;
  let top = itemRect.top - 5;
  if (left + rect.width > window.innerWidth - 4) { left = itemRect.left - rect.width + 2; }
  if (left < 4) { left = 4; }
  if (top + rect.height > window.innerHeight - 4) { top = Math.max(4, window.innerHeight - rect.height - 4); }
  submenu.style.left = left + 'px';
  submenu.style.top = top + 'px';
}

function positionRootContextMenu(ctx, x, y, keepPosition) {
  ctx.__ctxX = x;
  ctx.__ctxY = y;
  const width = ctx.offsetWidth || 220;
  const height = ctx.offsetHeight || 220;
  let left = x;
  let top = y;
  if (left + width > window.innerWidth - 6) { left = Math.max(4, window.innerWidth - width - 6); }
  if (top + height > window.innerHeight - 6) { top = Math.max(4, window.innerHeight - height - 6); }
  if (keepPosition === true) {
    const currentLeft = parseFloat(ctx.style.left);
    const currentTop = parseFloat(ctx.style.top);
    left = Math.min(isNaN(currentLeft) ? left : currentLeft, Math.max(4, window.innerWidth - width - 6));
    top = Math.min(isNaN(currentTop) ? top : currentTop, Math.max(4, window.innerHeight - height - 6));
  }
  ctx.style.left = left + 'px';
  ctx.style.top = top + 'px';
}

function activateContextItem(item) {
  if (!ctxMenuState) { return; }
  if (item.source === 'shell' || item.commandId >= 0) {
    runShellContextItem(item);
    return;
  }
  const state = ctxMenuState;
  hideContextMenu();
  runOwnContextAction(item.action, item.arg, state);
}

function runShellContextItem(item) {
  const state = ctxMenuState;
  if (!state || item.commandId < 0) { return; }
  const payload = { paths: state.targetPaths, background: state.background, commandId: item.commandId };
  hideContextMenu();
  apiFetch(url('/api/ctxmenu/run'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  }).then(async function (response) {
    const text = await response.text();
    if (!response.ok) { flash(text || '系统菜单命令执行失败'); }
    else { flash('已执行系统菜单命令：' + (item.title || '')); }
    await refresh();
  }).catch(function (error) {
    flash('系统菜单命令请求失败：' + error);
  });
}

function runOwnContextAction(action, arg, state) {
  if (!state) { return; }
  const paths = state.targetPaths || [];
  const isDisk = state.tabType === 'disk';
  const row = state.row;
  if (action === 'open') {
    if (row) { openTarget(row); }
    return;
  }
  if (action === 'newtab') {
    if (row && row.isDirectory) { post('/api/tabs/new', { p: row.fullPath }); }
    else if (row) { openFile(row.fullPath); }
    return;
  }
  if (action === 'copy') {
    if (isDisk) { doAction('diskcopysource', paths.join('\n')); }
    else { doAction('copy'); }
    return;
  }
  if (action === 'cut') {
    if (isDisk) { doAction('diskcutsource', paths.join('\n')); }
    else { doAction('cut'); }
    return;
  }
  if (action === 'copypath') {
    copyContextText(paths.join('\n'));
    return;
  }
  if (action === 'rename') {
    const current = row ? row.name : (paths.length ? paths[0] : '');
    const name = window.prompt('新名称', current);
    if (!name || name === current) { return; }
    if (isDisk) { doAction('diskrename', (paths[0] || '') + '\n' + name); }
    else { renameEntry(paths[0] || (row ? row.fullPath : '')); }
    return;
  }
  if (action === 'delete') {
    const permanent = isDisk && !!state.shift;
    const confirmText = permanent
      ? '确认永久删除选中的 ' + paths.length + ' 项？永久删除不进回收站，无法恢复。'
      : '确认把选中的 ' + paths.length + ' 项移入回收站？';
    if (!window.confirm(confirmText)) { return; }
    if (isDisk) { doAction('diskdelete', (permanent ? '1\n' : '0\n') + paths.join('\n')); }
    else if (checked.size > 0) { doAction('deletechecked'); }
    else if (paths.length === 1) { deleteEntry(paths[0]); }
    return;
  }
  if (action === 'pasteto') {
    if (row && row.isDirectory) {
      if (isDisk) { doAction('diskpasteinto', row.fullPath); }
      else { doAction('pasteto', row.fullPath); }
    }
    return;
  }
  if (action === 'pastebg') {
    if (isDisk) { doAction('diskpasteinto', state.folder || ''); }
    else { doAction('paste'); }
    return;
  }
  if (action === 'newfolder') {
    const directory = isDisk ? (state.background ? state.folder : (row && row.isDirectory ? row.fullPath : '')) : state.folder;
    if (!directory) { flash('请先进入目标文件夹'); return; }
    const name = window.prompt('新建文件夹名称');
    if (!name) { return; }
    if (isDisk) { doAction('disknewfolder', directory + '\n' + name); }
    else if (state.background) { post('/api/folder', { p: directory, n: name }); }
    return;
  }
  if (action === 'newfile') {
    const directory = state.folder;
    if (!directory) { flash('请先进入目标文件夹'); return; }
    const name = window.prompt('新建文件名称', '新建文件.txt');
    if (!name) { return; }
    doAction('disknewfile', directory + '\n' + name);
    return;
  }
  if (action === 'refresh') { doAction('refresh'); return; }
  if (action === 'up') { post('/api/up', {}); return; }
  if (action === 'sort') { doAction('sort', arg); return; }
  if (action === 'openinmanager') { doAction('openinmanager', paths.join('\n')); return; }
  if (action === 'compare') { doAction('comparechecked'); return; }
}

function copyContextText(text) {
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(text).then(function () { flash('已复制完整路径'); }, function () { fallbackCopyContextText(text); });
  } else {
    fallbackCopyContextText(text);
  }
}

function fallbackCopyContextText(text) {
  const helper = document.createElement('textarea');
  helper.value = text;
  document.body.appendChild(helper);
  helper.select();
  document.execCommand('copy');
  document.body.removeChild(helper);
  flash('已复制完整路径');
}

function focusFirstContextItem() {
  const items = visibleContextItems();
  if (items.length > 0) { setContextFocus(items[0]); }
}

function visibleContextItems() {
  const result = [];
  const walk = function (container) {
    for (const child of container.children) {
      if (!child.classList || !child.classList.contains('ctxItem')) { continue; }
      if (child.classList.contains('disabled')) { continue; }
      result.push(child);
      const submenu = child.__submenu;
      if (submenu && child.classList.contains('open')) { walk(submenu); }
    }
  };
  const ctx = document.getElementById('ctx');
  if (ctx) { walk(ctx); }
  return result;
}

function setContextFocus(element) {
  const items = visibleContextItems();
  for (const item of items) { item.classList.remove('focused'); }
  if (!element) { return; }
  element.classList.add('focused');
  if (ctxMenuState) { ctxMenuState.focus = element; }
}

function handleContextMenuKeydown(event) {
  if (!ctxMenuState) { return; }
  const ctx = document.getElementById('ctx');
  if (!ctx || ctx.classList.contains('hidden')) { return; }
  const items = visibleContextItems();
  if (items.length === 0) { return; }
  let currentIndex = items.indexOf(ctxMenuState.focus);
  if (event.key === 'ArrowDown') {
    event.preventDefault();
    event.stopPropagation();
    setContextFocus(items[(currentIndex + 1) % items.length]);
    return;
  }
  if (event.key === 'ArrowUp') {
    event.preventDefault();
    event.stopPropagation();
    setContextFocus(items[(currentIndex <= 0) ? items.length - 1 : currentIndex - 1]);
    return;
  }
  if (event.key === 'ArrowRight') {
    event.preventDefault();
    event.stopPropagation();
    const focused = ctxMenuState.focus;
    if (focused && focused.classList.contains('hasSub')) {
      openContextSubmenu(focused);
      const subItems = focused.__submenu ? focused.__submenu.querySelectorAll('.ctxItem:not(.disabled)') : [];
      if (subItems.length > 0) { setContextFocus(subItems[0]); }
    }
    return;
  }
  if (event.key === 'ArrowLeft') {
    event.preventDefault();
    event.stopPropagation();
    const focused = ctxMenuState.focus;
    if (focused && focused.parentElement && focused.parentElement.classList.contains('ctxSub')) {
      const parentItem = focused.parentElement.__parentItem;
      focused.parentElement.classList.remove('openSub');
      if (parentItem) {
        parentItem.classList.remove('open');
        setContextFocus(parentItem);
      }
    }
    return;
  }
  if (event.key === 'Enter') {
    event.preventDefault();
    event.stopPropagation();
    const focused = ctxMenuState.focus;
    if (focused && !focused.classList.contains('disabled')) {
      const item = focused.__item;
      if (item && item.children && item.children.length) { openContextSubmenu(focused); }
      else if (item) { activateContextItem(item); }
    }
    return;
  }
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    hideContextMenu();
  }
}

function hideContextMenu() {
  const ctx = document.getElementById('ctx');
  if (ctx) {
    ctx.classList.add('hidden');
    ctx.innerHTML = '';
    ctx.__ctxX = 0;
    ctx.__ctxY = 0;
  }
  const orphanMenus = document.querySelectorAll('.ctxSub');
  for (const submenu of orphanMenus) {
    submenu.classList.remove('openSub');
    if (submenu.parentElement === document.body) { submenu.remove(); }
  }
  ctxMenuState = null;
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
    const response = await apiFetch(url('/api/action', { a: 'batchrename' }), { method: 'POST', body: plan });
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
    const response = await apiFetch(url('/api/action', { a: 'encodepreview' }), { method: 'POST', body: body });
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
    const response = await apiFetch(url('/api/action', { a: 'encodeapply' }), { method: 'POST', body: plan });
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
    const response = await apiFetch(url('/api/file', { p: target, w: workspaceId }));
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
    const response = await apiFetch(url('/api/write', { p: editingPath }), { method: 'POST', body: content });
    document.getElementById('editorHint').textContent = response.ok ? '已保存' : ('保存失败：' + await response.text());
  } catch (error) {
    document.getElementById('editorHint').textContent = '保存失败：' + error;
  }
}
