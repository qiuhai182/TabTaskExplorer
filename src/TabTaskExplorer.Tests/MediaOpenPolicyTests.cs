using System.Collections.Generic;
using TabTaskExplorer.Kernel.Media;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>媒体打开方式策略测试：同大类规则 → 全部媒体规则 → 全局默认。</summary>
public class MediaOpenPolicyTests
{
    /// <summary>没有规则时使用全局默认方式。</summary>
    [Fact]
    public void ResolveMode_NoRules_UsesDefault()
    {
        List<MediaOpenRule> rules = new List<MediaOpenRule>();

        Assert.Equal(MediaOpenModeValues.Builtin,
            MediaOpenPolicy.ResolveMode(rules, MediaOpenModeValues.Builtin, MediaKind.Image));
        Assert.Equal(MediaOpenModeValues.System,
            MediaOpenPolicy.ResolveMode(rules, MediaOpenModeValues.System, MediaKind.Video));
        Assert.Equal(MediaOpenModeValues.Ask,
            MediaOpenPolicy.ResolveMode(rules, MediaOpenModeValues.Ask, MediaKind.Audio));
    }

    /// <summary>精确大类规则优先于全局默认。</summary>
    [Fact]
    public void ResolveMode_ExactRuleOverridesDefault()
    {
        List<MediaOpenRule> rules = new List<MediaOpenRule>();
        MediaOpenRule rule = new MediaOpenRule();
        rule.KindKey = "Video";
        rule.Mode = MediaOpenModeValues.System;
        rules.Add(rule);

        Assert.Equal(MediaOpenModeValues.System,
            MediaOpenPolicy.ResolveMode(rules, MediaOpenModeValues.Builtin, MediaKind.Video));
        Assert.Equal(MediaOpenModeValues.Builtin,
            MediaOpenPolicy.ResolveMode(rules, MediaOpenModeValues.Builtin, MediaKind.Image));
    }

    /// <summary>全部媒体规则（*）作为兜底生效。</summary>
    [Fact]
    public void ResolveMode_WildcardRuleActsAsFallback()
    {
        List<MediaOpenRule> rules = new List<MediaOpenRule>();
        MediaOpenRule rule = new MediaOpenRule();
        rule.KindKey = "*";
        rule.Mode = MediaOpenModeValues.Ask;
        rules.Add(rule);

        Assert.Equal(MediaOpenModeValues.Ask,
            MediaOpenPolicy.ResolveMode(rules, MediaOpenModeValues.Builtin, MediaKind.Audio));
    }

    /// <summary>非法方式回落为内置，非法大类键回落为通配。</summary>
    [Fact]
    public void Normalize_InvalidValuesFallBack()
    {
        Assert.Equal(MediaOpenModeValues.Builtin, MediaOpenPolicy.NormalizeMode("nonsense"));
        Assert.Equal(MediaOpenModeValues.Builtin, MediaOpenPolicy.NormalizeMode(null));
        Assert.Equal("*", MediaOpenPolicy.NormalizeKindKey("nonsense"));
        Assert.Equal("Image", MediaOpenPolicy.NormalizeKindKey("image"));
    }

    /// <summary>只有「内置」才返回 true，系统与询问都返回 false。</summary>
    [Fact]
    public void ShouldUseBuiltin_OnlyTrueForBuiltinMode()
    {
        List<MediaOpenRule> rules = new List<MediaOpenRule>();

        Assert.True(MediaOpenPolicy.ShouldUseBuiltin(rules, MediaOpenModeValues.Builtin, MediaKind.Image));
        Assert.False(MediaOpenPolicy.ShouldUseBuiltin(rules, MediaOpenModeValues.System, MediaKind.Image));
        Assert.False(MediaOpenPolicy.ShouldUseBuiltin(rules, MediaOpenModeValues.Ask, MediaKind.Image));
    }

    /// <summary>设置界面的下标与方式字符串双向一致。</summary>
    [Fact]
    public void Rule_ModeIndexRoundTrips()
    {
        MediaOpenRule rule = new MediaOpenRule();
        rule.ModeIndex = 2;
        Assert.Equal(MediaOpenModeValues.Ask, rule.Mode);
        rule.ModeIndex = 1;
        Assert.Equal(MediaOpenModeValues.System, rule.Mode);
        rule.ModeIndex = 0;
        Assert.Equal(MediaOpenModeValues.Builtin, rule.Mode);
    }
}
