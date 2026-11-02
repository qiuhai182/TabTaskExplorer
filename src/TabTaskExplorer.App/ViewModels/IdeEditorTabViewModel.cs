using System;
using System.ComponentModel;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// IDE 编辑器标签：一个标签对应一个打开的文件（本机或远程），
/// 负责脏标记、保存回写、编码显示与查找替换状态。
/// </summary>
public class IdeEditorTabViewModel : INotifyPropertyChanged
{
    private readonly IEditorFileAccess fileAccess;
    private string text = string.Empty;
    private string savedText = string.Empty;
    private bool saving;
    private string statusText = string.Empty;
    private int caretLine = 1;
    private int caretColumn = 1;
    private int selectionLength;
    private bool readOnly;
    private string encodingName = "UTF-8";

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>保存完成事件（供资源树 / 主窗口刷新）。</summary>
    public event EventHandler? Saved;

    /// <summary>标签标题：文件名。</summary>
    public string Title
    {
        get { return this.fileAccess.FileName; }
    }

    /// <summary>标签显示标题：文件名，末尾带未保存圆点。</summary>
    public string DisplayTitle
    {
        get { return this.IsDirty ? this.Title + " ●" : this.Title; }
    }

    /// <summary>标签提示：完整位置。</summary>
    public string ToolTipText
    {
        get { return this.fileAccess.FilePath; }
    }

    /// <summary>是否为远程文件标签。</summary>
    public bool IsRemote
    {
        get { return this.fileAccess.IsRemote; }
    }

    /// <summary>文件路径。</summary>
    public string FilePath
    {
        get { return this.fileAccess.FilePath; }
    }

    /// <summary>位置显示文字（所在目录或「连接名:目录」）。</summary>
    public string LocationText
    {
        get { return this.fileAccess.LocationText; }
    }

    /// <summary>标签图标分类键。</summary>
    public string IconCategory
    {
        get { return FileIconCatalog.GetCategoryForName(this.fileAccess.FileName); }
    }

    /// <summary>标签图标强调色。</summary>
    public string IconColor
    {
        get { return FileIconCatalog.GetAccent(this.IconCategory); }
    }

    /// <summary>编辑中的文本内容。</summary>
    public string Text
    {
        get { return this.text; }
        set
        {
            if (this.text == value)
            {
                return;
            }
            this.text = value;
            this.RaisePropertyChanged(nameof(this.Text));
            this.RaisePropertyChanged(nameof(this.IsDirty));
            this.RaisePropertyChanged(nameof(this.DisplayTitle));
            this.RaisePropertyChanged(nameof(this.HasText));
            this.RefreshLineCount();
        }
    }

    /// <summary>内容是否未保存。</summary>
    public bool IsDirty
    {
        get { return this.text != this.savedText; }
    }

    /// <summary>是否有内容（供空状态提示使用）。</summary>
    public bool HasText
    {
        get { return this.text.Length > 0; }
    }

    /// <summary>是否只读（二进制 / 超大文件）。</summary>
    public bool IsReadOnly
    {
        get { return this.readOnly; }
        private set
        {
            this.readOnly = value;
            this.RaisePropertyChanged(nameof(this.IsReadOnly));
        }
    }

    /// <summary>探测到的编码名称。</summary>
    public string EncodingName
    {
        get { return this.encodingName; }
        private set
        {
            this.encodingName = value;
            this.RaisePropertyChanged(nameof(this.EncodingName));
        }
    }

    /// <summary>是否正在保存。</summary>
    public bool Saving
    {
        get { return this.saving; }
        private set
        {
            this.saving = value;
            this.RaisePropertyChanged(nameof(this.Saving));
        }
    }

    /// <summary>状态提示文字。</summary>
    public string StatusText
    {
        get { return this.statusText; }
        private set
        {
            this.statusText = value;
            this.RaisePropertyChanged(nameof(this.StatusText));
        }
    }

