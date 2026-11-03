<#
Screenpresso 静态取证探针（不依赖外部工具，纯 PowerShell 读取 PE）

产出：
  pe-info.md              PE 头、节表、数据目录、链接器信息、熵
  imports.txt             导入表（含延迟导入）
  exports.txt             导出表
  resources.txt           资源目录（类型/名称/语言/大小）
  manifest.xml            RT_MANIFEST 原文（若存在）
  version-info.txt        版本资源（FileVersionInfo）
  debug-directory.txt     调试目录 / PDB 路径
  clr-header.txt          是否 .NET 托管程序及元数据版本
  rich-header.txt         Rich 头标记与条目
  strings-ascii.txt       ASCII 字符串（最小 4 字符）
  strings-utf16.txt       UTF-16LE 字符串（最小 4 字符，含中文）
  keyword-hits.md         关键行为关键字命中（带文件偏移）
  static-probe-summary.json 机器可读汇总
  static-probe.log        过程日志（纯中文、完整参数）
  static-probe.exitcode   退出码

用法：
  powershell -NoProfile -ExecutionPolicy Bypass -File Invoke-StaticProbe.ps1 `
      -TargetPath 'C:\Users\QiuHai\.cache\rea-screenpresso-target\screenpresso.exe' `
      -OutDir 'D:\TabTaskExplorer\analysis\screenpresso\evidence'
#>

param(
    [string]$TargetPath = 'C:\Users\QiuHai\.cache\rea-screenpresso-target\screenpresso.exe',
    [string]$OutDir = 'D:\TabTaskExplorer\analysis\screenpresso\evidence',
    [switch]$SkipStrings
)

$ErrorActionPreference = 'Stop'
$script:LogPath = Join-Path $OutDir 'static-probe.log'
$script:ExitCodePath = Join-Path $OutDir 'static-probe.exitcode'

function Write-Log {
    param([string]$Message)
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $Message
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding utf8
}

function Get-UInt16 {
    param([byte[]]$Bytes, [int]$Offset)
    return [System.BitConverter]::ToUInt16($Bytes, $Offset)
}
function Get-UInt32 {
    param([byte[]]$Bytes, [int]$Offset)
    return [System.BitConverter]::ToUInt32($Bytes, $Offset)
}
function Get-UInt64 {
    param([byte[]]$Bytes, [int]$Offset)
    return [System.BitConverter]::ToUInt64($Bytes, $Offset)
}
function Get-HexOffset {
    param([UInt64]$Value)
    return '0x' + $Value.ToString('X')
}
function Get-ReadAsciiZ {
    param([byte[]]$Bytes, [int]$Offset, [int]$MaxLength = 256)
    if ($Offset -lt 0 -or $Offset -ge $Bytes.Length) { return '' }
    $end = $Offset
    $limit = [Math]::Min($Bytes.Length, $Offset + $MaxLength)
    while ($end -lt $limit -and $Bytes[$end] -ne 0) { $end++ }
    $length = $end - $Offset
    if ($length -le 0) { return '' }
    return [System.Text.Encoding]::ASCII.GetString($Bytes, $Offset, $length)
}

