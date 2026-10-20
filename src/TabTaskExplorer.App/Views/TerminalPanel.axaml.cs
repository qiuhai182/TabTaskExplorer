using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;

namespace TabTaskExplorer.App.Views;

/// <summary>远程终端面板交互逻辑。</summary>
public partial class TerminalPanel : UserControl
{
    /// <summary>构造终端面板。</summary>
    public TerminalPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>终端视图模型。</summary>
    private TerminalTabViewModel? ViewModel
    {
        get { return this.DataContext as TerminalTabViewModel; }
    }

    /// <summary>发送输入行。</summary>
    private void OnSendClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.SendInput();
    }

    /// <summary>发送选中的快捷命令。</summary>
    private void OnQuickSendClick(object? sender, RoutedEventArgs e)
    {
        string? command = this.QuickCommandBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(command))
        {
            this.ViewModel?.SendQuickCommand(command);
        }
    }

    /// <summary>断线后重连。</summary>
    private void OnReconnectClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.RequestReconnect();
    }

    /// <summary>清空输出。</summary>
    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.ClearOutput();
    }

    /// <summary>切换会话日志记录。</summary>
    private void OnLogToggleClick(object? sender, RoutedEventArgs e)
    {
        TerminalTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.ToggleLogging();
        this.LogPathText.Text = viewModel.LoggingEnabled ? viewModel.LogFilePath : string.Empty;
    }

    /// <summary>断开会话。</summary>
    private void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.Disconnect();
    }

    /// <summary>输入框回车发送。</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            this.ViewModel?.SendInput();
            e.Handled = true;
        }
    }
}
