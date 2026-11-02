using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>系统启动项列表中的一行：展示字段由内核模型换算而来。</summary>
public class StartupRow
{
    public StartupRow(StartupItem item)
    {
        this.Item = item;
    }

    /// <summary>对应的系统启动项模型。</summary>
    public StartupItem Item { get; private set; }

    /// <summary>启动项编号（网页端启停时回传）。</summary>
    public string Id
    {
        get { return this.Item.Id; }
    }

    /// <summary>显示名称。</summary>
    public string Name
    {
        get { return this.Item.Name; }
    }

    /// <summary>列表展示名称：本程序自身的自启项额外标注，避免用户误操作后不清楚影响范围。</summary>
    public string DisplayName
    {
        get { return this.Item.IsSelfAutoStart ? this.Item.Name + "（本程序）" : this.Item.Name; }
    }

    /// <summary>类型文字。</summary>
    public string KindText
    {
        get { return StartupItemType.Describe(this.Item.ItemType); }
    }

    /// <summary>来源文字。</summary>
    public string SourceText
    {
        get { return DescribeSource(this.Item.Source); }
    }

    /// <summary>作用范围文字。</summary>
    public string ScopeText
    {
        get { return StartupItemScope.Describe(this.Item.Scope); }
    }

    /// <summary>启动目标显示文字：目标与参数合并。</summary>
    public string CommandText
    {
        get
        {
            if (string.IsNullOrEmpty(this.Item.Arguments))
            {
                return this.Item.Command;
            }
            return this.Item.Command + " " + this.Item.Arguments;
        }
    }

    /// <summary>当前状态文字。</summary>
    public string StateText
    {
        get { return this.Item.Enabled ? "已启用" : "已禁用" ; }
    }

    /// <summary>是否处于启用状态。</summary>
    public bool IsEnabled
    {
        get { return this.Item.Enabled; }
    }

    /// <summary>是否允许修改启停状态。</summary>
    public bool CanToggle
    {
        get { return this.Item.CanToggle; }
    }

    /// <summary>不可修改时的原因提示。</summary>
    public string ToggleHint
    {
        get { return this.Item.ToggleHint; }
    }

    /// <summary>行内启停按钮文字。</summary>
    public string ToggleActionText
    {
        get { return this.Item.Enabled ? "禁用" : "启用"; }
    }

    /// <summary>把来源枚举转成界面文字。</summary>
    private static string DescribeSource(StartupItemSource source)
    {
        if (source == StartupItemSource.RegistryRun)
        {
            return "注册表 Run";
        }
        if (source == StartupItemSource.StartupFolder)
        {
            return "启动文件夹";
        }
        if (source == StartupItemSource.Service)
        {
            return "服务";
        }
        if (source == StartupItemSource.AutoStartFile)
        {
            return "自启动文件";
        }
        if (source == StartupItemSource.LaunchAgent)
        {
            return "launchd";
        }
        if (source == StartupItemSource.ScheduledTask)
        {
            return "计划任务";
        }
        return "未知";
    }
}

/// <summary>自定义启动项列表中的一行：展示编辑字段与本次开机执行状态。</summary>
public class CustomStartupRow
{
    public CustomStartupRow(CustomStartupItem item, string bootStateText)
    {
        this.Item = item;
        this.BootStateText = bootStateText;
    }

    /// <summary>对应的自定义启动项模型。</summary>
    public CustomStartupItem Item { get; private set; }

    /// <summary>条目编号。</summary>
    public string Id
    {
        get { return this.Item.Id; }
    }

    /// <summary>显示名称。</summary>
    public string Name
    {
        get { return this.Item.Name; }
    }

    /// <summary>类型文字。</summary>
    public string KindText
    {
        get { return StartupItemType.Describe(this.Item.ItemType); }
    }

    /// <summary>启动目标显示文字。</summary>
    public string TargetText
    {
        get
        {
            if (string.IsNullOrEmpty(this.Item.Arguments))
            {
                return this.Item.Target;
            }
            return this.Item.Target + " " + this.Item.Arguments;
        }
    }

