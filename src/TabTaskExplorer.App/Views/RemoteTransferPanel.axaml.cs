using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
            return;
        }
        string? localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            return;
        }

        string? remotePath = await InputDialog.Show(owner as Window, "上传到远端", "请输入目标远端路径：", "/" + System.IO.Path.GetFileName(localPath));
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return;
        }
        viewModel.EnqueueUpload(localPath, remotePath);
    }

    /// <summary>下载远端文件。</summary>
    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        RemoteTransferPanelViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            return;
        }

        string? remotePath = await InputDialog.Show(owner as Window, "下载远端文件", "请输入远端文件路径：", "/");
        if (string.IsNullOrWhiteSpace(remotePath))
        {
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
            return;
        }
        string? localPath = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            return;
        }
        viewModel.EnqueueDownload(remotePath, localPath);
    }

    /// <summary>暂停选中任务。</summary>
    private void OnPauseClick(object? sender, RoutedEventArgs e)
    {
        TransferJobRow? row = this.SelectedRow;
        if (row != null)
        {
            this.ViewModel?.Pause(row);
        }
    }

    /// <summary>继续选中任务。</summary>
    private void OnResumeClick(object? sender, RoutedEventArgs e)
    {
        TransferJobRow? row = this.SelectedRow;
        if (row != null)
        {
            this.ViewModel?.Resume(row);
        }
    }

    /// <summary>取消选中任务。</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        TransferJobRow? row = this.SelectedRow;
        if (row != null)
        {
            this.ViewModel?.Cancel(row);
        }
    }
}
