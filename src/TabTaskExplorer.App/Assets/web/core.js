// TabTaskExplorer 网页端核心模块：常量、图标、工具函数、会话与 API、导航动作。
// 加载顺序：core → views → panels → actions → ide → boot；本模块只声明全局函数与状态，不直接启动页面。

const PORT_LABEL = '127.0.0.1:__PORT__';
const VIEW_MODES = ['ExtraLargeIcons', 'LargeIcons', 'MediumIcons', 'SmallIcons', 'List', 'Details', 'Tile', 'Content'];
const ICON_MODES = ['ExtraLargeIcons', 'LargeIcons', 'MediumIcons', 'SmallIcons', 'Tile'];

// ===== 界面图标路径表（与桌面端 VectorIcon 使用同一套 24×24 图形） =====
const UI_PATHS = {
  plus: 'M12 4.6 L12 19.4 M4.6 12 L19.4 12',
  board: 'M4.4 5.6 L19.6 5.6 L19.6 18.4 L4.4 18.4 Z M4.4 9.2 L19.6 9.2 M8.2 8.6 L8.2 15.4 M12 8.6 L12 12.6 M15.8 8.6 L15.8 14.4',
  code: 'M8.2 7.2 L3.2 12 L8.2 16.8 M15.8 7.2 L20.8 12 L15.8 16.8 M13.8 5.6 L10.2 18.4',
  sun: 'M12 8 A4 4 0 1 0 12 16 A4 4 0 1 0 12 8 M12 2.2 L12 4.2 M12 19.8 L12 21.8 M2.2 12 L4.2 12 M19.8 12 L21.8 12 M5.1 5.1 L6.5 6.5 M17.5 17.5 L18.9 18.9 M18.9 5.1 L17.5 6.5 M6.5 17.5 L5.1 18.9',
  view: 'M4.2 4.2 L10.2 4.2 L10.2 10.2 L4.2 10.2 Z M13.8 4.2 L19.8 4.2 L19.8 10.2 L13.8 10.2 Z M4.2 13.8 L10.2 13.8 L10.2 19.8 L4.2 19.8 Z M13.8 13.8 L19.8 13.8 L19.8 19.8 L13.8 19.8 Z',
  back: 'M20.4 12 L4.2 12 M11.2 5 L4.2 12 L11.2 19',
  forward: 'M3.6 12 L19.8 12 M12.8 5 L19.8 12 L12.8 19',
  up: 'M12 20.2 L12 3.8 M5 10.8 L12 3.8 L19 10.8',
  clock: 'M3 12 A9 9 0 1 0 21 12 A9 9 0 1 0 3 12 M12 6.8 L12 12.4 L16 14.4',
  refresh: 'M5 12 A7 7 0 0 1 12 5 M8.4 2.4 L12 5 L8.4 7.6 M19 12 A7 7 0 0 1 12 19 M15.6 16.4 L12 19 L15.6 21.6',
  bookmark: 'M6 3.4 L18 3.4 L18 20.6 L12 16.2 L6 20.6 Z',
  monitor: 'M3.4 4.4 L20.6 4.4 L20.6 16.2 L3.4 16.2 Z M9 20 L15 20 M12 16.2 L12 20',
  globe: 'M3 12 A9 9 0 1 0 21 12 A9 9 0 1 0 3 12 M3 12 L21 12 M12 3 C8.4 6.4 8.4 17.6 12 21 C15.6 17.6 15.6 6.4 12 3',
  search: 'M3.4 10.6 A7.2 7.2 0 1 0 17.8 10.6 A7.2 7.2 0 1 0 3.4 10.6 M16 17 L20.8 21.8',
  donut: 'M3 12 A9 9 0 1 0 21 12 A9 9 0 1 0 3 12 M12 7 A5 5 0 1 0 12 17 A5 5 0 1 0 12 7',
  activity: 'M2.4 12 L7 12 L9.6 5.4 L13.6 18.6 L16.2 12 L21.6 12',
  save: 'M4.4 4 L16.6 4 L19.6 7 L19.6 19.6 C19.6 20.4 19 21 18.2 21 L5.8 21 C5 21 4.4 20.4 4.4 19.6 Z M8 4 L8 9 L16 9 L16 4 M7.4 13 L16.6 13 L16.6 21 L7.4 21 Z',
  saveall: 'M8 4 L17.6 4 L20 6.4 L20 17.6 C20 18.4 19.4 19 18.6 19 L8 19 C7.2 19 6.6 18.4 6.6 17.6 L6.6 5.4 C6.6 4.6 7.2 4 8 4 Z M3.4 8 L3.4 21 L16.6 21',
  quickopen: 'M6.4 3 L15 3 L19.4 7.4 L19.4 11.4 M6.4 3 C5.5 3 4.9 3.6 4.9 4.5 L4.9 19.5 C4.9 20.4 5.5 21 6.4 21 L11.6 21 M15 3 L15 7.4 L19.4 7.4 M10.4 17.6 A4.4 4.4 0 1 0 19.2 17.6 A4.4 4.4 0 1 0 10.4 17.6 M18.2 19.6 L21.8 23.2',
  chevronright: 'M9 5.4 L15.6 12 L9 18.6',
  compare: 'M5 4.5 L5 19.5 M19 4.5 L19 19.5 M8.4 8.6 L11.8 12 L8.4 15.4 M15.6 8.6 L12.2 12 L15.6 15.4',
  power: 'M12 3.2 L12 11.6 M7.4 5.4 A7.6 7.6 0 1 0 16.6 5.4',
  close: 'M5.6 5.6 L18.4 18.4 M18.4 5.6 L5.6 18.4'
};

