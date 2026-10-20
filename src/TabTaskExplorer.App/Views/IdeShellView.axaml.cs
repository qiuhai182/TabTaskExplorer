using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// IDE 模式视图：资源树 + 编辑器标签条 + 带行号的原生文本框 + 查找替换面板 + 快速打开浮层。
/// 行号栏通过同步内部滚动条偏移与编辑区对齐；Tab 键按设置替换为空格。
/// </summary>
public partial class IdeShellView : UserControl
{
    private IdeShellViewModel? hookedShell;
    private ScrollViewer? editorScroll;
    private bool syncing;

    public IdeShellView()
    {
        this.InitializeComponent();
        this.DataContextChanged += this.OnDataContextChanged;
        this.AttachedToVisualTree += this.OnAttachedToVisualTree;
        this.EditorBox.TextChanged += this.OnEditorTextChanged;
        this.EditorBox.KeyDown += this.OnEditorKeyDown;
        this.EditorBox.KeyUp += this.OnEditorCaretChanged;
        this.EditorBox.PointerReleased += this.OnEditorCaretChanged;
        this.EditorBox.AddHandler(InputElement.ScrollGestureEvent, this.OnEditorPointerWheel, RoutingStrategies.Tunnel);
    }

    /// <summary>IDE 外壳视图模型。</summary>
    private IdeShellViewModel? Shell
    {
        get { return this.DataContext as IdeShellViewModel; }
    }

