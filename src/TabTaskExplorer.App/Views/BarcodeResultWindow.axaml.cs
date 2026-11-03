using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Capture;

namespace TabTaskExplorer.App.Views;

/// <summary>二维码 / 条码识别结果窗口：展示码制与内容，支持复制到剪贴板。</summary>
public partial class BarcodeResultWindow : Window
{
    private readonly BarcodeScanResult result;

    public BarcodeResultWindow(BarcodeScanResult scanResult)
    {
        this.InitializeComponent();
        // 结果窗口作为附属工具窗口，不进入任务栏与 Alt+Tab
        WindowHostStyler.MakeAuxiliary(this);
        this.result = scanResult;
        this.ResultBox.Text = scanResult.Text;
        this.FormatText.Text = "码制：" + (scanResult.Format.Length == 0 ? "未知" : scanResult.Format);
        UiLogger.Log("扫码", "扫码结果窗口已打开：码制=" + scanResult.Format
            + " 内容长度=" + scanResult.Text.Length
            + " 内容=" + scanResult.Text);
    }

    /// <summary>复制识别内容到系统剪贴板。</summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        IClipboard? clipboard = this.Clipboard;
        if (clipboard == null)
        {
            UiLogger.Log("扫码", "复制扫码内容失败：系统剪贴板不可用");
            return;
        }
        await clipboard.SetTextAsync(this.result.Text);
        UiLogger.Log("扫码", "扫码内容已复制到剪贴板：内容长度=" + this.result.Text.Length + " 结果=成功");
    }

    /// <summary>关闭结果窗口。</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
