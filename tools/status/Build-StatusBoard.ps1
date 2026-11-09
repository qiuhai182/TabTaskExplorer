param(
    [string]$RepoRoot = '',
    [string]$ImplementationPath = '',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}
if ([string]::IsNullOrWhiteSpace($ImplementationPath)) {
    $ImplementationPath = Join-Path $RepoRoot 'docs\modern-design\implementation'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $RepoRoot 'docs\status\BOARD.md'
}

$StatusVocabulary = @('待构建', '待实测', '已实测通过', '阻塞', '等你决策')
$StatusRank = @{
    '等你决策' = 0
    '阻塞' = 0
    '待构建' = 1
    '待实测' = 2
    '已实测通过' = 3
}
$CountOrder = @('等你决策', '阻塞', '待构建', '待实测', '已实测通过')

$TaskNameMap = @{
    'p0-1-progress.md' = 'Web 端 P0-1'
    'logging-progress.md' = '日志与诊断'
    'fileview-progress.md' = '文件视图'
    'web-parity-task-center/progress.md' = 'Web 对齐 + 统一任务中心（含复制/剪切/粘贴、卡死修复）'
    'capture-progress.md' = '截图链路'
    'ctxmenu-progress.md' = '右键菜单'
    'remote-progress.md' = '远程连接'
    'taskbar-progress.md' = '任务栏单图标'
    'theme-tokens-progress.md' = '样式令牌统一'
    'build-channel-progress.md' = '构建通道（环境）'
}

function Get-FieldValue {
    param(
        [string[]]$Lines,
        [string]$FieldName
    )
    $pattern = '^' + [regex]::Escape($FieldName) + '\s*[：:]\s*(.*)$'
    for ($i = 0; $i -lt [Math]::Min(15, $Lines.Count); $i++) {
        if ($Lines[$i] -match $pattern) {
            return $Matches[1].Trim()
        }
    }
    return ''
}

function Get-TaskName {
    param(
        [string]$RelativePath,
        [string[]]$Lines
    )
    $key = $RelativePath -replace '\\', '/'
    if ($TaskNameMap.ContainsKey($key)) {
        return $TaskNameMap[$key]
    }
    foreach ($line in $Lines) {
        if ($line -match '^#\s+(.+?)\s*$') {
            $title = $Matches[1].Trim()
            $title = $title -replace '（[^）]*）', ''
            $title = $title -replace '\([^)]*\)', ''
            $title = $title -replace '\s*进度\s*$', ''
            $title = $title.Trim()
            if (-not [string]::IsNullOrWhiteSpace($title)) {
                return $title
            }
            break
        }
    }
    return [System.IO.Path]::GetFileNameWithoutExtension($RelativePath)
}

function Get-ShortReason {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }
    $cut = $Text.IndexOfAny([char[]]@('，', '；', '。', ';'))
    if ($cut -ge 0) {
        $Text = $Text.Substring(0, $cut)
    }
    $Text = $Text.Trim()
    if ($Text.Length -gt 40) {
        $Text = $Text.Substring(0, 40) + '…'
    }
    return $Text
}

function Format-Cell {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return '（未填写）'
    }
    $value = $Text -replace '\|', '｜'
    $value = $value -replace "`r", ' '
    $value = $value -replace "`n", ' '
    return $value.Trim()
}

