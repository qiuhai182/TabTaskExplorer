using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TabTaskExplorer.Kernel.Ocr;

/// <summary>文字识别结果中的一行：文字内容与在原图中的像素包围盒。</summary>
public class TextRecognitionLine
{
    /// <summary>该行文字内容。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>该行左上角横坐标（原图像素）。</summary>
    public double Left { get; set; }

    /// <summary>该行左上角纵坐标（原图像素）。</summary>
    public double Top { get; set; }

    /// <summary>该行宽度（原图像素）。</summary>
    public double Width { get; set; }

    /// <summary>该行高度（原图像素）。</summary>
    public double Height { get; set; }
}

/// <summary>一次文字识别结果：成功时携带识别出的文字与行信息。</summary>
public class TextRecognitionResult
{
    /// <summary>是否成功完成识别（成功但未识别到文字时 Text 为空）。</summary>
    public bool Success { get; set; }

    /// <summary>识别出的完整文字，行与行之间使用换行分隔。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>逐行识别结果。</summary>
    public List<TextRecognitionLine> Lines { get; set; } = new List<TextRecognitionLine>();

    /// <summary>本次识别使用的语言标识。</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>实际使用的识别引擎名称。</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>失败原因（中文，可直接展示给用户）。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>构造成功结果。</summary>
    public static TextRecognitionResult CreateSuccess(string text, List<TextRecognitionLine> lines,
        string language, string providerName)
    {
        TextRecognitionResult result = new TextRecognitionResult();
        result.Success = true;
        result.Text = text;
        result.Lines = lines;
        result.Language = language;
        result.ProviderName = providerName;
        return result;
    }

    /// <summary>构造失败结果。</summary>
    public static TextRecognitionResult CreateFailure(string errorMessage, string providerName)
    {
        TextRecognitionResult result = new TextRecognitionResult();
        result.Success = false;
        result.ErrorMessage = errorMessage;
        result.ProviderName = providerName;
        return result;
    }
}

/// <summary>
/// 文字识别服务：内核统一契约，Windows 使用系统内置识别引擎，Linux / macOS 使用可选的 Tesseract；
/// 引擎不可用时通过 <see cref="IsAvailable"/> 与 <see cref="DescribeAvailability"/> 明确告知界面。
/// </summary>
public interface ITextRecognitionService
{
    /// <summary>当前平台当前配置下是否具备文字识别能力。</summary>
    bool IsAvailable { get; }

    /// <summary>识别引擎显示名称（用于界面与日志）。</summary>
    string ProviderName { get; }

    /// <summary>能力与依赖说明（中文，可直接展示给用户）。</summary>
    string DescribeAvailability();

    /// <summary>识别 PNG 图片中的文字；失败时返回中文原因，不抛出异常。</summary>
    Task<TextRecognitionResult> RecognizeAsync(byte[] pngBytes, string language);
}

/// <summary>引擎不可用时的空实现：界面可安全调用，结果明确提示当前平台缺少识别引擎。</summary>
public class NullTextRecognitionService : ITextRecognitionService
{
    /// <inheritdoc />
    public bool IsAvailable
    {
        get { return false; }
    }

    /// <inheritdoc />
    public string ProviderName
    {
        get { return "未配置文字识别引擎"; }
    }

    /// <inheritdoc />
    public string DescribeAvailability()
    {
        return "当前平台未配置文字识别引擎";
    }

    /// <inheritdoc />
    public Task<TextRecognitionResult> RecognizeAsync(byte[] pngBytes, string language)
    {
        return Task.FromResult(TextRecognitionResult.CreateFailure(
            "当前平台未配置文字识别引擎", this.ProviderName));
    }
}

/// <summary>
/// Tesseract 文字识别服务（跨平台）：用户在设置中指定路径，或自动在常见安装目录 / 系统路径中查找。
/// 不随程序打包任何识别二进制，找不到时明确告知用户安装或配置路径。
/// </summary>
public class TesseractTextRecognitionService : ITextRecognitionService
{
    private readonly string configuredPath;

    private readonly string language;

    /// <summary>构造服务。</summary>
    /// <param name="configuredPath">用户配置的 tesseract 可执行文件路径；留空表示自动查找。</param>
    /// <param name="language">识别语言（例如 eng、chi_sim、eng+chi_sim）。</param>
    public TesseractTextRecognitionService(string configuredPath, string language)
    {
        this.configuredPath = configuredPath == null ? string.Empty : configuredPath;
        this.language = string.IsNullOrWhiteSpace(language) ? "eng+chi_sim" : language;
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get { return this.FindExecutable() != null; }
    }

    /// <inheritdoc />
    public string ProviderName
    {
        get { return "Tesseract"; }
    }

    /// <inheritdoc />
    public string DescribeAvailability()
    {
        string? executable = this.FindExecutable();
        if (executable != null)
        {
            return "文字识别=Tesseract 可用，路径=" + executable;
        }
        return "文字识别=未找到 Tesseract，可安装 Tesseract-OCR 或在设置中指定可执行文件路径";
    }

