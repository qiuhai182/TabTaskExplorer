using System;
using System.IO;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 文件图标目录：把文件名或扩展名归类为图标分类，并给出每个分类的强调色。
/// 桌面端与网页端共用同一套分类键，保证两端的文件图标造型与配色完全一致。
/// </summary>
public static class FileIconCatalog
{
    /// <summary>文件夹。</summary>
    public const string Folder = "folder";

    /// <summary>磁盘分区。</summary>
    public const string Drive = "drive";

    /// <summary>未知类型的普通文件。</summary>
    public const string File = "file";

    /// <summary>源代码。</summary>
    public const string Code = "code";

    /// <summary>网页与样式表。</summary>
    public const string Web = "web";

    /// <summary>纯文本。</summary>
    public const string Text = "text";

    /// <summary>日志文件。</summary>
    public const string Log = "log";

    /// <summary>办公文档。</summary>
    public const string Document = "document";

    /// <summary>Markdown 文档。</summary>
    public const string Markdown = "markdown";

    /// <summary>PDF 与版式文档。</summary>
    public const string Pdf = "pdf";

    /// <summary>电子表格。</summary>
    public const string Spreadsheet = "spreadsheet";

    /// <summary>演示文稿。</summary>
    public const string Presentation = "presentation";

    /// <summary>图片。</summary>
    public const string Image = "image";

    /// <summary>音频。</summary>
    public const string Audio = "audio";

    /// <summary>视频。</summary>
    public const string Video = "video";

    /// <summary>压缩包。</summary>
    public const string Archive = "archive";

    /// <summary>字体文件。</summary>
    public const string Font = "font";

    /// <summary>数据库文件。</summary>
    public const string Database = "database";

    /// <summary>配置文件。</summary>
    public const string Config = "config";

    /// <summary>可执行文件与程序库。</summary>
    public const string Executable = "executable";

    /// <summary>脚本文件。</summary>
    public const string Script = "script";

    /// <summary>磁盘镜像与虚拟机磁盘。</summary>
    public const string DiskImage = "diskimage";

    /// <summary>字幕文件。</summary>
    public const string Subtitle = "subtitle";

    /// <summary>种子文件。</summary>
    public const string Torrent = "torrent";

    /// <summary>邮件文件。</summary>
    public const string Email = "email";

    /// <summary>证书与密钥。</summary>
    public const string Certificate = "certificate";

    /// <summary>设计稿与三维模型。</summary>
    public const string Design = "design";

    /// <summary>二进制与中间产物。</summary>
    public const string Binary = "binary";

    /// <summary>快捷方式。</summary>
    public const string Shortcut = "shortcut";

    /// <summary>电子书。</summary>
    public const string Ebook = "ebook";

    /// <summary>软件包。</summary>
    public const string Package = "package";

