@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem TabTaskExplorer 构建脚本：产物输出到 output 目录
rem 用法：build.bat        框架依赖单文件（需安装 .NET 10 运行时）
rem       build.bat full   自包含单文件（无需运行时，体积较大）
rem ============================================================

rem ---------- 第 0 步：源码编码自愈（GBK 无损转 UTF-8；代码带 BOM，脚本无 BOM） ----------
powershell -NoProfile -ExecutionPolicy Bypass -Command "$utf8Bom = New-Object System.Text.UTF8Encoding($true); $utf8NoBom = New-Object System.Text.UTF8Encoding($false); $utf8Strict = New-Object System.Text.UTF8Encoding($false, $true); $gbk = [System.Text.Encoding]::GetEncoding(936); $root = '%~dp0'; $codeFiles = Get-ChildItem (Join-Path $root 'src') -Recurse -File -Include *.cs,*.axaml,*.csproj,*.props,*.md,*.json,*.editorconfig; foreach ($file in $codeFiles) { $bytes = [System.IO.File]::ReadAllBytes($file.FullName); if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $text = $utf8Strict.GetString($bytes[3..($bytes.Length-1)]) } else { try { $text = $utf8Strict.GetString($bytes) } catch { $text = $gbk.GetString($bytes) } }; while ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }; if ($text.StartsWith('?<') -or $text.StartsWith('?using')) { $text = $text.Substring(1) }; [System.IO.File]::WriteAllText($file.FullName, $text, $utf8Bom) }; $scripts = @('build.bat','upLoad.sh','start.bat','stop.bat','restart.bat') | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ }; foreach ($file in $scripts) { $bytes = [System.IO.File]::ReadAllBytes($file); if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $text = $utf8Strict.GetString($bytes[3..($bytes.Length-1)]) } else { try { $text = $utf8Strict.GetString($bytes) } catch { $text = $gbk.GetString($bytes) } }; [System.IO.File]::WriteAllText($file, $text, $utf8NoBom) }; $bad = $codeFiles | Where-Object { [System.IO.File]::ReadAllText($_.FullName, $utf8Strict).Contains([string][char]0xFFFD) }; if ($bad) { $bad | ForEach-Object { Write-Host ('ENCODING-BROKEN: ' + $_.FullName) }; exit 1 }; Write-Host ('第 0 步 编码自愈完成：' + $codeFiles.Count + ' 个代码文件已确认为 UTF-8')"
if errorlevel 1 (
    echo.
    echo 存在字符已丢失的文件（含 U+FFFD），已中止构建，请先修复上述文件
    exit /b 1
)

set CONFIG=Release
set RID=win-x64
set OUTPUT=%~dp0output
set SELF_CONTAINED=false

if /i "%~1"=="full" set SELF_CONTAINED=true

echo 正在发布 TabTaskExplorer 至 %OUTPUT% ...

dotnet publish src\TabTaskExplorer.App\TabTaskExplorer.App.csproj -c %CONFIG% -r %RID% --self-contained %SELF_CONTAINED% -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none -o "%OUTPUT%"

if errorlevel 1 (
    echo.
    echo 构建失败，请检查上方错误信息
    exit /b 1
)

rem 清理原生库附带的调试符号文件，保证 output 目录只有单个 exe
del "%OUTPUT%\*.pdb" 2>nul

echo.
echo 构建完成：%OUTPUT%\TabTaskExplorer.exe
endlocal