    /// <summary>光标所在行（1 基）。</summary>
    public int CaretLine
    {
        get { return this.caretLine; }
        private set
        {
            this.caretLine = value;
            this.RaisePropertyChanged(nameof(this.CaretLine));
            this.RaisePropertyChanged(nameof(this.CaretText));
        }
    }

    /// <summary>光标所在列（1 基）。</summary>
    public int CaretColumn
    {
        get { return this.caretColumn; }
        private set
        {
            this.caretColumn = value;
            this.RaisePropertyChanged(nameof(this.CaretColumn));
            this.RaisePropertyChanged(nameof(this.CaretText));
        }
    }

    /// <summary>状态栏「行:列」显示文字。</summary>
    public string CaretText
    {
        get { return "行 " + this.caretLine + "，列 " + this.caretColumn; }
    }

    /// <summary>总行数。</summary>
    public int LineCount { get; private set; } = 1;

    /// <summary>构建编辑器标签（读取内容由调用方在后台线程预先完成）。</summary>
    public IdeEditorTabViewModel(IEditorFileAccess fileAccess, TextFileContent content)
    {
        this.fileAccess = fileAccess;
        this.EncodingName = content.EncodingName;
        if (content.IsBinary)
        {
            this.text = string.Empty;
            this.savedText = string.Empty;
            this.IsReadOnly = true;
            this.StatusText = "二进制文件，无法按文本编辑";
        }
        else if (content.IsTooLarge)
        {
            this.text = string.Empty;
            this.savedText = string.Empty;
            this.IsReadOnly = true;
            this.StatusText = "文件超过 5MB，已拒绝按文本打开";
        }
        else
        {
            this.text = content.Text;
            this.savedText = content.Text;
        }
        this.RefreshLineCount();
    }

    /// <summary>后台读取内容；失败抛出异常由调用方提示。</summary>
    public static TextFileContent ReadContent(IEditorFileAccess fileAccess)
    {
        return fileAccess.Read();
    }

    /// <summary>把编辑内容写回（后台线程执行）。</summary>
    public async void Save()
    {
        if (this.Saving || this.IsReadOnly || !this.IsDirty)
        {
            return;
        }
        this.Saving = true;
        this.StatusText = "正在保存……";
        string snapshot = this.text;
        string encoding = this.encodingName;
        try
        {
            await System.Threading.Tasks.Task.Run(delegate
            {
                this.fileAccess.Write(snapshot, encoding);
            });
            this.savedText = snapshot;
            this.StatusText = "已保存：" + DateTime.Now.ToString("HH:mm:ss");
            this.RaisePropertyChanged(nameof(this.IsDirty));
            this.RaisePropertyChanged(nameof(this.DisplayTitle));
            EventHandler? handler = this.Saved;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
        catch (Exception error)
        {
            this.StatusText = "保存失败：" + error.Message;
        }
        finally
        {
            this.Saving = false;
        }
    }

    /// <summary>更新光标位置（界面在选区变化时回传）。</summary>
    public void UpdateCaret(int line, int column, int selectionLength)
    {
        this.CaretLine = line;
        this.CaretColumn = column;
        this.selectionLength = selectionLength;
    }

    /// <summary>当前选中字符数。</summary>
    public int SelectionLength
    {
        get { return this.selectionLength; }
    }

    /// <summary>由界面把选中文字替换为新文字（供查找替换使用）。</summary>
    public void ReplaceSelection(string replacement)
    {
        if (this.IsReadOnly || this.selectionLength <= 0)
        {
            return;
        }
        this.StatusText = "已替换 " + this.selectionLength + " 个字符";
    }

    /// <summary>重新统计行数并通知界面。</summary>
    private void RefreshLineCount()
    {
        int count = 1;
        for (int index = 0; index < this.text.Length; index++)
        {
            if (this.text[index] == '\n')
            {
                count++;
            }
        }
        this.LineCount = count;
        this.RaisePropertyChanged(nameof(this.LineCount));
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
