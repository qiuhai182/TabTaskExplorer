using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TabTaskExplorer.App.Services;
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
            UiLogger.Log("程序行为", "按钮=新建连接 被点击：连接管理视图模型未就绪，结果=失败，未生成连接草稿");
            return;
        }
        this.editingProfile = viewModel.CreateDraft();
        this.editingDraft = true;
        this.FillForm();
        this.StatusText.Text = "正在新建连接，填写完成后点击保存";
        UiLogger.Log("程序行为", "按钮=新建连接 被点击：草稿连接名称=" + (this.editingProfile != null ? this.editingProfile.Name : string.Empty)
            + " 结果=成功，编辑表单已切换为新建状态，填写完成并点击保存后才会写入连接列表");
    }

    /// <summary>删除当前选中的连接。</summary>
    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=删除 被点击：连接管理视图模型未就绪，结果=失败，未删除任何连接");
            return;
        }
        RemoteConnectionProfile? target = viewModel.SelectedConnection;
        if (target == null)
        {
            this.StatusText.Text = "请先在左侧选择要删除的连接";
            UiLogger.Log("程序行为", "按钮=删除 被点击：连接名称=（未选中） 结果=失败，左侧连接列表没有选中项，未执行删除");
            return;
        }
        string removedName = target.Name;
        string removedId = target.Id;
        viewModel.Delete(target.Id);
        this.editingProfile = null;
        this.editingDraft = false;
        this.FillForm();
        this.StatusText.Text = "已删除：" + removedName;
        UiLogger.Log("程序行为", "按钮=删除 被点击：连接名称=" + removedName + " 连接编号=" + removedId
            + " 结果=成功，连接已从连接列表删除，编辑表单已清空");
    }

    /// <summary>测试当前选中连接的连通性（网络请求放在后台线程）。</summary>
    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=测试连接 被点击：连接管理视图模型未就绪，结果=失败，未执行连通性测试");
            return;
        }
        RemoteConnectionProfile? target = viewModel.SelectedConnection;
        if (target == null)
        {
            this.StatusText.Text = "请先选择要测试的连接";
            UiLogger.Log("程序行为", "按钮=测试连接 被点击：连接名称=（未选中） 结果=失败，左侧连接列表没有选中项，未执行连通性测试");
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
                UiLogger.Log("程序行为", "按钮=测试连接 被点击：连接名称=" + target.Name + " 服务器地址=" + target.Host
                    + " 端口号=" + target.Port + " 结果=成功，连接耗时=" + result.LatencyMilliseconds + " 毫秒 说明=" + result.Message);
            }
            else
            {
                this.StatusText.Text = "测试失败：" + result.Message;
                UiLogger.Log("程序行为", "按钮=测试连接 被点击：连接名称=" + target.Name + " 服务器地址=" + target.Host
                    + " 端口号=" + target.Port + " 结果=失败，未连通 说明=" + result.Message);
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
            UiLogger.Log("程序行为", "按钮=打开终端 被点击：连接管理视图模型、所属主窗口或当前选中连接为空，结果=失败，未打开远程终端标签");
            return;
        }
        viewModel.Owner.OpenTerminalTab(selected);
        UiLogger.Log("程序行为", "按钮=打开终端 被点击：连接名称=" + selected.Name + " 服务器地址=" + selected.Host
            + " 结果=成功，已请求主窗口打开该连接的远程终端标签");
    }

    /// <summary>把选中连接的远程目录打开为文件标签。</summary>
    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        RemoteConnectionProfile? selected = viewModel?.SelectedConnection;
        if (viewModel?.Owner == null || selected == null)
        {
            UiLogger.Log("程序行为", "按钮=打开远程文件夹 被点击：连接管理视图模型、所属主窗口或当前选中连接为空，结果=失败，未打开远程文件夹标签");
            return;
        }
        viewModel.Owner.OpenRemoteFolderTab(selected);
        UiLogger.Log("程序行为", "按钮=打开远程文件夹 被点击：连接名称=" + selected.Name + " 服务器地址=" + selected.Host
            + " 结果=成功，已请求主窗口把该连接的远程目录作为文件标签打开");
    }

    /// <summary>以选中连接的远程目录为工作区进入 IDE 模式，可直接编辑并保存回传。</summary>
    private void OnOpenIdeClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        RemoteConnectionProfile? selected = viewModel?.SelectedConnection;
        if (viewModel?.Owner == null || selected == null)
        {
            UiLogger.Log("程序行为", "按钮=在集成开发环境中打开 被点击：连接管理视图模型、所属主窗口或当前选中连接为空，结果=失败，未进入集成开发环境模式");
            return;
        }
        viewModel.Owner.OpenRemoteIdeRoot(selected);
        UiLogger.Log("程序行为", "按钮=在集成开发环境中打开 被点击：连接名称=" + selected.Name + " 服务器地址=" + selected.Host
            + " 结果=成功，已请求以该连接的远程目录为工作区进入集成开发环境模式");
    }

    /// <summary>打开选中连接的传输队列。</summary>
    private void OnOpenTransfersClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        RemoteConnectionProfile? selected = viewModel?.SelectedConnection;
        if (viewModel?.Owner == null || selected == null)
        {
            UiLogger.Log("程序行为", "按钮=传输队列 被点击：连接管理视图模型、所属主窗口或当前选中连接为空，结果=失败，未打开传输队列标签");
            return;
        }
        viewModel.Owner.OpenTransfersTab(selected);
        UiLogger.Log("程序行为", "按钮=传输队列 被点击：连接名称=" + selected.Name + " 服务器地址=" + selected.Host
            + " 结果=成功，已请求主窗口打开该连接的上传下载传输队列标签");
    }

    /// <summary>从用户选择的 Xshell 会话文件导入连接配置。</summary>
    private async void OnImportXshellClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "按钮=导入 Xshell 被点击：连接管理视图模型或顶层窗口未就绪，结果=失败，未打开会话文件选择框");
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
        UiLogger.Log("程序行为", "按钮=导入 Xshell 被点击：选择文件数=" + files.Count + " 成功导入数=" + importedCount
            + " 结果=" + (files.Count == 0
                ? "未执行，用户取消了会话文件选择"
                : importedCount > 0 ? "成功，导入的连接已加入连接列表" : "失败，会话文件解析失败或缺少主机地址"));
    }

    /// <summary>导出全部连接配置到用户选择的 JSON 文件。</summary>
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "按钮=导出 被点击：连接管理视图模型或顶层窗口未就绪，结果=失败，未打开保存文件选择框");
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
            UiLogger.Log("程序行为", "按钮=导出 被点击：结果=未执行，用户取消了保存文件选择");
            return;
        }
        viewModel.ExportTo(file.Path.LocalPath);
        this.StatusText.Text = "已导出到：" + file.Path.LocalPath;
        UiLogger.Log("程序行为", "按钮=导出 被点击：导出文件路径=" + file.Path.LocalPath
            + " 结果=成功，全部连接配置已写入该文件");
    }

    /// <summary>从用户选择的 JSON 文件导入连接配置。</summary>
    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "按钮=导入 被点击：连接管理视图模型或顶层窗口未就绪，结果=失败，未打开配置文件选择框");
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
            UiLogger.Log("程序行为", "按钮=导入 被点击：选择文件数=0 结果=未执行，用户取消了配置文件选择");
            return;
        }
        int importedCount = viewModel.ImportFrom(files[0].Path.LocalPath);
        this.StatusText.Text = "导入完成：新增 " + importedCount + " 条";
        UiLogger.Log("程序行为", "按钮=导入 被点击：配置文件路径=" + files[0].Path.LocalPath + " 新增连接数=" + importedCount
            + " 结果=成功，导入的连接已加入连接列表，编号重复的条目已跳过");
    }

    /// <summary>列表选中变化：把选中的连接填入编辑表单。</summary>
    private void OnSelectedChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.editingDraft)
        {
            UiLogger.Log("程序行为", "连接列表选中变化：正在新建连接草稿，结果=忽略，编辑表单保持草稿内容不变");
            return;
        }
        this.editingProfile = this.ViewModel?.SelectedConnection;
        this.FillForm();
        if (this.editingProfile == null)
        {
            UiLogger.Log("程序行为", "连接列表选中变化：连接名称=（无选中项） 结果=成功，编辑表单已清空并禁用");
            return;
        }
        UiLogger.Log("程序行为", "连接列表选中变化：连接名称=" + this.editingProfile.Name
            + " 服务器地址=" + this.editingProfile.Host + " 结果=成功，已把该连接填入编辑表单");
    }

    /// <summary>保存表单：校验必填项后写入存储。</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null || this.editingProfile == null)
        {
            UiLogger.Log("程序行为", "按钮=保存 被点击：连接管理视图模型或正在编辑的连接为空，结果=失败，未执行保存");
            return;
        }
        if (string.IsNullOrWhiteSpace(this.NameBox.Text))
        {
            this.StatusText.Text = "请填写连接名称";
            UiLogger.Log("程序行为", "按钮=保存 被点击：连接名称=（空白） 结果=失败，连接名称为必填项，未执行保存");
            return;
        }
        if (string.IsNullOrWhiteSpace(this.HostBox.Text))
        {
            this.StatusText.Text = "请填写主机地址";
            UiLogger.Log("程序行为", "按钮=保存 被点击：服务器地址=（空白） 结果=失败，主机地址为必填项，未执行保存");
            return;
        }
        this.CollectForm();
        string plaintextSecret = this.SecretBox.Text ?? string.Empty;
        string savedName = this.editingProfile.Name;
        string savedHost = this.editingProfile.Host;
        string secretText = string.IsNullOrEmpty(plaintextSecret) ? "留空，保持原密码" : "已更新密码";
        viewModel.Save(this.editingProfile, plaintextSecret);
        this.editingDraft = false;
        this.editingProfile = viewModel.SelectedConnection;
        this.FillForm();
        this.StatusText.Text = "已保存：" + (this.editingProfile != null ? this.editingProfile.Name : string.Empty);
        UiLogger.Log("程序行为", "按钮=保存 被点击：连接名称=" + savedName + " 服务器地址=" + savedHost
            + " 密码处理=" + secretText + " 结果=成功，连接配置已写入存储并重新选中该连接");
    }

    /// <summary>取消编辑：恢复为当前选中连接的内容。</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        this.editingDraft = false;
        this.editingProfile = this.ViewModel?.SelectedConnection;
        this.FillForm();
        this.StatusText.Text = "已取消编辑";
        UiLogger.Log("程序行为", "按钮=取消编辑 被点击：结果=成功，编辑表单已恢复为当前选中连接的内容");
    }

    /// <summary>协议切换：更新认证区块可见性等表单状态。</summary>
    private void OnProtocolChanged(object? sender, SelectionChangedEventArgs e)
    {
        this.UpdateAuthSections();
        string protocolText = this.ProtocolBox.SelectedIndex == 1 ? "文件传输协议" : "安全外壳协议";
        UiLogger.Log("程序行为", "协议选择框切换：协议=" + protocolText
            + " 结果=成功，编辑表单的协议字段已更新，点击保存后生效");
    }

    /// <summary>认证方式切换：密码输入与私钥路径二选一显示。</summary>
    private void OnAuthChanged(object? sender, SelectionChangedEventArgs e)
    {
        this.UpdateAuthSections();
        string authText = this.AuthBox.SelectedIndex == 1 ? "私钥文件" : "密码";
        UiLogger.Log("程序行为", "认证方式选择框切换：认证方式=" + authText
            + " 结果=成功，密码输入区与私钥路径输入区已按认证方式切换显示");
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
