using System;
using TabTaskExplorer.App.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>网页端安全基线测试：日志脱敏、会话签发与校验、请求限流、登录失败锁定。</summary>
public class WebSecurityTests
{
    [Fact]
    public void MaskSecrets_ShouldHidePasswordAndToken()
    {
        string masked = WebSecurity.MaskSecrets("password=abc123 token=xyz authorization=Bearer secret");
        Assert.DoesNotContain("abc123", masked);
        Assert.DoesNotContain("token=xyz", masked);
        Assert.DoesNotContain("Bearer secret", masked);
        Assert.Contains("password=***", masked);
    }

    [Fact]
    public void SessionStore_ShouldReuseSessionForSameAccountAndValidateProfile()
    {
        WebSessionStore store = new WebSessionStore();
        WebAccessProfile profile = new WebAccessProfile();
        profile.Name = "测试方案";
        WebSession first = store.GetOrCreateBasic(profile, "admin");
        WebSession second = store.GetOrCreateBasic(profile, "admin");
        Assert.Same(first, second);
        Assert.NotNull(store.Find(first.Token, profile));

        WebAccessProfile otherProfile = new WebAccessProfile();
        otherProfile.Name = "其他方案";
        Assert.Null(store.Find(first.Token, otherProfile));
    }

    [Fact]
    public void RateLimiter_ShouldRejectRequestsOverLimit()
    {
        WebRateLimiter limiter = new WebRateLimiter();
        int retryAfter;
        Assert.True(limiter.TryAcquire("来源一", 2, 60, out retryAfter));
        Assert.True(limiter.TryAcquire("来源一", 2, 60, out retryAfter));
        Assert.False(limiter.TryAcquire("来源一", 2, 60, out retryAfter));
        Assert.True(retryAfter > 0);
    }

    [Fact]
    public void LoginFailureLimiter_ShouldLockAfterRepeatedFailuresAndClearOnSuccess()
    {
        WebLoginFailureLimiter limiter = new WebLoginFailureLimiter();
        int retryAfter;
        for (int index = 0; index < WebSecurity.LoginFailuresPerWindow; index++)
        {
            limiter.RecordFailure("来源一\n方案一");
        }
        Assert.True(limiter.IsLocked("来源一\n方案一", out retryAfter));
        Assert.True(retryAfter > 0);
        limiter.RecordSuccess("来源一\n方案一");
        Assert.False(limiter.IsLocked("来源一\n方案一", out retryAfter));
    }

    [Fact]
    public void StaticAssets_ShouldBeEmbeddedAndVersioned()
    {
        Assert.True(WebStaticAssets.IsAvailable);
        Assert.NotEqual("legacy", WebStaticAssets.Version);
    }

    [Fact]
    public void FixedTimeEquals_ShouldCompareExactText()
    {
        Assert.True(WebSecurity.FixedTimeEquals("abc", "abc"));
        Assert.False(WebSecurity.FixedTimeEquals("abc", "abd"));
        Assert.False(WebSecurity.FixedTimeEquals(null, "abc"));
        Assert.False(WebSecurity.FixedTimeEquals("abc", null));
    }
}
