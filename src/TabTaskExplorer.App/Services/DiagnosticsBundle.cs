using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services;

/// <summary>诊断包生成参数：设置对象、系统右键菜单服务、屏幕描述与输出目录。</summary>
public sealed class DiagnosticsBundleOptions
{
    /// <summary>应用设置，用于生成脱敏后的配置快照。</summary>
    public AppSettings? Settings { get; set; }

    /// <summary>系统右键菜单服务，用于列出已安装的 Shell 扩展；平台不支持时传空。</summary>
    public ISystemContextMenuService? SystemContextMenus { get; set; }

    /// <summary>显示器描述行（数量、分辨率、缩放比例、主屏标记），由界面层采集。</summary>
    public List<string> ScreenDescriptions { get; set; } = new List<string>();

    /// <summary>输出目录；留空使用用户数据目录下的 diagnostics 子目录。</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>进度回调，在后台线程中触发。</summary>
    public IProgress<string>? Progress { get; set; }
}

/// <summary>诊断包生成结果：成功时给出 zip 完整路径与条目数。</summary>
public sealed class DiagnosticsBundleResult
{
    /// <summary>是否生成成功。</summary>
    public bool Success { get; set; }

    /// <summary>生成的 zip 完整路径。</summary>
    public string ZipPath { get; set; } = string.Empty;

    /// <summary>失败原因（中文，可直接展示给用户）。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>已写入 zip 的条目名称清单。</summary>
    public List<string> Entries { get; } = new List<string>();
}

/// <summary>
/// 诊断包生成器：收集版本、操作系统、显示器、脱敏配置、最近日志、崩溃记录、
/// Shell 扩展与 known_hosts 指纹摘要，压缩为一个 zip 并返回保存路径。
/// 全部工作在后台线程执行，不阻塞界面。
/// </summary>
public static class DiagnosticsBundle
{
    /// <summary>最近日志最多打包的份数。</summary>
    private const int MaxLogFileCount = 3;

