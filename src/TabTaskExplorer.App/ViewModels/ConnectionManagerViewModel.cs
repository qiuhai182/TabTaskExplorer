using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 连接管理标签页视图模型：远程主机（SSH / FTP）的增删改查、连通性测试与导出导入。
/// 只依赖内核 ConnectionManager，不接触任何平台类型。
/// </summary>
public class ConnectionManagerViewModel : INotifyPropertyChanged, ITabView
{
    /// <summary>标签类型标识：连接管理。</summary>
    public string Kind
    {
        get { return TabKinds.Connections; }
    }

    /// <summary>把连接列表与选中项写入网页端快照（桌面与网页共用）。</summary>
    public void FillSnapshot(DebugTabState tabState)
    {
        TabSnapshotFiller.FillConnections(this, tabState);
    }

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

    /// <summary>
    /// 保存连接配置：编号已存在则更新，否则新增。明文密码非空时一并加密保存；
    /// hopPlaintextSecrets 与 JumpHops 按顺序对应，留空表示保持该跳原有凭据。
    /// </summary>
    public RemoteConnectionProfile Save(RemoteConnectionProfile profile, string? plaintextSecret,
        IReadOnlyList<string>? hopPlaintextSecrets = null)
    {
        if (hopPlaintextSecrets != null && profile.JumpHops != null)
        {
            for (int index = 0; index < hopPlaintextSecrets.Count && index < profile.JumpHops.Count; index++)
            {
                if (!string.IsNullOrEmpty(hopPlaintextSecrets[index]))
                {
                    this.manager.SetJumpSecret(profile, index, hopPlaintextSecrets[index]);
                }
            }
        }
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

    /// <summary>在调用线程直接执行一键导入（网页端动作线程调用，不经过后台线程）。</summary>
    /// <param name="homeDirectory">用于定位配置目录的用户主目录；为空时取当前登录用户的主目录。</param>
    public XshellImportSummary ImportInstalled(string? homeDirectory = null)
    {
        XshellImportSummary summary = this.manager.ImportInstalledXshellSessions(homeDirectory);
        this.Reload();
        this.SelectFirstByName(summary.Names);
        return summary;
    }

    /// <summary>一键导入本机已安装的 Xshell / Xftp 会话配置：同名连接更新参数，新连接追加。</summary>
    /// <param name="homeDirectory">用于定位配置目录的用户主目录；为空时取当前登录用户的主目录。</param>
    public async Task<XshellImportSummary> ImportInstalledAsync(string? homeDirectory = null)
    {
        XshellImportSummary summary = await Task.Run(delegate
        {
            return this.manager.ImportInstalledXshellSessions(homeDirectory);
        });
        this.Reload();
        this.SelectFirstByName(summary.Names);
        return summary;
    }

    /// <summary>按名称选中列表中的连接配置（导入完成后选中首条）。</summary>
    private void SelectFirstByName(List<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }
        foreach (RemoteConnectionProfile profile in this.Connections)
        {
            if (profile.Name == names[0])
            {
                this.SelectedConnection = profile;
                return;
            }
        }
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