    /// <summary>DataContext 就绪：订阅视图模型事件、应用编辑器字体、初始化行号。</summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (this.hookedShell != null)
        {
            this.hookedShell.PropertyChanged -= this.OnShellPropertyChanged;
            this.hookedShell.FindSelectionChanged -= this.OnFindSelectionChanged;
        }
        this.hookedShell = this.Shell;
        if (this.hookedShell != null)
        {
            this.hookedShell.PropertyChanged += this.OnShellPropertyChanged;
            this.hookedShell.FindSelectionChanged += this.OnFindSelectionChanged;
        }
        this.ApplyEditorFont();
        this.UpdateLineNumbers();
    }

    /// <summary>挂到可视化树后：定位编辑区内部滚动条并同步行号栏偏移。</summary>
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        this.AttachEditorScroll();
        this.ApplyEditorFont();
        this.UpdateLineNumbers();
    }

    /// <summary>查找内部滚动条并监听其滚动，用于同步行号栏。</summary>
    private void AttachEditorScroll()
    {
        if (this.editorScroll != null)
        {
            return;
        }
        ScrollViewer? viewer = this.EditorBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer == null)
        {
            return;
        }
        this.editorScroll = viewer;
        viewer.ScrollChanged += this.OnEditorScrollChanged;
    }

    /// <summary>编辑区滚动：把纵向偏移同步给行号栏。</summary>
    private void OnEditorScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (this.editorScroll == null || this.syncing)
        {
            return;
        }
        this.syncing = true;
        this.LineNumberScroll.Offset = new Vector(this.LineNumberScroll.Offset.X, this.editorScroll.Offset.Y);
        this.syncing = false;
    }

    /// <summary>编辑区滚轮：同步行号栏纵向偏移（滚轮默认不触发 ScrollChanged 时兜底）。</summary>
    private void OnEditorPointerWheel(object? sender, ScrollGestureEventArgs e)
    {
        Dispatcher.UIThread.Post(this.SyncLineNumberScroll, DispatcherPriority.Background);
    }

    /// <summary>把内层滚动条偏移同步到行号栏。</summary>
    private void SyncLineNumberScroll()
    {
        if (this.editorScroll == null)
        {
            this.AttachEditorScroll();
        }
        if (this.editorScroll == null)
        {
            return;
        }
        this.syncing = true;
        this.LineNumberScroll.Offset = new Vector(this.LineNumberScroll.Offset.X, this.editorScroll.Offset.Y);
        this.syncing = false;
    }

    /// <summary>按设置应用编辑器与行号栏字体、字号。</summary>
    private void ApplyEditorFont()
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            return;
        }
        FontFamily family = FontFamily.Parse(shell.Settings.EditorFontFamily);
        double size = shell.Settings.EditorFontSize <= 0 ? 13 : shell.Settings.EditorFontSize;
        this.EditorBox.FontFamily = family;
        this.EditorBox.FontSize = size;
        this.LineNumbersBlock.FontFamily = family;
        this.LineNumbersBlock.FontSize = size;
        this.EditorBox.TextWrapping = shell.Settings.EditorWordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    /// <summary>文本变化：刷新行号、光标位置与行数。</summary>
    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        this.UpdateLineNumbers();
        this.PushCaret();
    }

    /// <summary>光标 / 选区变化：更新状态栏行列。</summary>
    private void OnEditorCaretChanged(object? sender, EventArgs e)
    {
        this.PushCaret();
    }

    /// <summary>编辑器按键：Tab 替换空格、Esc 关闭浮层。</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (e.Key == Key.Tab && shell != null && shell.Settings.EditorInsertSpaces)
        {
            this.InsertSpacesAtCaret(shell.Settings.EditorTabWidth);
            e.Handled = true;
        }
    }

    /// <summary>在当前光标处插入指定数量的空格。</summary>
    private void InsertSpacesAtCaret(int count)
    {
        if (count <= 0)
        {
            count = 4;
        }
        string current = this.EditorBox.Text ?? string.Empty;
        int caret = this.EditorBox.CaretIndex;
        if (caret < 0)
        {
            caret = 0;
        }
        if (caret > current.Length)
        {
            caret = current.Length;
        }
        string spaces = new string(' ', count);
        string updated = current.Substring(0, caret) + spaces + current.Substring(caret);
        this.EditorBox.Text = updated;
        this.EditorBox.CaretIndex = caret + count;
    }

    /// <summary>把光标行列推送到视图模型状态栏。</summary>
    private void PushCaret()
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || shell.SelectedEditor == null)
        {
            return;
        }
        string text = this.EditorBox.Text ?? string.Empty;
        int caret = this.EditorBox.CaretIndex;
        if (caret < 0)
        {
            caret = 0;
        }
        if (caret > text.Length)
        {
            caret = text.Length;
        }
        int line = 1;
        int column = 1;
        for (int index = 0; index < caret; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        int selectionLength = Math.Abs(this.EditorBox.SelectionEnd - this.EditorBox.SelectionStart);
        shell.SelectedEditor.UpdateCaret(line, column, selectionLength);
    }

    /// <summary>按当前文本重排行号栏。</summary>
    private void UpdateLineNumbers()
    {
        string text = this.EditorBox.Text ?? string.Empty;
        int lineCount = 1;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                lineCount++;
            }
        }
        if (lineCount < 1)
        {
            lineCount = 1;
        }
        StringBuilder builder = new StringBuilder();
        for (int number = 1; number <= lineCount; number++)
        {
            if (number > 1)
            {
                builder.Append('\n');
            }
            builder.Append(number);
        }
        this.LineNumbersBlock.Text = builder.ToString();
        this.SyncLineNumberScroll();
    }

    /// <summary>外壳属性变化：切换编辑器 / 打开快速打开 / 待定位行等。</summary>
    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IdeShellViewModel.SelectedEditor))
        {
            Dispatcher.UIThread.Post(this.OnEditorSwitched, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(IdeShellViewModel.ShowQuickOpen))
        {
            if (this.Shell != null && this.Shell.ShowQuickOpen)
            {
                Dispatcher.UIThread.Post(delegate { this.QuickOpenBox.Focus(); }, DispatcherPriority.Background);
            }
        }
    }

    /// <summary>切换编辑器标签后：刷新行号与光标，并处理待定位行。</summary>
    private void OnEditorSwitched()
    {
        this.UpdateLineNumbers();
        this.PushCaret();
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || shell.SelectedEditor == null)
        {
            return;
        }
        if (shell.PendingCaretLine > 0)
        {
            int targetLine = shell.PendingCaretLine;
            shell.PendingCaretLine = 0;
            this.MoveCaretToLine(targetLine);
        }
    }

    /// <summary>把光标移动到指定行首并聚焦。</summary>
    private void MoveCaretToLine(int lineNumber)
    {
        string text = this.EditorBox.Text ?? string.Empty;
        int currentLine = 1;
        int index = 0;
        while (index < text.Length && currentLine < lineNumber)
        {
            if (text[index] == '\n')
            {
                currentLine++;
            }
            index++;
        }
        this.EditorBox.CaretIndex = index;
        this.EditorBox.Focus();
    }

    /// <summary>查找命中位置变化：在编辑器中选中并滚动到可见。</summary>
    private void OnFindSelectionChanged(object? sender, EventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || shell.FindSelectionStart < 0)
        {
            return;
        }
        int start = shell.FindSelectionStart;
        int length = shell.FindSelectionLength;
        this.EditorBox.SelectionStart = start;
        this.EditorBox.SelectionEnd = start + length;
        this.EditorBox.Focus();
        this.MoveCaretToLine(this.CountLinesBefore(shell.SelectedEditor != null ? shell.SelectedEditor.Text : string.Empty, start));
    }

    /// <summary>统计指定位置之前的换行数以得到行号。</summary>
    private int CountLinesBefore(string text, int position)
    {
        int line = 1;
        int limit = Math.Min(position, text.Length);
        for (int index = 0; index < limit; index++)
        {
            if (text[index] == '\n')
            {
                line++;
            }
        }
        return line;
    }

    // ======== 工具栏 / 面板按钮 ========

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        this.Shell?.SaveActive();
    }

    private void OnSaveAllClick(object? sender, RoutedEventArgs e)
    {
        this.Shell?.SaveAll();
    }

    private void OnFindClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            return;
        }
        shell.ShowFindPanel = true;
        this.FindBox.Focus();
    }

    private void OnQuickOpenClick(object? sender, RoutedEventArgs e)
    {
        this.Shell?.OpenQuickOpen();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        this.Shell?.ReloadWorkspace();
    }

    // ======== 资源树 ========

    /// <summary>资源树双击：文件夹展开 / 收起，文件打开到编辑器。</summary>
    private void OnTreeItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || sender is not Control control || control.DataContext is not FileTreeItemViewModel item)
        {
            return;
        }
        if (item.IsPlaceholder)
        {
            return;
        }
        if (item.IsDirectory)
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }
        shell.OpenFile(item.FullPath, item.ContentAccess, item.ConnectionName);
    }

    // ======== 编辑器标签条 ========

    /// <summary>关闭编辑器标签：有未保存更改时先确认。</summary>
    private async void OnCloseEditorTabClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || sender is not Button button || button.Tag is not IdeEditorTabViewModel editor)
        {
            return;
        }
        if (editor.IsDirty)
        {
            Window? owner = this.GetOwnerWindow();
            SavePromptChoice choice = await MessageDialog.ConfirmSaveAsync(owner, editor.Title);
            if (choice == SavePromptChoice.Cancel)
            {
                return;
            }
            if (choice == SavePromptChoice.Save)
            {
                editor.Save();
            }
        }
        shell.CloseEditor(editor);
    }

    /// <summary>向上查找宿主窗口。</summary>
    private Window? GetOwnerWindow()
    {
        return TopLevel.GetTopLevel(this) as Window;
    }

    // ======== 查找替换 ========

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            if (shell.FindNext(true))
            {
                shell.RequestApplyFindSelection();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            shell.ShowFindPanel = false;
            e.Handled = true;
        }
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell != null && shell.FindNext(true))
        {
            shell.RequestApplyFindSelection();
        }
    }

    private void OnFindPreviousClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell != null && shell.FindNext(false))
        {
            shell.RequestApplyFindSelection();
        }
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        this.Shell?.ReplaceCurrent();
    }

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
    {
        this.Shell?.ReplaceAll();
    }

    /// <summary>在工作区范围内递归搜索（结果进入下方列表）。</summary>
    private void OnSearchWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        this.Shell?.SearchWorkspace();
    }

    /// <summary>查找结果被选中：打开对应文件并定位到该行。</summary>
    private void OnFindResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || sender is not ListBox list || list.SelectedItem is not FindResultItem item)
        {
            return;
        }
        shell.OpenFindResult(item);
        list.SelectedItem = null;
    }

    // ======== 快速打开 ========

    private void OnQuickOpenKeyDown(object? sender, KeyEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            return;
        }
        if (e.Key == Key.Escape)
        {
            shell.ShowQuickOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (this.QuickOpenList.ItemCount > 0)
            {
                shell.CommitQuickOpen((QuickOpenItem)this.QuickOpenList.Items[0]!);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down && this.QuickOpenList.ItemCount > 0)
        {
            this.QuickOpenList.SelectedIndex = 0;
            this.QuickOpenList.Focus();
            e.Handled = true;
        }
    }

    /// <summary>快速打开列表双击：打开该项。</summary>
    private void OnQuickOpenDoubleTapped(object? sender, TappedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || sender is not Control control || control.DataContext is not QuickOpenItem item)
        {
            return;
        }
        shell.CommitQuickOpen(item);
    }
}