// ===== 文件分类符号路径表（与桌面端 FileTypeIcon 同款） =====
const FILE_SYMBOL_PATHS = {
  code: 'M10 9.6 L7.6 13 L10 16.4 M17 9.6 L19.4 13 L17 16.4 M14.6 8.8 L12.4 17.2',
  web: 'M8.4 13.7 A4.6 4.6 0 1 0 17.6 13.7 A4.6 4.6 0 1 0 8.4 13.7 M8.4 13.7 L17.6 13.7 M13 9 C10.6 11 10.6 16.4 13 18.4 C15.4 16.4 15.4 11 13 9',
  text: 'M8.4 9.6 L18.4 9.6 M8.4 12.7 L18.4 12.7 M8.4 15.8 L18.4 15.8 M8.4 18.8 L15.4 18.8',
  log: 'M8.4 9.4 L18.4 9.4 M8.4 12.4 L18.4 12.4 M8.4 15.4 L18.4 15.4 M8.4 18.4 L13.4 18.4',
  document: 'M8.4 9.4 L18.4 9.4 M8.4 12.3 L18.4 12.3 M8.4 15.2 L18.4 15.2 M8.4 18.1 L14.2 18.1',
  markdown: 'M8 16 L10.2 9.4 L13 14.2 L15.8 9.4 L18 16 M13.4 8.2 L13.4 5.2 M11.6 6.8 L13.4 5.2 L15.2 6.8',
  pdf: 'M8.4 9.2 L18.6 9.2 M8.4 12.1 L18.6 12.1 M8.4 15 L14.6 15 M8.4 17.9 L18.6 17.9',
  spreadsheet: 'M8.2 9 L18.8 9 L18.8 17.6 L8.2 17.6 Z M8.2 12.6 L18.8 12.6 M13.5 9 L13.5 17.6',
  presentation: 'M8.2 17.2 L8.2 13 M12.2 17.2 L12.2 9 M16.2 17.2 L16.2 11.2 M7 18.4 L18.4 18.4',
  image: 'M7.8 16.8 L11 12.6 L13.2 15.2 L15.4 13 L18.6 16.8 Z M15.6 9.8 A1.4 1.4 0 1 0 18.4 9.8 A1.4 1.4 0 1 0 15.6 9.8',
  audio: 'M9 17.2 A3 3 0 1 0 15 17.2 A3 3 0 1 0 9 17.2 M12 17.2 L12 6.8 L18.8 5.4',
  video: 'M10 8.6 L18.6 13 L10 17.4 Z',
  archive: 'M8.2 8.8 L18.6 8.8 L18.6 17.2 L8.2 17.2 Z M13.4 9 L13.4 17 M11.6 10.4 L15.2 10.4 M11.6 12.4 L15.2 12.4 M11.6 14.4 L15.2 14.4 M11.6 16.4 L15.2 16.4',
  font: 'M8.6 17.4 L13.4 7.6 L18.2 17.4 M10.2 14 L16.6 14',
  database: 'M8.6 9.4 C8.6 8 10.8 7.2 13.4 7.2 C16 7.2 18.2 8 18.2 9.4 C18.2 10.8 16 11.6 13.4 11.6 C10.8 11.6 8.6 10.8 8.6 9.4 Z M8.6 9.4 L8.6 15.2 C8.6 16.6 10.8 17.4 13.4 17.4 C16 17.4 18.2 16.6 18.2 15.2 L18.2 9.4 M8.6 12.3 C8.6 13.7 10.8 14.5 13.4 14.5 C16 14.5 18.2 13.7 18.2 12.3',
  config: 'M8.4 9.4 L18.4 9.4 M8.4 13 L18.4 13 M8.4 16.6 L18.4 16.6 M10.6 9.4 A1.6 1.6 0 1 0 13.8 9.4 A1.6 1.6 0 1 0 10.6 9.4 M13.8 13 A1.6 1.6 0 1 0 17 13 A1.6 1.6 0 1 0 13.8 13 M9 16.6 A1.6 1.6 0 1 0 12.2 16.6 A1.6 1.6 0 1 0 9 16.6',
  executable: 'M8 8.4 L19 8.4 L19 17 L8 17 Z M8 11 L19 11 M10 9.7 L10 9.9 M12 9.7 L12 9.9',
  script: 'M8.4 9.6 L11.6 13 L8.4 16.4 M12.6 16.8 L17.6 16.8',
  diskimage: 'M8.6 13 A4.8 4.8 0 1 0 18.2 13 A4.8 4.8 0 1 0 8.6 13 M11.6 13 A1.8 1.8 0 1 0 15.2 13 A1.8 1.8 0 1 0 11.6 13',
  subtitle: 'M7.6 8.6 L19.2 8.6 C20 8.6 20.6 9.2 20.6 10 L20.6 15 C20.6 15.8 20 16.4 19.2 16.4 L13 16.4 L9.4 19.2 L9.4 16.4 L7.6 16.4 C6.8 16.4 6.2 15.8 6.2 15 L6.2 10 C6.2 9.2 6.8 8.6 7.6 8.6 Z M9.4 11.4 L17.4 11.4 M9.4 13.8 L14.6 13.8',
  torrent: 'M9 15.6 A4 4 0 1 0 17 15.6 A4 4 0 1 0 9 15.6 M13 16.4 L13 8.2 M10.2 11 L13 8.2 L15.8 11',
  email: 'M7 9 L19.4 9 L19.4 17 L7 17 Z M7 9 L13.2 13.6 L19.4 9',
  certificate: 'M8.8 10.6 A3.4 3.4 0 1 0 15.6 10.6 A3.4 3.4 0 1 0 8.8 10.6 M11 13.6 L9.6 18.2 L12.2 16.6 L14.8 18.2 L13.4 13.6',
  design: 'M7.8 17.6 L8.8 12.6 L14.4 7 L17.2 9.8 L11.6 15.4 Z M8.8 12.6 L11.6 15.4 M14.4 7 L17.2 9.8',
  binary: 'M8.6 8.6 L18.2 8.6 L18.2 17 L8.6 17 Z M11 8.6 L11 6.4 M15.8 8.6 L15.8 6.4 M11 17 L11 19.2 M15.8 17 L15.8 19.2 M8.6 11 L6.4 11 M8.6 14.8 L6.4 14.8 M18.2 11 L20.4 11 M18.2 14.8 L20.4 14.8 M11.6 11.4 L15.2 11.4 M11.6 14.6 L15.2 14.6',
  shortcut: 'M7.8 16.6 L13.4 16.6 C15 16.6 16.4 15.2 16.4 13.6 L16.4 8.6 M13 5.4 L16.6 8.6 L20.2 5.4',
  ebook: 'M7.4 8.6 L11.4 8.6 C12.2 8.6 13 9.2 13.4 10 L13.4 17 C12.9 16.2 12.2 15.8 11.4 15.8 L7.4 15.8 Z M13.4 10 C13.9 9.2 14.6 8.8 15.4 8.8 L19.4 8.8 L19.4 15.8 L15.4 15.8 C14.6 15.8 13.9 16.2 13.4 17 Z',
  package: 'M12.6 5.8 L19 9.2 L19 15.4 L12.6 18.8 L6.2 15.4 L6.2 9.2 Z M6.2 9.2 L12.6 12.6 L19 9.2 M12.6 12.6 L12.6 18.8',
  file: 'M8.4 10 L18.4 10 M8.4 13 L18.4 13 M8.4 16 L15.4 16'
};

