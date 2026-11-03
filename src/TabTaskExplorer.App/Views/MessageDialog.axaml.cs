using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;

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

    /// <summary>点击确定关闭。</summary>
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
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
        this.choice = SavePromptChoice.Save;
        UiLogger.Log("程序行为", "未保存更改处理点击保存：标题=" + this.Title
            + " 结果=成功，选择保存后关闭，未保存的更改将写入文件");
        this.Close();
    }

    private void OnDontSaveChoiceClick(object? sender, RoutedEventArgs e)
    {
        this.choice = SavePromptChoice.DontSave;
        UiLogger.Log("程序行为", "未保存更改处理点击不保存：标题=" + this.Title
            + " 结果=成功，选择不保存直接关闭，未保存的更改被丢弃");
        this.Close();
    }

    private void OnCancelChoiceClick(object? sender, RoutedEventArgs e)
    {
        this.choice = SavePromptChoice.Cancel;
        UiLogger.Log("程序行为", "未保存更改处理点击取消：标题=" + this.Title
            + " 结果=成功，已取消关闭操作，未保存的更改保持原样");
        this.Close();
    }
}
