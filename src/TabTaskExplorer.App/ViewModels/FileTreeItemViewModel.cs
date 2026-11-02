using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// IDE 资源树节点：目录懒加载子节点，双击文件请求编辑器标签打开。
/// 节点自带文件系统提供者与远程内容访问能力，因此本机与远程目录可混在同一棵树中。
/// </summary>
public class FileTreeItemViewModel : INotifyPropertyChanged
{
    private readonly IFileSystemProvider provider;
    private readonly bool showHiddenFiles;
    private readonly string connectionName;
    private bool childrenLoaded;
    private bool isLoadingChildren;
    private bool isExpanded;
    private bool isSelected;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>节点显示名称。</summary>
    public string Name { get; private set; }

    /// <summary>节点完整路径（本机为磁盘路径，远程为远端绝对路径）。</summary>
    public string FullPath { get; private set; }

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; private set; }

    /// <summary>是否为远程节点。</summary>
    public bool IsRemote { get; private set; }

    /// <summary>是否为占位节点（加载中 / 无法读取）。</summary>
    public bool IsPlaceholder { get; private set; }

    /// <summary>文件系统提供者（子节点与内容读取共用）。</summary>
    public IFileSystemProvider Provider
    {
        get { return this.provider; }
    }

    /// <summary>远程文件内容访问能力；本机节点为 null。</summary>
    public IFileContentAccess? ContentAccess { get; private set; }

    /// <summary>所属连接名；本机节点为空文本。</summary>
    public string ConnectionName
    {
        get { return this.connectionName; }
    }

    /// <summary>子节点集合（懒加载）。</summary>
    public ObservableCollection<FileTreeItemViewModel> Children { get; } = new ObservableCollection<FileTreeItemViewModel>();

    /// <summary>类型图标分类键（见 <see cref="FileIconCatalog"/>）。</summary>
    public string IconCategory { get; private set; } = FileIconCatalog.File;

    /// <summary>类型图标颜色（十六进制）。</summary>
    public string IconColor { get; private set; } = FileIconCatalog.GetAccent(FileIconCatalog.File);

    /// <summary>是否展开：展开时触发一次子节点懒加载。</summary>
    public bool IsExpanded
    {
        get { return this.isExpanded; }
        set
        {
            this.isExpanded = value;
            this.RaisePropertyChanged(nameof(this.IsExpanded));
            if (value)
            {
                this.LoadChildren();
            }
        }
    }

    /// <summary>是否被选中。</summary>
    public bool IsSelected
    {
        get { return this.isSelected; }
        set
        {
            this.isSelected = value;
            this.RaisePropertyChanged(nameof(this.IsSelected));
        }
    }

    /// <summary>创建真实节点（名称 + 路径直接给出）。</summary>
    public FileTreeItemViewModel(
        IFileSystemProvider provider,
        bool showHiddenFiles,
        string name,
        string fullPath,
        bool isDirectory,
        bool isRemote,
        IFileContentAccess? contentAccess,
        string connectionName)
    {
        this.provider = provider;
        this.showHiddenFiles = showHiddenFiles;
        this.Name = name;
        this.FullPath = fullPath;
        this.IsDirectory = isDirectory;
        this.IsRemote = isRemote;
        this.ContentAccess = contentAccess;
        this.connectionName = connectionName;
        this.ApplyIcon();
        if (this.IsDirectory)
        {
            // 先放一个占位子节点，让树控件显示展开箭头
            this.Children.Add(CreatePlaceholder("加载中……"));
        }
        else
        {
            this.childrenLoaded = true;
        }
    }

    /// <summary>从内核目录条目创建子节点。</summary>
    public FileTreeItemViewModel(
        IFileSystemProvider provider,
        bool showHiddenFiles,
        FileEntry entry,
        bool isRemote,
        IFileContentAccess? contentAccess,
        string connectionName)
        : this(provider, showHiddenFiles, entry.Name, entry.FullPath, entry.IsDirectory, isRemote, contentAccess, connectionName)
    {
    }

    /// <summary>创建占位节点（加载中 / 无法读取提示）。</summary>
    public static FileTreeItemViewModel CreatePlaceholder(string text)
    {
        FileTreeItemViewModel placeholder = new FileTreeItemViewModel(
            new NullFileSystemProvider(), true, text, string.Empty, false, false, null, string.Empty);
        placeholder.IsPlaceholder = true;
        placeholder.childrenLoaded = true;
        placeholder.IconCategory = FileIconCatalog.File;
        return placeholder;
    }

    /// <summary>展开时按需加载子节点（后台枚举，界面线程应用）。</summary>
    public void LoadChildren()
    {
        if (!this.IsDirectory || this.childrenLoaded || this.isLoadingChildren || this.IsPlaceholder)
        {
            return;
        }
        this.isLoadingChildren = true;
        string targetPath = this.FullPath;
        IFileSystemProvider currentProvider = this.provider;
        bool includeHidden = this.showHiddenFiles;
        bool remote = this.IsRemote;
        IFileContentAccess? access = this.ContentAccess;
        string currentConnection = this.connectionName;

        Task.Run(delegate
        {
            List<FileTreeItemViewModel> loaded = new List<FileTreeItemViewModel>();
            string? errorText = null;
            try
            {
                DirectoryListingOptions options = new DirectoryListingOptions();
                options.ShowHiddenFiles = includeHidden;
                foreach (FileEntry entry in currentProvider.ListDirectory(targetPath, options))
                {
                    loaded.Add(new FileTreeItemViewModel(currentProvider, includeHidden, entry, remote, access, currentConnection));
                }
            }
            catch (Exception error)
            {
                errorText = error.Message;
            }
            Dispatcher.UIThread.Post(delegate { this.ApplyLoadedChildren(loaded, errorText); });
        });
    }

    /// <summary>强制重新加载子节点（刷新工作区时调用）。</summary>
    public void ForceReload()
    {
        if (!this.IsDirectory || this.IsPlaceholder)
        {
            return;
        }
        this.childrenLoaded = false;
        this.Children.Clear();
        this.Children.Add(CreatePlaceholder("加载中……"));
        this.isLoadingChildren = false;
        if (this.isExpanded)
        {
            this.LoadChildren();
        }
    }

    /// <summary>在界面线程上应用后台枚举结果。</summary>
    private void ApplyLoadedChildren(List<FileTreeItemViewModel> loaded, string? errorText)
    {
        loaded.Sort(new Comparison<FileTreeItemViewModel>(CompareItems));
        this.Children.Clear();
        foreach (FileTreeItemViewModel item in loaded)
        {
            this.Children.Add(item);
        }
        if (errorText != null)
        {
            this.Children.Add(CreatePlaceholder("无法读取：" + errorText));
        }
        this.childrenLoaded = true;
        this.isLoadingChildren = false;
    }

    /// <summary>子节点排序：文件夹在前，其余按名称不区分大小写。</summary>
    private static int CompareItems(FileTreeItemViewModel left, FileTreeItemViewModel right)
    {
        if (left.IsDirectory != right.IsDirectory)
        {
            return left.IsDirectory ? -1 : 1;
        }
        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按类型设置图标。</summary>
    private void ApplyIcon()
    {
        if (this.IsDirectory)
        {
            this.IconCategory = FileIconCatalog.Folder;
            this.IconColor = FileIconCatalog.GetAccent(FileIconCatalog.Folder);
            return;
        }
        string category = FileIconCatalog.GetCategoryForName(this.Name);
        this.IconCategory = category;
        this.IconColor = FileIconCatalog.GetAccent(category);
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

/// <summary>占位节点使用的空文件系统提供者，任何调用均返回空列表。</summary>
internal class NullFileSystemProvider : IFileSystemProvider
{
    /// <inheritdoc />
    public IReadOnlyList<FileEntry> ListDirectory(string directoryPath)
    {
        return new List<FileEntry>();
    }

    /// <inheritdoc />
    public IReadOnlyList<FileEntry> ListDirectory(string directoryPath, DirectoryListingOptions options)
    {
        return new List<FileEntry>();
    }

    /// <inheritdoc />
    public void CreateDirectory(string directoryPath)
    {
    }

    /// <inheritdoc />
    public void Rename(string path, string newName)
    {
    }

    /// <inheritdoc />
    public void Delete(string path)
    {
    }

    /// <inheritdoc />
    public void Copy(string sourcePath, string targetDirectory)
    {
    }

    /// <inheritdoc />
    public void Move(string sourcePath, string targetDirectory)
    {
    }
}
