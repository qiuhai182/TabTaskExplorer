using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Tasks;

namespace TabTaskExplorer.App.Views;

/// <summary>未保存更改处理方式。</summary>
public enum SavePromptChoice
{
    /// <summary>取消关闭。</summary>
    Cancel = 0,

    /// <summary>保存后关闭。</summary>
    Save = 1,

    /// <summary>不保存直接关闭。</summary>
    DontSave = 2,
}

/// <summary>简单消息提示窗：默认单按钮提示，也支持「保存 / 不保存 / 取消」三选一确认。</summary>
public partial class MessageDialog : Window
{
    private SavePromptChoice choice = SavePromptChoice.Cancel;

    /// <summary>是否为「确认 / 取消」二选一模式（此时确定按钮代表确认）。</summary>
    private bool confirmMode;

    /// <summary>是否为任务冲突处理模式（同名文件二选一 / 同名文件夹四选一）。</summary>
    private bool conflictMode;

    /// <summary>是否为密码输入模式；用户取消或关闭窗口时密码保持 null。</summary>
    private bool passwordMode;

    /// <summary>密码输入模式下用户点击“连接”后填写的值；取消或关闭为 null。</summary>
    private string? passwordValue;

    /// <summary>冲突模式下的选择；默认取消任务，关闭窗口也按取消处理。</summary>
    private FileConflictAction conflictAction = FileConflictAction.Cancel;

    /// <summary>冲突模式下四个按钮分别写回的动作。</summary>
    private FileConflictAction okAction = FileConflictAction.Skip;
    private FileConflictAction saveAction = FileConflictAction.Skip;
    private FileConflictAction dontSaveAction = FileConflictAction.Skip;
    private FileConflictAction cancelAction = FileConflictAction.Cancel;

    /// <summary>构造消息窗。</summary>
    public MessageDialog()
    {
        this.InitializeComponent();
        // 临时提示 / 确认框属于辅助工具窗口，不进入任务栏与 Alt+Tab
        WindowHostStyler.MakeAuxiliary(this);
        UiLogger.AttachWindow(this);
    }

