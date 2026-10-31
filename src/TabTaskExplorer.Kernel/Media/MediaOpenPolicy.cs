using System;
using System.Collections.Generic;

namespace TabTaskExplorer.Kernel.Media;

/// <summary>媒体打开方式常量。</summary>
public static class MediaOpenModeValues
{
    /// <summary>用内置媒体标签打开（默认）。</summary>
    public const string Builtin = "Builtin";

    /// <summary>交给系统默认程序打开。</summary>
    public const string System = "System";

    /// <summary>每次双击都询问。</summary>
    public const string Ask = "Ask";
}

/// <summary>
/// 单个媒体大类的打开方式规则：把某一类（图片 / 视频 / 音频）指定为内置、系统或询问。
/// KindKey 取 Image / Video / Audio，* 表示全部媒体（兜底规则）。
/// </summary>
public class MediaOpenRule
{
    /// <summary>媒体大类键：Image / Video / Audio / *（全部）。</summary>
    public string KindKey { get; set; } = "*";

    /// <summary>该大类使用的打开方式：Builtin / System / Ask。</summary>
    public string Mode { get; set; } = MediaOpenModeValues.Builtin;

    /// <summary>设置界面绑定用：0 = 内置查看器，1 = 系统默认程序，2 = 每次询问。</summary>
    public int ModeIndex
    {
        get
        {
            if (this.Mode == MediaOpenModeValues.System)
            {
                return 1;
            }
            if (this.Mode == MediaOpenModeValues.Ask)
            {
                return 2;
            }
            return 0;
        }
        set
        {
            if (value == 1)
            {
                this.Mode = MediaOpenModeValues.System;
            }
            else if (value == 2)
            {
                this.Mode = MediaOpenModeValues.Ask;
            }
            else
            {
                this.Mode = MediaOpenModeValues.Builtin;
            }
        }
    }
}

/// <summary>
/// 媒体打开策略：解析某个媒体文件应「内置标签打开」「系统默认程序打开」还是「每次询问」。
/// 规则优先级：同大类精确规则 → 全部媒体规则（*）→ 全局默认方式。
/// </summary>
public static class MediaOpenPolicy
{
    /// <summary>把媒体大类转成规则键（Image / Video / Audio / *）。</summary>
    public static string ToKindKey(MediaKind kind)
    {
        if (kind == MediaKind.Image)
        {
            return "Image";
        }
        if (kind == MediaKind.Video)
        {
            return "Video";
        }
        if (kind == MediaKind.Audio)
        {
            return "Audio";
        }
        return "*";
    }

    /// <summary>把规则键规整为合法取值（非法值一律视为 *）。</summary>
    public static string NormalizeKindKey(string? kindKey)
    {
        string trimmed = (kindKey ?? string.Empty).Trim();
        if (string.Equals(trimmed, "Image", StringComparison.OrdinalIgnoreCase))
        {
            return "Image";
        }
        if (string.Equals(trimmed, "Video", StringComparison.OrdinalIgnoreCase))
        {
            return "Video";
        }
        if (string.Equals(trimmed, "Audio", StringComparison.OrdinalIgnoreCase))
        {
            return "Audio";
        }
        return "*";
    }

    /// <summary>把任意字符串规整为三种合法打开方式之一（非法值回落为内置）。</summary>
    public static string NormalizeMode(string? mode)
    {
        if (mode == MediaOpenModeValues.System)
        {
            return MediaOpenModeValues.System;
        }
        if (mode == MediaOpenModeValues.Ask)
        {
            return MediaOpenModeValues.Ask;
        }
        return MediaOpenModeValues.Builtin;
    }

    /// <summary>解析某媒体大类应使用的打开方式。</summary>
    public static string ResolveMode(IList<MediaOpenRule>? rules, string? defaultMode, MediaKind kind)
    {
        if (rules != null)
        {
            string kindKey = ToKindKey(kind);
            MediaOpenRule? fallback = null;
            for (int index = 0; index < rules.Count; index++)
            {
                MediaOpenRule? rule = rules[index];
                if (rule == null)
                {
                    continue;
                }
                string ruleKey = NormalizeKindKey(rule.KindKey);
                if (ruleKey == kindKey)
                {
                    return NormalizeMode(rule.Mode);
                }
                if (ruleKey == "*" && fallback == null)
                {
                    fallback = rule;
                }
            }
            if (fallback != null)
            {
                return NormalizeMode(fallback.Mode);
            }
        }
        return NormalizeMode(defaultMode);
    }

    /// <summary>是否应直接用内置媒体标签打开（Ask 与 System 都返回 false，由调用方另行处理询问）。</summary>
    public static bool ShouldUseBuiltin(IList<MediaOpenRule>? rules, string? defaultMode, MediaKind kind)
    {
        return ResolveMode(rules, defaultMode, kind) == MediaOpenModeValues.Builtin;
    }
}
