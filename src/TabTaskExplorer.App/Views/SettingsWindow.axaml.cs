using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>设置窗口：模仿 TabX Explorer 设置中心的分页布局。</summary>
public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<FileOpenRule> openRules = new ObservableCollection<FileOpenRule>();

    public SettingsWindow()
    {
        this.InitializeComponent();
        UiLogger.AttachWindow(this);
        this.OpenRuleList.ItemsSource = this.openRules;
        // 选中第一项必须在 XAML 初始化完成后进行：初始化阶段各分页容器尚未创建，此时触发分页切换会取到空引用
        this.SectionList.SelectedIndex = 0;
    }

    /// <summary>绑定共享设置对象：改动即时生效，点确定保存到磁盘。</summary>
    public SettingsWindow(AppSettings settings) : this()
    {
        this.DataContext = settings;
        this.WebServicePortBox.Text = settings.WebServicePort.ToString();
        this.EditorFontSizeBox.Text = settings.EditorFontSize.ToString();
        this.EditorTabWidthBox.Text = settings.EditorTabWidth.ToString();
        this.DefaultOpenModeBox.SelectedIndex = settings.DefaultOpenMode == "Ide" ? 1 : 0;
        this.DefaultPriorityBox.SelectedIndex = settings.FileOpenDefaultPriority == FileOpenPriority.System ? 1 : 0;
        if (settings.FileOpenRules != null)
        {
            foreach (FileOpenRule rule in settings.FileOpenRules)
            {
                if (rule != null)
                {
                    this.openRules.Add(rule);
                }
            }
        }
    }

    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = this.SectionList.SelectedIndex;
        // XAML 初始化时 ListBox 会因第一项 IsSelected="True" 触发本事件，此时各分页容器尚未创建，直接跳过
        if (this.BasicPage == null || this.MousePage == null || this.FilePage == null
            || this.EditorPage == null || this.OpenWithPage == null || this.AboutPage == null)
        {
            UiLogger.Log("设置窗口", "分页切换：初始化阶段分页容器尚未创建，跳过 index=" + index);
            return;
        }
        UiLogger.Log("设置窗口", "分页切换：index=" + index);
        this.BasicPage.IsVisible = index == 0;
        this.MousePage.IsVisible = index == 1;
        this.FilePage.IsVisible = index == 2;
        this.EditorPage.IsVisible = index == 3;
        this.OpenWithPage.IsVisible = index == 4;
        this.AboutPage.IsVisible = index == 5;
    }

    /// <summary>新增一条按类型打开规则，并选中等待填写扩展名。</summary>
    private void OnAddOpenRuleClick(object? sender, RoutedEventArgs e)
    {
        FileOpenRule rule = new FileOpenRule();
        rule.Extension = string.Empty;
        rule.Priority = FileOpenPriority.Builtin;
        this.openRules.Add(rule);
        this.OpenRuleList.SelectedItem = rule;
    }

    /// <summary>删除选中的打开方式规则。</summary>
    private void OnRemoveOpenRuleClick(object? sender, RoutedEventArgs e)
    {
        FileOpenRule? rule = this.OpenRuleList.SelectedItem as FileOpenRule;
        if (rule != null)
        {
            this.openRules.Remove(rule);
        }
    }

    /// <summary>把快捷键配置恢复为默认值。</summary>
    private void OnResetHotkeysClick(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is AppSettings settings)
        {
            settings.Hotkeys.Clear();
            foreach (HotkeySetting setting in HotkeySetting.CreateDefaults())
            {
                settings.Hotkeys.Add(setting);
            }
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        this.CommitWebServicePort();
        this.CommitEditorSettings();
        this.CommitOpenRules();
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings != null)
        {
            UiLogger.Log("配置更改", "设置窗口确定：网页端服务端口=" + settings.WebServicePort
                + " 编辑器字号=" + settings.EditorFontSize
                + " 编辑器Tab宽度=" + settings.EditorTabWidth
                + " 默认打开模式=" + settings.DefaultOpenMode
                + " 打开方式默认优先级=" + settings.FileOpenDefaultPriority
                + " 打开方式规则数=" + (settings.FileOpenRules != null ? settings.FileOpenRules.Count : 0)
                + " 快捷键条数=" + settings.Hotkeys.Count);
        }
        UiLogger.Log("设置窗口", "点击确定：提交设置并关闭");
        this.Close(true);
    }

    /// <summary>提交打开方式设置：扩展名统一规整为小写带点形式，重复类型只保留一条规则。</summary>
    private void CommitOpenRules()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        settings.FileOpenDefaultPriority = this.DefaultPriorityBox.SelectedIndex == 1 ? FileOpenPriority.System : FileOpenPriority.Builtin;

        List<FileOpenRule> committed = new List<FileOpenRule>();
        foreach (FileOpenRule rule in this.openRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Extension))
            {
                continue;
            }
            string extension = FileOpenPolicy.NormalizeExtension(rule.Extension);
            bool duplicated = false;
            foreach (FileOpenRule existing in committed)
            {
                if (existing.Extension == extension)
                {
                    duplicated = true;
                    break;
                }
            }
            if (duplicated)
            {
                continue;
            }
            FileOpenRule kept = new FileOpenRule();
            kept.Extension = extension;
            kept.Priority = rule.Priority;
            committed.Add(kept);
        }
        settings.FileOpenRules = committed;

        this.openRules.Clear();
        foreach (FileOpenRule rule in committed)
        {
            this.openRules.Add(rule);
        }
    }

    /// <summary>提交编辑器设置：字号与 Tab 宽度只接受合法范围内的数字，非法输入还原为原值。</summary>
    private void CommitEditorSettings()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        double fontSize;
        if (double.TryParse(this.EditorFontSizeBox.Text, out fontSize) && fontSize >= 8 && fontSize <= 48)
        {
            settings.EditorFontSize = fontSize;
        }
        this.EditorFontSizeBox.Text = settings.EditorFontSize.ToString();

        int tabWidth;
        if (int.TryParse(this.EditorTabWidthBox.Text, out tabWidth) && tabWidth >= 1 && tabWidth <= 16)
        {
            settings.EditorTabWidth = tabWidth;
        }
        this.EditorTabWidthBox.Text = settings.EditorTabWidth.ToString();

        settings.DefaultOpenMode = this.DefaultOpenModeBox.SelectedIndex == 1 ? "Ide" : "Explorer";
    }

    /// <summary>提交网页端服务端口：只接受 1024 至 65535，非法输入保持原值并还原显示。</summary>
    private void CommitWebServicePort()
    {
        AppSettings? settings = this.DataContext as AppSettings;
        if (settings == null)
        {
            return;
        }
        int port;
        if (int.TryParse(this.WebServicePortBox.Text, out port) && port >= 1024 && port <= 65535)
        {
            settings.WebServicePort = port;
            return;
        }
        this.WebServicePortBox.Text = settings.WebServicePort.ToString();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        UiLogger.Log("设置窗口", "点击取消：关闭设置窗口");
        this.Close(false);
    }
}
