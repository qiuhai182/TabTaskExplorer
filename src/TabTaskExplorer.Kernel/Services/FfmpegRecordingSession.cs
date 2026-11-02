using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// FFmpeg 录屏会话：负责启动编码进程、收集错误输出、停止时等待文件正常收尾。
/// 三平台共用同一套进程管理逻辑，平台层只负责拼接采集参数。
/// </summary>
public class FfmpegRecordingSession
{
    private readonly object syncRoot = new object();
    private readonly StringBuilder errorTail = new StringBuilder();
    private Process? process;
    private string outputPath = string.Empty;

    /// <summary>编码进程是否仍在运行。</summary>
    public bool IsRunning
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.process != null && !this.process.HasExited;
            }
        }
    }

    /// <summary>当前输出文件完整路径；未录制时为空文本。</summary>
    public string OutputPath
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.outputPath;
            }
        }
    }

    /// <summary>最近捕获的 FFmpeg 错误输出尾部（用于失败提示，最多保留两千字符）。</summary>
    public string LastErrorText
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.errorTail.ToString();
            }
        }
    }

    /// <summary>启动编码进程；返回成功时进程仍在运行。</summary>
    public async Task<ScreenRecordingResult> StartAsync(string executablePath, IReadOnlyList<string> arguments,
        string outputFilePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return ScreenRecordingResult.CreateFailure(outputFilePath, "找不到 FFmpeg 可执行文件，请在设置中心填写 FFmpeg 完整路径");
        }
        lock (this.syncRoot)
        {
            if (this.process != null && !this.process.HasExited)
            {
                return ScreenRecordingResult.CreateFailure(outputFilePath, "已有录制正在进行，不能重复开始");
            }
            if (this.process != null)
            {
                // 上一次编码进程已自行退出：释放残留进程对象后再开始新录制
                try
                {
                    this.process.Dispose();
                }
                catch
                {
                    // 释放失败不影响本次开始
                }
                this.process = null;
            }
            this.errorTail.Clear();
            this.outputPath = outputFilePath;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = executablePath;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        for (int index = 0; index < arguments.Count; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        Process process = new Process();
        process.StartInfo = startInfo;
        process.ErrorDataReceived += this.OnErrorDataReceived;
        process.OutputDataReceived += this.OnOutputDataReceived;
        try
        {
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }
        catch (Exception error)
        {
            process.Dispose();
            return ScreenRecordingResult.CreateFailure(outputFilePath, "启动 FFmpeg 失败：" + error.Message);
        }
        lock (this.syncRoot)
        {
            this.process = process;
        }

        // 给编码器留出初始化时间：参数错误或设备不可用时进程会立即退出
        await Task.Delay(800);
        bool exited = false;
        string errorText = string.Empty;
        lock (this.syncRoot)
        {
            exited = this.process == null || this.process.HasExited;
            errorText = this.errorTail.ToString();
        }
        if (exited)
        {
            this.ReleaseProcess();
            string detail = string.IsNullOrEmpty(errorText) ? "没有可用的错误输出" : errorText;
            return ScreenRecordingResult.CreateFailure(outputFilePath, "FFmpeg 启动后立即退出：" + detail);
        }
        return ScreenRecordingResult.CreateSuccess(outputFilePath);
    }

    /// <summary>请求 FFmpeg 正常结束（向标准输入写入 q），最多等待八秒，超时后强制结束进程。</summary>
    public async Task<ScreenRecordingResult> StopAsync()
    {
        Process? currentProcess;
        string targetPath;
        lock (this.syncRoot)
        {
            currentProcess = this.process;
            targetPath = this.outputPath;
        }
        if (currentProcess == null)
        {
            return ScreenRecordingResult.CreateFailure(targetPath, "当前没有正在进行的录制");
        }

        try
        {
            if (!currentProcess.HasExited)
            {
                try
                {
                    currentProcess.StandardInput.WriteLine("q");
                    currentProcess.StandardInput.Flush();
                }
                catch
                {
                    // 标准输入已关闭时直接进入等待与强制结束流程
                }
            }
            bool exited = await Task.Run(delegate
            {
                return currentProcess.WaitForExit(8000);
            });
            if (!exited)
            {
                try
                {
                    currentProcess.Kill(true);
                }
                catch
                {
                    // 进程可能刚好自行退出，忽略强制结束失败
                }
                await Task.Run(delegate
                {
                    return currentProcess.WaitForExit(3000);
                });
            }
        }
        catch (Exception error)
        {
            string stopErrorText = this.LastErrorText;
            this.ReleaseProcess();
            return ScreenRecordingResult.CreateFailure(targetPath, "停止 FFmpeg 失败：" + error.Message
                + (string.IsNullOrEmpty(stopErrorText) ? string.Empty : " 错误输出=" + stopErrorText));
        }

        string tail = this.LastErrorText;
        this.ReleaseProcess();
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
        {
            return ScreenRecordingResult.CreateFailure(targetPath, "录制文件未生成，FFmpeg 错误输出=" + tail);
        }
        long fileLength = 0;
        try
        {
            FileInfo fileInfo = new FileInfo(targetPath);
            fileLength = fileInfo.Length;
        }
        catch (Exception error)
        {
            return ScreenRecordingResult.CreateFailure(targetPath, "读取录制文件信息失败：" + error.Message);
        }
        if (fileLength <= 0)
        {
            return ScreenRecordingResult.CreateFailure(targetPath, "录制文件为空，FFmpeg 错误输出=" + tail);
        }
        return ScreenRecordingResult.CreateSuccess(targetPath);
    }

    /// <summary>强制结束仍在运行的编码进程并释放资源（用于程序退出兜底）。</summary>
    public void Dispose()
    {
        Process? currentProcess;
        lock (this.syncRoot)
        {
            currentProcess = this.process;
            this.process = null;
        }
        if (currentProcess == null)
        {
            return;
        }
        try
        {
            if (!currentProcess.HasExited)
            {
                currentProcess.StandardInput.WriteLine("q");
                currentProcess.StandardInput.Flush();
                currentProcess.WaitForExit(3000);
            }
            if (!currentProcess.HasExited)
            {
                currentProcess.Kill(true);
            }
        }
        catch
        {
            // 退出阶段忽略所有异常
        }
        finally
        {
            currentProcess.Dispose();
        }
    }

    private void OnErrorDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data == null)
        {
            return;
        }
        lock (this.syncRoot)
        {
            this.errorTail.AppendLine(e.Data);
            if (this.errorTail.Length > 2000)
            {
                this.errorTail.Remove(0, this.errorTail.Length - 2000);
            }
        }
    }

    private void OnOutputDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data == null)
        {
            return;
        }
        lock (this.syncRoot)
        {
            this.errorTail.AppendLine(e.Data);
            if (this.errorTail.Length > 2000)
            {
                this.errorTail.Remove(0, this.errorTail.Length - 2000);
            }
        }
    }

    private void ReleaseProcess()
    {
        Process? currentProcess;
        lock (this.syncRoot)
        {
            currentProcess = this.process;
            this.process = null;
        }
        if (currentProcess == null)
        {
            return;
        }
        try
        {
            currentProcess.Dispose();
        }
        catch
        {
            // 释放失败不影响结果
        }
    }
}
