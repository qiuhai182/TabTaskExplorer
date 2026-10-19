using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 浏览器调试镜像服务：在本机回环地址上开启一个轻量 HTTP 服务，
/// 浏览器打开后看到的页面与程序界面同构并实时同步，便于调试。
/// 只绑定 127.0.0.1，不监听外部网络；基于 TcpListener 实现，无额外依赖、无需端口授权。
/// </summary>
public class DebugWebHost
{
    /// <summary>默认服务端口。</summary>
    public const int DefaultPort = 15919;

    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private TcpListener? listener;
    private volatile bool running;
    private Func<Task<DebugUiState>>? snapshotProvider;
    private Action<int>? selectHandler;
    private Action<string>? themeHandler;

    private static readonly object diagnosticLock = new object();
    private static readonly List<string> diagnostics = new List<string>();
    private const int MaxDiagnostics = 100;

    /// <summary>记录一条诊断信息（UI 线程未处理异常等），供浏览器镜像的 /api/diagnostics 查看。</summary>
    public static void RecordDiagnostic(string source, Exception error)
    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + source + "] " + error.GetType().Name + ": " + error.Message
            + "\n" + error.StackTrace;
        if (line.Length > 4000)
        {
            line = line.Substring(0, 4000) + "…";
        }
        lock (diagnosticLock)
        {
            diagnostics.Add(line);
            if (diagnostics.Count > MaxDiagnostics)
            {
                diagnostics.RemoveAt(0);
            }
        }
        Trace.WriteLine("[" + source + "] " + error.Message);
    }

    /// <summary>读取全部诊断信息（新→旧）。</summary>
    public static string[] GetDiagnostics()
    {
        lock (diagnosticLock)
        {
            string[] copy = new string[diagnostics.Count];
            for (int i = 0; i < diagnostics.Count; i++)
            {
                copy[i] = diagnostics[diagnostics.Count - 1 - i];
            }
            return copy;
        }
    }

    /// <summary>实际监听的地址（启动成功后可读）。</summary>
    public string ListenUrl { get; private set; } = string.Empty;

    /// <summary>启动服务。端口被占用或其他网络错误时静默放弃（调试服务不影响主程序）。</summary>
    public void Start(Func<Task<DebugUiState>> snapshotProvider, Action<int> selectHandler, Action<string> themeHandler, int port = DefaultPort)
    {
        if (this.running)
        {
            return;
        }
        this.snapshotProvider = snapshotProvider;
        this.selectHandler = selectHandler;
        this.themeHandler = themeHandler;

        try
        {
            this.listener = new TcpListener(IPAddress.Loopback, port);
            this.listener.Start();
        }
        catch (Exception error)
        {
            Trace.WriteLine("调试镜像服务启动失败：" + error.Message);
            this.listener = null;
            return;
        }

        this.running = true;
        this.ListenUrl = "http://127.0.0.1:" + port + "/";
        Thread acceptThread = new Thread(this.AcceptLoop);
        acceptThread.IsBackground = true;
        acceptThread.Start();
        Trace.WriteLine("调试镜像服务已启动：" + this.ListenUrl);
    }

    /// <summary>停止服务（程序退出时由后台线程自动结束，无需显式调用）。</summary>
    public void Stop()
    {
        this.running = false;
        try
        {
            this.listener?.Stop();
        }
        catch
        {
            // 忽略停止时的套接字错误
        }
    }

    /// <summary>接受连接循环：每个连接交给独立后台线程处理。</summary>
    private void AcceptLoop()
    {
        while (this.running)
        {
            TcpClient client;
            try
            {
                client = this.listener!.AcceptTcpClient();
            }
            catch
            {
                if (this.running)
                {
                    continue;
                }
                break;
            }
            Thread handleThread = new Thread(new ThreadStart(delegate { this.HandleClient(client); }));
            handleThread.IsBackground = true;
            handleThread.Start();
        }
    }

    /// <summary>处理单个 HTTP 请求。</summary>
    private void HandleClient(TcpClient client)
    {
        try
        {
            client.ReceiveTimeout = 5000;
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                string requestLine = ReadRequestLine(stream);
                if (requestLine.Length == 0)
                {
                    return;
                }
                string[] parts = requestLine.Split(' ');
                if (parts.Length < 2)
                {
                    return;
                }
                string method = parts[0];
                string rawUrl = parts[1];
                string path = rawUrl;
                string query = string.Empty;
                int queryStart = rawUrl.IndexOf('?');
                if (queryStart >= 0)
                {
                    path = rawUrl.Substring(0, queryStart);
                    query = rawUrl.Substring(queryStart + 1);
                }

                if (path == "/" || path == "/index.html")
                {
                    WriteResponse(stream, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(DebugPageHtml));
                }
                else if (path == "/api/state" && method == "GET")
                {
                    DebugUiState? state = this.snapshotProvider != null ? this.snapshotProvider().GetAwaiter().GetResult() : null;
                    string json = JsonSerializer.Serialize(state, jsonOptions);
                    WriteResponse(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                }
                else if (path == "/api/select" && method == "POST")
                {
                    int index;
                    if (this.selectHandler != null && int.TryParse(GetQueryValue(query, "i"), out index))
                    {
                        this.selectHandler(index);
                    }
                    WriteResponse(stream, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("ok"));
                }
                else if (path == "/api/theme" && method == "POST")
                {
                    string theme = GetQueryValue(query, "t");
                    if (this.themeHandler != null && (theme == "Dark" || theme == "Light"))
                    {
                        this.themeHandler(theme);
                    }
                    WriteResponse(stream, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("ok"));
                }
                else if (path == "/api/diagnostics" && method == "GET")
                {
                    string json = JsonSerializer.Serialize(GetDiagnostics(), jsonOptions);
                    WriteResponse(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                }
                else
                {
                    WriteResponse(stream, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found"), 404);
                }
            }
        }
        catch
        {
            // 单个连接的任何错误都不影响服务继续运行
        }
    }

    /// <summary>读取请求首行（GET /path HTTP/1.1）。</summary>
    private static string ReadRequestLine(NetworkStream stream)
    {
        byte[] buffer = new byte[4096];
        StringBuilder builder = new StringBuilder();
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (builder.Length < 8192 && DateTime.UtcNow < deadline)
        {
            if (!stream.DataAvailable)
            {
                Thread.Sleep(10);
                continue;
            }
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }
            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            int lineEnd = builder.ToString().IndexOf("\r\n", StringComparison.Ordinal);
            if (lineEnd >= 0)
            {
                return builder.ToString(0, lineEnd);
            }
        }
        return builder.ToString();
    }

    /// <summary>从查询串取参数值。</summary>
    private static string GetQueryValue(string query, string key)
    {
        foreach (string pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && pair.Substring(0, eq) == key)
            {
                return Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
        }
        return string.Empty;
    }

    /// <summary>写出 HTTP 响应。</summary>
    private static void WriteResponse(NetworkStream stream, string contentType, byte[] body, int statusCode = 200)
    {
        string header = "HTTP/1.1 " + statusCode + " " + (statusCode == 200 ? "OK" : "Not Found") + "\r\n"
            + "Content-Type: " + contentType + "\r\n"
            + "Content-Length: " + body.Length + "\r\n"
            + "Cache-Control: no-store\r\n"
            + "Connection: close\r\n"
            + "\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    /// <summary>内嵌调试页面：TabX 风格，深浅色跟随程序主题，1.5 秒轮询刷新。</summary>
    private const string DebugPageHtml = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""utf-8"">
<title>TabTaskExplorer 调试镜像</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font:12px 'Segoe UI','Microsoft YaHei',sans-serif; color:#1C1C1C; background:#FFFFFF; }
  .titlebar { height:36px; background:#F1F1F1; border-bottom:1px solid #D9D9D9; display:flex; align-items:center; padding:0 8px; gap:4px; }
  .logo { width:16px; height:16px; border-radius:3px; background:#4A8FD0; color:#fff; font-size:10px; line-height:16px; text-align:center; }
  .tab { height:28px; line-height:28px; padding:0 12px; border-radius:4px 4px 0 0; cursor:pointer; border-bottom:2px solid transparent; }
  .tab:hover { background:#E4E4E4; }
  .tab.active { background:#FFFFFF; border-bottom:2px solid #4A8FD0; }
  .url { margin-left:auto; color:#808080; }
  .bookmarkbar { height:32px; background:#FFFFFF; border-bottom:1px solid #E9E9E9; display:flex; align-items:center; padding:0 12px; color:#808080; }
  .content { padding:10px 14px; }
  .path { font-weight:bold; margin-bottom:8px; }
  table { border-collapse:collapse; width:100%; max-width:980px; }
  th { text-align:left; font-weight:normal; color:#808080; border-bottom:1px solid #E0E0E0; padding:4px 10px; }
  td { padding:4px 10px; }
  tr.row:hover { background:#F5F7FA; }
  tr.row.checked { background:#DCE9F5; }
  .board { display:flex; gap:12px; max-width:1100px; }
  .col { flex:1; border:1px solid #E0E0E0; border-radius:8px; padding:8px; }
  .col h3 { font-size:12px; margin-bottom:6px; }
  .card { border:1px solid #E8E8E8; border-radius:6px; padding:6px 8px; margin-bottom:6px; }
  .hint { color:#808080; margin:6px 0; }
  .time { color:#808080; margin-bottom:8px; }
  body.dark { background:#191919; color:#E8E8E8; }
  body.dark .titlebar { background:#212121; border-bottom-color:#323232; }
  body.dark .tab:hover { background:#2E2E2E; }
  body.dark .tab.active { background:#191919; }
  body.dark .bookmarkbar { background:#3B3B3B; border-bottom-color:#323232; color:#8A8A8A; }
  body.dark th { color:#8A8A8A; border-bottom-color:#3C3C3C; }
  body.dark tr.row:hover { background:#2E2E2E; }
  body.dark tr.row.checked { background:#37373D; }
  body.dark .col, body.dark .card { border-color:#3C3C3C; }
  body.dark .path { color:#E8E8E8; }
  .themeBtn { margin-left:10px; padding:2px 10px; border:1px solid #B0B0B0; border-radius:4px; background:transparent; color:inherit; cursor:pointer; font:12px 'Segoe UI','Microsoft YaHei'; }
  .themeBtn:hover { border-color:#4A8FD0; color:#4A8FD0; }
  .toolbar { display:flex; gap:2px; padding:3px 8px; border-bottom:1px solid #E9E9E9; flex-wrap:wrap; }
  body.dark .toolbar { border-bottom-color:#323232; }
  .tbtn { padding:3px 9px; border-radius:4px; cursor:pointer; }
  .tbtn:hover { background:#F5F7FA; }
  body.dark .tbtn:hover { background:#2E2E2E; }
  .crumb { padding:5px 10px; border-bottom:1px solid #E9E9E9; display:flex; gap:2px; align-items:center; flex-wrap:wrap; }
  body.dark .crumb { border-bottom-color:#323232; }
  .seg { padding:2px 8px; border-radius:4px; cursor:default; }
  .seg:hover { background:#F5F7FA; }
  body.dark .seg:hover { background:#2E2E2E; }
  .layout { display:flex; min-height:420px; }
  .sidebar { width:200px; border-right:1px solid #E9E9E9; padding:6px 4px; }
  body.dark .sidebar { border-right-color:#323232; }
  .navhead { color:#4A8FD0; font-weight:bold; margin:10px 6px 4px; }
  .navitem { padding:4px 8px; border-radius:4px; cursor:default; }
  .navitem:hover { background:#F5F7FA; }
  body.dark .navitem:hover { background:#2E2E2E; }
  .navitem.selected { background:#DCE9F5; }
  body.dark .navitem.selected { background:#37373D; }
  .content { flex:1; padding:10px 14px; }
  .statusbar { border-top:1px solid #E9E9E9; padding:4px 12px; color:#808080; }
  body.dark .statusbar { border-top-color:#323232; color:#8A8A8A; }
  details.diagBox { max-width:980px; margin-top:14px; }
  details.diagBox summary { cursor:pointer; color:#808080; }
  pre.diag { background:#11151B; color:#9CDCFE; font:11px Consolas,monospace; padding:8px; border-radius:6px; max-height:240px; overflow:auto; white-space:pre-wrap; margin-top:6px; }
</style>
</head>
<body>
  <div class=""titlebar"">
    <div class=""logo"">T</div>
    <div id=""tabs"" style=""display:flex;gap:2px;""></div>
    <div class=""url"" id=""url""></div>
    <button class=""themeBtn"" id=""themeBtn"" onclick=""toggleTheme()"">浅色</button>
  </div>
  <div class=""bookmarkbar"">浏览器调试镜像：页面与程序界面实时同构，点击标签可在程序中切换，右侧按钮可切换程序主题</div>
  <div class=""toolbar"">
    <span class=""tbtn"">查看</span><span class=""tbtn"">新建文件夹</span><span class=""tbtn"">批量改名</span><span class=""tbtn"">回收站</span><span class=""tbtn"">显示隐藏条目</span><span class=""tbtn"">信息窗格</span><span class=""tbtn"">刷新</span>
  </div>
  <div class=""crumb"" id=""crumb""></div>
  <div class=""layout"">
    <div class=""sidebar"">
      <div class=""navhead"">快捷访问</div><div id=""navPinned""></div>
      <div class=""navhead"">此电脑</div><div id=""navPc""></div>
      <div class=""navhead"">网络</div>
      <div class=""navitem"">连接管理</div>
    </div>
    <div class=""content"">
      <div class=""time"" id=""time""></div>
      <div id=""main""></div>
      <details class=""diagBox"">
        <summary>诊断信息（UI 线程未处理异常，最多保留 100 条）</summary>
        <pre class=""diag"" id=""diag"">（暂无）</pre>
      </details>
    </div>
  </div>
  <div class=""statusbar"" id=""statusbar"">0 个项目</div>
<script>
let state = null;
async function refresh() {
  try {
    const r = await fetch('/api/state');
    state = await r.json();
    render();
  } catch (e) { /* 服务不可用时保持上次画面 */ }
  try {
    const dr = await fetch('/api/diagnostics');
    const items = await dr.json();
    document.getElementById('diag').textContent = items.length ? items.join('\n\n────────\n\n') : '（暂无）';
  } catch (e) { /* 诊断拉取失败忽略 */ }
}
async function selectTab(i) {
  try { await fetch('/api/select?i=' + i, { method: 'POST' }); } catch (e) {}
  setTimeout(refresh, 200);
}
async function toggleTheme() {
  const t = (state && state.theme === 'Light') ? 'Dark' : 'Light';
  try { await fetch('/api/theme?t=' + t, { method: 'POST' }); } catch (e) {}
  setTimeout(refresh, 200);
}
function esc(s) { const d = document.createElement('div'); d.textContent = s == null ? '' : s; return d.innerHTML; }
function renderNav(id, items) {
  let html = '';
  for (const n of items) {
    html += '<div class=""navitem' + (n.selected ? ' selected' : '') + '"" title=""' + esc(n.path) + '"">' + esc(n.name) + '</div>';
  }
  if (!items.length) { html = '<div class=""hint"" style=""margin:0 6px;"">空</div>'; }
  document.getElementById(id).innerHTML = html;
}
function renderCrumb(path) {
  let html = '<span class=""seg"">此电脑</span>';
  if (path) {
    let acc = '';
    for (const part of path.replace(/\\+$/, '').split('\\')) {
      if (!part) { continue; }
      if (part.endsWith(':')) { acc = part + '\\'; html += '<span>›</span><span class=""seg"">' + esc(part) + '</span>'; continue; }
      acc = acc.replace(/\\+$/, '') + '\\' + part;
      html += '<span>›</span><span class=""seg"">' + esc(part) + '</span>';
    }
  }
  document.getElementById('crumb').innerHTML = html;
}
function render() {
  if (!state) return;
  document.getElementById('time').textContent = '快照时间：' + state.generatedTime + '（每 1.5 秒自动刷新）';
  document.getElementById('url').textContent = '127.0.0.1:15919';
  document.body.className = (state.theme === 'Light') ? '' : 'dark';
  document.getElementById('themeBtn').textContent = (state.theme === 'Light') ? '深色' : '浅色';
  renderNav('navPinned', state.navPinned);
  renderNav('navPc', state.navPc);
  let tabs = '';
  for (const t of state.tabs) {
    tabs += '<div class=""tab' + (t.selected ? ' active' : '') + '"" onclick=""selectTab(' + t.index + ')"">' + esc(t.title) + '</div>';
  }
  document.getElementById('tabs').innerHTML = tabs;
  const main = document.getElementById('main');
  const current = state.tabs.find(t => t.selected);
  if (!current) { main.innerHTML = '<div class=""hint"">没有打开的标签页</div>'; return; }
  if (current.tabType === 'file') {
    renderCrumb(current.currentPath);
    document.getElementById('statusbar').textContent = current.rows.length + ' 个项目';
    let html = '<div class=""path"">' + esc(current.currentPath === '' ? '此电脑' : current.currentPath) + '</div>';
    html += '<table><tr><th>名称</th><th>类型</th><th>大小</th><th>修改日期</th></tr>';
    for (const row of current.rows) {
      html += '<tr class=""row""><td>' + esc(row.name) + '</td><td>' + esc(row.type) + '</td><td>' + esc(row.size) + '</td><td>' + esc(row.modified) + '</td></tr>';
    }
    html += '</table>';
    main.innerHTML = html;
  } else if (current.tabType === 'board') {
    renderCrumb('');
    document.getElementById('statusbar').textContent = '任务看板';
    main.innerHTML = '<div class=""board"">' + col('待办', current.todo) + col('进行中', current.inProgress) + col('已完成', current.done) + '</div>';
  } else if (current.tabType === 'connections') {
    renderCrumb('');
    document.getElementById('statusbar').textContent = '连接管理';
    let html = '<div class=""path"">远程连接（SSH / FTP）</div>';
    if (current.connections.length === 0) { html += '<div class=""hint"">暂无连接，在程序中点「新建连接」添加</div>'; }
    html += '<table><tr><th>名称</th><th>摘要</th></tr>';
    for (const c of current.connections) {
      const sel = c[0] === current.selectedConnection ? ' checked' : '';
      html += '<tr class=""row' + sel + '""><td>' + esc(c[0]) + '</td><td>' + esc(c[1]) + '</td></tr>';
    }
    html += '</table>';
    main.innerHTML = html;
  }
}
function col(title, items) {
  let html = '<div class=""col""><h3>' + title + '</h3>';
  for (const it of items) { html += '<div class=""card"">' + esc(it) + '</div>'; }
  if (items.length === 0) { html += '<div class=""hint"">空</div>'; }
  return html + '</div>';
}
refresh();
setInterval(refresh, 1500);
</script>
</body>
</html>";
}
