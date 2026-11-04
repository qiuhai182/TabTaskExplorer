using System;
using System.Collections.Generic;
using System.Text;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 网页端安全基线：会话令牌、CSRF 令牌、请求限流、登录失败次数限制、审计日志与日志脱敏。
/// 全部为纯托管实现，Windows / Linux / macOS 行为一致。
/// </summary>
public static class WebSecurity
{
    /// <summary>写操作必须携带的 CSRF 请求头名称。</summary>
    public const string CsrfHeaderName = "X-CSRF-Token";

    /// <summary>每个来源地址每分钟允许的请求总数（包含静态资源与页面请求）。</summary>
    public const int GeneralRequestsPerMinute = 600;

    /// <summary>每个来源地址与账号每分钟允许的写操作数。</summary>
    public const int WriteRequestsPerMinute = 60;

    /// <summary>同一来源地址在统计窗口内允许的登录失败次数。</summary>
    public const int LoginFailuresPerWindow = 5;

    /// <summary>登录失败次数达到上限后的锁定时长（秒）。</summary>
    public const int LoginLockoutSeconds = 60;

    /// <summary>会话空闲超时（分钟）；超过后需要重新认证。</summary>
    public const int SessionIdleMinutes = 480;

    /// <summary>会话绝对有效期（小时）；即使一直活跃也会强制重新认证。</summary>
    public const int SessionAbsoluteHours = 24;

    /// <summary>单个会话允许保留的最长数量，超出后顶出最旧的会话，避免内存无限增长。</summary>
    public const int MaxSessions = 512;



