using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>IDE 底部面板中的一个查找结果条目。</summary>
public class FindResultItem
{
    /// <summary>文件名。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件完整路径。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>行号（1 基）。</summary>
    public int LineNumber { get; set; }

    /// <summary>该行文本（已裁剪）。</summary>
    public string LineText { get; set; } = string.Empty;

    /// <summary>结果摘要文字。</summary>
    public string Summary
    {
        get { return this.FileName + "  行 " + this.LineNumber; }
    }
}

/// <summary>IDE 快速打开（Ctrl+P）列表项。</summary>
public class QuickOpenItem
{
    /// <summary>文件名。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件完整路径。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>所在目录显示文字。</summary>
    public string DirectoryText { get; set; } = string.Empty;
}

/// <summary>
/// IDE 外壳视图模型：工作区资源树 + 编辑器标签集合 + 当前编辑器 + 查找 / 输出面板。
/// 与资源管理器模式并存，切换仅改可见性，标签会话各自保持。
/// </summary>
public class IdeShellViewModel : INotifyPropertyChanged
{
    private static readonly string[] TextFileExtensions =
    {
        ".cs", ".txt", ".md", ".json", ".xml", ".config", ".csproj", ".sln", ".props", ".targets",
        ".js", ".ts", ".css", ".html", ".htm", ".py", ".java", ".c", ".cpp", ".h", ".hpp", ".go",
        ".rs", ".sh", ".bat", ".cmd", ".ps1", ".ini", ".yml", ".yaml", ".sql", ".log", ".gitignore",
        ".editorconfig", ".axaml", ".xaml", ".csv", ".tsv", ".toml", ".env", ".gradle", ".kt",
    };

    private readonly AppSettings settings;
    private readonly IServiceProvider? services;
    private string workspaceRoot = string.Empty;
    private IFileSystemProvider workspaceProvider;
    private IFileContentAccess? workspaceContentAccess;
    private string workspaceConnectionName = string.Empty;
    private IdeEditorTabViewModel? selectedEditor;
    private bool showFindPanel;
    private string findText = string.Empty;
    private string replaceText = string.Empty;
    private string findStatusText = string.Empty;
    private string solutionStatusText = "就绪";
    private bool isWorkspaceLoading;
    private string quickOpenText = string.Empty;
    private bool showQuickOpen;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>打开文件请求转发（资源树双击时由视图调用）。</summary>
    public event EventHandler<string>? StatusMessage;

    /// <summary>应用设置（字体、字号、Tab 宽度等由视图读取）。</summary>
    public AppSettings Settings
    {
        get { return this.settings; }
    }

    /// <summary>工作区根路径。</summary>
    public string WorkspaceRoot
    {
        get { return this.workspaceRoot; }
        private set
        {
            this.workspaceRoot = value;
            this.RaisePropertyChanged(nameof(this.WorkspaceRoot));
            this.RaisePropertyChanged(nameof(this.WorkspaceRootText));
        }
    }

    /// <summary>工作区根路径显示文字（远程加连接名前缀）。</summary>
    public string WorkspaceRootText
    {
        get
        {
            if (this.workspaceConnectionName.Length == 0)
            {
                return this.workspaceRoot;
            }
            return this.workspaceConnectionName + ":" + this.workspaceRoot;
        }
    }

    /// <summary>工作区资源树根节点集合。</summary>
    public ObservableCollection<FileTreeItemViewModel> RootItems { get; } = new ObservableCollection<FileTreeItemViewModel>();

    /// <summary>已打开的编辑器标签。</summary>
    public ObservableCollection<IdeEditorTabViewModel> Editors { get; } = new ObservableCollection<IdeEditorTabViewModel>();

    /// <summary>当前激活的编辑器标签。</summary>
    public IdeEditorTabViewModel? SelectedEditor
    {
        get { return this.selectedEditor; }
        set
        {
            this.selectedEditor = value;
            this.RaisePropertyChanged(nameof(this.SelectedEditor));
            this.RaisePropertyChanged(nameof(this.HasActiveEditor));
            this.RaisePropertyChanged(nameof(this.HasNoActiveEditor));
        }
    }

    /// <summary>是否存在激活的编辑器。</summary>
    public bool HasActiveEditor
    {
        get { return this.selectedEditor != null; }
    }

    /// <summary>是否没有激活的编辑器（供空状态提示绑定）。</summary>
    public bool HasNoActiveEditor
    {
        get { return this.selectedEditor == null; }
    }

    /// <summary>是否显示底部查找面板。</summary>
    public bool ShowFindPanel
    {
        get { return this.showFindPanel; }
        set
        {
            this.showFindPanel = value;
            this.RaisePropertyChanged(nameof(this.ShowFindPanel));
        }
    }

    /// <summary>查找关键字。</summary>
    public string FindText
    {
        get { return this.findText; }
        set
        {
            this.findText = value;
            this.RaisePropertyChanged(nameof(this.FindText));
        }
    }

