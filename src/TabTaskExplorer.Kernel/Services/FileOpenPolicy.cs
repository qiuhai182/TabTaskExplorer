using System;
using System.Collections.Generic;
using System.IO;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>打开文件的优先级常量。</summary>
public static class FileOpenPriority
{
    /// <summary>优先用内置文本编辑器打开，不支持的类型才交给系统默认程序。</summary>
    public const string Builtin = "Builtin";

    /// <summary>优先用系统默认程序打开。</summary>
    public const string System = "System";
}

/// <summary>
/// 单个文件类型的打开方式规则：把某个扩展名指定为「优先内置」或「优先系统」。
/// 扩展名用 * 表示全部类型（作为兜底规则）。
/// </summary>
public class FileOpenRule
{
    /// <summary>文件扩展名（含点，例如 .cs；* 表示全部类型）。</summary>
    public string Extension { get; set; } = "*";

    /// <summary>该类型使用的打开优先级：Builtin（优先内置编辑器）或 System（优先系统默认程序）。</summary>
    public string Priority { get; set; } = FileOpenPriority.Builtin;

    /// <summary>设置界面绑定用：0 = 优先内置编辑器，1 = 优先系统默认程序。</summary>
    public int PriorityIndex
    {
        get { return this.Priority == FileOpenPriority.System ? 1 : 0; }
        set { this.Priority = value == 1 ? FileOpenPriority.System : FileOpenPriority.Builtin; }
    }
}

/// <summary>
/// 文件打开策略：解析某个文件应「优先内置编辑器」还是「优先系统默认程序」。
/// 规则优先级为：同类型精确规则 → 全部类型规则（*）→ 全局默认优先级。
/// 仅 IDE 模式使用；资源管理器模式始终使用系统默认程序。
/// </summary>
public static class FileOpenPolicy
{
    /// <summary>规整扩展名：空或 * 统一为 *；缺少前导点则补齐；统一小写。</summary>
    public static string NormalizeExtension(string? extension)
    {
        string trimmed = (extension ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed == "*")
        {
            return "*";
        }
        if (!trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = "." + trimmed;
        }
        return trimmed.ToLowerInvariant();
    }

    /// <summary>解析某文件应使用的打开优先级。</summary>
    public static string ResolvePriority(IList<FileOpenRule>? rules, string? defaultPriority, string fileName)
    {
        if (rules != null)
        {
            string extension = NormalizeExtension(Path.GetExtension(fileName));
            FileOpenRule? fallback = null;
            foreach (FileOpenRule rule in rules)
            {
                if (rule == null)
                {
                    continue;
                }
                string ruleExtension = NormalizeExtension(rule.Extension);
                if (ruleExtension == extension)
                {
                    return NormalizePriority(rule.Priority);
                }
                if (ruleExtension == "*" && fallback == null)
                {
                    fallback = rule;
                }
            }
            if (fallback != null)
            {
                return NormalizePriority(fallback.Priority);
            }
        }
        return NormalizePriority(defaultPriority);
    }

    /// <summary>判断是否存在显式指定该文件类型为「优先内置」的规则（视为用户声明该类型可按文本处理）。</summary>
    public static bool HasExplicitBuiltinRule(IList<FileOpenRule>? rules, string fileName)
    {
        if (rules == null)
        {
            return false;
        }
        string extension = NormalizeExtension(Path.GetExtension(fileName));
        foreach (FileOpenRule rule in rules)
        {
            if (rule == null)
            {
                continue;
            }
            if (NormalizeExtension(rule.Extension) == extension && NormalizePriority(rule.Priority) == FileOpenPriority.Builtin)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>把任意字符串规整为两个合法优先级之一。</summary>
    public static string NormalizePriority(string? priority)
    {
        return priority == FileOpenPriority.System ? FileOpenPriority.System : FileOpenPriority.Builtin;
    }
}
