using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
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
        TerminalTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=发送输入 被点击：终端会话视图模型未就绪，结果=失败，未发送任何内容");
            return;
        }
        string command = viewModel.InputText;
        if (string.IsNullOrWhiteSpace(command))
        {
            UiLogger.Log("程序行为", "按钮=发送输入 被点击：命令文本=（空白） 结果=未发送，输入区没有可发送的命令");
            return;
        }
        viewModel.SendInput();
        UiLogger.Log("程序行为", "按钮=发送输入 被点击：命令文本=" + command.Trim() + " 终端会话标识=" + viewModel.Title
            + " 结果=成功，命令已写入终端会话并清空输入区");
    }

    /// <summary>发送选中的快捷命令。</summary>
    private void OnQuickSendClick(object? sender, RoutedEventArgs e)
    {
        TerminalTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=快捷命令 被点击：终端会话视图模型未就绪，结果=失败，未发送快捷命令");
            return;
        }
        string? command = this.QuickCommandBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(command))
        {
            UiLogger.Log("程序行为", "按钮=快捷命令 被点击：快捷命令=（未选中） 结果=未发送，快捷命令下拉框没有选中项");
            return;
        }
        viewModel.SendQuickCommand(command);
        UiLogger.Log("程序行为", "按钮=快捷命令 被点击：命令文本=" + command.Trim() + " 终端会话标识=" + viewModel.Title
            + " 结果=成功，快捷命令已写入终端会话");
    }

    /// <summary>断线后重连。</summary>
    private void OnReconnectClick(object? sender, RoutedEventArgs e)
    {
        TerminalTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=重连 被点击：终端会话视图模型未就绪，结果=失败，未提交重连请求");
            return;
        }
        viewModel.RequestReconnect();
        UiLogger.Log("程序行为", "按钮=重连 被点击：终端会话标识=" + viewModel.Title
            + " 连接配置编号=" + viewModel.AttachedProfileId
            + " 结果=成功，已向主窗口提交按连接配置重建会话的重连请求");
    }

    /// <summary>清空输出。</summary>
    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        TerminalTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=清屏 被点击：终端会话视图模型未就绪，结果=失败，未清空输出");
            return;
        }
        viewModel.ClearOutput();
        UiLogger.Log("程序行为", "按钮=清屏 被点击：终端会话标识=" + viewModel.Title
            + " 结果=成功，本地输出显示已清空，远端会话不受影响");
    }

    /// <summary>切换会话日志记录。</summary>
    private void OnLogToggleClick(object? sender, RoutedEventArgs e)
    {
        TerminalTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=记录会话日志 被点击：终端会话视图模型未就绪，结果=失败，未切换会话日志状态");
            return;
        }
        viewModel.ToggleLogging();
        this.LogPathText.Text = viewModel.LoggingEnabled ? viewModel.LogFilePath : string.Empty;
        if (viewModel.LoggingEnabled)
        {
            UiLogger.Log("程序行为", "按钮=记录会话日志 被点击：终端会话标识=" + viewModel.Title
                + " 会话日志文件路径=" + viewModel.LogFilePath
                + " 结果=成功，已开启会话日志记录，后续输出将写入该文件");
        }
        else
        {
            UiLogger.Log("程序行为", "按钮=记录会话日志 被点击：终端会话标识=" + viewModel.Title
                + " 结果=成功，已关闭会话日志记录并把缓冲的输出写入日志文件");
        }
    }

    /// <summary>断开会话。</summary>
    private void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        TerminalTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按钮=断开 被点击：终端会话视图模型未就绪，结果=失败，未断开会话");
            return;
        }
        viewModel.Disconnect();
        UiLogger.Log("程序行为", "按钮=断开 被点击：终端会话标识=" + viewModel.Title
            + " 连接配置编号=" + viewModel.AttachedProfileId
            + " 结果=成功，远端会话已关闭，终端不再收发数据");
    }

    /// <summary>输入框回车发送。</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        TerminalTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "按键=回车（命令输入框）：终端会话视图模型未就绪，结果=失败，未发送任何内容");
        }
        else if (string.IsNullOrWhiteSpace(viewModel.InputText))
        {
            UiLogger.Log("程序行为", "按键=回车（命令输入框）：命令文本=（空白） 结果=未发送，输入区没有可发送的命令");
        }
        else
        {
            string command = viewModel.InputText;
            viewModel.SendInput();
            UiLogger.Log("程序行为", "按键=回车（命令输入框）：命令文本=" + command.Trim() + " 终端会话标识=" + viewModel.Title
                + " 结果=成功，命令已写入终端会话并清空输入区");
        }
        e.Handled = true;
    }
}
