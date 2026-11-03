[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
& 'D:\TabTaskExplorer\analysis\faststone\scripts\Invoke-FastStoneProbe.ps1' `
    -TargetPath 'D:\TabTaskExplorer\references\FastStone Capture\FSCapture.exe' `
    -OutDir 'D:\TabTaskExplorer\analysis\faststone\evidence'
exit $LASTEXITCODE
