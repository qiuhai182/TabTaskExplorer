using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 自定义启动项服务：负责配置的增删改、立即拉起，以及每次开机后的首次自动拉起。
/// 开机判定使用 <see cref="BootSessionIdentifier"/>：同一开机会话内多次启动本程序时，
/// 已经记录过本开机标识的条目不会重复拉起；重启系统后标识变化，全部条目重新按顺序拉起。
/// </summary>
public class CustomStartupService
{
    private const int MaximumDelaySeconds = 600;

    private readonly AppSettings settings;
    private readonly AppSettingsStore store;
    private readonly object gate = new object();
    private string currentBootId = string.Empty;
    private bool bootRunStarted = false;

    public CustomStartupService(AppSettings settings, AppSettingsStore store)
    {
        this.settings = settings;
        this.store = store;
    }

    /// <summary>本次开机会话标识。</summary>
    public string CurrentBootId
    {
        get
        {
            if (string.IsNullOrEmpty(this.currentBootId))
            {
                this.currentBootId = BootSessionIdentifier.GetCurrentSessionId();
            }
            return this.currentBootId;
        }
    }

    /// <summary>取自定义启动项清单的副本（避免后台拉起线程与界面同时遍历同一列表）。</summary>
    public List<CustomStartupItem> List()
    {
        lock (this.gate)
        {
            List<CustomStartupItem> items = new List<CustomStartupItem>();
            if (this.settings.CustomStartupItems == null)
            {
                this.settings.CustomStartupItems = new List<CustomStartupItem>();
            }
            foreach (CustomStartupItem item in this.settings.CustomStartupItems)
            {
                items.Add(item);
            }
            return items;
        }
    }

