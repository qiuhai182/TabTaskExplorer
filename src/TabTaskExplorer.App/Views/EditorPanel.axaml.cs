using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
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
        EditorTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            UiLogger.Log("程序行为", "在线编辑器点击保存按钮被拒绝：未绑定编辑器数据 结果=失败");
            return;
        }
        bool dirty = viewModel.IsDirty;
        viewModel.Save();
        UiLogger.Log("程序行为", "在线编辑器点击保存按钮：文件路径=" + viewModel.RemotePath
            + " 是否有未保存更改=" + (dirty ? "是" : "否")
            + " 结果=" + (dirty ? "成功，已发起写回远端" : "内容没有变化，未执行写回"));
    }

    /// <summary>编辑区 Ctrl+S 保存（参考 sshtools 在线编辑回传）。</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            MainWindowViewModel? mainViewModel = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
            if (mainViewModel != null && mainViewModel.Settings.ShortcutsDisabled)
            {
                // 已一键停用全部快捷键：Ctrl+S 不触发保存，交给编辑器默认处理
                return;
            }
            EditorTabViewModel? viewModel = this.ViewModel;
            if (viewModel == null)
            {
                UiLogger.Log("程序行为", "在线编辑器按下保存组合键被拒绝：未绑定编辑器数据 结果=失败");
            }
            else
            {
                bool dirty = viewModel.IsDirty;
                viewModel.Save();
                UiLogger.Log("程序行为", "在线编辑器按下保存组合键：文件路径=" + viewModel.RemotePath
                    + " 是否有未保存更改=" + (dirty ? "是" : "否")
                    + " 结果=" + (dirty ? "成功，已发起写回远端" : "内容没有变化，未执行写回"));
            }
            e.Handled = true;
        }
    }
}