    /// <summary>延迟文字。</summary>
    public string DelayText
    {
        get
        {
            if (this.Item.DelaySeconds <= 0)
            {
                return "立即";
            }
            return "延迟 " + this.Item.DelaySeconds + " 秒";
        }
    }

    /// <summary>启用状态文字。</summary>
    public string StateText
    {
        get { return this.Item.Enabled ? "已启用" : "已禁用"; }
    }

    /// <summary>是否启用。</summary>
    public bool IsEnabled
    {
        get { return this.Item.Enabled; }
    }

    /// <summary>本次开机的执行状态文字。</summary>
    public string BootStateText { get; private set; }

    /// <summary>最后一次拉起结果。</summary>
    public string ResultText
    {
        get
        {
            if (string.IsNullOrEmpty(this.Item.LastResult))
            {
                return "尚未拉起";
            }
            return this.Item.LastResult;
        }
    }
}

/// <summary>
/// 启动项管理标签：系统启动项（注册表 Run、启动文件夹、自动启动服务）的枚举与启停，
/// 以及自定义启动项（程序 / 快捷方式 / 脚本 / 网页链接）的增删改、立即运行与本次开机执行状态展示。
/// </summary>
public class StartupManagerViewModel : INotifyPropertyChanged, IColumnWidthViewModel, ITabView
{
    private readonly IStartupItemProvider provider;
    private readonly AppSettings settings;
    private readonly AppSettingsStore settingsStore;
    private readonly CustomStartupService customService;
    private readonly List<StartupItem> enumeratedItems = new List<StartupItem>();
    private bool refreshInProgress;
    private bool closed;
    private string summaryText = "正在读取启动项……";
    private string statusText = "就绪";
    private string environmentText = string.Empty;
    private string columnWidths = string.Empty;
    private StartupRow? selectedSystemItem;
    private CustomStartupRow? selectedCustomItem;
    private string editingId = string.Empty;
    private string editName = string.Empty;
    private int editKindIndex = 0;
    private string editTarget = string.Empty;
    private string editArguments = string.Empty;
    private int editDelaySeconds = 0;
    private bool editEnabled = true;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    public StartupManagerViewModel(IStartupItemProvider provider, AppSettings settings,
        AppSettingsStore settingsStore, CustomStartupService customService)
    {
        this.provider = provider;
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.customService = customService;
        this.environmentText = this.provider.PlatformText + " · 本次开机标识=" + this.customService.CurrentBootId;
        this.RefreshCustomItems();
        this.Refresh();
        UiLogger.Log("启动项", "启动项管理标签创建：平台=" + this.provider.PlatformText
            + " 支持=" + this.provider.IsSupported + " 自定义启动项条数=" + this.customService.List().Count
            + " 本次开机标识=" + this.customService.CurrentBootId);
    }

    /// <inheritdoc />
    public string Kind
    {
        get { return TabKinds.Startup; }
    }

    /// <inheritdoc />
    public string Title
    {
        get { return "启动项管理"; }
    }

    /// <inheritdoc />
    public void FillSnapshot(DebugTabState tabState)
    {
        TabSnapshotFiller.FillStartup(this, tabState);
    }

    /// <summary>系统启动项列表。</summary>
    public ObservableCollection<StartupRow> SystemItems { get; } = new ObservableCollection<StartupRow>();

    /// <summary>自定义启动项列表。</summary>
    public ObservableCollection<CustomStartupRow> CustomItems { get; } = new ObservableCollection<CustomStartupRow>();

    /// <summary>当前选中的系统启动项。</summary>
    public StartupRow? SelectedSystemItem
    {
        get { return this.selectedSystemItem; }
        set
        {
            this.selectedSystemItem = value;
            this.RaisePropertyChanged(nameof(this.SelectedSystemItem));
        }
    }

    /// <summary>当前选中的自定义启动项；选中后编辑表单自动回填。</summary>
    public CustomStartupRow? SelectedCustomItem
    {
        get { return this.selectedCustomItem; }
        set
        {
            this.selectedCustomItem = value;
            this.RaisePropertyChanged(nameof(this.SelectedCustomItem));
            if (value != null)
            {
                this.LoadEditFields(value.Item);
            }
        }
    }