    /// <summary>替换文字。</summary>
    public string ReplaceText
    {
        get { return this.replaceText; }
        set
        {
            this.replaceText = value;
            this.RaisePropertyChanged(nameof(this.ReplaceText));
        }
    }

    /// <summary>查找状态提示。</summary>
    public string FindStatusText
    {
        get { return this.findStatusText; }
        private set
        {
            this.findStatusText = value;
            this.RaisePropertyChanged(nameof(this.FindStatusText));
        }
    }

    /// <summary>输出面板状态文字。</summary>
    public string SolutionStatusText
    {
        get { return this.solutionStatusText; }
        private set
        {
            this.solutionStatusText = value;
            this.RaisePropertyChanged(nameof(this.SolutionStatusText));
        }
    }

    /// <summary>工作区是否正在加载。</summary>
    public bool IsWorkspaceLoading
    {
        get { return this.isWorkspaceLoading; }
        private set
        {
            this.isWorkspaceLoading = value;
            this.RaisePropertyChanged(nameof(this.IsWorkspaceLoading));
        }
    }

    /// <summary>查找结果集合（工作区范围内搜索）。</summary>
    public ObservableCollection<FindResultItem> FindResults { get; } = new ObservableCollection<FindResultItem>();

    /// <summary>快速打开候选集合（Ctrl+P）。</summary>
    public ObservableCollection<QuickOpenItem> QuickOpenItems { get; } = new ObservableCollection<QuickOpenItem>();

    /// <summary>快速打开关键字。</summary>
    public string QuickOpenText
    {
        get { return this.quickOpenText; }
        set
        {
            this.quickOpenText = value;
            this.ApplyQuickOpenFilter();
            this.RaisePropertyChanged(nameof(this.QuickOpenText));
        }
    }

    /// <summary>是否显示快速打开浮层。</summary>
    public bool ShowQuickOpen
    {
        get { return this.showQuickOpen; }
        set
        {
            this.showQuickOpen = value;
            this.RaisePropertyChanged(nameof(this.ShowQuickOpen));
        }
    }

    /// <summary>构建 IDE 外壳；工作区初始为空（由 SetWorkspace 注入）。</summary>
    public IdeShellViewModel(AppSettings settings, IServiceProvider? services)
    {
        this.settings = settings;
        this.services = services;
        this.workspaceProvider = new NullFileSystemProvider();
    }

    /// <summary>
    /// 设置工作区：注入文件系统提供者与远程内容访问能力，随后重建资源树。
    /// 本机调用时 contentAccess 传 null（走内核 ITextFileService），远程传入会话的内容访问能力。
    /// </summary>
    public void SetWorkspace(string rootPath, IFileSystemProvider provider, IFileContentAccess? contentAccess, string connectionName)
    {
        this.workspaceProvider = provider;
        this.workspaceContentAccess = contentAccess;
        this.workspaceConnectionName = connectionName ?? string.Empty;
        this.WorkspaceRoot = contentAccess != null ? RemotePathUtility.Normalize(rootPath) : (rootPath ?? string.Empty);
        this.ReloadWorkspace();
    }

    /// <summary>重建资源树根节点（后台枚举根目录）。</summary>
    public void ReloadWorkspace()
    {
        if (this.workspaceProvider is NullFileSystemProvider || string.IsNullOrEmpty(this.workspaceRoot))
        {
            this.RootItems.Clear();
            return;
        }
        this.IsWorkspaceLoading = true;
        string root = this.workspaceRoot;
        IFileSystemProvider provider = this.workspaceProvider;
        bool includeHidden = this.settings.ShowHiddenFiles;
        bool remote = this.workspaceContentAccess != null;
        IFileContentAccess? access = this.workspaceContentAccess;
        string connection = this.workspaceConnectionName;

        Task.Run(delegate
        {
            List<FileTreeItemViewModel> loaded = new List<FileTreeItemViewModel>();
            string? errorText = null;
            try
            {
                DirectoryListingOptions options = new DirectoryListingOptions();
                options.ShowHiddenFiles = includeHidden;
                foreach (FileEntry entry in provider.ListDirectory(root, options))
                {
                    loaded.Add(new FileTreeItemViewModel(provider, includeHidden, entry, remote, access, connection));
                }
            }
            catch (Exception error)
            {
                errorText = error.Message;
            }
            Dispatcher.UIThread.Post(delegate { this.ApplyWorkspace(loaded, errorText); });
        });
    }

    /// <summary>在界面线程应用资源树根节点。</summary>
    private void ApplyWorkspace(List<FileTreeItemViewModel> loaded, string? errorText)
    {
        loaded.Sort(new Comparison<FileTreeItemViewModel>(CompareItems));
        this.RootItems.Clear();
        foreach (FileTreeItemViewModel item in loaded)
        {
            this.RootItems.Add(item);
        }
        this.IsWorkspaceLoading = false;
        if (errorText != null)
        {
            this.SolutionStatusText = "无法读取工作区：" + errorText;
        }
        else
        {
            this.SolutionStatusText = "已加载 " + loaded.Count + " 个根条目";
        }
    }

