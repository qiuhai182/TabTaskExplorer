@echo off
chcp 65001 >nul
setlocal

rem Always run from the folder containing this script, no matter where it is invoked from.
cd /d "%~dp0"

rem ============================================================
rem TabTaskExplorer build script
rem Usage: build.bat         framework-dependent single file
rem        build.bat full    self-contained single file
rem Exit codes: 0 = success, 1 = failed (which step is reported at the end)
rem ============================================================

set CONFIG=Release
set RID=win-x64
set OUTPUT=%~dp0output
set PROJECT=%~dp0src\TabTaskExplorer.App\TabTaskExplorer.App.csproj
set SELF_CONTAINED=false
set BUILD_LOG=%TEMP%\TabTaskExplorer-build.log
set FAILED_STAGE=
set FAIL_REASON=

if /i "%~1"=="full" set SELF_CONTAINED=true

echo ============================================================
echo TabTaskExplorer 构建开始
echo ============================================================

rem ---------- Step 0: normalize source encoding to UTF-8 BOM ----------
rem 单个文件被占用或不可读时只记录警告并跳过，最后汇总跳过数量，不影响构建结果；
rem 只有真正影响编译的 U+FFFD 乱码才视为失败。
echo [1/3] 源码编码归一化 ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$utf8Bom = New-Object System.Text.UTF8Encoding($true); $utf8NoBom = New-Object System.Text.UTF8Encoding($false); $utf8Strict = New-Object System.Text.UTF8Encoding($false, $true); $gbk = [System.Text.Encoding]::GetEncoding(936); $root = '%~dp0'; $codeFiles = Get-ChildItem (Join-Path $root 'src') -Recurse -File -Include *.cs,*.axaml,*.csproj,*.props,*.md,*.json,*.editorconfig; $done = 0; $skippedCount = 0; $skippedText = ''; $brokenCount = 0; $brokenText = ''; foreach ($file in $codeFiles) { try { $bytes = [System.IO.File]::ReadAllBytes($file.FullName); if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $text = $utf8Strict.GetString($bytes[3..($bytes.Length-1)]) } else { try { $text = $utf8Strict.GetString($bytes) } catch { $text = $gbk.GetString($bytes) } }; while ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }; if ($text.StartsWith('?<') -or $text.StartsWith('?using')) { $text = $text.Substring(1) }; [System.IO.File]::WriteAllText($file.FullName, $text, $utf8Bom); $done = $done + 1 } catch { $skippedCount = $skippedCount + 1; $skippedText = $skippedText + 'WARNING: 编码归一化已跳过被占用或不可读文件：' + $file.FullName + [Environment]::NewLine } }; $scripts = 'build.bat,upLoad.sh,start.bat,stop.bat,restart.bat'.Split(',') | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ }; foreach ($file in $scripts) { try { $bytes = [System.IO.File]::ReadAllBytes($file); if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $text = $utf8Strict.GetString($bytes[3..($bytes.Length-1)]) } else { try { $text = $utf8Strict.GetString($bytes) } catch { $text = $gbk.GetString($bytes) } }; [System.IO.File]::WriteAllText($file, $text, $utf8NoBom) } catch { $skippedCount = $skippedCount + 1; $skippedText = $skippedText + 'WARNING: 编码归一化已跳过被占用或不可读文件：' + $file + [Environment]::NewLine } }; foreach ($file in $codeFiles) { try { $txt = [System.IO.File]::ReadAllText($file.FullName, $utf8Strict); if ($txt.Contains([string][char]0xFFFD)) { $brokenCount = $brokenCount + 1; $brokenText = $brokenText + 'ENCODING-BROKEN: ' + $file.FullName + [Environment]::NewLine } } catch { } }; if ($skippedCount -gt 0) { Write-Host $skippedText; Write-Host ('WARNING: 编码归一化跳过 ' + $skippedCount + ' 个文件（被占用或不可读），不影响构建') }; if ($brokenCount -gt 0) { Write-Host $brokenText; Write-Host ('源码编码损坏：' + $brokenCount + ' 个文件包含 U+FFFD，构建中止'); exit 1 }; Write-Host ('Step 0 编码归一化完成：已处理 ' + $done + ' 个文件，跳过 ' + $skippedCount + ' 个'); exit 0"
if errorlevel 1 (
    set "FAILED_STAGE=归一化"
    set "FAIL_REASON=源码文件包含 U+FFFD 乱码字符，需先修复后再构建"
    goto FAIL
)

