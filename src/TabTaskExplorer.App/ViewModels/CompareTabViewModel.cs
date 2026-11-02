using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>文件比对打开请求：左右两个文件路径。</summary>
public class ComparePairEventArgs : EventArgs
{
    public ComparePairEventArgs(string leftPath, string rightPath)
    {
        this.LeftPath = leftPath;
        this.RightPath = rightPath;
    }

    /// <summary>左侧文件路径。</summary>
    public string LeftPath { get; }

    /// <summary>右侧文件路径。</summary>
    public string RightPath { get; }
}

/// <summary>
/// 比对标签页：目录比对（递归双栏状态）与文件比对（按行对齐、行内差异高亮）。
/// 对应 WinMerge 的 Folder Compare 与 File Compare 两种模式，纯托管实现，跨平台行为一致。
/// </summary>
public class CompareTabViewModel : INotifyPropertyChanged, ITabView
{
    /// <summary>目录比对模式标识。</summary>
    public const string ModeDirectoryValue = "directory";

    /// <summary>文件比对模式标识。</summary>
    public const string ModeFileValue = "file";

    private readonly AppSettings settings;
    private readonly ITextFileService textFileService;
    private readonly DirectoryCompareEngine directoryEngine = new DirectoryCompareEngine();
    private readonly TextDiffEngine textEngine = new TextDiffEngine();

    private string leftPathInput = string.Empty;
    private string rightPathInput = string.Empty;
    private string leftPath = string.Empty;
    private string rightPath = string.Empty;
    private string mode = ModeDirectoryValue;
    private string title = "比对";
    private string summaryText = "请输入左右两侧路径";
    private string detailText = string.Empty;
    private string leftMetaText = string.Empty;
    private string rightMetaText = string.Empty;
    private bool isLoading;
    private bool showOnlyDifferences;
    private bool includeSubDirectories = true;
    private bool compareFileContent;
    private bool closed;

    private List<DirectoryCompareRow> allDirectoryRows = new List<DirectoryCompareRow>();
    private List<FileDiffRow> allFileRows = new List<FileDiffRow>();
    private IReadOnlyList<DirectoryCompareRow> directoryRows = new List<DirectoryCompareRow>();
    private IReadOnlyList<FileDiffRow> fileRows = new List<FileDiffRow>();
    private DirectoryCompareRow? selectedDirectoryRow;
    private FileDiffRow? selectedFileRow;

    private List<object> activeDifferences = new List<object>();
    private int currentDifferenceIndex = -1;
    private string differencePositionText = "没有差异";
    private bool suppressSelectionSync;

    private int loadGeneration;
    private int appliedGeneration = -1;

    /// <summary>文件比对行打开请求：双击目录比对中的文件行时，由主窗口新开文件比对标签。</summary>
    public event EventHandler<ComparePairEventArgs>? FileCompareRequested;

    /// <summary>属性变化通知。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>标签类型标识：比对。</summary>
    public string Kind
    {
        get { return TabKinds.Compare; }
    }

