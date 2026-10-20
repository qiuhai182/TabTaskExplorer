using System.ComponentModel;
using System.Text;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 远程文件在线编辑标签：读取远程文本文件编辑，保存时整体写回（参考 sshtools 的 Ctrl+S 回传）。
/// </summary>
public class EditorTabViewModel : INotifyPropertyChanged
{
    private readonly IFileContentAccess contentAccess;
    private readonly Encoding encoding;
    private string text = string.Empty;
    private string savedText = string.Empty;
    private bool saving;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>保存完成事件（参数为远端路径）。</summary>
    public event EventHandler<string>? Saved;

    /// <summary>标签页标题。</summary>
    public string Title { get; }

    /// <summary>关联的连接配置编号。</summary>
    public string AttachedProfileId { get; set; } = string.Empty;

    /// <summary>远端文件完整路径。</summary>
    public string RemotePath { get; }

    /// <summary>编辑中的文本内容。</summary>
    public string Text
    {
        get { return this.text; }
        set
        {
            this.text = value;
            this.RaisePropertyChanged(nameof(this.Text));
            this.RaisePropertyChanged(nameof(this.IsDirty));
        }
    }

    /// <summary>内容是否未保存。</summary>
    public bool IsDirty
    {
        get { return this.text != this.savedText; }
    }

    /// <summary>是否正在保存。</summary>
    public bool Saving
    {
        get { return this.saving; }
        private set { this.saving = value; this.RaisePropertyChanged(nameof(this.Saving)); }
    }

    /// <summary>状态提示文本（保存结果等）。</summary>
    public string StatusText
    {
        get { return this.statusText; }
        private set { this.statusText = value; this.RaisePropertyChanged(nameof(this.StatusText)); }
    }

    /// <summary>状态文本私有字段。</summary>
    private string statusText = string.Empty;

    /// <summary>使用内容访问能力与远端路径创建编辑器标签。</summary>
    public EditorTabViewModel(IFileContentAccess contentAccess, string remotePath, string initialText, string profileId)
    {
        this.contentAccess = contentAccess;
        this.RemotePath = remotePath;
        this.Title = RemotePathUtility.GetFileName(remotePath) + " - 在线编辑";
        this.AttachedProfileId = profileId;
        this.encoding = new UTF8Encoding(false);
        this.text = initialText;
        this.savedText = initialText;
    }

    /// <summary>把编辑内容写回远端（后台线程执行）。</summary>
    public async void Save()
    {
        if (this.Saving || !this.IsDirty)
        {
            return;
        }
        this.Saving = true;
        this.StatusText = "正在保存…";
        byte[] bytes = this.encoding.GetBytes(this.text);
        try
        {
            await Task.Run(delegate
            {
                this.contentAccess.WriteAllBytes(this.RemotePath, bytes);
            });
            this.savedText = this.text;
            this.StatusText = "已保存：" + DateTime.Now.ToString("HH:mm:ss");
            this.RaisePropertyChanged(nameof(this.IsDirty));
            EventHandler<string>? handler = this.Saved;
            if (handler != null)
            {
                handler(this, this.RemotePath);
            }
        }
        catch (Exception ex)
        {
            this.StatusText = "保存失败：" + ex.Message;
        }
        finally
        {
            this.Saving = false;
            this.RaisePropertyChanged(nameof(this.Saving));
        }
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
