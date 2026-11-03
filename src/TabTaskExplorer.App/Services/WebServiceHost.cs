using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 网页端服务的宿主：可同时开启任意数量的端口，每个端口独立引用一个权限方案。
/// 每个请求依次校验来源地址、账号密码与路径白名单，再按方案放行读取、修改、增删操作。
/// </summary>
public class WebServiceHost
{
    /// <summary>默认服务端口。</summary>
    public const int DefaultPort = 15919;

    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly List<WebEndpoint> endpoints = new List<WebEndpoint>();
    private readonly IArchiveService archiveService;
    private readonly WebSessionStore sessionStore = new WebSessionStore();
    private readonly WebRateLimiter rateLimiter = new WebRateLimiter();
    private readonly WebLoginFailureLimiter loginFailureLimiter = new WebLoginFailureLimiter();
    private WebEventHub? eventHub;
    private IWebWorkspaceBridge? bridge;
    private AppSettings? settings;

    /// <summary>构造网页端服务宿主；压缩服务用于读取压缩包内的文件内容。</summary>
    public WebServiceHost(IArchiveService archiveService)
    {
        this.archiveService = archiveService;
    }

    /// <summary>实际监听成功的地址清单（启动成功后可读）。</summary>
    public List<string> ListenUrls { get; private set; } = new List<string>();

    /// <summary>启动失败的端口与原因（供调用方写入日志）。</summary>
    public List<string> Failures { get; private set; } = new List<string>();

    private static readonly object diagnosticLock = new object();
    private static readonly List<string> diagnostics = new List<string>();
    private const int MaxDiagnostics = 100;

