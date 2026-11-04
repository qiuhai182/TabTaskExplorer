using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TabTaskExplorer.App.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>日志轮转集合：与其它测试串行执行，避免全局测试注入点互相干扰。</summary>
[CollectionDefinition("UiLoggerRotation", DisableParallelization = true)]
public class UiLoggerRotationCollection
{
}

/// <summary>
/// 日志轮转端到端测试：通过注入时间源、日志目录与单文件上限，
/// 验证超限裁剪、整行不劈开以及跨天新建文件并保留旧文件。
/// </summary>
[Collection("UiLoggerRotation")]
public class UiLoggerRotationTests
{
    [Fact]
    public void Log_ShouldTrimOldestAndKeepWholeLinesWhenExceedingLimit()
    {
        string directory = CreateTempDirectory();
        try
        {
            UiLogger.ResetTestOverrides();
            UiLogger.SetMinimumLevel(LogLevel.Info);
            UiLogger.LogDirectoryOverride = directory;
            // 测试用 1 兆阈值替代生产默认的 32 兆，只改测试覆盖值，不改生产常量
            UiLogger.MaxFileBytesOverride = 1024L * 1024;
            UiLogger.NowProvider = delegate { return new DateTime(2026, 11, 4, 10, 0, 0); };

            UiLogger.LogInfo(LogCategories.Performance, "裁剪测试", "旧内容标记=OLD-MARKER");
            for (int index = 0; index < 12000; index++)
            {
                UiLogger.LogInfo(LogCategories.Performance, "裁剪测试",
                    "序号=" + index + " 填充=" + new string('A', 100));
            }
            UiLogger.LogInfo(LogCategories.Performance, "裁剪测试", "最新内容标记=NEW-MARKER");

            string path = Path.Combine(directory, "ui-2026-11-04.log");
            Assert.True(File.Exists(path), "日志文件未生成：" + path);
            long length = new FileInfo(path).Length;
            Assert.True(length <= 1024L * 1024, "裁剪后文件仍超过 1 兆，实际字节=" + length);

            string text = File.ReadAllText(path, Encoding.UTF8);
            Assert.DoesNotContain("OLD-MARKER", text);
            Assert.Contains("NEW-MARKER", text);

            string[] lines = text.Split('\n');
            int checkedLines = 0;
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length == 0)
                {
                    continue;
                }
                checkedLines = checkedLines + 1;
                Assert.StartsWith("2026-11-04 10:00:00.000 [信息]", line);
                Assert.DoesNotContain("\uFFFD", line);
            }
            Assert.True(checkedLines > 0, "裁剪后没有完整日志行");
        }
        finally
        {
            UiLogger.ResetTestOverrides();
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Log_ShouldCreateNewFileNextDayAndKeepOldFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            UiLogger.ResetTestOverrides();
            UiLogger.SetMinimumLevel(LogLevel.Info);
            UiLogger.LogDirectoryOverride = directory;
            UiLogger.NowProvider = delegate { return new DateTime(2026, 11, 4, 23, 59, 0); };
            UiLogger.LogInfo(LogCategories.System, "跨天测试", "第一日标记=DAY1-MARKER");

            string firstPath = Path.Combine(directory, "ui-2026-11-04.log");
            Assert.True(File.Exists(firstPath));

            UiLogger.NowProvider = delegate { return new DateTime(2026, 11, 5, 0, 1, 0); };
            string secondPath = Path.Combine(directory, "ui-2026-11-05.log");
            UiLogger.LogInfo(LogCategories.System, "跨天测试", "第二日标记=DAY2-MARKER");

            Assert.True(File.Exists(firstPath), "跨天后旧日志文件被误删：" + firstPath);
            Assert.True(File.Exists(secondPath), "跨天后未新建当天日志文件：" + secondPath);
            string firstText = File.ReadAllText(firstPath, Encoding.UTF8);
            string secondText = File.ReadAllText(secondPath, Encoding.UTF8);
            Assert.Contains("DAY1-MARKER", firstText);
            Assert.Contains("DAY2-MARKER", secondText);
            Assert.DoesNotContain("DAY1-MARKER", secondText);
        }
        finally
        {
            UiLogger.ResetTestOverrides();
            DeleteTempDirectory(directory);
        }
    }

    /// <summary>创建测试专用临时目录。</summary>
    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "tte-log-rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>删除测试临时目录；清理失败不影响测试结论。</summary>
    private static void DeleteTempDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch
        {
            // 临时目录清理失败时保留，交给系统临时目录清理
        }
    }
}
