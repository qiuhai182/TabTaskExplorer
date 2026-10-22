using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>编码转换对话框：把文件名 / 文件夹名从源编码转换到目标编码，带实时预览与递归开关。</summary>
public partial class EncodeConvertDialog : Window
{
    private readonly IFileSystemProvider? provider;
    private readonly List<string> targetPaths = new List<string>();
    private readonly ObservableCollection<EncodePreviewRow> previewItems = new ObservableCollection<EncodePreviewRow>();

    /// <summary>生成的转换计划（仅含原因为空且名称有变化的条目）：entry[0] 为旧路径，entry[1] 为新名称。</summary>
    public List<string[]> EncodeConvertPlan { get; private set; } = new List<string[]>();

    public EncodeConvertDialog()
    {
        this.InitializeComponent();
        UiLogger.AttachWindow(this);
    }

    /// <summary>以勾选的路径初始化对话框。</summary>
    public EncodeConvertDialog(IFileSystemProvider provider, List<string> targetPaths) : this()
    {
        this.provider = provider;
        this.targetPaths.AddRange(targetPaths);
        this.PreviewList.ItemsSource = this.previewItems;
        this.FromBox.ItemsSource = FileNameEncodingConverter.GetSupportedEncodings();
        this.ToBox.ItemsSource = FileNameEncodingConverter.GetSupportedEncodings();
        this.FromBox.SelectedItem = FindChoice(936);
        this.ToBox.SelectedItem = FindChoice(65001);
        this.RebuildPreview();
    }

    /// <summary>按代码页查找编码选项。</summary>
    private static EncodingChoice? FindChoice(int codePage)
    {
        foreach (EncodingChoice choice in FileNameEncodingConverter.GetSupportedEncodings())
        {
            if (choice.CodePage == codePage)
            {
                return choice;
            }
        }
        return null;
    }

    /// <summary>按当前选项重建转换计划与预览列表。</summary>
    private void RebuildPreview()
    {
        this.previewItems.Clear();
        this.EncodeConvertPlan = new List<string[]>();
        EncodingChoice? fromChoice = this.FromBox.SelectedItem as EncodingChoice;
        EncodingChoice? toChoice = this.ToBox.SelectedItem as EncodingChoice;
        if (this.provider == null || fromChoice == null || toChoice == null)
        {
            this.SummaryText.Text = "请选择源编码与目标编码";
            return;
        }

        bool recursive = this.RecursiveCheck.IsChecked == true;
        List<EncodeConvertEntry> plan = FileNameEncodingConverter.BuildPlan(
            this.provider, this.targetPaths, recursive, fromChoice.CodePage, toChoice.CodePage);
        int executableCount = 0;
        int failedCount = 0;
        foreach (EncodeConvertEntry entry in plan)
        {
            string originalName = FileNameEncodingConverter.GetEntryName(entry.OldPath);
            bool changed = entry.NewName != originalName;

            EncodePreviewRow previewRow = new EncodePreviewRow();
            previewRow.OldText = entry.OldPath;
            previewRow.NewText = entry.NewName;
            previewRow.Reason = entry.Reason;
            this.previewItems.Add(previewRow);

            if (entry.Reason.Length > 0)
            {
                failedCount++;
                continue;
            }
            if (changed)
            {
                executableCount++;
                this.EncodeConvertPlan.Add(new string[] { entry.OldPath, entry.NewName });
            }
        }
        this.SummaryText.Text = "共 " + plan.Count + " 项，可执行 " + executableCount
            + " 项，无法转换 " + failedCount + " 项";
        UiLogger.Log("编码转换", "对话框预览：源编码=" + fromChoice.Name
            + " 目标编码=" + toChoice.Name + " 递归=" + (recursive ? "是" : "否")
            + " 计划条数=" + plan.Count + " 可执行条数=" + executableCount
            + " 无法转换条数=" + failedCount);
    }

    private void OnOptionChanged(object? sender, SelectionChangedEventArgs e)
    {
        this.RebuildPreview();
        UiLogger.Log("编码转换", "源编码或目标编码下拉选择已变更：已按当前编码重建转换预览");
    }

    private void OnOptionChanged(object? sender, RoutedEventArgs e)
    {
        this.RebuildPreview();
        UiLogger.Log("编码转换", "递归处理选项已点击：已按当前递归开关重建转换预览");
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        this.RebuildPreview();
        if (this.EncodeConvertPlan.Count == 0)
        {
            this.SummaryText.Text = "没有可执行的转换项";
            UiLogger.Log("编码转换", "点击执行转换被拒绝：没有可执行的转换项 结果=失败，未执行任何修改");
            return;
        }
        EncodingChoice? fromChoice = this.FromBox.SelectedItem as EncodingChoice;
        EncodingChoice? toChoice = this.ToBox.SelectedItem as EncodingChoice;
        bool recursive = this.RecursiveCheck.IsChecked == true;
        UiLogger.Log("编码转换", "编码转换已确认：源编码=" + (fromChoice == null ? "未选择" : fromChoice.Name)
            + " 目标编码=" + (toChoice == null ? "未选择" : toChoice.Name)
            + " 递归=" + (recursive ? "是" : "否")
            + " 影响条数=" + this.EncodeConvertPlan.Count
            + " 结果=成功，已把转换计划交回调用方执行");
        this.Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("编码转换", "点击取消：已关闭对话框，未执行任何转换");
        this.Close(false);
    }
}

/// <summary>编码转换预览中的一行。</summary>
public class EncodePreviewRow
{
    /// <summary>旧完整路径。</summary>
    public string OldText { get; set; } = string.Empty;

    /// <summary>转换后的新名称。</summary>
    public string NewText { get; set; } = string.Empty;

    /// <summary>原因：为空表示可执行。</summary>
    public string Reason { get; set; } = string.Empty;
}