    /// <summary>数量摘要。</summary>
    public string SummaryText
    {
        get { return this.summaryText; }
        private set
        {
            this.summaryText = value;
            this.RaisePropertyChanged(nameof(this.SummaryText));
        }
    }

    /// <summary>最近一次操作结果。</summary>
    public string StatusText
    {
        get { return this.statusText; }
        private set
        {
            this.statusText = value;
            this.RaisePropertyChanged(nameof(this.StatusText));
        }
    }

    /// <summary>平台能力、开机标识与自动拉起说明。</summary>
    public string EnvironmentText
    {
        get { return this.environmentText; }
        private set
        {
            this.environmentText = value;
            this.RaisePropertyChanged(nameof(this.EnvironmentText));
        }
    }

    /// <summary>系统启动项表头列宽。</summary>
    public string ColumnWidths
    {
        get { return this.columnWidths; }
        set
        {
            this.columnWidths = value ?? string.Empty;
            this.RaisePropertyChanged(nameof(this.ColumnWidths));
        }
    }

    /// <summary>正在编辑的自定义启动项编号；空文本表示新建。</summary>
    public string EditingId
    {
        get { return this.editingId; }
        private set
        {
            this.editingId = value;
            this.RaisePropertyChanged(nameof(this.EditingId));
        }
    }

    /// <summary>编辑表单：名称。</summary>
    public string EditName
    {
        get { return this.editName; }
        set
        {
            this.editName = value ?? string.Empty;
            this.RaisePropertyChanged(nameof(this.EditName));
        }
    }

    /// <summary>编辑表单：类型下拉框下标（0 程序、1 快捷方式、2 脚本、3 网页链接）。</summary>
    public int EditKindIndex
    {
        get { return this.editKindIndex; }
        set
        {
            this.editKindIndex = value;
            this.RaisePropertyChanged(nameof(this.EditKindIndex));
        }
    }

    /// <summary>编辑表单：启动目标。</summary>
    public string EditTarget
    {
        get { return this.editTarget; }
        set
        {
            this.editTarget = value ?? string.Empty;
            this.RaisePropertyChanged(nameof(this.EditTarget));
        }
    }

    /// <summary>编辑表单：启动参数。</summary>
    public string EditArguments
    {
        get { return this.editArguments; }
        set
        {
            this.editArguments = value ?? string.Empty;
            this.RaisePropertyChanged(nameof(this.EditArguments));
        }
    }

    /// <summary>编辑表单：延迟秒数。</summary>
    public int EditDelaySeconds
    {
        get { return this.editDelaySeconds; }
        set
        {
            this.editDelaySeconds = value < 0 ? 0 : value;
            this.RaisePropertyChanged(nameof(this.EditDelaySeconds));
        }
    }

    /// <summary>编辑表单：是否启用。</summary>
    public bool EditEnabled
    {
        get { return this.editEnabled; }
        set
        {
            this.editEnabled = value;
            this.RaisePropertyChanged(nameof(this.EditEnabled));
        }
    }

    /// <summary>当前平台的启动项服务是否可用。</summary>
    public bool IsSupported
    {
        get { return this.provider.IsSupported; }
    }

    /// <summary>标签被选中时刷新系统启动项与自定义启动项状态。</summary>
    public void OnActivated()
    {
        this.Refresh();
    }

    /// <summary>
    /// 刷新系统启动项：在后台线程枚举，完成后回到界面线程更新列表。
    /// 启动项枚举可能调用 systemctl 等外部命令，不能阻塞界面。
    /// </summary>
    public void Refresh()
    {
        if (this.closed || this.refreshInProgress)
        {
            return;
        }
        this.refreshInProgress = true;
        this.StatusText = "正在刷新启动项……";
        UiLogger.Log("启动项", "开始刷新系统启动项：平台=" + this.provider.PlatformText);
        Task.Run(new Action(this.EnumerateInBackground));
    }

