using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>日志查看器的一行：保留原始文本与解析出的级别、分类、事件名，供筛选与着色。</summary>
public sealed class LogViewerRow
{
    /// <summary>完整日志行。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>解析出的级别。</summary>
    public LogLevel Level { get; set; } = LogLevel.Info;

    /// <summary>解析出的分类标签。</summary>
    public string Category { get; set; } = "未分类";

    /// <summary>解析出的事件名。</summary>
    public string EventName { get; set; } = "未知";

    /// <summary>级别对应的文字颜色：错误为红色、警告为橙色，其余沿用主题默认色。</summary>
    public IBrush? Foreground { get; set; }
}

/// <summary>
/// 可批量追加的可观察集合：日志按块加载，一次通知界面刷新一批行，避免逐行通知造成卡顿。
/// </summary>
public sealed class LogViewerRowCollection : ObservableCollection<LogViewerRow>
{
    /// <summary>批量追加若干行，只触发一次集合变更通知。</summary>
    public void AddRange(List<LogViewerRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }
        int startIndex = this.Count;
        foreach (LogViewerRow row in rows)
        {
            this.Items.Add(row);
        }
        this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, rows, startIndex));
        this.OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        this.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}

/// <summary>
/// 应用内日志查看器：按级别、分类与关键字筛选，跟随最新，复制选中行并导出当前视图。
/// 文件读取放在后台任务中按块加载，界面线程只负责追加已解析的行。
/// </summary>
public partial class LogViewerWindow : Window
{
    private const int ChunkBytes = 256 * 1024;
    private const int ChunkLines = 5000;
    private const int MaxLoadedRows = 200000;
    private const long InitialTailBytes = 4L * 1024 * 1024;