function toHex(value) {
  const text = value.toString(16);
  return text.length < 2 ? '0' + text : text;
}

function mixColor(hex, target, amount) {
  const source = (hex || '#8A94A6').replace('#', '');
  const destination = target.replace('#', '');
  const sr = parseInt(source.substring(0, 2), 16);
  const sg = parseInt(source.substring(2, 4), 16);
  const sb = parseInt(source.substring(4, 6), 16);
  const dr = parseInt(destination.substring(0, 2), 16);
  const dg = parseInt(destination.substring(2, 4), 16);
  const db = parseInt(destination.substring(4, 6), 16);
  const r = Math.round(sr * (1 - amount) + dr * amount);
  const g = Math.round(sg * (1 - amount) + dg * amount);
  const b = Math.round(sb * (1 - amount) + db * amount);
  return '#' + toHex(r) + toHex(g) + toHex(b);
}

function uiIcon(kind, size, color) {
  const path = UI_PATHS[kind];
  if (!path) { return ''; }
  const stroke = color || 'currentColor';
  return '<svg viewBox="0 0 24 24" width="' + size + '" height="' + size
    + '" fill="none" stroke="' + stroke + '" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">'
    + '<path d="' + path + '"/></svg>';
}

function fileIcon(category, accent, size) {
  const cat = category || 'file';
  const color = accent || '#8A94A6';
  const dark = mixColor(color, '#000000', 0.22);
  const light = mixColor(color, '#FFFFFF', 0.3);
  let body = '';
  if (cat === 'folder') {
    body = '<path d="M2.6 19.6 L2.6 6.4 Q2.6 4.6 4.6 4.6 L9.6 4.6 L12 7 L21.2 7 L21.2 9.8 L2.6 9.8 Z" fill="' + dark + '"/>'
      + '<path d="M2.6 9.6 L21.2 9.6 L21.2 18.4 Q21.2 20.6 19 20.6 L4.8 20.6 Q2.6 20.6 2.6 18.4 Z" fill="' + color + '"/>'
      + '<rect x="4.6" y="11" width="15.6" height="1.2" rx="0.6" fill="#FFFFFF" fill-opacity="0.32"/>'
      + '<rect x="4.6" y="13.4" width="9.4" height="1" rx="0.5" fill="#FFFFFF" fill-opacity="0.14"/>';
  } else if (cat === 'drive') {
    body = '<rect x="2" y="6.4" width="20" height="11.6" rx="3" fill="' + color + '"/>'
      + '<path d="M5 6.4 L19 6.4 C21 6.4 22 8 22 10.6 L2 10.6 C2 8 3 6.4 5 6.4 Z" fill="' + light + '"/>'
      + '<rect x="2" y="10.6" width="20" height="0.6" fill="' + dark + '" fill-opacity="0.8"/>'
      + '<rect x="5" y="13.4" width="8.4" height="1.8" rx="0.9" fill="#FFFFFF" fill-opacity="0.72"/>'
      + '<circle cx="18.4" cy="14.3" r="1.2" fill="#FFFFFF" fill-opacity="0.92"/>';
  } else {
    const symbol = FILE_SYMBOL_PATHS[cat] || FILE_SYMBOL_PATHS.file;
    if (size < 20) {
      body = '<rect x="4.4" y="2.2" width="15.4" height="19.6" rx="2.6" fill="' + color + '" stroke="' + dark + '" stroke-width="0.8"/>'
        + '<path d="M14.6 2.2 L19.8 7.3 L16.4 7.3 Q14.6 7.3 14.6 5.5 Z" fill="#FFFFFF" fill-opacity="0.82"/>'
        + '<path d="' + symbol + '" fill="none" stroke="#FFFFFF" stroke-opacity="0.95" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>';
    } else {
      body = '<rect x="4.6" y="2.2" width="15.2" height="19.6" rx="2.5" fill="#FBFCFE" stroke="#0E172133" stroke-width="0.9"/>'
        + '<path d="M4.6 4.7 Q4.6 2.2 7.3 2.2 L7.6 2.2 L7.6 21.8 L7.3 21.8 Q4.6 21.8 4.6 19.3 Z" fill="' + color + '"/>'
        + '<path d="M14.8 2.2 L19.8 7.3 L16.6 7.3 Q14.8 7.3 14.8 5.5 Z" fill="' + light + '" stroke="#0E172133" stroke-width="0.7"/>'
        + '<path d="' + symbol + '" fill="none" stroke="' + color + '" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>';
    }
  }
  return '<svg viewBox="0 0 24 24" width="' + size + '" height="' + size + '" aria-hidden="true">' + body + '</svg>';
}

