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
using TabTaskExplorer.App.Services;
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
        if (e.Key != Key.Tab)
        {
            return;
        }
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按键=制表键（编辑器）：集成开发环境视图模型未就绪，结果=未插入空格，保持默认输入行为");
            return;
        }
        if (!shell.Settings.EditorInsertSpaces)
        {
            UiLogger.Log("程序行为", "按键=制表键（编辑器）：设置要求直接插入制表符，结果=未替换为空格，保持默认输入行为");
            return;
        }
        this.InsertSpacesAtCaret(shell.Settings.EditorTabWidth);
        e.Handled = true;
        UiLogger.Log("程序行为", "按键=制表键（编辑器）：插入空格数=" + shell.Settings.EditorTabWidth
            + " 结果=成功，已在光标处插入空格并拦截制表符");
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
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=保存 被点击：集成开发环境视图模型未就绪，结果=未执行保存");
            return;
        }
        IdeEditorTabViewModel? editor = shell.SelectedEditor;
        bool saving = editor != null && editor.Saving;
        bool readOnly = editor != null && editor.IsReadOnly;
        bool dirty = editor != null && editor.IsDirty;
        shell.SaveActive();
        string resultText;
        if (editor == null)
        {
            resultText = "未执行，当前没有打开的编辑器";
        }
        else if (saving)
        {
            resultText = "未执行，该文件正在保存中";
        }
        else if (readOnly)
        {
            resultText = "未执行，该文件为只读";
        }
        else if (!dirty)
        {
            resultText = "未执行，该文件没有未保存的更改";
        }
        else
        {
            resultText = "成功，已把当前内容提交到后台写回文件，最终结果见编辑器状态栏";
        }
        UiLogger.Log("程序行为", "按钮=保存 被点击：文件=" + (editor != null ? editor.Title : "（无打开的编辑器）")
            + " 结果=" + resultText);
    }

    private void OnSaveAllClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=全部保存 被点击：集成开发环境视图模型未就绪，结果=未执行保存");
            return;
        }
        int totalEditors = 0;
        int saveableEditors = 0;
        foreach (IdeEditorTabViewModel editor in shell.Editors)
        {
            totalEditors++;
            if (editor.IsDirty && !editor.IsReadOnly && !editor.Saving)
            {
                saveableEditors++;
            }
        }
        shell.SaveAll();
        string resultText;
        if (totalEditors == 0)
        {
            resultText = "未执行，当前没有打开的编辑器";
        }
        else if (saveableEditors == 0)
        {
            resultText = "未执行，没有需要保存的更改";
        }
        else
        {
            resultText = "成功，已把 " + saveableEditors + " 个文件提交到后台写回";
        }
        UiLogger.Log("程序行为", "按钮=全部保存 被点击：编辑器数量=" + totalEditors + " 可保存数量=" + saveableEditors
            + " 结果=" + resultText);
    }

    private void OnFindClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=查找 被点击：集成开发环境视图模型未就绪，结果=未打开查找面板");
            return;
        }
        shell.ShowFindPanel = true;
        this.FindBox.Focus();
        UiLogger.Log("程序行为", "按钮=查找 被点击：结果=成功，查找面板已显示并把焦点移到查找输入框");
    }

    private void OnQuickOpenClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=快速打开 被点击：集成开发环境视图模型未就绪，结果=未打开快速打开浮层");
            return;
        }
        shell.OpenQuickOpen();
        UiLogger.Log("程序行为", "按钮=快速打开 被点击：工作区根目录="
            + (string.IsNullOrEmpty(shell.WorkspaceRoot) ? "（未设置）" : shell.WorkspaceRoot)
            + " 结果=成功，快速打开浮层已打开并开始收集工作区文件清单");
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=刷新工作区 被点击：集成开发环境视图模型未就绪，结果=未刷新资源树");
            return;
        }
        string workspaceRoot = shell.WorkspaceRoot;
        shell.ReloadWorkspace();
        UiLogger.Log("程序行为", "按钮=刷新工作区 被点击：工作区根目录="
            + (string.IsNullOrEmpty(workspaceRoot) ? "（未设置）" : workspaceRoot)
            + " 结果=" + (string.IsNullOrEmpty(workspaceRoot)
                ? "资源树已清空，未设置工作区根目录"
                : "成功，资源树正在后台重新枚举根目录"));
    }

    // ======== 资源树 ========

    /// <summary>资源树双击：文件夹展开 / 收起，文件打开到编辑器。</summary>
    private void OnTreeItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || sender is not Control control || control.DataContext is not FileTreeItemViewModel item)
        {
            UiLogger.Log("程序行为", "资源树节点双击：集成开发环境视图模型未就绪或触发源不是资源树节点，结果=未执行任何操作");
            return;
        }
        if (item.IsPlaceholder)
        {
            UiLogger.Log("程序行为", "资源树节点双击：节点名称=" + item.Name
                + " 结果=未执行，该节点是加载占位节点");
            return;
        }
        if (item.IsDirectory)
        {
            item.IsExpanded = !item.IsExpanded;
            UiLogger.Log("程序行为", "资源树节点双击：节点名称=" + item.Name + " 节点类型=文件夹"
                + " 结果=成功，文件夹已" + (item.IsExpanded ? "展开" : "收起"));
            return;
        }
        bool alreadyOpen = false;
        foreach (IdeEditorTabViewModel editor in shell.Editors)
        {
            if (string.Equals(editor.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                alreadyOpen = true;
                break;
            }
        }
        shell.OpenFile(item.FullPath, item.ContentAccess, item.ConnectionName);
        UiLogger.Log("程序行为", "资源树节点双击：文件=" + item.FullPath
            + " 结果=" + (alreadyOpen
                ? "成功，该文件已在编辑器中打开，本次只是激活既有标签"
                : shell.SolutionStatusText));
    }

    // ======== 编辑器标签条 ========

    /// <summary>关闭编辑器标签：有未保存更改时先确认。</summary>
    private async void OnCloseEditorTabClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || sender is not Button button || button.Tag is not IdeEditorTabViewModel editor)
        {
            UiLogger.Log("程序行为", "按钮=关闭编辑器标签 被点击：集成开发环境视图模型未就绪或按钮没有绑定编辑器标签，结果=未关闭任何标签");
            return;
        }
        string fileName = editor.Title;
        bool dirty = editor.IsDirty;
        SavePromptChoice choice = SavePromptChoice.DontSave;
        if (dirty)
        {
            Window? owner = this.GetOwnerWindow();
            choice = await MessageDialog.ConfirmSaveAsync(owner, editor.Title);
            if (choice == SavePromptChoice.Cancel)
            {
                UiLogger.Log("程序行为", "按钮=关闭编辑器标签 被点击：文件=" + fileName
                    + " 结果=取消，用户在未保存提示中选择了取消，编辑器标签保持打开");
                return;
            }
            if (choice == SavePromptChoice.Save)
            {
                editor.Save();
            }
        }
        shell.CloseEditor(editor);
        string dirtyText = !dirty
            ? "无未保存更改"
            : choice == SavePromptChoice.Save ? "先保存再关闭" : "放弃未保存的更改直接关闭";
        UiLogger.Log("程序行为", "按钮=关闭编辑器标签 被点击：文件=" + fileName + " 未保存处理=" + dirtyText
            + " 结果=成功，编辑器标签已关闭");
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
            UiLogger.Log("程序行为", "按键（查找输入框）：集成开发环境视图模型未就绪，结果=未处理该按键");
            return;
        }
        if (e.Key == Key.Enter)
        {
            bool found = shell.FindNext(true);
            if (found)
            {
                shell.RequestApplyFindSelection();
            }
            UiLogger.Log("程序行为", "按键=回车（查找输入框）：查找内容=" + shell.FindText + " 查找方向=向下"
                + " 结果=" + (found ? "成功，已选中下一处匹配" : "失败，" + shell.FindStatusText));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            shell.ShowFindPanel = false;
            UiLogger.Log("程序行为", "按键=退出键（查找输入框）：结果=成功，查找面板已关闭");
            e.Handled = true;
        }
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=下一个 被点击：集成开发环境视图模型未就绪，结果=未执行查找");
            return;
        }
        bool found = shell.FindNext(true);
        if (found)
        {
            shell.RequestApplyFindSelection();
        }
        UiLogger.Log("程序行为", "按钮=下一个 被点击：查找内容=" + shell.FindText + " 查找方向=向下"
            + " 结果=" + (found ? "成功，已选中下一处匹配" : "失败，" + shell.FindStatusText));
    }

    private void OnFindPreviousClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=上一个 被点击：集成开发环境视图模型未就绪，结果=未执行查找");
            return;
        }
        bool found = shell.FindNext(false);
        if (found)
        {
            shell.RequestApplyFindSelection();
        }
        UiLogger.Log("程序行为", "按钮=上一个 被点击：查找内容=" + shell.FindText + " 查找方向=向上"
            + " 结果=" + (found ? "成功，已选中上一处匹配" : "失败，" + shell.FindStatusText));
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=替换 被点击：集成开发环境视图模型未就绪，结果=未执行替换");
            return;
        }
        if (shell.SelectedEditor == null || string.IsNullOrWhiteSpace(shell.FindText))
        {
            UiLogger.Log("程序行为", "按钮=替换 被点击：查找内容=（空白）或没有打开的编辑器，结果=未执行替换");
            return;
        }
        if (shell.SelectedEditor.IsReadOnly)
        {
            UiLogger.Log("程序行为", "按钮=替换 被点击：文件=" + shell.SelectedEditor.Title
                + " 结果=未执行，该文件为只读");
            return;
        }
        string keyword = shell.FindText;
        string replacement = shell.ReplaceText;
        bool hadSelection = shell.FindSelectionStart >= 0 && shell.FindSelectionLength > 0;
        shell.ReplaceCurrent();
        UiLogger.Log("程序行为", "按钮=替换 被点击：查找内容=" + keyword + " 替换为=" + replacement
            + " 结果=" + (hadSelection
                ? shell.FindStatusText
                : "没有选中的命中，已先定位到下一处匹配（" + shell.FindStatusText + "）"));
    }

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=全部替换 被点击：集成开发环境视图模型未就绪，结果=未执行替换");
            return;
        }
        if (shell.SelectedEditor == null || string.IsNullOrWhiteSpace(shell.FindText))
        {
            UiLogger.Log("程序行为", "按钮=全部替换 被点击：查找内容=（空白）或没有打开的编辑器，结果=未执行替换");
            return;
        }
        if (shell.SelectedEditor.IsReadOnly)
        {
            UiLogger.Log("程序行为", "按钮=全部替换 被点击：文件=" + shell.SelectedEditor.Title
                + " 结果=未执行，该文件为只读");
            return;
        }
        string keyword = shell.FindText;
        string replacement = shell.ReplaceText;
        shell.ReplaceAll();
        UiLogger.Log("程序行为", "按钮=全部替换 被点击：查找内容=" + keyword + " 替换为=" + replacement
            + " 结果=" + shell.FindStatusText);
    }

    /// <summary>在工作区范围内递归搜索（结果进入下方列表）。</summary>
    private void OnSearchWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按钮=在工作区搜索 被点击：集成开发环境视图模型未就绪，结果=未执行搜索");
            return;
        }
        string keyword = shell.FindText;
        string root = shell.WorkspaceRoot;
        shell.SearchWorkspace();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            UiLogger.Log("程序行为", "按钮=在工作区搜索 被点击：查找内容=（空白） 结果=未执行搜索，查找内容不能为空");
            return;
        }
        UiLogger.Log("程序行为", "按钮=在工作区搜索 被点击：查找内容=" + keyword
            + " 工作区根目录=" + (string.IsNullOrEmpty(root) ? "（未设置）" : root)
            + " 结果=成功，已启动后台搜索，命中结果将写入查找结果面板");
    }

    /// <summary>查找结果被选中：打开对应文件并定位到该行。</summary>
    private void OnFindResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "查找结果列表选中变化：集成开发环境视图模型未就绪，结果=未打开文件");
            return;
        }
        if (sender is not ListBox list || list.SelectedItem is not FindResultItem item)
        {
            // 打开结果后代码会把选中项置空并再次触发本事件，这一步由程序发起，不记录日志
            return;
        }
        shell.OpenFindResult(item);
        list.SelectedItem = null;
        UiLogger.Log("程序行为", "查找结果列表选中变化：文件=" + item.FilePath + " 行号=" + item.LineNumber
            + " 结果=成功，已打开该文件并定位到该行");
    }

    // ======== 快速打开 ========

    private void OnQuickOpenKeyDown(object? sender, KeyEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null)
        {
            UiLogger.Log("程序行为", "按键（快速打开输入框）：集成开发环境视图模型未就绪，结果=未处理该按键");
            return;
        }
        if (e.Key == Key.Escape)
        {
            shell.ShowQuickOpen = false;
            e.Handled = true;
            UiLogger.Log("程序行为", "按键=退出键（快速打开输入框）：结果=成功，快速打开浮层已关闭");
        }
        else if (e.Key == Key.Enter)
        {
            if (this.QuickOpenList.ItemCount > 0)
            {
                QuickOpenItem item = (QuickOpenItem)this.QuickOpenList.Items[0]!;
                shell.CommitQuickOpen(item);
                e.Handled = true;
                UiLogger.Log("程序行为", "按键=回车（快速打开输入框）：文件=" + item.FilePath
                    + " 结果=成功，已打开列表第一项并关闭快速打开浮层");
            }
            else
            {
                e.Handled = true;
                UiLogger.Log("程序行为", "按键=回车（快速打开输入框）：结果=未打开文件，列表中没有可打开的文件");
            }
        }
        else if (e.Key == Key.Down && this.QuickOpenList.ItemCount > 0)
        {
            this.QuickOpenList.SelectedIndex = 0;
            this.QuickOpenList.Focus();
            e.Handled = true;
            UiLogger.Log("程序行为", "按键=向下键（快速打开输入框）：结果=成功，已选中列表第一项并把焦点移到列表");
        }
    }

    /// <summary>快速打开列表双击：打开该项。</summary>
    private void OnQuickOpenDoubleTapped(object? sender, TappedEventArgs e)
    {
        IdeShellViewModel? shell = this.Shell;
        if (shell == null || sender is not Control control || control.DataContext is not QuickOpenItem item)
        {
            UiLogger.Log("程序行为", "快速打开列表双击：集成开发环境视图模型未就绪或触发源没有绑定快速打开项，结果=未打开文件");
            return;
        }
        shell.CommitQuickOpen(item);
        UiLogger.Log("程序行为", "快速打开列表双击：文件=" + item.FilePath
            + " 结果=成功，已打开该文件并关闭快速打开浮层");
    }
}
