using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;

namespace TabTaskExplorer.App.Views;

/// <summary>远程传输队列面板交互逻辑。</summary>
public partial class RemoteTransferPanel : UserControl
{
    /// <summary>构造传输队列面板。</summary>
    public RemoteTransferPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>传输队列视图模型。</summary>
    private RemoteTransferPanelViewModel? ViewModel
    {
        get { return this.DataContext as RemoteTransferPanelViewModel; }
    }

    /// <summary>当前选中的任务行。</summary>
    private TransferJobRow? SelectedRow
    {
        get { return this.JobList.SelectedItem as TransferJobRow; }
    }

    /// <summary>上传本地文件。</summary>
    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        RemoteTransferPanelViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "上传文件被拒绝：传输面板未绑定视图模型或找不到顶层窗口 结果=失败");
            return;
        }

        FilePickerOpenOptions options = new FilePickerOpenOptions
        {
            Title = "选择要上传的文件",
            AllowMultiple = false,
        };
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
        {
            UiLogger.Log("程序行为", "上传文件已取消：文件选择窗口里没有选择任何文件，未执行上传");
            return;
        }
        string? localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            UiLogger.Log("程序行为", "上传文件被拒绝：所选文件没有本地完整路径 结果=失败");
            return;
        }

        string? remotePath = await InputDialog.Show(owner as Window, "上传到远端", "请输入目标远端路径：", "/" + System.IO.Path.GetFileName(localPath));
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            UiLogger.Log("程序行为", "上传文件已取消：未填写目标远端路径 文件路径=" + localPath + "，未执行上传");
            return;
        }
        viewModel.EnqueueUpload(localPath, remotePath);
        UiLogger.Log("程序行为", "上传文件已加入传输队列：文件路径=" + localPath
            + " 目标远端路径=" + remotePath
            + " 结果=成功，已开始排队上传");
    }

    /// <summary>下载远端文件。</summary>
    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        RemoteTransferPanelViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "下载文件被拒绝：传输面板未绑定视图模型或找不到顶层窗口 结果=失败");
            return;
        }

        string? remotePath = await InputDialog.Show(owner as Window, "下载远端文件", "请输入远端文件路径：", "/");
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            UiLogger.Log("程序行为", "下载文件已取消：未填写远端文件路径，未执行下载");
            return;
        }
        string remoteName = System.IO.Path.GetFileName(remotePath.Replace('\\', '/').TrimEnd('/'));
        if (string.IsNullOrEmpty(remoteName))
        {
            remoteName = "download";
        }

        FilePickerSaveOptions options = new FilePickerSaveOptions
        {
            Title = "保存下载文件",
            SuggestedFileName = remoteName,
        };
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(options);
        if (file == null)
        {
            UiLogger.Log("程序行为", "下载文件已取消：未选择本地保存位置 远端文件路径=" + remotePath + "，未执行下载");
            return;
        }
        string? localPath = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            UiLogger.Log("程序行为", "下载文件被拒绝：所选保存位置没有本地完整路径 远端文件路径=" + remotePath + " 结果=失败");
            return;
        }
        viewModel.EnqueueDownload(remotePath, localPath);
        UiLogger.Log("程序行为", "下载文件已加入传输队列：远端文件路径=" + remotePath
            + " 本地保存路径=" + localPath
            + " 结果=成功，已开始排队下载");
    }

    /// <summary>暂停选中任务。</summary>
    private void OnPauseClick(object? sender, RoutedEventArgs e)
    {
        TransferJobRow? row = this.SelectedRow;
        RemoteTransferPanelViewModel? viewModel = this.ViewModel;
        if (row == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "暂停传输任务被拒绝：未绑定视图模型或未选中任何任务 结果=失败");
            return;
        }
        string statusBefore = row.StatusText;
        viewModel.Pause(row);
        UiLogger.Log("程序行为", "暂停传输任务：任务=" + row.Job.Summary
            + " 暂停前状态=" + statusBefore
            + " 结果=成功，已提交暂停请求");
    }

    /// <summary>继续选中任务。</summary>
    private void OnResumeClick(object? sender, RoutedEventArgs e)
    {
        TransferJobRow? row = this.SelectedRow;
        RemoteTransferPanelViewModel? viewModel = this.ViewModel;
        if (row == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "继续传输任务被拒绝：未绑定视图模型或未选中任何任务 结果=失败");
            return;
        }
        string statusBefore = row.StatusText;
        viewModel.Resume(row);
        UiLogger.Log("程序行为", "继续传输任务：任务=" + row.Job.Summary
            + " 继续前状态=" + statusBefore
            + " 结果=成功，已提交继续传输请求");
    }

    /// <summary>取消选中任务。</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        TransferJobRow? row = this.SelectedRow;
        RemoteTransferPanelViewModel? viewModel = this.ViewModel;
        if (row == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "取消传输任务被拒绝：未绑定视图模型或未选中任何任务 结果=失败");
            return;
        }
        string statusBefore = row.StatusText;
        viewModel.Cancel(row);
        UiLogger.Log("程序行为", "取消传输任务：任务=" + row.Job.Summary
            + " 取消前状态=" + statusBefore
            + " 结果=成功，已提交取消请求");
    }
}
