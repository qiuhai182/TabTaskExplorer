using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TabTaskExplorer.App.Views;

/// <summary>输入对话框：获取一行用户输入。</summary>
public partial class InputDialog : Window
{
    private InputDialog()
    {
        this.InitializeComponent();
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
        this.Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
