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
    private readonly List<RemoteJumpHop> editingHops = new List<RemoteJumpHop>();
    private readonly List<string> editingHopSecrets = new List<string>();
    private int selectedHopIndex = -1;
    private bool suppressHopSelection;

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
                    Patterns = new List<string> { "*.xsh", "*.xshf", "*.xfp", "*.xftp" },
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

    /// <summary>一键导入本机已安装的 Xshell / Xftp 配置：自动扫描配置目录并按名称合并。</summary>
    private async void OnImportInstalledClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=一键导入已安装配置 被点击：连接管理视图模型未就绪，结果=失败，未执行配置目录扫描");
            return;
        }

        this.ImportInstalledButton.IsEnabled = false;
        try
        {
            XshellImportSummary summary = await viewModel.ImportInstalledAsync();
            string rootsText = string.Join("、", summary.RootsScanned);
            string skippedText = string.Join("、", summary.Skipped);
            string statusText;
            string resultText;
            if (summary.RootsScanned.Count == 0)
            {
                statusText = "未发现 Xshell / Xftp 配置目录（已查找用户文档目录与 OneDrive 文档目录下的 NetSarang Computer）";
                resultText = "未执行导入，本机不存在 NetSarang Computer 配置目录";
            }
            else if (summary.Names.Count == 0)
            {
                statusText = "已扫描 " + summary.RootsScanned.Count + " 个配置目录，但没有可导入的会话"
                    + (summary.Skipped.Count > 0 ? "；跳过：" + skippedText : string.Empty);
                resultText = "扫描完成但没有可导入的会话";
            }
            else
            {
                statusText = "一键导入完成：新增 " + summary.Imported + " 个、更新 " + summary.Updated + " 个连接"
                    + (summary.Skipped.Count > 0 ? "；跳过 " + summary.Skipped.Count + " 个：" + skippedText : string.Empty)
                    + (summary.NeedsPassword.Count > 0
                        ? "；其中 " + summary.NeedsPassword.Count + " 个连接未包含密码，需在“密码”框重新输入后保存"
                        : "（Xshell 密码与私钥需补填）");
                resultText = "成功，已按名称合并到连接列表";
            }
            this.StatusText.Text = statusText;
            UiLogger.Log("程序行为", "按钮=一键导入已安装配置 被点击：配置目录数=" + summary.RootsScanned.Count
                + " 配置目录=" + rootsText + " 新增连接数=" + summary.Imported + " 更新连接数=" + summary.Updated
                + " 会话名称=" + string.Join("、", summary.Names) + " 跳过数=" + summary.Skipped.Count
                + " 跳过清单=" + skippedText + " 结果=" + resultText);
        }
        finally
        {
            this.ImportInstalledButton.IsEnabled = true;
        }
    }

    /// <summary>查看或删除选中连接主机已信任的 SSH 主机密钥。</summary>
    private void OnHostKeyClick(object? sender, RoutedEventArgs e)
    {
        ConnectionManagerViewModel? viewModel = this.ViewModel;
        RemoteConnectionProfile? selected = viewModel?.SelectedConnection;
        if (viewModel?.Owner == null || selected == null)
        {
            this.StatusText.Text = "请先在左侧选择要管理主机密钥的连接";
            UiLogger.Log("程序行为", "按钮=主机密钥 被点击：连接管理视图模型、所属主窗口或当前选中连接为空，结果=失败，未打开主机密钥管理");
            return;
        }
        viewModel.Owner.ManageKnownHosts(selected);
        this.StatusText.Text = "已请求查看主机密钥：" + selected.Host + ":" + selected.GetEffectivePort();
        UiLogger.Log("程序行为", "按钮=主机密钥 被点击：连接名称=" + selected.Name + " 服务器=" + selected.Host
            + " 端口=" + selected.GetEffectivePort() + " 结果=成功，已请求主窗口打开主机密钥管理");
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
            this.StatusText.Text = "请填写目标主机地址（当前连接）";
            UiLogger.Log("程序行为", "按钮=保存 被点击：目标主机地址=（空白） 结果=失败，目标主机地址为必填项，未执行保存");
            return;
        }
        if (this.JumpEnabledBox.IsChecked == true && this.editingHops.Count == 0)
        {
            this.StatusText.Text = "已勾选经跳板连接，请至少添加一跳跳板机";
            return;
        }
        this.CollectForm();
        string? jumpError = this.editingProfile.ValidateJumpChain(true);
        if (jumpError != null)
        {
            this.StatusText.Text = jumpError;
            UiLogger.Log("程序行为", "按钮=保存 被点击：跳板链校验失败，结果=未执行保存，原因=" + jumpError);
            return;
        }
        string plaintextSecret = this.SecretBox.Text ?? string.Empty;
        string savedName = this.editingProfile.Name;
        string savedHost = this.editingProfile.Host;
        string secretText = string.IsNullOrEmpty(plaintextSecret) ? "留空，保持原密码" : "已更新密码";
        viewModel.Save(this.editingProfile, plaintextSecret, this.editingHopSecrets);
        this.editingDraft = false;
        this.editingProfile = viewModel.SelectedConnection;
        this.FillForm();
        this.StatusText.Text = "已保存：" + (this.editingProfile != null ? this.editingProfile.Name : string.Empty);
        UiLogger.Log("程序行为", "按钮=保存 被点击：连接名称=" + savedName + " 服务器地址=" + savedHost
            + " 密码处理=" + secretText + " 跳板跳数=" + this.editingHops.Count
            + " 结果=成功，连接配置已写入存储并重新选中该连接");
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

        // 跳板链用独立副本编辑：取消编辑或切换连接时不会改动已保存的配置
        this.editingHops.Clear();
        this.editingHopSecrets.Clear();
        if (profile.JumpHops != null)
        {
            foreach (RemoteJumpHop hop in profile.JumpHops)
            {
                this.editingHops.Add(hop.Clone());
                this.editingHopSecrets.Add(string.Empty);
            }
        }
        this.selectedHopIndex = this.editingHops.Count > 0 ? 0 : -1;
        this.JumpEnabledBox.IsChecked = this.editingHops.Count > 0;
        this.JumpSection.IsVisible = this.editingHops.Count > 0;
        this.RefreshHopList();
        this.ClearHopForm();
        if (this.selectedHopIndex >= 0)
        {
            this.LoadHopForm(this.selectedHopIndex);
        }
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
        profile.JumpHops = this.JumpEnabledBox.IsChecked == true
            ? new List<RemoteJumpHop>(this.editingHops)
            : new List<RemoteJumpHop>();
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
        this.editingHops.Clear();
        this.editingHopSecrets.Clear();
        this.selectedHopIndex = -1;
        this.JumpEnabledBox.IsChecked = false;
        this.JumpSection.IsVisible = false;
        this.RefreshHopList();
        this.ClearHopForm();
    }

    /// <summary>跳板开关：控制跳板链编辑区的可见性。</summary>
    private void OnJumpEnabledChanged(object? sender, RoutedEventArgs e)
    {
        this.JumpSection.IsVisible = this.JumpEnabledBox.IsChecked == true;
        if (this.JumpEnabledBox.IsChecked != true)
        {
            UiLogger.Log("程序行为", "跳板链开关：已选择直连，结果=跳板链编辑区隐藏，保存时清空跳板");
        }
        else
        {
            UiLogger.Log("程序行为", "跳板链开关：已选择经跳板连接，最多 " + RemoteConnectionProfile.MaxJumpHops + " 跳");
        }
    }

    /// <summary>跳板认证方式切换：密码与私钥路径二选一。</summary>
    private void OnHopAuthChanged(object? sender, SelectionChangedEventArgs e)
    {
        this.UpdateHopAuthSections();
    }

    /// <summary>跳板列表选中：把该跳内容填入编辑字段。</summary>
    private void OnHopSelectedChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.suppressHopSelection)
        {
            return;
        }
        int index = this.HopList.SelectedIndex;
        if (index < 0 || index >= this.editingHops.Count)
        {
            this.selectedHopIndex = -1;
            return;
        }
        this.selectedHopIndex = index;
        this.LoadHopForm(index);
    }

    /// <summary>添加一跳：校验必填项与最大跳数后追加到链尾。</summary>
    private void OnHopAddClick(object? sender, RoutedEventArgs e)
    {
        if (this.editingHops.Count >= RemoteConnectionProfile.MaxJumpHops)
        {
            this.StatusText.Text = "跳板机最多 " + RemoteConnectionProfile.MaxJumpHops + " 跳";
            return;
        }
        RemoteJumpHop hop = new RemoteJumpHop();
        if (!this.ReadHopForm(hop, this.editingHops.Count + 1))
        {
            return;
        }
        this.editingHops.Add(hop);
        this.editingHopSecrets.Add(this.HopSecretBox.Text ?? string.Empty);
        this.selectedHopIndex = this.editingHops.Count - 1;
        this.JumpEnabledBox.IsChecked = true;
        this.RefreshHopList();
        this.HopSecretBox.Text = string.Empty;
        this.StatusText.Text = "已添加第 " + this.editingHops.Count + " 跳：" + hop.Summary;
        UiLogger.Log("程序行为", "跳板链添加：主机=" + hop.Host + " 端口=" + hop.GetEffectivePort()
            + " 用户名=" + hop.Username + " 当前跳数=" + this.editingHops.Count + " 结果=成功");
    }

    /// <summary>更新当前选中跳：把编辑字段写回跳板节点。</summary>
    private void OnHopUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (this.selectedHopIndex < 0 || this.selectedHopIndex >= this.editingHops.Count)
        {
            this.StatusText.Text = "请先在跳板列表中选择要更新的跳";
            return;
        }
        RemoteJumpHop hop = this.editingHops[this.selectedHopIndex];
        if (!this.ReadHopForm(hop, this.selectedHopIndex + 1))
        {
            return;
        }
        string secret = this.HopSecretBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(secret))
        {
            this.editingHopSecrets[this.selectedHopIndex] = secret;
            this.HopSecretBox.Text = string.Empty;
        }
        this.RefreshHopList();
        this.StatusText.Text = "已更新第 " + (this.selectedHopIndex + 1) + " 跳：" + hop.Summary;
        UiLogger.Log("程序行为", "跳板链更新：跳序=" + (this.selectedHopIndex + 1) + " 主机=" + hop.Host
            + " 端口=" + hop.GetEffectivePort() + " 用户名=" + hop.Username + " 结果=成功");
    }

    /// <summary>删除当前选中跳，并同步删除对应的新密码暂存值。</summary>
    private void OnHopRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (this.selectedHopIndex < 0 || this.selectedHopIndex >= this.editingHops.Count)
        {
            this.StatusText.Text = "请先在跳板列表中选择要删除的跳";
            return;
        }
        string removed = this.editingHops[this.selectedHopIndex].Summary;
        this.editingHops.RemoveAt(this.selectedHopIndex);
        this.editingHopSecrets.RemoveAt(this.selectedHopIndex);
        if (this.editingHops.Count == 0)
        {
            this.selectedHopIndex = -1;
            this.ClearHopForm();
        }
        else
        {
            this.selectedHopIndex = Math.Min(this.selectedHopIndex, this.editingHops.Count - 1);
        }
        this.RefreshHopList();
        if (this.selectedHopIndex >= 0)
        {
            this.LoadHopForm(this.selectedHopIndex);
        }
        this.StatusText.Text = "已删除跳板：" + removed;
        UiLogger.Log("程序行为", "跳板链删除：跳板=" + removed + " 剩余跳数=" + this.editingHops.Count + " 结果=成功");
    }

    /// <summary>上移当前跳。</summary>
    private void OnHopMoveUpClick(object? sender, RoutedEventArgs e)
    {
        this.MoveHop(-1);
    }

    /// <summary>下移当前跳。</summary>
    private void OnHopMoveDownClick(object? sender, RoutedEventArgs e)
    {
        this.MoveHop(1);
    }

    private void MoveHop(int offset)
    {
        int index = this.selectedHopIndex;
        int target = index + offset;
        if (index < 0 || index >= this.editingHops.Count || target < 0 || target >= this.editingHops.Count)
        {
            this.StatusText.Text = "跳板已位于链首或链尾，无法继续移动";
            return;
        }
        RemoteJumpHop hop = this.editingHops[index];
        this.editingHops[index] = this.editingHops[target];
        this.editingHops[target] = hop;
        string secret = this.editingHopSecrets[index];
        this.editingHopSecrets[index] = this.editingHopSecrets[target];
        this.editingHopSecrets[target] = secret;
        this.selectedHopIndex = target;
        this.RefreshHopList();
        this.StatusText.Text = "已调整跳板顺序：当前第 " + (target + 1) + " 跳";
        UiLogger.Log("程序行为", "跳板链排序：跳序=" + (target + 1) + " 主机=" + hop.Host + " 结果=成功");
    }

    /// <summary>把编辑字段读入跳板节点；错误提示固定带“第 N 跳”和具体字段名。</summary>
    private bool ReadHopForm(RemoteJumpHop hop, int hopNumber)
    {
        string prefix = "第 " + hopNumber + " 跳";
        string host = this.HopHostBox.Text == null ? string.Empty : this.HopHostBox.Text.Trim();
        if (host.Length == 0)
        {
            this.StatusText.Text = prefix + "缺少主机地址";
            return false;
        }
        string user = this.HopUserBox.Text == null ? string.Empty : this.HopUserBox.Text.Trim();
        if (user.Length == 0)
        {
            this.StatusText.Text = prefix + "缺少用户名";
            return false;
        }
        string portText = this.HopPortBox.Text == null ? string.Empty : this.HopPortBox.Text.Trim();
        int hopPort = 0;
        if (portText.Length > 0)
        {
            if (!int.TryParse(portText, out hopPort) || hopPort <= 0 || hopPort > 65535)
            {
                this.StatusText.Text = prefix + "端口无效：" + portText;
                return false;
            }
        }
        hop.Port = hopPort;
        hop.Host = host;
        hop.Username = user;
        hop.AuthType = this.HopAuthBox.SelectedIndex == 1 ? RemoteAuthType.PrivateKey : RemoteAuthType.Password;
        hop.PrivateKeyPath = this.HopKeyBox.Text == null ? string.Empty : this.HopKeyBox.Text.Trim();
        if (hop.AuthType == RemoteAuthType.PrivateKey)
        {
            if (hop.PrivateKeyPath.Length == 0)
            {
                this.StatusText.Text = prefix + "私钥路径为空";
                return false;
            }
            if (!File.Exists(hop.PrivateKeyPath))
            {
                this.StatusText.Text = prefix + "私钥文件不存在：" + hop.PrivateKeyPath;
                return false;
            }
        }
        return true;
    }

    /// <summary>把指定跳的内容填入编辑字段。</summary>
    private void LoadHopForm(int index)
    {
        RemoteJumpHop hop = this.editingHops[index];
        this.HopHostBox.Text = hop.Host;
        this.HopPortBox.Text = hop.Port > 0 ? hop.Port.ToString() : string.Empty;
        this.HopUserBox.Text = hop.Username;
        this.HopAuthBox.SelectedIndex = hop.AuthType == RemoteAuthType.PrivateKey ? 1 : 0;
        this.HopKeyBox.Text = hop.PrivateKeyPath;
        this.HopSecretBox.Text = string.Empty;
        this.UpdateHopAuthSections();
    }

    private void ClearHopForm()
    {
        this.HopHostBox.Text = string.Empty;
        this.HopPortBox.Text = string.Empty;
        this.HopUserBox.Text = string.Empty;
        this.HopAuthBox.SelectedIndex = 0;
        this.HopKeyBox.Text = string.Empty;
        this.HopSecretBox.Text = string.Empty;
        this.UpdateHopAuthSections();
    }

    private void UpdateHopAuthSections()
    {
        bool useKey = this.HopAuthBox.SelectedIndex == 1;
        this.HopSecretSection.IsVisible = !useKey;
        this.HopKeySection.IsVisible = useKey;
    }

    /// <summary>重建跳板列表显示，并保持当前选中项。</summary>
    private void RefreshHopList()
    {
        this.suppressHopSelection = true;
        List<string> items = new List<string>();
        for (int index = 0; index < this.editingHops.Count; index++)
        {
            RemoteJumpHop hop = this.editingHops[index];
            bool hasSecret = !string.IsNullOrEmpty(hop.ProtectedSecret)
                || (index < this.editingHopSecrets.Count && !string.IsNullOrEmpty(this.editingHopSecrets[index]));
            items.Add("第 " + (index + 1) + " 跳：" + hop.Summary + (hasSecret ? "（已保存凭据）" : "（未保存凭据）"));
        }
        this.HopList.ItemsSource = items;
        if (this.selectedHopIndex >= 0 && this.selectedHopIndex < items.Count)
        {
            this.HopList.SelectedIndex = this.selectedHopIndex;
        }
        else
        {
            this.HopList.SelectedIndex = -1;
        }
        this.suppressHopSelection = false;
    }
}
