using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 事件推送中枢：为每条 SSE 连接维护一个消息队列，由后台线程定期比较界面状态快照，
/// 只在标签、文件列表、任务、连接、进程、IDE、主题等分区发生变化时推送完整状态。
/// 新连接或断线重连时发送 snapshot，保证客户端立即恢复到最新状态。
/// </summary>
internal sealed class WebEventHub
{
    private const string DesktopScope = "desktop";
    private const int IdleRefreshMilliseconds = 1000;

    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IWebWorkspaceBridge bridge;
    private readonly object clientGate = new object();
    private readonly object cacheGate = new object();
    private readonly List<WebSseClient> clients = new List<WebSseClient>();
    private readonly Dictionary<string, ScopeCache> caches = new Dictionary<string, ScopeCache>(StringComparer.Ordinal);
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private Thread? worker;
    private volatile bool running;

    public WebEventHub(IWebWorkspaceBridge bridge)
    {
        this.bridge = bridge;
    }

    /// <summary>是否正在运行。</summary>
    public bool IsRunning
    {
        get { return this.running; }
    }

    /// <summary>启动后台比较线程；重复调用不会创建第二个线程。</summary>
    public void Start()
    {
        if (this.running)
        {
            return;
        }
        this.running = true;
        this.worker = new Thread(new ThreadStart(this.PumpLoop));
        this.worker.IsBackground = true;
        this.worker.Start();
    }

    /// <summary>停止后台线程并关闭全部推送连接。</summary>
    public void Stop()
    {
        this.running = false;
        this.wake.Set();
        WebSseClient[] closing;
        lock (this.clientGate)
        {
            closing = this.clients.ToArray();
            this.clients.Clear();
        }
        foreach (WebSseClient client in closing)
        {
            client.Close();
        }
        Thread? current = this.worker;
        this.worker = null;
        if (current != null && current.IsAlive)
        {
            try
            {
                current.Join(500);
            }
            catch (Exception)
            {
                // 停止时等待超时不影响服务关闭
            }
        }
        lock (this.cacheGate)
        {
            this.caches.Clear();
        }
    }

    /// <summary>订阅指定作用域（desktop 或独立工作区编号）；新订阅者会收到一次快照。</summary>
    public WebSseClient Subscribe(string scope)
    {
        WebSseClient client = new WebSseClient(scope);
        client.NeedsSnapshot = true;
        lock (this.clientGate)
        {
            this.clients.Add(client);
        }
        this.wake.Set();
        return client;
    }

    /// <summary>取消订阅；连接关闭时由服务端调用。</summary>
    public void Unsubscribe(WebSseClient client)
    {
        lock (this.clientGate)
        {
            this.clients.Remove(client);
        }
        client.Close();
    }

    /// <summary>请求立即比较一次，用于写操作完成后缩短推送延迟。</summary>
    public void RequestRefresh()
    {
        this.wake.Set();
    }

    private void PumpLoop()
    {
        while (this.running)
        {
            this.wake.WaitOne(WebEventHub.IdleRefreshMilliseconds);
            if (!this.running)
            {
                break;
            }
            WebSseClient[] snapshot;
            lock (this.clientGate)
            {
                snapshot = this.clients.ToArray();
            }
            if (snapshot.Length == 0)
            {
                lock (this.cacheGate)
                {
                    this.caches.Clear();
                }
                continue;
            }
            HashSet<string> scopes = new HashSet<string>(StringComparer.Ordinal);
            foreach (WebSseClient client in snapshot)
            {
                if (!client.IsClosed)
                {
                    scopes.Add(client.Scope);
                }
            }
            foreach (string scope in scopes)
            {
                try
                {
                    this.BroadcastScope(scope, snapshot);
                }
                catch (Exception error)
                {
                    // 单个作用域序列化失败不能终止推送线程，其他作用域继续工作
                    UiLogger.LogError("网页端事件", "推送作用域失败 作用域=" + scope, error);
                }
            }
        }
    }