    /// <summary>生成 URL 安全的随机令牌（Base64Url，不带填充）。</summary>
    public static string GenerateToken(int byteLength)
    {
        byte[] raw = System.Security.Cryptography.RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>生成短请求编号，用于在错误响应中关联本机日志，不暴露异常细节。</summary>
    public static string GenerateRequestId()
    {
        return WebSecurity.GenerateToken(5);
    }

    /// <summary>
    /// 对日志文本中的密码、密钥、Bearer 与会话令牌、Authorization 头、URL 内嵌凭据做脱敏。
    /// 统一委托给 <see cref="LogSanitizer"/>，保证网页端审计与诊断包使用同一套规则。
    /// </summary>
    public static string MaskSecrets(string? text)
    {
        return LogSanitizer.MaskSecrets(text);
    }

    /// <summary>固定时间比较两个文本，避免通过比较耗时推断令牌。</summary>
    public static bool FixedTimeEquals(string? left, string? right)
    {
        if (left == null || right == null)
        {
            return false;
        }
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    /// <summary>是否是需要 CSRF 与同源校验的写方法。</summary>
    public static bool IsWriteMethod(string method)
    {
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    /// <summary>审计日志中的账号显示：空账号统一显示为匿名。</summary>
    public static string DescribeAccount(string? account)
    {
        if (string.IsNullOrEmpty(account))
        {
            return "匿名";
        }
        return account;
    }
}

/// <summary>一个网页端会话：会话令牌、CSRF 令牌、所属权限方案与账号。</summary>
public sealed class WebSession
{
    /// <summary>会话令牌（Bearer 认证使用）。</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>CSRF 令牌（写操作请求头使用）。</summary>
    public string CsrfToken { get; set; } = string.Empty;

    /// <summary>签发该会话的权限方案名称。</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>认证账号；匿名访问时为空文本。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>创建时间（UTC）。</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>最后访问时间（UTC）。</summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>是否已过期：空闲超时或绝对有效期到期。</summary>
    public bool IsExpired(DateTime now)
    {
        if ((now - this.LastSeenUtc).TotalMinutes > WebSecurity.SessionIdleMinutes)
        {
            return true;
        }
        return (now - this.CreatedUtc).TotalHours > WebSecurity.SessionAbsoluteHours;
    }
}

/// <summary>
/// 会话仓库：按权限方案签发会话，Basic 认证成功时复用同一账号的会话；
/// 匿名方案也签发会话，使写操作同样具备 CSRF 令牌。
/// </summary>
public sealed class WebSessionStore
{
    private readonly object gate = new object();
    private readonly Dictionary<string, WebSession> byToken = new Dictionary<string, WebSession>(StringComparer.Ordinal);
    private readonly Dictionary<string, WebSession> byIdentity = new Dictionary<string, WebSession>(StringComparer.Ordinal);

    /// <summary>清空全部会话（服务重启时调用）。</summary>
    public void Reset()
    {
        lock (this.gate)
        {
            this.byToken.Clear();
            this.byIdentity.Clear();
        }
    }

    /// <summary>按权限方案与账号取回或创建会话；Basic 认证每次请求都复用同一会话。</summary>
    public WebSession GetOrCreateBasic(WebAccessProfile profile, string account)
    {
        return this.GetOrCreate("认证:" + profile.Name + "\n" + account, profile, account);
    }

    /// <summary>为不要求密码的方案签发匿名会话。</summary>
    public WebSession GetOrCreateAnonymous(WebAccessProfile profile)
    {
        return this.GetOrCreate("匿名:" + profile.Name, profile, string.Empty);
    }

    /// <summary>按令牌查找有效会话；令牌无效、方案不匹配或已过期时返回 null。</summary>
    public WebSession? Find(string? token, WebAccessProfile profile)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        DateTime now = DateTime.UtcNow;
        lock (this.gate)
        {
            WebSession? session;
            if (!this.byToken.TryGetValue(token, out session))
            {
                return null;
            }
            if (!string.Equals(session.ProfileName, profile.Name, StringComparison.Ordinal) || session.IsExpired(now))
            {
                this.byToken.Remove(session.Token);
                this.byIdentity.Remove("认证:" + session.ProfileName + "\n" + session.Account);
                this.byIdentity.Remove("匿名:" + session.ProfileName);
                return null;
            }
            session.LastSeenUtc = now;
            return session;
        }
    }

    private WebSession GetOrCreate(string identityKey, WebAccessProfile profile, string account)
    {
        DateTime now = DateTime.UtcNow;
        lock (this.gate)
        {
            WebSession? existing;
            if (this.byIdentity.TryGetValue(identityKey, out existing) && !existing.IsExpired(now))
            {
                existing.LastSeenUtc = now;
                return existing;
            }
            if (existing != null)
            {
                this.byToken.Remove(existing.Token);
                this.byIdentity.Remove(identityKey);
            }
            WebSession session = new WebSession();
            session.Token = WebSecurity.GenerateToken(32);
            session.CsrfToken = WebSecurity.GenerateToken(24);
            session.ProfileName = profile.Name;
            session.Account = account;
            session.CreatedUtc = now;
            session.LastSeenUtc = now;
            this.byToken[session.Token] = session;
            this.byIdentity[identityKey] = session;
            this.TrimOldSessions(now);
            return session;
        }
    }

    /// <summary>会话数量超过上限时按最后访问时间顶出最旧的会话。</summary>
    private void TrimOldSessions(DateTime now)
    {
        if (this.byToken.Count <= WebSecurity.MaxSessions)
        {
            return;
        }
        string? oldestToken = null;
        DateTime oldestTime = DateTime.MaxValue;
        foreach (KeyValuePair<string, WebSession> pair in this.byToken)
        {
            if (pair.Value.LastSeenUtc < oldestTime)
            {
                oldestTime = pair.Value.LastSeenUtc;
                oldestToken = pair.Key;
            }
        }
        if (oldestToken != null)
        {
            WebSession removed = this.byToken[oldestToken];
            this.byToken.Remove(oldestToken);
            this.byIdentity.Remove("认证:" + removed.ProfileName + "\n" + removed.Account);
            this.byIdentity.Remove("匿名:" + removed.ProfileName);
        }
    }
}

/// <summary>
/// 滑动窗口限流器：按调用方给定的键统计窗口内请求数，超限时返回需要等待的秒数。
/// 键通常由来源地址、账号与操作类型组成；定时清理空桶，避免长期运行后无限增长。
/// </summary>
public sealed class WebRateLimiter
{
    private readonly object gate = new object();
    private readonly Dictionary<string, Queue<DateTime>> buckets = new Dictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
    private DateTime lastCleanupUtc = DateTime.UtcNow;

    /// <summary>尝试获取一次配额；成功返回 true，超限返回 false 并给出建议等待秒数。</summary>
    public bool TryAcquire(string key, int limit, int windowSeconds, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        DateTime now = DateTime.UtcNow;
        lock (this.gate)
        {
            this.Cleanup(now);
            Queue<DateTime> bucket;
            if (!this.buckets.TryGetValue(key, out bucket))
            {
                bucket = new Queue<DateTime>();
                this.buckets[key] = bucket;
            }
            while (bucket.Count > 0 && (now - bucket.Peek()).TotalSeconds >= windowSeconds)
            {
                bucket.Dequeue();
            }
            if (bucket.Count >= limit)
            {
                if (bucket.Count > 0)
                {
                    double wait = windowSeconds - (now - bucket.Peek()).TotalSeconds;
                    retryAfterSeconds = wait > 0 ? (int)Math.Ceiling(wait) : 1;
                }
                else
                {
                    retryAfterSeconds = 1;
                }
                return false;
            }
            bucket.Enqueue(now);
            return true;
        }
    }

    /// <summary>清理长时间没有请求的桶，避免来源地址或账号变化导致的内存增长。</summary>
    private void Cleanup(DateTime now)
    {
        if ((now - this.lastCleanupUtc).TotalSeconds < 60)
        {
            return;
        }
        this.lastCleanupUtc = now;
        List<string> stale = new List<string>();
        foreach (KeyValuePair<string, Queue<DateTime>> pair in this.buckets)
        {
            if (pair.Value.Count == 0 || (now - pair.Value.Peek()).TotalSeconds > 300)
            {
                stale.Add(pair.Key);
            }
        }
        foreach (string key in stale)
        {
            this.buckets.Remove(key);
        }
    }
}

/// <summary>
/// 登录失败次数限制：同一来源地址与权限方案在窗口内失败次数达到上限后锁定一段时间。
/// 正确密码仍然放行（先校验密码，再决定是否返回锁定提示），避免误伤正在输入正确密码的用户。
/// </summary>
public sealed class WebLoginFailureLimiter
{
    private readonly object gate = new object();
    private readonly Dictionary<string, Queue<DateTime>> failures = new Dictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> lockedUntil = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    private DateTime lastCleanupUtc = DateTime.UtcNow;

    /// <summary>查询是否处于锁定期；返回 true 时给出剩余秒数。</summary>
    public bool IsLocked(string key, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        DateTime now = DateTime.UtcNow;
        lock (this.gate)
        {
            DateTime until;
            if (this.lockedUntil.TryGetValue(key, out until))
            {
                if (now < until)
                {
                    retryAfterSeconds = (int)Math.Ceiling((until - now).TotalSeconds);
                    return true;
                }
                this.lockedUntil.Remove(key);
            }
            return false;
        }
    }

    /// <summary>记录一次登录失败；达到阈值时开始锁定。</summary>
    public void RecordFailure(string key)
    {
        DateTime now = DateTime.UtcNow;
        lock (this.gate)
        {
            this.Cleanup(now);
            Queue<DateTime> bucket;
            if (!this.failures.TryGetValue(key, out bucket))
            {
                bucket = new Queue<DateTime>();
                this.failures[key] = bucket;
            }
            while (bucket.Count > 0 && (now - bucket.Peek()).TotalSeconds >= 60)
            {
                bucket.Dequeue();
            }
            bucket.Enqueue(now);
            if (bucket.Count >= WebSecurity.LoginFailuresPerWindow)
            {
                this.lockedUntil[key] = now.AddSeconds(WebSecurity.LoginLockoutSeconds);
            }
        }
    }

    /// <summary>登录成功后清除失败计数与锁定状态。</summary>
    public void RecordSuccess(string key)
    {
        lock (this.gate)
        {
            this.failures.Remove(key);
            this.lockedUntil.Remove(key);
        }
    }

    private void Cleanup(DateTime now)
    {
        if ((now - this.lastCleanupUtc).TotalSeconds < 120)
        {
            return;
        }
        this.lastCleanupUtc = now;
        List<string> staleKeys = new List<string>();
        foreach (KeyValuePair<string, Queue<DateTime>> pair in this.failures)
        {
            if (pair.Value.Count == 0 || (now - pair.Value.Peek()).TotalSeconds > 300)
            {
                staleKeys.Add(pair.Key);
            }
        }
        foreach (string key in staleKeys)
        {
            this.failures.Remove(key);
            this.lockedUntil.Remove(key);
        }
    }
}

/// <summary>
/// 网页端审计日志：记录所有写操作与认证失败，包含时间、来源、账号、动作、目标、结果状态。
/// 复用统一日志文件（ui.log），分类为“网页端审计”，并对密码、令牌等敏感字段脱敏。
/// </summary>
internal static class WebAuditLog
{
    /// <summary>记录一次已完成（或已拒绝）的写请求。</summary>
    public static void Record(int port, HttpRequest request)
    {
        string action = request.GetQuery("a");
        if (string.IsNullOrEmpty(action))
        {
            action = request.Path;
        }
        string target = request.AuditTarget;
        if (string.IsNullOrEmpty(target))
        {
            target = request.GetQuery("p");
        }
        if (target.Length > 300)
        {
            target = target.Substring(0, 300) + "…";
        }
        string result = request.ResponsePreview;
        if (string.IsNullOrEmpty(result))
        {
            result = request.ResponseStatus > 0 ? "无响应正文" : "未写出响应";
        }
        string query = request.Query;
        if (query.Length > 300)
        {
            query = query.Substring(0, 300) + "…";
        }
        string line = "端口=" + port
            + " 来源=" + request.RemoteAddress
            + " 账号=" + WebSecurity.DescribeAccount(request.Account)
            + " 方法=" + request.Method
            + " 请求路径=" + request.Path
            + " 查询参数=" + WebSecurity.MaskSecrets(query)
            + " 动作=" + WebSecurity.MaskSecrets(action)
            + " 目标=" + WebSecurity.MaskSecrets(target)
            + " 结果状态=" + request.ResponseStatus
            + " 结果说明=" + WebSecurity.MaskSecrets(result)
            + " 请求编号=" + request.RequestId;
        UiLogger.Log("网页端审计", line);
    }

    /// <summary>记录一次认证失败或安全校验拒绝，用于追踪暴力尝试来源。</summary>
    public static void RecordDenied(int port, HttpRequest request, string account, string reason)
    {
        string query = request.Query;
        if (query.Length > 300)
        {
            query = query.Substring(0, 300) + "…";
        }
        string line = "端口=" + port
            + " 来源=" + request.RemoteAddress
            + " 账号=" + WebSecurity.DescribeAccount(account)
            + " 方法=" + request.Method
            + " 请求路径=" + request.Path
            + " 查询参数=" + WebSecurity.MaskSecrets(query)
            + " 拒绝原因=" + WebSecurity.MaskSecrets(reason)
            + " 请求编号=" + request.RequestId;
        UiLogger.Log("网页端审计", line);
    }
}
