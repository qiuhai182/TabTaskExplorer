// TabTaskExplorer 网页端 IDE 模块：资源树、编辑器标签、查找替换与快速打开。

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

// 资源树：按深度缩进拍平节点；单击文件按预览方式打开，双击目录展开、双击文件固定为普通标签
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
      const arrow = node.isDirectory ? (node.isExpanded ? '&#9662;' : '&#9656;') : '';
      html += '<div class="ideNode' + selected + '" data-i="' + i + '" style="padding-left:' + (4 + node.depth * 14) + 'px"'
        + ' title="' + escAttr(node.fullPath) + '">'
        + '<span class="arrow">' + arrow + '</span>'
        + '<span class="fIco" style="width:16px;height:16px">' + fileIcon(node.iconCategory, node.iconColor, 16) + '</span>'
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
    const preview = editor.isPreview ? ' preview' : '';
    html += '<div class="ideTab' + active + preview + '" data-i="' + editor.index + '" title="' + escAttr(editor.toolTipText) + '">'
      + '<span class="fIco" style="width:14px;height:14px">' + fileIcon(editor.iconCategory, editor.iconColor, 14) + '</span>'
      + '<span class="ttl">' + esc(editor.displayTitle) + '</span>'
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
    const response = await apiFetch(url('/api/ide/text', { i: index }));
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
  // 等已在途的推送完成，确保接下来的打开 / 保存动作在服务端看到的是最新文本（最多等 1 秒，避免推送异常时卡住）
  let pushWaitCount = 0;
  while (idePushPending && pushWaitCount < 50) {
    await new Promise(function (resolve) { setTimeout(resolve, 20); });
    pushWaitCount++;
  }
  if (!ideLocalDirty || ideLoadedIndex < 0) { return; }
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
    const response = await apiFetch(url('/api/action', { a: 'idetext' }), { method: 'POST', body: index + '\n' + text });
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
    const fresh = await (await apiFetch('/api/state')).json();
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