function Get-RelativeLink {
    param(
        [string]$FromDirectory,
        [string]$ToPath
    )
    $fromFull = [System.IO.Path]::GetFullPath($FromDirectory).TrimEnd([char[]]@('\', '/'))
    $toFull = [System.IO.Path]::GetFullPath($ToPath)
    $fromParts = @($fromFull -split '\\')
    $toParts = @($toFull -split '\\')
    $common = 0
    $maxCommon = [Math]::Min($fromParts.Count, $toParts.Count - 1)
    while ($common -lt $maxCommon -and $fromParts[$common] -eq $toParts[$common]) {
        $common = $common + 1
    }
    if ($common -eq 0) {
        return ($toFull -replace '\\', '/')
    }
    $parts = New-Object System.Collections.Generic.List[string]
    for ($i = $common; $i -lt $fromParts.Count; $i++) {
        $parts.Add('..')
    }
    for ($i = $common; $i -lt $toParts.Count; $i++) {
        $parts.Add($toParts[$i])
    }
    return ($parts -join '/')
}

function Get-DocLink {
    param([string]$RelativePath)
    $targetPath = Join-Path $ImplementationPath $RelativePath
    return Get-RelativeLink -FromDirectory (Split-Path -Parent $OutputPath) -ToPath $targetPath
}

if (-not (Test-Path -LiteralPath $ImplementationPath)) {
    throw "找不到实现进度目录：$ImplementationPath"
}

$files = @(Get-ChildItem -LiteralPath $ImplementationPath -Recurse -File -Filter '*progress.md' | Sort-Object FullName)
if ($files.Count -eq 0) {
    throw "在 $ImplementationPath 下没有找到 *progress.md"
}

$entries = New-Object System.Collections.Generic.List[object]
$warnings = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName, [System.Text.Encoding]::UTF8)
    $relativePath = $file.FullName.Substring($ImplementationPath.Length)
    $relativePath = $relativePath.TrimStart([char[]]@('\', '/'))

    $status = Get-FieldValue -Lines $lines -FieldName '状态'
    if ($StatusVocabulary -notcontains $status) {
        $warnings.Add($relativePath + ' 的状态头缺失或非法：' + $status)
        $status = '阻塞'
        $blocker = '状态头缺失或非法：需在第一行标题后补「状态：待构建|待实测|已实测通过|阻塞|等你决策」'
        $next = '补全状态头后重新运行脚本'
    }
    else {
        $blocker = Get-FieldValue -Lines $lines -FieldName '卡点'
        $next = Get-FieldValue -Lines $lines -FieldName '下一步'
    }

    if ([string]::IsNullOrWhiteSpace($blocker)) {
        $blocker = '（卡点未填写）'
    }
    if ([string]::IsNullOrWhiteSpace($next)) {
        $next = '（下一步未填写）'
    }

    $entry = [PSCustomObject]@{
        Name = (Get-TaskName -RelativePath $relativePath -Lines $lines)
        Status = $status
        Blocker = $blocker
        Next = $next
        RelativePath = $relativePath
    }
    $entries.Add($entry)
}

$oldStatusByName = @{}
$oldLogLines = New-Object System.Collections.Generic.List[string]

if (Test-Path -LiteralPath $OutputPath) {
    $oldLines = [System.IO.File]::ReadAllLines($OutputPath, [System.Text.Encoding]::UTF8)
    $inLog = $false
    foreach ($line in $oldLines) {
        if ($line -match '^##\s+变更日志') {
            $inLog = $true
            continue
        }
        if ($inLog) {
            if ($line -match '^##\s+') {
                break
            }
            if ($line -match '^-\s+(.+)$') {
                $oldLogLines.Add($Matches[1].Trim())
            }
            elseif ($line -match '^\d{4}-\d{2}-\d{2}\s+.+$') {
                $oldLogLines.Add($line.Trim())
            }
            continue
        }
        if ($line -match '^\|') {
            $cells = @($line.Trim().Trim([char]'|').Split('|') | ForEach-Object { $_.Trim() })
            if ($cells.Count -ge 5 -and $StatusVocabulary -contains $cells[1]) {
                $oldStatusByName[$cells[0]] = $cells[1]
            }
        }
    }
}

$sortedEntries = @($entries | Sort-Object @{ Expression = { $StatusRank[$_.Status] } }, @{ Expression = { $_.Name } })
$decisionEntries = @($sortedEntries | Where-Object { $_.Status -eq '等你决策' })
$blockedEntries = @($sortedEntries | Where-Object { $_.Status -eq '阻塞' })

$counts = @{}
foreach ($status in $StatusVocabulary) {
    $counts[$status] = 0
}
foreach ($entry in $entries) {
    $counts[$entry.Status] = $counts[$entry.Status] + 1
}

$now = Get-Date -Format 'yyyy-MM-dd HH:mm'
$builder = New-Object System.Text.StringBuilder

[void]$builder.AppendLine('<!-- 本文件由脚本生成，不要手改。 -->')
[void]$builder.AppendLine('# 任务状态看板')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('生成时间：' + $now)
[void]$builder.AppendLine('生成命令：powershell -NoProfile -ExecutionPolicy Bypass -File tools\status\Build-StatusBoard.ps1')
[void]$builder.AppendLine('数据来源：docs\modern-design\implementation 下的 *progress.md')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('> 本文件由脚本生成，不要手改。状态词表只有五个：待构建 / 待实测 / 已实测通过 / 阻塞 / 等你决策。')
[void]$builder.AppendLine('')

[void]$builder.AppendLine('## 等你决策')
[void]$builder.AppendLine('')
if ($decisionEntries.Count -eq 0) {
    [void]$builder.AppendLine('- 暂无')
}
else {
    foreach ($entry in $decisionEntries) {
        [void]$builder.AppendLine('- **' + $entry.Name + '**：' + (Format-Cell $entry.Blocker) + ' [文档](' + (Get-DocLink $entry.RelativePath) + ')')
    }
}
[void]$builder.AppendLine('')

[void]$builder.AppendLine('## 阻塞')
[void]$builder.AppendLine('')
if ($blockedEntries.Count -eq 0) {
    [void]$builder.AppendLine('- 暂无')
}
else {
    foreach ($entry in $blockedEntries) {
        [void]$builder.AppendLine('- **' + $entry.Name + '**：' + (Format-Cell $entry.Blocker) + ' [文档](' + (Get-DocLink $entry.RelativePath) + ')')
    }
}
[void]$builder.AppendLine('')

$summaryParts = New-Object System.Collections.Generic.List[string]
foreach ($status in $CountOrder) {
    $summaryParts.Add($status + ' ' + $counts[$status])
}
[void]$builder.AppendLine('## 任务总览')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('统计：' + ($summaryParts -join ' / ') + '（共 ' + $entries.Count + ' 条）')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('| 任务线 | 状态 | 卡点 | 下一步 | 文档链接 |')
[void]$builder.AppendLine('| --- | --- | --- | --- | --- |')
foreach ($entry in $sortedEntries) {
    $fileName = [System.IO.Path]::GetFileName($entry.RelativePath)
    $docCell = '[' + $fileName + '](' + (Get-DocLink $entry.RelativePath) + ')'
    $row = '| ' + (Format-Cell $entry.Name) + ' | ' + $entry.Status + ' | ' + (Format-Cell $entry.Blocker) + ' | ' + (Format-Cell $entry.Next) + ' | ' + $docCell + ' |'
    [void]$builder.AppendLine($row)
}
[void]$builder.AppendLine('')

$newLogLines = New-Object System.Collections.Generic.List[string]
if ($oldStatusByName.Count -eq 0) {
    $newLogLines.Add($now + ' 初始化：共 ' + $entries.Count + ' 条（' + ($summaryParts -join ' / ') + '）')
}
else {
    foreach ($entry in $sortedEntries) {
        if ($oldStatusByName.ContainsKey($entry.Name)) {
            $oldStatus = $oldStatusByName[$entry.Name]
            if ($oldStatus -ne $entry.Status) {
                $reason = Get-ShortReason $entry.Blocker
                $suffix = ''
                if (-not [string]::IsNullOrWhiteSpace($reason)) {
                    $suffix = '（' + $reason + '）'
                }
                $newLogLines.Add($now + ' ' + $entry.Name + '：' + $oldStatus + ' → ' + $entry.Status + $suffix)
            }
        }
        else {
            $newLogLines.Add($now + ' ' + $entry.Name + '：新增 → ' + $entry.Status)
        }
    }
}

$combinedLog = New-Object System.Collections.Generic.List[string]
foreach ($line in $newLogLines) {
    $combinedLog.Add($line)
}
foreach ($line in $oldLogLines) {
    $combinedLog.Add($line)
}
$logLimit = 50
if ($combinedLog.Count -gt $logLimit) {
    $trimmed = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $logLimit; $i++) {
        $trimmed.Add($combinedLog[$i])
    }
    $combinedLog = $trimmed
}

[void]$builder.AppendLine('## 变更日志')
[void]$builder.AppendLine('')
if ($combinedLog.Count -eq 0) {
    [void]$builder.AppendLine('- 暂无')
}
else {
    foreach ($line in $combinedLog) {
        [void]$builder.AppendLine('- ' + $line)
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    [void][System.IO.Directory]::CreateDirectory($outputDirectory)
}
$utf8NoBom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), $utf8NoBom)

Write-Output ('已生成：' + $OutputPath)
Write-Output ('任务线：' + $entries.Count + ' 条')
foreach ($warning in $warnings) {
    Write-Warning $warning
}
