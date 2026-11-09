using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Tasks;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 桌面任务中心：以统一任务快照（本地复制 / 移动 / 删除 + 远程传输）为一屏列表，
/// 只调用 <see cref="MainWindowViewModel"/> 已有任务方法，不另写任务逻辑。
/// </summary>
public sealed class TaskCenterWindow : Window
{
    /// <summary>需要随主题切换重新解析的画刷绑定（控件, 属性, 资源键）。</summary>
    private readonly List<(AvaloniaObject Target, AvaloniaProperty Property, string Key)> brushBindings =
        new List<(AvaloniaObject, AvaloniaProperty, string)>();

    private readonly MainWindowViewModel viewModel;
    private readonly DispatcherTimer refreshTimer;
    private readonly TextBlock statsText;
    private readonly TextBlock statusText;
    private readonly StackPanel taskHost;
    private readonly Button cancelButton;
    private readonly Button retryButton;
    private readonly Button mergeButton;
    private readonly Button overwriteButton;
    private readonly Button perItemButton;
    private readonly Button renameButton;
    private readonly CheckBox applyToAllBox;

    private List<FileTaskSnapshot> tasks = new List<FileTaskSnapshot>();
    private string selectedId = string.Empty;

    /// <summary>构造任务中心窗口。</summary>
    public TaskCenterWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        this.Title = "任务中心";
        this.Width = 900;
        this.Height = 560;
        this.MinWidth = 720;
        this.MinHeight = 420;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Grid root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        this.Content = root;

        // 顶部：标题 + 统计 + 刷新
        TextBlock title = this.CreateText("任务中心", "TextPrimaryBrush", Token("TokenFontSizeLarge", 15), true);
        this.statsText = this.CreateText(string.Empty, "TextMutedBrush", Token("TokenFontSizeSmall", 12));
        StackPanel titleRow = new StackPanel();
        titleRow.Orientation = Orientation.Horizontal;
        titleRow.Spacing = Token("TokenSpacingLarge", 8);
        titleRow.Children.Add(title);
        titleRow.Children.Add(this.statsText);
        Button refreshButton = CreateButton("刷新", new Thickness(9, 4));
        refreshButton.Click += delegate { this.Refresh("手动刷新"); };
        Grid head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        head.Margin = new Thickness(Token("TokenSpacingXLarge", 12), Token("TokenSpacingMedium", 6));
        head.Children.Add(titleRow);
        head.Children.Add(refreshButton);
        Grid.SetColumn(refreshButton, 1);
        root.Children.Add(head);
        Grid.SetRow(head, 0);

        // 中部：任务列表（每秒重建，保持当前选中编号）
        this.taskHost = new StackPanel();
        this.taskHost.Spacing = Token("TokenSpacingSmall", 4);
        this.taskHost.Margin = new Thickness(Token("TokenSpacingXLarge", 12));
        ScrollViewer scroll = new ScrollViewer();
        scroll.Content = this.taskHost;
        Border listBorder = new Border();
        listBorder.BorderThickness = new Thickness(0, 1, 0, 1);
        this.ApplyBrush(listBorder, Border.BorderBrushProperty, "SeparatorBrush");
        listBorder.Child = scroll;
        root.Children.Add(listBorder);
        Grid.SetRow(listBorder, 1);

        // 底部操作区
        this.cancelButton = CreateButton("取消任务", new Thickness(9, 4));
        this.cancelButton.Click += delegate { this.CancelSelected(); };
        this.retryButton = CreateButton("重试失败项", new Thickness(9, 4));
        this.retryButton.Click += delegate { this.RetrySelected(); };
        this.mergeButton = CreateButton("合并", new Thickness(9, 4));
        this.mergeButton.Click += delegate { this.ResolveSelected("merge"); };
        this.overwriteButton = CreateButton("替换", new Thickness(9, 4));
        this.overwriteButton.Click += delegate { this.ResolveSelected("overwrite"); };
        this.perItemButton = CreateButton("逐项处理", new Thickness(9, 4));
        this.perItemButton.Click += delegate { this.ResolveSelected("perItem"); };
        this.renameButton = CreateButton("改名共存", new Thickness(9, 4));
        this.renameButton.Click += delegate { this.ResolveSelected("rename"); };
        this.applyToAllBox = new CheckBox();
        this.applyToAllBox.Content = "本批统一";
        this.applyToAllBox.FontSize = Token("TokenFontSizeSmall", 12);
        this.applyToAllBox.Margin = new Thickness(0, 0, Token("TokenSpacingSmall", 4), 0);