    /// <summary>比较指定作用域的快照并把变化推送给该作用域的全部连接。</summary>
    private void BroadcastScope(string scope, WebSseClient[] snapshot)
    {
        List<WebSseClient> targets = new List<WebSseClient>();
        foreach (WebSseClient client in snapshot)
        {
            if (!client.IsClosed && string.Equals(client.Scope, scope, StringComparison.Ordinal))
            {
                targets.Add(client);
            }
        }
        if (targets.Count == 0)
        {
            lock (this.cacheGate)
            {
                this.caches.Remove(scope);
            }
            return;
        }

        DebugUiState? state;
        try
        {
            state = this.BuildState(scope);
        }
        catch (Exception error)
        {
            UiLogger.LogError("网页端事件", "构建状态快照失败 作用域=" + scope, error);
            this.SendError(targets, 500, "状态快照构建失败，请稍后重试");
            return;
        }
        if (state == null)
        {
            this.SendError(targets, 410, "工作区不存在或已过期，请刷新页面");
            lock (this.cacheGate)
            {
                this.caches.Remove(scope);
            }
            return;
        }

        ScopeCache cache;
        lock (this.cacheGate)
        {
            if (!this.caches.TryGetValue(scope, out cache))
            {
                cache = new ScopeCache();
                this.caches[scope] = cache;
            }
        }
        List<string> changed = cache.Update(state);
        bool hasState = cache.LastJson.Length > 0;
        if (changed.Count == 0 && hasState)
        {
            // 没有分区变化时只补给新订阅者快照，不向已连接页面重复发送全量 JSON
            foreach (WebSseClient client in targets)
            {
                if (client.NeedsSnapshot)
                {
                    client.NeedsSnapshot = false;
                    client.Enqueue(WebEventHub.BuildMessage("snapshot", cache.Version, scope,
                        new string[] { "snapshot" }, cache.LastJson));
                }
            }
            return;
        }

        string stateJson = JsonSerializer.Serialize(state, WebEventHub.jsonOptions);
        cache.LastJson = stateJson;
        cache.Version++;
        foreach (WebSseClient client in targets)
        {
            bool isSnapshot = client.NeedsSnapshot || !hasState;
            client.NeedsSnapshot = false;
            string[] types = isSnapshot ? new string[] { "snapshot" } : changed.ToArray();
            client.Enqueue(WebEventHub.BuildMessage(isSnapshot ? "snapshot" : "state", cache.Version, scope, types, stateJson));
        }
    }

    private DebugUiState? BuildState(string scope)
    {
        if (string.Equals(scope, WebEventHub.DesktopScope, StringComparison.Ordinal))
        {
            DebugUiState state = this.bridge.GetDesktopStateAsync().GetAwaiter().GetResult();
            state.FollowDesktop = true;
            return state;
        }
        WebWorkspace? workspace = this.bridge.FindWorkspace(scope);
        if (workspace == null)
        {
            return null;
        }
        DebugUiState workspaceState = workspace.BuildStateAsync().GetAwaiter().GetResult();
        workspaceState.FollowDesktop = false;
        return workspaceState;
    }

    private void SendError(List<WebSseClient> targets, int code, string messageText)
    {
        string payload = "{\"code\":" + code + ",\"message\":" + JsonSerializer.Serialize(messageText, WebEventHub.jsonOptions) + "}";
        string text = "event: error\ndata: " + payload + "\n\n";
        foreach (WebSseClient client in targets)
        {
            client.Enqueue(text);
            client.Close();
        }
    }

    private static string BuildMessage(string eventName, int version, string scope, IEnumerable<string> types,
        string stateJson)
    {
        string typesJson = JsonSerializer.Serialize(types, WebEventHub.jsonOptions);
        string scopeJson = JsonSerializer.Serialize(scope, WebEventHub.jsonOptions);
        return "event: " + eventName + "\n"
            + "id: " + version + "\n"
            + "data: {\"version\":" + version + ",\"scope\":" + scopeJson
            + ",\"types\":" + typesJson + ",\"state\":" + stateJson + "}\n\n";
    }

    /// <summary>一个作用域的变化缓存：按分区比较，只有变化的分区名会进入 types。</summary>
    private sealed class ScopeCache
    {
        private readonly Dictionary<string, string> sections = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Version { get; set; }

        public string LastJson { get; set; } = string.Empty;

