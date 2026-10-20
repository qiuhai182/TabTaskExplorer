using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 连接管理面板：左侧连接列表，右侧编辑表单。
/// 事件处理只调用视图模型与内核服务，不接触平台实现代码。
/// </summary>
public partial class ConnectionPanel : UserControl
{
    private RemoteConnectionProfile? editingProfile;
    private bool editingDraft;

    public ConnectionPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>连接管理视图模型。</summary>
    private ConnectionManagerViewModel? ViewModel
    {
        get { return this.DataContext as ConnectionManagerViewModel; }
    }

    /// <summary>新建连接：准备一份草稿填入表单，保存后才入库。</summary>
    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        this.editingProfile = viewModel.CreateDraft();
        this.editingDraft = true;
        this.FillForm();
        this.StatusText.Text = "正在新建连接，填写完成后点击保存";
    }

    /// <summary>删除当前选中的连接。</summary>
    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        RemoteConnectionProfile? target = viewModel.SelectedConnection;
        if (target == null)
        {
            this.StatusText.Text = "请先在左侧选择要删除的连接";
            return;
        }
        string removedName = target.Name;
        viewModel.Delete(target.Id);
        this.editingProfile = null;
        this.editingDraft = false;
        this.FillForm();
        this.StatusText.Text = "已删除：" + removedName;
    }

    /// <summary>测试当前选中连接的连通性（网络请求放在后台线程）。</summary>
    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        RemoteConnectionProfile? target = viewModel.SelectedConnection;
        if (target == null)
        {
            this.StatusText.Text = "请先选择要测试的连接";
            return;
        }
        this.TestButton.IsEnabled = false;
        this.StatusText.Text = "正在测试 " + target.Host + " ...";
        try
        {
            ConnectionTestResult result = await viewModel.TestAsync(target, TcpConnectivityTester.DefaultTimeoutMilliseconds);
            if (result.Success)
            {
                this.StatusText.Text = "测试成功：" + result.Message;
            }
            else
            {
                this.StatusText.Text = "测试失败：" + result.Message;
            }
        }
        finally
        {
            this.TestButton.IsEnabled = true;
        }
    }

    /// <summary>打开选中连接的远程终端。</summary>
    private void OnOpenTerminalClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        RemoteConnectionProfile? selected = viewModel?.SelectedConnection;
        if (viewModel?.Owner == null || selected == null)
        {
            return;
        }
        viewModel.Owner.OpenTerminalTab(selected);
    }

    /// <summary>把选中连接的远程目录打开为文件标签。</summary>
    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        RemoteConnectionProfile? selected = viewModel?.SelectedConnection;
        if (viewModel?.Owner == null || selected == null)
        {
            return;
        }
        viewModel.Owner.OpenRemoteFolderTab(selected);
    }

    /// <summary>以选中连接的远程目录为工作区进入 IDE 模式，可直接编辑并保存回传。</summary>
    private void OnOpenIdeClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        RemoteConnectionProfile? selected = viewModel?.SelectedConnection;
        if (viewModel?.Owner == null || selected == null)
        {
            return;
        }
        viewModel.Owner.OpenRemoteIdeRoot(selected);
    }

    /// <summary>打开选中连接的传输队列。</summary>
    private void OnOpenTransfersClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        RemoteConnectionProfile? selected = viewModel?.SelectedConnection;
        if (viewModel?.Owner == null || selected == null)
        {
            return;
        }
        viewModel.Owner.OpenTransfersTab(selected);
    }

    /// <summary>从用户选择的 Xshell 会话文件导入连接配置。</summary>
    private async void OnImportXshellClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            return;
        }
        FilePickerOpenOptions options = new FilePickerOpenOptions
        {
            Title = "选择 Xshell 会话文件",
            AllowMultiple = true,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new FilePickerFileType("Xshell 会话文件")
                {
                    Patterns = new List<string> { "*.xsh", "*.xshf" },
                },
            },
        };
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(options);
        int importedCount = 0;
        foreach (IStorageFile file in files)
        {
            string? localPath = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath))
            {
                continue;
            }
            RemoteConnectionProfile? profile = XshellSessionImporter.ImportFromFile(localPath);
            if (profile != null)
            {
                viewModel.ImportXshellProfile(profile);
                importedCount++;
            }
        }
        this.StatusText.Text = importedCount > 0
            ? "已导入 " + importedCount + " 个连接（Xshell 密码需重新输入）"
            : "未导入任何连接（文件解析失败或缺少主机信息）";
    }

    /// <summary>导出全部连接配置到用户选择的 JSON 文件。</summary>
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            return;
        }
        FilePickerSaveOptions options = new FilePickerSaveOptions
        {
            Title = "导出连接配置",
            SuggestedFileName = "connections",
            DefaultExtension = "json",
        };
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(options);
        if (file == null)
        {
            return;
        }
        viewModel.ExportTo(file.Path.LocalPath);
        this.StatusText.Text = "已导出到：" + file.Path.LocalPath;
    }

    /// <summary>从用户选择的 JSON 文件导入连接配置。</summary>
    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            return;
        }
        FilePickerOpenOptions options = new FilePickerOpenOptions
        {
            Title = "导入连接配置",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new FilePickerFileType("JSON 连接配置")
                {
                    Patterns = new List<string> { "*.json" },
                },
            },
        };
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
        {
            return;
        }
        int importedCount = viewModel.ImportFrom(files[0].Path.LocalPath);
        this.StatusText.Text = "导入完成：新增 " + importedCount + " 条";
    }

    /// <summary>列表选中变化：把选中的连接填入编辑表单。</summary>
    private void OnSelectedChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.editingDraft)
        {
            return;
        }
        this.editingProfile = this.ViewModel?.SelectedConnection;
        this.FillForm();
    }

    /// <summary>保存表单：校验必填项后写入存储。</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null || this.editingProfile == null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(this.NameBox.Text))
        {
            this.StatusText.Text = "请填写连接名称";
            return;
        }
        if (string.IsNullOrWhiteSpace(this.HostBox.Text))
        {
            this.StatusText.Text = "请填写主机地址";
            return;
        }
        this.CollectForm();
        string plaintextSecret = this.SecretBox.Text ?? string.Empty;
        viewModel.Save(this.editingProfile, plaintextSecret);
        this.editingDraft = false;
        this.editingProfile = viewModel.SelectedConnection;
        this.FillForm();
        this.StatusText.Text = "已保存：" + (this.editingProfile != null ? this.editingProfile.Name : string.Empty);
    }

    /// <summary>取消编辑：恢复为当前选中连接的内容。</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        this.editingDraft = false;
        this.editingProfile = this.ViewModel?.SelectedConnection;
        this.FillForm();
        this.StatusText.Text = "已取消编辑";
    }

    /// <summary>协议切换：更新认证区块可见性等表单状态。</summary>
    private void OnProtocolChanged(object? sender, SelectionChangedEventArgs e)
    {
        this.UpdateAuthSections();
    }

    /// <summary>认证方式切换：密码输入与私钥路径二选一显示。</summary>
    private void OnAuthChanged(object? sender, SelectionChangedEventArgs e)
    {
        this.UpdateAuthSections();
    }

    /// <summary>把编辑中的连接配置填充到表单。</summary>
    private void FillForm()
    {
        RemoteConnectionProfile? profile = this.editingProfile;
        if (profile == null)
        {
            this.EditForm.IsEnabled = false;
            this.ClearForm();
            return;
        }
        this.EditForm.IsEnabled = true;
        this.NameBox.Text = profile.Name;
        this.ProtocolBox.SelectedIndex = profile.Protocol == RemoteProtocol.Ftp ? 1 : 0;
        this.HostBox.Text = profile.Host;
        this.PortBox.Text = profile.Port > 0 ? profile.Port.ToString() : string.Empty;
        this.UserBox.Text = profile.Username;
        this.AuthBox.SelectedIndex = profile.AuthType == RemoteAuthType.PrivateKey ? 1 : 0;
        this.SecretBox.Text = string.Empty;
        this.KeyPathBox.Text = profile.PrivateKeyPath;
        this.KeepAliveBox.Text = profile.KeepAliveSeconds > 0 ? profile.KeepAliveSeconds.ToString() : "0";
        this.UpdateAuthSections();
    }

    /// <summary>把表单内容写回编辑中的连接配置。</summary>
    private void CollectForm()
    {
        RemoteConnectionProfile? profile = this.editingProfile;
        if (profile == null)
        {
            return;
        }
        profile.Name = this.NameBox.Text == null ? string.Empty : this.NameBox.Text.Trim();
        profile.Protocol = this.ProtocolBox.SelectedIndex == 1 ? RemoteProtocol.Ftp : RemoteProtocol.Ssh;
        profile.Host = this.HostBox.Text == null ? string.Empty : this.HostBox.Text.Trim();
        int port = 0;
        if (int.TryParse(this.PortBox.Text == null ? string.Empty : this.PortBox.Text.Trim(), out port) && port > 0)
        {
            profile.Port = port;
        }
        else
        {
            profile.Port = 0;
        }
        profile.Username = this.UserBox.Text == null ? string.Empty : this.UserBox.Text.Trim();
        profile.AuthType = this.AuthBox.SelectedIndex == 1 ? RemoteAuthType.PrivateKey : RemoteAuthType.Password;
        profile.PrivateKeyPath = this.KeyPathBox.Text == null ? string.Empty : this.KeyPathBox.Text.Trim();
        int keepAlive = 15;
        if (int.TryParse(this.KeepAliveBox.Text == null ? string.Empty : this.KeepAliveBox.Text.Trim(), out keepAlive) && keepAlive >= 0)
        {
            profile.KeepAliveSeconds = keepAlive;
        }
        else
        {
            profile.KeepAliveSeconds = 15;
        }
    }

    /// <summary>按认证方式切换密码与私钥输入区的可见性。</summary>
    private void UpdateAuthSections()
    {
        bool useKey = this.AuthBox.SelectedIndex == 1;
        this.SecretSection.IsVisible = !useKey;
        this.KeySection.IsVisible = useKey;
    }

    /// <summary>清空表单。</summary>
    private void ClearForm()
    {
        this.NameBox.Text = string.Empty;
        this.ProtocolBox.SelectedIndex = 0;
        this.HostBox.Text = string.Empty;
        this.PortBox.Text = string.Empty;
        this.UserBox.Text = string.Empty;
        this.AuthBox.SelectedIndex = 0;
        this.SecretBox.Text = string.Empty;
        this.KeyPathBox.Text = string.Empty;
        this.UpdateAuthSections();
    }
}