    /// <summary>记录一条诊断信息（UI 线程未处理异常等），供浏览器页面的诊断面板查看。</summary>
    public static void RecordDiagnostic(string source, Exception error)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + source + "] " + error.GetType().Name + ": " + error.Message
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
            for (int index = 0; index < diagnostics.Count; index++)
            {
                copy[index] = diagnostics[diagnostics.Count - 1 - index];
            }
            return copy;
        }
    }

    /// <summary>
    /// 按端口清单启动服务：先停止全部旧监听，再逐个端口尝试监听。
    /// 单个端口失败只记录原因，不影响其他端口。
    /// </summary>
    public void Start(IReadOnlyList<WebPortRuntime> ports, IWebWorkspaceBridge workspaceBridge, AppSettings appSettings)
    {
        this.Stop();
        this.bridge = workspaceBridge;
        this.settings = appSettings;
        this.ListenUrls = new List<string>();
        this.Failures = new List<string>();
        this.eventHub = new WebEventHub(workspaceBridge);
        this.eventHub.Start();

        foreach (WebPortRuntime portRuntime in ports)
        {
            WebEndpoint endpoint = new WebEndpoint(this, portRuntime);
            string failure = endpoint.TryStart();
            if (string.IsNullOrEmpty(failure))
            {
                this.endpoints.Add(endpoint);
                this.ListenUrls.Add(endpoint.ListenUrl);
            }
            else
            {
                this.Failures.Add(failure);
            }
        }
    }

    /// <summary>停止全部监听并清理端口；同时清空会话，避免旧令牌在服务重启后继续可用。</summary>
    public void Stop()
    {
        foreach (WebEndpoint endpoint in this.endpoints)
        {
            endpoint.Stop();
        }
        this.endpoints.Clear();
        this.ListenUrls = new List<string>();
        this.eventHub?.Stop();
        this.eventHub = null;
        this.sessionStore.Reset();
    }

    /// <summary>单个端口监听器：独立线程接收连接，独立应用所属权限方案。</summary>
    private class WebEndpoint
    {
        /// <summary>当前线程正在处理的请求：供写出响应时回填状态与结果，用于审计日志与响应头。</summary>
        [ThreadStatic]
        private static HttpRequest? currentRequest;

        private readonly WebServiceHost host;
        private readonly WebPortRuntime runtime;
        private TcpListener? listener;
        private volatile bool running;

        public WebEndpoint(WebServiceHost host, WebPortRuntime runtime)
        {
            this.host = host;
            this.runtime = runtime;
        }

        /// <summary>实际监听地址（例如 http://127.0.0.1:15919/）。</summary>
        public string ListenUrl { get; private set; } = string.Empty;

        /// <summary>尝试启动监听；成功返回空文本，失败返回原因。</summary>
        public string TryStart()
        {
            // 安全基线：默认仅监听回环地址；只有权限方案启用账号密码后才允许监听局域网地址
            if (runtime.BindScope == WebBindScope.Any
                && (!runtime.Profile.RequirePassword || string.IsNullOrEmpty(runtime.Profile.PasswordHash)))
            {
                return "端口=" + runtime.Port + " 监听范围=Any 原因=非回环监听必须启用账号密码，已拒绝启动";
            }
            IPAddress bindAddress = runtime.BindScope == WebBindScope.Any ? IPAddress.Any : IPAddress.Loopback;
            if (runtime.BindScope == WebBindScope.Any)
            {
                UiLogger.Log("网页端服务", "风险提示：端口=" + runtime.Port + " 监听范围=全部网络地址"
                    + " 已强制要求账号密码 方案=" + runtime.Profile.Name);
            }
            try
            {
                this.listener = new TcpListener(bindAddress, runtime.Port);
                this.listener.Start();
            }
            catch (Exception error)
            {
                this.listener = null;
                return "端口=" + runtime.Port + " 监听地址=" + bindAddress + " 原因=" + error.Message;
            }
            this.running = true;
            string display = runtime.BindScope == WebBindScope.Any ? "0.0.0.0" : "127.0.0.1";
            this.ListenUrl = "http://" + display + ":" + runtime.Port + "/";
            Thread acceptThread = new Thread(new ThreadStart(this.AcceptLoop));
            acceptThread.IsBackground = true;
            acceptThread.Start();
            return string.Empty;
        }

        /// <summary>停止监听。</summary>
        public void Stop()
        {
            this.running = false;
            try
            {
                this.listener?.Stop();
            }
            catch (Exception)
            {
                // 忽略停止时的套接字错误
            }
            this.listener = null;
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
                catch (Exception)
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

        /// <summary>
        /// 处理单个请求：来源地址 → 全局限流 → 认证（Basic / 会话令牌）→ 写操作同源与 CSRF 校验 → 路由执行。
        /// 所有写请求在结束时写入审计日志；认证失败与安全拒绝也进入审计。
        /// </summary>
        private void HandleClient(TcpClient client)
        {
            HttpRequest? request = null;
            try
            {
                client.ReceiveTimeout = 8000;
                // 写超时用于发现已断开的推送连接，避免线程长期阻塞在向失效套接字写入
                client.SendTimeout = 15000;
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    request = HttpRequest.Read(stream);
                    if (request == null)
                    {
                        return;
                    }
                    WebEndpoint.currentRequest = request;
                    request.RequestId = WebSecurity.GenerateRequestId();
                    IPAddress? remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                    request.RemoteAddress = WebAccessControl.NormalizeAddress(remote ?? IPAddress.Loopback);

                    WebAccessProfile profile = this.runtime.Profile;
                    string remoteText = request.RemoteAddress == null ? "未知" : request.RemoteAddress.ToString();
                    if (!WebAccessControl.IsAddressAllowed(profile, request.RemoteAddress))
                    {
                        WebAuditLog.RecordDenied(this.runtime.Port, request, string.Empty, "来源地址不在白名单");
                        UiLogger.Log("网页端访问", "拒绝：来源地址不在白名单 端口=" + this.runtime.Port
                            + " 来源=" + remoteText + " 请求=" + request.Method + " " + request.Path);
                        this.WriteText(stream, "来源地址未获授权", 403);
                        return;
                    }

                    int retryAfterSeconds;
                    if (!this.host.rateLimiter.TryAcquire("全部请求:" + remoteText,
                        WebSecurity.GeneralRequestsPerMinute, 60, out retryAfterSeconds))
                    {
                        UiLogger.Log("网页端安全", "拒绝：请求频率超限 端口=" + this.runtime.Port
                            + " 来源=" + remoteText + " 请求=" + request.Method + " " + request.Path
                            + " 建议等待秒数=" + retryAfterSeconds);
                        this.WriteText(stream, "请求过于频繁，请稍后再试", 429, retryAfterSeconds);
                        return;
                    }

                    WebSession? session = this.Authenticate(stream, profile, request);
                    if (session == null)
                    {
                        return;
                    }
                    request.Session = session;
                    request.Account = session.Account;

                    if (WebSecurity.IsWriteMethod(request.Method))
                    {
                        if (!this.CheckSameOrigin(request))
                        {
                            WebAuditLog.RecordDenied(this.runtime.Port, request, session.Account, "同源校验未通过");
                            UiLogger.Log("网页端安全", "拒绝：同源校验未通过 端口=" + this.runtime.Port
                                + " 来源=" + remoteText + " 账号=" + WebSecurity.DescribeAccount(session.Account)
                                + " 请求=" + request.Method + " " + request.Path);
                            this.WriteText(stream, "请求来源与本地服务不一致，已拒绝", 403);
                            return;
                        }
                        if (!this.CheckCsrf(session, request))
                        {
                            WebAuditLog.RecordDenied(this.runtime.Port, request, session.Account, "缺少或错误的 CSRF 令牌");
                            UiLogger.Log("网页端安全", "拒绝：CSRF 令牌校验未通过 端口=" + this.runtime.Port
                                + " 来源=" + remoteText + " 账号=" + WebSecurity.DescribeAccount(session.Account)
                                + " 请求=" + request.Method + " " + request.Path);
                            this.WriteText(stream, "缺少或错误的 CSRF 令牌", 403);
                            return;
                        }
                        if (!this.host.rateLimiter.TryAcquire("写操作:" + remoteText + ":" + session.Account,
                            WebSecurity.WriteRequestsPerMinute, 60, out retryAfterSeconds))
                        {
                            UiLogger.Log("网页端安全", "拒绝：写操作频率超限 端口=" + this.runtime.Port
                                + " 来源=" + remoteText + " 账号=" + WebSecurity.DescribeAccount(session.Account)
                                + " 请求=" + request.Method + " " + request.Path
                                + " 建议等待秒数=" + retryAfterSeconds);
                            this.WriteText(stream, "写操作过于频繁，请稍后再试", 429, retryAfterSeconds);
                            return;
                        }
                    }

                    this.Dispatch(stream, request, profile);
                    this.host.eventHub?.RequestRefresh();
                }
            }
            catch (Exception error)
            {
                // 单个连接的任何错误都不影响服务继续运行；异常细节只写入本机日志，不回显给客户端
                UiLogger.LogError("网页端访问", "处理请求失败 端口=" + this.runtime.Port
                    + " 请求编号=" + (request == null ? "无" : request.RequestId), error);
            }
            finally
            {
                if (request != null && WebSecurity.IsWriteMethod(request.Method))
                {
                    WebAuditLog.Record(this.runtime.Port, request);
                }
                WebEndpoint.currentRequest = null;
            }
        }

        /// <summary>
        /// 认证：优先使用 Bearer 会话令牌；事件推送允许查询串携带令牌（EventSource 不能设置请求头）；
        /// 未携带令牌时回落 HTTP Basic，连续失败达到阈值后返回 429。
        /// </summary>
        private WebSession? Authenticate(NetworkStream stream, WebAccessProfile profile, HttpRequest request)
        {
            bool isEventRequest = string.Equals(request.Path, "/api/events", StringComparison.Ordinal);
            string authorization = request.Headers.GetValueOrDefault("authorization") ?? string.Empty;
            WebSession? session = null;

            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                session = this.host.sessionStore.Find(authorization.Substring("Bearer ".Length).Trim(), profile);
                if (session != null)
                {
                    return session;
                }
                if (!profile.RequirePassword)
                {
                    return this.host.sessionStore.GetOrCreateAnonymous(profile);
                }
                WebAuditLog.RecordDenied(this.runtime.Port, request, string.Empty, "会话令牌无效或已过期");
                if (isEventRequest)
                {
                    this.WriteText(stream, "会话已过期", 401);
                }
                else
                {
                    this.WriteAuthChallenge(stream);
                }
                return null;
            }

            if (isEventRequest && request.Method == "GET")
            {
                string queryToken = request.GetQuery("token");
                if (queryToken.Length > 0)
                {
                    session = this.host.sessionStore.Find(queryToken, profile);
                    if (session != null)
                    {
                        return session;
                    }
                    if (!profile.RequirePassword)
                    {
                        return this.host.sessionStore.GetOrCreateAnonymous(profile);
                    }
                    WebAuditLog.RecordDenied(this.runtime.Port, request, string.Empty, "事件推送令牌无效或已过期");
                    this.WriteText(stream, "会话已过期", 401);
                    return null;
                }
            }

            if (!profile.RequirePassword)
            {
                return this.host.sessionStore.GetOrCreateAnonymous(profile);
            }

            string account;
            string password;
            if (!WebAccessControl.TryParseBasic(authorization, out account, out password))
            {
                UiLogger.Log("网页端访问", "认证提示：端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 请求=" + request.Method + " " + request.Path + " 结果=需要 Basic 认证");
                if (isEventRequest)
                {
                    this.WriteText(stream, "需要认证", 401);
                }
                else
                {
                    this.WriteAuthChallenge(stream);
                }
                return null;
            }

            string failureKey = profile.Name + "\n" + request.RemoteAddress;
            bool passed = profile.VerifyPassword(account, password);
            if (!passed)
            {
                this.host.loginFailureLimiter.RecordFailure(failureKey);
                int retryAfterSeconds;
                bool locked = this.host.loginFailureLimiter.IsLocked(failureKey, out retryAfterSeconds);
                WebAuditLog.RecordDenied(this.runtime.Port, request, account, "账号或密码错误");
                UiLogger.Log("网页端安全", "认证失败：端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 账号=" + account + " 方案=" + profile.Name);
                if (locked)
                {
                    this.WriteText(stream, "登录失败次数过多，请稍后再试", 429, retryAfterSeconds);
                    return null;
                }
                if (isEventRequest)
                {
                    this.WriteText(stream, "需要认证", 401);
                }
                else
                {
                    this.WriteAuthChallenge(stream);
                }
                return null;
            }

            this.host.loginFailureLimiter.RecordSuccess(failureKey);
            session = this.host.sessionStore.GetOrCreateBasic(profile, account);
            UiLogger.Log("网页端访问", "认证通过：端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                + " 账号=" + account + " 方案=" + profile.Name);
            return session;
        }

        /// <summary>
        /// 同源校验：浏览器发出的写请求若带 Origin 或 Referer，必须与本端口的回环地址一致。
        /// 没有这两个请求头时仍由 CSRF 令牌保证请求来自本页面（例如脚本或命令行客户端）。
        /// </summary>
        private bool CheckSameOrigin(HttpRequest request)
        {
            string origin = request.Headers.GetValueOrDefault("origin") ?? string.Empty;
            string referer = request.Headers.GetValueOrDefault("referer") ?? string.Empty;
            if (origin.Length == 0 && referer.Length == 0)
            {
                return true;
            }
            List<string> allowedAddresses = new List<string>();
            string hostHeader = request.Headers.GetValueOrDefault("host") ?? string.Empty;
            if (hostHeader.Length > 0)
            {
                allowedAddresses.Add("http://" + hostHeader);
            }
            allowedAddresses.Add("http://127.0.0.1:" + this.runtime.Port);
            allowedAddresses.Add("http://localhost:" + this.runtime.Port);
            allowedAddresses.Add("http://[::1]:" + this.runtime.Port);

            string candidate = origin.Length > 0 ? origin : referer;
            Uri? candidateUri;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out candidateUri))
            {
                return false;
            }
            foreach (string allowed in allowedAddresses)
            {
                Uri? allowedUri;
                if (!Uri.TryCreate(allowed, UriKind.Absolute, out allowedUri))
                {
                    continue;
                }
                if (string.Equals(candidateUri.Scheme, allowedUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidateUri.Host, allowedUri.Host, StringComparison.OrdinalIgnoreCase)
                    && candidateUri.Port == allowedUri.Port)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>CSRF 令牌校验：写请求必须携带与当前会话匹配的请求头。</summary>
        private bool CheckCsrf(WebSession session, HttpRequest request)
        {
            string token = request.Headers.GetValueOrDefault("x-csrf-token") ?? string.Empty;
            return WebSecurity.FixedTimeEquals(token, session.CsrfToken);
        }

        /// <summary>按路径分发请求。</summary>
        private void Dispatch(NetworkStream stream, HttpRequest request, WebAccessProfile profile)
        {
            IWebWorkspaceBridge? bridge = this.host.bridge;
            if (bridge == null)
            {
                this.WriteText(stream, "服务未就绪", 503);
                return;
            }

            string path = request.Path;
            if (path == "/" || path == "/index.html")
            {
                string html;
                if (WebStaticAssets.TryBuildPage(this.runtime.Port, out html))
                {
                    this.WriteBytes(stream, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
                    return;
                }
                // 静态资源缺失时回退到内联页面，保证升级包不完整时仍能打开网页端
                html = WebPageHtml.Content.Replace("__PORT__", this.runtime.Port.ToString());
                this.WriteBytes(stream, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
                return;
            }
            if (path.StartsWith(WebStaticAssets.AssetPrefix, StringComparison.Ordinal) && request.Method == "GET")
            {
                string assetName = path.Substring(WebStaticAssets.AssetPrefix.Length);
                string contentType;
                byte[] assetContent;
                if (!WebStaticAssets.TryReadAsset(assetName, this.runtime.Port, out contentType, out assetContent))
                {
                    this.WriteText(stream, "not found", 404);
                    return;
                }
                bool versionMatched = string.Equals(request.GetQuery("v"), WebStaticAssets.Version, StringComparison.Ordinal);
                string cacheControl = versionMatched ? "public, max-age=31536000, immutable" : "no-store";
                this.WriteBytes(stream, contentType, assetContent, 200, cacheControl);
                return;
            }
            if (path == "/favicon.ico")
            {
                this.WriteBytes(stream, "image/x-icon", new byte[0]);
                return;
            }
            if (path == "/api/session" && request.Method == "GET")
            {
                this.HandleSession(stream, request, profile);
                return;
            }
            if (path == "/api/version" && request.Method == "GET")
            {
                string versionJson = JsonSerializer.Serialize(new
                {
                    version = "p0-1-web-modules-" + WebStaticAssets.Version,
                    features = new { session = true, csrf = true, events = true, staticAssets = WebStaticAssets.IsAvailable },
                }, jsonOptions);
                this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(versionJson));
                return;
            }
            if (path == "/api/permissions")
            {
                string json = JsonSerializer.Serialize(this.BuildPermissions(profile), jsonOptions);
                this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                return;
            }
            if (path == "/api/diagnostics")
            {
                string json = JsonSerializer.Serialize(GetDiagnostics(), jsonOptions);
                this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                return;
            }
            if (path == "/api/events" && request.Method == "GET")
            {
                this.HandleEvents(stream, request, profile, bridge);
                return;
            }
            if (path == "/api/state" && request.Method == "GET")
            {
                this.HandleState(stream, request, profile);
                return;
            }
            if (path == "/api/mode" && request.Method == "POST")
            {
                this.HandleMode(stream, request, bridge);
                return;
            }
            if (!profile.RequirePassword)
            {
                UiLogger.Log("网页端访问", "匿名操作：端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 请求=" + request.Method + " " + path);
            }

            string workspaceId = request.GetQuery("w");
            if (path == "/api/theme" && request.Method == "POST")
            {
                bridge.ApplyTheme(request.GetQuery("t"));
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/file" && request.Method == "GET")
            {
                this.HandleReadFile(stream, request, profile, workspaceId, bridge);
                return;
            }
            if (path == "/api/ide/text" && request.Method == "GET")
            {
                this.HandleIdeText(stream, request, workspaceId, bridge);
                return;
            }
            if (path == "/api/action" && request.Method == "POST")
            {
                this.HandleAction(stream, request, profile, workspaceId, bridge);
                return;
            }
            if (path == "/api/ctxmenu" && request.Method == "POST")
            {
                this.HandleContextMenu(stream, request, profile, workspaceId, bridge, false);
                return;
            }
            if (path == "/api/ctxmenu/run" && request.Method == "POST")
            {
                this.HandleContextMenu(stream, request, profile, workspaceId, bridge, true);
                return;
            }
            if (string.IsNullOrEmpty(workspaceId))
            {
                this.HandleDesktopAction(stream, request, path, bridge, profile);
                return;
            }
            WebWorkspace? workspace = bridge.FindWorkspace(workspaceId);
            if (workspace == null)
            {
                UiLogger.Log("网页端访问", "拒绝：工作区不存在或已过期 工作区=" + workspaceId + " 请求=" + path);
                this.WriteText(stream, "工作区不存在或已过期，请刷新页面", 410);
                return;
            }
            this.HandleWorkspaceAction(stream, request, path, profile, workspace);
        }

        /// <summary>通用界面动作：跟随桌面走桌面执行器，独立工作区走工作区执行器；参数取查询串，超长时取请求体。</summary>
        private void HandleAction(NetworkStream stream, HttpRequest request, WebAccessProfile profile, string workspaceId, IWebWorkspaceBridge bridge)
        {
            string action = request.GetQuery("a");
            string argument = request.GetQuery("arg");
            if (argument.Length == 0 && request.Body.Length > 0)
            {
                argument = Encoding.UTF8.GetString(request.Body);
            }
            if (string.IsNullOrEmpty(action))
            {
                this.WriteText(stream, "缺少动作名称", 400);
                return;
            }
            request.AuditTarget = argument.Length > 200 ? argument.Substring(0, 200) + "…" : argument;
            if (!this.CheckActionPermission(stream, profile, request, action, argument))
            {
                return;
            }

            string message;
            if (string.IsNullOrEmpty(workspaceId))
            {
                UiLogger.Log("网页端访问", "执行动作：模式=跟随桌面 端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 动作=" + action + " 参数长度=" + argument.Length);
                message = bridge.ExecuteDesktopAction(action, argument);
            }
            else
            {
                WebWorkspace? workspace = bridge.FindWorkspace(workspaceId);
                if (workspace == null)
                {
                    UiLogger.Log("网页端访问", "拒绝：工作区不存在或已过期 工作区=" + workspaceId + " 请求=" + request.Path);
                    this.WriteText(stream, "工作区不存在或已过期，请刷新页面", 410);
                    return;
                }
                UiLogger.Log("网页端访问", "执行动作：模式=独立工作区 端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 工作区=" + workspace.Id + " 动作=" + action + " 参数长度=" + argument.Length);
                message = workspace.ExecuteAction(action, argument);
            }
            if (!string.IsNullOrEmpty(message))
            {
                UiLogger.Log("网页端访问", "动作结果：动作=" + action + " 结果=" + message);
            }
            this.WriteText(stream, message);
        }

        /// <summary>
        /// 网页端右键菜单：build 返回系统外壳菜单层级，run 按命令编号唤起。
        /// 目标路径先由工作区/桌面校验（只允许当前已打开标签内的路径），越权直接拒绝并记日志。
        /// </summary>
        private void HandleContextMenu(NetworkStream stream, HttpRequest request, WebAccessProfile profile,
            string workspaceId, IWebWorkspaceBridge bridge, bool execute)
        {
            ContextMenuRequestPayload? payload;
            try
            {
                string body = Encoding.UTF8.GetString(request.Body);
                payload = body.Length == 0
                    ? null
                    : JsonSerializer.Deserialize<ContextMenuRequestPayload>(body, jsonOptions);
            }
            catch (Exception error)
            {
                UiLogger.LogError("网页端右键菜单", "解析请求体失败 端口=" + this.runtime.Port
                    + " 来源=" + request.RemoteAddress + " 请求=" + request.Path, error);
                this.WriteText(stream, "右键请求格式不正确", 400);
                return;
            }
            if (payload == null || payload.Paths == null || payload.Paths.Count == 0)
            {
                this.WriteText(stream, "右键请求缺少目标路径", 400);
                return;
            }
            if (execute && !profile.AllowCreateDelete)
            {
                UiLogger.Log("网页端右键菜单", "拒绝执行：未放行新建与删除权限 端口=" + this.runtime.Port
                    + " 来源=" + request.RemoteAddress + " 命令编号=" + payload.CommandId + " 方案=" + profile.Name);
                this.WriteText(stream, "该端口未放行新建与删除权限，不能执行系统菜单命令", 403);
                return;
            }
            List<string> paths = new List<string>();
            foreach (string path in payload.Paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path.Trim());
                }
            }
            if (paths.Count == 0)
            {
                this.WriteText(stream, "右键请求缺少有效目标路径", 400);
                return;
            }
            if (execute && payload.CommandId < 0)
            {
                this.WriteText(stream, "右键命令缺少有效命令编号", 400);
                return;
            }
            request.AuditTarget = string.Join("、", paths);
            try
            {
                WebContextMenuResult? result = null;
                bool success = false;
                if (string.IsNullOrEmpty(workspaceId))
                {
                    if (execute)
                    {
                        success = bridge.ExecuteDesktopContextMenuCommand(payload.CommandId, paths, payload.Background);
                    }
                    else
                    {
                        result = bridge.BuildDesktopContextMenu(paths, payload.Background);
                    }
                }
                else
                {
                    WebWorkspace? workspace = bridge.FindWorkspace(workspaceId);
                    if (workspace == null)
                    {
                        UiLogger.Log("网页端右键菜单", "拒绝：工作区不存在或已过期 工作区=" + workspaceId
                            + " 请求=" + request.Path);
                        this.WriteText(stream, "工作区不存在或已过期，请刷新页面", 410);
                        return;
                    }
                    if (execute)
                    {
                        success = workspace.ExecuteContextMenuCommand(payload.CommandId, paths, payload.Background);
                    }
                    else
                    {
                        result = workspace.BuildContextMenu(paths, payload.Background);
                    }
                }
                if (execute)
                {
                    this.WriteText(stream, success ? "ok" : "执行失败：系统外壳未接受该命令", success ? 200 : 500);
                    return;
                }
                string json = JsonSerializer.Serialize(result, jsonOptions);
                this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
            }
            catch (UnauthorizedAccessException error)
            {
                UiLogger.Log("网页端右键菜单", "拒绝：越权右键请求 端口=" + this.runtime.Port
                    + " 来源=" + request.RemoteAddress + " 工作区=" + (string.IsNullOrEmpty(workspaceId) ? "跟随桌面" : workspaceId)
                    + " 目标=" + string.Join("、", paths) + " 原因=" + error.Message
                    + " 结果=未枚举也未执行任何外壳命令");
                this.WriteText(stream, "目标路径不在当前标签允许范围内，已拒绝", 403);
            }
            catch (Exception error)
            {
                UiLogger.LogError("网页端右键菜单", "处理失败 端口=" + this.runtime.Port
                    + " 来源=" + request.RemoteAddress + " 工作区=" + (string.IsNullOrEmpty(workspaceId) ? "跟随桌面" : workspaceId)
                    + " 目标=" + string.Join("、", paths) + " 请求编号=" + request.RequestId, error);
                this.WriteText(stream, "右键菜单处理失败，请查看本机日志（请求编号=" + request.RequestId + "）", 500);
            }
        }

        /// <summary>右键请求体：目标路径、是否空白处、系统外壳命令编号。</summary>
        private sealed class ContextMenuRequestPayload
        {
            /// <summary>目标绝对路径列表。</summary>
            public List<string>? Paths { get; set; }

            /// <summary>是否为列表空白处右键。</summary>
            public bool Background { get; set; }

            /// <summary>要唤起的外壳菜单命令编号（枚举请求忽略）。</summary>
            public int CommandId { get; set; } = -1;
        }

        /// <summary>按动作判定所需权限：增删类要放行新建与删除，改配置与写文件类要放行修改。</summary>
        private bool CheckActionPermission(NetworkStream stream, WebAccessProfile profile, HttpRequest request, string action,
            string argument)
        {
            // 比对路径来自用户在网页端手动输入，必须与文件访问走同一份路径白名单，避免绕过权限方案读取任意本机文件
            if (action == "compareapply" && profile.AllowedPaths != null && profile.AllowedPaths.Count > 0)
            {
                foreach (string line in argument.Split('\n'))
                {
                    string comparePath = line.TrimEnd('\r').Trim();
                    if (comparePath.Length > 0 && !WebAccessControl.IsPathAllowed(profile, comparePath))
                    {
                        UiLogger.Log("网页端访问", "拒绝：" + action + " 目标路径不在放行范围 端口=" + this.runtime.Port
                            + " 来源=" + request.RemoteAddress + " 目标=" + comparePath + " 方案=" + profile.Name);
                        this.WriteText(stream, "比对路径不在当前权限方案放行范围内：" + comparePath, 403);
                        return false;
                    }
                }
            }
            bool needCreateDelete = action == "cut" || action == "paste" || action == "pasteto" || action == "deletechecked"
                || action == "batchrename" || action == "encodepreview" || action == "encodeapply"
                || action == "connsave" || action == "conndelete" || action == "connimport"
                // 信任首次主机密钥与删除旧记录属于连接记录的新增/删除，默认方案放行增删但关闭修改时仍可完成首次确认
                || action == "connhostkeytrust" || action == "connhostkeyforget"
                || action == "startupcustomsave" || action == "startupcustomdelete"
                || action == "disknewfolder" || action == "disknewfile" || action == "diskrename"
                || action == "diskdelete" || action == "diskpasteinto" || action == "diskcopyto"
                || action == "diskmoveto" || action == "diskcutsource";
            bool needModify = action == "togglehidden" || action == "toggleextensions" || action == "pin" || action == "unpin" || action == "chmod"
                || action == "idetext" || action == "idesave" || action == "idesaveall" || action == "idereplace" || action == "idereplaceall"
                || action == "processkill" || action == "processpriority" || action == "processaffinity"
                || action == "startuptoggle" || action == "startupcustomtoggle" || action == "startupcustomrun";
            if (needCreateDelete && !profile.AllowCreateDelete)
            {
                UiLogger.Log("网页端访问", "拒绝：" + action + " 未放行新建与删除权限 端口=" + this.runtime.Port
                    + " 来源=" + request.RemoteAddress + " 动作=" + action + " 方案=" + profile.Name);
                this.WriteText(stream, "该端口未放行新建与删除权限", 403);
                return false;
            }
            if (needModify && !profile.AllowModify)
            {
                UiLogger.Log("网页端访问", "拒绝：" + action + " 未放行修改权限 端口=" + this.runtime.Port
                    + " 来源=" + request.RemoteAddress + " 动作=" + action + " 方案=" + profile.Name);
                this.WriteText(stream, "该端口未放行修改权限", 403);
                return false;
            }
            return true;
        }

        /// <summary>跟随桌面模式的动作：只允许切换标签、打开路径与新建标签。</summary>
        private void HandleDesktopAction(NetworkStream stream, HttpRequest request, string path, IWebWorkspaceBridge bridge, WebAccessProfile profile)
        {
            if (path == "/api/select" && request.Method == "POST")
            {
                int index;
                if (int.TryParse(request.GetQuery("i"), out index))
                {
                    bridge.SelectDesktopTab(index);
                }
                this.WriteOk(stream);
                return;
            }
            bool restrictedPaths = profile.AllowedPaths != null && profile.AllowedPaths.Count > 0;
            if (path == "/api/open" && request.Method == "POST")
            {
                if (restrictedPaths)
                {
                    UiLogger.Log("网页端访问", "拒绝：该权限方案限定了可访问目录，跟随桌面模式不能打开新路径 端口=" + this.runtime.Port
                        + " 来源=" + request.RemoteAddress + " 方案=" + profile.Name);
                    this.WriteText(stream, "该权限方案限定了可访问目录，请在独立新页面中浏览", 403);
                    return;
                }
                bridge.OpenDesktopPath(request.GetQuery("p"));
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/tabs/new" && request.Method == "POST")
            {
                if (restrictedPaths)
                {
                    UiLogger.Log("网页端访问", "拒绝：该权限方案限定了可访问目录，跟随桌面模式不能新建标签 端口=" + this.runtime.Port
                        + " 来源=" + request.RemoteAddress + " 方案=" + profile.Name);
                    this.WriteText(stream, "该权限方案限定了可访问目录，请在独立新页面中浏览", 403);
                    return;
                }
                bridge.OpenDesktopPath(request.GetQuery("p"));
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/up" && request.Method == "POST")
            {
                bridge.NavigateDesktopUp();
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/tabs/disk" && request.Method == "POST")
            {
                bridge.OpenDesktopDiskView();
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/disk/nav" && request.Method == "POST")
            {
                string target = request.GetQuery("p");
                if (!string.IsNullOrEmpty(target) && !this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                bridge.NavigateDesktopDisk(target);
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/folder" && request.Method == "POST")
            {
                if (!this.RequireCreateDelete(stream, profile, request, "新建文件夹"))
                {
                    return;
                }
                string parent = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, parent))
                {
                    return;
                }
                bridge.CreateDesktopFolder(request.GetQuery("n"));
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/rename" && request.Method == "POST")
            {
                if (!this.RequireCreateDelete(stream, profile, request, "重命名"))
                {
                    return;
                }
                string target = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                bridge.RenameDesktopEntry(target, request.GetQuery("n"));
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/delete" && request.Method == "POST")
            {
                if (!this.RequireCreateDelete(stream, profile, request, "删除"))
                {
                    return;
                }
                string target = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                bridge.DeleteDesktopEntry(target);
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/write" && request.Method == "POST")
            {
                if (!profile.AllowModify)
                {
                    UiLogger.Log("网页端访问", "拒绝：未放行修改权限 端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress + " 目标=" + request.GetQuery("p"));
                    this.WriteText(stream, "该端口未放行修改权限", 403);
                    return;
                }
                string target = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                bridge.WriteDesktopFile(target, request.Body);
                UiLogger.Log("网页端访问", "修改文件：模式=跟随桌面 端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 目标=" + target + " 字节数=" + request.Body.Length);
                this.WriteOk(stream);
                return;
            }
            this.WriteText(stream, "not found", 404);
        }

        /// <summary>独立工作区的动作：标签、导航与文件操作，全部按权限方案放行。</summary>
        private void HandleWorkspaceAction(NetworkStream stream, HttpRequest request, string path, WebAccessProfile profile, WebWorkspace workspace)
        {
            if (path == "/api/select" && request.Method == "POST")
            {
                int index;
                if (int.TryParse(request.GetQuery("i"), out index))
                {
                    workspace.SelectTab(index);
                }
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/open" && request.Method == "POST")
            {
                string target = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                workspace.OpenPath(target);
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/up" && request.Method == "POST")
            {
                workspace.NavigateUp();
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/tabs/new" && request.Method == "POST")
            {
                workspace.AddFileTab(request.GetQuery("p"));
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/tabs/disk" && request.Method == "POST")
            {
                workspace.AddDiskViewTab();
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/disk/nav" && request.Method == "POST")
            {
                string target = request.GetQuery("p");
                if (!string.IsNullOrEmpty(target) && !this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                workspace.NavigateDisk(target);
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/tabs/close" && request.Method == "POST")
            {
                int index;
                if (int.TryParse(request.GetQuery("i"), out index))
                {
                    workspace.CloseTab(index);
                }
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/folder" && request.Method == "POST")
            {
                if (!this.RequireCreateDelete(stream, profile, request, "新建文件夹"))
                {
                    return;
                }
                string parent = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, parent))
                {
                    return;
                }
                workspace.CreateFolder(request.GetQuery("n"));
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/rename" && request.Method == "POST")
            {
                if (!this.RequireCreateDelete(stream, profile, request, "重命名"))
                {
                    return;
                }
                string target = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                workspace.RenameEntry(target, request.GetQuery("n"));
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/delete" && request.Method == "POST")
            {
                if (!this.RequireCreateDelete(stream, profile, request, "删除"))
                {
                    return;
                }
                string target = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                workspace.DeleteEntry(target);
                this.WriteOk(stream);
                return;
            }
            if (path == "/api/write" && request.Method == "POST")
            {
                if (!profile.AllowModify)
                {
                    UiLogger.Log("网页端访问", "拒绝：未放行修改权限 端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress + " 目标=" + request.GetQuery("p"));
                    this.WriteText(stream, "该端口未放行修改权限", 403);
                    return;
                }
                string target = request.GetQuery("p");
                if (!this.CheckPath(stream, profile, request, target))
                {
                    return;
                }
                workspace.WriteFile(target, request.Body);
                UiLogger.Log("网页端访问", "修改文件：端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 目标=" + target + " 字节数=" + request.Body.Length);
                this.WriteOk(stream);
                return;
            }
            this.WriteText(stream, "not found", 404);
        }

        /// <summary>只读文件读取：独立工作区走该工作区的文件访问能力，跟随桌面走本机读取。</summary>
        private void HandleReadFile(NetworkStream stream, HttpRequest request, WebAccessProfile profile, string workspaceId, IWebWorkspaceBridge bridge)
        {
            string target = request.GetQuery("p");
            if (!this.CheckPath(stream, profile, request, target))
            {
                return;
            }
            // 压缩包内的文件：先解出到临时文件再按普通文件读取，读完清理临时目录
            string? archiveTempFile = this.TryExtractArchiveFile(target);
            try
            {
                string text;
                if (archiveTempFile != null)
                {
                    text = WebWorkspace.ReadFileTextLocal(archiveTempFile);
                }
                else
                {
                    WebWorkspace? workspace = string.IsNullOrEmpty(workspaceId) ? null : bridge.FindWorkspace(workspaceId);
                    if (workspace != null)
                    {
                        text = workspace.ReadFileText(target);
                    }
                    else
                    {
                        text = WebWorkspace.ReadFileTextLocal(target);
                    }
                }
                UiLogger.Log("网页端访问", "只读打开文件：端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 目标=" + target + " 字符数=" + text.Length);
                string json = JsonSerializer.Serialize(new { path = target, text = text }, jsonOptions);
                this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
            }
            catch (Exception error)
            {
                UiLogger.LogError("网页端访问", "读取文件失败：端口=" + this.runtime.Port + " 目标=" + target
                    + " 请求编号=" + request.RequestId, error);
                this.WriteText(stream, "读取失败，请查看本机日志（请求编号=" + request.RequestId + "）", 500);
            }
            finally
            {
                this.DeleteArchiveTempFile(archiveTempFile);
            }
        }

        /// <summary>
        /// 路径位于压缩包内时解出到临时文件并返回其路径；不是压缩包内路径时返回 null。
        /// </summary>
        private string? TryExtractArchiveFile(string target)
        {
            if (!ArchiveFileSystemProvider.TryCreate(this.host.archiveService, target, out ArchiveFileSystemProvider? provider)
                || provider == null || !provider.OwnsPath(target) || provider.IsRoot(target))
            {
                return null;
            }
            return provider.ExtractFileToTemp(target);
        }

        /// <summary>删除压缩包解出的临时文件及其所在目录；失败时忽略（临时目录会被系统回收）。</summary>
        private void DeleteArchiveTempFile(string? archiveTempFile)
        {
            if (string.IsNullOrEmpty(archiveTempFile))
            {
                return;
            }
            try
            {
                string? tempDirectory = Path.GetDirectoryName(archiveTempFile);
                if (!string.IsNullOrEmpty(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>读取 IDE 编辑器全文：独立工作区走该工作区的 IDE 外壳，跟随桌面走桌面 IDE 外壳。</summary>
        private void HandleIdeText(NetworkStream stream, HttpRequest request, string workspaceId, IWebWorkspaceBridge bridge)
        {
            int index;
            if (!int.TryParse(request.GetQuery("i"), out index))
            {
                this.WriteText(stream, "缺少编辑器下标", 400);
                return;
            }
            try
            {
                string payload;
                WebWorkspace? workspace = string.IsNullOrEmpty(workspaceId) ? null : bridge.FindWorkspace(workspaceId);
                if (workspace != null)
                {
                    payload = workspace.ReadIdeText(index);
                }
                else
                {
                    payload = bridge.ReadDesktopIdeText(index);
                }
                if (payload.Length == 0)
                {
                    this.WriteText(stream, "编辑器不存在", 404);
                    return;
                }
                int split = payload.IndexOf('\n');
                string token = split < 0 ? payload : payload.Substring(0, split);
                string text = split < 0 ? string.Empty : payload.Substring(split + 1);
                UiLogger.Log("网页端访问", "读取 IDE 编辑器文本：端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                    + " 编辑器下标=" + index + " 字符数=" + text.Length);
                string json = JsonSerializer.Serialize(new { index = index, token = token, text = text }, jsonOptions);
                this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
            }
            catch (Exception error)
            {
                UiLogger.LogError("网页端访问", "读取 IDE 编辑器文本失败：端口=" + this.runtime.Port + " 编辑器下标=" + index
                    + " 请求编号=" + request.RequestId, error);
                this.WriteText(stream, "读取失败，请查看本机日志（请求编号=" + request.RequestId + "）", 500);
            }
        }

        /// <summary>
        /// 事件推送（SSE）：建连后先发送当前快照，之后由事件中枢在状态变化时推送；
        /// 空闲时每 20 秒发送一次心跳注释，客户端断开或服务停止时退出。
        /// </summary>
        private void HandleEvents(NetworkStream stream, HttpRequest request, WebAccessProfile profile,
            IWebWorkspaceBridge bridge)
        {
            string workspaceId = request.GetQuery("w");
            if (string.IsNullOrEmpty(workspaceId) && profile.AllowedPaths != null && profile.AllowedPaths.Count > 0)
            {
                UiLogger.Log("网页端事件", "拒绝：该权限方案限定了可访问目录，跟随桌面事件不可用 端口=" + this.runtime.Port
                    + " 来源=" + request.RemoteAddress + " 方案=" + profile.Name);
                this.WriteText(stream, "restricted-paths", 403);
                return;
            }
            string scope = string.IsNullOrEmpty(workspaceId) ? "desktop" : workspaceId;
            if (!string.IsNullOrEmpty(workspaceId))
            {
                WebWorkspace? workspace = bridge.FindWorkspace(workspaceId);
                if (workspace == null)
                {
                    this.WriteText(stream, "workspace-missing", 410);
                    return;
                }
            }
            WebEventHub? hub = this.host.eventHub;
            if (hub == null || !hub.IsRunning)
            {
                this.WriteText(stream, "服务未就绪", 503);
                return;
            }

            WebSseClient client = hub.Subscribe(scope);
            try
            {
                this.WriteSseHeaders(stream);
                DateTime lastHeartbeat = DateTime.UtcNow;
                while (hub.IsRunning && this.running && !client.IsClosed)
                {
                    string? message = client.Take(1000);
                    if (!string.IsNullOrEmpty(message))
                    {
                        this.WriteSseChunk(stream, Encoding.UTF8.GetBytes(message));
                        lastHeartbeat = DateTime.UtcNow;
                        continue;
                    }
                    if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= 20)
                    {
                        string heartbeat = ": 心跳 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n";
                        this.WriteSseChunk(stream, Encoding.UTF8.GetBytes(heartbeat));
                        lastHeartbeat = DateTime.UtcNow;
                    }
                }
            }
            catch (IOException)
            {
                // 客户端关闭连接属于正常结束
            }
            catch (ObjectDisposedException)
            {
                // 服务停止时套接字已释放
            }
            catch (Exception error)
            {
                UiLogger.LogError("网页端事件", "推送连接异常 端口=" + this.runtime.Port
                    + " 来源=" + request.RemoteAddress + " 作用域=" + scope, error);
            }
            finally
            {
                try
                {
                    // 结束分块响应：浏览器看到 0 长度块后正常关闭事件流
                    byte[] finalChunk = Encoding.ASCII.GetBytes("0\r\n\r\n");
                    stream.Write(finalChunk, 0, finalChunk.Length);
                    stream.Flush();
                }
                catch (Exception)
                {
                    // 客户端已断开时结束块写入失败可忽略
                }
                hub.Unsubscribe(client);
            }
        }

        /// <summary>写出 SSE 响应头：采用分块传输，避免无 Content-Length 的长连接被浏览器缓存或截断。</summary>
        private void WriteSseHeaders(NetworkStream stream)
        {
            HttpRequest? active = WebEndpoint.currentRequest;
            if (active != null)
            {
                active.ResponseStatus = 200;
                active.ResponsePreview = "事件推送已连接";
            }
            string header = "HTTP/1.1 200 OK\r\n"
                + "Content-Type: text/event-stream; charset=utf-8\r\n"
                + "Cache-Control: no-store, no-cache\r\n"
                + "Transfer-Encoding: chunked\r\n"
                + "Connection: keep-alive\r\n"
                + "X-Accel-Buffering: no\r\n"
                + "\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Flush();
        }

        /// <summary>写出一条分块编码的 SSE 消息。</summary>
        private void WriteSseChunk(NetworkStream stream, byte[] body)
        {
            string sizeText = body.Length.ToString("x") + "\r\n";
            byte[] sizeBytes = Encoding.ASCII.GetBytes(sizeText);
            stream.Write(sizeBytes, 0, sizeBytes.Length);
            stream.Write(body, 0, body.Length);
            byte[] tail = Encoding.ASCII.GetBytes("\r\n");
            stream.Write(tail, 0, tail.Length);
            stream.Flush();
        }

        /// <summary>工作区状态：工作区编号为空时返回跟随桌面的快照。</summary>
        private void HandleState(NetworkStream stream, HttpRequest request, WebAccessProfile profile)
        {
            IWebWorkspaceBridge? bridge = this.host.bridge;
            if (bridge == null)
            {
                this.WriteText(stream, "服务未就绪", 503);
                return;
            }
            string workspaceId = request.GetQuery("w");
            if (string.IsNullOrEmpty(workspaceId))
            {
                if (profile.AllowedPaths != null && profile.AllowedPaths.Count > 0)
                {
                    UiLogger.Log("网页端访问", "拒绝：该权限方案限定了可访问目录，跟随桌面快照不可用 端口=" + this.runtime.Port
                        + " 来源=" + request.RemoteAddress + " 方案=" + profile.Name);
                    this.WriteText(stream, "restricted-paths", 403);
                    return;
                }
                DebugUiState state = bridge.GetDesktopStateAsync().GetAwaiter().GetResult();
                state.FollowDesktop = true;
                string json = JsonSerializer.Serialize(state, jsonOptions);
                this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                return;
            }
            WebWorkspace? workspace = bridge.FindWorkspace(workspaceId);
            if (workspace == null)
            {
                this.WriteText(stream, "workspace-missing", 410);
                return;
            }
            DebugUiState workspaceState = workspace.BuildStateAsync().GetAwaiter().GetResult();
            workspaceState.FollowDesktop = false;
            string workspaceJson = JsonSerializer.Serialize(workspaceState, jsonOptions);
            this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(workspaceJson));
        }

        /// <summary>切换工作区模式：新建独立工作区并返回其编号。</summary>
        private void HandleMode(NetworkStream stream, HttpRequest request, IWebWorkspaceBridge bridge)
        {
            string mode = request.GetQuery("m");
            if (mode == "independent")
            {
                WebWorkspace workspace = bridge.CreateWorkspace();
                UiLogger.Log("网页端访问", "新建独立工作区：端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress + " 工作区=" + workspace.Id);
                string json = JsonSerializer.Serialize(new { workspaceId = workspace.Id }, jsonOptions);
                this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                return;
            }
            this.WriteOk(stream);
        }

        /// <summary>校验路径白名单；不放行时写出 403 并记录日志。</summary>
        private bool CheckPath(NetworkStream stream, WebAccessProfile profile, HttpRequest request, string path)
        {
            if (WebAccessControl.IsPathAllowed(profile, path))
            {
                return true;
            }
            UiLogger.Log("网页端访问", "拒绝：路径不在白名单 端口=" + this.runtime.Port + " 来源=" + request.RemoteAddress
                + " 目标=" + path + " 方案=" + profile.Name);
            this.WriteText(stream, "该路径未获授权", 403);
            return false;
        }

        /// <summary>校验新建与删除权限；不放行时写出 403 并记录日志。</summary>
        private bool RequireCreateDelete(NetworkStream stream, WebAccessProfile profile, HttpRequest request, string action)
        {
            if (profile.AllowCreateDelete)
            {
                return true;
            }
            UiLogger.Log("网页端访问", "拒绝：" + action + " 未放行 端口=" + this.runtime.Port
                + " 来源=" + request.RemoteAddress + " 目标=" + request.GetQuery("p") + " 方案=" + profile.Name);
            this.WriteText(stream, "该端口未放行新建与删除权限", 403);
            return false;
        }

        /// <summary>签发或返回当前会话的会话令牌与 CSRF 令牌；页面只保存在内存中，不写入本地存储。</summary>
        private void HandleSession(NetworkStream stream, HttpRequest request, WebAccessProfile profile)
        {
            WebSession session = request.Session ?? this.host.sessionStore.GetOrCreateAnonymous(profile);
            string json = JsonSerializer.Serialize(new
            {
                sessionToken = session.Token,
                csrfToken = session.CsrfToken,
                account = WebSecurity.DescribeAccount(session.Account),
                profile = profile.Name,
                expiresInSeconds = WebSecurity.SessionIdleMinutes * 60,
            }, jsonOptions);
            this.WriteBytes(stream, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
        }

        /// <summary>构建当前端口的权限说明，供页面隐藏无权操作的按钮。</summary>
        private object BuildPermissions(WebAccessProfile profile)
        {
            return new
            {
                profile = profile.Name,
                canModify = profile.AllowModify,
                canCreateDelete = profile.AllowCreateDelete,
                requirePassword = profile.RequirePassword,
                port = this.runtime.Port,
            };
        }

        private void WriteOk(NetworkStream stream)
        {
            this.WriteText(stream, "ok");
        }

        private void WriteText(NetworkStream stream, string text, int statusCode = 200, int retryAfterSeconds = 0)
        {
            this.WriteBytes(stream, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), statusCode, null, retryAfterSeconds);
        }

        private void WriteAuthChallenge(NetworkStream stream)
        {
            HttpRequest? active = WebEndpoint.currentRequest;
            if (active != null)
            {
                active.ResponseStatus = 401;
                active.ResponsePreview = "需要账号密码验证";
            }
            string body = "需要账号密码验证";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            string header = "HTTP/1.1 401 Unauthorized\r\n"
                + "Content-Type: text/plain; charset=utf-8\r\n"
                + "Content-Length: " + bytes.Length + "\r\n"
                + "WWW-Authenticate: Basic realm=\"TabTaskExplorer\"\r\n"
                + "Cache-Control: no-store\r\n"
                + "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// 写出响应：同时把状态码与脱敏后的响应摘要回填到当前请求，供审计日志记录；
        /// cacheControl 为空时沿用 no-store，静态资源可显式传入长期缓存策略。
        /// </summary>
        private void WriteBytes(NetworkStream stream, string contentType, byte[] body, int statusCode = 200,
            string? cacheControl = null, int retryAfterSeconds = 0)
        {
            HttpRequest? active = WebEndpoint.currentRequest;
            if (active != null)
            {
                active.ResponseStatus = statusCode;
                if (contentType.StartsWith("text/", StringComparison.Ordinal)
                    || contentType.StartsWith("application/json", StringComparison.Ordinal))
                {
                    string preview = Encoding.UTF8.GetString(body);
                    if (preview.Length > 200)
                    {
                        preview = preview.Substring(0, 200) + "…";
                    }
                    active.ResponsePreview = WebSecurity.MaskSecrets(preview);
                }
                else
                {
                    active.ResponsePreview = "二进制响应，字节数=" + body.Length;
                }
            }

            string reason = "OK";
            if (statusCode == 400)
            {
                reason = "Bad Request";
            }
            else if (statusCode == 401)
            {
                reason = "Unauthorized";
            }
            else if (statusCode == 403)
            {
                reason = "Forbidden";
            }
            else if (statusCode == 404)
            {
                reason = "Not Found";
            }
            else if (statusCode == 410)
            {
                reason = "Gone";
            }
            else if (statusCode == 413)
            {
                reason = "Payload Too Large";
            }
            else if (statusCode == 429)
            {
                reason = "Too Many Requests";
            }
            else if (statusCode == 500)
            {
                reason = "Internal Server Error";
            }
            else if (statusCode == 503)
            {
                reason = "Service Unavailable";
            }
            string cacheHeader = cacheControl == null
                ? "Cache-Control: no-store\r\n"
                : "Cache-Control: " + cacheControl + "\r\n";
            string retryHeader = retryAfterSeconds > 0 ? "Retry-After: " + retryAfterSeconds + "\r\n" : string.Empty;
            string header = "HTTP/1.1 " + statusCode + " " + reason + "\r\n"
                + "Content-Type: " + contentType + "\r\n"
                + "Content-Length: " + body.Length + "\r\n"
                + cacheHeader
                + retryHeader
                + "Connection: close\r\n"
                + "\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }
}

/// <summary>一个已解析的端口运行时配置：端口号、监听范围与解析后的权限方案。</summary>
public class WebPortRuntime
{
    /// <summary>监听端口。</summary>
    public int Port { get; set; }

    /// <summary>监听范围：Loopback 或 Any。</summary>
    public string BindScope { get; set; } = WebBindScope.Loopback;

    /// <summary>该端口使用的权限方案。</summary>
    public WebAccessProfile Profile { get; set; } = WebAccessProfile.CreateDefault();
}

/// <summary>解析后的 HTTP 请求：方法、路径、查询参数、请求头与请求体。</summary>
internal class HttpRequest
{
    private const int MaxHeaderBytes = 32768;
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    /// <summary>请求方法（GET / POST）。</summary>
    public string Method { get; private set; } = string.Empty;

    /// <summary>不含查询串的路径。</summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>查询串（不含问号）。</summary>
    public string Query { get; private set; } = string.Empty;

    /// <summary>请求头（键统一小写）。</summary>
    public Dictionary<string, string> Headers { get; private set; } = new Dictionary<string, string>();

    /// <summary>请求体原始字节。</summary>
    public byte[] Body { get; private set; } = new byte[0];

    /// <summary>来源地址。</summary>
    public IPAddress? RemoteAddress { get; set; }

    /// <summary>请求编号：错误响应与审计日志用它关联本机日志，不回显异常细节。</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>当前请求使用的会话；认证失败时为空。</summary>
    public WebSession? Session { get; set; }

    /// <summary>当前请求的账号；匿名访问时为空文本。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>审计日志：已写出的响应状态码。</summary>
    public int ResponseStatus { get; set; }

    /// <summary>审计日志：脱敏后的响应摘要。</summary>
    public string ResponsePreview { get; set; } = string.Empty;

    /// <summary>审计日志：本次操作的目标路径或对象（由具体处理函数补充）。</summary>
    public string AuditTarget { get; set; } = string.Empty;

    /// <summary>读取一个完整请求；请求非法时返回 null。</summary>
    public static HttpRequest? Read(NetworkStream stream)
    {
        byte[] buffer = new byte[MaxHeaderBytes];
        int total = 0;
        int headerEnd = -1;
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);

        while (total < buffer.Length && DateTime.UtcNow < deadline)
        {
            if (!stream.DataAvailable)
            {
                Thread.Sleep(5);
                continue;
            }
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                break;
            }
            total = total + read;
            headerEnd = FindHeaderEnd(buffer, total);
            if (headerEnd >= 0)
            {
                break;
            }
        }
        if (headerEnd < 0)
        {
            return null;
        }

        string headerText = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        string[] lines = headerText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
        if (lines.Length < 1)
        {
            return null;
        }
        string[] parts = lines[0].Split(' ');
        if (parts.Length < 2)
        {
            return null;
        }

        HttpRequest request = new HttpRequest();
        request.Method = parts[0].ToUpperInvariant();
        string rawUrl = parts[1];
        int queryStart = rawUrl.IndexOf('?');
        if (queryStart >= 0)
        {
            request.Path = rawUrl.Substring(0, queryStart);
            request.Query = rawUrl.Substring(queryStart + 1);
        }
        else
        {
            request.Path = rawUrl;
        }
        for (int index = 1; index < lines.Length; index++)
        {
            int colon = lines[index].IndexOf(':');
            if (colon > 0)
            {
                string key = lines[index].Substring(0, colon).Trim().ToLowerInvariant();
                string value = lines[index].Substring(colon + 1).Trim();
                request.Headers[key] = value;
            }
        }

        int contentLength = 0;
        string lengthText;
        if (request.Headers.TryGetValue("content-length", out lengthText))
        {
            int.TryParse(lengthText, out contentLength);
        }
        if (contentLength < 0)
        {
            contentLength = 0;
        }
        if (contentLength > MaxBodyBytes)
        {
            contentLength = MaxBodyBytes;
        }

        int bodyStart = headerEnd + 4;
        int alreadyRead = total - bodyStart;
        if (alreadyRead < 0)
        {
            alreadyRead = 0;
        }
        byte[] body = new byte[contentLength];
        int copied = alreadyRead < contentLength ? alreadyRead : contentLength;
        if (copied > 0)
        {
            Array.Copy(buffer, bodyStart, body, 0, copied);
        }
        while (copied < contentLength)
        {
            int read = stream.Read(body, copied, contentLength - copied);
            if (read <= 0)
            {
                break;
            }
            copied = copied + read;
        }
        if (copied < contentLength)
        {
            byte[] shortened = new byte[copied];
            Array.Copy(body, shortened, copied);
            request.Body = shortened;
        }
        else
        {
            request.Body = body;
        }
        return request;
    }

    /// <summary>读取查询参数值；不存在返回空文本。</summary>
    public string GetQuery(string key)
    {
        foreach (string pair in this.Query.Split('&'))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0 && pair.Substring(0, equals) == key)
            {
                return Uri.UnescapeDataString(pair.Substring(equals + 1));
            }
        }
        return string.Empty;
    }

    /// <summary>查找请求头结束位置（连续两个 CRLF 的起点）。</summary>
    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        for (int index = 0; index + 3 < length; index++)
        {
            if (buffer[index] == '\r' && buffer[index + 1] == '\n' && buffer[index + 2] == '\r' && buffer[index + 3] == '\n')
            {
                return index;
            }
        }
        return -1;
    }
}