    /// <summary>按编号查找自定义启动项；不存在返回 null。</summary>
    public CustomStartupItem? FindById(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        lock (this.gate)
        {
            if (this.settings.CustomStartupItems == null)
            {
                return null;
            }
            foreach (CustomStartupItem item in this.settings.CustomStartupItems)
            {
                if (string.Equals(item.Id, id, StringComparison.Ordinal))
                {
                    return item;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 新增或更新一条自定义启动项。编号为空时新建；名称与目标为空时拒绝保存。
    /// </summary>
    public CustomStartupItem? Save(string id, string name, string itemType, string target,
        string arguments, int delaySeconds, bool enabled)
    {
        string trimmedName = name == null ? string.Empty : name.Trim();
        string trimmedTarget = target == null ? string.Empty : target.Trim();
        if (trimmedName.Length == 0)
        {
            UiLogger.Log("启动项", "保存自定义启动项被拒绝：名称为空 编号=" + id + " 目标=" + trimmedTarget);
            return null;
        }
        if (trimmedTarget.Length == 0)
        {
            UiLogger.Log("启动项", "保存自定义启动项被拒绝：启动目标为空 编号=" + id + " 名称=" + trimmedName);
            return null;
        }
        int normalizedDelay = delaySeconds;
        if (normalizedDelay < 0)
        {
            normalizedDelay = 0;
        }
        if (normalizedDelay > MaximumDelaySeconds)
        {
            normalizedDelay = MaximumDelaySeconds;
        }
        CustomStartupItem? targetItem = this.FindById(id);
        bool created = targetItem == null;
        if (targetItem == null)
        {
            targetItem = new CustomStartupItem();
            targetItem.Id = Guid.NewGuid().ToString("N");
            lock (this.gate)
            {
                if (this.settings.CustomStartupItems == null)
                {
                    this.settings.CustomStartupItems = new List<CustomStartupItem>();
                }
                this.settings.CustomStartupItems.Add(targetItem);
            }
        }
        targetItem.Name = trimmedName;
        targetItem.ItemType = string.IsNullOrEmpty(itemType) ? StartupItemType.Program : itemType;
        targetItem.Target = trimmedTarget;
        targetItem.Arguments = arguments == null ? string.Empty : arguments.Trim();
        targetItem.DelaySeconds = normalizedDelay;
        targetItem.Enabled = enabled;
        this.store.Save(this.settings);
        UiLogger.Log("启动项", "保存自定义启动项：动作=" + (created ? "新增" : "更新")
            + " 编号=" + targetItem.Id + " 名称=" + targetItem.Name + " 类型=" + targetItem.ItemType
            + " 目标=" + targetItem.Target + " 参数=" + targetItem.Arguments
            + " 延迟秒数=" + targetItem.DelaySeconds + " 启用=" + targetItem.Enabled);
        return targetItem;
    }

    /// <summary>切换一条自定义启动项的启用状态。</summary>
    public bool SetEnabled(string id, bool enabled)
    {
        CustomStartupItem? item = this.FindById(id);
        if (item == null)
        {
            UiLogger.Log("启动项", "切换自定义启动项启用状态被忽略：找不到条目 编号=" + id);
            return false;
        }
        item.Enabled = enabled;
        this.store.Save(this.settings);
        UiLogger.Log("启动项", "切换自定义启动项启用状态：编号=" + item.Id + " 名称=" + item.Name + " 新状态=" + (enabled ? "启用" : "禁用"));
        return true;
    }

    /// <summary>删除一条自定义启动项。</summary>
    public bool Remove(string id)
    {
        lock (this.gate)
        {
            if (this.settings.CustomStartupItems == null)
            {
                return false;
            }
            for (int index = 0; index < this.settings.CustomStartupItems.Count; index++)
            {
                if (string.Equals(this.settings.CustomStartupItems[index].Id, id, StringComparison.Ordinal))
                {
                    string name = this.settings.CustomStartupItems[index].Name;
                    this.settings.CustomStartupItems.RemoveAt(index);
                    this.store.Save(this.settings);
                    UiLogger.Log("启动项", "删除自定义启动项：编号=" + id + " 名称=" + name
                        + " 剩余条数=" + this.settings.CustomStartupItems.Count);
                    return true;
                }
            }
        }
        UiLogger.Log("启动项", "删除自定义启动项被忽略：找不到条目 编号=" + id);
        return false;
    }

    /// <summary>立即拉起一条自定义启动项，并记录本次开机已执行。</summary>
    public StartupLaunchResult LaunchNow(CustomStartupItem item)
    {
        if (item == null)
        {
            StartupLaunchResult missing = new StartupLaunchResult();
            missing.Success = false;
            missing.Message = "启动项为空";
            return missing;
        }
        StartupLaunchResult result = StartupItemLauncher.Launch(item.ItemType, item.Target, item.Arguments);
        this.RecordLaunchResult(item, result);
        return result;
    }

    /// <summary>
    /// 每次开机后的首次启动检查：对启用且本次开机未执行过的条目按延迟顺序拉起。
    /// 同一进程生命周期内只执行一次；重复启动本程序时由条目的开机标识避免重复拉起。
    /// </summary>
    public void RunBootItemsIfNeeded()
    {
        if (this.bootRunStarted)
        {
            UiLogger.Log("启动项", "本次程序运行的自动拉起已执行过，跳过：开机标识=" + this.CurrentBootId);
            return;
        }
        this.bootRunStarted = true;
        if (Dispatcher.UIThread.CheckAccess())
        {
            this.RunBootItemsAsync();
            return;
        }
        Dispatcher.UIThread.Post(new Action(this.RunBootItemsAsync));
    }

    /// <summary>在界面线程按延迟顺序拉起本次开机尚未执行的条目。</summary>
    private async void RunBootItemsAsync()
    {
        string bootId = this.CurrentBootId;
        List<CustomStartupItem> items = this.List();
        UiLogger.Log("启动项", "开始检查自定义启动项：本次开机标识=" + bootId + " 条目总数=" + items.Count);
        int launchedCount = 0;
        int skippedDisabledCount = 0;
        int skippedLaunchedCount = 0;
        int failedCount = 0;
        try
        {
            foreach (CustomStartupItem item in items)
            {
                if (!item.Enabled)
                {
                    skippedDisabledCount++;
                    UiLogger.Log("启动项", "跳过自定义启动项：原因=已禁用 编号=" + item.Id + " 名称=" + item.Name);
                    continue;
                }
                if (string.Equals(item.LastLaunchBootId, bootId, StringComparison.Ordinal))
                {
                    skippedLaunchedCount++;
                    UiLogger.Log("启动项", "跳过自定义启动项：原因=本次开机已启动过 编号=" + item.Id
                        + " 名称=" + item.Name + " 上次启动时间=" + item.LastLaunchTimeText);
                    continue;
                }
                int delaySeconds = item.DelaySeconds;
                if (delaySeconds > MaximumDelaySeconds)
                {
                    delaySeconds = MaximumDelaySeconds;
                }
                if (delaySeconds > 0)
                {
                    UiLogger.Log("启动项", "等待延迟：编号=" + item.Id + " 名称=" + item.Name + " 延迟秒数=" + delaySeconds);
                    await Task.Delay(delaySeconds * 1000);
                }
                StartupLaunchResult result = StartupItemLauncher.Launch(item.ItemType, item.Target, item.Arguments);
                this.RecordLaunchResult(item, result);
                if (result.Success)
                {
                    launchedCount++;
                }
                else
                {
                    failedCount++;
                }
                UiLogger.Log("启动项", "自动拉起自定义启动项：编号=" + item.Id + " 名称=" + item.Name
                    + " 类型=" + item.ItemType + " 目标=" + item.Target + " 参数=" + item.Arguments
                    + " 延迟秒数=" + delaySeconds + " 结果=" + (result.Success ? "成功" : "失败")
                    + " 说明=" + result.Message + " 开机标识=" + bootId);
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("启动项", "自动拉起自定义启动项过程中出现异常，已停止后续条目 开机标识=" + bootId, error);
        }
        UiLogger.Log("启动项", "自定义启动项检查完成：开机标识=" + bootId + " 本次拉起=" + launchedCount
            + " 失败=" + failedCount + " 因禁用跳过=" + skippedDisabledCount
            + " 因本次开机已启动跳过=" + skippedLaunchedCount);
    }

    /// <summary>记录一次拉起结果并立即落盘，保证下次启动能识别本次开机是否已执行。</summary>
    private void RecordLaunchResult(CustomStartupItem item, StartupLaunchResult result)
    {
        item.LastLaunchBootId = this.CurrentBootId;
        item.LastLaunchTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        item.LastResult = result.Success ? result.Message : "失败：" + result.Message;
        this.store.Save(this.settings);
    }

    /// <summary>把条目在本次开机的执行状态转成界面文字。</summary>
    public string DescribeBootState(CustomStartupItem item)
    {
        if (item == null)
        {
            return string.Empty;
        }
        if (string.Equals(item.LastLaunchBootId, this.CurrentBootId, StringComparison.Ordinal))
        {
            return "本次开机已启动：" + item.LastLaunchTimeText;
        }
        return "本次开机未启动";
    }
}