        StackPanel actions = new StackPanel();
        actions.Orientation = Orientation.Horizontal;
        actions.Spacing = Token("TokenSpacingMedium", 6);
        actions.Children.Add(this.cancelButton);
        actions.Children.Add(this.retryButton);
        Border actionSeparator = new Border();
        actionSeparator.Width = 1;
        actionSeparator.Height = 20;
        actionSeparator.Margin = new Thickness(Token("TokenSpacingSmall", 4), 0);
        this.ApplyBrush(actionSeparator, Border.BackgroundProperty, "SeparatorBrush");
        actions.Children.Add(actionSeparator);
        actions.Children.Add(this.mergeButton);
        actions.Children.Add(this.overwriteButton);
        actions.Children.Add(this.perItemButton);
        actions.Children.Add(this.renameButton);
        actions.Children.Add(this.applyToAllBox);

        Button closeButton = CreateButton("关闭", new Thickness(9, 4));
        closeButton.Click += delegate { this.Close(); };
        Grid actionGrid = new Grid();
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        actionGrid.Margin = new Thickness(Token("TokenSpacingXLarge", 12), Token("TokenSpacingMedium", 6));
        actionGrid.Children.Add(actions);
        actionGrid.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 1);
        root.Children.Add(actionGrid);
        Grid.SetRow(actionGrid, 2);

        // 状态栏
        this.statusText = this.CreateText("就绪", "TextMutedBrush", Token("TokenFontSizeTiny", 11));
        Border statusBar = new Border();
        statusBar.Padding = new Thickness(Token("TokenSpacingXLarge", 12), Token("TokenSpacingSmall", 4));
        statusBar.BorderThickness = new Thickness(0, 1, 0, 0);
        this.ApplyBrush(statusBar, Border.BorderBrushProperty, "SeparatorBrush");
        this.ApplyBrush(statusBar, Border.BackgroundProperty, "StatusBarBrush");
        statusBar.Child = this.statusText;
        root.Children.Add(statusBar);
        Grid.SetRow(statusBar, 3);

        this.refreshTimer = new DispatcherTimer();
        this.refreshTimer.Interval = TimeSpan.FromSeconds(1);
        this.refreshTimer.Tick += delegate { this.Refresh(null); };
        this.Opened += delegate
        {
            this.ApplyBrushes();
            this.Refresh("打开任务中心");
            this.refreshTimer.Start();
        };
        this.ActualThemeVariantChanged += delegate { this.ApplyBrushes(); };
        this.Closed += delegate { this.refreshTimer.Stop(); };
        this.KeyDown += this.OnWindowKeyDown;
        UiLogger.AttachWindow(this);
    }

    /// <summary>Esc 关闭任务中心窗口。</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
            e.Handled = true;
        }
    }

    /// <summary>登记一个资源画刷绑定，并在窗口打开后按当前主题解析。</summary>
    private void ApplyBrush(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        this.brushBindings.Add((target, property, key));
        this.ResolveBrush(target, property, key);
    }

    private void ApplyBrushes()
    {
        foreach ((AvaloniaObject target, AvaloniaProperty property, string key) in this.brushBindings)
        {
            this.ResolveBrush(target, property, key);
        }
        this.RenderTasks();
    }

    private void ResolveBrush(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        if (this.TryFindResource(key, out object? value) && value is IBrush brush)
        {
            target.SetValue(property, brush);
        }
    }

    /// <summary>读取快照并刷新界面；<paramref name="reason"/> 为空表示定时静默刷新。</summary>
    private void Refresh(string? reason)
    {
        try
        {
            this.tasks = this.viewModel.GetFileTasks(string.Empty);
            if (!string.IsNullOrEmpty(this.selectedId)
                && !this.tasks.Exists(delegate(FileTaskSnapshot item) { return item.Id == this.selectedId; }))
            {
                this.selectedId = string.Empty;
            }
            this.RenderTasks();
            this.RenderStats();
            this.UpdateActions();
            if (!string.IsNullOrEmpty(reason))
            {
                this.statusText.Text = reason + "：共 " + this.tasks.Count + " 个任务";
            }
        }
        catch (Exception error)
        {
            this.statusText.Text = "刷新任务失败：" + error.Message;
            UiLogger.Log("任务中心", "刷新任务失败：" + error.Message);
        }
    }

    private void RenderStats()
    {
        int running = this.tasks.Count(delegate(FileTaskSnapshot item) { return item.State == "running"; });
        int waiting = this.tasks.Count(delegate(FileTaskSnapshot item) { return item.State == "waitingConflict"; });
        this.statsText.Text = "共 " + this.tasks.Count + " 个任务 · 运行中 " + running + " · 等待冲突 " + waiting;
    }

    private void RenderTasks()
    {
        this.taskHost.Children.Clear();
        if (this.tasks.Count == 0)
        {
            TextBlock empty = this.CreateText("暂无任务", "TextMutedBrush", Token("TokenFontSizeBody", 13));
            empty.Margin = new Thickness(0, Token("TokenSpacingXLarge", 12));
            this.taskHost.Children.Add(empty);
            return;
        }
        foreach (FileTaskSnapshot snapshot in this.tasks)
        {
            this.taskHost.Children.Add(this.BuildTaskRow(snapshot));
        }
    }

    private Control BuildTaskRow(FileTaskSnapshot snapshot)
    {
        bool selected = snapshot.Id == this.selectedId;
        Border row = new Border();
        row.Padding = new Thickness(Token("TokenSpacingLarge", 8));
        row.CornerRadius = new CornerRadius(Token("TokenRadiusLarge", 6));
        row.BorderThickness = new Thickness(1);
        row.BorderBrush = this.ResolveBrushValue(
            snapshot.State == "waitingConflict" ? "WarningBrush" : "SubtleBorderBrush");
        row.Background = this.ResolveBrushValue(selected ? "RowSelectedBrush" : "ContentBrush");
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerPressed += delegate
        {
            this.selectedId = snapshot.Id;
            this.RenderTasks();
            this.UpdateActions();
        };

        StackPanel body = new StackPanel();
        body.Spacing = Token("TokenSpacingXSmall", 2);
        row.Child = body;

        StackPanel head = new StackPanel();
        head.Orientation = Orientation.Horizontal;
        head.Spacing = Token("TokenSpacingMedium", 6);
        TextBlock kind = this.CreateText(
            (snapshot.IsRemote ? "远程" : string.Empty) + KindText(snapshot.Kind),
            "TextPrimaryBrush", Token("TokenFontSizeSmall", 12), true);
        TextBlock state = this.CreateText(StateText(snapshot.State), StateBrush(snapshot.State),
            Token("TokenFontSizeTiny", 11));
        TextBlock progress = this.CreateText(
            snapshot.Total > 0
                ? "进度 " + snapshot.Processed + "/" + snapshot.Total + "（" + snapshot.Progress + "%）"
                : "进度 " + snapshot.Progress + "%",
            "TextMutedBrush", Token("TokenFontSizeTiny", 11));
        progress.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(kind);
        head.Children.Add(state);
        head.Children.Add(progress);
        body.Children.Add(head);

        TextBlock paths = this.CreateText(
            "源：" + (string.IsNullOrEmpty(snapshot.SourceText) ? "—" : snapshot.SourceText)
            + "    →    目标：" + (string.IsNullOrEmpty(snapshot.DestinationText) ? "—" : snapshot.DestinationText),
            "TextMutedBrush", Token("TokenFontSizeTiny", 11));
        paths.TextTrimming = TextTrimming.CharacterEllipsis;
        body.Children.Add(paths);

        if (!string.IsNullOrEmpty(snapshot.Message))
        {
            TextBlock message = this.CreateText(snapshot.Message, "TextPrimaryBrush", Token("TokenFontSizeTiny", 11));
            message.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(message);
        }

        if (!string.IsNullOrEmpty(snapshot.ConflictPath))
        {
            TextBlock conflict = this.CreateText(
                "冲突：" + (string.IsNullOrEmpty(snapshot.ConflictName) ? snapshot.ConflictPath : snapshot.ConflictName)
                + (snapshot.ConflictIsDirectory ? "（文件夹）" : "（文件）"),
                "WarningBrush", Token("TokenFontSizeTiny", 11));
            conflict.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(conflict);
        }
        return row;
    }

    private void UpdateActions()
    {
        FileTaskSnapshot? selected = this.FindSelected();
        this.cancelButton.IsEnabled = selected != null && selected.CanCancel;
        this.retryButton.IsEnabled = selected != null && selected.CanRetry;
        bool hasConflict = selected != null && !string.IsNullOrEmpty(selected.ConflictPath);
        bool isDirectory = selected != null && selected.ConflictIsDirectory;
        this.mergeButton.IsEnabled = hasConflict && isDirectory;
        this.overwriteButton.IsEnabled = hasConflict;
        this.perItemButton.IsEnabled = hasConflict && isDirectory;
        this.renameButton.IsEnabled = hasConflict;
        this.applyToAllBox.IsEnabled = hasConflict;
    }

    private FileTaskSnapshot? FindSelected()
    {
        foreach (FileTaskSnapshot item in this.tasks)
        {
            if (item.Id == this.selectedId)
            {
                return item;
            }
        }
        return null;
    }

    private void CancelSelected()
    {
        FileTaskSnapshot? selected = this.FindSelected();
        if (selected == null)
        {
            this.statusText.Text = "请先选择要取消的任务";
            return;
        }
        bool ok = this.viewModel.CancelFileTask(string.Empty, selected.Id);
        this.statusText.Text = ok ? "已请求取消任务 " + selected.Id : "取消失败：任务可能已结束，请刷新后重试";
        UiLogger.Log("任务中心", "取消任务：编号=" + selected.Id + " 结果=" + ok);
        this.Refresh(null);
    }

    private void RetrySelected()
    {
        FileTaskSnapshot? selected = this.FindSelected();
        if (selected == null)
        {
            this.statusText.Text = "请先选择要重试的任务";
            return;
        }
        bool ok = this.viewModel.RetryFileTask(string.Empty, selected.Id);
        this.statusText.Text = ok ? "已提交重试：任务 " + selected.Id : "重试失败：任务可能已结束，请刷新后重试";
        UiLogger.Log("任务中心", "重试任务：编号=" + selected.Id + " 结果=" + ok);
        this.Refresh(null);
    }

    private void ResolveSelected(string action)
    {
        FileTaskSnapshot? selected = this.FindSelected();
        if (selected == null)
        {
            this.statusText.Text = "请先选择等待冲突的任务";
            return;
        }
        bool applyToAll = this.applyToAllBox.IsChecked == true;
        bool ok = this.viewModel.ResolveFileTaskConflict(string.Empty, selected.Id, action, applyToAll);
        this.statusText.Text = ok
            ? "冲突已处理：" + ActionText(action) + (applyToAll ? "（本批统一）" : string.Empty)
            : "冲突处理失败：任务可能已结束，请刷新后重试";
        UiLogger.Log("任务中心", "处理冲突：编号=" + selected.Id + " 选择=" + action
            + " 本批统一=" + applyToAll + " 结果=" + ok);
        this.Refresh(null);
    }

    private TextBlock CreateText(string text, string brushKey, double fontSize, bool bold = false)
    {
        TextBlock block = new TextBlock();
        block.Text = text;
        block.FontSize = fontSize;
        block.VerticalAlignment = VerticalAlignment.Center;
        block.Foreground = this.ResolveBrushValue(brushKey);
        if (bold)
        {
            block.FontWeight = FontWeight.SemiBold;
        }
        return block;
    }

    private IBrush? ResolveBrushValue(string key)
    {
        if (this.TryFindResource(key, out object? value) && value is IBrush brush)
        {
            return brush;
        }
        return null;
    }

    private static Button CreateButton(string text, Thickness padding)
    {
        Button button = new Button();
        button.Content = text;
        button.Padding = padding;
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    private static string KindText(string kind)
    {
        if (kind == "move")
        {
            return "移动";
        }
        if (kind == "delete")
        {
            return "删除";
        }
        return "复制";
    }

    private static string StateText(string state)
    {
        if (state == "running")
        {
            return "进行中";
        }
        if (state == "waitingConflict")
        {
            return "等待冲突";
        }
        if (state == "completed")
        {
            return "已完成";
        }
        if (state == "failed")
        {
            return "失败";
        }
        if (state == "canceled")
        {
            return "已取消";
        }
        if (state == "paused")
        {
            return "已暂停";
        }
        return "排队中";
    }

    private static string StateBrush(string state)
    {
        if (state == "completed")
        {
            return "SuccessBrush";
        }
        if (state == "failed")
        {
            return "DangerBrush";
        }
        if (state == "waitingConflict")
        {
            return "WarningBrush";
        }
        return "TextMutedBrush";
    }

    private static string ActionText(string action)
    {
        if (action == "merge")
        {
            return "合并";
        }
        if (action == "overwrite")
        {
            return "替换";
        }
        if (action == "perItem")
        {
            return "逐项处理";
        }
        return "改名共存";
    }

    private static double Token(string key, double fallback)
    {
        if (Application.Current != null
            && Application.Current.TryFindResource(key, out object? value)
            && value is double number)
        {
            return number;
        }
        return fallback;
    }
}
