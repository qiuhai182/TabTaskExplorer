using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 连接管理标签页视图模型：远程主机（SSH / FTP）的增删改查、连通性测试与导出导入。
/// 只依赖内核 ConnectionManager，不接触任何平台类型。
/// </summary>
public class ConnectionManagerViewModel : INotifyPropertyChanged
{
    /// <summary>标签页标题。</summary>
    public string Title
    {
        get { return "连接管理"; }
    }

    private readonly ConnectionManager manager;
    private RemoteConnectionProfile? selectedConnection;

    /// <summary>所属主窗口视图模型：用于打开终端、远程文件夹与传输队列标签。</summary>
    public MainWindowViewModel? Owner { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>全部连接配置。</summary>
    public ObservableCollection<RemoteConnectionProfile> Connections { get; } = new ObservableCollection<RemoteConnectionProfile>();

    /// <summary>当前选中的连接配置。</summary>
    public RemoteConnectionProfile? SelectedConnection
    {
        get { return this.selectedConnection; }
        set
        {
            this.selectedConnection = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedConnection)));
        }
    }

    /// <summary>使用内核连接管理器创建视图模型。</summary>
    public ConnectionManagerViewModel(ConnectionManager manager)
    {
        this.manager = manager;
        this.Reload();
    }

    /// <summary>从存储重新加载全部连接配置。</summary>
    public void Reload()
    {
        this.Connections.Clear();
        foreach (RemoteConnectionProfile profile in this.manager.GetAll())
        {
            this.Connections.Add(profile);
        }
    }

    /// <summary>创建一个带默认值的新连接配置（不保存，供编辑表单填写）。</summary>
    public RemoteConnectionProfile CreateDraft()
    {
        RemoteConnectionProfile draft = new RemoteConnectionProfile();
        draft.Name = "新建连接";
        draft.Protocol = RemoteProtocol.Ssh;
        return draft;
    }

    /// <summary>保存连接配置：编号已存在则更新，否则新增。明文密码非空时一并加密保存。</summary>
    public RemoteConnectionProfile Save(RemoteConnectionProfile profile, string? plaintextSecret)
    {
        bool exists = this.manager.FindById(profile.Id) != null;
        if (exists)
        {
            this.manager.Update(profile);
        }
        else
        {
            this.manager.Add(profile);
        }
        if (!string.IsNullOrEmpty(plaintextSecret))
        {
            this.manager.SetSecret(profile, plaintextSecret);
        }
        this.Reload();
        this.SelectById(profile.Id);
        return profile;
    }

    /// <summary>删除指定连接配置。</summary>
    public void Delete(string id)
    {
        this.manager.Delete(id);
        this.Reload();
        if (this.selectedConnection != null && this.selectedConnection.Id == id)
        {
            this.SelectedConnection = null;
        }
    }

    /// <summary>导入一个从 Xshell 会话文件解析出的连接配置并选中新条目。</summary>
    public void ImportXshellProfile(RemoteConnectionProfile profile)
    {
        RemoteConnectionProfile added = this.manager.Add(profile);
        this.Reload();
        this.SelectById(added.Id);
    }

    /// <summary>测试指定连接的连通性（网络请求放在后台线程执行）。</summary>
    public Task<ConnectionTestResult> TestAsync(RemoteConnectionProfile profile, int timeoutMilliseconds)
    {
        return Task.Run(delegate
        {
            return this.manager.Test(profile, timeoutMilliseconds);
        });
    }

    /// <summary>导出全部连接配置到 JSON 文件。</summary>
    public void ExportTo(string filePath)
    {
        this.manager.ExportTo(filePath);
    }

    /// <summary>从 JSON 文件导入连接配置（编号重复的跳过）。</summary>
    /// <returns>实际导入的条目数量。</returns>
    public int ImportFrom(string filePath)
    {
        int importedCount = this.manager.ImportFrom(filePath);
        this.Reload();
        return importedCount;
    }

    /// <summary>按编号选中列表中的连接配置。</summary>
    private void SelectById(string id)
    {
        foreach (RemoteConnectionProfile profile in this.Connections)
        {
            if (profile.Id == id)
            {
                this.SelectedConnection = profile;
                return;
            }
        }
    }
}
