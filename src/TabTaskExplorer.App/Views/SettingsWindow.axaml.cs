using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>设置窗口：模仿 TabX Explorer 设置中心的分页布局。</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        this.InitializeComponent();
    }

    /// <summary>绑定共享设置对象：改动即时生效，点确定保存到磁盘。</summary>
    public SettingsWindow(AppSettings settings) : this()
    {
        this.DataContext = settings;
    }

    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = this.SectionList.SelectedIndex;
        this.BasicPage.IsVisible = index == 0;
        this.MousePage.IsVisible = index == 1;
        this.FilePage.IsVisible = index == 2;
        this.AboutPage.IsVisible = index == 3;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        this.Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        this.Close(false);
    }
}