    /// <summary>后台枚举系统启动项，完成后调度回界面线程。</summary>
    private void EnumerateInBackground()
    {
        try
        {
            IReadOnlyList<StartupItem> items = this.provider.Enumerate();
            lock (this.enumeratedItems)
            {
                this.enumeratedItems.Clear();
                foreach (StartupItem item in items)
                {
                    this.enumeratedItems.Add(item);
                }
            }
            Dispatcher.UIThread.Post(new Action(this.ApplyEnumeratedItems));
        }
        catch (Exception error)
        {
            UiLogger.LogError("启动项", "刷新系统启动项失败：平台=" + this.provider.PlatformText, error);
            Dispatcher.UIThread.Post(new Action(delegate { this.ApplyEnumerateFailure(error.Message); }));
        }
    }

    /// <summary>把后台枚举结果填入界面列表。</summary>
    private void ApplyEnumeratedItems()
    {
        this.refreshInProgress = false;
        if (this.closed)
        {
            return;
        }
        string previousSelectedId = this.selectedSystemItem == null ? string.Empty : this.selectedSystemItem.Id;
        List<StartupItem> items;
        lock (this.enumeratedItems)
        {
            items = new List<StartupItem>(this.enumeratedItems);
        }
        this.SystemItems.Clear();
        StartupRow? restored = null;
        foreach (StartupItem item in items)
        {
            StartupRow row = new StartupRow(item);
            this.SystemItems.Add(row);
            if (previousSelectedId.Length > 0 && string.Equals(row.Id, previousSelectedId, StringComparison.Ordinal))
            {
                restored = row;
            }
        }
        if (restored != null)
        {
            this.SelectedSystemItem = restored;
        }
        else
        {
            // 刷新后原选中条目已不存在（已被系统移除或改名）：清空选择，避免对旧条目误操作
            this.SelectedSystemItem = null;
        }
        int enabledCount = 0;
        foreach (StartupRow row in this.SystemItems)
        {
            if (row.IsEnabled)
            {
                enabledCount++;
            }
        }
        this.UpdateSummary();
        if (string.Equals(this.statusText, "正在刷新启动项……", StringComparison.Ordinal))
        {
            this.StatusText = "系统启动项已刷新：共 " + this.SystemItems.Count + " 项，启用 " + enabledCount + " 项";
        }
        UiLogger.Log("启动项", "系统启动项刷新完成：平台=" + this.provider.PlatformText
            + " 总条数=" + this.SystemItems.Count + " 启用条数=" + enabledCount);
    }

    /// <summary>枚举失败时回到界面线程展示原因。</summary>
    private void ApplyEnumerateFailure(string message)
    {
        this.refreshInProgress = false;
        if (this.closed)
        {
            return;
        }
        this.SystemItems.Clear();
        this.UpdateSummary();
        this.StatusText = "读取系统启动项失败：" + message;
        UiLogger.Log("启动项", "读取系统启动项失败：平台=" + this.provider.PlatformText + " 原因=" + message);
    }

    /// <summary>自定义启动项列表是否为空（为空时显示引导提示）。</summary>
    public bool HasNoCustomItems
    {
        get { return this.CustomItems.Count == 0; }
    }

    /// <summary>重建自定义启动项列表。</summary>
    public void RefreshCustomItems()
    {
        string previousSelectedId = this.selectedCustomItem == null ? string.Empty : this.selectedCustomItem.Id;
        List<CustomStartupItem> items = this.customService.List();
        this.CustomItems.Clear();
        CustomStartupRow? restored = null;
        foreach (CustomStartupItem item in items)
        {
            CustomStartupRow row = new CustomStartupRow(item, this.customService.DescribeBootState(item));
            this.CustomItems.Add(row);
            if (previousSelectedId.Length > 0 && string.Equals(row.Id, previousSelectedId, StringComparison.Ordinal))
            {
                restored = row;
            }
        }
        if (restored != null)
        {
            this.selectedCustomItem = restored;
            this.RaisePropertyChanged(nameof(this.SelectedCustomItem));
        }
        else if (!string.IsNullOrEmpty(previousSelectedId))
        {
            // 刷新后原选中条目已被删除：清空选择，避免对已删除条目继续操作
            this.selectedCustomItem = null;
            this.RaisePropertyChanged(nameof(this.SelectedCustomItem));
        }
        this.RaisePropertyChanged(nameof(this.HasNoCustomItems));
        this.UpdateSummary();
    }

