// TabTaskExplorer 网页端启动模块：快捷键、事件挂接与页面启动。

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
document.addEventListener('keydown', handleContextMenuKeydown, true);
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

// 资源树：单击选中；单击文件按预览方式打开（下一次预览替换它），双击目录展开 / 收起、双击文件固定为普通标签
document.getElementById('ideTreeList').addEventListener('click', async function (event) {
  const element = event.target.closest('[data-i]');
  if (!element) { return; }
  const node = ideTreeNodes[Number(element.getAttribute('data-i'))];
  if (!node) { return; }
  ideSelectedPath = node.fullPath;
  const nodes = document.querySelectorAll('#ideTreeList .ideNode');
  for (const item of nodes) { item.classList.remove('sel'); }
  element.classList.add('sel');
  if (!node.isPlaceholder && !node.isDirectory) {
    // 预览可能替换上一个预览标签：先推送本地尚未同步的编辑，让服务端把它标记为已编辑而固定保留
    await ideFlushPushNow();
    doAction('ideopenpreview', node.fullPath);
  }
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

// 编辑器标签：双击标签标题把预览标签固定为普通标签
document.getElementById('ideTabs').addEventListener('dblclick', function (event) {
  if (event.target.closest('[data-close]')) { return; }
  const tabElement = event.target.closest('[data-i]');
  if (!tabElement) { return; }
  doAction('idepin', tabElement.getAttribute('data-i'));
});

// 工作区搜索结果：单击按预览方式打开并跳到行，双击固定为普通标签
document.getElementById('ideFindList').addEventListener('click', async function (event) {
  const element = event.target.closest('[data-i]');
  if (!element) { return; }
  const item = ideFindResults[Number(element.getAttribute('data-i'))];
  if (!item) { return; }
  // 预览可能替换上一个预览标签：先推送本地尚未同步的编辑，让服务端把它标记为已编辑而固定保留
  await ideFlushPushNow();
  idePendingCaretLine = item.lineNumber;
  doAction('idefindpreview', item.filePath + '\n' + item.lineNumber);
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

loadSession(false).then(loadPermissions).then(initMode).then(function () {
  connectEvents();
  return refresh();
});
// 事件推送为主；低频兜底刷新只在连接不可用时执行，避免空闲时反复拉取全量状态
pollTimer = setInterval(function () {
  if (!eventSource || eventSource.readyState !== 1) { refresh(); }
}, 10000);