    /// <inheritdoc />
    public async Task<TextRecognitionResult> RecognizeAsync(byte[] pngBytes, string language)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return TextRecognitionResult.CreateFailure("没有可识别的图片数据", this.ProviderName);
        }
        string? executable = this.FindExecutable();
        if (executable == null)
        {
            return TextRecognitionResult.CreateFailure(
                "未找到 Tesseract 可执行文件，请安装 Tesseract-OCR 或在设置中指定路径", this.ProviderName);
        }
        string effectiveLanguage = string.IsNullOrWhiteSpace(language) ? this.language : language;
        string tempDirectory = Path.Combine(Path.GetTempPath(), "tabtaskexplorer-ocr");
        string imagePath = Path.Combine(tempDirectory, "ocr-input-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            await File.WriteAllBytesAsync(imagePath, pngBytes);
            string arguments = "\"" + imagePath + "\" stdout -l " + effectiveLanguage
                + " --psm 3 -c preserve_interword_spaces=1";
            ProcessResult processResult = await RunProcessAsync(executable, arguments, 90);
            if (!processResult.Started)
            {
                return TextRecognitionResult.CreateFailure(processResult.ErrorMessage, this.ProviderName);
            }
            if (processResult.ExitCode != 0)
            {
                string error = processResult.StandardError.Trim();
                if (error.Length == 0)
                {
                    error = "Tesseract 退出代码=" + processResult.ExitCode;
                }
                return TextRecognitionResult.CreateFailure("文字识别失败：" + error, this.ProviderName);
            }
            string text = processResult.StandardOutput.Replace("\r\n", "\n").TrimEnd();
            List<TextRecognitionLine> lines = SplitLines(text);
            return TextRecognitionResult.CreateSuccess(text, lines, effectiveLanguage, this.ProviderName);
        }
        catch (Exception error)
        {
            return TextRecognitionResult.CreateFailure("文字识别异常：" + error.Message, this.ProviderName);
        }
        finally
        {
            TryDelete(imagePath);
        }
    }

    /// <summary>把纯文本按行拆分为识别行（Tesseract 不提供包围盒时仅保留文字）。</summary>
    private static List<TextRecognitionLine> SplitLines(string text)
    {
        List<TextRecognitionLine> lines = new List<TextRecognitionLine>();
        string[] parts = text.Split('\n');
        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }
            TextRecognitionLine line = new TextRecognitionLine();
            line.Text = part;
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>按「用户配置 → 程序目录 tools → 系统路径 → 常见安装目录」的顺序查找 Tesseract。</summary>
    private string? FindExecutable()
    {
        if (!string.IsNullOrWhiteSpace(this.configuredPath) && File.Exists(this.configuredPath))
        {
            return this.configuredPath;
        }
        string executableName = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        string programDirectory = AppContext.BaseDirectory;
        string toolsPath = Path.Combine(programDirectory, "tools", "tesseract", executableName);
        if (File.Exists(toolsPath))
        {
            return toolsPath;
        }
        List<string> candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            candidates.Add("C:\\Program Files\\Tesseract-OCR\\tesseract.exe");
            candidates.Add("C:\\Program Files (x86)\\Tesseract-OCR\\tesseract.exe");
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/usr/local/bin/tesseract");
            candidates.Add("/opt/homebrew/bin/tesseract");
        }
        else
        {
            candidates.Add("/usr/bin/tesseract");
            candidates.Add("/usr/local/bin/tesseract");
        }
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return executableName;
    }

    /// <summary>运行外部进程并收集输出；超时后结束进程。</summary>
    private static async Task<ProcessResult> RunProcessAsync(string executable, string arguments, int timeoutSeconds)
    {
        ProcessResult result = new ProcessResult();
        try
        {
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.FileName = executable;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
            using (System.Diagnostics.Process process = new System.Diagnostics.Process())
            {
                process.StartInfo = startInfo;
                if (!process.Start())
                {
                    result.ErrorMessage = "无法启动 Tesseract 进程";
                    return result;
                }
                result.Started = true;
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                Task waitTask = process.WaitForExitAsync();
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    Task completed = await Task.WhenAny(waitTask, Task.Delay(Timeout.Infinite, timeout.Token));
                    if (completed != waitTask)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                            // 进程可能已经退出，结束失败不影响错误提示
                        }
                        result.ErrorMessage = "文字识别超时（超过 " + timeoutSeconds + " 秒）";
                        return result;
                    }
                }
                result.ExitCode = process.ExitCode;
                result.StandardOutput = await outputTask;
                result.StandardError = await errorTask;
                return result;
            }
        }
        catch (Exception error)
        {
            result.ErrorMessage = "启动 Tesseract 失败：" + error.Message;
            return result;
        }
    }

    /// <summary>删除临时文件，失败不影响结果。</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件清理失败不向上传播
        }
    }

    /// <summary>外部进程运行结果。</summary>
    private sealed class ProcessResult
    {
        /// <summary>进程是否成功启动。</summary>
        public bool Started;

        /// <summary>进程退出代码。</summary>
        public int ExitCode;

        /// <summary>标准输出。</summary>
        public string StandardOutput = string.Empty;

        /// <summary>标准错误。</summary>
        public string StandardError = string.Empty;

        /// <summary>启动或超时错误。</summary>
        public string ErrorMessage = string.Empty;
    }
}