    /// <summary>按系统启动项与自定义启动项数量更新摘要。</summary>
    private void UpdateSummary()
    {
        int enabledSystem = 0;
        foreach (StartupRow row in this.SystemItems)
        {
            if (row.IsEnabled)
            {
                enabledSystem++;
            }
        }
        int enabledCustom = 0;
        foreach (CustomStartupRow row in this.CustomItems)
        {
            if (row.IsEnabled)
            {
                enabledCustom++;
            }
        }
        this.SummaryText = "系统启动项 " + this.SystemItems.Count + " 项（启用 " + enabledSystem
            + " 项）· 自定义启动项 " + this.CustomItems.Count + " 项（启用 " + enabledCustom + " 项）";
    }

    /// <summary>启用或禁用一条系统启动项，返回给界面的提示文字。</summary>
    public string ToggleSystem(StartupRow row)
    {
        if (row == null)
        {
            this.StatusText = "请先选择要启用或禁用的系统启动项";
            UiLogger.Log("启动项", "切换系统启动项被忽略：没有选中条目");
            return this.StatusText;
        }
        if (!row.CanToggle)
        {
            this.StatusText = "无法修改「" + row.Name + "」：" + row.ToggleHint;
            UiLogger.Log("启动项", "切换系统启动项被忽略：条目不允许修改 名称=" + row.Name
                + " 来源=" + row.SourceText + " 位置=" + row.Item.Location + " 原因=" + row.ToggleHint);
            return this.StatusText;
        }
        bool targetEnabled = !row.IsEnabled;
        bool success = this.provider.SetEnabled(row.Item, targetEnabled);
        string resultText = success ? "成功" : "失败（可能权限不足或条目已被系统移除）";
        string resultMessage = (success ? "" : "操作失败：") + "「" + row.Name + "」已" + (targetEnabled ? "启用" : "禁用")
            + (success ? "" : "，请以管理员权限重新尝试");
        UiLogger.Log("启动项", "切换系统启动项：名称=" + row.Name + " 类型=" + row.KindText
            + " 来源=" + row.SourceText + " 位置=" + row.Item.Location + " 目标=" + row.CommandText
            + " 原状态=" + (row.IsEnabled ? "已启用" : "已禁用") + " 目标状态=" + (targetEnabled ? "已启用" : "已禁用")
            + " 结果=" + resultText);
        if (success && row.Item.IsSelfAutoStart)
        {
            // 本程序自身的自启项：同步「开机启动」配置，避免下次启动时按旧配置重新写回
            this.settings.AutoStart = targetEnabled;
            this.settingsStore.Save(this.settings);
            UiLogger.Log("启动项", "同步本程序开机启动配置：系统启动项已" + (targetEnabled ? "启用" : "禁用")
                + " 配置值=" + this.settings.AutoStart);
        }
        this.Refresh();
        this.StatusText = resultMessage;
        return resultMessage;
    }

    /// <summary>启用或禁用当前选中的系统启动项。</summary>
    public string ToggleSelectedSystem()
    {
        return this.ToggleSystem(this.selectedSystemItem);
    }

    /// <summary>切换一条自定义启动项的启用状态。</summary>
    public string ToggleCustom(CustomStartupRow row)
    {
        if (row == null)
        {
            this.StatusText = "请先选择要启用或禁用的自定义启动项";
            UiLogger.Log("启动项", "切换自定义启动项被忽略：没有选中条目");
            return this.StatusText;
        }
        bool targetEnabled = !row.IsEnabled;
        bool success = this.customService.SetEnabled(row.Id, targetEnabled);
        this.StatusText = success
            ? "「" + row.Name + "」已" + (targetEnabled ? "启用" : "禁用")
            : "切换自定义启动项失败：" + row.Name;
        this.RefreshCustomItems();
        return this.StatusText;
    }

    /// <summary>切换当前选中自定义启动项的启用状态。</summary>
    public string ToggleSelectedCustom()
    {
        return this.ToggleCustom(this.selectedCustomItem);
    }

