// TabTaskExplorer 网页端面板模块：目录/文件比对、进程监控、任务看板、连接管理与启动项管理。

// ===== 目录 / 文件比对（与桌面 ComparePanel 同构：路径栏 + 选项 + 对齐结果表） =====

function cmpRowBackground(key) {
  if (key === 'leftonly') { return 'rgba(242,178,62,.10)'; }
  if (key === 'rightonly') { return 'rgba(47,111,208,.10)'; }
  if (key === 'different' || key === 'modified') { return 'rgba(232,163,23,.10)'; }
  if (key === 'leftnewer' || key === 'rightnewer') { return 'rgba(142,124,195,.10)'; }
  if (key === 'type') { return 'rgba(224,82,82,.10)'; }
  return 'transparent';
}

function renderCompare(tab) {
  const rows = tab.compareRows || [];
  const isFile = tab.compareMode === 'file';
  let html = '<div class="cmpBar">'
    + '<span class="cmpLbl">左侧</span>'
    + '<input id="cmpLeft" class="cmpInput" placeholder="输入左侧目录或文件路径" value="' + escAttr(tab.leftPathInput || '') + '">'
    + '<button class="btn" onclick="cmpApply()">开始比对</button>'
    + '<span class="cmpLbl">右侧</span>'
    + '<input id="cmpRight" class="cmpInput" placeholder="输入右侧目录或文件路径" value="' + escAttr(tab.rightPathInput || '') + '">'
    + '<button class="btn" onclick="cmpSwap()">⇄ 交换</button>'
    + '</div>';
  html += '<div class="cmpOpts"><span class="cmpMode">' + (isFile ? '文件比对' : '目录比对') + '</span>';
  if (!isFile) {
    html += '<button class="btn" onclick="clickBtn(null,\'compareup\')">上一级</button>';
  }
  html += '<label><input type="checkbox"' + (tab.compareShowOnlyDifferences ? ' checked' : '')
    + ' onclick="clickBtn(null,\'comparetoggle\',\'onlydiff\')"> 仅显示差异</label>';
  if (!isFile) {
    html += '<label><input type="checkbox"' + (tab.compareIncludeSubDirectories ? ' checked' : '')
      + ' onclick="clickBtn(null,\'comparetoggle\',\'subdirs\')"> 包含子目录</label>'
      + '<label><input type="checkbox"' + (tab.compareFileContent ? ' checked' : '')
      + ' onclick="clickBtn(null,\'comparetoggle\',\'content\')"> 按内容比较</label>';
  }
  html += '<button class="btn" onclick="clickBtn(null,\'compareprev\')">上一处差异</button>'
    + '<button class="btn" onclick="clickBtn(null,\'comparenext\')">下一处差异</button>'
    + '<span class="cmpPos">' + esc(tab.compareDifferencePosition || '') + '</span></div>';
  if (isFile) {
    html += '<div class="cmpMeta">左：' + esc(tab.leftMetaText || '') + '　右：' + esc(tab.rightMetaText || '') + '</div>';
  }
  if (rows.length === 0) {
    html += '<div class="hint">' + (tab.summaryText ? esc(tab.summaryText) : '请输入左右两侧路径后点击「开始比对」') + '</div>';
    return html;
  }
  if (isFile) {
    html += renderCompareFileTable(rows);
  } else {
    html += renderCompareDirectoryTable(rows);
  }
  return html;
}