    /// <summary>根节点排序：文件夹在前。</summary>
    private static int CompareItems(FileTreeItemViewModel left, FileTreeItemViewModel right)
    {
        if (left.IsDirectory != right.IsDirectory)
        {
            return left.IsDirectory ? -1 : 1;
        }
        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>构建设计 <see cref="IEditorFileAccess"/>：本机走内核文本服务，远程走内容访问能力。</summary>
    public IEditorFileAccess BuildAccess(string filePath, IFileContentAccess? contentAccess, string connectionName)
    {
        if (contentAccess != null)
        {
            return new RemoteEditorFileAccess(contentAccess, filePath, connectionName);
        }
        ITextFileService textService = ResolveTextService();
        return new LocalEditorFileAccess(textService, filePath);
    }

    /// <summary>解析内核文本服务实例。</summary>
    private ITextFileService ResolveTextService()
    {
        if (this.services != null)
        {
            ITextFileService? registered = this.services.GetService(typeof(ITextFileService)) as ITextFileService;
            if (registered != null)
            {
                return registered;
            }
        }
        return new DefaultTextFileService();
    }

    /// <summary>打开一个文件到编辑器标签（已打开则直接激活）。</summary>
    public void OpenFile(string filePath, IFileContentAccess? contentAccess, string connectionName)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }
        foreach (IdeEditorTabViewModel existing in this.Editors)
        {
            if (string.Equals(existing.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                this.SelectedEditor = existing;
                return;
            }
        }

        IEditorFileAccess access = this.BuildAccess(filePath, contentAccess, connectionName);
        // 按设置解析打开方式：优先系统默认程序，或优先内置编辑器（不支持的类型再交给系统默认程序）
        string priority = FileOpenPolicy.ResolvePriority(this.settings.FileOpenRules, this.settings.FileOpenDefaultPriority, access.FileName);
        if (priority == FileOpenPriority.System)
        {
            this.OpenWithSystem(filePath, access.IsRemote);
            return;
        }
        // 未显式声明该类型时，先按扩展名判断是否为文本类型，非文本直接交给系统默认程序
        bool explicitBuiltin = FileOpenPolicy.HasExplicitBuiltinRule(this.settings.FileOpenRules, access.FileName);
        if (!explicitBuiltin && !access.IsRemote && !IsLikelyTextFile(access.FileName))
        {
            this.OpenWithSystem(filePath, false);
            return;
        }

        try
        {
            TextFileContent content = IdeEditorTabViewModel.ReadContent(access);
            // 二进制或超出大小上限：不属于可编辑文本，交给系统默认程序
            if (!content.IsEditable)
            {
                this.OpenWithSystem(filePath, access.IsRemote);
                return;
            }
            IdeEditorTabViewModel editor = new IdeEditorTabViewModel(access, content);
            editor.Saved += this.OnEditorSaved;
            this.Editors.Add(editor);
            this.SelectedEditor = editor;
            this.SolutionStatusText = "已打开 " + editor.Title;
        }
        catch (Exception error)
        {
            this.SolutionStatusText = "打开失败：" + error.Message;
        }
    }

