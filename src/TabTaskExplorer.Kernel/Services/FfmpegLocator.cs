using System;
using System.IO;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// FFmpeg 定位器：按「显式配置 → 程序目录 → 开发仓库 tools 目录 → 系统 PATH → 常见安装目录」的顺序查找，
/// 与平台无关，供截图与录屏实现共用。
/// </summary>
public static class FfmpegLocator
{
    /// <summary>查找 FFmpeg 可执行文件；未找到返回 null。</summary>
    public static string? Find(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string configured = configuredPath.Trim();
            if (File.Exists(configured))
            {
                return Path.GetFullPath(configured);
            }
        }

        string executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string baseDirectory = AppContext.BaseDirectory;

        // 程序目录与开发仓库 tools 目录：向上逐级查找，方便直接运行源码构建产物
        DirectoryInfo? directory = new DirectoryInfo(baseDirectory);
        for (int level = 0; level < 5 && directory != null; level++)
        {
            string[] relativeCandidates = new string[]
            {
                executableName,
                Path.Combine("tools", "ffmpeg", executableName),
                Path.Combine("tools", "ffmpeg", "bin", executableName),
                Path.Combine("tools", "ffmpeg", "ffmpeg", executableName),
            };
            foreach (string relative in relativeCandidates)
            {
                string candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            directory = directory.Parent;
        }

        // 系统 PATH
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVariable))
        {
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
                    return Path.GetFullPath(candidate);
                }
            }
        }

        // 常见安装位置（Linux 软件包与 Homebrew / MacPorts）
        if (!OperatingSystem.IsWindows())
        {
            string[] commonPaths = new string[]
            {
                "/usr/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
                "/opt/homebrew/bin/ffmpeg",
                "/opt/local/bin/ffmpeg",
            };
            foreach (string candidate in commonPaths)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
