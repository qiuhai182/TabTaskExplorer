using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Ocr;

namespace TabTaskExplorer.Platform.Windows.Ocr;

/// <summary>
/// Windows 内置文字识别服务：调用 Windows.Media.Ocr（Windows 10 及以上自带，离线运行），
/// 通过 Windows PowerShell 5.1 的 WinRT 投影执行，不依赖任何第三方识别库或网络接口。
/// 其余平台由组合根改用 Tesseract 或空实现，接口与能力说明保持一致。
/// </summary>
public class WindowsOcrService : ITextRecognitionService
{
    private const string ProviderDisplayName = "Windows 内置文字识别";

    /// <inheritdoc />
    public bool IsAvailable
    {
        get { return OperatingSystem.IsWindows() && FindPowerShell() != null; }
    }

    /// <inheritdoc />
    public string ProviderName
    {
        get { return ProviderDisplayName; }
    }

    /// <inheritdoc />
    public string DescribeAvailability()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "文字识别=Windows 内置识别仅在 Windows 上可用";
        }
        string? powerShell = FindPowerShell();
        if (powerShell == null)
        {
            return "文字识别=未找到 Windows PowerShell 5.1，无法调用系统识别引擎";
        }
        return "文字识别=Windows 内置引擎可用（离线，使用系统已安装的语言包）";
    }

    /// <inheritdoc />
    public async Task<TextRecognitionResult> RecognizeAsync(byte[] pngBytes, string language)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return TextRecognitionResult.CreateFailure("没有可识别的图片数据", ProviderDisplayName);
        }
        string? powerShell = FindPowerShell();
        if (powerShell == null)
        {
            return TextRecognitionResult.CreateFailure(
                "未找到 Windows PowerShell 5.1，无法调用系统文字识别引擎", ProviderDisplayName);
        }
        string tempDirectory = Path.Combine(Path.GetTempPath(), "tabtaskexplorer-ocr");
        string imagePath = Path.Combine(tempDirectory, "ocr-input-" + Guid.NewGuid().ToString("N") + ".png");
        string resultPath = Path.Combine(tempDirectory, "ocr-result-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            await File.WriteAllBytesAsync(imagePath, pngBytes);
            string script = BuildScript(imagePath, resultPath, language == null ? string.Empty : language.Trim());
            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedScript;
            ProcessResult processResult = await RunProcessAsync(powerShell, arguments, 90);
            if (!processResult.Started)
            {
                return TextRecognitionResult.CreateFailure(processResult.ErrorMessage, ProviderDisplayName);
            }
            if (processResult.ExitCode != 0)
            {
                string error = processResult.StandardError.Trim();
                if (error.Length == 0)
                {
                    error = "识别进程退出代码=" + processResult.ExitCode;
                }
                return TextRecognitionResult.CreateFailure("文字识别失败：" + error, ProviderDisplayName);
            }
            if (!File.Exists(resultPath))
            {
                return TextRecognitionResult.CreateFailure("系统识别引擎没有返回结果", ProviderDisplayName);
            }
            string content = await File.ReadAllTextAsync(resultPath, Encoding.UTF8);
            List<TextRecognitionLine> lines = new List<TextRecognitionLine>();
            StringBuilder text = new StringBuilder();
            string[] rows = content.Replace("\r\n", "\n").Split('\n');
            foreach (string row in rows)
            {
                if (row.Length == 0)
                {
                    continue;
                }
                string[] fields = row.Split('\t');
                TextRecognitionLine line = new TextRecognitionLine();
                line.Text = fields[0];
                if (fields.Length >= 5)
                {
                    line.Left = ParseDouble(fields[1]);
                    line.Top = ParseDouble(fields[2]);
                    line.Width = ParseDouble(fields[3]);
                    line.Height = ParseDouble(fields[4]);
                }
                lines.Add(line);
                if (text.Length > 0)
                {
                    text.Append('\n');
                }
                text.Append(line.Text);
            }
            return TextRecognitionResult.CreateSuccess(text.ToString(), lines,
                language == null ? string.Empty : language, ProviderDisplayName);
        }
        catch (Exception error)
        {
            return TextRecognitionResult.CreateFailure("文字识别异常：" + error.Message, ProviderDisplayName);
        }
        finally
        {
            TryDelete(imagePath);
            TryDelete(resultPath);
        }
    }

    /// <summary>生成调用 Windows.Media.Ocr 的 PowerShell 脚本；行结果使用制表符分隔写出。</summary>
    private static string BuildScript(string imagePath, string resultPath, string language)
    {
        StringBuilder script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine("Add-Type -AssemblyName System.Runtime.WindowsRuntime");
        script.AppendLine("$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]");
        script.AppendLine("$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime]");
        script.AppendLine("$null = [Windows.Storage.StorageFile, Windows.Foundation, ContentType = WindowsRuntime]");
        script.AppendLine("$null = [Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime]");
        script.AppendLine("$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]");
        script.AppendLine("function Await($Operation, $ResultType) {");
        script.AppendLine("  $netTask = $asTaskGeneric.MakeGenericMethod($ResultType).Invoke($null, @($Operation))");
        script.AppendLine("  $netTask.Wait(-1) | Out-Null");
        script.AppendLine("  $netTask.Result");
        script.AppendLine("}");
        script.AppendLine("$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync('" + EscapePowerShell(imagePath) + "')) ([Windows.Storage.StorageFile])");
        script.AppendLine("$stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])");
        script.AppendLine("$decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])");
        script.AppendLine("$bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])");
        script.AppendLine("$engine = $null");
        if (language.Length > 0)
        {
            script.AppendLine("try { $language = New-Object Windows.Globalization.Language '" + EscapePowerShell(language) + "'; $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language) } catch { $engine = $null }");
        }
        script.AppendLine("if ($null -eq $engine) { $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages() }");
        script.AppendLine("if ($null -eq $engine) { throw '系统未安装可用的文字识别语言包' }");
        script.AppendLine("$result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])");
        script.AppendLine("$builder = New-Object System.Text.StringBuilder");
        script.AppendLine("foreach ($line in $result.Lines) {");
        script.AppendLine("  $left = 0.0; $top = 0.0; $right = 0.0; $bottom = 0.0; $first = $true");
        script.AppendLine("  foreach ($word in $line.Words) {");
        script.AppendLine("    $rect = $word.BoundingRect");
        script.AppendLine("    if ($first) { $left = $rect.X; $top = $rect.Y; $right = $rect.X + $rect.Width; $bottom = $rect.Y + $rect.Height; $first = $false }");
        script.AppendLine("    else { if ($rect.X -lt $left) { $left = $rect.X }; if ($rect.Y -lt $top) { $top = $rect.Y }; if (($rect.X + $rect.Width) -gt $right) { $right = $rect.X + $rect.Width }; if (($rect.Y + $rect.Height) -gt $bottom) { $bottom = $rect.Y + $rect.Height } }");
        script.AppendLine("  }");
        script.AppendLine("  $width = $right - $left; $height = $bottom - $top");
        script.AppendLine("  [void]$builder.AppendLine(($line.Text + [char]9 + $left + [char]9 + $top + [char]9 + $width + [char]9 + $height))");
        script.AppendLine("}");
        script.AppendLine("$utf8 = New-Object System.Text.UTF8Encoding($false)");
        script.AppendLine("[System.IO.File]::WriteAllText('" + EscapePowerShell(resultPath) + "', $builder.ToString(), $utf8)");
        return script.ToString();
    }

    /// <summary>转义 PowerShell 单引号字符串中的单引号。</summary>
    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>解析识别行坐标，失败时返回零。</summary>
    private static double ParseDouble(string value)
    {
        double parsed;
        if (double.TryParse(value, out parsed))
        {
            return parsed;
        }
        return 0.0;
    }

    /// <summary>查找 Windows PowerShell 5.1；优先使用系统目录下的固定路径。</summary>
    private static string? FindPowerShell()
    {
        string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
        string path = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(path))
        {
            return path;
        }
        return null;
    }

    /// <summary>运行外部识别进程并收集输出；超时后结束进程。</summary>
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
                    result.ErrorMessage = "无法启动 Windows PowerShell 识别进程";
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
            result.ErrorMessage = "启动文字识别失败：" + error.Message;
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
