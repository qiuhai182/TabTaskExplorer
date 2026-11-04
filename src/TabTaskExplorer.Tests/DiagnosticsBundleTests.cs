using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using TabTaskExplorer.App.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>诊断包生成测试：验证 zip 真实生成、包含必需条目、配置中的密码摘要已脱敏。</summary>
public class DiagnosticsBundleTests
{
    [Fact]
    public async System.Threading.Tasks.Task CreateAsync_ShouldProduceZipWithRequiredEntriesAndMaskedSettings()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(),
            "tte-diagnostics-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppSettings settings = new AppSettings();
            WebAccessProfile profile = WebAccessProfile.CreateDefault();
            profile.Name = "测试方案";
            profile.SetPassword("仅用于测试的密码");
            settings.WebAccessProfiles.Add(profile);

            DiagnosticsBundleOptions options = new DiagnosticsBundleOptions();
            options.Settings = settings;
            options.SystemContextMenus = null;
            options.OutputDirectory = outputDirectory;
            options.ScreenDescriptions = new List<string>
            {
                "显示器序号=1 主显示器=True 宽度=1920 高度=1080 缩放比例=1",
            };

            DiagnosticsBundleResult result = await DiagnosticsBundle.CreateAsync(options);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(result.ZipPath), "诊断包 zip 未生成：" + result.ZipPath);

            using (ZipArchive archive = ZipFile.OpenRead(result.ZipPath))
            {
                List<string> names = new List<string>();
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    names.Add(entry.FullName);
                }
                Assert.Contains("系统信息.txt", names);
                Assert.Contains("配置快照-脱敏.json", names);
                Assert.Contains("崩溃记录.txt", names);
                Assert.Contains("Shell扩展列表.txt", names);
                Assert.Contains("known_hosts指纹摘要.txt", names);

                string systemInfo = ReadEntryText(archive, "系统信息.txt");
                Assert.Contains("程序集版本=", systemInfo);
                Assert.Contains("显示器数量=1", systemInfo);
                Assert.Contains("缩放比例=1", systemInfo);

                string settingsText = ReadEntryText(archive, "配置快照-脱敏.json");
                Assert.Contains("\"PasswordHash\": \"***\"", settingsText);
                Assert.Contains("\"PasswordSalt\": \"***\"", settingsText);
                Assert.DoesNotContain(profile.PasswordHash, settingsText);
                Assert.DoesNotContain(profile.PasswordSalt, settingsText);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响测试结论
            }
        }
    }

    /// <summary>读取 zip 中一个文本条目的全部内容。</summary>
    private static string ReadEntryText(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        using (Stream stream = entry!.Open())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }
}