    /// <summary>标签标题：目录比对 / 文件比对 + 两侧名称。</summary>
    public string Title
    {
        get { return this.title; }
        private set
        {
            this.title = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    /// <summary>左侧路径输入框内容（可编辑后重新比对）。</summary>
    public string LeftPathInput
    {
        get { return this.leftPathInput; }
        set
        {
            this.leftPathInput = value ?? string.Empty;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftPathInput)));
        }
    }

    /// <summary>右侧路径输入框内容（可编辑后重新比对）。</summary>
    public string RightPathInput
    {
        get { return this.rightPathInput; }
        set
        {
            this.rightPathInput = value ?? string.Empty;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightPathInput)));
        }
    }

    /// <summary>当前生效的左侧路径。</summary>
    public string LeftPath
    {
        get { return this.leftPath; }
        private set
        {
            this.leftPath = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftPath)));
        }
    }

    /// <summary>当前生效的右侧路径。</summary>
    public string RightPath
    {
        get { return this.rightPath; }
        private set
        {
            this.rightPath = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightPath)));
        }
    }

    /// <summary>比对模式：directory / file。</summary>
    public string Mode
    {
        get { return this.mode; }
        private set
        {
            this.mode = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirectoryMode)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFileMode)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModeText)));
        }
    }

    /// <summary>是否为目录比对模式。</summary>
    public bool IsDirectoryMode
    {
        get { return this.mode == ModeDirectoryValue; }
    }

    /// <summary>是否为文件比对模式。</summary>
    public bool IsFileMode
    {
        get { return this.mode == ModeFileValue; }
    }

    /// <summary>模式中文名称。</summary>
    public string ModeText
    {
        get { return this.IsDirectoryMode ? "目录比对" : "文件比对"; }
    }

    /// <summary>底部摘要：条目统计或错误提示。</summary>
    public string SummaryText
    {
        get { return this.summaryText; }
        private set
        {
            this.summaryText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryText)));
        }
    }

    /// <summary>补充说明：编码、行数或降级提示。</summary>
    public string DetailText
    {
        get { return this.detailText; }
        private set
        {
            this.detailText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));
        }
    }

    /// <summary>左侧元信息（文件模式显示编码与行数）。</summary>
    public string LeftMetaText
    {
        get { return this.leftMetaText; }
        private set
        {
            this.leftMetaText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftMetaText)));
        }
    }

    /// <summary>右侧元信息（文件模式显示编码与行数）。</summary>
    public string RightMetaText
    {
        get { return this.rightMetaText; }
        private set
        {
            this.rightMetaText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightMetaText)));
        }
    }

    /// <summary>是否正在后台比对。</summary>
    public bool IsLoading
    {
        get { return this.isLoading; }
        private set
        {
            this.isLoading = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
        }
    }

    /// <summary>是否只显示差异行。</summary>
    public bool ShowOnlyDifferences
    {
        get { return this.showOnlyDifferences; }
        set
        {
            this.showOnlyDifferences = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowOnlyDifferences)));
            this.ApplyCurrentFilter();
        }
    }

    /// <summary>目录比对是否包含子目录。</summary>
    public bool IncludeSubDirectories
    {
        get { return this.includeSubDirectories; }
        set
        {
            if (this.includeSubDirectories == value)
            {
                return;
            }
            this.includeSubDirectories = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncludeSubDirectories)));
            this.Reload();
        }
    }

    /// <summary>目录比对是否按文件内容比较。</summary>
    public bool CompareFileContent
    {
        get { return this.compareFileContent; }
        set
        {
            if (this.compareFileContent == value)
            {
                return;
            }
            this.compareFileContent = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompareFileContent)));
            this.Reload();
        }
    }

    /// <summary>目录比对可见行。</summary>
    public IReadOnlyList<DirectoryCompareRow> DirectoryRows
    {
        get { return this.directoryRows; }
        private set
        {
            this.directoryRows = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DirectoryRows)));
        }
    }

    /// <summary>文件比对可见行。</summary>
    public IReadOnlyList<FileDiffRow> FileRows
    {
        get { return this.fileRows; }
        private set
        {
            this.fileRows = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileRows)));
        }
    }

    /// <summary>目录比对当前选中行。</summary>
    public DirectoryCompareRow? SelectedDirectoryRow
    {
        get { return this.selectedDirectoryRow; }
        set
        {
            this.selectedDirectoryRow = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDirectoryRow)));
            if (!this.suppressSelectionSync && value != null && value.IsDifferent)
            {
                this.SyncDifferenceIndex(value);
            }
        }
    }

    /// <summary>文件比对当前选中行。</summary>
    public FileDiffRow? SelectedFileRow
    {
        get { return this.selectedFileRow; }
        set
        {
            this.selectedFileRow = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFileRow)));
            if (!this.suppressSelectionSync && value != null && value.IsDifferent)
            {
                this.SyncDifferenceIndex(value);
            }
        }
    }

    /// <summary>差异位置文字（差异 3 / 12）。</summary>
    public string DifferencePositionText
    {
        get { return this.differencePositionText; }
        private set
        {
            this.differencePositionText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DifferencePositionText)));
        }
    }

    /// <summary>创建比对标签。</summary>
    public CompareTabViewModel(AppSettings appSettings, ITextFileService textFileService,
        string leftPath, string rightPath)
    {
        this.settings = appSettings;
        this.textFileService = textFileService;
        this.leftPathInput = leftPath ?? string.Empty;
        this.rightPathInput = rightPath ?? string.Empty;
        UiLogger.Log("比对", "创建比对标签：左侧路径=" + this.leftPathInput + " 右侧路径=" + this.rightPathInput);
        this.Reload();
    }

    /// <summary>按当前输入重新比对（后台执行，不阻塞界面）。</summary>
    public void Reload()
    {
        string left = this.leftPathInput.Trim();
        string right = this.rightPathInput.Trim();
        if (left.Length == 0 && right.Length == 0)
        {
            this.SummaryText = "请输入左右两侧路径后再开始比对";
            this.DirectoryRows = new List<DirectoryCompareRow>();
            this.FileRows = new List<FileDiffRow>();
            this.activeDifferences = new List<object>();
            this.DifferencePositionText = "没有差异";
            return;
        }

        bool directoryMode = this.DetectDirectoryMode(left, right);
        this.loadGeneration++;
        int generation = this.loadGeneration;
        this.Mode = directoryMode ? ModeDirectoryValue : ModeFileValue;
        this.IsLoading = true;
        this.SummaryText = "正在比对……";
        this.DetailText = string.Empty;
        this.DirectoryRows = new List<DirectoryCompareRow>();
        this.FileRows = new List<FileDiffRow>();
        this.activeDifferences = new List<object>();
        this.DifferencePositionText = "没有差异";
        this.selectedDirectoryRow = null;
        this.selectedFileRow = null;

        UiLogger.Log("比对", "开始比对：模式=" + this.ModeText + " 左侧路径=" + left + " 右侧路径=" + right
            + " 包含子目录=" + (this.includeSubDirectories ? "是" : "否")
            + " 按内容比较=" + (this.compareFileContent ? "是" : "否"));
        Task.Run(delegate { this.ComputeInBackground(generation, left, right, directoryMode); });
    }

    /// <summary>交换左右两侧路径并重新比对。</summary>
    public void SwapSides()
    {
        string previousLeft = this.leftPathInput;
        this.leftPathInput = this.rightPathInput;
        this.rightPathInput = previousLeft;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftPathInput)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightPathInput)));
        UiLogger.Log("比对", "交换左右两侧路径：新的左侧路径=" + this.leftPathInput + " 新的右侧路径=" + this.rightPathInput);
        this.Reload();
    }

    /// <summary>目录比对返回上一层（两侧同时上移一级）。</summary>
    public void NavigateUp()
    {
        if (!this.IsDirectoryMode)
        {
            return;
        }
        string newLeft = this.GetParentPath(this.leftPath, this.leftPathInput);
        string newRight = this.GetParentPath(this.rightPath, this.rightPathInput);
        if (newLeft == this.leftPathInput && newRight == this.rightPathInput)
        {
            return;
        }
        this.leftPathInput = newLeft;
        this.rightPathInput = newRight;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftPathInput)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightPathInput)));
        UiLogger.Log("比对", "目录比对返回上一层：新的左侧路径=" + newLeft + " 新的右侧路径=" + newRight);
        this.Reload();
    }

    /// <summary>打开目录比对中的一行：目录继续进入，文件交给主窗口新开文件比对标签。</summary>
    public void OpenRow(DirectoryCompareRow row)
    {
        if (row == null)
        {
            return;
        }
        if (row.IsDirectory)
        {
            this.leftPathInput = row.LeftExists ? row.LeftPath : string.Empty;
            this.rightPathInput = row.RightExists ? row.RightPath : string.Empty;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftPathInput)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightPathInput)));
            UiLogger.Log("比对", "进入子目录：相对路径=" + row.Name + " 新的左侧路径=" + this.leftPathInput
                + " 新的右侧路径=" + this.rightPathInput);
            this.Reload();
            return;
        }
        if (!row.LeftExists && !row.RightExists)
        {
            return;
        }
        UiLogger.Log("比对", "打开文件比对：相对路径=" + row.Name + " 左侧路径=" + row.LeftPath
            + " 右侧路径=" + row.RightPath);
        EventHandler<ComparePairEventArgs>? handler = this.FileCompareRequested;
        if (handler != null)
        {
            handler(this, new ComparePairEventArgs(row.LeftPath, row.RightPath));
        }
    }

    /// <summary>选中下一处差异（循环）。</summary>
    public void SelectNextDifference()
    {
        if (this.activeDifferences.Count == 0)
        {
            return;
        }
        int next = this.currentDifferenceIndex + 1;
        if (next >= this.activeDifferences.Count || next < 0)
        {
            next = 0;
        }
        this.currentDifferenceIndex = next;
        this.SelectDifferenceAt(next);
    }

    /// <summary>选中上一处差异（循环）。</summary>
    public void SelectPreviousDifference()
    {
        if (this.activeDifferences.Count == 0)
        {
            return;
        }
        int previous = this.currentDifferenceIndex - 1;
        if (previous < 0)
        {
            previous = this.activeDifferences.Count - 1;
        }
        this.currentDifferenceIndex = previous;
        this.SelectDifferenceAt(previous);
    }

    /// <summary>把当前比对状态写入网页端快照（桌面与网页共用）。</summary>
    public void FillSnapshot(DebugTabState tabState)
    {
        tabState.CompareMode = this.Mode;
        tabState.LeftPath = this.LeftPath;
        tabState.RightPath = this.RightPath;
        tabState.LeftPathInput = this.LeftPathInput;
        tabState.RightPathInput = this.RightPathInput;
        tabState.CompareShowOnlyDifferences = this.ShowOnlyDifferences;
        tabState.CompareIncludeSubDirectories = this.IncludeSubDirectories;
        tabState.CompareFileContent = this.CompareFileContent;
        tabState.CompareDifferencePosition = this.DifferencePositionText;
        tabState.DetailText = this.DetailText;
        tabState.LeftMetaText = this.LeftMetaText;
        tabState.RightMetaText = this.RightMetaText;

        if (this.IsDirectoryMode)
        {
            tabState.TotalRows = this.DirectoryRows.Count;
            int limit = this.DirectoryRows.Count > 1000 ? 1000 : this.DirectoryRows.Count;
            for (int index = 0; index < limit; index++)
            {
                DirectoryCompareRow row = this.DirectoryRows[index];
                DebugCompareRow mirror = new DebugCompareRow();
                mirror.Name = row.Name;
                mirror.IsDirectory = row.IsDirectory;
                mirror.StatusText = row.StatusText;
                mirror.StatusKey = row.StatusKey;
                mirror.StatusColor = row.StatusColor;
                mirror.LeftInfo = row.LeftInfoText;
                mirror.RightInfo = row.RightInfoText;
                mirror.LeftPath = row.LeftPath;
                mirror.RightPath = row.RightPath;
                mirror.LeftExists = row.LeftExists;
                mirror.RightExists = row.RightExists;
                mirror.IconCategory = row.IconCategory;
                mirror.IconColor = row.IconColor;
                mirror.IsDifferent = row.IsDifferent;
                tabState.CompareRows.Add(mirror);
            }
            return;
        }

        tabState.TotalRows = this.FileRows.Count;
        int fileLimit = this.FileRows.Count > 1000 ? 1000 : this.FileRows.Count;
        for (int index = 0; index < fileLimit; index++)
        {
            FileDiffRow row = this.FileRows[index];
            DebugCompareRow mirror = new DebugCompareRow();
            mirror.Name = row.LeftLineNumberText + "/" + row.RightLineNumberText;
            mirror.IsDirectory = false;
            mirror.KindKey = row.KindKey;
            mirror.StatusKey = row.KindKey;
            mirror.StatusText = row.IsSame ? "相同" : (row.IsModified ? "修改" : (row.IsLeftOnly ? "仅左侧" : "仅右侧"));
            mirror.StatusColor = row.IsSame ? "#4CAF50" : (row.IsModified ? "#E8A317" : (row.IsLeftOnly ? "#F2B23E" : "#2F6FD0"));
            mirror.LeftNumber = row.LeftLineNumberText;
            mirror.RightNumber = row.RightLineNumberText;
            mirror.LeftText = row.LeftPrefix + row.LeftChanged + row.LeftSuffix;
            mirror.RightText = row.RightPrefix + row.RightChanged + row.RightSuffix;
            mirror.LeftPrefix = row.LeftPrefix;
            mirror.LeftChanged = row.LeftChanged;
            mirror.LeftSuffix = row.LeftSuffix;
            mirror.RightPrefix = row.RightPrefix;
            mirror.RightChanged = row.RightChanged;
            mirror.RightSuffix = row.RightSuffix;
            mirror.IsDifferent = row.IsDifferent;
            tabState.CompareRows.Add(mirror);
        }
    }

    /// <summary>关闭标签：丢弃在途比对结果。</summary>
    public void Close()
    {
        this.closed = true;
        this.loadGeneration++;
        UiLogger.Log("比对", "关闭比对标签：模式=" + this.ModeText + " 左侧路径=" + this.LeftPath + " 右侧路径=" + this.RightPath);
    }

    /// <summary>后台计算比对结果，完成后回界面线程应用。</summary>
    private void ComputeInBackground(int generation, string left, string right, bool directoryMode)
    {
        try
        {
            if (directoryMode)
            {
                DirectoryCompareOptions options = new DirectoryCompareOptions();
                options.IncludeSubDirectories = this.includeSubDirectories;
                options.IncludeHidden = this.settings.ShowHiddenFiles;
                options.CompareFileContent = this.compareFileContent;
                DirectoryCompareResult result = this.directoryEngine.Compare(left, right, options, CancellationToken.None);
                Dispatcher.UIThread.Post(delegate { this.ApplyDirectoryResult(generation, left, right, result); });
                return;
            }

            SideTextResult leftSide = this.ReadTextSide(left);
            SideTextResult rightSide = this.ReadTextSide(right);
            if (leftSide.ErrorText.Length > 0 || rightSide.ErrorText.Length > 0)
            {
                string errorText = leftSide.ErrorText.Length > 0 ? leftSide.ErrorText : rightSide.ErrorText;
                Dispatcher.UIThread.Post(delegate { this.ApplyFileError(generation, left, right, errorText); });
                return;
            }
            TextCompareResult compareResult = this.textEngine.Compare(leftSide.Text, rightSide.Text);
            Dispatcher.UIThread.Post(delegate
            {
                this.ApplyFileResult(generation, left, right, leftSide, rightSide, compareResult);
            });
        }
        catch (Exception error)
        {
            UiLogger.LogError("比对", "比对失败：模式=" + (directoryMode ? "目录比对" : "文件比对")
                + " 左侧路径=" + left + " 右侧路径=" + right, error);
            Dispatcher.UIThread.Post(delegate { this.ApplyFailure(generation, error.Message); });
        }
    }

    /// <summary>应用目录比对结果：构建行数据与统计摘要。</summary>
    private void ApplyDirectoryResult(int generation, string left, string right, DirectoryCompareResult result)
    {
        if (this.closed || generation != this.loadGeneration)
        {
            return;
        }
        this.appliedGeneration = generation;
        this.LeftPath = left;
        this.RightPath = right;
        this.LeftPathInput = left;
        this.RightPathInput = right;
        this.LeftMetaText = left.Length == 0 ? "（无）" : left;
        this.RightMetaText = right.Length == 0 ? "（无）" : right;

        List<DirectoryCompareRow> rows = new List<DirectoryCompareRow>();
        foreach (DirectoryCompareEntry entry in result.Entries)
        {
            rows.Add(this.BuildDirectoryRow(entry));
        }
        this.allDirectoryRows = rows;
        this.allFileRows = new List<FileDiffRow>();
        this.SummaryText = "共 " + result.Entries.Count + " 项：相同 " + result.SameCount
            + "，不同 " + result.DifferentCount + "，仅左侧 " + result.LeftOnlyCount
            + "，仅右侧 " + result.RightOnlyCount
            + " · " + (this.compareFileContent ? "按内容比较" : "按大小与时间比较");
        this.DetailText = "包含子目录：" + (this.includeSubDirectories ? "是" : "否")
            + " · 包含隐藏条目：" + (this.settings.ShowHiddenFiles ? "是" : "否");
        this.IsLoading = false;
        this.ApplyCurrentFilter();
        this.UpdateTitle();
        UiLogger.Log("比对", "目录比对完成：左侧路径=" + left + " 右侧路径=" + right
            + " 条目总数=" + result.Entries.Count + " 相同=" + result.SameCount
            + " 不同=" + result.DifferentCount + " 仅左侧=" + result.LeftOnlyCount
            + " 仅右侧=" + result.RightOnlyCount);
    }

    /// <summary>应用文件比对结果：构建对齐行、编码信息与统计摘要。</summary>
    private void ApplyFileResult(int generation, string left, string right, SideTextResult leftSide,
        SideTextResult rightSide, TextCompareResult result)
    {
        if (this.closed || generation != this.loadGeneration)
        {
            return;
        }
        this.appliedGeneration = generation;
        this.LeftPath = left;
        this.RightPath = right;
        this.LeftPathInput = left;
        this.RightPathInput = right;
        this.LeftMetaText = leftSide.Describe(left.Length == 0);
        this.RightMetaText = rightSide.Describe(right.Length == 0);

        List<FileDiffRow> rows = new List<FileDiffRow>();
        foreach (DiffRow diffRow in result.Rows)
        {
            rows.Add(FileDiffRow.FromDiff(diffRow));
        }
        this.allFileRows = rows;
        this.allDirectoryRows = new List<DirectoryCompareRow>();
        this.SummaryText = "共 " + result.Rows.Count + " 行：相同 " + result.SameCount
            + "，修改 " + result.ModifiedCount + "，仅左侧 " + result.LeftOnlyCount
            + "，仅右侧 " + result.RightOnlyCount;
        this.DetailText = "编码：左 " + leftSide.EncodingName + " / 右 " + rightSide.EncodingName
            + (result.UsedFallbackAlignment ? " · 文本较大，已按线性对齐（差异仍完整显示）" : string.Empty);
        this.IsLoading = false;
        this.ApplyCurrentFilter();
        this.UpdateTitle();
        UiLogger.Log("比对", "文件比对完成：左侧路径=" + left + " 右侧路径=" + right
            + " 左侧编码=" + leftSide.EncodingName + " 右侧编码=" + rightSide.EncodingName
            + " 行数=" + result.Rows.Count + " 相同=" + result.SameCount
            + " 修改=" + result.ModifiedCount + " 仅左侧=" + result.LeftOnlyCount
            + " 仅右侧=" + result.RightOnlyCount);
    }

    /// <summary>应用文件读取错误（二进制、超大或不可读）。</summary>
    private void ApplyFileError(int generation, string left, string right, string errorText)
    {
        if (this.closed || generation != this.loadGeneration)
        {
            return;
        }
        this.LeftPath = left;
        this.RightPath = right;
        this.LeftPathInput = left;
        this.RightPathInput = right;
        this.allDirectoryRows = new List<DirectoryCompareRow>();
        this.allFileRows = new List<FileDiffRow>();
        this.DirectoryRows = new List<DirectoryCompareRow>();
        this.FileRows = new List<FileDiffRow>();
        this.summaryText = "无法按文本比对：" + errorText;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryText)));
        this.DetailText = "二进制或超大文件请改用目录比对，或在外部程序中查看";
        this.IsLoading = false;
        this.UpdateTitle();
        UiLogger.Log("比对", "文件比对被拒绝：左侧路径=" + left + " 右侧路径=" + right + " 原因=" + errorText);
    }

    /// <summary>应用比对整体失败（路径非法、权限不足等）。</summary>
    private void ApplyFailure(int generation, string message)
    {
        if (this.closed || generation != this.loadGeneration)
        {
            return;
        }
        this.IsLoading = false;
        this.summaryText = "比对失败：" + message;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryText)));
    }

    /// <summary>按“仅显示差异”开关重建可见行与差异导航列表。</summary>
    private void ApplyCurrentFilter()
    {
        if (this.IsDirectoryMode)
        {
            List<DirectoryCompareRow> visibleDirectory = new List<DirectoryCompareRow>();
            foreach (DirectoryCompareRow row in this.allDirectoryRows)
            {
                if (!this.showOnlyDifferences || row.IsDifferent)
                {
                    visibleDirectory.Add(row);
                }
            }
            this.DirectoryRows = visibleDirectory;
        }
        else
        {
            List<FileDiffRow> visibleFile = new List<FileDiffRow>();
            foreach (FileDiffRow row in this.allFileRows)
            {
                if (!this.showOnlyDifferences || row.IsDifferent)
                {
                    visibleFile.Add(row);
                }
            }
            this.FileRows = visibleFile;
        }

        List<object> differences = new List<object>();
        if (this.IsDirectoryMode)
        {
            foreach (DirectoryCompareRow row in this.DirectoryRows)
            {
                if (row.IsDifferent)
                {
                    differences.Add(row);
                }
            }
        }
        else
        {
            foreach (FileDiffRow row in this.FileRows)
            {
                if (row.IsDifferent)
                {
                    differences.Add(row);
                }
            }
        }
        this.activeDifferences = differences;
        if (this.currentDifferenceIndex >= differences.Count)
        {
            this.currentDifferenceIndex = differences.Count - 1;
        }
        this.UpdateDifferencePosition();
    }

    /// <summary>选中差异列表中的指定下标，并同步两种模式的选中行。</summary>
    private void SelectDifferenceAt(int index)
    {
        if (index < 0 || index >= this.activeDifferences.Count)
        {
            return;
        }
        object item = this.activeDifferences[index];
        this.suppressSelectionSync = true;
        if (item is DirectoryCompareRow directoryRow)
        {
            this.SelectedDirectoryRow = directoryRow;
        }
        else if (item is FileDiffRow fileRow)
        {
            this.SelectedFileRow = fileRow;
        }
        this.suppressSelectionSync = false;
        this.UpdateDifferencePosition();
    }

    /// <summary>用户手动选中差异行时，把差异下标同步到该行位置。</summary>
    private void SyncDifferenceIndex(object item)
    {
        for (int index = 0; index < this.activeDifferences.Count; index++)
        {
            if (object.ReferenceEquals(this.activeDifferences[index], item))
            {
                this.currentDifferenceIndex = index;
                this.UpdateDifferencePosition();
                return;
            }
        }
    }

    /// <summary>刷新差异位置文字。</summary>
    private void UpdateDifferencePosition()
    {
        if (this.activeDifferences.Count == 0)
        {
            this.DifferencePositionText = "没有差异";
            return;
        }
        int displayIndex = this.currentDifferenceIndex < 0 ? 0 : this.currentDifferenceIndex + 1;
        this.DifferencePositionText = "差异 " + displayIndex + " / " + this.activeDifferences.Count;
    }

    /// <summary>根据两侧路径更新标签标题与窗口标题。</summary>
    private void UpdateTitle()
    {
        string leftName = this.DescribePathName(this.LeftPath);
        string rightName = this.DescribePathName(this.RightPath);
        this.Title = this.ModeText + "：" + leftName + " ↔ " + rightName;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
    }

    /// <summary>取路径末尾名称用于标题。</summary>
    private string DescribePathName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "（无）";
        }
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name))
        {
            return trimmed;
        }
        return name;
    }

    /// <summary>从内核条目构建目录比对行。</summary>
    private DirectoryCompareRow BuildDirectoryRow(DirectoryCompareEntry entry)
    {
        DirectoryCompareRow row = new DirectoryCompareRow();
        row.Name = entry.RelativePath;
        row.IsDirectory = entry.IsDirectory;
        row.Status = entry.Status;
        row.StatusText = DirectoryCompareRow.DescribeStatus(entry.Status);
        row.StatusColor = DirectoryCompareRow.DescribeStatusColor(entry.Status);
        row.LeftPath = entry.LeftPath;
        row.RightPath = entry.RightPath;
        row.LeftExists = entry.LeftExists;
        row.RightExists = entry.RightExists;
        row.LeftInfoText = DirectoryCompareRow.FormatSideInfo(entry.LeftExists, entry.IsDirectory,
            entry.LeftLengthBytes, entry.LeftModifiedTimeUtc);
        row.RightInfoText = DirectoryCompareRow.FormatSideInfo(entry.RightExists, entry.IsDirectory,
            entry.RightLengthBytes, entry.RightModifiedTimeUtc);
        if (entry.IsDirectory)
        {
            row.IconCategory = FileIconCatalog.Folder;
        }
        else
        {
            row.IconCategory = FileIconCatalog.GetCategoryForName(entry.Name);
        }
        row.IconColor = FileIconCatalog.GetAccent(row.IconCategory);
        return row;
    }

    /// <summary>读取文件比对一侧的文本，并给出编码、行数与错误说明。</summary>
    private SideTextResult ReadTextSide(string path)
    {
        SideTextResult side = new SideTextResult();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            side.Text = string.Empty;
            side.EncodingName = "（文件不存在）";
            return side;
        }
        try
        {
            TextFileContent content = this.textFileService.Read(path);
            if (content.IsBinary)
            {
                side.ErrorText = "文件为二进制格式（" + path + "）";
                return side;
            }
            if (content.IsTooLarge)
            {
                side.ErrorText = "文件超过 5MB 文本上限（" + path + "）";
                return side;
            }
            side.Text = content.Text;
            side.EncodingName = content.EncodingName;
            return side;
        }
        catch (Exception error)
        {
            side.ErrorText = "读取失败：" + error.Message;
            return side;
        }
    }

    /// <summary>判断两侧路径应按目录还是文件比对。</summary>
    private bool DetectDirectoryMode(string left, string right)
    {
        bool leftDirectory = left.Length > 0 && Directory.Exists(left);
        bool rightDirectory = right.Length > 0 && Directory.Exists(right);
        if (leftDirectory || rightDirectory)
        {
            return true;
        }
        bool leftFile = left.Length > 0 && File.Exists(left);
        bool rightFile = right.Length > 0 && File.Exists(right);
        if (leftFile || rightFile)
        {
            return false;
        }
        // 两个路径都不存在时默认按目录比对，由结果区提示全部仅单侧
        return true;
    }

    /// <summary>取一侧路径的父目录；路径不存在或已到根时保持原值。</summary>
    private string GetParentPath(string resolvedPath, string inputPath)
    {
        if (string.IsNullOrEmpty(resolvedPath))
        {
            return string.Empty;
        }
        if (!Directory.Exists(resolvedPath) && !File.Exists(resolvedPath))
        {
            return inputPath;
        }
        DirectoryInfo? parent = Directory.GetParent(resolvedPath);
        if (parent == null)
        {
            return resolvedPath;
        }
        return parent.FullName;
    }

    /// <summary>一侧文本读取结果：文本、编码名与错误说明。</summary>
    private sealed class SideTextResult
    {
        /// <summary>读取到的文本。</summary>
        public string Text = string.Empty;

        /// <summary>探测到的编码名称。</summary>
        public string EncodingName = "UTF-8";

        /// <summary>读取失败原因；为空表示成功。</summary>
        public string ErrorText = string.Empty;

        /// <summary>生成一侧的元信息文字（编码 · 行数）。</summary>
        public string Describe(bool missingPath)
        {
            if (this.ErrorText.Length > 0)
            {
                return this.ErrorText;
            }
            if (missingPath)
            {
                return "（文件不存在）";
            }
            int lineCount = this.Text.Length == 0 ? 0 : this.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Length;
            return this.EncodingName + " · " + lineCount + " 行";
        }
    }
}