let workspaceId = '';
let permissions = { canModify: false, canCreateDelete: false, profile: '' };
let lastNotice = '';
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
// 表头列宽拖动：拖出的列宽按表格键保存，事件推送触发的重绘后按此恢复（仅本次页面打开期间有效，不持久化）
let tableColWidths = {};
let colDrag = null;
let suppressClickUntil = 0;

// 会话与安全令牌：Basic 仅用于首次认证，之后所有请求携带 Bearer 会话令牌；写操作再携带 CSRF 令牌
const rawFetch = window.fetch.bind(window);
let sessionToken = '';
let csrfToken = '';
// 事件推送：SSE 连接、当前作用域与重连计数
let eventSource = null;
let eventScope = '';
let eventRetryCount = 0;

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

// ======== 会话与安全令牌 ========

// 首次认证沿用浏览器已缓存的 Basic 凭据；之后由服务端签发会话令牌与 CSRF 令牌。
// 令牌只保存在页面内存中，不写入 localStorage，页面刷新后重新向服务端申请。
async function loadSession(force) {
  if (!force && sessionToken && csrfToken) { return true; }
  try {
    const response = await rawFetch('/api/session', { cache: 'no-store' });
    if (!response.ok) { return false; }
    const data = await response.json();
    sessionToken = data.sessionToken || '';
    csrfToken = data.csrfToken || '';
    return sessionToken !== '' && csrfToken !== '';
  } catch (error) {
    return false;
  }
}

