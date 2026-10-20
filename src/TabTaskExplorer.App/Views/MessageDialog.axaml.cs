using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TabTaskExplorer.App.Views;

/// <summary>简单消息提示窗：一个图标一行文字加确定按钮，风格与 InputDialog 一致。</summary>
public partial class MessageDialog : Window
{
    /// <summary>构造消息窗。</summary>
    public MessageDialog()
    {
        this.InitializeComponent();
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

    /// <summary>点击确定关闭。</summary>
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
