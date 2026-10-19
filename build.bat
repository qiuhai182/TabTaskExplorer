@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem TabTaskExplorer 构建脚本：发布到 output 目录
rem 用法：build.bat        框架依赖单文件（需已安装 .NET 10 运行时）
rem       build.bat full   自包含单文件（无需运行时，体积较大）
rem ============================================================

set CONFIG=Release
set RID=win-x64
set OUTPUT=%~dp0output
set SELF_CONTAINED=false

if /i "%~1"=="full" set SELF_CONTAINED=true

echo 正在发布 TabTaskExplorer 到 %OUTPUT% ...

dotnet publish src\TabTaskExplorer.App\TabTaskExplorer.App.csproj -c %CONFIG% -r %RID% --self-contained %SELF_CONTAINED% -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none -o "%OUTPUT%"

if errorlevel 1 (
    echo.
    echo 构建失败，请检查上方错误信息
    exit /b 1
)

rem 清理原生库自带的调试符号文件，保证 output 内只有单个 exe
del "%OUTPUT%\*.pdb" 2>nul

echo.
echo 构建完成：%OUTPUT%\TabTaskExplorer.exe
endlocal
