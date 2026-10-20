using System.Collections.Generic;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>文件打开策略解析测试：精确类型规则 → 全部类型规则 → 全局默认优先级。</summary>
public class FileOpenPolicyTests
{
    /// <summary>无规则命中时使用全局默认优先级。</summary>
    [Fact]
    public void ResolvePriority_NoRules_UsesDefaultPriority()
    {
        List<FileOpenRule> rules = new List<FileOpenRule>();

        string builtin = FileOpenPolicy.ResolvePriority(rules, FileOpenPriority.Builtin, "readme.txt");
        string system = FileOpenPolicy.ResolvePriority(rules, FileOpenPriority.System, "readme.txt");

        Assert.Equal(FileOpenPriority.Builtin, builtin);
        Assert.Equal(FileOpenPriority.System, system);
    }

    /// <summary>精确类型规则优先于全局默认优先级。</summary>
    [Fact]
    public void ResolvePriority_ExactRuleOverridesDefault()
    {
        List<FileOpenRule> rules = new List<FileOpenRule>();
        rules.Add(Create(".cs", FileOpenPriority.System));

        Assert.Equal(FileOpenPriority.System, FileOpenPolicy.ResolvePriority(rules, FileOpenPriority.Builtin, "Program.cs"));
        // 其它类型仍用全局默认
        Assert.Equal(FileOpenPriority.Builtin, FileOpenPolicy.ResolvePriority(rules, FileOpenPriority.Builtin, "notes.txt"));
    }

    /// <summary>类型规则匹配忽略大小写，且用户填写不带前导点的扩展名同样生效。</summary>
    [Fact]
    public void ResolvePriority_RuleIsCaseInsensitiveAndToleratesMissingDot()
    {
        List<FileOpenRule> rules = new List<FileOpenRule>();
        rules.Add(Create("log", FileOpenPriority.Builtin));

        Assert.Equal(FileOpenPriority.Builtin, FileOpenPolicy.ResolvePriority(rules, FileOpenPriority.System, "server.LOG"));
    }

    /// <summary>全部类型规则（*）在没有精确规则命中时生效，且精确规则优先。</summary>
    [Fact]
    public void ResolvePriority_WildcardRuleAppliesWhenNoExactMatch()
    {
        List<FileOpenRule> rules = new List<FileOpenRule>();
        rules.Add(Create("*", FileOpenPriority.System));
        rules.Add(Create(".md", FileOpenPriority.Builtin));

        Assert.Equal(FileOpenPriority.Builtin, FileOpenPolicy.ResolvePriority(rules, FileOpenPriority.Builtin, "readme.md"));
        Assert.Equal(FileOpenPriority.System, FileOpenPolicy.ResolvePriority(rules, FileOpenPriority.Builtin, "photo.png"));
    }

    /// <summary>显式声明某类型为优先内置时，即使不在内置文本扩展名白名单内也算可按文本处理。</summary>
    [Fact]
    public void HasExplicitBuiltinRule_DetectsUserDeclaredType()
    {
        List<FileOpenRule> rules = new List<FileOpenRule>();
        rules.Add(Create(".log", FileOpenPriority.Builtin));
        rules.Add(Create(".cs", FileOpenPriority.System));

        Assert.True(FileOpenPolicy.HasExplicitBuiltinRule(rules, "server.log"));
        Assert.False(FileOpenPolicy.HasExplicitBuiltinRule(rules, "Program.cs"));
        Assert.False(FileOpenPolicy.HasExplicitBuiltinRule(rules, "image.png"));
        Assert.False(FileOpenPolicy.HasExplicitBuiltinRule(null, "server.log"));
    }

    /// <summary>扩展名规整：空与 * 归一为空对应 *，缺少前导点补齐，大小写统一。</summary>
    [Fact]
    public void NormalizeExtension_NormalizesUserInput()
    {
        Assert.Equal("*", FileOpenPolicy.NormalizeExtension(null));
        Assert.Equal("*", FileOpenPolicy.NormalizeExtension("  "));
        Assert.Equal("*", FileOpenPolicy.NormalizeExtension("*"));
        Assert.Equal(".txt", FileOpenPolicy.NormalizeExtension("TXT"));
        Assert.Equal(".tar.gz", FileOpenPolicy.NormalizeExtension(".TAR.GZ"));
    }

    /// <summary>未知优先级字符串一律回落为「优先内置编辑器」。</summary>
    [Fact]
    public void NormalizePriority_UnknownValueFallsBackToBuiltin()
    {
        Assert.Equal(FileOpenPriority.Builtin, FileOpenPolicy.NormalizePriority(null));
        Assert.Equal(FileOpenPriority.Builtin, FileOpenPolicy.NormalizePriority("Whatever"));
        Assert.Equal(FileOpenPriority.System, FileOpenPolicy.NormalizePriority(FileOpenPriority.System));
    }

    private static FileOpenRule Create(string extension, string priority)
    {
        FileOpenRule rule = new FileOpenRule();
        rule.Extension = extension;
        rule.Priority = priority;
        return rule;
    }
}