        /// <summary>比较并更新分区，返回发生变化的分区名称。</summary>
        public List<string> Update(DebugUiState state)
        {
            List<string> changed = new List<string>();
            this.Compare("tabs", JsonSerializer.Serialize(state.Tabs.Select(tab => new
            {
                tab.Index,
                tab.TabType,
                tab.Title,
                tab.Selected,
                tab.CurrentPath,
                tab.TotalRows,
                tab.SortField,
                tab.SortAscending,
                tab.SearchText,
                tab.ViewMode,
                tab.ShowInfoPane,
                tab.CanGoBack,
                tab.CanGoForward,
                tab.CheckedCount,
                tab.SummaryText,
                tab.ResourceText,
                tab.DetailText,
                tab.AffinityInput,
                tab.PriorityIndex,
                tab.DetailSupported,
            }).ToList(), WebEventHub.jsonOptions), changed);

            this.Compare("files", JsonSerializer.Serialize(state.Tabs.Select(tab => new
            {
                tab.Index,
                tab.CurrentPath,
                tab.TotalRows,
                tab.Rows,
            }).ToList(), WebEventHub.jsonOptions), changed);

            this.Compare("tasks", JsonSerializer.Serialize(state.Tabs.Select(tab => new
            {
                tab.Index,
                tab.Todo,
                tab.InProgress,
                tab.Done,
            }).ToList(), WebEventHub.jsonOptions), changed);

            this.Compare("connections", JsonSerializer.Serialize(state.Tabs.Select(tab => new
            {
                tab.Index,
                tab.Connections,
                tab.SelectedConnection,
                tab.SelectedConnectionId,
            }).ToList(), WebEventHub.jsonOptions), changed);

            this.Compare("process", JsonSerializer.Serialize(state.Tabs.Select(tab => new
            {
                tab.Index,
                tab.ProcessRows,
                tab.SystemCpuTrend,
                tab.SystemMemoryTrend,
                tab.SelectedCpuTrend,
                tab.TrendVersion,
            }).ToList(), WebEventHub.jsonOptions), changed);

            this.Compare("startup", JsonSerializer.Serialize(state.Tabs.Select(tab => new
            {
                tab.Index,
                tab.StartupRows,
                tab.CustomStartupRows,
                tab.StartupEnvironmentText,
            }).ToList(), WebEventHub.jsonOptions), changed);

            this.Compare("ide", JsonSerializer.Serialize(new
            {
                state.IsIdeMode,
                state.Ide,
            }, WebEventHub.jsonOptions), changed);

            this.Compare("theme", JsonSerializer.Serialize(new
            {
                state.Theme,
                state.ShowHiddenFiles,
                state.ShowFileExtensions,
                state.TabCount,
                state.RecentPaths,
                state.NavPinned,
                state.NavPc,
                state.Notice,
                state.WorkspaceId,
            }, WebEventHub.jsonOptions), changed);

            return changed;
        }

        private void Compare(string key, string value, List<string> changed)
        {
            string previous;
            if (!this.sections.TryGetValue(key, out previous) || !string.Equals(previous, value, StringComparison.Ordinal))
            {
                this.sections[key] = value;
                changed.Add(key);
            }
        }
    }
}

/// <summary>
/// 单条 SSE 连接的消息队列：后台线程写入，连接线程读取并写出；
/// 队列只保留最近的消息，避免客户端长时间不读导致内存增长。
/// </summary>
internal sealed class WebSseClient
{
    private readonly object gate = new object();
    private readonly Queue<string> messages = new Queue<string>();
    private bool closed;

    public WebSseClient(string scope)
    {
        this.Scope = scope;
    }

    /// <summary>作用域：desktop 或独立工作区编号。</summary>
    public string Scope { get; private set; }

    /// <summary>是否为尚未收到首帧快照的新连接（由事件中枢读写）。</summary>
    public bool NeedsSnapshot { get; set; }

    /// <summary>连接是否已关闭。</summary>
    public bool IsClosed
    {
        get
        {
            lock (this.gate)
            {
                return this.closed;
            }
        }
    }

    /// <summary>加入一条待写出的 SSE 消息；队列超过上限时丢弃最旧的消息。</summary>
    public void Enqueue(string message)
    {
        lock (this.gate)
        {
            if (this.closed)
            {
                return;
            }
            this.messages.Enqueue(message);
            while (this.messages.Count > 64)
            {
                this.messages.Dequeue();
            }
            Monitor.PulseAll(this.gate);
        }
    }

    /// <summary>等待并取出下一条消息；超时或连接关闭时返回 null。</summary>
    public string? Take(int timeoutMilliseconds)
    {
        lock (this.gate)
        {
            if (this.closed)
            {
                return null;
            }
            if (this.messages.Count == 0)
            {
                Monitor.Wait(this.gate, timeoutMilliseconds);
            }
            if (this.closed)
            {
                return null;
            }
            return this.messages.Count > 0 ? this.messages.Dequeue() : null;
        }
    }

    /// <summary>关闭连接并唤醒等待线程。</summary>
    public void Close()
    {
        lock (this.gate)
        {
            this.closed = true;
            this.messages.Clear();
            Monitor.PulseAll(this.gate);
        }
    }
}