    /// <summary>弹出消息提示并等待用户确认。</summary>
    public static void Show(Window? owner, string title, string message)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Show(owner, title, message));
            return;
        }
        MessageDialog dialog = new MessageDialog();
        dialog.Title = title;
        dialog.MessageText.Text = message;
        if (owner != null)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
    }

    /// <summary>弹出「保存 / 不保存 / 取消」确认框（用于关闭含有未保存更改的编辑器）。</summary>
    public static async Task<SavePromptChoice> ConfirmSaveAsync(Window? owner, string fileName)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Task<SavePromptChoice> inner = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                new Func<Task<SavePromptChoice>>(() => ConfirmSaveAsync(owner, fileName)));
            return await inner;
        }
        MessageDialog dialog = new MessageDialog();
        dialog.Title = "未保存的更改";
        dialog.MessageText.Text = "文件「" + fileName + "」有未保存的更改，是否保存？";
        dialog.OkButton.IsVisible = false;
        dialog.SaveButton.IsVisible = true;
        dialog.DontSaveButton.IsVisible = true;
        dialog.CancelChoiceButton.IsVisible = true;
        if (owner != null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
        return dialog.choice;
    }

    /// <summary>
    /// 弹出「确认 / 取消」二选一对话框：点确认返回 true，点取消或关闭返回 false。
    /// 用于媒体打开方式询问等需要用户当场拍板的场景。
    /// </summary>
    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message,
        string confirmText, string cancelText)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Task<bool> inner = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                new Func<Task<bool>>(() => ConfirmAsync(owner, title, message, confirmText, cancelText)));
            return await inner;
        }
        MessageDialog dialog = new MessageDialog();
        dialog.confirmMode = true;
        dialog.Title = title;
        dialog.MessageText.Text = message;
        dialog.OkButton.Content = confirmText;
        dialog.CancelChoiceButton.Content = cancelText;
        dialog.CancelChoiceButton.IsVisible = true;
        if (owner != null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
        return dialog.choice == SavePromptChoice.Save;
    }

    /// <summary>
    /// 任务冲突询问：同名文件二选一（替换 / 改名共存），同名文件夹四选一（合并 / 替换 / 逐项处理 / 改名共存），
    /// 可勾选「本批统一按此处理」；文件夹对话框关闭窗口按取消任务处理。
    /// </summary>
    public static async Task<(FileConflictAction Action, bool ApplyToAll)> AskTaskConflictAsync(
        Window? owner, string title, string message, bool isDirectory)
    {
        // 必须从界面线程发起：后台线程调用时先投递到 UI 线程再弹窗
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Task<(FileConflictAction Action, bool ApplyToAll)> inner =
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    new Func<Task<(FileConflictAction Action, bool ApplyToAll)>>(
                        () => AskTaskConflictAsync(owner, title, message, isDirectory)));
            return await inner;
        }
        MessageDialog dialog = new MessageDialog();
        dialog.conflictMode = true;
        dialog.Title = title;
        dialog.MessageText.Text = message;
        dialog.ApplyAllCheck.IsVisible = true;
        if (isDirectory)
        {
            dialog.okAction = FileConflictAction.Merge;
            dialog.saveAction = FileConflictAction.Overwrite;
            dialog.dontSaveAction = FileConflictAction.PerItem;
            dialog.cancelAction = FileConflictAction.Rename;
            dialog.OkButton.Content = "合并";
            dialog.SaveButton.Content = "替换";
            dialog.DontSaveButton.Content = "逐项处理";
            dialog.CancelChoiceButton.Content = "改名共存";
        }
        else
        {
            dialog.okAction = FileConflictAction.Overwrite;
            dialog.saveAction = FileConflictAction.Rename;
            dialog.cancelAction = FileConflictAction.Cancel;
            dialog.OkButton.Content = "替换";
            dialog.SaveButton.Content = "改名共存";
            dialog.CancelChoiceButton.Content = "取消任务";
            dialog.DontSaveButton.IsVisible = false;
        }
        dialog.SaveButton.IsVisible = true;
        dialog.DontSaveButton.IsVisible = isDirectory;
        dialog.CancelChoiceButton.IsVisible = true;
        dialog.Opened += delegate
        {
            // 默认聚焦更安全的「改名共存」
            if (isDirectory)
            {
                dialog.CancelChoiceButton.Focus();
            }
            else
            {
                dialog.SaveButton.Focus();
            }
        };
        TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>();
        dialog.Closed += delegate { closed.TrySetResult(true); };
        if (owner != null)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
        await closed.Task;
        return (dialog.conflictAction, dialog.ApplyAllCheck.IsChecked == true);
    }

    /// <summary>
    /// 密码输入对话框：用于 Xshell / Xftp 导入连接缺少密码时要求用户现场补填。
    /// 点“连接”返回输入值（允许空串由调用方判定），点取消或关闭返回 null。日志与界面不输出明文。
    /// </summary>
    public static async Task<string?> PromptPasswordAsync(Window? owner, string title, string message)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Task<string?> inner = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                new Func<Task<string?>>(() => PromptPasswordAsync(owner, title, message)));
            return await inner;
        }
        MessageDialog dialog = new MessageDialog();
        dialog.passwordMode = true;
        dialog.Title = title;
        dialog.MessageText.Text = message;
        dialog.PasswordInput.IsVisible = true;
        dialog.OkButton.Content = "连接";
        dialog.CancelChoiceButton.Content = "取消";
        dialog.CancelChoiceButton.IsVisible = true;
        dialog.Opened += delegate { dialog.PasswordInput.Focus(); };
        TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>();
        dialog.Closed += delegate { closed.TrySetResult(true); };
        if (owner != null)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
        await closed.Task;
        UiLogger.Log("程序行为", "密码输入对话框关闭：标题=" + title + " 结果="
            + (dialog.passwordValue == null ? "取消或关闭，未提供密码" : "已提供密码，长度=" + dialog.passwordValue.Length));
        return dialog.passwordValue;
    }

    /// <summary>点击确定关闭。</summary>
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (this.passwordMode)
        {
            this.passwordValue = this.PasswordInput.Text ?? string.Empty;
        }
        if (this.conflictMode)
        {
            this.conflictAction = this.okAction;
        }
        if (this.confirmMode)
        {
            this.choice = SavePromptChoice.Save;
        }
        UiLogger.Log("程序行为", "消息提示窗点击确定：标题=" + this.Title
            + " 结果=成功，已确认提示并关闭对话框");
        this.Close();
    }

    private void OnSaveChoiceClick(object? sender, RoutedEventArgs e)
    {
        if (this.conflictMode)
        {
            this.conflictAction = this.saveAction;
        }
        this.choice = SavePromptChoice.Save;
        UiLogger.Log("程序行为", "未保存更改处理点击保存：标题=" + this.Title
            + " 结果=成功，选择保存后关闭，未保存的更改将写入文件");
        this.Close();
    }

    private void OnDontSaveChoiceClick(object? sender, RoutedEventArgs e)
    {
        if (this.conflictMode)
        {
            this.conflictAction = this.dontSaveAction;
        }
        this.choice = SavePromptChoice.DontSave;
        UiLogger.Log("程序行为", "未保存更改处理点击不保存：标题=" + this.Title
            + " 结果=成功，选择不保存直接关闭，未保存的更改被丢弃");
        this.Close();
    }

    private void OnCancelChoiceClick(object? sender, RoutedEventArgs e)
    {
        if (this.conflictMode)
        {
            this.conflictAction = this.cancelAction;
        }
        this.choice = SavePromptChoice.Cancel;
        UiLogger.Log("程序行为", "未保存更改处理点击取消：标题=" + this.Title
            + " 结果=成功，已取消关闭操作，未保存的更改保持原样");
        this.Close();
    }
}