    private static readonly LogLevel[] LevelThresholds = new LogLevel[]
    {
        LogLevel.Error, LogLevel.Warn, LogLevel.Info, LogLevel.Debug, LogLevel.Trace,
    };

    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF6B6B"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#FFB454"));

    private readonly List<LogViewerRow> loadedRows = new List<LogViewerRow>();
    private readonly LogViewerRowCollection visibleRows = new LogViewerRowCollection();
    private readonly ObservableCollection<string> categoryOptions = new ObservableCollection<string>();
    private LogFileTailReader? reader;
    private CancellationTokenSource? loopCancellation;
    private DispatcherTimer? keywordTimer;
    private bool filterReady;

    public LogViewerWindow()
    {
        this.InitializeComponent();
        // 日志查看器作为主窗口辅助窗口，不单独占用任务栏按钮
        WindowHostStyler.MakeAuxiliary(this);
        this.LogList.ItemsSource = this.visibleRows;

        this.categoryOptions.Add("全部");
        this.categoryOptions.Add(LogCategories.Screenshot);
        this.categoryOptions.Add(LogCategories.File);
        this.categoryOptions.Add(LogCategories.Remote);
        this.categoryOptions.Add(LogCategories.Web);
        this.categoryOptions.Add(LogCategories.Ui);
        this.categoryOptions.Add(LogCategories.Performance);
        this.categoryOptions.Add(LogCategories.System);
        this.categoryOptions.Add(LogCategories.Startup);
        this.categoryOptions.Add(LogCategories.Config);
        this.categoryOptions.Add(LogCategories.Diagnostics);
        this.categoryOptions.Add("未分类");
        this.CategoryBox.ItemsSource = this.categoryOptions;
        this.CategoryBox.SelectedIndex = 0;
        this.LevelBox.SelectedIndex = 2;
        this.filterReady = true;
        this.Opened += this.OnWindowOpened;
        this.Closed += this.OnWindowClosed;
        this.UpdateStatusText();
    }

    /// <summary>窗口打开后开始加载，避免构造函数内启动后台任务时控件尚未完成布局。</summary>
    private void OnWindowOpened(object? sender, EventArgs e)
    {
        this.StartLoading();
    }

    /// <summary>窗口关闭时停止后台读取，保证句柄与任务及时释放。</summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        this.StopLoading();
        if (this.keywordTimer != null)
        {
            this.keywordTimer.Stop();
            this.keywordTimer = null;
        }
        UiLogger.LogInfo(LogCategories.Diagnostics, "关闭日志查看器",
            "已加载行数=" + this.loadedRows.Count + " 当前显示行数=" + this.visibleRows.Count);
    }

    /// <summary>重新加载当前日志文件：清空旧行并从最近 4 兆处开始按块读取。</summary>
    private void StartLoading()
    {
        this.StopLoading();
        this.loadedRows.Clear();
        this.visibleRows.Clear();
        this.reader = new LogFileTailReader(UiLogger.FilePath);
        this.reader.SeekToTail(InitialTailBytes);
        CancellationTokenSource cancellation = new CancellationTokenSource();
        this.loopCancellation = cancellation;
        _ = this.LoadLoopAsync(this.reader, cancellation.Token);
        UiLogger.LogInfo(LogCategories.Diagnostics, "打开日志查看器",
            "日志文件=" + this.reader.FilePath
            + " 初始读取字节上限=" + InitialTailBytes
            + " 最低级别=" + UiLogger.DescribeLevel(UiLogger.MinimumLevel));
        this.UpdateStatusText();
    }

    /// <summary>停止后台读取任务。</summary>
    private void StopLoading()
    {
        if (this.loopCancellation == null)
        {
            return;
        }
        try
        {
            this.loopCancellation.Cancel();
        }
        catch
        {
            // 取消已结束的任务不会抛出到界面
        }
        this.loopCancellation = null;
    }

    /// <summary>
    /// 后台加载循环：每次最多读若干块，读完等待一秒再增量读取；续体回到界面线程追加行。
    /// </summary>
    private async Task LoadLoopAsync(LogFileTailReader targetReader, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                int chunkCount = 0;
                while (chunkCount < 6 && !token.IsCancellationRequested)
                {
                    LogChunk chunk = await Task.Run(() => targetReader.ReadChunk(ChunkBytes, ChunkLines), token);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    if (chunk.ResetDueToTruncation)
                    {
                        this.loadedRows.Clear();
                        this.visibleRows.Clear();
                    }
                    if (chunk.Lines.Count > 0)
                    {
                        this.IngestLines(chunk.Lines);
                    }
                    if (chunk.ReachedEnd)
                    {
                        break;
                    }
                    chunkCount = chunkCount + 1;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 读取失败时等待下一轮重试，查看器保持可用
            }
            try
            {
                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>把一批原始日志行解析后加入已加载列表，并按当前筛选条件追加到显示列表。</summary>
    private void IngestLines(List<string> lines)
    {
        List<LogViewerRow> appended = new List<LogViewerRow>();
        foreach (string line in lines)
        {
            LogViewerRow row = ParseLine(line);
            this.loadedRows.Add(row);
            this.RegisterCategory(row.Category);
            if (this.MatchesFilter(row))
            {
                appended.Add(row);
            }
        }

        bool trimmed = false;
        if (this.loadedRows.Count > MaxLoadedRows)
        {
            this.loadedRows.RemoveRange(0, this.loadedRows.Count - MaxLoadedRows);
            trimmed = true;
        }
        if (trimmed)
        {
            this.RebuildVisibleRows();
        }
        else if (appended.Count > 0)
        {
            this.visibleRows.AddRange(appended);
            this.UpdateStatusText();
            this.ScrollToEndIfFollowing();
        }
        else
        {
            this.UpdateStatusText();
        }
    }

    /// <summary>把新出现的分类补进筛选下拉框，保证自定义分类也能筛选。</summary>
    private void RegisterCategory(string category)
    {
        if (category.Length == 0 || this.categoryOptions.Contains(category))
        {
            return;
        }
        this.categoryOptions.Add(category);
    }

    /// <summary>判断一行是否满足当前最低级别、分类与关键字条件。</summary>
    private bool MatchesFilter(LogViewerRow row)
    {
        LogLevel threshold = this.GetSelectedLevel();
        if (row.Level < threshold)
        {
            return false;
        }
        string selectedCategory = this.CategoryBox.SelectedItem as string ?? "全部";
        if (!string.Equals(selectedCategory, "全部", StringComparison.Ordinal)
            && !string.Equals(selectedCategory, row.Category, StringComparison.Ordinal))
        {
            return false;
        }
        string keyword = this.KeywordBox.Text == null ? string.Empty : this.KeywordBox.Text.Trim();
        if (keyword.Length > 0 && !row.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    /// <summary>取下拉框当前选中的最低级别；控件未就绪时按信息级。</summary>
    private LogLevel GetSelectedLevel()
    {
        int index = this.LevelBox.SelectedIndex;
        if (index < 0 || index >= LevelThresholds.Length)
        {
            return LogLevel.Info;
        }
        return LevelThresholds[index];
    }

    /// <summary>按当前筛选条件重建显示列表。</summary>
    private void RebuildVisibleRows()
    {
        List<LogViewerRow> matches = new List<LogViewerRow>();
        foreach (LogViewerRow row in this.loadedRows)
        {
            if (this.MatchesFilter(row))
            {
                matches.Add(row);
            }
        }
        this.visibleRows.Clear();
        this.visibleRows.AddRange(matches);
        this.UpdateStatusText();
        this.ScrollToEndIfFollowing();
    }

    /// <summary>把一行原始文本解析为查看器行：解析失败时保留原文并按信息级、未分类展示。</summary>
    private static LogViewerRow ParseLine(string line)
    {
        LogViewerRow row = new LogViewerRow();
        row.Text = line;
        row.Level = LogLevel.Info;
        row.Category = "未分类";
        row.EventName = "未知";

        int levelStart = line.IndexOf('[');
        if (levelStart < 0)
        {
            return row;
        }
        int levelEnd = line.IndexOf(']', levelStart + 1);
        if (levelEnd < 0)
        {
            return row;
        }
        string levelText = line.Substring(levelStart + 1, levelEnd - levelStart - 1);
        row.Level = ParseLevelText(levelText);

        int categoryStart = line.IndexOf("[分类=", levelEnd + 1, StringComparison.Ordinal);
        if (categoryStart >= 0)
        {
            int categoryEnd = line.IndexOf(']', categoryStart + 1);
            if (categoryEnd > categoryStart)
            {
                row.Category = line.Substring(categoryStart + "[分类=".Length,
                    categoryEnd - categoryStart - "[分类=".Length);
                int eventStart = line.IndexOf("[事件=", categoryEnd + 1, StringComparison.Ordinal);
                if (eventStart >= 0)
                {
                    int eventEnd = line.IndexOf(']', eventStart + 1);
                    if (eventEnd > eventStart)
                    {
                        row.EventName = line.Substring(eventStart + "[事件=".Length,
                            eventEnd - eventStart - "[事件=".Length);
                    }
                }
            }
        }

        if (row.Level == LogLevel.Error)
        {
            row.Foreground = ErrorBrush;
        }
        else if (row.Level == LogLevel.Warn)
        {
            row.Foreground = WarnBrush;
        }
        return row;
    }

    /// <summary>把日志里的中文或英文级别名解析为枚举；无法识别时按信息级。</summary>
    private static LogLevel ParseLevelText(string text)
    {
        if (text == "错误" || string.Equals(text, "Error", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Error;
        }
        if (text == "警告" || string.Equals(text, "Warn", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Warn;
        }
        if (text == "调试" || string.Equals(text, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Debug;
        }
        if (text == "跟踪" || string.Equals(text, "Trace", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Trace;
        }
        return LogLevel.Info;
    }

    /// <summary>最低级别或分类改变时立即重建显示列表。</summary>
    private void OnFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!this.filterReady)
        {
            return;
        }
        this.RebuildVisibleRows();
        UiLogger.LogDebug(LogCategories.Diagnostics, "日志筛选",
            "最低级别=" + UiLogger.DescribeLevel(this.GetSelectedLevel())
            + " 分类=" + (this.CategoryBox.SelectedItem as string ?? "全部")
            + " 关键字=" + (this.KeywordBox.Text ?? string.Empty)
            + " 当前显示行数=" + this.visibleRows.Count);
    }

    /// <summary>关键字输入：延迟 300 毫秒再筛选，避免逐字符扫描大量行造成界面卡顿。</summary>
    private void OnKeywordChanged(object? sender, TextChangedEventArgs e)
    {
        if (!this.filterReady)
        {
            return;
        }
        if (this.keywordTimer == null)
        {
            this.keywordTimer = new DispatcherTimer();
            this.keywordTimer.Interval = TimeSpan.FromMilliseconds(300);
            this.keywordTimer.Tick += this.OnKeywordTimerTick;
        }
        this.keywordTimer.Stop();
        this.keywordTimer.Start();
    }

    private void OnKeywordTimerTick(object? sender, EventArgs e)
    {
        if (this.keywordTimer != null)
        {
            this.keywordTimer.Stop();
        }
        this.RebuildVisibleRows();
        UiLogger.LogDebug(LogCategories.Diagnostics, "关键字搜索",
            "关键字=" + (this.KeywordBox.Text ?? string.Empty)
            + " 当前显示行数=" + this.visibleRows.Count);
    }

    /// <summary>刷新：停止当前读取并从文件尾部重新加载。</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.LogInfo(LogCategories.Diagnostics, "刷新日志查看器", "日志文件=" + UiLogger.FilePath);
        this.StartLoading();
    }

    /// <summary>复制选中行到系统剪贴板；未选中时记录并提示。</summary>
    private async void OnCopySelectedClick(object? sender, RoutedEventArgs e)
    {
        List<string> lines = new List<string>();
        IList? selectedItems = this.LogList.SelectedItems;
        if (selectedItems != null)
        {
            foreach (object item in selectedItems)
            {
                LogViewerRow? row = item as LogViewerRow;
                if (row != null)
                {
                    lines.Add(row.Text);
                }
            }
        }
        if (lines.Count == 0)
        {
            this.StatusText.Text = "未选中任何日志行，请先在列表中选择要复制的内容。";
            UiLogger.LogWarn(LogCategories.Diagnostics, "复制选中日志", "结果=未选中任何行，未写入剪贴板");
            return;
        }
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || topLevel.Clipboard == null)
        {
            this.StatusText.Text = "系统剪贴板不可用，复制失败。";
            UiLogger.LogError(LogCategories.Diagnostics, "复制选中日志", "结果=系统剪贴板不可用");
            return;
        }
        try
        {
            await topLevel.Clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
            this.StatusText.Text = "已复制 " + lines.Count + " 行到系统剪贴板。";
            UiLogger.LogInfo(LogCategories.Diagnostics, "复制选中日志", "已复制行数=" + lines.Count);
        }
        catch (Exception error)
        {
            this.StatusText.Text = "复制失败：" + error.Message;
            UiLogger.LogError(LogCategories.Diagnostics, "复制选中日志", "结果=写入剪贴板失败", error);
        }
    }

    /// <summary>导出当前筛选后的视图为 UTF-8 文本文件，只包含界面上看得到的行。</summary>
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            this.StatusText.Text = "当前窗口未就绪，无法打开导出对话框。";
            UiLogger.LogError(LogCategories.Diagnostics, "导出日志视图", "结果=顶层窗口未就绪");
            return;
        }
        try
        {
            FilePickerSaveOptions options = new FilePickerSaveOptions();
            options.Title = "导出当前日志视图";
            options.SuggestedFileName = "TabTaskExplorer-日志-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
            options.DefaultExtension = "txt";
            List<FilePickerFileType> fileTypes = new List<FilePickerFileType>();
            FilePickerFileType textType = new FilePickerFileType("文本文件");
            textType.Patterns = new List<string> { "*.txt" };
            fileTypes.Add(textType);
            options.FileTypeChoices = fileTypes;
            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
            if (file == null)
            {
                this.StatusText.Text = "已取消导出。";
                UiLogger.LogInfo(LogCategories.Diagnostics, "导出日志视图", "结果=用户取消选择");
                return;
            }
            using (Stream stream = await file.OpenWriteAsync())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (LogViewerRow row in this.visibleRows)
                {
                    writer.WriteLine(row.Text);
                }
            }
            string pathText = file.TryGetLocalPath() ?? file.Name;
            this.StatusText.Text = "已导出当前视图 " + this.visibleRows.Count + " 行：" + pathText;
            UiLogger.LogInfo(LogCategories.Diagnostics, "导出日志视图",
                "导出文件=" + pathText + " 导出行数=" + this.visibleRows.Count);
        }
        catch (Exception error)
        {
            this.StatusText.Text = "导出失败：" + error.Message;
            UiLogger.LogError(LogCategories.Diagnostics, "导出日志视图", "结果=写入文件失败", error);
        }
    }

    /// <summary>用系统文件管理器打开日志目录。</summary>
    private void OnOpenDirectoryClick(object? sender, RoutedEventArgs e)
    {
        string directory = UiLogger.LogDirectory;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory)
            {
                UseShellExecute = true,
            });
            UiLogger.LogInfo(LogCategories.Diagnostics, "打开日志目录", "目录=" + directory + " 结果=成功");
        }
        catch (Exception error)
        {
            this.StatusText.Text = "打开日志目录失败：" + error.Message;
            UiLogger.LogError(LogCategories.Diagnostics, "打开日志目录", "目录=" + directory, error);
        }
    }

    /// <summary>跟随最新开启时滚动到最后一行。</summary>
    private void ScrollToEndIfFollowing()
    {
        if (this.FollowToggle.IsChecked != true || this.visibleRows.Count == 0)
        {
            return;
        }
        try
        {
            this.LogList.ScrollIntoView(this.visibleRows[this.visibleRows.Count - 1]);
        }
        catch
        {
            // 列表尚未完成布局时滚动失败，下一批追加会再次尝试
        }
    }

    /// <summary>刷新底部状态：日志文件、已加载行数、当前显示行数与全局最低级别。</summary>
    private void UpdateStatusText()
    {
        string path = this.reader == null ? UiLogger.FilePath : this.reader.FilePath;
        this.StatusText.Text = "日志文件=" + path
            + " 已加载行数=" + this.loadedRows.Count
            + " 当前显示行数=" + this.visibleRows.Count
            + " 全局最低级别=" + UiLogger.DescribeLevel(UiLogger.MinimumLevel);
    }
}
