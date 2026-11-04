using System;
using TabTaskExplorer.App.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>日志与诊断包脱敏规则测试：密码、密钥、令牌、URL 凭据与私钥统一替换，指纹保留。</summary>
public class LogSanitizerTests
{
    [Fact]
    public void MaskSecrets_ShouldMaskEnglishPasswordAndToken()
    {
        string masked = LogSanitizer.MaskSecrets("password=abc123 token=xyz789");
        Assert.DoesNotContain("abc123", masked);
        Assert.DoesNotContain("xyz789", masked);
        Assert.Contains("password=***", masked);
        Assert.Contains("token=***", masked);
    }

    [Fact]
    public void MaskSecrets_ShouldMaskChineseSecretFields()
    {
        string masked = LogSanitizer.MaskSecrets("密码=abc123 密钥=key456 令牌=token789 凭证=cred001");
        Assert.DoesNotContain("abc123", masked);
        Assert.DoesNotContain("key456", masked);
        Assert.DoesNotContain("token789", masked);
        Assert.DoesNotContain("cred001", masked);
        Assert.Contains("密码=***", masked);
        Assert.Contains("密钥=***", masked);
        Assert.Contains("令牌=***", masked);
        Assert.Contains("凭证=***", masked);
    }

    [Fact]
    public void MaskSecrets_ShouldMaskAuthorizationHeader()
    {
        string masked = LogSanitizer.MaskSecrets("Authorization: Bearer abc.def-123");
        Assert.DoesNotContain("abc.def-123", masked);
        Assert.Contains("Authorization: ***", masked);
    }

    [Fact]
    public void MaskSecrets_ShouldMaskBearerTokenInText()
    {
        string masked = LogSanitizer.MaskSecrets("请求头携带 Bearer sk-1234567890");
        Assert.DoesNotContain("sk-1234567890", masked);
        Assert.Contains("Bearer ***", masked);
    }

    [Fact]
    public void MaskSecrets_ShouldMaskUrlCredentials()
    {
        string masked = LogSanitizer.MaskSecrets("连接地址=https://alice:secret@example.com/path");
        Assert.DoesNotContain("alice", masked);
        Assert.DoesNotContain("secret", masked);
        Assert.Contains("https://***@example.com/path", masked);
    }

    [Fact]
    public void MaskSecrets_ShouldMaskPrivateKeyBlock()
    {
        string text = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----";
        string masked = LogSanitizer.MaskSecrets(text);
        Assert.DoesNotContain("MIIEowIBAAKCAQEA", masked);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void MaskSecrets_ShouldMaskJwt()
    {
        string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.c2lnbmF0dXJlMTIzNDU2Nzg5MA";
        string masked = LogSanitizer.MaskSecrets("会话=" + jwt);
        Assert.DoesNotContain(jwt, masked);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void MaskSecrets_ShouldKeepHostKeyFingerprint()
    {
        string fingerprint = "SHA256:abcdefghijklmnop1234567890";
        string masked = LogSanitizer.MaskSecrets("主机密钥指纹=" + fingerprint);
        Assert.Contains(fingerprint, masked);
    }

    [Fact]
    public void MaskSecrets_ShouldKeepOrdinaryTextUnchanged()
    {
        string text = "普通日志：文件已保存 路径=C:\\Users\\test\\a.txt";
        Assert.Equal(text, LogSanitizer.MaskSecrets(text));
    }

    [Fact]
    public void MaskSecrets_ShouldReturnEmptyForNull()
    {
        Assert.Equal(string.Empty, LogSanitizer.MaskSecrets(null));
    }

    [Fact]
    public void IsSensitivePropertyName_ShouldRecognizeSecretFields()
    {
        Assert.True(LogSanitizer.IsSensitivePropertyName("PasswordHash"));
        Assert.True(LogSanitizer.IsSensitivePropertyName("PasswordSalt"));
        Assert.True(LogSanitizer.IsSensitivePropertyName("PrivateKeyPath"));
        Assert.True(LogSanitizer.IsSensitivePropertyName("AccessToken"));
        Assert.True(LogSanitizer.IsSensitivePropertyName("ApiKey"));
        Assert.True(LogSanitizer.IsSensitivePropertyName("Credential"));
    }

    [Fact]
    public void IsSensitivePropertyName_ShouldNotMaskOrdinaryFields()
    {
        Assert.False(LogSanitizer.IsSensitivePropertyName("KeyText"));
        Assert.False(LogSanitizer.IsSensitivePropertyName("Account"));
        Assert.False(LogSanitizer.IsSensitivePropertyName("FfmpegPath"));
        Assert.False(LogSanitizer.IsSensitivePropertyName("LogLevel"));
    }

    [Fact]
    public void WebSecurityMaskSecrets_ShouldUseCompletedRules()
    {
        string masked = WebSecurity.MaskSecrets("password=abc token=xyz Authorization: Bearer secret-token");
        Assert.DoesNotContain("abc", masked);
        Assert.DoesNotContain("xyz", masked);
        Assert.DoesNotContain("secret-token", masked);
    }

    [Fact]
    public void MaskSecrets_ShouldPreserveOtherQueryParameters()
    {
        string single = LogSanitizer.MaskSecrets("查询参数=token=CANARY-TOKEN-987654&note=ok&page=2");
        Assert.Equal("查询参数=token=***&note=ok&page=2", single);

        string multiple = LogSanitizer.MaskSecrets("password=abc123&token=xyz789&note=ok&secret=s3cr3t");
        Assert.Equal("password=***&token=***&note=ok&secret=***", multiple);
    }

    [Fact]
    public void MaskSecrets_ShouldNotMaskBooleanNumberOrEnumValues()
    {
        string text = "需要密码=True 允许修改=False 重试次数=3 缩放比例=1.5"
            + " 最低级别=Info 会话=ReadOnly token=12345 hotkey=Ctrl+A";
        Assert.Equal(text, LogSanitizer.MaskSecrets(text));
    }

    [Fact]
    public void MaskSecrets_ShouldMaskSensitiveKeysAtAnyNestingLevel()
    {
        string json = "{\"database\":{\"password\":\"p@ssw0rd\",\"secret\":\"s3cr3t\"},"
            + "\"api\":{\"access_token\":\"tok-abc-123\",\"note\":\"ok\"},"
            + "\"auth\":{\"authorization\":\"Bearer nested-token\"},"
            + "\"inner\":[{\"token\":\"abc123\"}]}";
        string masked = LogSanitizer.MaskSecrets(json);
        Assert.DoesNotContain("p@ssw0rd", masked);
        Assert.DoesNotContain("s3cr3t", masked);
        Assert.DoesNotContain("tok-abc-123", masked);
        Assert.DoesNotContain("nested-token", masked);
        Assert.DoesNotContain("abc123", masked);
        Assert.Contains("\"password\":\"***\"", masked);
        Assert.Contains("\"secret\":\"***\"", masked);
        Assert.Contains("\"access_token\":\"***\"", masked);
        Assert.Contains("\"authorization\":\"***\"", masked);
        Assert.Contains("\"token\":\"***\"", masked);
        Assert.Contains("\"note\":\"ok\"", masked);
    }
}
