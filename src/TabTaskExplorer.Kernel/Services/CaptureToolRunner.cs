using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 系统截图工具运行器：Linux 的 FFmpeg / ImageMagick、macOS 的 screencapture 都通过这里执行，
/// 统一处理参数传递、超时、错误输出与临时文件清理，避免各平台重复实现进程管理细节。
/// </summary>
public static class CaptureToolRunner
{
    /// <summary>运行外部截图工具并把输出文件读成 PNG 字节。</summary>
    public static async Task<ScreenCaptureResult> RunAsync(string executablePath, IReadOnlyList<string> arguments,
        string outputFilePath, string failurePrefix)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return ScreenCaptureResult.CreateFailure(failurePrefix + "：找不到可执行文件 " + executablePath);
        }
        TryDeleteFile(outputFilePath);

        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = executablePath;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardOutput = true;
        for (int index = 0; index < arguments.Count; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        Process process = new Process();
        process.StartInfo = startInfo;
        string errorText = string.Empty;
        string outputText = string.Empty;
        bool exited = false;
        try
        {
            process.Start();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            exited = await Task.Run(delegate
            {
                return process.WaitForExit(15000);
            });
            if (!exited)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // 进程可能刚好自行退出
                }
            }
            errorText = await errorTask;
            outputText = await outputTask;
        }
        catch (Exception error)
        {
            process.Dispose();
            TryDeleteFile(outputFilePath);
            return ScreenCaptureResult.CreateFailure(failurePrefix + "：启动截图工具失败，" + error.Message);
        }
        int exitCode = -1;
        try
        {
            exitCode = process.ExitCode;
        }
        catch
        {
            exitCode = -1;
        }
        process.Dispose();

        if (!exited)
        {
            TryDeleteFile(outputFilePath);
            return ScreenCaptureResult.CreateFailure(failurePrefix + "：截图工具执行超时（超过十五秒）");
        }
        if (exitCode != 0)
        {
            TryDeleteFile(outputFilePath);
            string detail = errorText.Trim();
            if (detail.Length == 0)
            {
                detail = outputText.Trim();
            }
            return ScreenCaptureResult.CreateFailure(failurePrefix + "：截图工具退出码=" + exitCode
                + (detail.Length == 0 ? " 没有错误输出" : " 错误输出=" + detail));
        }
        if (!File.Exists(outputFilePath))
        {
            return ScreenCaptureResult.CreateFailure(failurePrefix + "：截图工具未生成输出文件");
        }

        byte[] imageBytes;
        try
        {
            imageBytes = File.ReadAllBytes(outputFilePath);
        }
        catch (Exception error)
        {
            TryDeleteFile(outputFilePath);
            return ScreenCaptureResult.CreateFailure(failurePrefix + "：读取截图文件失败，" + error.Message);
        }
        finally
        {
            TryDeleteFile(outputFilePath);
        }
        if (imageBytes.Length == 0)
        {
            return ScreenCaptureResult.CreateFailure(failurePrefix + "：截图文件内容为空");
        }
        return ScreenCaptureResult.CreateSuccess(imageBytes, 0, 0, CaptureArea.Empty);
    }

    /// <summary>删除临时文件；文件不存在或删除失败时静默忽略。</summary>
    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // 临时文件删除失败不影响截图结果
        }
    }

    /// <summary>在当前进程的系统路径中查找可执行文件；找不到返回空文本。</summary>
    public static string FindExecutableOnPath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return string.Empty;
        }
        if (Path.IsPathRooted(executableName) && File.Exists(executableName))
        {
            return executableName;
        }
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return string.Empty;
        }
        string[] entries = pathVariable.Split(Path.PathSeparator);
        foreach (string entry in entries)
        {
            string folder = entry.Trim();
            if (folder.Length == 0)
            {
                continue;
            }
            string candidate = Path.Combine(folder, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return string.Empty;
    }
}
