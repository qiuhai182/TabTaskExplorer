using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Ocr;

namespace TabTaskExplorer.App.Views;

/// <summary>文字识别结果窗口：展示识别文字与引擎信息，支持一键复制全部文字。</summary>
public partial class OcrResultWindow : Window
{
    private readonly TextRecognitionResult result;

    public OcrResultWindow(TextRecognitionResult recognitionResult)
    {
        this.InitializeComponent();
        // 结果窗口作为附属工具窗口，不进入任务栏与 Alt+Tab
        WindowHostStyler.MakeAuxiliary(this);
        this.result = recognitionResult;
        this.ResultBox.Text = recognitionResult.Text;
        this.SummaryText.Text = "引擎：" + recognitionResult.ProviderName
            + "　语言：" + (recognitionResult.Language.Length == 0 ? "跟随系统" : recognitionResult.Language)
            + "　行数：" + recognitionResult.Lines.Count;
        UiLogger.Log("截图标注", "文字识别结果窗口已打开：引擎=" + recognitionResult.ProviderName
            + " 语言=" + recognitionResult.Language
            + " 行数=" + recognitionResult.Lines.Count
            + " 文字长度=" + recognitionResult.Text.Length);
    }

    /// <summary>复制全部识别文字到系统剪贴板。</summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        IClipboard? clipboard = this.Clipboard;
        if (clipboard == null)
        {
            UiLogger.Log("截图标注", "复制识别文字失败：系统剪贴板不可用");
            return;
        }
        try
        {
            await clipboard.SetTextAsync(this.result.Text);
            UiLogger.Log("截图标注", "识别文字已复制到剪贴板：文字长度=" + this.result.Text.Length + " 结果=成功");
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图标注", "复制识别文字失败", error);
        }
    }

    /// <summary>关闭结果窗口。</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
