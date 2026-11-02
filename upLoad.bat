@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

rem ============================================================
rem TabTaskExplorer quick upload: pull, normalize, commit, push.
rem Run from the repository root, or double-click this file.
rem Uses gitee/github remotes when configured, otherwise origin.
rem ASCII-only on purpose: cmd.exe batch parsing is unreliable
rem with non-ASCII text across different console code pages.
rem ============================================================

cd /d "%~dp0"

where git >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Git was not found. Install Git and add it to PATH.
    pause
    exit /b 1
)

rem ---------- Pull latest code ----------
set PULL_COUNT=0
for %%R in (gitee github) do (
    git remote get-url %%R >nul 2>&1
    if not errorlevel 1 (
        echo Pulling main from %%R ...
        git pull %%R main
        if errorlevel 1 (
            echo [ERROR] Pull from %%R failed. Check network, remotes, or local conflicts.
            pause
            exit /b 1
        )
        set PULL_COUNT=1
    )
)
if "!PULL_COUNT!"=="0" (
    echo No gitee/github remote found, pulling main from origin ...
    git pull origin main
    if errorlevel 1 (
        echo [ERROR] Pull from origin failed. Check network, remotes, or local conflicts.
        pause
        exit /b 1
    )
)

rem ---------- Normalize text files to UTF-8 ----------
where python >nul 2>&1
if errorlevel 1 (
    echo [WARN] Python was not found, skipping UTF-8 normalization.
) else (
    echo Normalizing text files to UTF-8 ...
    python convert_to_utf8.py
    if errorlevel 1 (
        echo [ERROR] UTF-8 normalization failed. Check Python or the converter output.
        pause
        exit /b 1
    )
)

rem ---------- Stage and commit ----------
git add --all
if errorlevel 1 (
    echo [ERROR] git add failed. Check file permissions or repository state.
    pause
    exit /b 1
)

git diff --cached --quiet
if errorlevel 1 (
    echo Committing changes ...
    git commit -m "Quick upload: latest code"
    if errorlevel 1 (
        echo [ERROR] Commit failed. Configure git user.name and user.email first.
        pause
        exit /b 1
    )
) else (
    echo No changes to commit.
)

rem ---------- Push ----------
set PUSH_COUNT=0
for %%R in (github gitee) do (
    git remote get-url %%R >nul 2>&1
    if not errorlevel 1 (
        echo Pushing main to %%R ...
        git push %%R main
        if errorlevel 1 (
            echo [ERROR] Push to %%R failed. Check network, permissions, or branch protection.
            pause
            exit /b 1
        )
        set PUSH_COUNT=1
    )
)
if "!PUSH_COUNT!"=="0" (
    echo No github/gitee remote found, pushing main to origin ...
    git push origin main
    if errorlevel 1 (
        echo [ERROR] Push to origin failed. Check network, permissions, or branch protection.
        pause
        exit /b 1
    )
)

echo Upload finished.
pause
endlocal
exit /b 0