    /// <summary>立即拉起一条自定义启动项。</summary>
    public string RunCustom(CustomStartupRow row)
    {
        if (row == null)
        {
            this.StatusText = "请先选择要立即运行的自定义启动项";
            UiLogger.Log("启动项", "立即运行自定义启动项被忽略：没有选中条目");
            return this.StatusText;
        }
        StartupLaunchResult result = this.customService.LaunchNow(row.Item);
        this.StatusText = "立即运行「" + row.Name + "」：" + result.Message;
        UiLogger.Log("启动项", "立即运行自定义启动项：编号=" + row.Id + " 名称=" + row.Name
            + " 类型=" + row.KindText + " 目标=" + row.Item.Target + " 参数=" + row.Item.Arguments
            + " 结果=" + (result.Success ? "成功" : "失败") + " 说明=" + result.Message);
        this.RefreshCustomItems();
        return this.StatusText;
    }

    /// <summary>立即拉起当前选中的自定义启动项。</summary>
    public string RunSelectedCustom()
    {
        return this.RunCustom(this.selectedCustomItem);
    }

    /// <summary>删除当前选中的自定义启动项。</summary>
    public string DeleteSelectedCustom()
    {
        CustomStartupRow? row = this.selectedCustomItem;
        if (row == null)
        {
            this.StatusText = "请先选择要删除的自定义启动项";
            UiLogger.Log("启动项", "删除自定义启动项被忽略：没有选中条目");
            return this.StatusText;
        }
        bool success = this.customService.Remove(row.Id);
        if (success)
        {
            this.SelectedCustomItem = null;
            this.NewCustom();
            this.StatusText = "已删除自定义启动项：" + row.Name;
        }
        else
        {
            this.StatusText = "删除自定义启动项失败：" + row.Name;
        }
        this.RefreshCustomItems();
        return this.StatusText;
    }

    /// <summary>清空编辑表单，进入新建自定义启动项状态。</summary>
    public void NewCustom()
    {
        this.EditingId = string.Empty;
        this.EditName = string.Empty;
        this.EditKindIndex = 0;
        this.EditTarget = string.Empty;
        this.EditArguments = string.Empty;
        this.EditDelaySeconds = 0;
        this.EditEnabled = true;
        this.selectedCustomItem = null;
        this.RaisePropertyChanged(nameof(this.SelectedCustomItem));
        this.StatusText = "已切换到新建自定义启动项，填写完成后点「保存」";
        UiLogger.Log("启动项", "界面操作：新建自定义启动项，编辑表单已清空");
    }

    /// <summary>保存编辑表单中的自定义启动项。</summary>
    public string SaveCustom()
    {
        CustomStartupItem? saved = this.customService.Save(this.EditingId, this.EditName,
            StartupItemType.FromIndex(this.EditKindIndex), this.EditTarget, this.EditArguments,
            this.EditDelaySeconds, this.EditEnabled);
        if (saved == null)
        {
            this.StatusText = "保存失败：名称与启动目标不能为空";
            return this.StatusText;
        }
        this.EditingId = saved.Id;
        this.RefreshCustomItems();
        this.SelectCustomById(saved.Id);
        this.StatusText = "已保存自定义启动项：" + saved.Name;
        UiLogger.Log("启动项", "界面操作：保存自定义启动项 编号=" + saved.Id + " 名称=" + saved.Name
            + " 类型=" + saved.ItemType + " 目标=" + saved.Target + " 延迟秒数=" + saved.DelaySeconds
            + " 启用=" + saved.Enabled);
        return this.StatusText;
    }

    /// <summary>把一条自定义启动项的字段回填到编辑表单。</summary>
    private void LoadEditFields(CustomStartupItem item)
    {
        this.EditingId = item.Id;
        this.EditName = item.Name;
        this.EditKindIndex = StartupItemType.ToIndex(item.ItemType);
        this.EditTarget = item.Target;
        this.EditArguments = item.Arguments;
        this.EditDelaySeconds = item.DelaySeconds;
        this.EditEnabled = item.Enabled;
        UiLogger.Log("启动项", "编辑表单已载入自定义启动项：编号=" + item.Id + " 名称=" + item.Name);
    }

