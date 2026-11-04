using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Views;

/// <summary>磁盘视图面板：展示目录占用统计，单击文件夹原地展开或收起（不进入下一级）。</summary>
public partial class DiskViewPanel : UserControl
{
    /// <summary>当前系统菜单子项的目标路径（菜单打开时记录，点击子项时使用）。</summary>
    private string systemMenuTargetPath = string.Empty;

    /// <summary>当前系统菜单子项是否为空白处菜单。</summary>
    private bool systemMenuBackground;

    public DiskViewPanel()
    {
        this.InitializeComponent();
        // 拖拽源与落点：磁盘视图内只做移动或复制，不触发任何删除源文件的额外行为
        this.AddHandler(InputElement.PointerPressedEvent, this.OnPointerPressedForDrag, RoutingStrategies.Bubble, handledEventsToo: true);
        this.AddHandler(InputElement.PointerMovedEvent, this.OnPointerMovedForDrag, RoutingStrategies.Bubble, handledEventsToo: true);
        this.AddHandler(InputElement.PointerReleasedEvent, this.OnPointerReleasedForDrag, RoutingStrategies.Bubble, handledEventsToo: true);
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, this.OnDiskDragOver);
        DragDrop.AddDragLeaveHandler(this, this.OnDiskDragLeave);
        DragDrop.AddDropHandler(this, this.OnDiskDrop);
    }

    /// <summary>拖拽起点行。</summary>
    private DiskRow? dragRow;

    /// <summary>按下事件（发起系统拖拽时复用）。</summary>
    private PointerPressedEventArgs? dragPressEvent;

    /// <summary>按下位置（用于 6 像素阈值判定）。</summary>
    private Avalonia.Point dragPressPoint;

    /// <summary>本次按下是否已经发起拖拽。</summary>
    private bool dragStarted;

    /// <summary>拖拽结束后的点击抑制截止时间（防止拖放被误判为展开或收起）。</summary>
    private long suppressTapUntil;

    /// <summary>主窗口视图模型（用于新标签打开、复制路径到剪贴板）。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get { return TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel; }
    }

    /// <summary>磁盘视图自身的视图模型（编译兼容：由 DataContext 提供）。</summary>
    private DiskViewPanelViewModel? ViewModel
    {
        get { return this.DataContext as DiskViewPanelViewModel; }
    }

    /// <summary>右键菜单"打开"：文件夹进入该路径，文件用系统默认程序打开。</summary>
    private void OnMenuOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is DiskRow row)
        {
            if (this.MainViewModel == null)
            {
                UiLogger.Log("程序行为", "右键菜单打开：未找到主窗口视图模型 结果=失败，未打开该条目");
                return;
            }
            this.OpenRowPath(row.FullPath, row.IsDirectory, false);
            UiLogger.Log("程序行为", "右键菜单打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已进入该目录" : "已调用系统默认程序打开该文件"));
        }
    }

    /// <summary>右键菜单"在新标签打开"：在新标签页打开该路径（文件则用系统默认程序打开）。</summary>
    private void OnMenuOpenInNewTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is DiskRow row)
        {
            if (this.MainViewModel == null)
            {
                UiLogger.Log("程序行为", "右键菜单在新标签打开：未找到主窗口视图模型 结果=失败，未打开该条目");
                return;
            }
            this.OpenRowPath(row.FullPath, row.IsDirectory, true);
            UiLogger.Log("程序行为", "右键菜单在新标签打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已新建标签打开该目录" : "已调用系统默认程序打开该文件"));
        }
    }

    /// <summary>右键菜单"复制路径"：把该行路径复制到剪贴板。</summary>
    private void OnMenuCopyPathClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单复制路径：未找到主窗口视图模型 结果=失败，本次复制取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is DiskRow row)
        {
            mainViewModel.CopyPathsToClipboard(new List<string> { row.FullPath });
            UiLogger.Log("程序行为", "右键菜单复制路径：行路径=" + row.FullPath
                + " 结果=成功，已把该路径放入剪贴板，粘贴时生效");
        }
    }

    /// <summary>右键菜单"复制"：把该行放入剪贴板（复制模式）。</summary>
    private void OnMenuCopyClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null || sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单复制：未找到主窗口视图模型或行数据 结果=失败，本次复制取消");
            return;
        }
        if (row.Parent == null && string.Equals(row.EntryTypeText, "驱动器", StringComparison.Ordinal))
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单复制：行路径=" + row.FullPath + " 条目类型=驱动器 结果=失败，驱动器不能整体复制");
            return;
        }
        mainViewModel.CopyPathsToClipboard(new List<string> { row.FullPath });
        UiLogger.Log("程序行为", "磁盘视图右键菜单复制：行路径=" + row.FullPath + " 结果=成功，已放入剪贴板");
    }

    /// <summary>右键菜单"剪切"：把该行放入剪贴板（移动模式）。</summary>
    private void OnMenuCutClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null || sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单剪切：未找到主窗口视图模型或行数据 结果=失败，本次剪切取消");
            return;
        }
        if (row.Parent == null && string.Equals(row.EntryTypeText, "驱动器", StringComparison.Ordinal))
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单剪切：行路径=" + row.FullPath + " 条目类型=驱动器 结果=失败，驱动器不能整体剪切");
            return;
        }
        mainViewModel.CutPathsToClipboard(new List<string> { row.FullPath });
        UiLogger.Log("程序行为", "磁盘视图右键菜单剪切：行路径=" + row.FullPath + " 结果=成功，已放入剪贴板");
    }

    /// <summary>右键菜单"粘贴到该文件夹"：把剪贴板内容粘贴进该行文件夹。</summary>
    private async void OnMenuPasteToRowClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        if (mainViewModel == null || viewModel == null
            || sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴到该文件夹：缺少主窗口或行数据 结果=失败，本次粘贴取消");
            return;
        }
        if (!row.IsDirectory)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴到该文件夹：行路径=" + row.FullPath
                + " 条目类型=文件 结果=失败，文件不能作为粘贴目标");
            return;
        }
        await mainViewModel.PasteFromClipboardAsync(row.FullPath);
        viewModel.RefreshAfterChange(row.FullPath);
    }

    /// <summary>右键菜单"重命名"：输入新名称后就地更新该行（不重建列表）。</summary>
    private async void OnMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (owner == null || viewModel == null || sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单重命名：缺少所属窗口、视图模型或行数据 结果=失败，本次重命名取消");
            return;
        }
        string? newName = await InputDialog.Show(owner, "重命名", "请输入新的名称：", row.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == row.Name)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单重命名：行路径=" + row.FullPath
                + " 新名称=" + (string.IsNullOrWhiteSpace(newName) ? "未输入" : newName)
                + " 结果=未执行重命名，新名称为空或与原名称相同");
            return;
        }
        string parent = System.IO.Path.GetDirectoryName(row.FullPath) ?? string.Empty;
        string message = viewModel.Rename(row, newName);
        if (mainViewModel != null) { mainViewModel.RefreshTabsAtPath(parent, null); }
        UiLogger.Log("程序行为", "磁盘视图右键菜单重命名：原路径=" + row.FullPath + " 新名称=" + newName.Trim()
            + " 结果=" + message);
        if (message.StartsWith("已重命名")) { return; }
        if (owner != null) { MessageDialog.Show(owner, "重命名", message); }
    }

    /// <summary>右键菜单"删除到回收站"：删除该行并刷新所在目录。</summary>
    private void OnMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        this.DeleteRowFromMenu(sender, false);
    }

    /// <summary>右键菜单"永久删除…"：二次确认后永久删除，不进入回收站。</summary>
    private async void OnMenuDeletePermanentlyClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        if (owner == null || sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单永久删除：缺少所属窗口或行数据 结果=失败，本次删除取消");
            return;
        }
        string? confirmation = await InputDialog.Show(owner, "永久删除",
            "永久删除不进回收站且无法恢复。请输入「删除」两个字确认：", string.Empty);
        if (string.IsNullOrWhiteSpace(confirmation) || confirmation.Trim() != "删除")
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单永久删除：行路径=" + row.FullPath
                + " 确认文字=" + (string.IsNullOrWhiteSpace(confirmation) ? "未输入" : confirmation)
                + " 结果=用户未确认，本次删除取消");
            return;
        }
        this.DeleteRowFromMenu(sender, true);
    }

    /// <summary>删除菜单项的公共落地：移入回收站或永久删除，并刷新所在目录。</summary>
    private void DeleteRowFromMenu(object? sender, bool permanent)
    {
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        if (viewModel == null || sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单删除：缺少视图模型或行数据 结果=失败，本次删除取消");
            return;
        }
        string parent = System.IO.Path.GetDirectoryName(row.FullPath) ?? string.Empty;
        string message = viewModel.Delete(row, permanent);
        if (mainViewModel != null) { mainViewModel.RefreshTabsAtPath(parent, null); }
        UiLogger.Log("程序行为", "磁盘视图右键菜单删除：行路径=" + row.FullPath + " 方式=" + (permanent ? "永久删除" : "移入回收站")
            + " 结果=" + message);
        if (!message.StartsWith("已") && owner != null) { MessageDialog.Show(owner, "删除", message); }
    }

    /// <summary>右键菜单"在资源管理器打开"：文件夹打开该目录，文件在资源管理器中选中。</summary>
    private void OnMenuOpenInExplorerClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单在资源管理器打开：未关联行数据 结果=失败，本次打开取消");
            return;
        }
        try
        {
            if (row.IsDirectory)
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + row.FullPath + "\"", UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + row.FullPath + "\"", UseShellExecute = true });
            }
            UiLogger.Log("程序行为", "磁盘视图右键菜单在资源管理器打开：行路径=" + row.FullPath + " 结果=已调用资源管理器");
        }
        catch (Exception error)
        {
            UiLogger.LogError("程序行为", "磁盘视图右键菜单在资源管理器打开失败：行路径=" + row.FullPath, error);
        }
    }

    /// <summary>空白处右键菜单"刷新"：完全重扫当前根目录。</summary>
    private void OnBackgroundRefreshClick(object? sender, RoutedEventArgs e)
    {
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "磁盘视图空白处菜单刷新：未找到视图模型 结果=失败，本次刷新取消");
            return;
        }
        viewModel.StartFullRefresh();
        UiLogger.Log("程序行为", "磁盘视图空白处菜单刷新：根目录=" + (string.IsNullOrEmpty(viewModel.RootPathInput) ? "此电脑" : viewModel.RootPathInput)
            + " 结果=成功，已请求完全重扫");
    }

    /// <summary>空白处右键菜单"新建文件夹…"：在当前根目录下创建文件夹。</summary>
    private async void OnBackgroundNewFolderClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "磁盘视图空白处菜单新建文件夹：缺少所属窗口或视图模型 结果=失败，本次新建取消");
            return;
        }
        if (string.IsNullOrEmpty(viewModel.RootPathInput))
        {
            MessageDialog.Show(owner, "新建文件夹", "此电脑视图没有具体目录，请先打开某个磁盘或目录");
            return;
        }
        string? folderName = await InputDialog.Show(owner, "新建文件夹", "请输入文件夹名称：", "新建文件夹");
        if (string.IsNullOrWhiteSpace(folderName)) { return; }
        string message = viewModel.CreateFolder(viewModel.RootPathInput, folderName.Trim());
        this.MainViewModel?.RefreshTabsAtPath(viewModel.RootPathInput, null);
        UiLogger.Log("程序行为", "磁盘视图空白处菜单新建文件夹：目录=" + viewModel.RootPathInput
            + " 名称=" + folderName.Trim() + " 结果=" + message);
        if (!message.StartsWith("已") && owner != null) { MessageDialog.Show(owner, "新建文件夹", message); }
    }

    /// <summary>空白处右键菜单"新建文件…"：在当前根目录下创建空文件。</summary>
    private async void OnBackgroundNewFileClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "磁盘视图空白处菜单新建文件：缺少所属窗口或视图模型 结果=失败，本次新建取消");
            return;
        }
        if (string.IsNullOrEmpty(viewModel.RootPathInput))
        {
            MessageDialog.Show(owner, "新建文件", "此电脑视图没有具体目录，请先打开某个磁盘或目录");
            return;
        }
        string? fileName = await InputDialog.Show(owner, "新建文件", "请输入文件名（含扩展名）：", "新建文件.txt");
        if (string.IsNullOrWhiteSpace(fileName)) { return; }
        string message = viewModel.CreateFile(viewModel.RootPathInput, fileName.Trim());
        this.MainViewModel?.RefreshTabsAtPath(viewModel.RootPathInput, null);
        UiLogger.Log("程序行为", "磁盘视图空白处菜单新建文件：目录=" + viewModel.RootPathInput
            + " 名称=" + fileName.Trim() + " 结果=" + message);
        if (!message.StartsWith("已") && owner != null) { MessageDialog.Show(owner, "新建文件", message); }
    }

    /// <summary>空白处右键菜单"粘贴"：把剪贴板内容粘贴到当前根目录。</summary>
    private async void OnBackgroundPasteClick(object? sender, RoutedEventArgs e)
    {
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (viewModel == null || mainViewModel == null)
        {
            UiLogger.Log("程序行为", "磁盘视图空白处菜单粘贴：缺少视图模型 结果=失败，本次粘贴取消");
            return;
        }
        if (string.IsNullOrEmpty(viewModel.RootPathInput))
        {
            UiLogger.Log("程序行为", "磁盘视图空白处菜单粘贴：根目录=此电脑 结果=失败，没有可粘贴的目标目录");
            return;
        }
        await mainViewModel.PasteFromClipboardAsync(viewModel.RootPathInput);
        viewModel.RefreshAfterChange(viewModel.RootPathInput);
    }

    /// <summary>空白处右键菜单"返回上一级"：回到上级目录。</summary>
    private void OnBackgroundNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "磁盘视图空白处菜单返回上一级：未找到视图模型 结果=失败，本次返回取消");
            return;
        }
        viewModel.NavigateUp();
        UiLogger.Log("程序行为", "磁盘视图空白处菜单返回上一级：新根目录="
            + (string.IsNullOrEmpty(viewModel.RootPathInput) ? "此电脑" : viewModel.RootPathInput) + " 结果=成功");
    }

    /// <summary>条目右键菜单打开前：注入系统项，并按条目类型启用「粘贴到该文件夹」。</summary>
    private void OnSystemMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图条目右键菜单打开：菜单未关联磁盘行 结果=失败，未添加系统菜单项");
            return;
        }
        ContextMenuSingleton.PrepareOpen(menu);
        foreach (object item in menu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Tag is string tag && tag == "DiskSystemMenuHost")
            {
                menuItem.IsVisible = false;
                menuItem.Items.Clear();
            }
        }
        foreach (object item in menu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Header is string header
                && header == "粘贴到该文件夹")
            {
                menuItem.IsVisible = row.IsDirectory;
            }
        }
        this.AppendShellMenuItems(menu, row.FullPath, false);
    }

    /// <summary>空白处右键菜单打开前：注入当前根目录的系统项。</summary>
    private void OnBackgroundMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }
        ContextMenuSingleton.PrepareOpen(menu);
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        if (viewModel == null || string.IsNullOrEmpty(viewModel.RootPathInput))
        {
            foreach (object item in menu.Items)
            {
                if (item is MenuItem menuItem && menuItem.Tag is string tag && tag == "DiskSystemMenuHost")
                {
                    menuItem.IsVisible = false;
                    menuItem.Items.Clear();
                }
            }
            return;
        }
        this.AppendShellMenuItems(menu, viewModel.RootPathInput, true);
    }

    /// <summary>把系统外壳菜单项注入占位子菜单；远程或非 Windows 时保持隐藏。</summary>
    private void AppendShellMenuItems(ContextMenu menu, string targetPath, bool isBackground)
    {
        ISystemContextMenuService? service = this.MainViewModel?.SystemContextMenus;
        MenuItem? host = null;
        foreach (object item in menu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Tag is string tag && tag == "DiskSystemMenuHost")
            {
                host = menuItem;
                break;
            }
        }
        if (host == null)
        {
            return;
        }
        host.Items.Clear();
        this.systemMenuTargetPath = targetPath;
        this.systemMenuBackground = isBackground;
        if (service == null || !service.IsSupported || string.IsNullOrEmpty(targetPath))
        {
            host.IsVisible = false;
            return;
        }
        IReadOnlyList<SystemContextMenuEntry> entries;
        UiWatchdog.EnterStage("Shell 菜单枚举");
        try
        {
            entries = service.EnumerateShellMenu(targetPath, isBackground);
        }
        finally
        {
            UiWatchdog.LeaveStage("Shell 菜单枚举");
        }
        if (entries.Count == 0)
        {
            host.IsVisible = false;
            UiLogger.Log("磁盘视图", "系统右键菜单：目标=" + targetPath + " 结果=外壳未返回菜单项，已隐藏系统子菜单");
            return;
        }
        this.AppendShellEntries(host.Items, entries);
        host.IsVisible = true;
        UiLogger.Log("磁盘视图", "系统右键菜单：目标=" + targetPath + " 落点=" + (isBackground ? "空白处" : "条目")
            + " 顶层条目数=" + entries.Count + " 结果=成功，已加载系统项与第三方扩展");
    }

    /// <summary>递归把系统条目（含子菜单与分隔线）加入菜单集合；复用统一的菜单项构建器保留图标、勾选、快捷键与盾牌。</summary>
    private void AppendShellEntries(ItemCollection target, IReadOnlyList<SystemContextMenuEntry> entries)
    {
        SystemMenuUiBuilder.Append(target, entries, this.OnDiskSystemMenuEntryClick);
    }

    /// <summary>点击注入的系统菜单项：以当前菜单的目标路径唤起对应外壳命令。</summary>
    private void OnDiskSystemMenuEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not SystemContextMenuEntry entry)
        {
            return;
        }
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        ISystemContextMenuService? service = mainViewModel?.SystemContextMenus;
        if (service == null || string.IsNullOrEmpty(this.systemMenuTargetPath))
        {
            return;
        }
        bool background = this.systemMenuBackground;
        string target = this.systemMenuTargetPath;
        bool succeeded = service.ExecuteShellMenuEntry(entry, target, background);
        UiLogger.Log("磁盘视图", "唤起系统菜单项：条目名称=" + entry.DisplayName + " 目标=" + target
            + " 结果=" + (succeeded ? "成功，已交给外壳扩展执行" : "失败，未接受该命令"));
        if (succeeded)
        {
            string affectedDirectory = background ? target : (Path.GetDirectoryName(target) ?? string.Empty);
            DiskViewPanelViewModel? viewModel = this.ViewModel;
            if (viewModel != null && affectedDirectory.Length > 0)
            {
                viewModel.RefreshAfterChange(affectedDirectory);
            }
            this.MainViewModel?.RefreshTabsAtPath(affectedDirectory, null);
        }
        if (!succeeded)
        {
            Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
            if (owner != null) { MessageDialog.Show(owner, "系统菜单", "唤起系统菜单项失败：" + entry.DisplayName); }
        }
    }


    /// <summary>右键菜单"粘贴"：文件夹条目粘贴进该文件夹，文件条目粘贴进它所在的文件夹。</summary>
    private async void OnMenuPasteClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴：未找到主窗口视图模型 结果=失败，本次粘贴取消");
            return;
        }
        if (sender is not MenuItem menuItem || menuItem.DataContext is not DiskRow row)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴：菜单项未关联磁盘行数据 结果=失败，本次粘贴取消");
            return;
        }
        string targetDirectory = row.IsDirectory
            ? row.FullPath
            : (Path.GetDirectoryName(row.FullPath) ?? string.Empty);
        if (targetDirectory.Length == 0)
        {
            UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴：行路径=" + row.FullPath + " 目标目录=未取到 结果=失败，本次粘贴取消");
            return;
        }
        await mainViewModel.PasteFromClipboardAsync(targetDirectory);
        UiLogger.Log("程序行为", "磁盘视图右键菜单粘贴：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
            + " 目标目录=" + targetDirectory + " 结果=已请求把剪贴板条目粘贴到该目录（剪贴板为空或目录不可写时由界面提示）");
    }

    /// <summary>打开行路径的公共逻辑：目录进入文件夹视图（新标签或当前标签），文件用系统默认程序打开。</summary>
    private void OpenRowPath(string path, bool isDirectory, bool inNewTab)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            return;
        }
        if (isDirectory)
        {
            if (inNewTab || mainViewModel.SelectedFileTab == null)
            {
                mainViewModel.AddTab(path);
            }
            else
            {
                mainViewModel.SelectedFileTab.OpenPath(path);
            }
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiLogger.LogError("程序行为", "调用系统默认程序打开文件：行路径=" + path + " 结果=失败", ex);
        }
    }

    /// <summary>刷新按钮：完全重扫当前路径（忽略快照）。</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            string currentRoot = viewModel.RootPathInput == null ? string.Empty : viewModel.RootPathInput;
            viewModel.StartFullRefresh();
            UiLogger.Log("程序行为", "点击刷新按钮：当前根目录=" + (currentRoot.Length == 0 ? "此电脑" : currentRoot)
                + " 结果=成功，已请求完全重扫该根目录");
        }
        else
        {
            UiLogger.Log("程序行为", "点击刷新按钮：未找到磁盘视图模型 结果=失败，本次刷新取消");
        }
    }

    /// <summary>打开按钮：按输入框路径增量扫描。</summary>
    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            string targetRoot = viewModel.RootPathInput == null ? string.Empty : viewModel.RootPathInput;
            viewModel.StartRefresh(viewModel.RootPathInput);
            UiLogger.Log("程序行为", "点击打开按钮：路径输入框内容=" + (targetRoot.Length == 0 ? "（空）" : targetRoot)
                + " 结果=" + (targetRoot.Length == 0 ? "成功，已请求加载此电脑视图" : "成功，已请求按该路径扫描"));
        }
        else
        {
            UiLogger.Log("程序行为", "点击打开按钮：未找到磁盘视图模型 结果=失败，本次打开取消");
        }
    }

    /// <summary>上一级按钮：回到上级目录。</summary>
    private void OnNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is DiskViewPanelViewModel viewModel)
        {
            string originalRoot = viewModel.RootPathInput == null ? string.Empty : viewModel.RootPathInput;
            viewModel.NavigateUp();
            string newRoot = viewModel.RootPathInput == null ? string.Empty : viewModel.RootPathInput;
            UiLogger.Log("程序行为", "点击上一级按钮：原根目录=" + (originalRoot.Length == 0 ? "此电脑" : originalRoot)
                + " 新根目录=" + (newRoot.Length == 0 ? "此电脑" : newRoot)
                + " 结果=成功，已请求返回上一级根目录并刷新视图");
        }
        else
        {
            UiLogger.Log("程序行为", "点击上一级按钮：未找到磁盘视图模型 结果=失败，本次返回上级取消");
        }
    }

    /// <summary>单击列表条目：文件夹原地展开或收起，文件忽略。</summary>
    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (Stopwatch.GetTimestamp() < this.suppressTapUntil)
        {
            UiLogger.Log("程序行为", "点击列表条目：刚结束一次拖拽 结果=已忽略，避免把拖放误判为展开或收起");
            return;
        }
        if (this.DataContext is not DiskViewPanelViewModel viewModel || e.Source is not Control control)
        {
            UiLogger.Log("程序行为", "点击列表条目：视图模型或事件来源缺失 结果=已忽略，未展开或收起");
            return;
        }
        DiskRow? row = control.DataContext as DiskRow;
        if (row == null)
        {
            UiLogger.Log("程序行为", "点击列表条目：未关联磁盘行数据 结果=已忽略，未展开或收起");
            return;
        }
        if (!row.IsDirectory)
        {
            UiLogger.Log("程序行为", "点击文件条目：行路径=" + row.FullPath + " 结果=已忽略，文件不参与展开或收起");
            return;
        }
        UiLogger.Log("程序行为", "点击目录条目：行路径=" + row.FullPath + " 结果=已请求切换该目录的展开或收起状态");
        viewModel.ToggleRow(row);
    }

    /// <summary>点击表头：按该列排序（重复点击同一列切换升序降序），随后刷新表头箭头。</summary>
    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not DiskViewPanelViewModel viewModel || sender is not Button button || button.Tag is not string sortField)
        {
            return;
        }
        UiLogger.Log("磁盘视图", "界面操作：点击表头排序按钮 表头=" + sortField);
        viewModel.SetSortField(sortField);
        this.UpdateSortArrows();
    }

    /// <summary>按当前排序状态刷新六个表头的升序 / 降序箭头（未排序的列不显示箭头）。</summary>
    private void UpdateSortArrows()
    {
        if (this.DataContext is not DiskViewPanelViewModel viewModel)
        {
            return;
        }
        string field = viewModel.SortField;
        string arrow = viewModel.SortAscending ? "▲" : "▼";
        this.ArrowName.Text = field == "Name" ? arrow : string.Empty;
        this.ArrowType.Text = field == "Type" ? arrow : string.Empty;
        this.ArrowSize.Text = field == "Size" ? arrow : string.Empty;
        this.ArrowFree.Text = field == "Free" ? arrow : string.Empty;
        this.ArrowUsage.Text = field == "Usage" ? arrow : string.Empty;
        this.ArrowFileSystem.Text = field == "FileSystem" ? arrow : string.Empty;
    }

    /// <summary>按下时记录起点行与位置，供后续超过阈值时发起拖拽。</summary>
    private void OnPointerPressedForDrag(object? sender, PointerPressedEventArgs e)
    {
        this.dragRow = null;
        this.dragPressEvent = null;
        this.dragStarted = false;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        DiskRow? row = FindDiskRowFromSource(e.Source);
        if (row == null)
        {
            return;
        }
        this.dragRow = row;
        this.dragPressEvent = e;
        this.dragPressPoint = e.GetPosition(this);
    }

    /// <summary>移动超过 6 像素后发起系统拖拽（移动或复制，绝不删除源文件）。</summary>
    private void OnPointerMovedForDrag(object? sender, PointerEventArgs e)
    {
        if (this.dragStarted || this.dragRow == null || this.dragPressEvent == null)
        {
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        Avalonia.Point point = e.GetPosition(this);
        if (Math.Abs(point.X - this.dragPressPoint.X) < 6.0 && Math.Abs(point.Y - this.dragPressPoint.Y) < 6.0)
        {
            return;
        }
        this.dragStarted = true;
        this.suppressTapUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2;
        PointerPressedEventArgs pressEvent = this.dragPressEvent;
        DiskRow row = this.dragRow;
        this.BeginDiskDrag(pressEvent, row);
    }

    /// <summary>松开鼠标后清空拖拽状态。</summary>
    private void OnPointerReleasedForDrag(object? sender, PointerReleasedEventArgs e)
    {
        this.dragRow = null;
        this.dragPressEvent = null;
        this.dragStarted = false;
    }

    /// <summary>发起系统拖拽：携带本机文件项与移动或复制语义，外部程序按系统规则接收。</summary>
    private async void BeginDiskDrag(PointerPressedEventArgs pressEvent, DiskRow row)
    {
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (row.Parent == null && string.Equals(row.EntryTypeText, "驱动器", StringComparison.Ordinal))
        {
            UiLogger.Log("磁盘视图", "拒绝拖拽驱动器：路径=" + row.FullPath + " 结果=未发起拖拽");
            return;
        }
        if (viewModel == null || owner == null || !Directory.Exists(row.FullPath) && !File.Exists(row.FullPath))
        {
            UiLogger.Log("磁盘视图", "拖拽发起失败：行=" + (row == null ? "无" : row.FullPath) + " 原因=视图模型、窗口或条目缺失");
            return;
        }
        DataTransfer data = new DataTransfer();
        FileDragPayload payload = new FileDragPayload();
        payload.Paths.Add(row.FullPath);
        payload.Mode = FileDragMode.MoveOrCopy;
        data.Add(DataTransferItem.Create(FileDropPolicy.Format, payload));
        int fileItemCount = 0;
        try
        {
            IStorageItem? item = await FileDropPolicy.TryCreateStorageItemAsync(owner, row.FullPath);
            if (item != null)
            {
                data.Add(DataTransferItem.CreateFile(item));
                fileItemCount++;
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("磁盘视图", "拖拽携带本机文件项失败：行=" + row.FullPath, error);
        }
        UiLogger.Log("磁盘视图", "发起拖拽：行=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
            + " 携带文件项数=" + fileItemCount + " 拖拽源语义=移动或复制 结果=成功，已交给系统拖放");
        DragDropEffects applied;
        try
        {
            applied = await DragDrop.DoDragDropAsync(pressEvent, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception error)
        {
            UiLogger.LogError("磁盘视图", "拖拽过程出错：行=" + row.FullPath, error);
            return;
        }
        if (applied != DragDropEffects.None)
        {
            string parent = Path.GetDirectoryName(row.FullPath) ?? string.Empty;
            viewModel.RefreshAfterChange(parent);
            this.MainViewModel?.RefreshTabsAtPath(parent, null);
        }
        UiLogger.Log("磁盘视图", "拖拽结束：行=" + row.FullPath + " 目标实际应用效果=" + FileDropPolicy.DescribeEffects(applied)
            + " 结果=" + (applied == DragDropEffects.None ? "目标未接收，源文件保持不变" : "目标已接收，已刷新源目录"));
    }

    /// <summary>拖拽经过：按落点目录与源条目计算允许效果（文件夹行可接收，文件行与无目录背景拒绝）。</summary>
    private void OnDiskDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        List<string> sourcePaths = FileDropPolicy.CollectSourcePaths(e.DataTransfer, out bool isInternal, out FileDragMode mode);
        DiskRow? row = FindDiskRowFromSource(e.Source);
        string target = row == null ? (viewModel == null ? string.Empty : viewModel.RootPathInput)
            : (row.IsDirectory ? row.FullPath : string.Empty);
        if (viewModel == null || sourcePaths.Count == 0 || target.Length == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        if (!FileDropPolicy.IsDropTargetValid(sourcePaths, target, Directory.Exists(target)))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = FileDropPolicy.ResolveEffects(sourcePaths[0], target, e.KeyModifiers, mode);
        UiLogger.Log("磁盘视图", "拖拽经过：目标目录=" + target + " 拖放来源=" + (isInternal ? "本程序发起" : "外部程序拖入")
            + " 源条目数=" + sourcePaths.Count + " 效果=" + FileDropPolicy.DescribeEffects(e.DragEffects));
    }

    /// <summary>拖拽离开：清除状态。</summary>
    private void OnDiskDragLeave(object? sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>执行拖放：移动或复制到目标目录，并同步刷新磁盘视图与文件夹视图。</summary>
    private void OnDiskDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        DiskViewPanelViewModel? viewModel = this.ViewModel;
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        List<string> sourcePaths = FileDropPolicy.CollectSourcePaths(e.DataTransfer, out bool isInternal, out FileDragMode mode);
        DiskRow? row = FindDiskRowFromSource(e.Source);
        string target = row == null ? (viewModel == null ? string.Empty : viewModel.RootPathInput)
            : (row.IsDirectory ? row.FullPath : string.Empty);
        if (viewModel == null || mainViewModel == null || sourcePaths.Count == 0 || target.Length == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        if (!FileDropPolicy.IsDropTargetValid(sourcePaths, target, Directory.Exists(target)))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        DragDropEffects effects = FileDropPolicy.ResolveEffects(sourcePaths[0], target, e.KeyModifiers, mode);
        e.DragEffects = effects;
        bool isMove = (effects & DragDropEffects.Move) != 0;
        List<string> affectedDirectories = new List<string>();
        foreach (string source in sourcePaths)
        {
            string parent = Path.GetDirectoryName(source) ?? string.Empty;
            if (parent.Length > 0 && !affectedDirectories.Contains(parent)) { affectedDirectories.Add(parent); }
        }
        int successCount = mainViewModel.TransferPathsToDirectory(null, target, sourcePaths, isMove, out int failedCount);
        affectedDirectories.Add(target);
        foreach (string directory in affectedDirectories)
        {
            viewModel.RefreshAfterChange(directory);
            mainViewModel.RefreshTabsAtPath(directory, null);
        }
        if (isInternal)
        {
            FileDropPolicy.LastInternalDropAction = isMove ? "移动" : "复制";
        }
        UiLogger.Log("磁盘视图", "拖放执行：目标目录=" + target + " 动作=" + (isMove ? "移动" : "复制")
            + " 拖放来源=" + (isInternal ? "本程序发起" : "外部程序拖入")
            + " 源条目数=" + sourcePaths.Count + " 成功条数=" + successCount + " 失败条数=" + failedCount
            + " 结果=已执行并刷新磁盘视图与文件夹视图");
    }

    /// <summary>从事件来源向上找到承载磁盘行的控件。</summary>
    private static DiskRow? FindDiskRowFromSource(object? source)
    {
        Avalonia.Visual? visual = source as Avalonia.Visual;
        while (visual != null)
        {
            if (visual is Control control && control.DataContext is DiskRow row)
            {
                return row;
            }
            if (visual is ListBox)
            {
                return null;
            }
            visual = visual.GetVisualParent();
        }
        return null;
    }
}
