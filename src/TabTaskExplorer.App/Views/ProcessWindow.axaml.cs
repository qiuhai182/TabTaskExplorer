using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>进程监控窗口。</summary>
public partial class ProcessWindow : Window
{
    public ProcessWindow()
    {
        this.InitializeComponent();
        UiLogger.AttachWindow(this);
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is ProcessPanelViewModel viewModel)
        {
            viewModel.StartRefresh();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