function sendApi(path, options) {
  const headers = new Headers((options && options.headers) || {});
  if (sessionToken) { headers.set('Authorization', 'Bearer ' + sessionToken); }
  if (options && options.method && options.method !== 'GET' && csrfToken) {
    headers.set('X-CSRF-Token', csrfToken);
  }
  const init = Object.assign({}, options || {});
  init.headers = headers;
  if (!init.cache) { init.cache = 'no-store'; }
  return rawFetch(path, init);
}

// 统一请求入口：自动补会话令牌与 CSRF 令牌；会话过期时重新申请一次再重试。
async function apiFetch(path, options) {
  await loadSession(false);
  const response = await sendApi(path, options);
  if (response.status === 401) {
    sessionToken = '';
    csrfToken = '';
    const renewed = await loadSession(true);
    if (renewed) { return sendApi(path, options); }
  }
  return response;
}

function flash(text) {
  lastMessage = text;
  lastMessageTime = Date.now();
  renderStatus();
}

async function loadPermissions() {
  try {
    const response = await apiFetch('/api/permissions');
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
  apiFetch(url('/api/theme', { t: next }), { method: 'POST' }).then(refresh).catch(refresh);
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
    connectEvents();
    await refresh();
    return;
  }
  try {
    const response = await apiFetch('/api/mode?m=independent', { method: 'POST' });
    const data = await response.json();
    workspaceId = data.workspaceId || '';
    updateModeButton(false);
    connectEvents();
  } catch (error) { /* 创建失败保持镜像主界面 */ }
  await refresh();
}