function renderCompareDirectoryTable(rows) {
  let html = '<table class="cmpTable" style="width:100%;max-width:100%">'
    + '<colgroup><col style="width:100px"><col><col style="width:200px"><col style="width:200px"></colgroup>'
    + '<tr><th>状态</th><th>相对路径</th><th>左侧（时间 · 大小）</th><th>右侧（时间 · 大小）</th></tr>';
  for (let index = 0; index < rows.length; index++) {
    const row = rows[index];
    html += '<tr class="row" style="background:' + cmpRowBackground(row.statusKey) + '"'
      + ' ondblclick="cmpOpen(' + index + ')">'
      + '<td><span class="cmpBadge" style="background:' + escAttr(row.statusColor || '#4CAF50') + '">'
      + esc(row.statusText || '') + '</span></td>'
      + '<td class="name"><span class="fIco" style="width:16px;height:16px">'
      + fileIcon(row.iconCategory || 'file', row.iconColor || '#8A94A6', 16) + '</span>'
      + '<span class="text ' + (row.isDirectory ? 'dir' : 'file') + '" title="' + escAttr(row.name || '') + '">'
      + esc(row.name || '') + '</span></td>'
      + '<td class="clip" title="' + escAttr(row.leftInfo || '') + '">' + esc(row.leftInfo || '') + '</td>'
      + '<td class="clip" title="' + escAttr(row.rightInfo || '') + '">' + esc(row.rightInfo || '') + '</td></tr>';
  }
  html += '</table>';
  return html;
}

function renderCompareFileTable(rows) {
  let html = '<table class="cmpTable" style="width:100%;max-width:100%">'
    + '<colgroup><col style="width:52px"><col><col style="width:52px"><col></colgroup>'
    + '<tr><th>行</th><th>左侧内容</th><th>行</th><th>右侧内容</th></tr>';
  for (let index = 0; index < rows.length; index++) {
    const row = rows[index];
    html += '<tr style="background:' + cmpRowBackground(row.kindKey) + '">'
      + '<td class="cmpLineNo">' + esc(row.leftNumber || '') + '</td>'
      + '<td class="cmpText">' + cmpChangedHtml(row.leftPrefix, row.leftChanged, row.leftSuffix, row.kindKey, 'left') + '</td>'
      + '<td class="cmpLineNo">' + esc(row.rightNumber || '') + '</td>'
      + '<td class="cmpText">' + cmpChangedHtml(row.rightPrefix, row.rightChanged, row.rightSuffix, row.kindKey, 'right') + '</td></tr>';
  }
  html += '</table>';
  return html;
}

function cmpChangedHtml(prefix, changed, suffix, kind, side) {
  let color = 'rgba(232,163,23,.35)';
  if (kind === 'leftonly') { color = 'rgba(224,82,82,.35)'; }
  else if (kind === 'rightonly') { color = 'rgba(51,168,82,.35)'; }
  let html = esc(prefix || '');
  if (changed) {
    html += '<span class="cmpChanged" style="background:' + color + '">' + esc(changed) + '</span>';
  }
  html += esc(suffix || '');
  return html;
}

function cmpApply() {
  const leftElement = document.getElementById('cmpLeft');
  const rightElement = document.getElementById('cmpRight');
  const left = leftElement ? leftElement.value.trim() : '';
  const right = rightElement ? rightElement.value.trim() : '';
  if (left === '' && right === '') {
    flash('请输入左右两侧路径后再开始比对');
    return;
  }
  doAction('compareapply', left + '\n' + right);
}

function cmpSwap() {
  doAction('compareswap');
}

