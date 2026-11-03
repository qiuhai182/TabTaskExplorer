using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 网页端静态资源：从嵌入资源读取入口页面、样式表与按面板拆分的脚本模块。
/// 编译期由 csproj 以固定逻辑名嵌入；若嵌入资源缺失（例如开发环境直接运行旧产物），
/// 调用方回落到 <see cref="WebPageHtml.Content"/> 的内联页面，保证功能可回退。
/// </summary>
public static class WebStaticAssets
{
    /// <summary>静态资源 URL 前缀。</summary>
    public const string AssetPrefix = "/assets/web/";

    private const string IndexResource = "web.index.html";
    private const string StyleResource = "web.app.css";
    private const string CoreResource = "web.core.js";
    private const string ViewsResource = "web.views.js";
    private const string PanelsResource = "web.panels.js";
    private const string ActionsResource = "web.actions.js";
    private const string IdeResource = "web.ide.js";
    private const string BootResource = "web.boot.js";

    private static readonly Dictionary<string, string> contentTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app.css"] = "text/css; charset=utf-8",
        ["core.js"] = "text/javascript; charset=utf-8",
        ["views.js"] = "text/javascript; charset=utf-8",
        ["panels.js"] = "text/javascript; charset=utf-8",
        ["actions.js"] = "text/javascript; charset=utf-8",
        ["ide.js"] = "text/javascript; charset=utf-8",
        ["boot.js"] = "text/javascript; charset=utf-8",
    };

    private static readonly object gate = new object();
    private static bool loaded;
    private static bool available;
    private static string indexHtml = string.Empty;
    private static string styleSheet = string.Empty;
    private static string coreScript = string.Empty;
    private static string viewsScript = string.Empty;
    private static string panelsScript = string.Empty;
    private static string actionsScript = string.Empty;
    private static string ideScript = string.Empty;
    private static string bootScript = string.Empty;
    private static string version = "legacy";

    /// <summary>资源内容版本：由全部资源内容的 SHA-256 前 10 位组成，用于 URL 查询串与缓存控制。</summary>
    public static string Version
    {
        get
        {
            WebStaticAssets.EnsureLoaded();
            return WebStaticAssets.version;
        }
    }

    /// <summary>静态资源是否完整可用；缺任意一个文件时返回 false，由调用方回落内联页面。</summary>
    public static bool IsAvailable
    {
        get
        {
            WebStaticAssets.EnsureLoaded();
            return WebStaticAssets.available;
        }
    }

    /// <summary>构建带版本号的外部资源入口页面；端口占位符在返回前替换。</summary>
    public static bool TryBuildPage(int port, out string html)
    {
        WebStaticAssets.EnsureLoaded();
        html = string.Empty;
        if (!WebStaticAssets.available)
        {
            return false;
        }
        html = WebStaticAssets.indexHtml
            .Replace("__VERSION__", WebStaticAssets.version)
            .Replace("__PORT__", port.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>读取指定静态资源；文件名只允许已知白名单，避免路径穿越与任意资源读取。</summary>
    public static bool TryReadAsset(string fileName, int port, out string contentType, out byte[] content)
    {
        WebStaticAssets.EnsureLoaded();
        contentType = string.Empty;
        content = Array.Empty<byte>();
        string text;
        if (string.Equals(fileName, "app.css", StringComparison.Ordinal))
        {
            text = WebStaticAssets.styleSheet;
        }
        else if (string.Equals(fileName, "core.js", StringComparison.Ordinal))
        {
            text = WebStaticAssets.coreScript;
        }
        else if (string.Equals(fileName, "views.js", StringComparison.Ordinal))
        {
            text = WebStaticAssets.viewsScript;
        }
        else if (string.Equals(fileName, "panels.js", StringComparison.Ordinal))
        {
            text = WebStaticAssets.panelsScript;
        }
        else if (string.Equals(fileName, "actions.js", StringComparison.Ordinal))
        {
            text = WebStaticAssets.actionsScript;
        }
        else if (string.Equals(fileName, "ide.js", StringComparison.Ordinal))
        {
            text = WebStaticAssets.ideScript;
        }
        else if (string.Equals(fileName, "boot.js", StringComparison.Ordinal))
        {
            text = WebStaticAssets.bootScript;
        }
        else
        {
            return false;
        }
        if (text.Length == 0)
        {
            return false;
        }
        contentType = WebStaticAssets.contentTypes[fileName];
        text = text
            .Replace("__VERSION__", WebStaticAssets.version)
            .Replace("__PORT__", port.ToString(CultureInfo.InvariantCulture));
        content = Encoding.UTF8.GetBytes(text);
        return true;
    }

    private static void EnsureLoaded()
    {
        lock (WebStaticAssets.gate)
        {
            if (WebStaticAssets.loaded)
            {
                return;
            }
            WebStaticAssets.loaded = true;
            WebStaticAssets.indexHtml = WebStaticAssets.LoadResource(WebStaticAssets.IndexResource) ?? string.Empty;
            WebStaticAssets.styleSheet = WebStaticAssets.LoadResource(WebStaticAssets.StyleResource) ?? string.Empty;
            WebStaticAssets.coreScript = WebStaticAssets.LoadResource(WebStaticAssets.CoreResource) ?? string.Empty;
            WebStaticAssets.viewsScript = WebStaticAssets.LoadResource(WebStaticAssets.ViewsResource) ?? string.Empty;
            WebStaticAssets.panelsScript = WebStaticAssets.LoadResource(WebStaticAssets.PanelsResource) ?? string.Empty;
            WebStaticAssets.actionsScript = WebStaticAssets.LoadResource(WebStaticAssets.ActionsResource) ?? string.Empty;
            WebStaticAssets.ideScript = WebStaticAssets.LoadResource(WebStaticAssets.IdeResource) ?? string.Empty;
            WebStaticAssets.bootScript = WebStaticAssets.LoadResource(WebStaticAssets.BootResource) ?? string.Empty;
            WebStaticAssets.available = WebStaticAssets.indexHtml.Length > 0
                && WebStaticAssets.styleSheet.Length > 0
                && WebStaticAssets.coreScript.Length > 0
                && WebStaticAssets.viewsScript.Length > 0
                && WebStaticAssets.panelsScript.Length > 0
                && WebStaticAssets.actionsScript.Length > 0
                && WebStaticAssets.ideScript.Length > 0
                && WebStaticAssets.bootScript.Length > 0;
            if (!WebStaticAssets.available)
            {
                UiLogger.Log("网页端静态资源", "回退内联页面：嵌入资源不完整 index=" + WebStaticAssets.indexHtml.Length
                    + " 样式=" + WebStaticAssets.styleSheet.Length + " 核心=" + WebStaticAssets.coreScript.Length
                    + " 视图=" + WebStaticAssets.viewsScript.Length + " 面板=" + WebStaticAssets.panelsScript.Length
                    + " 交互=" + WebStaticAssets.actionsScript.Length + " IDE=" + WebStaticAssets.ideScript.Length
                    + " 启动=" + WebStaticAssets.bootScript.Length);
                return;
            }
            string combined = WebStaticAssets.indexHtml + "\n" + WebStaticAssets.styleSheet + "\n"
                + WebStaticAssets.coreScript + "\n" + WebStaticAssets.viewsScript + "\n"
                + WebStaticAssets.panelsScript + "\n" + WebStaticAssets.actionsScript + "\n"
                + WebStaticAssets.ideScript + "\n" + WebStaticAssets.bootScript;
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            WebStaticAssets.version = Convert.ToHexString(digest).Substring(0, 10).ToLowerInvariant();
            UiLogger.Log("网页端静态资源", "加载完成 版本=" + WebStaticAssets.version
                + " 样式字节=" + WebStaticAssets.styleSheet.Length
                + " 核心脚本字节=" + WebStaticAssets.coreScript.Length
                + " 视图脚本字节=" + WebStaticAssets.viewsScript.Length
                + " 面板脚本字节=" + WebStaticAssets.panelsScript.Length
                + " 交互脚本字节=" + WebStaticAssets.actionsScript.Length
                + " IDE 脚本字节=" + WebStaticAssets.ideScript.Length
                + " 启动脚本字节=" + WebStaticAssets.bootScript.Length);
        }
    }

    /// <summary>
    /// 读取嵌入资源；未找到时尝试程序目录下的 Assets/web 目录，便于开发期直接替换文件调试。
    /// 资源名使用 csproj 中固定的 LogicalName，避免依赖工程根命名空间。
    /// </summary>
    private static string? LoadResource(string resourceName)
    {
        try
        {
            Assembly assembly = typeof(WebStaticAssets).Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            string diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "web",
                resourceName.Substring("web.".Length));
            if (File.Exists(diskPath))
            {
                return File.ReadAllText(diskPath, Encoding.UTF8);
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("网页端静态资源", "读取失败 资源=" + resourceName, error);
        }
        return null;
    }
}
