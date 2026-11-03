using System;
using Avalonia.Controls;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 全局右键菜单单例：同一时刻只保留一个已打开的菜单。
/// 打开新菜单前先关闭并释放旧菜单，避免连续右键叠加多层；关闭时记录打开数量，便于诊断残留窗口。
/// </summary>
public static class ContextMenuSingleton
{
    /// <summary>当前打开的菜单（弱引用，菜单被控件回收时不持有强引用）。</summary>
    private static WeakReference<ContextMenu>? currentMenu;

    /// <summary>当前是否有菜单处于打开状态。</summary>
    public static bool IsOpen
    {
        get
        {
            ContextMenu? menu = GetCurrentMenu();
            return menu != null && menu.IsOpen;
        }
    }

    /// <summary>取当前打开的菜单；已经被回收或已关闭时返回 null。</summary>
    public static ContextMenu? GetCurrentMenu()
    {
        WeakReference<ContextMenu>? reference = currentMenu;
        if (reference == null)
        {
            return null;
        }
        ContextMenu? menu = null;
        if (!reference.TryGetTarget(out menu))
        {
            return null;
        }
        return menu;
    }

    /// <summary>
    /// 注册即将打开的菜单：若已有别的菜单打开，先强制关闭它（不是隐藏），再登记新菜单。
    /// 由每个右键菜单的 Opening 事件调用。
    /// </summary>
    /// <param name="menu">即将打开的菜单。</param>
    public static void PrepareOpen(ContextMenu menu)
    {
        ContextMenu? previous = GetCurrentMenu();
        bool previousOpen = previous != null && previous.IsOpen;
        if (previous != null && previous != menu && previousOpen)
        {
            UiLogger.Log("程序行为", "右键菜单单例：检测到旧菜单仍打开，先关闭旧菜单"
                + " 旧菜单类型=" + previous.GetType().Name + " 结果=已关闭，随后只登记本次新菜单");
            previous.Close();
        }
        if (previous != null && previous != menu)
        {
            // 旧菜单已经关闭或已被回收：直接丢弃引用即可
            UiLogger.Log("程序行为", "右键菜单单例：旧菜单未处于打开状态，丢弃旧引用 打开菜单数=0");
        }
        if (menu.IsOpen)
        {
            // 同一实例被重复打开：先关再开，保证重新定位而不是叠加
            menu.Close();
        }
        currentMenu = new WeakReference<ContextMenu>(menu);
        UiLogger.Log("程序行为", "右键菜单单例：登记新菜单 菜单类型=" + menu.GetType().Name + " 当前打开菜单数=1");
    }

    /// <summary>关闭当前菜单并清空登记；无菜单打开时只清引用。</summary>
    public static void CloseCurrent()
    {
        ContextMenu? menu = GetCurrentMenu();
        currentMenu = null;
        if (menu == null)
        {
            return;
        }
        bool wasOpen = menu.IsOpen;
        if (wasOpen)
        {
            menu.Close();
        }
        UiLogger.Log("程序行为", "右键菜单单例：关闭当前菜单 菜单类型=" + menu.GetType().Name
            + " 关闭前是否打开=" + wasOpen + " 结果=已关闭并清空登记，当前打开菜单数=0");
    }

    /// <summary>当前打开菜单数量（诊断用：0 表示没有残留菜单）。</summary>
    public static int OpenCount
    {
        get { return IsOpen ? 1 : 0; }
    }
}
</content>