    /// <summary>按文件名取图标分类：先识别常见无扩展名文件，再按扩展名归类。</summary>
    /// <param name="fileName">文件名或完整路径。</param>
    public static string GetCategoryForName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return File;
        }
        string name = Path.GetFileName(fileName).ToLowerInvariant();
        switch (name)
        {
            case "dockerfile":
            case "containerfile":
            case "makefile":
            case "cmakelists.txt":
            case "gradlew":
            case "mvnw":
                return Code;
            case "license":
            case "licence":
            case "readme":
            case "changelog":
            case "authors":
            case "notice":
            case "copying":
                return Text;
            case ".gitignore":
            case ".gitattributes":
            case ".gitmodules":
            case ".env":
            case ".editorconfig":
            case ".npmrc":
            case ".yarnrc":
            case ".dockerignore":
            case ".htaccess":
                return Config;
            case ".bashrc":
            case ".bash_profile":
            case ".zshrc":
            case ".profile":
            case ".vimrc":
                return Script;
            default:
                return GetCategory(Path.GetExtension(name));
        }
    }

    /// <summary>按扩展名取图标分类。</summary>
    /// <param name="extension">扩展名，允许带点或不带点；空文本表示无扩展名。</param>
    public static string GetCategory(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return File;
        }
        string lower = extension.ToLowerInvariant();
        if (!lower.StartsWith(".", StringComparison.Ordinal))
        {
            lower = "." + lower;
        }
        switch (lower)
        {
            // ===== 源代码 =====
            case ".cs":
            case ".csx":
            case ".py":
            case ".pyw":
            case ".pyi":
            case ".js":
            case ".mjs":
            case ".cjs":
            case ".ts":
            case ".mts":
            case ".cts":
            case ".tsx":
            case ".jsx":
            case ".c":
            case ".h":
            case ".cpp":
            case ".cxx":
            case ".cc":
            case ".hpp":
            case ".hxx":
            case ".hh":
            case ".ipp":
            case ".inl":
            case ".java":
            case ".go":
            case ".rs":
            case ".rb":
            case ".php":
            case ".swift":
            case ".kt":
            case ".kts":
            case ".m":
            case ".mm":
            case ".scala":
            case ".sc":
            case ".lua":
            case ".pl":
            case ".pm":
            case ".r":
            case ".dart":
            case ".vue":
            case ".svelte":
            case ".asm":
            case ".s":
            case ".groovy":
            case ".clj":
            case ".cljs":
            case ".cljc":
            case ".ex":
            case ".exs":
            case ".erl":
            case ".hrl":
            case ".elm":
            case ".zig":
            case ".nim":
            case ".f":
            case ".for":
            case ".f90":
            case ".f95":
            case ".vb":
            case ".pas":
            case ".sol":
            case ".proto":
            case ".thrift":
            case ".graphql":
            case ".gql":
            case ".hbs":
            case ".mustache":
            case ".ejs":
            case ".pug":
            case ".jade":
            case ".cshtml":
            case ".razor":
                return Code;

            // ===== 网页与样式 =====
            case ".html":
            case ".htm":
            case ".xhtml":
            case ".shtml":
            case ".css":
            case ".scss":
            case ".sass":
            case ".less":
            case ".styl":
            case ".stylus":
            case ".xml":
            case ".xsl":
            case ".xslt":
            case ".xsd":
            case ".dtd":
            case ".rss":
            case ".atom":
                return Web;

            // ===== 纯文本与日志 =====
            case ".txt":
            case ".text":
            case ".nfo":
            case ".readme":
            case ".me":
            case ".1st":
                return Text;
            case ".log":
            case ".log1":
            case ".log2":
                return Log;
            case ".md":
            case ".markdown":
            case ".mdown":
            case ".mkd":
            case ".mdx":
                return Markdown;

            // ===== 文档 =====
            case ".doc":
            case ".docx":
            case ".docm":
            case ".dot":
            case ".dotx":
            case ".dotm":
            case ".odt":
            case ".ott":
            case ".pages":
            case ".wps":
            case ".wpt":
            case ".rtf":
            case ".tex":
            case ".latex":
            case ".ltx":
                return Document;
            case ".pdf":
            case ".xps":
            case ".oxps":
            case ".djvu":
            case ".djv":
                return Pdf;

            // ===== 电子表格 =====
            case ".xls":
            case ".xlsx":
            case ".xlsm":
            case ".xlsb":
            case ".xlt":
            case ".xltx":
            case ".xltm":
            case ".csv":
            case ".tsv":
            case ".ods":
            case ".ots":
            case ".numbers":
            case ".gnumeric":
                return Spreadsheet;

            // ===== 演示文稿 =====
            case ".ppt":
            case ".pptx":
            case ".pptm":
            case ".pot":
            case ".potx":
            case ".potm":
            case ".odp":
            case ".otp":
            case ".key":
            case ".sxi":
                return Presentation;

            // ===== 图片 =====
            case ".png":
            case ".apng":
            case ".jpg":
            case ".jpeg":
            case ".jpe":
            case ".jfif":
            case ".gif":
            case ".bmp":
            case ".dib":
            case ".webp":
            case ".svg":
            case ".svgz":
            case ".ico":
            case ".icns":
            case ".tif":
            case ".tiff":
            case ".tga":
            case ".psd":
            case ".psb":
            case ".ai":
            case ".eps":
            case ".cr2":
            case ".cr3":
            case ".nef":
            case ".arw":
            case ".dng":
            case ".orf":
            case ".raf":
            case ".rw2":
            case ".sr2":
            case ".heic":
            case ".heif":
            case ".avif":
            case ".exr":
            case ".hdr":
            case ".ppm":
            case ".pgm":
            case ".pbm":
            case ".pcx":
            case ".dds":
            case ".xcf":
                return Image;

            // ===== 音频 =====
            case ".mp3":
            case ".wav":
            case ".flac":
            case ".aac":
            case ".ogg":
            case ".oga":
            case ".m4a":
            case ".wma":
            case ".opus":
            case ".ape":
            case ".mid":
            case ".midi":
            case ".aiff":
            case ".aif":
            case ".alac":
            case ".amr":
            case ".au":
            case ".mka":
            case ".ra":
            case ".cda":
            case ".dsf":
            case ".dff":
                return Audio;

            // ===== 视频 =====
            case ".mp4":
            case ".m4v":
            case ".mkv":
            case ".avi":
            case ".mov":
            case ".wmv":
            case ".flv":
            case ".webm":
            case ".mpg":
            case ".mpeg":
            case ".mpe":
            case ".m2ts":
            case ".3gp":
            case ".3g2":
            case ".rm":
            case ".rmvb":
            case ".vob":
            case ".ogv":
            case ".asf":
            case ".divx":
            case ".f4v":
                return Video;

            // ===== 压缩包 =====
            case ".zip":
            case ".zipx":
            case ".rar":
            case ".7z":
            case ".tar":
            case ".gz":
            case ".gzip":
            case ".bz2":
            case ".bzip2":
            case ".xz":
            case ".lz":
            case ".lzma":
            case ".lzo":
            case ".z":
            case ".zst":
            case ".tz":
            case ".tgz":
            case ".tbz":
            case ".tbz2":
            case ".txz":
            case ".cab":
            case ".arj":
            case ".lha":
            case ".lzh":
            case ".ace":
            case ".pak":
            case ".sit":
            case ".sitx":
            case ".cpio":
            case ".shar":
            case ".br":
            case ".rz":
            case ".jar":
            case ".war":
            case ".ear":
            case ".crx":
                return Archive;

            // ===== 字体 =====
            case ".ttf":
            case ".otf":
            case ".ttc":
            case ".woff":
            case ".woff2":
            case ".eot":
            case ".fon":
            case ".fnt":
            case ".pfb":
            case ".pfm":
            case ".bdf":
            case ".pcf":
                return Font;

            // ===== 数据库 =====
            case ".db":
            case ".db3":
            case ".sqlite":
            case ".sqlite3":
            case ".sqlitedb":
            case ".mdb":
            case ".accdb":
            case ".accde":
            case ".dbf":
            case ".sql":
            case ".mdf":
            case ".ldf":
            case ".ndf":
            case ".frm":
            case ".ibd":
            case ".myd":
            case ".myi":
            case ".parquet":
            case ".orc":
            case ".avro":
            case ".realm":
            case ".kdbx":
                return Database;

            // ===== 配置 =====
            case ".json":
            case ".jsonc":
            case ".json5":
            case ".yaml":
            case ".yml":
            case ".toml":
            case ".ini":
            case ".cfg":
            case ".conf":
            case ".config":
            case ".properties":
            case ".props":
            case ".targets":
            case ".env":
            case ".editorconfig":
            case ".gitignore":
            case ".gitattributes":
            case ".gitmodules":
            case ".dockerignore":
            case ".npmrc":
            case ".yarnrc":
            case ".babelrc":
            case ".eslintrc":
            case ".prettierrc":
            case ".stylelintrc":
            case ".htaccess":
            case ".plist":
            case ".reg":
            case ".inf":
            case ".sln":
            case ".csproj":
            case ".fsproj":
            case ".vbproj":
            case ".vcxproj":
            case ".vcproj":
            case ".xcodeproj":
            case ".pbxproj":
            case ".gradle":
            case ".cmake":
            case ".mk":
            case ".lock":
            case ".sum":
            case ".mod":
                return Config;

            // ===== 可执行与程序库 =====
            case ".exe":
            case ".com":
            case ".scr":
            case ".pif":
            case ".dll":
            case ".ocx":
            case ".sys":
            case ".drv":
            case ".cpl":
            case ".so":
            case ".dylib":
            case ".bundle":
            case ".msi":
            case ".msp":
            case ".msix":
            case ".msixbundle":
            case ".appx":
            case ".appxbundle":
            case ".apk":
            case ".aar":
            case ".ipa":
            case ".app":
            case ".deb":
            case ".rpm":
            case ".pkg":
            case ".snap":
            case ".flatpak":
            case ".appimage":
            case ".swf":
            case ".class":
                return Executable;

            // ===== 脚本 =====
            case ".bat":
            case ".cmd":
            case ".btm":
            case ".ps1":
            case ".psm1":
            case ".psd1":
            case ".sh":
            case ".bash":
            case ".zsh":
            case ".ksh":
            case ".csh":
            case ".fish":
            case ".vbs":
            case ".vbe":
            case ".wsf":
            case ".wsh":
            case ".ahk":
            case ".au3":
            case ".applescript":
            case ".scpt":
            case ".command":
            case ".run":
                return Script;

            // ===== 磁盘镜像 =====
            case ".iso":
            case ".img":
            case ".ima":
            case ".dmg":
            case ".vhd":
            case ".vhdx":
            case ".vdi":
            case ".vmdk":
            case ".qcow":
            case ".qcow2":
            case ".qed":
            case ".cue":
            case ".nrg":
            case ".ccd":
            case ".toast":
            case ".udf":
            case ".wim":
            case ".swm":
            case ".sparseimage":
                return DiskImage;

            // ===== 字幕 =====
            case ".srt":
            case ".ass":
            case ".ssa":
            case ".vtt":
            case ".sub":
            case ".idx":
            case ".smi":
            case ".sami":
            case ".lrc":
            case ".ttml":
            case ".dfxp":
            case ".sbv":
            case ".mpl2":
                return Subtitle;

            // ===== 种子 =====
            case ".torrent":
            case ".magnet":
                return Torrent;

            // ===== 邮件 =====
            case ".eml":
            case ".msg":
            case ".mbox":
            case ".mbx":
            case ".pst":
            case ".ost":
            case ".olm":
            case ".nsf":
                return Email;

            // ===== 证书与密钥 =====
            case ".cer":
            case ".crt":
            case ".der":
            case ".pem":
            case ".pfx":
            case ".p12":
            case ".p7b":
            case ".p7c":
            case ".spc":
            case ".gpg":
            case ".pgp":
            case ".asc":
            case ".sig":
            case ".sign":
            case ".jks":
            case ".keystore":
                return Certificate;

            // ===== 设计稿与三维模型 =====
            case ".xd":
            case ".sketch":
            case ".fig":
            case ".fxd":
            case ".indd":
            case ".idml":
            case ".afdesign":
            case ".afphoto":
            case ".afpub":
            case ".blend":
            case ".c4d":
            case ".max":
            case ".mb":
            case ".ma":
            case ".3ds":
            case ".stl":
            case ".obj":
            case ".fbx":
            case ".dae":
            case ".gltf":
            case ".glb":
            case ".ply":
            case ".lwo":
            case ".lws":
            case ".dwg":
            case ".dxf":
            case ".dwf":
            case ".skp":
            case ".rvt":
            case ".rfa":
            case ".unity":
            case ".upk":
            case ".uasset":
                return Design;

            // ===== 二进制与中间产物 =====
            case ".bin":
            case ".dat":
            case ".o":
            case ".a":
            case ".lib":
            case ".pyc":
            case ".pyo":
            case ".pdb":
            case ".ilk":
            case ".exp":
            case ".map":
            case ".res":
            case ".resources":
            case ".raw":
            case ".dump":
            case ".dmp":
            case ".core":
            case ".hex":
            case ".srec":
            case ".elf":
            case ".ko":
            case ".wasm":
                return Binary;

            // ===== 快捷方式 =====
            case ".lnk":
            case ".url":
            case ".webloc":
            case ".desktop":
            case ".appref-ms":
            case ".sym":
                return Shortcut;

            // ===== 电子书 =====
            case ".epub":
            case ".mobi":
            case ".azw":
            case ".azw3":
            case ".kfx":
            case ".fb2":
            case ".lit":
            case ".lrf":
            case ".cbz":
            case ".cbr":
            case ".cb7":
            case ".cbt":
            case ".chm":
                return Ebook;

            // ===== 软件包 =====
            case ".whl":
            case ".egg":
            case ".nupkg":
            case ".vsix":
            case ".gem":
            case ".crate":
            case ".vpk":
            case ".xpi":
                return Package;

            default:
                return File;
        }
    }

    /// <summary>取分类的强调色（十六进制），未知分类回退到普通文件灰蓝色。</summary>
    /// <param name="category">图标分类键。</param>
    public static string GetAccent(string category)
    {
        switch (category)
        {
            case Folder:
                return "#F2B23E";
            case Drive:
                return "#4CC2FF";
            case Code:
                return "#35C4A8";
            case Web:
                return "#3BA7F0";
            case Text:
                return "#7E8A99";
            case Log:
                return "#94A3B8";
            case Document:
                return "#3D8AE0";
            case Markdown:
                return "#6C7BE0";
            case Pdf:
                return "#E5534B";
            case Spreadsheet:
                return "#34A853";
            case Presentation:
                return "#ED7B3A";
            case Image:
                return "#C586C0";
            case Audio:
                return "#E0578F";
            case Video:
                return "#7C5CFF";
            case Archive:
                return "#E8A33D";
            case Font:
                return "#B07CF0";
            case Database:
                return "#3F9BC8";
            case Config:
                return "#8E9BAA";
            case Executable:
                return "#4A9EFF";
            case Script:
                return "#2FBF9A";
            case DiskImage:
                return "#46B8E8";
            case Subtitle:
                return "#DFA94C";
            case Torrent:
                return "#4FC3A1";
            case Email:
                return "#E0B13C";
            case Certificate:
                return "#D6A21C";
            case Design:
                return "#F07178";
            case Binary:
                return "#9AA0A6";
            case Shortcut:
                return "#A8B0BC";
            case Ebook:
                return "#8E7CF0";
            case Package:
                return "#67B7E8";
            default:
                return "#8A94A6";
        }
    }
}
