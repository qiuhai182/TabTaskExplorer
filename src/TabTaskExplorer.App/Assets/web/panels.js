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
    const needsPassword = item.protocol === 'SSH' && item.authType === 'Password' && !item.hasSecret;
    html += '<tr class="connRow' + (item.selected ? ' selected' : '') + '"'
      + ' onclick="clickBtn(null,\'connselect\',\'' + escAttr(item.id) + '\')">'
      + '<td>' + esc(item.name) + (needsPassword ? ' <span class="connWarn">需补密码</span>' : '')
      + '</td><td>' + esc(item.group) + '</td><td>' + esc(item.summary) + '</td></tr>';
  }
  html += '</table>';
  const selected = items.find(function (item) { return item.selected; }) || null;
  connEditingId = selected ? selected.id : '';
  const value = selected || {
    id: '', name: '', group: '', protocol: 'SSH', host: '', port: 0, username: '',
    authType: 'Password', privateKeyPath: '', keepAliveSeconds: 15, jumpHops: []
  };
  const hopLines = (value.jumpHops || []).map(function (hop) {
    return [hop.host || '', hop.port > 0 ? hop.port : '', hop.username || '',
      hop.authType || 'Password', hop.privateKeyPath || '', ''].join('|');
  }).join('\n');
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
    + '<label>跳板链</label><textarea id="connHops" class="wide" rows="3" placeholder="每行一跳：主机|端口|用户名|认证方式(Password/PrivateKey)|私钥路径|密码（留空保持）">' + esc(hopLines) + '</textarea>'
    + '</div>'
    + '<div class="hint">跳板链最多 3 跳，按填写顺序逐跳连接；每跳独立校验主机指纹。Xshell / Xftp 导入的连接不含密码与私钥路径，请在上方补填后保存。</div>';
  return html;
}

function connNew() {
  connEditingId = '';
  const textIds = ['connName', 'connGroup', 'connHost', 'connPort', 'connUser', 'connKey', 'connKeep', 'connSecret', 'connHops'];
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
    valueOf('connKeep'), valueOf('connSecret'), valueOf('connHops')
  ];
}

