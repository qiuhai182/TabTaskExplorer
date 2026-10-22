using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Views;

/// <summary>文件浏览面板：文件列表、视图切换、信息窗格（导航工具栏与地址栏在主窗口）。</summary>
public partial class FileListPanel : UserControl
{
    public FileListPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>当前标签页视图模型。</summary>
    private FileTabViewModel? ViewModel
    {
        get { return this.DataContext as FileTabViewModel; }
    }

    /// <summary>主窗口视图模型（用于新标签打开、复制剪切到剪贴板）。</summary>
    private MainWindowViewModel? MainViewModel
    {
        get { return TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel; }
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is FileRow row)
        {
            this.ViewModel?.OpenEntry(row);
            UiLogger.Log("程序行为", "双击列表条目：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已进入该目录" : "已按标签页规则打开该条目"));
            e.Handled = true;
        }
    }

    /// <summary>双击列表空白处返回上一级。</summary>
    private void OnListBackgroundDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled)
        {
            UiLogger.Log("程序行为", "双击列表空白处：事件已由列表条目处理 结果=已忽略，未返回上一级");
            return;
        }
        FileTabViewModel? viewModel = this.ViewModel;
        string originalPath = viewModel == null ? string.Empty : viewModel.CurrentPath;
        viewModel?.NavigateUp();
        UiLogger.Log("程序行为", "双击列表空白处返回上一级：原路径=" + (originalPath.Length == 0 ? "此电脑" : originalPath)
            + " 结果=" + (originalPath.Length == 0 ? "失败，当前没有打开的目录，未返回上一级" : "成功，已执行返回上一级"));
        e.Handled = true;
    }

    private void OnRowCheckChanged(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        int checkedCount = viewModel == null ? 0 : viewModel.GetCheckedRows().Count;
        UiLogger.Log("程序行为", "列表勾选状态变化：已勾选条目数=" + checkedCount + " 结果=成功，已刷新右侧信息窗格");
        this.UpdateInfoPane();
    }

    private void OnMenuOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            this.ViewModel?.OpenEntry(row);
            UiLogger.Log("程序行为", "右键菜单打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已进入该目录" : "已按标签页规则打开该条目"));
        }
    }

    private void OnMenuOpenInNewTabClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        FileTabViewModel? viewModel = this.ViewModel;
        if (mainViewModel == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单在新标签打开：主窗口或标签视图模型为空 结果=失败，本次操作取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            if (row.IsDirectory)
            {
                mainViewModel.AddTab(row.FullPath);
            }
            else
            {
                viewModel.OpenEntry(row);
            }
            UiLogger.Log("程序行为", "右键菜单在新标签打开：行路径=" + row.FullPath + " 条目类型=" + (row.IsDirectory ? "目录" : "文件")
                + " 结果=" + (row.IsDirectory ? "成功，已新建标签打开该目录" : "已按标签页规则打开该文件"));
        }
    }

    private void OnMenuCopyClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单复制：未找到主窗口视图模型 结果=失败，本次复制取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            mainViewModel.CopyPathsToClipboard(new List<string> { row.FullPath });
            UiLogger.Log("程序行为", "右键菜单复制：行路径=" + row.FullPath
                + " 结果=成功，已把该路径放入剪贴板，粘贴时生效");
        }
    }

    private void OnMenuCutClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainViewModel = this.MainViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单剪切：未找到主窗口视图模型 结果=失败，本次剪切取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            mainViewModel.CutPathsToClipboard(new List<string> { row.FullPath });
            UiLogger.Log("程序行为", "右键菜单剪切：行路径=" + row.FullPath
                + " 结果=成功，已把该路径放入剪贴板，粘贴时将移动该条目");
        }
    }

    /// <summary>右键"压缩为…"：选择保存位置后把选中（或右键）条目压缩为新压缩包。</summary>
    private async void OnMenuCompressClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "右键菜单压缩为：标签视图模型或所属窗口为空 结果=失败，本次压缩取消");
            return;
        }
        if (sender is not MenuItem menuItem || menuItem.DataContext is not FileRow row)
        {
            UiLogger.Log("程序行为", "右键菜单压缩为：菜单项未关联列表条目 结果=失败，本次压缩取消");
            return;
        }
        List<FileRow> rows = CollectTargetRows(viewModel, row);
        UiLogger.Log("程序行为", "右键菜单压缩为：右键行路径=" + row.FullPath + " 操作条目数=" + rows.Count
            + " 结果=成功，已弹出保存位置选择对话框");

        string suggestedName = rows.Count == 1
            ? Path.GetFileNameWithoutExtension(rows[0].DisplayName)
            : "新建压缩包";
        if (string.IsNullOrWhiteSpace(suggestedName))
        {
            suggestedName = "新建压缩包";
        }
        FilePickerSaveOptions options = new FilePickerSaveOptions();
        options.Title = "压缩为";
        options.SuggestedFileName = suggestedName;
        options.DefaultExtension = "zip";
        options.FileTypeChoices = new List<FilePickerFileType>
        {
            new FilePickerFileType("压缩包（ZIP）") { Patterns = new List<string> { "*.zip" } },
            new FilePickerFileType("7z 压缩包") { Patterns = new List<string> { "*.7z" } },
            new FilePickerFileType("tar 归档（含 gzip / bzip2 外层压缩）")
            {
                Patterns = new List<string> { "*.tar", "*.tar.gz", "*.tgz", "*.tar.bz2", "*.tbz2" },
            },
            new FilePickerFileType("单文件压缩流（gzip / bzip2 / xz / zstd）")
            {
                Patterns = new List<string> { "*.gz", "*.bz2", "*.xz", "*.zst" },
            },
        };
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(options);
        if (file == null)
        {
            UiLogger.Log("程序行为", "右键菜单压缩为：右键行路径=" + row.FullPath
                + " 结果=用户取消，未选择保存位置，本次压缩未执行");
            return;
        }
        string? archivePath = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(archivePath))
        {
            UiLogger.Log("程序行为", "右键菜单压缩为：右键行路径=" + row.FullPath
                + " 结果=失败，所选保存位置不是本地路径，本次压缩未执行");
            return;
        }
        if (string.IsNullOrEmpty(Path.GetExtension(archivePath)))
        {
            archivePath = archivePath + ".zip";
        }
        viewModel.CompressSelection(rows, archivePath);
    }

    /// <summary>右键"解压到…"：选择目标文件夹后解压压缩包，或把包内条目解出到该文件夹。</summary>
    private async void OnMenuExtractClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        TopLevel? owner = TopLevel.GetTopLevel(this);
        if (viewModel == null || owner == null)
        {
            UiLogger.Log("程序行为", "右键菜单解压到：标签视图模型或所属窗口为空 结果=失败，本次解压取消");
            return;
        }
        if (sender is not MenuItem menuItem || menuItem.DataContext is not FileRow row)
        {
            UiLogger.Log("程序行为", "右键菜单解压到：菜单项未关联列表条目 结果=失败，本次解压取消");
            return;
        }
        if (!viewModel.IsExtractableRow(row))
        {
            UiLogger.Log("程序行为", "右键菜单解压到：行路径=" + row.FullPath
                + " 结果=失败，该条目不是压缩包，已提示用户");
            viewModel.ShowMessage("请右键压缩包条目使用「解压到…」");
            return;
        }
        List<FileRow> rows = CollectTargetRows(viewModel, row);
        UiLogger.Log("程序行为", "右键菜单解压到：右键行路径=" + row.FullPath + " 操作条目数=" + rows.Count
            + " 结果=成功，已弹出目标文件夹选择对话框");

        FolderPickerOpenOptions options = new FolderPickerOpenOptions();
        options.Title = "解压到";
        options.AllowMultiple = false;
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count == 0)
        {
            UiLogger.Log("程序行为", "右键菜单解压到：右键行路径=" + row.FullPath
                + " 结果=用户取消，未选择目标文件夹，本次解压未执行");
            return;
        }
        string? targetDirectory = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(targetDirectory))
        {
            UiLogger.Log("程序行为", "右键菜单解压到：右键行路径=" + row.FullPath
                + " 结果=失败，所选目标文件夹不是本地路径，本次解压未执行");
            return;
        }
        viewModel.ExtractSelectionTo(rows, targetDirectory);
    }

    /// <summary>操作目标行：有勾选条目时用全部勾选行，否则用右键所在行。</summary>
    private static List<FileRow> CollectTargetRows(FileTabViewModel viewModel, FileRow contextRow)
    {
        List<FileRow> checkedRows = viewModel.GetCheckedRows();
        if (checkedRows.Count > 0)
        {
            return checkedRows;
        }
        List<FileRow> rows = new List<FileRow>();
        rows.Add(contextRow);
        return rows;
    }

    /// <summary>修改远程文件权限（仅远程连接支持，输入八进制权限如 644）。</summary>
    private async void OnMenuChangePermissionsClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单修改权限：所属窗口或标签视图模型为空 结果=失败，本次修改取消");
            return;
        }
        if (viewModel.PermissionChanger == null)
        {
            UiLogger.Log("程序行为", "右键菜单修改权限：当前标签不是远程连接 结果=失败，无法修改权限");
            viewModel.ShowMessage("当前标签不是远程连接，无法修改权限");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            string? modeText = await InputDialog.Show(owner, "修改权限", "请输入八进制权限（如 644、755）：", "644");
            if (string.IsNullOrWhiteSpace(modeText))
            {
                UiLogger.Log("程序行为", "右键菜单修改权限：行路径=" + row.FullPath
                    + " 结果=用户取消，未输入权限，本次修改未执行");
                return;
            }
            if (!TabTaskExplorer.App.Services.WebWorkspace.TryParseOctalMode(modeText.Trim(), out int octalMode))
            {
                UiLogger.Log("程序行为", "右键菜单修改权限：行路径=" + row.FullPath + " 权限输入=" + modeText.Trim()
                    + " 结果=失败，权限格式无效，本次修改未执行");
                viewModel.ShowMessage("权限格式无效（0~777 八进制）");
                return;
            }
            string targetPath = row.FullPath;
            try
            {
                await System.Threading.Tasks.Task.Run(delegate
                {
                    viewModel.PermissionChanger.ChangePermissions(targetPath, octalMode);
                });
                viewModel.ShowMessage("权限已修改：" + row.DisplayName + " → " + modeText.Trim());
                UiLogger.Log("程序行为", "右键菜单修改权限：行路径=" + targetPath + " 新权限=" + modeText.Trim()
                    + " 结果=成功，已修改远程文件权限");
            }
            catch (Exception error)
            {
                viewModel.ShowMessage("修改权限失败：" + error.Message);
                UiLogger.LogError("程序行为", "右键菜单修改权限：行路径=" + targetPath + " 新权限=" + modeText.Trim()
                    + " 结果=失败", error);
            }
        }
    }

    private async void OnMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Controls.Window? owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        FileTabViewModel? viewModel = this.ViewModel;
        if (owner == null || viewModel == null)
        {
            UiLogger.Log("程序行为", "右键菜单重命名：所属窗口或标签视图模型为空 结果=失败，本次重命名取消");
            return;
        }
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            string? newName = await InputDialog.Show(owner, "重命名", "请输入新的名称：", row.DisplayName);
            if (!string.IsNullOrWhiteSpace(newName) && newName != row.DisplayName)
            {
                viewModel.RenameRow(row, newName);
                UiLogger.Log("程序行为", "右键菜单重命名：行路径=" + row.FullPath + " 新名称=" + newName
                    + " 结果=成功，已提交重命名请求");
            }
            else
            {
                UiLogger.Log("程序行为", "右键菜单重命名：行路径=" + row.FullPath
                    + " 新名称=" + (string.IsNullOrWhiteSpace(newName) ? "未输入" : newName)
                    + " 结果=未执行重命名，新名称为空或与原名称相同");
            }
        }
    }

    private void OnMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileRow row)
        {
            FileTabViewModel? viewModel = this.ViewModel;
            if (viewModel == null)
            {
                UiLogger.Log("程序行为", "右键菜单删除：未找到标签视图模型 结果=失败，本次删除取消");
                return;
            }
            viewModel.DeleteRow(row);
            UiLogger.Log("程序行为", "右键菜单删除：行路径=" + row.FullPath
                + " 结果=成功，已提交删除请求");
        }
    }

    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null || sender is not Button button || button.Tag is not string sortField)
        {
            UiLogger.Log("程序行为", "列表排序切换：视图模型或表头参数缺失 结果=失败，本次排序未执行");
            return;
        }
        viewModel.SetSortField(sortField);
        this.UpdateSortArrows();
        UiLogger.Log("程序行为", "列表排序切换：排序字段=" + sortField
            + " 排序方向=" + (viewModel.SortAscending ? "升序" : "降序")
            + " 结果=成功，已按该字段重排列表并刷新表头箭头");
    }

    /// <summary>面板加载后按默认排序状态刷新表头箭头（默认按名称升序）。</summary>
    private void OnPanelLoaded(object? sender, RoutedEventArgs e)
    {
        this.UpdateSortArrows();
    }

    /// <summary>按当前排序状态刷新四个表头的升序 / 降序箭头（未排序的列不显示箭头）。</summary>
    private void UpdateSortArrows()
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        string field = viewModel.SortField;
        string arrow = viewModel.SortAscending ? "▲" : "▼";
        this.ArrowName.Text = field == "Name" ? arrow : string.Empty;
        this.ArrowModified.Text = field == "Modified" ? arrow : string.Empty;
        this.ArrowType.Text = field == "Type" ? arrow : string.Empty;
        this.ArrowSize.Text = field == "Size" ? arrow : string.Empty;
    }

    /// <summary>刷新右侧信息窗格内容。</summary>
    private void UpdateInfoPane()
    {
        FileTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        FileRow? row = viewModel.FindSingleCheckedRow();
        if (row == null)
        {
            this.InfoNameText.Text = "未勾选条目";
            this.InfoTypeText.Text = string.Empty;
            this.InfoSizeText.Text = string.Empty;
            this.InfoModifiedText.Text = string.Empty;
            this.InfoPathText.Text = string.Empty;
            return;
        }
        this.InfoNameText.Text = row.DisplayName;
        this.InfoTypeText.Text = row.TypeText;
        this.InfoSizeText.Text = row.SizeText;
        this.InfoModifiedText.Text = row.ModifiedText;
        this.InfoPathText.Text = row.FullPath;
    }
}
