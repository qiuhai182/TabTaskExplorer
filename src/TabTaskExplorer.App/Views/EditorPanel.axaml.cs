using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;

namespace TabTaskExplorer.App.Views;

/// <summary>远程文件在线编辑面板交互逻辑。</summary>
public partial class EditorPanel : UserControl
{
    /// <summary>构造编辑面板。</summary>
    public EditorPanel()
    {
        this.InitializeComponent();
    }

    /// <summary>编辑器视图模型。</summary>
    private EditorTabViewModel? ViewModel
    {
        get { return this.DataContext as EditorTabViewModel; }
    }

    /// <summary>保存按钮。</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.Save();
    }

    /// <summary>编辑区 Ctrl+S 保存（参考 sshtools 在线编辑回传）。</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            this.ViewModel?.Save();
            e.Handled = true;
        }
    }
}