function connSave() {
  if (!permissions.canCreateDelete) { flash('该端口未放行新建与删除权限，无法保存连接'); return; }
  const values = connFieldValues();
  // 前 11 段固定字段，第 12 段起为跳板链（每行一跳），服务端按行解析
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
    + '<div class="hostKeyLine">位置：' + (pending.hopIndex > 0 ? '第 ' + pending.hopIndex + ' 跳（跳板机）' : '目标主机') + '</div>'
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


// ======== 截图与录屏（分组与桌面 CapturePanel 一致；已接动作可点，未接动作置灰说明） ========

let captureState = null;
let captureError = '';
let captureTimer = null;

function openCapture() {
  const modal = document.getElementById('capturePanel');
  if (!modal) { return; }
  modal.classList.remove('hidden');
  renderCaptureLoading();
  loadCaptureState();
  if (captureTimer) { clearInterval(captureTimer); }
  captureTimer = setInterval(loadCaptureState, 2000);
}

function closeCapture() {
  const modal = document.getElementById('capturePanel');
  if (modal) { modal.classList.add('hidden'); }
  if (captureTimer) { clearInterval(captureTimer); captureTimer = null; }
}

async function loadCaptureState() {
  try {
    const response = await apiFetch('/api/capture/state');
    if (!response.ok) { throw new Error('HTTP ' + response.status); }
    captureState = await response.json();
    captureError = '';
  } catch (error) {
    captureState = null;
    captureError = error && error.message ? error.message : '未知错误';
  }
  renderCapture();
}

function renderCaptureLoading() {
  const body = document.getElementById('captureBody');
  if (body) { body.innerHTML = '<div class="capState">正在读取截图状态…</div>'; }
}

function captureDisabledButton(label, reason) {
  return '<span class="btn disabled" title="' + esc(reason) + '">' + label + '</span>';
}

function renderCapture() {
  const body = document.getElementById('captureBody');
  if (!body) { return; }
  if (!captureState) {
    body.innerHTML = '<div class="capGroup"><div class="capGroupHead">截图与录屏</div>'
      + '<div class="capState">读取截图状态失败：' + esc(captureError || '未知错误') + '</div>'
      + '<div class="capBar"><span class="btn" onclick="loadCaptureState()">重新读取</span></div></div>';
    return;
  }
  const state = captureState;
  const items = Array.isArray(state.items) ? state.items : [];
  let html = '';

  // 1. 截图动作：已接全屏 / 区域 / 贴图；其余保留并置灰
  html += '<div class="capGroup"><div class="capGroupHead">截图</div>'
    + '<div class="capBar">'
    + '<span class="btn" onclick="captureAction(\'fullscreen\')">全屏截图</span>'
    + '<span class="btn" onclick="captureAction(\'region\')">区域截图</span>'
    + '<span class="btn" onclick="captureAction(\'pin\')">贴图截图</span>'
    + captureDisabledButton('主屏截图', '网页端尚未接入该截图方式，请在桌面端使用')
    + captureDisabledButton('滚动长截图', '网页端尚未接入该截图方式，请在桌面端使用')
    + captureDisabledButton('活动窗口', '网页端尚未接入该截图方式，请在桌面端使用')
    + captureDisabledButton('上次区域', '网页端尚未接入该截图方式，请在桌面端使用')
    + captureDisabledButton('屏幕取色', '网页端尚未接入该截图方式，请在桌面端使用')
    + '</div>'
    + '<div class="capNote">延迟：' + (state.delaySeconds || 0) + ' 秒（网页端固定使用桌面设置的延迟，暂不可改）</div>'
    + '</div>';

  // 2. 截图选项：仅贴图保存可写，其余由桌面截图管线读取
  const pinSaveChecked = state.pinSave ? ' checked' : '';
  html += '<div class="capGroup"><div class="capGroupHead">截图选项</div>'
    + '<label class="capRow"><input type="checkbox"' + pinSaveChecked
    + ' onchange="captureAction(\'pinSave\', this.checked ? \'on\' : \'off\')">贴图截图保存产物</label>'
    + '<div class="capRow">'
    + captureDisabledButton('完成后自动打开产物文件夹', '该选项由桌面端截图管线读取，网页端暂不可改')
    + captureDisabledButton('截图后自动打开标注编辑器', '该选项由桌面端截图管线读取，网页端暂不可改')
    + captureDisabledButton('包含鼠标指针', '该选项由桌面端截图管线读取，网页端暂不可改')
    + '</div>'
    + '<div class="capNote">采集后端：' + esc(state.backend || 'auto') + '（由桌面端设置决定，网页端暂不可改）</div>'
    + '</div>';

  // 3. 录屏：网页端无桥接，整组置灰
  html += '<div class="capGroup"><div class="capGroupHead">录屏</div>'
    + '<div class="capBar">'
    + captureDisabledButton('开始录制', '网页端尚未接入录屏，请在桌面端录制')
    + captureDisabledButton('停止录制', '网页端尚未接入录屏，请在桌面端录制')
    + '</div>'
    + '<div class="capRow">'
    + captureDisabledButton('录制目标', '网页端尚未接入录屏，请在桌面端录制')
    + captureDisabledButton('麦克风', '网页端尚未接入录屏，请在桌面端录制')
    + captureDisabledButton('帧率 / 质量', '网页端尚未接入录屏，请在桌面端录制')
    + '</div>'
    + '<div class="capNote">网页端尚未接入录屏，请在桌面端录制。</div>'
    + '</div>';

  // 4. 产物历史：缩略图 + 删除 / 复制；打开与标注为桌面行为
  html += '<div class="capGroup"><div class="capGroupHead">产物历史（' + items.length + '）</div>'
    + '<div class="capBar"><span class="btn" onclick="loadCaptureState()">刷新</span>'
    + captureDisabledButton('打开产物目录', '产物在桌面本机，网页端不打开本地窗口') + '</div>';
  if (items.length === 0) {
    html += '<div class="capState">暂无截图产物</div>';
  } else {
    for (const item of items) {
      const file = item.fileName || item.fullPath || '未命名产物';
      const thumb = item.thumbnailUrl
        ? '<img src="' + escAttr(item.thumbnailUrl) + '" alt="" onerror="this.style.display=\'none\'">'
        : uiIcon('camera', 18, '#8A8F99');
      html += '<div class="capItem">'
        + '<span class="capThumb">' + thumb + '</span>'
        + '<span class="capMeta"><span class="capName" title="' + escAttr(item.fullPath || file) + '">' + esc(file) + '</span>'
        + '<span class="capSub">' + esc(item.modifiedTime || '') + ' · ' + esc(item.sizeText || '') + '</span></span>'
        + '<span class="capBar">'
        + '<span class="btn" onclick="captureAction(\'copy\',\'' + escAttr(item.fullPath || '') + '\')">复制到剪贴板</span>'
        + '<span class="btn" onclick="captureAction(\'delete\',\'' + escAttr(item.fullPath || '') + '\')">删除</span>'
        + captureDisabledButton('打开', '产物在桌面本机，网页端不打开本地窗口')
        + captureDisabledButton('标注此图', '标注编辑器仅桌面端可用')
        + '</span></div>';
    }
  }
  html += '</div>';

  // 5. 状态栏：保存目录 / 命名模板 / 格式
  html += '<div class="capGroup"><div class="capGroupHead">输出</div>'
    + '<div class="capNote">保存目录：' + esc(state.outputDirectory || '未设置') + '</div>'
    + '<div class="capNote">命名模板：' + esc(state.fileNameTemplate || '未设置') + ' · 格式：' + esc(state.format || '未设置') + '</div>'
    + '</div>';

  body.innerHTML = html;
}

async function captureAction(action, argument) {
  const arg = argument == null ? '' : argument;
  const url = '/api/capture/action?a=' + encodeURIComponent(action)
    + (arg ? '&arg=' + encodeURIComponent(arg) : '');
  try {
    const response = await apiFetch(url, { method: 'POST' });
    if (response.status === 403) {
      flash('当前端口未放行修改权限，无法执行该截图操作');
      return;
    }
    let data = null;
    try { data = await response.json(); } catch (error) { data = null; }
    if (!data) {
      flash('截图操作失败：服务未返回结果');
      return;
    }
    flash(data.message || (data.ok ? '操作已完成' : '操作未执行'));
    setTimeout(loadCaptureState, 600);
  } catch (error) {
    flash('截图操作失败：' + (error && error.message ? error.message : '网络错误'));
  }
}


// ======== 设置面板（分组与桌面 SettingsWindow 一致；/api/settings 已接的分组可写） ========

const SETTINGS_GROUPS = [
  { id: 'basic', title: '基本设置' },
  { id: 'shortcuts', title: '快捷操作', locked: 'shortcuts' },
  { id: 'files', title: '文件/文件夹', desktopOnly: '排序、列配置与只读目录等桌面选项' },
  { id: 'editor', title: '编辑器' },
  { id: 'openwith', title: '打开方式', locked: 'openwith', fallback: '默认打开方式需要系统文件关联，仅桌面端可用' },
  { id: 'media', title: '媒体', locked: 'media', fallback: '播放器设置由桌面内置播放器读取，网页端不使用' },
  { id: 'web', title: '网页端', locked: 'web', fallback: '网页服务地址与端口需在桌面端修改并重启服务' },
  { id: 'shellmenu', title: '右键菜单', locked: 'shellmenu', fallback: '系统右键菜单项仅桌面端可枚举与启停' },
  { id: 'startup', title: '启动项管理', locked: 'startup', fallback: '启动项需要写入系统注册表 / 启动目录，仅桌面端可用' },
  { id: 'capture', title: '截图与录屏' },
  { id: 'about', title: '关于' }
];

let settingsActive = 'basic';
let settingsData = null;
let settingsError = '';
let settingsLoading = false;

// 桌面专属入口：控件保留、点击给出可读说明，避免用户误以为是缺陷
function desktopOnly(name) {
  flash('「' + name + '」该功能仅桌面端可用');
}

function openSettings(group) {
  const modal = document.getElementById('settings');
  if (!modal) { return; }
  settingsActive = group || 'basic';
  renderSettingsNav();
  if (settingsData) {
    renderSettingsPane();
  } else {
    renderSettingsLoading();
  }
  modal.classList.remove('hidden');
  loadSettings();
}

function closeSettings() {
  const modal = document.getElementById('settings');
  if (modal) { modal.classList.add('hidden'); }
}

async function loadSettings() {
  settingsLoading = true;
  try {
    const response = await apiFetch('/api/settings');
    if (!response.ok) { throw new Error('HTTP ' + response.status); }
    settingsData = await response.json();
    settingsError = '';
  } catch (error) {
    settingsData = null;
    settingsError = error && error.message ? error.message : '未知错误';
  } finally {
    settingsLoading = false;
    renderSettingsNav();
    renderSettingsPane();
  }
}

function renderSettingsLoading() {
  const pane = document.getElementById('settingsPane');
  if (pane) { pane.innerHTML = '<div class="hint">正在读取设置…</div>'; }
}

function settingValue(key) {
  return settingsData && settingsData.values ? settingsData.values[key] : undefined;
}

async function applySetting(key, value, type) {
  const payload = {};
  payload[key] = type === 'bool' ? !!value : (type === 'number' ? Number(value) : value);
  try {
    const response = await apiFetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (response.status === 403) {
      flash('当前端口未放行修改权限，无法修改设置');
      loadSettings();
      return;
    }
    let data = null;
    try { data = await response.json(); } catch (error) { data = null; }
    if (!data) {
      flash('设置保存失败：服务未返回结果');
      loadSettings();
      return;
    }
    if (data.rejected && data.rejected.length > 0) {
      flash('未应用：' + data.rejected.map(item => item.reason).join('；'));
    } else {
      flash(data.message || '设置已保存');
    }
    loadSettings();
  } catch (error) {
    flash('设置保存失败：' + (error && error.message ? error.message : '网络错误'));
  }
}

function renderSettingsNav() {
  const nav = document.getElementById('settingsNav');
  if (!nav) { return; }
  let html = '';
  for (const group of SETTINGS_GROUPS) {
    const selected = group.id === settingsActive ? ' selected' : '';
    const locked = group.locked || group.desktopOnly;
    html += '<div class="navitem' + selected + '" onclick="switchSettingsGroup(\'' + group.id + '\')">'
      + '<span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">' + group.title + '</span>'
      + (locked ? '<span class="setNote">仅桌面</span>' : '')
      + '</div>';
  }
  nav.innerHTML = html;
}

function switchSettingsGroup(id) {
  settingsActive = id;
  renderSettingsNav();
  renderSettingsPane();
}

function renderSettingsPane() {
  const pane = document.getElementById('settingsPane');
  if (!pane) { return; }
  const group = SETTINGS_GROUPS.find(item => item.id === settingsActive) || SETTINGS_GROUPS[0];
  if (group.desktopOnly) {
    pane.innerHTML = '<div style="display:flex;flex-direction:column;gap:10px">'
      + '<div style="font-size:13px;font-weight:600">' + group.title + '</div>'
      + '<div class="hint">该分组仅桌面端可用：' + group.desktopOnly + '。网页端保留入口与位置，避免界面缺块。</div>'
      + '<div><span class="btn disabled" title="该功能仅桌面端可用">在桌面端打开设置</span></div>'
      + '</div>';
    return;
  }
  if (settingsActive === 'shortcuts') {
    pane.innerHTML = renderShortcutSettings();
    return;
  }
  if (group.locked && settingsActive !== 'web') {
    const reason = (settingsData && settingsData.locked && settingsData.locked[group.locked])
      || group.fallback || '该分组仅桌面端可用';
    pane.innerHTML = '<div style="display:flex;flex-direction:column;gap:10px">'
      + '<div style="font-size:13px;font-weight:600">' + group.title + '</div>'
      + '<div class="hint">' + esc(reason) + '。</div>'
      + '<div><span class="btn disabled" title="' + escAttr(reason) + '">在桌面端修改</span></div>'
      + '</div>';
    return;
  }
  if (settingsActive === 'web') {
    const webReason = (settingsData && settingsData.locked && settingsData.locked.web)
      || '网页服务地址与端口需在桌面端修改并重启服务';
    pane.innerHTML = '<div style="display:flex;flex-direction:column;gap:10px">'
      + '<div style="font-size:13px;font-weight:600">网页端</div>'
      + '<div class="hint">' + esc(webReason) + '；网页端只能查看当前地址。</div>'
      + '<div>当前地址：<code>' + location.origin + '</code></div>'
      + '<div><span class="btn disabled" title="请在桌面端修改网页端监听与权限">修改监听与权限</span></div>'
      + '</div>';
    return;
  }
  if (settingsActive === 'about') {
    pane.innerHTML = '<div style="display:flex;flex-direction:column;gap:10px">'
      + '<div style="font-size:13px;font-weight:600">关于</div>'
      + '<div class="hint">日志与诊断：桌面端在「设置 → 关于」生成诊断包；网页端日志显示在底部「诊断信息」。</div>'
      + '<div><span class="btn disabled" title="该功能仅桌面端可用">生成诊断包</span>'
      + ' <span class="btn" onclick="closeSettings();scrollDiagnostics()">查看网页诊断信息</span></div>'
      + '</div>';
    return;
  }
  if (!settingsData) {
    pane.innerHTML = '<div style="display:flex;flex-direction:column;gap:10px">'
      + '<div style="font-size:13px;font-weight:600">' + group.title + '</div>'
      + '<div class="hint">读取设置失败：' + esc(settingsError || '未知错误') + '</div>'
      + '<div><span class="btn" onclick="loadSettings()">重新读取</span></div>'
      + '</div>';
    return;
  }
  if (settingsActive === 'editor') {
    renderEditorSettings(pane);
    return;
  }
  if (settingsActive === 'capture') {
    renderCaptureSettings(pane);
    return;
  }
  renderBasicSettings(pane);
}

function settingCheckbox(key, label) {
  const checked = settingValue(key) ? ' checked' : '';
  return '<label class="setRow"><input type="checkbox"' + checked
    + ' onchange="applySetting(\'' + key + '\', this.checked, \'bool\')">' + label + '</label>';
}

function renderBasicSettings(pane) {
  const theme = settingValue('theme') || 'Light';
  pane.innerHTML = '<div style="display:flex;flex-direction:column;gap:10px">'
    + '<div style="font-size:13px;font-weight:600">基本设置</div>'
    + '<div class="hint">这些选项与桌面端共用同一份配置，修改后两端同时生效。</div>'
    + '<div class="setRow"><span class="setLabel">主题</span>'
    + '<select class="setInput" onchange="applySetting(\'theme\', this.value)">'
    + '<option value="Light"' + (theme === 'Light' ? ' selected' : '') + '>浅色</option>'
    + '<option value="Dark"' + (theme === 'Dark' ? ' selected' : '') + '>深色</option>'
    + '</select></div>'
    + settingCheckbox('showFileExtensions', '显示扩展名')
    + settingCheckbox('showHiddenFiles', '显示隐藏条目')
    + settingCheckbox('fileCompactMode', '紧凑模式')
    + settingCheckbox('closeToTray', '关闭窗口时最小化到托盘')
    + settingCheckbox('doubleClickTabClose', '双击标签关闭')
    + settingCheckbox('showToolbar', '显示工具栏')
    + settingCheckbox('showBookmarkBar', '显示书签栏')
    + settingCheckbox('restoreLastTabs', '启动时恢复上次标签')
    + '</div>';
}

function renderEditorSettings(pane) {
  const fontFamily = settingValue('editorFontFamily') || '';
  const fontSize = settingValue('editorFontSize') || 13;
  const tabWidth = settingValue('editorTabWidth') || 4;
  pane.innerHTML = '<div style="display:flex;flex-direction:column;gap:10px">'
    + '<div style="font-size:13px;font-weight:600">编辑器</div>'
    + '<div class="setRow"><span class="setLabel">字体</span>'
    + '<input class="setInput" style="flex:1;min-width:0" value="' + escAttr(fontFamily)
    + '" onchange="applySetting(\'editorFontFamily\', this.value)"></div>'
    + '<div class="setRow"><span class="setLabel">字号（8 - 48）</span>'
    + '<input class="setInput" type="number" min="8" max="48" value="' + fontSize
    + '" onchange="applySetting(\'editorFontSize\', this.value, \'number\')"></div>'
    + '<div class="setRow"><span class="setLabel">制表宽度（1 - 8）</span>'
    + '<input class="setInput" type="number" min="1" max="8" value="' + tabWidth
    + '" onchange="applySetting(\'editorTabWidth\', this.value, \'number\')"></div>'
    + settingCheckbox('editorInsertSpaces', '插入空格代替制表符')
    + settingCheckbox('editorWordWrap', '自动换行')
    + settingCheckbox('editorShowWhitespace', '显示空白字符')
    + '</div>';
}

function renderCaptureSettings(pane) {
  const backend = settingValue('captureBackend') || 'auto';
  const delay = settingValue('captureDelaySeconds');
  pane.innerHTML = '<div style="display:flex;flex-direction:column;gap:10px">'
    + '<div style="font-size:13px;font-weight:600">截图与录屏</div>'
    + '<div class="hint">以下选项与桌面端截图管线共用；录屏与标注窗口仅桌面端可用。</div>'
    + settingCheckbox('capturePinShotSaveFile', '贴图截图保存产物')
    + settingCheckbox('captureOpenFolderAfter', '完成后自动打开产物文件夹')
    + settingCheckbox('captureOpenEditorAfter', '截图后自动打开标注编辑器')
    + settingCheckbox('captureIncludeCursor', '包含鼠标指针')
    + '<div class="setRow"><span class="setLabel">截图延迟（0 - 30 秒）</span>'
    + '<input class="setInput" type="number" min="0" max="30" value="' + (delay == null ? 0 : delay)
    + '" onchange="applySetting(\'captureDelaySeconds\', this.value, \'number\')"></div>'
    + '<div class="setRow"><span class="setLabel">采集后端</span>'
    + '<select class="setInput" onchange="applySetting(\'captureBackend\', this.value)">'
    + '<option value="auto"' + (backend === 'auto' ? ' selected' : '') + '>自动</option>'
    + '<option value="wgc"' + (backend === 'wgc' ? ' selected' : '') + '>WGC</option>'
    + '<option value="gdi"' + (backend === 'gdi' ? ' selected' : '') + '>GDI</option>'
    + '</select></div>'
    + '<div class="capNote">录屏、录制目标、麦克风与帧率质量仅桌面端可用。</div>'
    + '</div>';
}

function renderShortcutSettings() {
  let html = '<div style="display:flex;flex-direction:column;gap:10px">'
    + '<div style="font-size:13px;font-weight:600">快捷操作</div>'
    + '<div class="hint">与桌面端共用同一套动作与默认按键；网页端未实现的条目保留并标注，不静默消失。</div>'
    + '<div style="display:flex;gap:6px">'
    + '<span class="btn disabled" title="该功能仅桌面端可用">添加</span>'
    + '<span class="btn disabled" title="该功能仅桌面端可用">清空</span>'
    + '<span class="btn disabled" title="该功能仅桌面端可用">恢复默认</span>'
    + '<span class="btn disabled" title="该功能仅桌面端可用">全局开关</span>'
    + '</div>'
    + '<table style="width:100%;border-collapse:collapse;font-size:12px">'
    + '<thead><tr>'
    + '<th style="text-align:left;padding:4px 6px;border-bottom:1px solid var(--tte-border)">动作</th>'
    + '<th style="text-align:left;padding:4px 6px;border-bottom:1px solid var(--tte-border)">按键</th>'
    + '<th style="text-align:left;padding:4px 6px;border-bottom:1px solid var(--tte-border)">作用域</th>'
    + '<th style="text-align:left;padding:4px 6px;border-bottom:1px solid var(--tte-border)">网页端</th>'
    + '</tr></thead><tbody>';
  for (const item of WEB_SHORTCUTS) {
    const dim = item.web.indexOf('仅') === 0 ? ' style="opacity:.55"' : '';
    html += '<tr' + dim + '>'
      + '<td style="padding:4px 6px;border-bottom:1px solid var(--tte-border)">' + item.label + '</td>'
      + '<td style="padding:4px 6px;border-bottom:1px solid var(--tte-border)"><code>' + item.keys + '</code></td>'
      + '<td style="padding:4px 6px;border-bottom:1px solid var(--tte-border)">' + item.scope + '</td>'
      + '<td style="padding:4px 6px;border-bottom:1px solid var(--tte-border)">' + item.web + '</td>'
      + '</tr>';
  }
  html += '</tbody></table></div>';
  return html;
}

// 底部诊断信息在设置面板关闭后滚动到可见位置
function scrollDiagnostics() {
  const box = document.querySelector('.diagBox');
  if (box) {
    box.setAttribute('open', 'open');
    box.scrollIntoView({ block: 'nearest' });
  }
}
