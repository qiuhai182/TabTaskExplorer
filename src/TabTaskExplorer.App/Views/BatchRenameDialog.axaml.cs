using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>批量改名对话框：查找替换、前后缀、序号与扩展名，带实时预览。</summary>
public partial class BatchRenameDialog : Window
{
    private readonly List<FileRow> targetRows;
    private readonly ObservableCollection<RenamePreviewRow> previewItems = new ObservableCollection<RenamePreviewRow>();

    /// <summary>生成的改名计划：entry[0] 为旧路径，entry[1] 为新名称。</summary>
    public List<string[]> RenamePlan { get; private set; } = new List<string[]>();

    public BatchRenameDialog()
    {
        this.InitializeComponent();
    }

    /// <summary>以勾选的条目初始化对话框。</summary>
    public BatchRenameDialog(List<FileRow> targetRows) : this()
    {
        this.targetRows = targetRows;
        this.PreviewList.ItemsSource = this.previewItems;
        this.RebuildPreview();
    }

    /// <summary>收集界面上的改名选项。</summary>
    private BatchRenameOptions CollectOptions()
    {
        BatchRenameOptions options = new BatchRenameOptions();
        options.FindText = this.FindBox.Text ?? string.Empty;
        options.ReplaceText = this.ReplaceBox.Text ?? string.Empty;
        options.PrefixText = this.PrefixBox.Text ?? string.Empty;
        options.SuffixText = this.SuffixBox.Text ?? string.Empty;
        options.UseSequence = this.SequenceCheck.IsChecked == true;
        int sequenceStart = 1;
        int.TryParse(this.SequenceStartBox.Text, out sequenceStart);
        options.SequenceStart = sequenceStart;
        options.NewExtension = this.ExtensionBox.Text ?? string.Empty;
        return options;
    }

    /// <summary>按当前选项重建改名计划与预览列表。</summary>
    private void RebuildPreview()
    {
        this.previewItems.Clear();
        this.RenamePlan = new List<string[]>();
        if (this.targetRows == null)
        {
            return;
        }

        BatchRenameOptions options = this.CollectOptions();
        int sequence = options.SequenceStart;
        foreach (FileRow row in this.targetRows)
        {
            string originalName = Path.GetFileName(row.FullPath);
            string newName = BatchRenamePlanner.BuildNewName(originalName, options, sequence);
            if (options.UseSequence)
            {
                sequence++;
            }

            this.RenamePlan.Add(new string[] { row.FullPath, newName });

            RenamePreviewRow previewRow = new RenamePreviewRow();
            previewRow.OldText = originalName;
            previewRow.NewText = newName;
            this.previewItems.Add(previewRow);
        }
    }

    private void OnOptionChanged(object? sender, RoutedEventArgs e)
    {
        this.RebuildPreview();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        this.RebuildPreview();
        this.Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        this.Close(false);
    }
}

/// <summary>批量改名预览中的一行。</summary>
public class RenamePreviewRow
{
    /// <summary>旧名称。</summary>
    public string OldText { get; set; } = string.Empty;

    /// <summary>新名称。</summary>
    public string NewText { get; set; } = string.Empty;
}
