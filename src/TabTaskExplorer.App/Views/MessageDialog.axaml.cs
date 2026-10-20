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

    /// <summary>构造消息窗。</summary>
    public MessageDialog()
    {
        this.InitializeComponent();
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

    /// <summary>点击确定关闭。</summary>
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void OnSaveChoiceClick(object? sender, RoutedEventArgs e)
    {
        this.choice = SavePromptChoice.Save;
        this.Close();
    }

    private void OnDontSaveChoiceClick(object? sender, RoutedEventArgs e)
    {
        this.choice = SavePromptChoice.DontSave;
        this.Close();
    }

    private void OnCancelChoiceClick(object? sender, RoutedEventArgs e)
    {
        this.choice = SavePromptChoice.Cancel;
        this.Close();
    }
}