rem ---------- Preflight: make sure the app is not running and the output exe is not locked ----------
echo [2/3] 检查输出目标是否被占用 ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$target = '%OUTPUT%\TabTaskExplorer.exe'; $procs = @(Get-Process -Name TabTaskExplorer -ErrorAction SilentlyContinue); if ($procs.Count -gt 0) { Write-Host ('ERROR: TabTaskExplorer.exe 正在运行（进程号=' + (($procs | ForEach-Object { $_.Id }) -join ',') + '），请先退出（或运行 stop.bat）后再构建。'); exit 2 }; if (-not (Test-Path $target)) { exit 0 }; try { $stream = [System.IO.File]::Open($target, 'Open', 'ReadWrite', 'None'); $stream.Close(); exit 0 } catch { Write-Host ('ERROR: TabTaskExplorer.exe 正在运行，请先退出。输出文件被占用：' + $target); exit 1 }"
if errorlevel 2 (
    set "FAILED_STAGE=预检"
    set "FAIL_REASON=TabTaskExplorer.exe 正在运行，请先退出（可运行 stop.bat）"
    goto FAIL
)
if errorlevel 1 (
    set "FAILED_STAGE=预检"
    set "FAIL_REASON=TabTaskExplorer.exe 正在运行，请先退出。输出文件被占用：%OUTPUT%\TabTaskExplorer.exe"
    goto FAIL
)

rem ---------- Step 3: publish ----------
echo [3/3] 发布中：%OUTPUT% ...
echo 构建日志：%BUILD_LOG%
dotnet publish "%PROJECT%" -c %CONFIG% -r %RID% --self-contained %SELF_CONTAINED% -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none -o "%OUTPUT%" > "%BUILD_LOG%" 2>&1
set PUBLISH_RESULT=%errorlevel%
type "%BUILD_LOG%"

if not "%PUBLISH_RESULT%"=="0" goto PUBLISH_FAILED

rem Remove native debug symbols so the output folder only contains the single exe
del "%OUTPUT%\*.pdb" 2>nul

echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$f = Get-Item -LiteralPath '%OUTPUT%\TabTaskExplorer.exe'; Write-Host ('产物时间戳：' + $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + '  大小=' + $f.Length + ' 字节')"
echo BUILD OK: %OUTPUT%\TabTaskExplorer.exe
echo 构建结果：成功（退出码 0）
echo 完整构建日志：%BUILD_LOG%
echo ============================================================
pause
endlocal
exit /b 0

:PUBLISH_FAILED
findstr /C:"error NU" "%BUILD_LOG%" >nul
if not errorlevel 1 goto FAILED_RESTORE
findstr /C:": error CS" "%BUILD_LOG%" >nul
if not errorlevel 1 goto FAILED_COMPILE
findstr /C:"error AVLN" "%BUILD_LOG%" >nul
if not errorlevel 1 goto FAILED_COMPILE
findstr /C:"UnauthorizedAccessException" "%BUILD_LOG%" >nul
if not errorlevel 1 goto FAILED_LOCKED
findstr /C:"being used by another process" "%BUILD_LOG%" >nul
if not errorlevel 1 goto FAILED_LOCKED
findstr /C:"error MSB" "%BUILD_LOG%" >nul
if not errorlevel 1 goto FAILED_MSBUILD
set "FAILED_STAGE=发布"
set "FAIL_REASON=发布阶段失败，原因见上方日志"
goto FAIL_DETAIL

:FAILED_RESTORE
set "FAILED_STAGE=还原"
set "FAIL_REASON=NuGet 包还原失败，请检查包源与网络"
goto FAIL_DETAIL

:FAILED_COMPILE
set "FAILED_STAGE=编译"
set "FAIL_REASON=C#/XAML 编译错误，请按上方日志修正后重新构建"
goto FAIL_DETAIL

:FAILED_LOCKED
set "FAILED_STAGE=发布"
set "FAIL_REASON=TabTaskExplorer.exe 正在运行，请先退出。输出文件被占用"
goto FAIL_DETAIL

:FAILED_MSBUILD
set "FAILED_STAGE=发布"
set "FAIL_REASON=MSBuild 任务执行失败，原因见上方日志"
goto FAIL_DETAIL

:FAIL_DETAIL
echo ------------------ 错误详情 ------------------
findstr /R /C:": error " /C:"error NU" /C:"error MSB" /C:"error AVLN" "%BUILD_LOG%"
goto FAIL

:FAIL
echo.
echo ================== 构建失败 ==================
echo 失败步骤：%FAILED_STAGE%
echo 失败原因：%FAIL_REASON%
echo 完整构建日志：%BUILD_LOG%
echo ==============================================
echo 构建结果：失败（退出码 1）
pause
endlocal
exit /b 1
