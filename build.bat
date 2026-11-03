@echo off
chcp 65001 >nul
setlocal

rem Always run from the folder containing this script, no matter where it is invoked from.
cd /d "%~dp0"

rem ============================================================
rem TabTaskExplorer build script
rem Usage: build.bat         framework-dependent single file
rem        build.bat full    self-contained single file
rem ============================================================

rem ---------- Step 0: normalize source encoding to UTF-8 BOM ----------
powershell -NoProfile -ExecutionPolicy Bypass -Command "$utf8Bom = New-Object System.Text.UTF8Encoding($true); $utf8NoBom = New-Object System.Text.UTF8Encoding($false); $utf8Strict = New-Object System.Text.UTF8Encoding($false, $true); $gbk = [System.Text.Encoding]::GetEncoding(936); $root = '%~dp0'; $codeFiles = Get-ChildItem (Join-Path $root 'src') -Recurse -File -Include *.cs,*.axaml,*.csproj,*.props,*.md,*.json,*.editorconfig; foreach ($file in $codeFiles) { $bytes = [System.IO.File]::ReadAllBytes($file.FullName); if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $text = $utf8Strict.GetString($bytes[3..($bytes.Length-1)]) } else { try { $text = $utf8Strict.GetString($bytes) } catch { $text = $gbk.GetString($bytes) } }; while ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }; if ($text.StartsWith('?<') -or $text.StartsWith('?using')) { $text = $text.Substring(1) }; [System.IO.File]::WriteAllText($file.FullName, $text, $utf8Bom) }; $scripts = @('build.bat','upLoad.sh','start.bat','stop.bat','restart.bat') | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ }; foreach ($file in $scripts) { $bytes = [System.IO.File]::ReadAllBytes($file); if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $text = $utf8Strict.GetString($bytes[3..($bytes.Length-1)]) } else { try { $text = $utf8Strict.GetString($bytes) } catch { $text = $gbk.GetString($bytes) } }; [System.IO.File]::WriteAllText($file, $text, $utf8NoBom) }; $bad = $codeFiles | Where-Object { [System.IO.File]::ReadAllText($_.FullName, $utf8Strict).Contains([string][char]0xFFFD) }; if ($bad) { $bad | ForEach-Object { Write-Host ('ENCODING-BROKEN: ' + $_.FullName) }; exit 1 }; Write-Host ('Step 0 encoding normalization completed: ' + $codeFiles.Count + ' files confirmed as UTF-8')"
if errorlevel 1 (
    echo.
    echo ERROR: One or more source files lost characters, U+FFFD found. Build aborted.
    echo Fix the listed files and run build.bat again.
    pause
    exit /b 1
)

set CONFIG=Release
set RID=win-x64
set OUTPUT=%~dp0output
set PROJECT=%~dp0src\TabTaskExplorer.App\TabTaskExplorer.App.csproj
set SELF_CONTAINED=false

if /i "%~1"=="full" set SELF_CONTAINED=true

set BUILD_LOG=%TEMP%\TabTaskExplorer-build.log

rem ---------- Preflight: make sure the output exe is not locked by a running instance ----------
powershell -NoProfile -ExecutionPolicy Bypass -Command "$target = '%OUTPUT%\TabTaskExplorer.exe'; if (-not (Test-Path $target)) { exit 0 }; try { $stream = [System.IO.File]::Open($target, 'Open', 'ReadWrite', 'None'); $stream.Close(); exit 0 } catch { exit 1 }"
if errorlevel 1 (
    echo.
    echo ================== BUILD FAILED ==================
    echo REASON: Output file is locked: %OUTPUT%\TabTaskExplorer.exe
    echo ACTION: Run stop.bat to close the tray instance, then run build.bat again.
    echo ==================================================
    pause
    exit /b 1
)

echo Publishing TabTaskExplorer to %OUTPUT% ...
echo Build log: %BUILD_LOG%

dotnet publish "%PROJECT%" -c %CONFIG% -r %RID% --self-contained %SELF_CONTAINED% -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none -o "%OUTPUT%" > "%BUILD_LOG%" 2>&1
set PUBLISH_RESULT=%errorlevel%
type "%BUILD_LOG%"

if not "%PUBLISH_RESULT%"=="0" goto BUILD_FAILED

rem Remove native debug symbols so the output folder only contains the single exe
del "%OUTPUT%\*.pdb" 2>nul

echo.
echo BUILD OK: %OUTPUT%\TabTaskExplorer.exe
echo Full build log: %BUILD_LOG%
pause
endlocal
exit /b 0

:BUILD_FAILED
echo.
echo ================== BUILD FAILED ==================
findstr /C:"UnauthorizedAccessException" "%BUILD_LOG%" >nul && (
    echo REASON: Output file is locked by a running instance.
    echo ACTION: Run stop.bat first, then run build.bat again.
    goto BUILD_FAILED_DETAIL
)
findstr /C:"error NU" "%BUILD_LOG%" >nul && (
    echo REASON: NuGet restore failed. Check package sources and network access.
    goto BUILD_FAILED_DETAIL
)
findstr /C:": error CS" "%BUILD_LOG%" >nul && (
    echo REASON: C# compile errors. Fix the errors listed below and run build.bat again.
    goto BUILD_FAILED_DETAIL
)
findstr /C:"error MSB" "%BUILD_LOG%" >nul && (
    echo REASON: MSBuild task error. See details below.
    goto BUILD_FAILED_DETAIL
)
echo REASON: Unknown build failure. See details and full log below.

:BUILD_FAILED_DETAIL
echo ------------------ ERROR DETAILS ------------------
findstr /R /C:": error " /C:"error NU" /C:"error MSB" "%BUILD_LOG%"
echo ==================================================
echo Full build log: %BUILD_LOG%
pause
endlocal
exit /b 1