    /// <summary>按扩展名判断是否为可编辑文本文件。</summary>
    public static bool IsLikelyTextFile(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        if (extension.Length == 0)
        {
            return true;
        }
        foreach (string known in TextFileExtensions)
        {
            if (string.Equals(known, extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>用系统默认程序打开；远程文件无法在本机调起默认程序时只在状态栏提示。</summary>
    private void OpenWithSystem(string filePath, bool isRemote)
    {
        if (isRemote)
        {
            this.SolutionStatusText = "按当前设置该类型使用系统默认程序打开，远程文件请改为在设置中指定「优先内置编辑器」";
            return;
        }
        try
        {
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.FileName = filePath;
            startInfo.UseShellExecute = true;
            System.Diagnostics.Process.Start(startInfo);
            this.SolutionStatusText = "已用系统默认程序打开 " + Path.GetFileName(filePath);
        }
        catch (Exception error)
        {
            this.SolutionStatusText = "无法打开：" + error.Message;
        }
    }

    /// <summary>文件保存完成：刷新状态。</summary>
    private void OnEditorSaved(object? sender, EventArgs e)
    {
        if (sender is IdeEditorTabViewModel editor)
        {
            this.SolutionStatusText = "已保存 " + editor.Title;
        }
    }

    /// <summary>关闭编辑器标签；脏内容由调用方先确认。</summary>
    public void CloseEditor(IdeEditorTabViewModel editor)
    {
        int index = this.Editors.IndexOf(editor);
        if (index < 0)
        {
            return;
        }
        this.Editors.Remove(editor);
        if (this.selectedEditor == editor)
        {
            if (this.Editors.Count == 0)
            {
                this.SelectedEditor = null;
            }
            else
            {
                int nextIndex = index >= this.Editors.Count ? this.Editors.Count - 1 : index;
                this.SelectedEditor = this.Editors[nextIndex];
            }
        }
    }

    /// <summary>保存当前编辑器。</summary>
    public void SaveActive()
    {
        this.selectedEditor?.Save();
    }

    /// <summary>保存全部编辑器。</summary>
    public void SaveAll()
    {
        foreach (IdeEditorTabViewModel editor in this.Editors)
        {
            editor.Save();
        }
    }

    /// <summary>是否存在未保存内容。</summary>
    public bool HasDirtyEditors()
    {
        foreach (IdeEditorTabViewModel editor in this.Editors)
        {
            if (editor.IsDirty)
            {
                return true;
            }
        }
        return false;
    }

    // ======== 查找替换（当前文件） ========

    /// <summary>在当前编辑器内查找下一个匹配并选中（由视图执行实际选中）。</summary>
    public bool FindNext(bool forward)
    {
        IdeEditorTabViewModel? editor = this.selectedEditor;
        if (editor == null || string.IsNullOrEmpty(this.findText))
        {
            this.FindStatusText = "请输入查找内容";
            return false;
        }
        string content = editor.Text;
        int start = forward ? this.lastFindIndex + 1 : this.lastFindIndex - 1;
        if (start < 0)
        {
            start = content.Length - 1;
        }
        if (start > content.Length)
        {
            start = 0;
        }
        int found = -1;
        if (forward)
        {
            found = content.IndexOf(this.findText, Math.Min(start, content.Length), StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                found = content.IndexOf(this.findText, 0, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            int searchFrom = Math.Min(start, content.Length - 1);
            found = searchFrom < 0 ? -1 : content.LastIndexOf(this.findText, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                found = content.LastIndexOf(this.findText, StringComparison.OrdinalIgnoreCase);
            }
        }
        if (found < 0)
        {
            this.FindStatusText = "未找到「" + this.findText + "」";
            this.lastFindIndex = -1;
            return false;
        }
        this.lastFindIndex = found;
        this.FindStatusText = "找到于第 " + found + " 个字符";
        this.FindSelectionStart = found;
        this.FindSelectionLength = this.findText.Length;
        this.RaisePropertyChanged(nameof(this.FindSelectionStart));
        this.RaisePropertyChanged(nameof(this.FindSelectionLength));
        return true;
    }

    private int lastFindIndex = -1;

    /// <summary>查找命中起始位置（供视图设置选区）。</summary>
    public int FindSelectionStart { get; private set; } = -1;

    /// <summary>查找命中长度。</summary>
    public int FindSelectionLength { get; private set; }

    /// <summary>选区变化事件（供视图同步选中并滚动到可见）。</summary>
    public event EventHandler? FindSelectionChanged;

    /// <summary>通知视图应用查找选区。</summary>
    public void RequestApplyFindSelection()
    {
        EventHandler? handler = this.FindSelectionChanged;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
    }

    /// <summary>替换当前命中。</summary>
    public void ReplaceCurrent()
    {
        IdeEditorTabViewModel? editor = this.selectedEditor;
        if (editor == null || editor.IsReadOnly || string.IsNullOrEmpty(this.findText))
        {
            return;
        }
        if (this.FindSelectionStart < 0 || this.FindSelectionLength <= 0)
        {
            this.FindNext(true);
            return;
        }
        string content = editor.Text;
        if (this.FindSelectionStart + this.FindSelectionLength > content.Length)
        {
            return;
        }
        string replaced = content.Substring(0, this.FindSelectionStart) + this.replaceText
            + content.Substring(this.FindSelectionStart + this.FindSelectionLength);
        editor.Text = replaced;
        this.FindStatusText = "已替换 1 处";
        this.lastFindIndex = this.FindSelectionStart;
        this.FindSelectionStart = -1;
        this.FindSelectionLength = 0;
    }

    /// <summary>替换当前文件全部命中。</summary>
    public void ReplaceAll()
    {
        IdeEditorTabViewModel? editor = this.selectedEditor;
        if (editor == null || editor.IsReadOnly || string.IsNullOrEmpty(this.findText))
        {
            return;
        }
        string content = editor.Text;
        int count = 0;
        int index = content.IndexOf(this.findText, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            count++;
            content = content.Substring(0, index) + this.replaceText + content.Substring(index + this.findText.Length);
            index = content.IndexOf(this.findText, index + this.replaceText.Length, StringComparison.OrdinalIgnoreCase);
        }
        if (count > 0)
        {
            editor.Text = content;
            this.FindStatusText = "已替换 " + count + " 处";
        }
        else
        {
            this.FindStatusText = "未找到「" + this.findText + "」";
        }
    }

    // ======== 工作区搜索（输出 / 查找结果面板） ========

    /// <summary>在当前工作区各级目录中递归搜索关键字，结果写入查找结果面板。</summary>
    public async void SearchWorkspace()
    {
        if (string.IsNullOrEmpty(this.findText))
        {
            this.FindStatusText = "请输入查找内容";
            return;
        }
        this.ShowFindPanel = true;
        this.FindStatusText = "正在搜索……";
        this.FindResults.Clear();

        string root = this.workspaceRoot;
        IFileSystemProvider provider = this.workspaceProvider;
        bool includeHidden = this.settings.ShowHiddenFiles;
        string keyword = this.findText;
        bool remote = this.workspaceContentAccess != null;
        IFileContentAccess? access = this.workspaceContentAccess;

        List<FindResultItem> found = await Task.Run(delegate
        {
            return SearchWorkspaceCore(provider, root, keyword, includeHidden, remote, access);
        });

        foreach (FindResultItem item in found)
        {
            this.FindResults.Add(item);
        }
        this.FindStatusText = "工作区搜索完成，命中 " + found.Count + " 处";
        this.SolutionStatusText = this.FindStatusText;
    }

    /// <summary>后台递归搜索：本机走 ITextFileService，远程走内容访问能力。</summary>
    private static List<FindResultItem> SearchWorkspaceCore(
        IFileSystemProvider provider,
        string root,
        string keyword,
        bool includeHidden,
        bool remote,
        IFileContentAccess? contentAccess)
    {
        List<FindResultItem> results = new List<FindResultItem>();
        if (string.IsNullOrEmpty(root))
        {
            return results;
        }
        ITextFileService textService = new DefaultTextFileService();
        Stack<string> pending = new Stack<string>();
        pending.Push(root);
        int scannedFiles = 0;
        while (pending.Count > 0 && scannedFiles < 2000 && results.Count < 500)
        {
            string directory = pending.Pop();
            IReadOnlyList<FileEntry> entries;
            try
            {
                DirectoryListingOptions options = new DirectoryListingOptions();
                options.ShowHiddenFiles = includeHidden;
                entries = provider.ListDirectory(directory, options);
            }
            catch
            {
                continue;
            }
            foreach (FileEntry entry in entries)
            {
                if (entry.IsDirectory)
                {
                    pending.Push(entry.FullPath);
                    continue;
                }
                scannedFiles++;
                string text;
                try
                {
                    if (remote)
                    {
                        if (contentAccess == null || entry.LengthBytes > TextFileCodec.MaxTextFileBytes)
                        {
                            continue;
                        }
                        TextFileContent decoded = TextFileCodec.Decode(contentAccess.ReadAllBytes(entry.FullPath));
                        if (!decoded.IsEditable)
                        {
                            continue;
                        }
                        text = decoded.Text;
                    }
                    else
                    {
                        TextFileContent decoded = textService.Read(entry.FullPath);
                        if (!decoded.IsEditable)
                        {
                            continue;
                        }
                        text = decoded.Text;
                    }
                }
                catch
                {
                    continue;
                }
                AppendMatches(results, entry.Name, entry.FullPath, text, keyword);
            }
        }
        return results;
    }

    /// <summary>在文本中逐行查找关键字并追加结果。</summary>
    private static void AppendMatches(List<FindResultItem> results, string fileName, string filePath, string text, string keyword)
    {
        int lineNumber = 0;
        int lineStart = 0;
        for (int index = 0; index <= text.Length; index++)
        {
            if (index == text.Length || text[index] == '\n')
            {
                lineNumber++;
                string lineText = text.Substring(lineStart, index - lineStart).TrimEnd('\r');
                if (lineText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FindResultItem item = new FindResultItem();
                    item.FileName = fileName;
                    item.FilePath = filePath;
                    item.LineNumber = lineNumber;
                    item.LineText = lineText.Trim();
                    results.Add(item);
                    if (results.Count >= 500)
                    {
                        return;
                    }
                }
                lineStart = index + 1;
            }
        }
    }

    /// <summary>打开查找结果对应文件并定位到该行。</summary>
    public void OpenFindResult(FindResultItem item)
    {
        if (item == null)
        {
            return;
        }
        this.OpenFile(item.FilePath, this.workspaceContentAccess, this.workspaceConnectionName);
        this.PendingCaretLine = item.LineNumber;
        this.FindStatusText = "已定位 " + item.FileName + " 行 " + item.LineNumber;
    }

    /// <summary>待定位行号（打开编辑器后由视图消费一次）。</summary>
    public int PendingCaretLine { get; set; }

    // ======== 快速打开（Ctrl+P） ========

    private readonly List<QuickOpenItem> quickOpenAll = new List<QuickOpenItem>();

    /// <summary>打开快速打开浮层并后台收集工作区文件清单。</summary>
    public void OpenQuickOpen()
    {
        this.ShowQuickOpen = true;
        this.QuickOpenText = string.Empty;
        if (this.quickOpenAll.Count > 0)
        {
            this.ApplyQuickOpenFilter();
            return;
        }
        string root = this.workspaceRoot;
        IFileSystemProvider provider = this.workspaceProvider;
        bool includeHidden = this.settings.ShowHiddenFiles;
        if (string.IsNullOrEmpty(root))
        {
            return;
        }
        Task.Run(delegate
        {
            List<QuickOpenItem> collected = CollectWorkspaceFiles(provider, root, includeHidden);
            Dispatcher.UIThread.Post(delegate { this.ApplyQuickOpenAll(collected); });
        });
    }

    /// <summary>后台收集工作区文件清单（最多 5000 个）。</summary>
    private static List<QuickOpenItem> CollectWorkspaceFiles(IFileSystemProvider provider, string root, bool includeHidden)
    {
        List<QuickOpenItem> items = new List<QuickOpenItem>();
        Stack<string> pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0 && items.Count < 5000)
        {
            string directory = pending.Pop();
            IReadOnlyList<FileEntry> entries;
            try
            {
                DirectoryListingOptions options = new DirectoryListingOptions();
                options.ShowHiddenFiles = includeHidden;
                entries = provider.ListDirectory(directory, options);
            }
            catch
            {
                continue;
            }
            foreach (FileEntry entry in entries)
            {
                if (entry.IsDirectory)
                {
                    pending.Push(entry.FullPath);
                    continue;
                }
                QuickOpenItem item = new QuickOpenItem();
                item.FileName = entry.Name;
                item.FilePath = entry.FullPath;
                item.DirectoryText = directory;
                items.Add(item);
            }
        }
        return items;
    }

    /// <summary>在界面线程应用收集到的文件清单。</summary>
    private void ApplyQuickOpenAll(List<QuickOpenItem> collected)
    {
        this.quickOpenAll.Clear();
        this.quickOpenAll.AddRange(collected);
        this.ApplyQuickOpenFilter();
    }

    /// <summary>按关键字模糊过滤快速打开列表（子序列匹配）。</summary>
    private void ApplyQuickOpenFilter()
    {
        this.QuickOpenItems.Clear();
        string keyword = this.quickOpenText == null ? string.Empty : this.quickOpenText.Trim();
        int count = 0;
        foreach (QuickOpenItem item in this.quickOpenAll)
        {
            if (keyword.Length == 0 || IsSubsequenceMatch(item.FileName, keyword))
            {
                this.QuickOpenItems.Add(item);
                count++;
                if (count >= 200)
                {
                    break;
                }
            }
        }
    }

    /// <summary>子序列模糊匹配：关键字的字符按序出现在文件名中即命中。</summary>
    private static bool IsSubsequenceMatch(string name, string keyword)
    {
        int keyIndex = 0;
        for (int index = 0; index < name.Length && keyIndex < keyword.Length; index++)
        {
            if (char.ToLowerInvariant(name[index]) == char.ToLowerInvariant(keyword[keyIndex]))
            {
                keyIndex++;
            }
        }
        return keyIndex == keyword.Length;
    }

    /// <summary>快速打开选中项：关闭浮层并打开文件。</summary>
    public void CommitQuickOpen(QuickOpenItem item)
    {
        this.ShowQuickOpen = false;
        if (item == null)
        {
            return;
        }
        this.OpenFile(item.FilePath, this.workspaceContentAccess, this.workspaceConnectionName);
    }

    // ======== 网页端镜像（IDE 模式状态、编辑器文本与动作执行） ========

    /// <summary>构建网页端 IDE 模式镜像状态（资源树、编辑器、查找与快速打开）。</summary>
    public DebugIdeState BuildMirrorState()
    {
        DebugIdeState mirror = new DebugIdeState();
        mirror.WorkspaceRootText = this.WorkspaceRootText;
        mirror.SolutionStatusText = this.solutionStatusText;
        mirror.IsWorkspaceLoading = this.isWorkspaceLoading;
        mirror.ShowFindPanel = this.showFindPanel;
        mirror.FindText = this.findText;
        mirror.ReplaceText = this.replaceText;
        mirror.FindStatusText = this.findStatusText;
        mirror.FindSelectionStart = this.FindSelectionStart;
        mirror.FindSelectionLength = this.FindSelectionLength;
        mirror.ShowQuickOpen = this.showQuickOpen;
        mirror.QuickOpenText = this.quickOpenText;
        foreach (FileTreeItemViewModel item in this.RootItems)
        {
            IdeShellViewModel.AppendTreeItem(mirror.Tree, item, 0);
        }
        int editorIndex = 0;
        foreach (IdeEditorTabViewModel editor in this.Editors)
        {
            DebugIdeEditor editorMirror = new DebugIdeEditor();
            editorMirror.Index = editorIndex;
            editorMirror.DisplayTitle = editor.DisplayTitle;
            editorMirror.ToolTipText = editor.ToolTipText;
            editorMirror.IsDirty = editor.IsDirty;
            editorMirror.IconCategory = editor.IconCategory;
            editorMirror.IconColor = editor.IconColor;
            mirror.Editors.Add(editorMirror);
            if (object.ReferenceEquals(editor, this.selectedEditor))
            {
                mirror.ActiveEditorIndex = editorIndex;
                mirror.ActiveTextToken = DebugIdeState.ComputeTextToken(editor.Text);
                mirror.ActiveIsReadOnly = editor.IsReadOnly;
                mirror.ActiveEncoding = editor.EncodingName;
                mirror.ActiveLocation = editor.LocationText;
                mirror.ActiveLineCount = editor.LineCount;
            }
            editorIndex++;
        }
        foreach (QuickOpenItem item in this.QuickOpenItems)
        {
            DebugIdeQuickOpen openMirror = new DebugIdeQuickOpen();
            openMirror.FileName = item.FileName;
            openMirror.FilePath = item.FilePath;
            openMirror.DirectoryText = item.DirectoryText;
            mirror.QuickOpenItems.Add(openMirror);
        }
        foreach (FindResultItem item in this.FindResults)
        {
            DebugIdeFindResult resultMirror = new DebugIdeFindResult();
            resultMirror.Summary = item.Summary;
            resultMirror.LineText = item.LineText;
            resultMirror.FilePath = item.FilePath;
            resultMirror.LineNumber = item.LineNumber;
            mirror.FindResults.Add(resultMirror);
        }
        return mirror;
    }

    /// <summary>把资源树节点（含已展开的子节点）按可见顺序拍平。</summary>
    private static void AppendTreeItem(List<DebugIdeTreeNode> target, FileTreeItemViewModel item, int depth)
    {
        DebugIdeTreeNode node = new DebugIdeTreeNode();
        node.Depth = depth;
        node.Name = item.Name;
        node.FullPath = item.FullPath;
        node.IsDirectory = item.IsDirectory;
        node.IsExpanded = item.IsExpanded;
        node.IsPlaceholder = item.IsPlaceholder;
        node.IconCategory = item.IconCategory;
        node.IconColor = item.IconColor;
        target.Add(node);
        if (item.IsDirectory && item.IsExpanded)
        {
            foreach (FileTreeItemViewModel child in item.Children)
            {
                IdeShellViewModel.AppendTreeItem(target, child, depth + 1);
            }
        }
    }

    /// <summary>按编辑器下标读取当前文本，返回格式：令牌 + 换行 + 文本；下标非法返回空文本。</summary>
    public string ReadEditorText(int index)
    {
        if (index < 0 || index >= this.Editors.Count)
        {
            return string.Empty;
        }
        IdeEditorTabViewModel editor = this.Editors[index];
        return DebugIdeState.ComputeTextToken(editor.Text) + "\n" + editor.Text;
    }

    /// <summary>写入指定编辑器的文本（网页端输入回传，只读编辑器忽略）；返回新令牌，下标非法返回空文本。</summary>
    public string SetEditorText(int index, string text)
    {
        if (index < 0 || index >= this.Editors.Count)
        {
            return string.Empty;
        }
        IdeEditorTabViewModel editor = this.Editors[index];
        if (editor.IsReadOnly)
        {
            return DebugIdeState.ComputeTextToken(editor.Text);
        }
        editor.Text = text;
        return DebugIdeState.ComputeTextToken(editor.Text);
    }

    /// <summary>执行网页端 IDE 动作，返回给页面的提示文字，空文本表示无提示。</summary>
    public string ExecuteIdeAction(string action, string argument)
    {
        if (action == "ideopen")
        {
            this.OpenFile(argument, this.workspaceContentAccess, this.workspaceConnectionName);
            return string.Empty;
        }
        if (action == "idetree")
        {
            FileTreeItemViewModel? treeNode = this.FindTreeNode(this.RootItems, argument);
            if (treeNode == null || !treeNode.IsDirectory || treeNode.IsPlaceholder)
            {
                return "未找到可展开的目录：" + argument;
            }
            treeNode.IsExpanded = !treeNode.IsExpanded;
            return string.Empty;
        }
        if (action == "ideclose")
        {
            int closeIndex;
            if (!int.TryParse(argument, out closeIndex) || closeIndex < 0 || closeIndex >= this.Editors.Count)
            {
                return "要关闭的编辑器不存在";
            }
            IdeEditorTabViewModel closing = this.Editors[closeIndex];
            this.CloseEditor(closing);
            return "已关闭 " + closing.Title;
        }
        if (action == "ideselect")
        {
            int selectIndex;
            if (!int.TryParse(argument, out selectIndex) || selectIndex < 0 || selectIndex >= this.Editors.Count)
            {
                return "要切换的编辑器不存在";
            }
            this.SelectedEditor = this.Editors[selectIndex];
            return string.Empty;
        }
        if (action == "idetext")
        {
            int split = argument.IndexOf('\n');
            if (split <= 0) { return "缺少编辑器下标"; }
            int textIndex;
            if (!int.TryParse(argument.Substring(0, split), out textIndex)) { return "编辑器下标无效"; }
            this.SetEditorText(textIndex, argument.Substring(split + 1));
            return string.Empty;
        }
        if (action == "idesave")
        {
            IdeEditorTabViewModel? active = this.selectedEditor;
            // 网页端关闭指定标签时可带编辑器下标：按下标保存，不带下标则保存当前激活编辑器
            int saveIndex;
            if (int.TryParse(argument, out saveIndex) && saveIndex >= 0 && saveIndex < this.Editors.Count)
            {
                active = this.Editors[saveIndex];
            }
            if (active == null) { return "没有打开的编辑器"; }
            if (active.IsReadOnly) { return "该文件为只读，无法保存"; }
            if (!active.IsDirty) { return "没有未保存的更改"; }
            active.Save();
            return "正在保存 " + active.Title + "……";
        }
        if (action == "idesaveall")
        {
            int dirtyCount = 0;
            foreach (IdeEditorTabViewModel editor in this.Editors)
            {
                if (editor.IsDirty && !editor.IsReadOnly)
                {
                    dirtyCount++;
                    editor.Save();
                }
            }
            return dirtyCount == 0 ? "没有未保存的更改" : "正在保存 " + dirtyCount + " 个文件……";
        }
        if (action == "idefindpanel")
        {
            this.ShowFindPanel = argument != "0";
            return string.Empty;
        }
        if (action == "idefindtext")
        {
            this.FindText = argument;
            return string.Empty;
        }
        if (action == "idereplacetext")
        {
            this.ReplaceText = argument;
            return string.Empty;
        }
        if (action == "idefindnext")
        {
            bool found = this.FindNext(argument != "0");
            if (found)
            {
                this.RequestApplyFindSelection();
            }
            return this.findStatusText;
        }
        if (action == "idereplace")
        {
            if (this.selectedEditor == null) { return "没有打开的编辑器"; }
            if (this.selectedEditor.IsReadOnly) { return "该文件为只读，无法替换"; }
            this.ReplaceCurrent();
            return this.findStatusText;
        }
        if (action == "idereplaceall")
        {
            if (this.selectedEditor == null) { return "没有打开的编辑器"; }
            if (this.selectedEditor.IsReadOnly) { return "该文件为只读，无法替换"; }
            this.ReplaceAll();
            return this.findStatusText;
        }
        if (action == "idesearchws")
        {
            this.SearchWorkspace();
            return string.Empty;
        }
        if (action == "idequickopen")
        {
            this.OpenQuickOpen();
            return string.Empty;
        }
        if (action == "idequicktext")
        {
            this.QuickOpenText = argument;
            return string.Empty;
        }
        if (action == "idequickclose")
        {
            this.ShowQuickOpen = false;
            return string.Empty;
        }
        if (action == "idequickcommit")
        {
            foreach (QuickOpenItem item in this.QuickOpenItems)
            {
                if (string.Equals(item.FilePath, argument, StringComparison.OrdinalIgnoreCase))
                {
                    this.CommitQuickOpen(item);
                    return string.Empty;
                }
            }
            this.ShowQuickOpen = false;
            this.OpenFile(argument, this.workspaceContentAccess, this.workspaceConnectionName);
            return string.Empty;
        }
        if (action == "iderefresh")
        {
            this.ReloadWorkspace();
            return "正在重新加载工作区……";
        }
        if (action == "idefindresult")
        {
            int lineSplit = argument.IndexOf('\n');
            if (lineSplit <= 0) { return "缺少结果路径"; }
            string resultPath = argument.Substring(0, lineSplit);
            int resultLine;
            if (!int.TryParse(argument.Substring(lineSplit + 1), out resultLine)) { resultLine = 0; }
            foreach (FindResultItem item in this.FindResults)
            {
                if (string.Equals(item.FilePath, resultPath, StringComparison.OrdinalIgnoreCase) && item.LineNumber == resultLine)
                {
                    this.OpenFindResult(item);
                    return this.findStatusText;
                }
            }
            this.OpenFile(resultPath, this.workspaceContentAccess, this.workspaceConnectionName);
            this.PendingCaretLine = resultLine;
            return "已打开 " + resultPath;
        }
        return "未知动作：" + action;
    }

    /// <summary>在资源树中按完整路径查找节点（含子节点）。</summary>
    private FileTreeItemViewModel? FindTreeNode(IEnumerable<FileTreeItemViewModel> items, string path)
    {
        foreach (FileTreeItemViewModel item in items)
        {
            if (string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
            if (item.IsDirectory && item.IsExpanded)
            {
                FileTreeItemViewModel? child = this.FindTreeNode(item.Children, path);
                if (child != null)
                {
                    return child;
                }
            }
        }
        return null;
    }

    /// <summary>触发属性变更通知。</summary>
    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