// 首次加载默认进入独立新页面；失败时回落镜像主界面（仅首载执行一次，手动切回后不再自动切回）
async function initMode() {
  try {
    const response = await apiFetch('/api/mode?m=independent', { method: 'POST' });
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
    const response = await apiFetch(url('/api/state'));
    if (response.status === 410) {
      workspaceId = '';
      updateModeButton(true);
      connectEvents();
      return;
    }
    if (response.status === 403 && !workspaceId) {
      // 权限方案限定了可访问目录，镜像主界面不可用：自动切到独立新页面，由路径白名单限定浏览范围
      const modeResponse = await apiFetch('/api/mode?m=independent', { method: 'POST' });
      const modeData = await modeResponse.json();
      workspaceId = modeData.workspaceId || '';
      updateModeButton(false);
      connectEvents();
      flash('当前权限方案限定了可访问目录，已自动切换到独立新页面');
      return;
    }
    state = await response.json();
    render();
  } catch (error) {
    flash('网页端服务连接中断，正在重试……');
  }
  try {
    const diagResponse = await apiFetch('/api/diagnostics');
    const items = await diagResponse.json();
    document.getElementById('diag').textContent = items.length ? items.join('\n\n────────\n\n') : '（暂无）';
  } catch (error) { /* 诊断拉取失败忽略 */ }
}

// ======== 事件推送（SSE） ========

function setConnState(kind, text) {
  const element = document.getElementById('connState');
  if (!element) { return; }
  element.className = 'connState ' + kind;
  const label = document.getElementById('connStateText');
  if (label) { label.textContent = text; }
}

// 事件推送地址：EventSource 不能设置请求头，令牌通过查询串传递；服务端仅在 /api/events 接受该方式。
function connectEvents() {
  if (eventSource) { eventSource.close(); eventSource = null; }
  if (!sessionToken) { setConnState('offline', '未认证'); return; }
  eventScope = workspaceId;
  const query = new URLSearchParams();
  if (workspaceId) { query.set('w', workspaceId); }
  query.set('token', sessionToken);
  const source = new EventSource('/api/events?' + query.toString());
  eventSource = source;
  setConnState('reconnecting', '连接中');
  source.addEventListener('open', function () {
    eventRetryCount = 0;
    setConnState('online', '已连接');
  });
  source.addEventListener('snapshot', function (event) {
    applyEventState(event.data, true);
  });
  source.addEventListener('state', function (event) {
    applyEventState(event.data, false);
  });
  source.addEventListener('error', function () {
    if (!eventSource || eventSource !== source) { return; }
    setConnState('reconnecting', '重连中');
    eventRetryCount++;
    // 连续失败后重新申请会话令牌再重建连接；EventSource 自身仍会按浏览器策略自动重试
    if (eventRetryCount >= 5) {
      eventRetryCount = 0;
      loadSession(true).then(function (renewed) {
        if (renewed) { setTimeout(connectEvents, 1500); }
      });
    }
  });
}

// 应用事件推送的状态快照；只接收当前页面作用域的数据，避免旧工作区的事件覆盖新页面。
function applyEventState(text, isSnapshot) {
  let payload = null;
  try { payload = JSON.parse(text); } catch (error) { return; }
  if (!payload || !payload.state) { return; }
  if (payload.scope === 'desktop' && workspaceId) { return; }
  if (payload.scope !== 'desktop' && payload.scope !== workspaceId) { return; }
  state = payload.state;
  state.followDesktop = payload.scope === 'desktop';
  render();
  if (isSnapshot) { setConnState('online', '已连接'); }
}

async function post(path, params) {
  try { await apiFetch(url(path, params), { method: 'POST' }); } catch (error) { /* 网络失败由下一次轮询反映 */ }
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
    const response = await apiFetch(url('/api/action', params), { method: 'POST', body: body });
    const text = await response.text();
    if (!response.ok) {
      flash(text || ('操作失败，状态码=' + response.status));
      return;
    }
    if (text) { flash(text); }
    if (text && text.indexOf('任务中心') >= 0) { toggleTaskPanel(true); }
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