    /// <summary>默认输出目录：用户数据目录下的 diagnostics 子目录。</summary>
    public static string DefaultOutputDirectory
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TabTaskExplorer",
                "diagnostics");
        }
    }

    /// <summary>在后台线程生成诊断包。</summary>
    public static Task<DiagnosticsBundleResult> CreateAsync(DiagnosticsBundleOptions options)
    {
        return Task.Run(() => Create(options));
    }

    /// <summary>同步生成诊断包：收集全部内容后写入 zip，任何异常都转换为失败结果。</summary>
    private static DiagnosticsBundleResult Create(DiagnosticsBundleOptions options)
    {
        DiagnosticsBundleResult result = new DiagnosticsBundleResult();
        string directory = string.IsNullOrEmpty(options.OutputDirectory)
            ? DefaultOutputDirectory : options.OutputDirectory;
        string zipPath = Path.Combine(directory,
            "diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".zip");
        result.ZipPath = zipPath;
        try
        {
            Directory.CreateDirectory(directory);
            Report(options, "开始生成诊断包：输出目录=" + directory);
            UiLogger.LogInfo(LogCategories.Diagnostics, "生成诊断包",
                "输出目录=" + directory + " 显示器数量=" + options.ScreenDescriptions.Count);

            string systemInfo = BuildSystemInfo(options.ScreenDescriptions);
            string sanitizedSettings = BuildSanitizedSettings(options.Settings);
            string crashRecords = BuildCrashRecords();
            string shellExtensions = BuildShellExtensions(options.SystemContextMenus);
            string knownHosts = BuildKnownHostsSummary();

            Report(options, "正在写入诊断包文件");
            using (FileStream zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                AddTextEntry(archive, "系统信息.txt", systemInfo);
                result.Entries.Add("系统信息.txt");
                AddTextEntry(archive, "配置快照-脱敏.json", sanitizedSettings);
                result.Entries.Add("配置快照-脱敏.json");
                AddTextEntry(archive, "崩溃记录.txt", crashRecords);
                result.Entries.Add("崩溃记录.txt");
                AddTextEntry(archive, "Shell扩展列表.txt", shellExtensions);
                result.Entries.Add("Shell扩展列表.txt");
                AddTextEntry(archive, "known_hosts指纹摘要.txt", knownHosts);
                result.Entries.Add("known_hosts指纹摘要.txt");

                AddRecentLogs(archive, result, options);
                Report(options, "正在压缩诊断包");
            }

            long length = new FileInfo(zipPath).Length;
            result.Success = true;
            Report(options, "诊断包已生成：" + zipPath + "（" + length + " 字节）");
            UiLogger.LogInfo(LogCategories.Diagnostics, "生成诊断包",
                "结果=成功 路径=" + zipPath + " 条目数=" + result.Entries.Count + " 字节数=" + length);
            return result;
        }
        catch (Exception error)
        {
            result.Success = false;
            result.ErrorMessage = error.Message;
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
                // 删除半成品 zip 失败时保留文件，不影响返回失败结果
            }
            UiLogger.LogError(LogCategories.Diagnostics, "生成诊断包", "结果=失败 路径=" + zipPath, error);
            return result;
        }
    }

    /// <summary>上报进度；没有回调时忽略。</summary>
    private static void Report(DiagnosticsBundleOptions options, string message)
    {
        if (options.Progress != null)
        {
            options.Progress.Report(message);
        }
    }

    /// <summary>收集版本、操作系统、运行时、进程、显示器与日志目录等环境信息。</summary>
    private static string BuildSystemInfo(List<string> screenDescriptions)
    {
        StringBuilder builder = new StringBuilder();
        Assembly assembly = typeof(DiagnosticsBundle).Assembly;
        AssemblyName assemblyName = assembly.GetName();
        string version = assemblyName.Version != null ? assemblyName.Version.ToString() : "未知";
        string executablePath = Environment.ProcessPath ?? "未知";
        string fileVersion = "未知";
        string productVersion = "未知";
        string buildTime = "未知";
        if (executablePath != "未知")
        {
            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                fileVersion = versionInfo.FileVersion ?? "未知";
                productVersion = versionInfo.ProductVersion ?? "未知";
                buildTime = File.GetLastWriteTime(executablePath).ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                // 可执行文件版本信息不可读时保持未知
            }
        }

        builder.AppendLine("TabTaskExplorer 诊断包");
        builder.AppendLine("采集时间=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine();
        builder.AppendLine("== 版本 ==");
        builder.AppendLine("程序集版本=" + version);
        builder.AppendLine("文件版本=" + fileVersion);
        builder.AppendLine("产品版本=" + productVersion);
        builder.AppendLine("可执行文件生成时间=" + buildTime);
        builder.AppendLine("可执行文件=" + executablePath);
        builder.AppendLine("进程号=" + Environment.ProcessId);
        builder.AppendLine("进程启动时间=" + DescribeProcessStartTime());
        builder.AppendLine("当前工作目录=" + DescribeWorkingDirectory());
        builder.AppendLine();
        builder.AppendLine("== 操作系统与运行时 ==");
        builder.AppendLine("操作系统描述=" + RuntimeInformation.OSDescription);
        builder.AppendLine("操作系统版本=" + Environment.OSVersion.VersionString);
        builder.AppendLine("运行时=" + RuntimeInformation.FrameworkDescription);
        builder.AppendLine("操作系统架构=" + RuntimeInformation.OSArchitecture);
        builder.AppendLine("进程架构=" + RuntimeInformation.ProcessArchitecture);
        builder.AppendLine("是否64位进程=" + Environment.Is64BitProcess);
        builder.AppendLine("处理器数量=" + Environment.ProcessorCount);
        builder.AppendLine("当前区域=" + CultureInfo.CurrentCulture.Name);
        builder.AppendLine("当前时区=" + TimeZoneInfo.Local.Id);
        builder.AppendLine("日志目录=" + UiLogger.LogDirectory);
        builder.AppendLine();
        builder.AppendLine("== 显示器与 DPI ==");
        builder.AppendLine("显示器数量=" + screenDescriptions.Count);
        if (screenDescriptions.Count == 0)
        {
            builder.AppendLine("显示器信息=界面层未提供（可能窗口尚未创建）");
        }
        else
        {
            foreach (string line in screenDescriptions)
            {
                builder.AppendLine(line);
            }
        }
        return builder.ToString();
    }

    /// <summary>描述本进程启动时间；读取失败时返回未知。</summary>
    private static string DescribeProcessStartTime()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return "未知（读取进程启动时间失败）";
        }
    }

    /// <summary>描述当前工作目录；读取失败时返回未知。</summary>
    private static string DescribeWorkingDirectory()
    {
        try
        {
            return Environment.CurrentDirectory;
        }
        catch
        {
            return "未知（读取工作目录失败）";
        }
    }

    /// <summary>生成脱敏后的配置快照 JSON：敏感字段直接替换为 ***，其余文本再应用脱敏规则。</summary>
    private static string BuildSanitizedSettings(AppSettings? settings)
    {
        if (settings == null)
        {
            return "设置对象=空（程序尚未加载设置）";
        }
        try
        {
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
            };
            string json = JsonSerializer.Serialize(settings, jsonOptions);
            JsonNode? root = JsonNode.Parse(json);
            SanitizeJsonNode(root);
            return root == null ? "设置序列化结果=空" : root.ToJsonString(jsonOptions);
        }
        catch (Exception error)
        {
            return "配置快照生成失败：" + error.Message;
        }
    }

    /// <summary>递归遍历 JSON：字段名命中敏感规则时整体替换，其余字符串按文本脱敏规则处理。</summary>
    private static void SanitizeJsonNode(JsonNode? node)
    {
        JsonObject? obj = node as JsonObject;
        if (obj != null)
        {
            List<string> names = new List<string>();
            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                names.Add(pair.Key);
            }
            foreach (string name in names)
            {
                JsonNode? child = obj[name];
                JsonValue? value = child as JsonValue;
                if (value != null)
                {
                    string? text;
                    if (value.TryGetValue<string>(out text))
                    {
                        obj[name] = LogSanitizer.IsSensitivePropertyName(name)
                            ? "***" : LogSanitizer.MaskSecrets(text);
                    }
                    continue;
                }
                SanitizeJsonNode(child);
            }
            return;
        }
        JsonArray? array = node as JsonArray;
        if (array != null)
        {
            foreach (JsonNode? item in array)
            {
                SanitizeJsonNode(item);
            }
        }
    }

    /// <summary>收集最近的内存崩溃记录（未处理异常），逐行脱敏。</summary>
    private static string BuildCrashRecords()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("最近崩溃与未处理异常记录（内存缓冲，重启后清空）");
        builder.AppendLine();
        string[] records = WebServiceHost.GetDiagnostics();
        if (records.Length == 0)
        {
            builder.AppendLine("最近没有记录到未处理异常。");
            return builder.ToString();
        }
        builder.AppendLine("记录条数=" + records.Length);
        foreach (string record in records)
        {
            builder.AppendLine("----");
            builder.AppendLine(LogSanitizer.MaskSecrets(record));
        }
        return builder.ToString();
    }

    /// <summary>列出已安装的系统 Shell 扩展与右键菜单项（含启用状态、注册表路径与来源）。</summary>
    private static string BuildShellExtensions(ISystemContextMenuService? service)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("已安装 Shell 扩展与右键菜单项");
        builder.AppendLine();
        if (service == null || !service.IsSupported)
        {
            builder.AppendLine("当前平台不支持 Shell 扩展枚举（非 Windows 平台）。");
            return builder.ToString();
        }
        try
        {
            IReadOnlyList<SystemContextMenuEntry> entries = service.Enumerate();
            int count = 0;
            foreach (SystemContextMenuEntry entry in entries)
            {
                if (entry == null || entry.Group == SystemMenuGroup.ShellNew)
                {
                    continue;
                }
                if (entry.SourceDescription.Length == 0 && entry.CommandTemplate.Length == 0
                    && entry.Group != SystemMenuGroup.ModernMenu)
                {
                    continue;
                }
                count = count + 1;
                builder.AppendLine("----");
                builder.AppendLine("显示名=" + entry.DisplayName);
                builder.AppendLine("分组=" + DescribeMenuGroup(entry.Group));
                builder.AppendLine("是否启用=" + entry.IsEnabled);
                builder.AppendLine("是否只读=" + entry.IsReadOnly);
                builder.AppendLine("需要管理员=" + entry.RequiresAdministrator);
                builder.AppendLine("注册表路径=" + entry.RegistryPath);
                builder.AppendLine("来源=" + (entry.SourceDescription.Length == 0 ? "无" : entry.SourceDescription));
                builder.AppendLine("命令模板=" + (entry.CommandTemplate.Length == 0 ? "无" : entry.CommandTemplate));
            }
            builder.Insert(0, "条目数量=" + count + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception error)
        {
            builder.AppendLine("枚举失败：" + error.Message);
        }
        return builder.ToString();
    }

    /// <summary>把右键菜单分组转成中文说明。</summary>
    private static string DescribeMenuGroup(SystemMenuGroup group)
    {
        switch (group)
        {
            case SystemMenuGroup.DesktopContext:
                return "桌面右键";
            case SystemMenuGroup.ShellNew:
                return "新建菜单";
            case SystemMenuGroup.ModernMenu:
                return "Windows 11 新版菜单";
            default:
                return "文件右键";
        }
    }

    /// <summary>汇总 known_hosts 中已信任主机的密钥类型与 SHA256 指纹。</summary>
    private static string BuildKnownHostsSummary()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("known_hosts 指纹摘要（主机密钥指纹按原样保留，其余敏感串不写入）");
        builder.AppendLine();
        try
        {
            KnownHostStore store = new KnownHostStore();
            IReadOnlyList<KnownHostEntry> entries = store.GetAll();
            builder.AppendLine("已信任主机记录数=" + entries.Count);
            foreach (KnownHostEntry entry in entries)
            {
                builder.AppendLine("----");
                builder.AppendLine("主机=" + entry.Host);
                builder.AppendLine("端口=" + entry.Port);
                builder.AppendLine("密钥算法=" + entry.KeyType);
                builder.AppendLine("SHA256指纹=" + entry.FingerprintSha256);
                builder.AppendLine("首次信任时间=" + entry.TrustedTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        catch (Exception error)
        {
            builder.AppendLine("读取失败：" + error.Message);
        }
        return builder.ToString();
    }

    /// <summary>写入最近日志文件：取最新若干份，逐行脱敏后写入 zip，避免诊断包泄露令牌。</summary>
    private static void AddRecentLogs(ZipArchive archive, DiagnosticsBundleResult result, DiagnosticsBundleOptions options)
    {
        List<FileInfo> files = new List<FileInfo>();
        try
        {
            string directory = UiLogger.LogDirectory;
            if (Directory.Exists(directory))
            {
                foreach (string path in Directory.GetFiles(directory, "ui-*.log"))
                {
                    try
                    {
                        files.Add(new FileInfo(path));
                    }
                    catch
                    {
                        // 单个日志文件信息读取失败时跳过
                    }
                }
            }
        }
        catch
        {
            // 日志目录不可读时不打包日志
        }
        files.Sort(CompareByWriteTimeDescending);
        for (int index = 0; index < files.Count && index < MaxLogFileCount; index++)
        {
            FileInfo file = files[index];
            string entryName = "最近日志/" + file.Name;
            Report(options, "正在写入日志：" + file.Name);
            if (AddSanitizedLogEntry(archive, entryName, file.FullName))
            {
                result.Entries.Add(entryName);
            }
        }
        string legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabTaskExplorer", "ui.log");
        if (File.Exists(legacyPath))
        {
            if (AddSanitizedLogEntry(archive, "最近日志/旧版-ui.log", legacyPath))
            {
                result.Entries.Add("最近日志/旧版-ui.log");
            }
        }
    }

    /// <summary>按写入时间从新到旧比较日志文件。</summary>
    private static int CompareByWriteTimeDescending(FileInfo left, FileInfo right)
    {
        return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
    }

    /// <summary>把日志文件逐行脱敏后写入 zip；失败时返回 false，不中断其余条目。</summary>
    private static bool AddSanitizedLogEntry(ZipArchive archive, string entryName, string sourcePath)
    {
        try
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream target = entry.Open())
            using (StreamWriter writer = new StreamWriter(target, new UTF8Encoding(false)))
            using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(source, Encoding.UTF8, true))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    writer.WriteLine(LogSanitizer.MaskSecrets(line));
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>写入一个 UTF-8 文本条目。</summary>
    private static void AddTextEntry(ZipArchive archive, string entryName, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(text);
        }
    }
}