function cmpOpen(index) {
  doAction('compareopen', String(index));
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

// ======== 主机密钥确认（首次连接 / 密钥变更阻断） ========

function renderHostKeyDialog(pending) {
  const existing = document.getElementById('hostKeyDialog');
  if (!pending) {
    if (existing) { existing.remove(); }
    return;
  }
  if (existing) { return; }
  const mismatch = pending.errorCode === 'HOST_KEY_MISMATCH';
  let body = '<div class="hostKeyBox">'
    + '<div class="hostKeyTitle">' + (mismatch ? '主机密钥不匹配（连接已阻断）' : '确认主机指纹') + '</div>'
    + '<div class="hostKeyLine">主机：' + esc(pending.host) + ':' + pending.port + '</div>'
    + '<div class="hostKeyLine">算法：' + esc(pending.keyType) + '</div>'
    + '<div class="hostKeyLine">SHA256：<span class="hostKeyPrint">' + esc(pending.fingerprintSha256) + '</span></div>';
  if (mismatch) {
    body += '<div class="hostKeyWarn">已信任指纹：' + esc(pending.expectedFingerprintSha256) + '</div>'
      + '<div class="hostKeyWarn">当前指纹与记录不一致，可能是服务器更换密钥，也可能是中间人攻击。删除旧记录后重新连接仍需再次确认新指纹。</div>';
  } else {
    body += '<div class="hostKeyHint">请与服务器管理员核对指纹。信任后会写入 known_hosts，下次连接自动校验，不再提示。</div>';
  }
  body += '<div class="hostKeyBtns">';
  if (mismatch) {
    body += '<span class="btn" onclick="hostKeyAction(\'connhostkeyforget\')">删除旧记录并重新连接</span>';
  } else {
    body += '<span class="btn" onclick="hostKeyAction(\'connhostkeytrust\')">信任并保存</span>';
  }
  body += '<span class="btn" onclick="hostKeyAction(\'connhostkeycancel\')">取消</span></div></div>';
  const dialog = document.createElement('div');
  dialog.id = 'hostKeyDialog';
  dialog.className = 'hostKeyMask';
  dialog.innerHTML = body;
  document.body.appendChild(dialog);
}

function hostKeyAction(action) {
  doAction(action);
}

// ======== 启动项管理（与桌面 StartupPanel 同构） ========
let startupEditingId = '';

function startupKindIndex(itemType) {
  if (itemType === 'Shortcut') { return 1; }
  if (itemType === 'Script') { return 2; }
  if (itemType === 'WebLink') { return 3; }
  return 0;
}

function renderStartup(tab) {
  const systemRows = tab.startupRows || [];
  const customRows = tab.customStartupRows || [];
  let html = '<div class="connToolbar">'
    + '<span class="btn" onclick="clickBtn(null,\'startuprefresh\')">刷新</span>'
    + '<span class="btn' + (permissions.canCreateDelete ? '' : ' disabled') + '" onclick="startupNew()">新建自定义项</span>'
    + '<span class="btn' + (permissions.canCreateDelete ? '' : ' disabled') + '" onclick="startupSave()">保存</span>'
    + '<span class="btn' + (permissions.canCreateDelete ? '' : ' disabled') + '" onclick="startupDelete()">删除</span>'
    + '<span class="btn' + (permissions.canModify ? '' : ' disabled') + '" onclick="startupRun()">立即运行</span>'
    + '</div>';
  if (tab.startupEnvironmentText) {
    html += '<div class="hint">' + esc(tab.startupEnvironmentText) + '</div>';
  }
  html += '<div class="hint">自定义启动项在本程序每次开机后首次启动时检查；本次开机已拉起过的条目不会重复拉起。</div>';

  html += '<div class="hint">系统启动项（程序与服务）</div>';
  html += tableOpenHtml('startup', [180, 80, 90, 80, 320, 60, 80])
    + '<tr><th>名称</th><th>类型</th><th>来源</th><th>范围</th><th>启动目标</th><th>状态</th><th>操作</th></tr>';
  if (systemRows.length === 0) {
    html += '<tr><td class="hint" colspan="7">暂无系统启动项，或当前平台不支持读取</td></tr>';
  }
  for (const row of systemRows) {
    const actionClass = row.canToggle && permissions.canModify ? 'btn' : 'btn disabled';
    html += '<tr>'
      + '<td class="name"><span class="text" title="' + escAttr(row.commandText) + '">' + esc(row.name)
      + (row.isSelfAutoStart ? '（本程序）' : '') + '</span></td>'
      + '<td>' + esc(row.kindText) + '</td>'
      + '<td>' + esc(row.sourceText) + '</td>'
      + '<td>' + esc(row.scopeText) + '</td>'
      + '<td class="clip" title="' + escAttr(row.commandText) + '">' + esc(row.commandText) + '</td>'
      + '<td>' + esc(row.stateText) + '</td>'
      + '<td><span class="' + actionClass + '" data-id="' + escAttr(row.id) + '"'
      + ' title="' + escAttr(row.toggleHint || '') + '"'
      + ' onclick="startupToggleRow(this)">' + (row.enabled ? '禁用' : '启用') + '</span></td></tr>';
  }
  html += '</table>';

  html += '<div class="hint">自定义启动项（程序 / 快捷方式 / 脚本 / 网页链接）</div>';
  html += tableOpenHtml('startupcustom', [180, 90, 300, 100, 170, 70, 190])
    + '<tr><th>名称</th><th>类型</th><th>启动目标</th><th>延迟</th><th>本次开机</th><th>状态</th><th>操作</th></tr>';
  if (customRows.length === 0) {
    html += '<tr><td class="hint" colspan="7">暂无自定义启动项，可在下方表单新建</td></tr>';
  }
  for (const row of customRows) {
    html += '<tr class="row startupCustomRow" data-id="' + escAttr(row.id) + '" onclick="startupSelectRow(this)">'
      + '<td class="name"><span class="text" title="' + escAttr(row.targetText) + '">' + esc(row.name) + '</span></td>'
      + '<td>' + esc(row.kindText) + '</td>'
      + '<td class="clip" title="' + escAttr(row.targetText) + '">' + esc(row.targetText) + '</td>'
      + '<td>' + esc(row.delayText) + '</td>'
      + '<td>' + esc(row.bootStateText) + '</td>'
      + '<td>' + esc(row.stateText) + '</td>'
      + '<td><span class="btn' + (permissions.canModify ? '' : ' disabled') + '" onclick="event.stopPropagation();startupRunRow(this)" data-id="' + escAttr(row.id) + '">运行</span>'
      + '<span class="btn' + (permissions.canModify ? '' : ' disabled') + '" onclick="event.stopPropagation();startupToggleCustomRow(this)" data-id="' + escAttr(row.id) + '">'
      + (row.enabled ? '禁用' : '启用') + '</span>'
      + '<span class="btn' + (permissions.canCreateDelete ? '' : ' disabled') + '" onclick="event.stopPropagation();startupDeleteRow(this)" data-id="' + escAttr(row.id) + '">删除</span></td></tr>';
  }
  html += '</table>';

  const selected = startupEditingId
    ? customRows.find(function (row) { return row.id === startupEditingId; })
    : null;
  const value = selected || { name: '', itemType: 'Program', target: '', arguments: '', delaySeconds: 0, enabled: true };
  html += '<div class="connForm">'
    + '<label>名称</label><input id="startupName" class="wide" value="' + escAttr(value.name) + '">'
    + '<label>类型</label><select id="startupKind">'
    + '<option value="0"' + (startupKindIndex(value.itemType) === 0 ? ' selected' : '') + '>程序</option>'
    + '<option value="1"' + (startupKindIndex(value.itemType) === 1 ? ' selected' : '') + '>快捷方式</option>'
    + '<option value="2"' + (startupKindIndex(value.itemType) === 2 ? ' selected' : '') + '>脚本</option>'
    + '<option value="3"' + (startupKindIndex(value.itemType) === 3 ? ' selected' : '') + '>网页链接</option></select>'
    + '<label>延迟秒数</label><input id="startupDelay" value="' + escAttr(String(value.delaySeconds)) + '" placeholder="0-600">'
    + '<label>启动目标</label><input id="startupTarget" class="wide" value="' + escAttr(value.target) + '" placeholder="程序路径 / 快捷方式 / 脚本 / https://...">'
    + '<label>启动参数</label><input id="startupArguments" class="wide" value="' + escAttr(value.arguments) + '">'
    + '<label>启用</label><label class="startupCheck"><input id="startupEnabled" type="checkbox"'
    + (value.enabled ? ' checked' : '') + '> 启用该自定义启动项</label>'
    + '</div>';
  return html;
}

// 系统启动项：按行内按钮回传的编号启停
function startupToggleRow(element) {
  if (!permissions.canModify) { flash('该端口未放行修改权限，无法启停系统启动项'); return; }
  if (!element || element.classList.contains('disabled')) { flash('该条目不允许从本程序修改'); return; }
  clickBtn(null, 'startuptoggle', element.getAttribute('data-id'));
}

// 自定义启动项：点击行回填编辑表单
function startupSelectRow(element) {
  if (!element || !currentTab) { return; }
  const id = element.getAttribute('data-id');
  const rows = currentTab.customStartupRows || [];
  const row = rows.find(function (item) { return item.id === id; });
  if (!row) { return; }
  startupEditingId = row.id;
  document.getElementById('startupName').value = row.name || '';
  document.getElementById('startupKind').value = String(startupKindIndex(row.itemType));
  document.getElementById('startupTarget').value = row.target || '';
  document.getElementById('startupArguments').value = row.arguments || '';
  document.getElementById('startupDelay').value = String(row.delaySeconds == null ? 0 : row.delaySeconds);
  document.getElementById('startupEnabled').checked = !!row.enabled;
  for (const element of document.querySelectorAll('.startupCustomRow')) {
    element.classList.toggle('checked', element.getAttribute('data-id') === id);
  }
  flash('已载入自定义启动项：' + row.name);
}

function startupNew() {
  startupEditingId = '';
  const textIds = ['startupName', 'startupTarget', 'startupArguments', 'startupDelay'];
  for (const id of textIds) {
    const element = document.getElementById(id);
    if (element) { element.value = ''; }
  }
  const kind = document.getElementById('startupKind');
  if (kind) { kind.value = '0'; }
  const enabled = document.getElementById('startupEnabled');
  if (enabled) { enabled.checked = true; }
  for (const element of document.querySelectorAll('.startupCustomRow')) {
    element.classList.remove('checked');
  }
  flash('已切换到新建自定义启动项，填写完成后再点「保存」');
}

function startupSave() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限，无法保存自定义启动项'); return; }
  function valueOf(id) {
    const element = document.getElementById(id);
    return element ? element.value : '';
  }
  const values = [
    valueOf('startupName'), valueOf('startupKind'), valueOf('startupTarget'),
    valueOf('startupArguments'), valueOf('startupDelay'),
    document.getElementById('startupEnabled') && document.getElementById('startupEnabled').checked ? '1' : '0'
  ];
  doAction('startupcustomsave', [startupEditingId].concat(values).join('\n'));
}

function startupDelete() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限，无法删除自定义启动项'); return; }
  if (!startupEditingId) { flash('请先在下方列表选中要删除的自定义启动项'); return; }
  const id = startupEditingId;
  startupEditingId = '';
  doAction('startupcustomdelete', id);
}

function startupRun() {
  if (!startupEditingId) { flash('请先在下方列表选中要立即运行的自定义启动项'); return; }
  doAction('startupcustomrun', startupEditingId);
}

function startupRunRow(element) {
  if (!permissions.canModify) { flash('该端口未放行修改权限，无法立即运行自定义启动项'); return; }
  doAction('startupcustomrun', element.getAttribute('data-id'));
}

function startupToggleCustomRow(element) {
  if (!permissions.canModify) { flash('该端口未放行修改权限，无法切换自定义启动项'); return; }
  doAction('startupcustomtoggle', element.getAttribute('data-id'));
}

function startupDeleteRow(element) {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限，无法删除自定义启动项'); return; }
  const id = element.getAttribute('data-id');
  if (id === startupEditingId) { startupEditingId = ''; }
  doAction('startupcustomdelete', id);
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