    /// <summary>按编号选中自定义启动项列表中的一行。</summary>
    private void SelectCustomById(string id)
    {
        foreach (CustomStartupRow row in this.CustomItems)
        {
            if (string.Equals(row.Id, id, StringComparison.Ordinal))
            {
                this.SelectedCustomItem = row;
                return;
            }
        }
    }

    /// <summary>执行网页端传来的启动项管理动作，返回给页面的提示文字。</summary>
    public string ExecuteAction(string action, string argument)
    {
        if (action == "startuprefresh")
        {
            this.Refresh();
            this.RefreshCustomItems();
            return "正在刷新启动项……";
        }
        if (action == "startuptoggle")
        {
            return this.ToggleSystem(this.FindSystemRow(argument));
        }
        if (action == "startupcustomtoggle")
        {
            return this.ToggleCustom(this.FindCustomRow(argument));
        }
        if (action == "startupcustomrun")
        {
            return this.RunCustom(this.FindCustomRow(argument));
        }
        if (action == "startupcustomdelete")
        {
            this.SelectedCustomItem = this.FindCustomRow(argument);
            return this.DeleteSelectedCustom();
        }
        if (action == "startupcustomsave")
        {
            return this.SaveCustomFromArgument(argument);
        }
        return "未知动作：" + action;
    }

    /// <summary>按编号查找系统启动项行。</summary>
    private StartupRow? FindSystemRow(string id)
    {
        foreach (StartupRow row in this.SystemItems)
        {
            if (string.Equals(row.Id, id, StringComparison.Ordinal))
            {
                return row;
            }
        }
        return null;
    }

    /// <summary>按编号查找自定义启动项行。</summary>
    private CustomStartupRow? FindCustomRow(string id)
    {
        foreach (CustomStartupRow row in this.CustomItems)
        {
            if (string.Equals(row.Id, id, StringComparison.Ordinal))
            {
                return row;
            }
        }
        return null;
    }

    /// <summary>
    /// 解析网页端保存自定义启动项的参数：共 7 行，依次为编号、名称、类型下标、目标、参数、延迟秒数、启用标志。
    /// </summary>
    private string SaveCustomFromArgument(string argument)
    {
        string[] parts = argument == null ? new string[0] : argument.Split(new char[] { '\n' }, 7);
        if (parts.Length < 7)
        {
            return "保存参数不完整，需要编号、名称、类型、目标、参数、延迟秒数与启用标志共 7 行";
        }
        string id = parts[0].Trim();
        string name = parts[1].Trim();
        int kindIndex = 0;
        int.TryParse(parts[2].Trim(), out kindIndex);
        string target = parts[3].Trim();
        string arguments = parts[4].Trim();
        int delaySeconds = 0;
        int.TryParse(parts[5].Trim(), out delaySeconds);
        bool enabled = parts[6].Trim() == "1";
        CustomStartupItem? saved = this.customService.Save(id, name, StartupItemType.FromIndex(kindIndex),
            target, arguments, delaySeconds, enabled);
        if (saved == null)
        {
            return "保存失败：名称与启动目标不能为空";
        }
        this.RefreshCustomItems();
        this.SelectCustomById(saved.Id);
        return "已保存自定义启动项：" + saved.Name;
    }

    /// <summary>关闭标签时停止后续刷新。</summary>
    public void Close()
    {
        this.closed = true;
        UiLogger.Log("启动项", "启动项管理标签已关闭：停止后续刷新");
    }

    /// <summary>把拖动得到的列宽应用到界面。</summary>
    public void ApplyColumnWidths(double[] widths)
    {
        this.ColumnWidths = ColumnResize.FormatWidths(widths);
    }

    /// <summary>记录表头列宽拖动完成日志。</summary>
    public void LogColumnWidths()
    {
        if (this.columnWidths.Length == 0)
        {
            UiLogger.Log("启动项", "表头列宽拖动结束：本次没有产生列宽变化，仍为默认列宽");
            return;
        }
        UiLogger.Log("启动项", "表头列宽拖动结束：调整后列宽=" + this.columnWidths + "像素");
    }

    /// <summary>通知界面属性变化。</summary>
    private void RaisePropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
