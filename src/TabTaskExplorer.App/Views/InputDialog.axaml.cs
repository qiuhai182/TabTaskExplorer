using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>输入对话框：获取一行用户输入。</summary>
public partial class InputDialog : Window
{
    /// <summary>请通过 Show 方法使用本对话框。</summary>
    public InputDialog()
    {
        this.InitializeComponent();
        // 输入框属于辅助工具窗口，不进入任务栏与 Alt+Tab
        WindowHostStyler.MakeAuxiliary(this);
        UiLogger.AttachWindow(this);
    }

    /// <summary>用户点击确定后的输入结果。</summary>
    public string? ResultValue { get; private set; }

    /// <summary>显示输入对话框并等待用户操作。</summary>
    public static async Task<string?> Show(Window owner, string title, string prompt, string defaultValue)
    {
        InputDialog dialog = new InputDialog();
        dialog.Title = title;
        dialog.PromptText.Text = prompt;
        dialog.ValueBox.Text = defaultValue;
        await dialog.ShowDialog(owner);
        return dialog.ResultValue;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        this.ResultValue = this.ValueBox.Text;
        UiLogger.Log("程序行为", "输入对话框点击确定：标题=" + this.Title
            + " 输入值=" + (this.ResultValue ?? string.Empty)
            + " 结果=成功，已提交输入并关闭对话框");
        this.Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("程序行为", "输入对话框点击取消：标题=" + this.Title
            + " 结果=已关闭对话框，未提交任何输入");
        this.Close();
    }
}