try {
    if (Test-Path -LiteralPath $script:ExitCodePath) { Remove-Item -LiteralPath $script:ExitCodePath -Force }
    if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
    Set-Content -LiteralPath $script:LogPath -Value '' -Encoding utf8

    Write-Log ('开始静态取证 目标=' + $TargetPath + ' 输出目录=' + $OutDir)
    if (-not (Test-Path -LiteralPath $TargetPath)) {
        throw ('目标文件不存在：' + $TargetPath)
    }
    $targetItem = Get-Item -LiteralPath $TargetPath
    Write-Log ('目标文件大小=' + $targetItem.Length + ' 字节 修改时间=' + $targetItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))

    $bytes = [System.IO.File]::ReadAllBytes($TargetPath)
    Write-Log ('已读取字节数=' + $bytes.Length)

    # ---------- PE 头 ----------
    if ($bytes.Length -lt 0x40) { throw '文件过短，不是有效 PE 文件' }
    $e_lfanew = [int](Get-UInt32 $bytes 0x3C)
    Write-Log ('DOS 头偏移 e_lfanew=' + (Get-HexOffset $e_lfanew))
    $peSignature = Get-UInt32 $bytes $e_lfanew
    if ($peSignature -ne 0x00004550) { throw ('PE 签名不匹配，实际值=' + (Get-HexOffset $peSignature)) }

    $coffOffset = $e_lfanew + 4
    $machine = Get-UInt16 $bytes $coffOffset
    $sectionCount = Get-UInt16 $bytes ($coffOffset + 2)
    $timeDateStamp = Get-UInt32 $bytes ($coffOffset + 4)
    $symbolTablePointer = Get-UInt32 $bytes ($coffOffset + 8)
    $symbolCount = Get-UInt32 $bytes ($coffOffset + 12)
    $optionalHeaderSize = Get-UInt16 $bytes ($coffOffset + 16)
    $characteristics = Get-UInt16 $bytes ($coffOffset + 18)

    $optionalOffset = $coffOffset + 20
    $optionalMagic = Get-UInt16 $bytes $optionalOffset
    $is64 = $false
    if ($optionalMagic -eq 0x20B) { $is64 = $true }
    elseif ($optionalMagic -eq 0x10B) { $is64 = $false }
    else { throw ('未知的可选头 Magic=' + (Get-HexOffset $optionalMagic)) }

    $machineText = '未知'
    switch ($machine) {
        0x014c { $machineText = 'x86 (I386)' }
        0x8664 { $machineText = 'x64 (AMD64)' }
        0x01c0 { $machineText = 'ARM' }
        0xaa64 { $machineText = 'ARM64' }
    }
    $subsystemText = '未知'
    $subsystem = Get-UInt16 $bytes ($optionalOffset + 68)
    switch ($subsystem) {
        2 { $subsystemText = 'GUI (Windows 图形界面)' }
        3 { $subsystemText = 'CUI (控制台)' }
    }
    $imageBase = if ($is64) { Get-UInt64 $bytes ($optionalOffset + 24) } else { [uint64](Get-UInt32 $bytes ($optionalOffset + 28)) }
    $entryPoint = Get-UInt32 $bytes ($optionalOffset + 16)
    $sizeOfImage = Get-UInt32 $bytes ($optionalOffset + 56)
    $sizeOfHeaders = Get-UInt32 $bytes ($optionalOffset + 60)
    $checksum = Get-UInt32 $bytes ($optionalOffset + 64)
    $dllCharacteristics = Get-UInt16 $bytes ($optionalOffset + 70)
    $linkerMajor = $bytes[$optionalOffset + 2]
    $linkerMinor = $bytes[$optionalOffset + 3]

    $timestampUtc = ([System.DateTimeOffset]::FromUnixTimeSeconds([long]$timeDateStamp)).UtcDateTime.ToString('yyyy-MM-dd HH:mm:ss')
    Write-Log ('PE 拆除完成 架构=' + $machineText + ' 节数=' + $sectionCount + ' 链接器=' + $linkerMajor + '.' + $linkerMinor + ' 时间戳=' + $timestampUtc)

    $sectionTableOffset = $optionalOffset + $optionalHeaderSize
    $sections = @()
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $sectionOffset = $sectionTableOffset + ($index * 40)
        $nameBytes = $bytes[$sectionOffset..($sectionOffset + 7)]
        $name = [System.Text.Encoding]::ASCII.GetString($nameBytes).Trim([char]0)
        $virtualSize = Get-UInt32 $bytes ($sectionOffset + 8)
        $virtualAddress = Get-UInt32 $bytes ($sectionOffset + 12)
        $rawSize = Get-UInt32 $bytes ($sectionOffset + 16)
        $rawPointer = Get-UInt32 $bytes ($sectionOffset + 20)
        $sectionCharacteristics = Get-UInt32 $bytes ($sectionOffset + 36)
        $entropy = 0.0
        if ($rawSize -gt 0 -and $rawPointer -lt $bytes.Length) {
            $safeSize = [Math]::Min([int]$rawSize, $bytes.Length - [int]$rawPointer)
            # PowerShell 逐字节统计 18MB 数据过慢，熵只取每节前 262144 字节作为样本，
            # 足以判断节是否被压缩/加密，同时保证脚本在几分钟内完成。
            if ($safeSize -gt 262144) { $safeSize = 262144 }
            if ($safeSize -gt 0) {
                $counts = New-Object int[] 256
                $start = [int]$rawPointer
                for ($pos = $start; $pos -lt ($start + $safeSize); $pos++) {
                    $counts[$bytes[$pos]] = $counts[$bytes[$pos]] + 1
                }
                $entropyValue = 0.0
                for ($valueIndex = 0; $valueIndex -lt 256; $valueIndex++) {
                    if ($counts[$valueIndex] -gt 0) {
                        $probability = [double]$counts[$valueIndex] / [double]$safeSize
                        $entropyValue = $entropyValue - ($probability * [Math]::Log($probability, 2))
                    }
                }
                $entropy = [Math]::Round($entropyValue, 3)
            }
        }
        $sections += [pscustomobject]@{
            名称 = $name
            虚拟大小 = $virtualSize
            虚拟地址 = ('0x' + $virtualAddress.ToString('X'))
            原始大小 = $rawSize
            原始偏移 = ('0x' + $rawPointer.ToString('X'))
            熵 = $entropy
            特征 = ('0x' + $sectionCharacteristics.ToString('X'))
        }
    }

    $directoryCountOffset = if ($is64) { $optionalOffset + 108 } else { $optionalOffset + 92 }
    $directoryCount = Get-UInt32 $bytes $directoryCountOffset
    $directoryOffset = if ($is64) { $optionalOffset + 112 } else { $optionalOffset + 96 }
    $directories = @()
    $directoryNames = @('导出表','导入表','资源表','异常表','证书表','重定位表','调试目录','架构保留','全局指针','TLS 表','加载配置','绑定导入','导入地址表','延迟导入','CLR 头','保留')
    $maxDirectories = [Math]::Min([int]$directoryCount, 16)
    for ($index = 0; $index -lt $maxDirectories; $index++) {
        $entryOffset = $directoryOffset + ($index * 8)
        $directoryRva = Get-UInt32 $bytes $entryOffset
        $directorySize = Get-UInt32 $bytes ($entryOffset + 4)
        $directoryName = '目录' + $index
        if ($index -lt $directoryNames.Count) { $directoryName = $directoryNames[$index] }
        $directories += [pscustomobject]@{
            序号 = $index
            名称 = $directoryName
            地址 = ('0x' + $directoryRva.ToString('X8'))
            大小 = $directorySize
        }
    }

    function Convert-RvaToOffset {
        param([byte[]]$Bytes, [object[]]$Sections, [uint32]$Rva, [uint32]$SizeOfHeaders)
        if ($Rva -eq 0) { return -1 }
        if ($Rva -lt $SizeOfHeaders) { return [int]$Rva }
        foreach ($section in $Sections) {
            $sectionStart = [uint32]([Convert]::ToUInt32(([string]$section.虚拟地址).Substring(2), 16))
            $sectionSpan = [uint32]([Math]::Max([double]$section.虚拟大小, [double]$section.原始大小))
            if ($Rva -ge $sectionStart -and $Rva -lt ($sectionStart + $sectionSpan)) {
                $rawPointer = [Convert]::ToUInt32(([string]$section.原始偏移).Substring(2), 16)
                if ($rawPointer -eq 0) { return -1 }
                return [int]($rawPointer + ($Rva - $sectionStart))
            }
        }
        return -1
    }

    # ---------- 导入表 ----------
    $importLines = New-Object System.Collections.Generic.List[string]
    $importModules = New-Object System.Collections.Generic.List[string]
    $importOffset = Convert-RvaToOffset $bytes $sections (Get-UInt32 $bytes ($directoryOffset + 8)) $sizeOfHeaders
    if ($importOffset -gt 0) {
        $descriptorIndex = 0
        while ($true) {
            $descriptorOffset = $importOffset + ($descriptorIndex * 20)
            $originalThunk = Get-UInt32 $bytes $descriptorOffset
            $nameRva = Get-UInt32 $bytes ($descriptorOffset + 12)
            $firstThunk = Get-UInt32 $bytes ($descriptorOffset + 16)
            if ($nameRva -eq 0 -and $firstThunk -eq 0) { break }
            $nameOffset = Convert-RvaToOffset $bytes $sections $nameRva $sizeOfHeaders
            $moduleName = Get-ReadAsciiZ $bytes $nameOffset
            $importModules.Add($moduleName)
            $importLines.Add(('[' + $moduleName + ']'))
            $thunkRva = $originalThunk
            if ($thunkRva -eq 0) { $thunkRva = $firstThunk }
            $thunkOffset = Convert-RvaToOffset $bytes $sections $thunkRva $sizeOfHeaders
            $thunkIndex = 0
            while ($thunkOffset -gt 0) {
                $entryOffset = $thunkOffset + ($thunkIndex * $(if ($is64) { 8 } else { 4 }))
                $value = if ($is64) { Get-UInt64 $bytes $entryOffset } else { [uint64](Get-UInt32 $bytes $entryOffset) }
                if ($value -eq 0) { break }
                $ordinalFlag = if ($is64) { [uint64]::Parse('8000000000000000', [System.Globalization.NumberStyles]::HexNumber) } else { [uint64]2147483648 }
                if (($value -band $ordinalFlag) -ne 0) {
                    $ordinal = $value -band 0xFFFF
                    $importLines.Add(('    #' + $ordinal))
                } else {
                    $functionNameOffset = Convert-RvaToOffset $bytes $sections ([uint32]$value) $sizeOfHeaders
                    $functionName = Get-ReadAsciiZ $bytes ($functionNameOffset + 2)
                    $importLines.Add(('    ' + $functionName))
                }
                $thunkIndex++
                if ($thunkIndex -gt 5000) { break }
            }
            $descriptorIndex++
            if ($descriptorIndex -gt 500) { break }
        }
    }

    # ---------- 延迟导入 ----------
    $delayOffset = -1
    if ($directoryCount -gt 13) {
        $delayOffset = Convert-RvaToOffset $bytes $sections (Get-UInt32 $bytes ($directoryOffset + (13 * 8))) $sizeOfHeaders
    }
    if ($delayOffset -gt 0) {
        $delayLines = New-Object System.Collections.Generic.List[string]
        $delayIndex = 0
        while ($true) {
            $descriptorOffset = $delayOffset + ($delayIndex * 32)
            $attributes = Get-UInt32 $bytes $descriptorOffset
            $delayNameRva = Get-UInt32 $bytes ($descriptorOffset + 4)
            if ($delayNameRva -eq 0) { break }
            $delayNameOffset = Convert-RvaToOffset $bytes $sections $delayNameRva $sizeOfHeaders
            $delayModule = Get-ReadAsciiZ $bytes $delayNameOffset
            $delayLines.Add(('[延迟导入 ' + $delayModule + '] 属性=' + (Get-HexOffset $attributes)))
            $delayIndex++
            if ($delayIndex -gt 200) { break }
        }
        $importLines.Add('')
        $importLines.Add('===== 延迟导入 =====')
        foreach ($line in $delayLines) { $importLines.Add($line) }
    }

    # ---------- 导出表 ----------
    $exportLines = New-Object System.Collections.Generic.List[string]
    $exportOffset = Convert-RvaToOffset $bytes $sections (Get-UInt32 $bytes ($directoryOffset)) $sizeOfHeaders
    if ($exportOffset -gt 0) {
        $exportNameRva = Get-UInt32 $bytes ($exportOffset + 12)
        $exportBase = Get-UInt32 $bytes ($exportOffset + 16)
        $functionCount = Get-UInt32 $bytes ($exportOffset + 20)
        $nameCount = Get-UInt32 $bytes ($exportOffset + 24)
        $functionTableRva = Get-UInt32 $bytes ($exportOffset + 28)
        $nameTableRva = Get-UInt32 $bytes ($exportOffset + 32)
        $ordinalTableRva = Get-UInt32 $bytes ($exportOffset + 36)
        $exportNameOffset = Convert-RvaToOffset $bytes $sections $exportNameRva $sizeOfHeaders
        $exportModuleName = Get-ReadAsciiZ $bytes $exportNameOffset
        $exportLines.Add(('导出模块=' + $exportModuleName + ' 函数数=' + $functionCount + ' 名称数=' + $nameCount + ' 基数=' + $exportBase))
        $functionTableOffset = Convert-RvaToOffset $bytes $sections $functionTableRva $sizeOfHeaders
        $nameTableOffset = Convert-RvaToOffset $bytes $sections $nameTableRva $sizeOfHeaders
        $ordinalTableOffset = Convert-RvaToOffset $bytes $sections $ordinalTableRva $sizeOfHeaders
        $nameLimit = [Math]::Min([int]$nameCount, 3000)
        for ($nameIndex = 0; $nameIndex -lt $nameLimit; $nameIndex++) {
            $namePointerRva = Get-UInt32 $bytes ($nameTableOffset + ($nameIndex * 4))
            $namePointerOffset = Convert-RvaToOffset $bytes $sections $namePointerRva $sizeOfHeaders
            $exportFunctionName = Get-ReadAsciiZ $bytes $namePointerOffset
            $ordinalIndex = Get-UInt16 $bytes ($ordinalTableOffset + ($nameIndex * 2))
            $functionRva = Get-UInt32 $bytes ($functionTableOffset + ($ordinalIndex * 4))
            $exportLines.Add(('    序号=' + ($exportBase + $ordinalIndex) + ' 地址=0x' + $functionRva.ToString('X8') + ' 名称=' + $exportFunctionName))
        }
    } else {
        $exportLines.Add('无导出表')
    }

    # ---------- 资源目录 ----------
    $resourceEntries = @()
    $resourceRva = Get-UInt32 $bytes ($directoryOffset + 16)
    $resourceSize = Get-UInt32 $bytes ($directoryOffset + 20)
    $resourceBase = Convert-RvaToOffset $bytes $sections $resourceRva $sizeOfHeaders
    $typeNameMap = @{
        1 = 'CURSOR'; 2 = 'BITMAP'; 3 = 'ICON'; 4 = 'MENU'; 5 = 'DIALOG'; 6 = 'STRING'; 7 = 'FONTDIR';
        8 = 'FONT'; 9 = 'ACCELERATOR'; 10 = 'RCDATA'; 11 = 'MESSAGETABLE'; 12 = 'GROUP_CURSOR';
        14 = 'GROUP_ICON'; 16 = 'VERSION'; 17 = 'DLGINCLUDE'; 19 = 'PLUGPLAY'; 20 = 'VXD';
        21 = 'ANICURSOR'; 22 = 'ANIICON'; 23 = 'HTML'; 24 = 'MANIFEST'
    }
    if ($resourceBase -gt 0) {
        $pending = New-Object System.Collections.Generic.Stack[object]
        $pending.Push([pscustomobject]@{ 目录偏移 = $resourceBase; 层级 = 0; 路径 = '' })
        $visited = 0
        while ($pending.Count -gt 0) {
            $current = $pending.Pop()
            $visited++
            if ($visited -gt 8000) { Write-Log '资源遍历超过 8000 项，提前结束'; break }
            $dirOffset = [int]$current.目录偏移
            $namedCount = Get-UInt16 $bytes ($dirOffset + 12)
            $idCount = Get-UInt16 $bytes ($dirOffset + 14)
            $totalCount = [int]$namedCount + [int]$idCount
            for ($entryIndex = 0; $entryIndex -lt $totalCount; $entryIndex++) {
                $entryOffset = $dirOffset + 16 + ($entryIndex * 8)
                $nameField = Get-UInt32 $bytes $entryOffset
                $dataField = Get-UInt32 $bytes ($entryOffset + 4)
                $entryName = ''
                if ([uint64]$nameField -ge 2147483648) {
                    $stringOffset = $resourceBase + [int]($nameField -band 0x7FFFFFFF)
                    $length = Get-UInt16 $bytes $stringOffset
                    if ($length -gt 200) { $length = 200 }
                    if ($stringOffset + 2 + ($length * 2) -le $bytes.Length) {
                        $entryName = [System.Text.Encoding]::Unicode.GetString($bytes, $stringOffset + 2, $length * 2)
                    } else {
                        $entryName = [string]$nameField
                    }
                } else {
                    $entryName = [string]$nameField
                }
                if ([uint64]$dataField -ge 2147483648) {
                    $subOffset = $resourceBase + [int]($dataField -band 0x7FFFFFFF)
                    $newPath = $current.路径
                    if ($newPath.Length -gt 0) { $newPath = $newPath + '/' }
                    $newPath = $newPath + $entryName
                    $pending.Push([pscustomobject]@{ 目录偏移 = $subOffset; 层级 = ([int]$current.层级 + 1); 路径 = $newPath })
                } else {
                    $dataEntryOffset = $resourceBase + [int]$dataField
                    $dataRva = Get-UInt32 $bytes $dataEntryOffset
                    $dataSize = Get-UInt32 $bytes ($dataEntryOffset + 4)
                    $codePage = Get-UInt32 $bytes ($dataEntryOffset + 8)
                    $resourceEntries += [pscustomobject]@{
                        类型编号 = $current.路径.Split('/')[0]
                        名称 = $entryName
                        完整路径 = $current.路径
                        语言 = $current.层级
                        数据地址 = ('0x' + $dataRva.ToString('X8'))
                        大小 = $dataSize
                        代码页 = $codePage
                    }
                }
            }
        }
    }

    $resourceLines = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $resourceEntries) {
        $typeKey = $entry.类型编号
        $typeDisplay = $typeKey
        if ($typeNameMap.ContainsKey([int]$typeKey)) { $typeDisplay = $typeNameMap[[int]$typeKey] }
        $resourceLines.Add(($typeDisplay + ' | 名称=' + $entry.名称 + ' | 路径=' + $entry.完整路径 + ' | 地址=' + $entry.数据地址 + ' | 大小=' + $entry.大小 + ' | 代码页=' + $entry.代码页))
    }

    # 提取清单
    $manifestExtracted = $false
    foreach ($entry in $resourceEntries) {
        if ([int]$entry.类型编号 -eq 24) {
            $manifestOffset = Convert-RvaToOffset $bytes $sections ([uint32]([Convert]::ToUInt32($entry.数据地址, 16))) $sizeOfHeaders
            if ($manifestOffset -gt 0 -and $entry.大小 -gt 0) {
                $manifestBytes = New-Object byte[] ([int]$entry.大小)
                [Array]::Copy($bytes, $manifestOffset, $manifestBytes, 0, [int]$entry.大小)
                [System.IO.File]::WriteAllBytes((Join-Path $OutDir 'manifest.xml'), $manifestBytes)
                $manifestExtracted = $true
            }
        }
    }

    # ---------- 调试目录 / PDB ----------
    $debugLines = New-Object System.Collections.Generic.List[string]
    $debugRva = 0
    $debugSize = 0
    if ($directoryCount -gt 6) {
        $debugRva = Get-UInt32 $bytes ($directoryOffset + (6 * 8))
        $debugSize = Get-UInt32 $bytes ($directoryOffset + (6 * 8 + 4))
    }
    $pdbPath = ''
    if ($debugRva -gt 0 -and $debugSize -gt 0) {
        $debugOffset = Convert-RvaToOffset $bytes $sections $debugRva $sizeOfHeaders
        if ($debugOffset -gt 0) {
            $entryCount = [int]($debugSize / 28)
            for ($debugIndex = 0; $debugIndex -lt $entryCount; $debugIndex++) {
                $entryOffset = $debugOffset + ($debugIndex * 28)
                $debugType = Get-UInt32 $bytes ($entryOffset + 12)
                $dataSize = Get-UInt32 $bytes ($entryOffset + 16)
                $dataPointer = Get-UInt32 $bytes ($entryOffset + 24)
                $debugLines.Add(('调试项 ' + $debugIndex + ' 类型=' + $debugType + ' 数据大小=' + $dataSize + ' 文件偏移=0x' + $dataPointer.ToString('X')))
                if ($debugType -eq 2 -and $dataPointer -gt 0 -and ($dataPointer + 24) -lt $bytes.Length) {
                    $signature = [System.Text.Encoding]::ASCII.GetString($bytes, [int]$dataPointer, 4)
                    if ($signature -eq 'RSDS') {
                        $pdbPath = Get-ReadAsciiZ $bytes ([int]$dataPointer + 24) 512
                        $debugLines.Add(('    代码视图签名=RSDS PDB 路径=' + $pdbPath))
                    } else {
                        $debugLines.Add(('    代码视图签名=' + $signature))
                    }
                }
            }
        }
    } else {
        $debugLines.Add('无调试目录')
    }

    # ---------- CLR 头 ----------
    $clrLines = New-Object System.Collections.Generic.List[string]
    $isManaged = $false
    $clrVersion = ''
    if ($directoryCount -gt 14) {
        $clrRva = Get-UInt32 $bytes ($directoryOffset + (14 * 8))
        $clrSize = Get-UInt32 $bytes ($directoryOffset + (14 * 8 + 4))
        if ($clrRva -gt 0 -and $clrSize -gt 0) {
            $isManaged = $true
            $clrOffset = Convert-RvaToOffset $bytes $sections $clrRva $sizeOfHeaders
            $clrLines.Add(('存在 CLR 头 地址=0x' + $clrRva.ToString('X8') + ' 大小=' + $clrSize))
            if ($clrOffset -gt 0) {
                $metadataRva = Get-UInt32 $bytes ($clrOffset + 8)
                $metadataSize = Get-UInt32 $bytes ($clrOffset + 12)
                $runtimeMajor = Get-UInt16 $bytes ($clrOffset + 4)
                $runtimeMinor = Get-UInt16 $bytes ($clrOffset + 6)
                $flags = Get-UInt32 $bytes ($clrOffset + 16)
                $entryPointToken = Get-UInt32 $bytes ($clrOffset + 20)
                $clrLines.Add(('运行时版本=' + $runtimeMajor + '.' + $runtimeMinor + ' 元数据地址=0x' + $metadataRva.ToString('X8') + ' 元数据大小=' + $metadataSize + ' 标志=0x' + $flags.ToString('X8') + ' 入口令牌=0x' + $entryPointToken.ToString('X8')))
                $metadataOffset = Convert-RvaToOffset $bytes $sections $metadataRva $sizeOfHeaders
                if ($metadataOffset -gt 0) {
                    $metadataSignature = Get-UInt32 $bytes $metadataOffset
                    $clrLines.Add(('元数据签名=0x' + $metadataSignature.ToString('X8') + '（期望 0x424A5342，即 BSJB）'))
                    $versionLength = Get-UInt32 $bytes ($metadataOffset + 12)
                    if ($versionLength -gt 0 -and $versionLength -lt 512) {
                        $clrVersion = [System.Text.Encoding]::ASCII.GetString($bytes, $metadataOffset + 16, [int]$versionLength).Trim([char]0)
                        $clrLines.Add(('元数据版本字符串=' + $clrVersion))
                    }
                }
            }
        } else {
            $clrLines.Add('无 CLR 头（原生程序）')
        }
    } else {
        $clrLines.Add('无 CLR 目录（原生程序）')
    }

    # ---------- Rich 头 ----------
    $richLines = New-Object System.Collections.Generic.List[string]
    $richAscii = [System.Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(0x2000, $bytes.Length))
    $richIndex = $richAscii.IndexOf('Rich')
    if ($richIndex -gt 0) {
        $richLines.Add(('Rich 标记文件偏移=0x' + ($richIndex).ToString('X')))
        $danSIndex = $richAscii.LastIndexOf('DanS', $richIndex)
        if ($danSIndex -gt 0) {
            $richLines.Add(('DanS 标记文件偏移=0x' + $danSIndex.ToString('X') + ' Rich 条目数=' + [Math]::Floor(($richIndex - $danSIndex - 16) / 8)))
            for ($offset = $danSIndex + 4; $offset -lt ($richIndex - 4); $offset += 8) {
                $productId = Get-UInt32 $bytes $offset
                $buildId = Get-UInt32 $bytes ($offset + 4)
                $richLines.Add(('    产品号=' + $productId + ' 构建号=' + $buildId))
            }
        }
    } else {
        $richLines.Add('未发现 Rich 头')
    }

    # ---------- 版本资源 ----------
    $versionInfo = $targetItem.VersionInfo
    $versionLines = New-Object System.Collections.Generic.List[string]
    $versionLines.Add('FileVersion=' + $versionInfo.FileVersion)
    $versionLines.Add('ProductVersion=' + $versionInfo.ProductVersion)
    $versionLines.Add('FileDescription=' + $versionInfo.FileDescription)
    $versionLines.Add('CompanyName=' + $versionInfo.CompanyName)
    $versionLines.Add('InternalName=' + $versionInfo.InternalName)
    $versionLines.Add('OriginalFilename=' + $versionInfo.OriginalFilename)
    $versionLines.Add('LegalCopyright=' + $versionInfo.LegalCopyright)
    $versionLines.Add('ProductName=' + $versionInfo.ProductName)
    $versionLines.Add('Comments=' + $versionInfo.Comments)

    # ---------- 字符串提取 ----------
    $asciiList = New-Object System.Collections.Generic.List[object]
    $unicodeList = New-Object System.Collections.Generic.List[object]
    $keywordSummary = [ordered]@{}
    if (-not $SkipStrings) {
    Write-Log '开始提取 ASCII 字符串（最小长度 4）'
    $asciiText = [System.Text.Encoding]::ASCII.GetString($bytes)
    $asciiMatches = [regex]::Matches($asciiText, '[ -~]{4,}')
    $asciiList = New-Object System.Collections.Generic.List[object]
    $asciiWriter = New-Object System.IO.StreamWriter((Join-Path $OutDir 'strings-ascii.txt'), $false, [System.Text.Encoding]::UTF8)
    foreach ($match in $asciiMatches) {
        $value = $match.Value.TrimEnd()
        if ($value.Length -lt 4) { continue }
        $offset = $match.Index
        $asciiList.Add([pscustomobject]@{ 偏移 = $offset; 文本 = $value })
        $asciiWriter.WriteLine(('0x' + $offset.ToString('X8') + "`t" + $value))
    }
    $asciiWriter.Close()
    Write-Log ('ASCII 字符串数量=' + $asciiList.Count)

    Write-Log '开始提取 UTF-16LE 字符串（最小长度 4，含中文）'
    $unicodeText = [System.Text.Encoding]::Unicode.GetString($bytes)
    $unicodeMatches = [regex]::Matches($unicodeText, '[\u0020-\u007E\u3000-\u9FFF\uFF01-\uFF5E]{4,}')
    $unicodeList = New-Object System.Collections.Generic.List[object]
    $unicodeWriter = New-Object System.IO.StreamWriter((Join-Path $OutDir 'strings-utf16.txt'), $false, [System.Text.Encoding]::UTF8)
    foreach ($match in $unicodeMatches) {
        $value = $match.Value.TrimEnd()
        if ($value.Length -lt 4) { continue }
        $offset = $match.Index * 2
        $unicodeList.Add([pscustomobject]@{ 偏移 = $offset; 文本 = $value })
        $unicodeWriter.WriteLine(('0x' + $offset.ToString('X8') + "`t" + $value))
    }
    $unicodeWriter.Close()
    Write-Log ('UTF-16 字符串数量=' + $unicodeList.Count)

    # ---------- 关键字命中 ----------
    $keywordGroups = [ordered]@{
        '框架与运行时' = @('Qt5','Qt6','QWidget','QString','QApplication','qt_','VCL','TForm','TObject','System\.SysUtils','Embarcadero','Borland','Delphi','mscoree','System\.Windows\.Forms','PresentationFramework','CEF','Chromium','WebView2','Electron','System\.Reflection')
        '截图采集' = @('BitBlt','PrintWindow','GetDC','CreateCompatibleBitmap','CreateCompatibleDC','StretchDIBits','GetDIBits','DesktopDuplication','DwmRegisterThumbnail','DWM','GraphicsCaptureItem','Windows\.Graphics\.Capture','MonitorFromPoint','GetSystemMetrics','EnumDisplayMonitors','GetForegroundWindow','GetWindowRect','MAGNIFICATION','Magnification')
        '录屏与编码' = @('H264','h264','HEVC','hevc','NVENC','nvenc','MFVideoFormat','MFCreateSinkWriterFromURL','SinkWriter','MediaFoundation','MFStartup','AVIF','libx264','x264','libvpx','VP8','VP9','AV1','MPEG4','mp4','webm','gif','GIF','AVI','WMV','MKV','AAC','MP3','Opus','PCM','WASAPI','waveIn','waveOut','DirectShow','ffmpeg','FFmpeg','ScreenpressoCodec','ICodec','VideoCodec')
        '快捷键与热键' = @('RegisterHotKey','UnregisterHotKey','SetWindowsHookEx','GetAsyncKeyState','PrintScreen','VK_SNAPSHOT','HotKey','MOD_CONTROL','MOD_ALT','MOD_SHIFT','LowLevelKeyboardProc','WH_KEYBOARD_LL')
        '剪贴板与拖拽' = @('SetClipboardData','OpenClipboard','EmptyClipboard','CF_DIB','CF_HDROP','OleSetClipboard','IDataObject','RegisterDragDrop','DoDragDrop','DragQueryPoint','DragQueryFile','CF_UNICODETEXT','GetClipboardData')
        '系统集成' = @('Shell_NotifyIcon','NIM_ADD','NIM_MODIFY','CurrentVersion\\Run','Startup','schtasks','TaskScheduler','CreateService','AutoStart','SingleInstance','Mutex','RegisterWindowMessage','DDE')
        '编辑与标注' = @('Arrow','Rectangle','Ellipse','Highlighter','Pixelate','Blur','Magnifier','Callout','Watermark','AutoCrop','Grayscale','Sepia','Sharpen','Denoise','Rotate','Flip','Crop','Resize','Stamp','Numbering','TextTool','Effects')
        'OCR 与识别' = @('OCR','Tesseract','tesseract','RecognizeText','OcrEngine','Windows\.Media\.Ocr','Barcode','ZXing','QRCoder','QRCode','ZBar','SevenZip','zlib','libpng','libjpeg','WebP')
        '云与分享' = @('ScreenpressoCloud','Screenpresso','Learnpulse','Dropbox','OneDrive','Google','Drive','YouTube','AWSSDK','S3','FTP','SFTP','imgur','GitHub','Gyazo','ShareX','Upload')
        '文件格式与导出' = @('PNG','JPG','JPEG','BMP','TIFF','PDF','DOCX','HTML','GemBox','Document','SaveAs','Export')
        '命令行与集成点' = @('ScreenpressoCapture','Capture\.exe','--','CommandLine','CommandLineToArgvW','WM_COPYDATA','IPC','NamedPipe','CreateProcess')
    }

    $keywordWriter = New-Object System.IO.StreamWriter((Join-Path $OutDir 'keyword-hits.md'), $false, [System.Text.Encoding]::UTF8)
    $keywordWriter.WriteLine('# Screenpresso 关键字命中（直接字符串证据）')
    $keywordWriter.WriteLine('')
    $keywordWriter.WriteLine('说明：偏移是文件内字节偏移；ASCII 与 UTF-16LE 分别统计。每条记录最多保留 12 个命中样本。')
    $keywordWriter.WriteLine('')
    foreach ($groupName in $keywordGroups.Keys) {
        $patterns = $keywordGroups[$groupName]
        $keywordWriter.WriteLine('## ' + $groupName)
        $keywordWriter.WriteLine('')
        $keywordWriter.WriteLine('| 关键字 | 命中总数 | 样本偏移与文本 |')
        $keywordWriter.WriteLine('| --- | --- | --- |')
        $groupSummary = [ordered]@{}
        $compiledPatterns = @()
        foreach ($pattern in $patterns) {
            $compiledPatterns += [pscustomobject]@{ 模式 = $pattern; 正则 = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) }
        }
        $combinedPattern = '(' + ($patterns -join ')|(') + ')'
        $combinedRegex = [regex]::new($combinedPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $candidateAscii = @($asciiList | Where-Object { $combinedRegex.IsMatch($_.文本) })
        $candidateUnicode = @($unicodeList | Where-Object { $combinedRegex.IsMatch($_.文本) })
        foreach ($compiled in $compiledPatterns) {
            $pattern = $compiled.模式
            $asciiHits = @($candidateAscii | Where-Object { $compiled.正则.IsMatch($_.文本) })
            $unicodeHits = @($candidateUnicode | Where-Object { $compiled.正则.IsMatch($_.文本) })
            $total = $asciiHits.Count + $unicodeHits.Count
            if ($total -eq 0) { continue }
            $samples = New-Object System.Collections.Generic.List[string]
            $sampleCount = 0
            foreach ($hit in $asciiHits) {
                if ($sampleCount -ge 12) { break }
                $safeText = $hit.文本.Replace('|', '｜')
                if ($safeText.Length -gt 140) { $safeText = $safeText.Substring(0, 140) + '…' }
                $samples.Add('ASCII 0x' + $hit.偏移.ToString('X8') + ' ' + $safeText)
                $sampleCount++
            }
            foreach ($hit in $unicodeHits) {
                if ($sampleCount -ge 12) { break }
                $safeText = $hit.文本.Replace('|', '｜')
                if ($safeText.Length -gt 140) { $safeText = $safeText.Substring(0, 140) + '…' }
                $samples.Add('UTF16 0x' + $hit.偏移.ToString('X8') + ' ' + $safeText)
                $sampleCount++
            }
            $keywordWriter.WriteLine('| ' + $pattern + ' | ' + $total + ' | ' + ($samples -join '<br>') + ' |')
            $groupSummary[$pattern] = $total
        }
        if ($groupSummary.Count -eq 0) { $keywordWriter.WriteLine('| （无命中） | 0 |  |') }
        $keywordWriter.WriteLine('')
        $keywordSummary[$groupName] = $groupSummary
    }
    $keywordWriter.Close()
    Write-Log '关键字命中报告已完成'
    }

    # ---------- 汇总 ----------
    $peInfoWriter = New-Object System.IO.StreamWriter((Join-Path $OutDir 'pe-info.md'), $false, [System.Text.Encoding]::UTF8)
    $peInfoWriter.WriteLine('# Screenpresso PE 静态信息')
    $peInfoWriter.WriteLine('')
    $peInfoWriter.WriteLine('- 目标文件：' + $TargetPath)
    $peInfoWriter.WriteLine('- 文件大小：' + $bytes.Length + ' 字节')
    $peInfoWriter.WriteLine('- PE 架构：' + $machineText)
    $peInfoWriter.WriteLine('- 子系统：' + $subsystemText)
    $peInfoWriter.WriteLine('- 链接器版本：' + $linkerMajor + '.' + $linkerMinor)
    $peInfoWriter.WriteLine('- 编译时间戳：' + $timestampUtc + ' UTC（值=' + $timeDateStamp + '）')
    $peInfoWriter.WriteLine('- 入口点 RVA：0x' + $entryPoint.ToString('X8'))
    $peInfoWriter.WriteLine('- 映像基址：0x' + $imageBase.ToString('X'))
    $peInfoWriter.WriteLine('- 映像大小：' + $sizeOfImage + ' 字节')
    $peInfoWriter.WriteLine('- 校验和：0x' + $checksum.ToString('X8'))
    $peInfoWriter.WriteLine('- DllCharacteristics：0x' + $dllCharacteristics.ToString('X4'))
    $peInfoWriter.WriteLine('- 是否存在 CLR 头：' + $isManaged)
    $peInfoWriter.WriteLine('- CLR 元数据版本：' + $clrVersion)
    $peInfoWriter.WriteLine('- PDB 路径：' + $pdbPath)
    $peInfoWriter.WriteLine('')
    $peInfoWriter.WriteLine('## 节表')
    $peInfoWriter.WriteLine('')
    $peInfoWriter.WriteLine('| 名称 | 虚拟地址 | 虚拟大小 | 原始偏移 | 原始大小 | 熵 | 特征 |')
    $peInfoWriter.WriteLine('| --- | --- | --- | --- | --- | --- | --- |')
    foreach ($section in $sections) {
        $peInfoWriter.WriteLine('| ' + $section.名称 + ' | ' + $section.虚拟地址 + ' | ' + $section.虚拟大小 + ' | ' + $section.原始偏移 + ' | ' + $section.原始大小 + ' | ' + $section.熵 + ' | ' + $section.特征 + ' |')
    }
    $peInfoWriter.WriteLine('')
    $peInfoWriter.WriteLine('## 数据目录')
    $peInfoWriter.WriteLine('')
    $peInfoWriter.WriteLine('| 序号 | 名称 | 地址 | 大小 |')
    $peInfoWriter.WriteLine('| --- | --- | --- | --- |')
    foreach ($directory in $directories) {
        $peInfoWriter.WriteLine('| ' + $directory.序号 + ' | ' + $directory.名称 + ' | ' + $directory.地址 + ' | ' + $directory.大小 + ' |')
    }
    $peInfoWriter.WriteLine('')
    $peInfoWriter.WriteLine('## 导入模块')
    $peInfoWriter.WriteLine('')
    foreach ($module in $importModules) { $peInfoWriter.WriteLine('- ' + $module) }
    $peInfoWriter.Close()

    $importLines | Out-File -LiteralPath (Join-Path $OutDir 'imports.txt') -Encoding utf8
    $exportLines | Out-File -LiteralPath (Join-Path $OutDir 'exports.txt') -Encoding utf8
    $resourceLines | Out-File -LiteralPath (Join-Path $OutDir 'resources.txt') -Encoding utf8
    $versionLines | Out-File -LiteralPath (Join-Path $OutDir 'version-info.txt') -Encoding utf8
    $debugLines | Out-File -LiteralPath (Join-Path $OutDir 'debug-directory.txt') -Encoding utf8
    $clrLines | Out-File -LiteralPath (Join-Path $OutDir 'clr-header.txt') -Encoding utf8
    $richLines | Out-File -LiteralPath (Join-Path $OutDir 'rich-header.txt') -Encoding utf8

    $summary = [ordered]@{
        目标文件 = $TargetPath
        文件大小 = $bytes.Length
        PE架构 = $machineText
        子系统 = $subsystemText
        链接器版本 = ($linkerMajor.ToString() + '.' + $linkerMinor.ToString())
        编译时间戳UTC = $timestampUtc
        入口点 = ('0x' + $entryPoint.ToString('X8'))
        映像基址 = ('0x' + $imageBase.ToString('X'))
        映像大小 = $sizeOfImage
        校验和 = ('0x' + $checksum.ToString('X8'))
        DllCharacteristics = ('0x' + $dllCharacteristics.ToString('X4'))
        是否托管 = $isManaged
        CLR元数据版本 = $clrVersion
        PDB路径 = $pdbPath
        节数量 = $sections.Count
        导入模块 = $importModules.ToArray()
        资源条目数 = $resourceEntries.Count
        ASCII字符串数 = $asciiList.Count
        UTF16字符串数 = $unicodeList.Count
        清单已提取 = $manifestExtracted
        关键字统计 = $keywordSummary
        版本信息 = [ordered]@{
            FileVersion = $versionInfo.FileVersion
            ProductVersion = $versionInfo.ProductVersion
            CompanyName = $versionInfo.CompanyName
            FileDescription = $versionInfo.FileDescription
            ProductName = $versionInfo.ProductName
            OriginalFilename = $versionInfo.OriginalFilename
        }
        节表 = $sections
        数据目录 = $directories
    }
    $summary | ConvertTo-Json -Depth 6 | Out-File -LiteralPath (Join-Path $OutDir 'static-probe-summary.json') -Encoding utf8

    Write-Log '静态取证完成'
    Set-Content -LiteralPath $script:ExitCodePath -Value 0 -Encoding utf8
    exit 0
} catch {
    $message = $_.Exception.Message
    Write-Log ('静态取证失败：' + $message)
    Set-Content -LiteralPath $script:ExitCodePath -Value 1 -Encoding utf8
    exit 1
}
