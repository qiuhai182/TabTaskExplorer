using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 应用内右键菜单构建器（Option B）：把系统外壳枚举出的条目渲染成 Avalonia MenuItem，
/// 保留勾选 / 单选、默认粗体、快捷键、图标与管理员盾牌，并递归生成多级子菜单。
/// 主题由 App.axaml 的 ContextMenu / MenuItem 样式统一控制，不再依赖系统原生菜单。
/// </summary>
public static class SystemMenuUiBuilder
{
    /// <summary>把系统菜单项（含子菜单与分隔线）递归加入目标菜单集合。</summary>
    /// <param name="target">目标菜单项集合。</param>
    /// <param name="entries">系统外壳枚举结果。</param>
    /// <param name="clickHandler">叶子菜单项的点击处理器（通过 DataContext 取回条目）。</param>
    public static void Append(ItemCollection target, IReadOnlyList<SystemContextMenuEntry> entries,
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> clickHandler)
    {
        foreach (SystemContextMenuEntry entry in entries)
        {
            if (entry.IsSeparator)
            {
                target.Add(new Separator());
                continue;
            }
            MenuItem item = new MenuItem();
            item.Header = BuildHeader(entry);
            item.IsEnabled = entry.CanExecute;
            if (entry.IsDefault)
            {
                item.FontWeight = FontWeight.Bold;
            }
            if (entry.IsChecked || entry.IsRadioCheck)
            {
                item.ToggleType = entry.IsRadioCheck ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox;
                item.IsChecked = entry.IsChecked;
            }
            KeyGesture? gesture = TryParseGesture(entry.AcceleratorText);
            if (gesture != null)
            {
                item.InputGesture = gesture;
            }
            Bitmap? icon = TryCreateIcon(entry.IconDataUri);
            if (icon != null)
            {
                item.Icon = new Image { Source = icon, Width = 16.0, Height = 16.0 };
            }
            ToolTip.SetTip(item, entry.RegistryPath);
            if (entry.Children.Count > 0)
            {
                Append(item.Items, entry.Children, clickHandler);
            }
            else
            {
                item.DataContext = entry;
                item.Click += clickHandler;
            }
            target.Add(item);
        }
    }

    /// <summary>构造标题：带管理员盾牌的条目在名称前加盾牌标识。</summary>
    private static object BuildHeader(SystemContextMenuEntry entry)
    {
        if (!entry.RequiresElevation)
        {
            return entry.DisplayName;
        }
        StackPanel panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4.0 };
        panel.Children.Add(new TextBlock { Text = "🛡", FontSize = 12.0, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = entry.DisplayName, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        return panel;
    }

    /// <summary>把 BMP data URI 解码成位图；格式不支持或数据为空时返回 null，菜单仍按文字显示。</summary>
    private static Bitmap? TryCreateIcon(string dataUri)
    {
        if (string.IsNullOrEmpty(dataUri))
        {
            return null;
        }
        const string Prefix = "data:image/bmp;base64,";
        if (!dataUri.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            byte[] bytes = Convert.FromBase64String(dataUri.Substring(Prefix.Length));
            MemoryStream stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>把系统菜单里的快捷键文本（如 Ctrl+C、Delete、F2）解析成 KeyGesture；无法解析时返回 null。</summary>
    private static KeyGesture? TryParseGesture(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }
        Avalonia.Input.KeyModifiers modifiers = Avalonia.Input.KeyModifiers.None;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            string part = parts[index].Trim().ToLowerInvariant();
            if (part == "ctrl" || part == "control")
            {
                modifiers |= Avalonia.Input.KeyModifiers.Control;
            }
            else if (part == "shift")
            {
                modifiers |= Avalonia.Input.KeyModifiers.Shift;
            }
            else if (part == "alt")
            {
                modifiers |= Avalonia.Input.KeyModifiers.Alt;
            }
            else if (part == "win" || part == "windows")
            {
                modifiers |= Avalonia.Input.KeyModifiers.Meta;
            }
            else
            {
                return null;
            }
        }
        Avalonia.Input.Key? key = TryParseKey(parts[parts.Length - 1].Trim());
        if (key == null)
        {
            return null;
        }
        return new KeyGesture(key.Value, modifiers);
    }

    /// <summary>把单个键名解析成 Avalonia Key；支持字母、数字、F1-F24 与常用控制键。</summary>
    private static Avalonia.Input.Key? TryParseKey(string text)
    {
        if (text.Length == 1)
        {
            char character = char.ToUpperInvariant(text[0]);
            if (character >= 'A' && character <= 'Z')
            {
                return Enum.Parse<Avalonia.Input.Key>(character.ToString());
            }
            if (character >= '0' && character <= '9')
            {
                return Enum.Parse<Avalonia.Input.Key>("D" + character);
            }
        }
        string normalized = text.ToUpperInvariant();
        if (normalized == "DEL" || normalized == "DELETE")
        {
            return Avalonia.Input.Key.Delete;
        }
        if (normalized == "INS" || normalized == "INSERT")
        {
            return Avalonia.Input.Key.Insert;
        }
        if (normalized == "ESC" || normalized == "ESCAPE")
        {
            return Avalonia.Input.Key.Escape;
        }
        if (normalized == "BACKSPACE" || normalized == "BACK")
        {
            return Avalonia.Input.Key.Back;
        }
        if (normalized == "RETURN")
        {
            return Avalonia.Input.Key.Enter;
        }
        if (normalized == "PGUP" || normalized == "PAGEUP")
        {
            return Avalonia.Input.Key.PageUp;
        }
        if (normalized == "PGDN" || normalized == "PAGEDOWN")
        {
            return Avalonia.Input.Key.PageDown;
        }
        if (normalized == "SPACEBAR")
        {
            return Avalonia.Input.Key.Space;
        }
        if (Enum.TryParse<Avalonia.Input.Key>(normalized, true, out Avalonia.Input.Key parsed))
        {
            return parsed;
        }
        return null;
    }
}
