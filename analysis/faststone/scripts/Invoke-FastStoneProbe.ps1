<#
FastStone Capture 静态取证探针（不依赖外部工具，纯 PowerShell 读取 PE）

产出（写入 -OutDir）：
  pe-info.md                 PE 头、可选头、数据目录、节表、熵
  sections.txt               节表明细
  imports.txt                导入表（含延迟导入）
  exports.txt                导出表
  resources.txt              资源目录（类型/名称/语言/大小）
  manifest.xml               RT_MANIFEST 原文（若存在）
  version-info.txt           版本资源
  debug-directory.txt        调试目录 / PDB 路径
  clr-header.txt             是否 .NET 托管程序及元数据版本
  strings-ascii.txt          ASCII 字符串（最小 5 字符）
  strings-utf16.txt          UTF-16LE 字符串（最小 4 字符，含中文）
  keyword-hits.md            滚动截图相关关键字命中（带文件偏移）
  static-probe.log           过程日志（纯中文、完整参数）
  static-probe.exitcode      退出码

用法：
  powershell -NoProfile -ExecutionPolicy Bypass -File Invoke-FastStoneProbe.ps1 `
      -TargetPath 'D:\TabTaskExplorer\references\FastStone Capture\FSCapture.exe' `
      -OutDir 'D:\TabTaskExplorer\analysis\faststone\evidence'
#>

param(
    [string]$TargetPath = 'D:\TabTaskExplorer\references\FastStone Capture\FSCapture.exe',
    [string]$OutDir = 'D:\TabTaskExplorer\analysis\faststone\evidence',
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

$script:Bytes = $null
$script:Sections = @()
$script:Is64 = $false

function Get-UInt16 { param([byte[]]$Bytes, [int]$Offset) return [System.BitConverter]::ToUInt16($Bytes, $Offset) }
function Get-UInt32 { param([byte[]]$Bytes, [int]$Offset) return [System.BitConverter]::ToUInt32($Bytes, $Offset) }
function Get-UInt64 { param([byte[]]$Bytes, [int]$Offset) return [System.BitConverter]::ToUInt64($Bytes, $Offset) }
function Get-Hex { param([UInt64]$Value) return '0x' + $Value.ToString('X') }

function Get-ReadAsciiZ {
    param([byte[]]$Bytes, [int]$Offset, [int]$MaxLength = 512)
    if ($Offset -lt 0 -or $Offset -ge $Bytes.Length) { return '' }
    $end = $Offset
    $limit = [Math]::Min($Bytes.Length, $Offset + $MaxLength)
    while ($end -lt $limit -and $Bytes[$end] -ne 0) { $end++ }
    $length = $end - $Offset
    if ($length -le 0) { return '' }
    return [System.Text.Encoding]::ASCII.GetString($Bytes, $Offset, $length)
}

function Convert-RvaToOffset {
    param([UInt32]$Rva)
    foreach ($section in $script:Sections) {
        if ($Rva -ge $section.VirtualAddress -and $Rva -lt ($section.VirtualAddress + $section.VirtualSize)) {
            return [int]($section.PointerToRawData + ($Rva - $section.VirtualAddress))
        }
    }
    # 头部区域：RVA 等于文件偏移
    if ($Rva -lt $script:Sections[0].PointerToRawData) { return [int]$Rva }
    return -1
}

function Get-SectionEntropy {
    param([byte[]]$Data)
    if ($Data.Length -le 0) { return 0.0 }
    $counts = New-Object 'int[]' 256
    foreach ($b in $Data) { $counts[$b]++ }
    $entropy = 0.0
    foreach ($c in $counts) {
        if ($c -gt 0) {
            $p = [double]$c / [double]$Data.Length
            $entropy += -$p * [Math]::Log($p, 2)
        }
    }
    return [Math]::Round($entropy, 4)
}

try {
    if (Test-Path -LiteralPath $script:ExitCodePath) { Remove-Item -LiteralPath $script:ExitCodePath -Force }
    if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
    Set-Content -LiteralPath $script:LogPath -Value '' -Encoding utf8

    Write-Log ('开始静态取证 目标=' + $TargetPath + ' 输出目录=' + $OutDir)
    if (-not (Test-Path -LiteralPath $TargetPath)) { throw ('目标文件不存在：' + $TargetPath) }

    $targetItem = Get-Item -LiteralPath $TargetPath
    Write-Log ('目标文件大小=' + $targetItem.Length + ' 字节 修改时间=' + $targetItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))

    $bytes = [System.IO.File]::ReadAllBytes($TargetPath)
    $script:Bytes = $bytes
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $machine = Get-UInt16 $bytes ($peOffset + 4)
    $numberOfSections = Get-UInt16 $bytes ($peOffset + 6)
    $timeStamp = Get-UInt32 $bytes ($peOffset + 8)
    $sizeOfOptionalHeader = Get-UInt16 $bytes ($peOffset + 20)
    $characteristics = Get-UInt16 $bytes ($peOffset + 22)
    $optionalOffset = $peOffset + 24
    $magic = Get-UInt16 $bytes $optionalOffset
    $script:Is64 = ($magic -eq 0x20B)

    $peInfo = New-Object System.Collections.Generic.List[string]
    $peInfo.Add('# PE 结构')
    $peInfo.Add('')
    $peInfo.Add('| 项 | 值 |')
    $peInfo.Add('| --- | --- |')
    $machineText = switch ($machine) {
        0x014c { '0x014C i386（32 位）' }
        0x8664 { '0x8664 x64（64 位）' }
        0xAA64 { '0xAA64 ARM64' }
        default { (Get-Hex $machine) }
    }
    $peInfo.Add('| 机器码 | ' + $machineText + ' |')
    $peInfo.Add('| 节数量 | ' + $numberOfSections + ' |')
    $peInfo.Add('| 链接器时间戳 | ' + (Get-Hex $timeStamp) + '（' + ([DateTimeOffset]::FromUnixTimeSeconds([int64]$timeStamp).ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')) + '） |')
    $magicText = if ($script:Is64) { '0x20B PE32+（64 位）' } else { '0x10B PE32（32 位）' }
    $peInfo.Add('| 可选头魔数 | ' + $magicText + ' |')
    $peInfo.Add('| 可选头大小 | ' + $sizeOfOptionalHeader + ' |')
    $peInfo.Add('| 文件特征 | ' + (Get-Hex $characteristics) + ' |')

    if ($sizeOfOptionalHeader -ge 68) {
        $linkerMajor = $bytes[$optionalOffset + 2]
        $linkerMinor = $bytes[$optionalOffset + 3]
        $entryPoint = Get-UInt32 $bytes ($optionalOffset + 16)
        $subsystem = Get-UInt16 $bytes ($optionalOffset + 68)
        $dllCharacteristics = Get-UInt16 $bytes ($optionalOffset + 70)
        if ($script:Is64) {
            $imageBase = Get-UInt64 $bytes ($optionalOffset + 24)
            $sectionAlignment = Get-UInt32 $bytes ($optionalOffset + 32)
            $fileAlignment = Get-UInt32 $bytes ($optionalOffset + 36)
            $sizeOfImage = Get-UInt32 $bytes ($optionalOffset + 56)
            $dataDirectoryOffset = $optionalOffset + 112
        } else {
            $imageBase = [uint64](Get-UInt32 $bytes ($optionalOffset + 28))
            $sectionAlignment = Get-UInt32 $bytes ($optionalOffset + 32)
            $fileAlignment = Get-UInt32 $bytes ($optionalOffset + 36)
            $sizeOfImage = Get-UInt32 $bytes ($optionalOffset + 56)
            $dataDirectoryOffset = $optionalOffset + 96
        }
        $peInfo.Add('| 链接器版本 | ' + $linkerMajor + '.' + $linkerMinor + ' |')
        $peInfo.Add('| 入口点 RVA | ' + (Get-Hex $entryPoint) + ' |')
        $peInfo.Add('| 映像基址 | ' + (Get-Hex $imageBase) + ' |')
        $peInfo.Add('| 节对齐 | ' + $sectionAlignment + ' |')
        $peInfo.Add('| 文件对齐 | ' + $fileAlignment + ' |')
        $peInfo.Add('| 映像大小 | ' + $sizeOfImage + ' |')
        $peInfo.Add('| 子系统 | ' + (Get-Hex $subsystem) + ' |')
        $peInfo.Add('| DLL 特征 | ' + (Get-Hex $dllCharacteristics) + ' |')
        Write-Log ('PE 头解析完成 机器码=' + $machineText + ' 节数量=' + $numberOfSections + ' 时间戳=' + $timeStamp + ' 可选头魔数=' + $magicText + ' 入口点=' + (Get-Hex $entryPoint) + ' 映像基址=' + (Get-Hex $imageBase) + ' 子系统=' + (Get-Hex $subsystem) + ' DLL特征=' + (Get-Hex $dllCharacteristics))
    }

    # 节表
    $sectionOffset = $optionalOffset + $sizeOfOptionalHeader
    $sectionLines = New-Object System.Collections.Generic.List[string]
    $sectionLines.Add('名称 虚拟地址 虚拟大小 原始数据偏移 原始数据大小 特征 熵')
    $script:Sections = @()
    for ($index = 0; $index -lt $numberOfSections; $index++) {
        $current = $sectionOffset + $index * 40
        $nameBytes = New-Object byte[] 8
        [Array]::Copy($bytes, $current, $nameBytes, 0, 8)
        $name = [System.Text.Encoding]::ASCII.GetString($nameBytes).Trim([char]0)
        $virtualSize = Get-UInt32 $bytes ($current + 8)
        $virtualAddress = Get-UInt32 $bytes ($current + 12)
        $rawSize = Get-UInt32 $bytes ($current + 16)
        $rawPointer = Get-UInt32 $bytes ($current + 20)
        $sectionCharacteristics = Get-UInt32 $bytes ($current + 36)
        $rawStart = [int]$rawPointer
        $rawEnd = [Math]::Min($bytes.Length, [int]($rawPointer + $rawSize))
        $entropy = 0.0
        if ($rawEnd -gt $rawStart) {
            $slice = New-Object byte[] ($rawEnd - $rawStart)
            [Array]::Copy($bytes, $rawStart, $slice, 0, $slice.Length)
            $entropy = Get-SectionEntropy $slice
            $slice = $null
        }
        $entry = [ordered]@{
            Name = $name
            VirtualAddress = $virtualAddress
            VirtualSize = $virtualSize
            PointerToRawData = $rawPointer
            SizeOfRawData = $rawSize
            Characteristics = $sectionCharacteristics
            Entropy = $entropy
        }
        $script:Sections += $entry
        $sectionLines.Add($name + ' ' + (Get-Hex $virtualAddress) + ' ' + $virtualSize + ' ' + (Get-Hex $rawPointer) + ' ' + $rawSize + ' ' + (Get-Hex $sectionCharacteristics) + ' ' + $entropy)
        Write-Log ('节表 ' + $name + ' 虚拟地址=' + (Get-Hex $virtualAddress) + ' 虚拟大小=' + $virtualSize + ' 原始数据偏移=' + (Get-Hex $rawPointer) + ' 原始数据大小=' + $rawSize + ' 特征=' + (Get-Hex $sectionCharacteristics) + ' 熵=' + $entropy)
    }
    $sectionLines | Out-File -LiteralPath (Join-Path $OutDir 'sections.txt') -Encoding utf8

    $dataDirectoryNames = @('导出表', '导入表', '资源表', '异常表', '证书表', '基址重定位表', '调试表', '架构保留',
        '全局指针', '线程本地存储', '加载配置', '绑定导入', '导入地址表', '延迟导入', 'CLR 运行时', '保留')
    $directoryLines = New-Object System.Collections.Generic.List[string]
    $directoryLines.Add('索引 名称 RVA 大小')
    $directoryValues = @()
    for ($index = 0; $index -lt 16; $index++) {
        $rva = Get-UInt32 $bytes ($dataDirectoryOffset + $index * 8)
        $size = Get-UInt32 $bytes ($dataDirectoryOffset + $index * 8 + 4)
        $directoryValues += [ordered]@{ Rva = $rva; Size = $size }
        $directoryLines.Add(([string]$index) + ' ' + $dataDirectoryNames[$index] + ' ' + (Get-Hex $rva) + ' ' + $size)
    }
    $directoryLines | Out-File -LiteralPath (Join-Path $OutDir 'data-directories.txt') -Encoding utf8

    # CLR 头
    $clrLines = New-Object System.Collections.Generic.List[string]
    $clrRva = $directoryValues[14].Rva
    $clrSize = $directoryValues[14].Size
    if ($clrRva -ne 0) {
        $clrOffset = Convert-RvaToOffset $clrRva
        $cb = Get-UInt32 $bytes $clrOffset
        $runtimeMajor = Get-UInt16 $bytes ($clrOffset + 4)
        $runtimeMinor = Get-UInt16 $bytes ($clrOffset + 6)
        $metaRva = Get-UInt32 $bytes ($clrOffset + 8)
        $metaSize = Get-UInt32 $bytes ($clrOffset + 12)
        $clrFlags = Get-UInt32 $bytes ($clrOffset + 16)
        $entryToken = Get-UInt32 $bytes ($clrOffset + 20)
        $clrLines.Add('托管程序：是')
        $clrLines.Add('CLR 头大小=' + $cb)
        $clrLines.Add('运行时版本=' + $runtimeMajor + '.' + $runtimeMinor)
        $clrLines.Add('元数据 RVA=' + (Get-Hex $metaRva) + ' 大小=' + $metaSize)
        $clrLines.Add('CLR 标志=' + (Get-Hex $clrFlags))
        $clrLines.Add('入口令牌=' + (Get-Hex $entryToken))
        Write-Log ('CLR 头存在：托管程序 运行时版本=' + $runtimeMajor + '.' + $runtimeMinor + ' 元数据大小=' + $metaSize + ' 入口令牌=' + (Get-Hex $entryToken))
    } else {
        $clrLines.Add('托管程序：否（CLR 数据目录为空，属于原生 PE）')
        Write-Log 'CLR 数据目录为空：目标为原生 PE，不使用 IL/程序集分析'
    }
    $clrLines | Out-File -LiteralPath (Join-Path $OutDir 'clr-header.txt') -Encoding utf8

    # 调试目录
    $debugLines = New-Object System.Collections.Generic.List[string]
    $debugRva = $directoryValues[6].Rva
    if ($debugRva -ne 0) {
        $debugOffset = Convert-RvaToOffset $debugRva
        $debugCount = [int]($directoryValues[6].Size / 28)
        for ($index = 0; $index -lt $debugCount; $index++) {
            $entryOffset = $debugOffset + $index * 28
            $dbgCharacteristics = Get-UInt32 $bytes $entryOffset
            $dbgTimeStamp = Get-UInt32 $bytes ($entryOffset + 4)
            $dbgMajor = Get-UInt16 $bytes ($entryOffset + 8)
            $dbgMinor = Get-UInt16 $bytes ($entryOffset + 10)
            $dbgType = Get-UInt32 $bytes ($entryOffset + 12)
            $dbgSizeOfData = Get-UInt32 $bytes ($entryOffset + 16)
            $dbgAddress = Get-UInt32 $bytes ($entryOffset + 20)
            $dbgPointer = Get-UInt32 $bytes ($entryOffset + 24)
            $debugLines.Add('调试项 ' + $index + ' 类型=' + $dbgType + ' 数据大小=' + $dbgSizeOfData + ' 数据RVA=' + (Get-Hex $dbgAddress) + ' 文件偏移=' + (Get-Hex $dbgPointer))
            if ($dbgType -eq 2 -and $dbgPointer -gt 0 -and $dbgPointer -lt $bytes.Length) {
                $pdbText = Get-ReadAsciiZ $bytes ([int]$dbgPointer) 512
                $debugLines.Add('PDB 路径=' + $pdbText)
                Write-Log ('调试目录包含 CodeView/PDB 路径=' + $pdbText)
            }
        }
    } else {
        $debugLines.Add('无调试目录（发行版已剥离符号）')
        Write-Log '调试目录为空：发行版剥离符号'
    }
    $debugLines | Out-File -LiteralPath (Join-Path $OutDir 'debug-directory.txt') -Encoding utf8

    # 导入表
    $importLines = New-Object System.Collections.Generic.List[string]
    $importRva = $directoryValues[1].Rva
    $dllCount = 0
    $functionCount = 0
    if ($importRva -ne 0) {
        $descriptorOffset = Convert-RvaToOffset $importRva
        for ($index = 0; $index -lt 4096; $index++) {
            $current = $descriptorOffset + $index * 20
            if ($current + 20 -gt $bytes.Length) { break }
            $originalFirstThunk = Get-UInt32 $bytes $current
            $nameRva = Get-UInt32 $bytes ($current + 12)
            $firstThunk = Get-UInt32 $bytes ($current + 16)
            if ($originalFirstThunk -eq 0 -and $nameRva -eq 0 -and $firstThunk -eq 0) { break }
            $nameOffset = Convert-RvaToOffset $nameRva
            $dllName = Get-ReadAsciiZ $bytes $nameOffset 256
            $dllCount++
            $importLines.Add('DLL=' + $dllName)
            $thunkRva = if ($originalFirstThunk -ne 0) { $originalFirstThunk } else { $firstThunk }
            $thunkOffset = Convert-RvaToOffset $thunkRva
            $entrySize = if ($script:Is64) { 8 } else { 4 }
            for ($thunkIndex = 0; $thunkIndex -lt 4096; $thunkIndex++) {
                $thunkAddress = $thunkOffset + $thunkIndex * $entrySize
                if ($thunkAddress + $entrySize -gt $bytes.Length) { break }
                $value = if ($script:Is64) { Get-UInt64 $bytes $thunkAddress } else { [uint64](Get-UInt32 $bytes $thunkAddress) }
                if ($value -eq 0) { break }
                $ordinalFlag = if ($script:Is64) { [uint64]0x8000000000000000 } else { [uint64]0x80000000 }
                if (($value -band $ordinalFlag) -ne 0) {
                    $importLines.Add('  ' + $dllName + '!序号' + ($value -band 0xFFFF))
                    $functionCount++
                    continue
                }
                $hintNameOffset = Convert-RvaToOffset ([uint32]$value)
                $functionName = Get-ReadAsciiZ $bytes ([int]($hintNameOffset + 2)) 256
                $importLines.Add('  ' + $dllName + '!' + $functionName)
                $functionCount++
            }
        }
    }
    $importLines | Out-File -LiteralPath (Join-Path $OutDir 'imports.txt') -Encoding utf8
    Write-Log ('导入表解析完成 DLL数量=' + $dllCount + ' 函数数量=' + $functionCount)

    # 延迟导入
    $delayLines = New-Object System.Collections.Generic.List[string]
    $delayRva = $directoryValues[13].Rva
    $delayCount = 0
    if ($delayRva -ne 0) {
        $delayOffset = Convert-RvaToOffset $delayRva
        for ($index = 0; $index -lt 256; $index++) {
            $current = $delayOffset + $index * 32
            if ($current + 32 -gt $bytes.Length) { break }
            $attributes = Get-UInt32 $bytes $current
            $nameRva = Get-UInt32 $bytes ($current + 4)
            $intRva = Get-UInt32 $bytes ($current + 16)
            if ($nameRva -eq 0) { break }
            $nameOffset = Convert-RvaToOffset $nameRva
            $dllName = Get-ReadAsciiZ $bytes $nameOffset 256
            $delayLines.Add('DLL=' + $dllName + '（延迟导入）')
            $delayCount++
        }
    }
    $delayLines | Out-File -LiteralPath (Join-Path $OutDir 'delay-imports.txt') -Encoding utf8
    Write-Log ('延迟导入解析完成 DLL数量=' + $delayCount)

    # 导出表
    $exportLines = New-Object System.Collections.Generic.List[string]
    $exportRva = $directoryValues[0].Rva
    if ($exportRva -ne 0) {
        $exportOffset = Convert-RvaToOffset $exportRva
        $exportNameRva = Get-UInt32 $bytes ($exportOffset + 12)
        $base = Get-UInt32 $bytes ($exportOffset + 16)
        $numberOfFunctions = Get-UInt32 $bytes ($exportOffset + 20)
        $numberOfNames = Get-UInt32 $bytes ($exportOffset + 24)
        $addressOfFunctions = Get-UInt32 $bytes ($exportOffset + 28)
        $addressOfNames = Get-UInt32 $bytes ($exportOffset + 32)
        $addressOfOrdinals = Get-UInt32 $bytes ($exportOffset + 36)
        $exportLines.Add('导出函数数量=' + $numberOfFunctions + ' 导出名称数量=' + $numberOfNames + ' 序号基值=' + $base)
        $namesOffset = Convert-RvaToOffset $addressOfNames
        $ordinalsOffset = Convert-RvaToOffset $addressOfOrdinals
        for ($index = 0; $index -lt [Math]::Min($numberOfNames, 20000); $index++) {
            $nameRva = Get-UInt32 $bytes ($namesOffset + $index * 4)
            $ordinal = Get-UInt16 $bytes ($ordinalsOffset + $index * 2)
            $exportLines.Add((Get-ReadAsciiZ $bytes (Convert-RvaToOffset $nameRva) 256) + ' 序号=' + ($base + $ordinal))
        }
        Write-Log ('导出表解析完成 函数数量=' + $numberOfFunctions + ' 名称数量=' + $numberOfNames)
    } else {
        $exportLines.Add('无导出表（可执行程序通常不导出）')
    }
    $exportLines | Out-File -LiteralPath (Join-Path $OutDir 'exports.txt') -Encoding utf8

    # 资源
    $resourceLines = New-Object System.Collections.Generic.List[string]
    $resourceRva = $directoryValues[2].Rva
    $resourceRootOffset = if ($resourceRva -ne 0) { Convert-RvaToOffset $resourceRva } else { -1 }
    $manifestOffset = -1
    $manifestSize = 0
    if ($resourceRootOffset -ge 0) {
        $typeCount = Get-UInt16 $bytes ($resourceRootOffset + 12)
        $nameCount = Get-UInt16 $bytes ($resourceRootOffset + 14)
        $resourceLines.Add('资源类型数量=' + $typeCount + ' 名称数量=' + $nameCount)
        for ($typeIndex = 0; $typeIndex -lt $typeCount; $typeIndex++) {
            $typeEntry = $resourceRootOffset + 16 + $typeIndex * 8
            $typeId = Get-UInt32 $bytes $typeEntry
            $typeIsName = ($typeId -band 0x80000000) -ne 0
            $typeValue = $typeId -band 0x7FFFFFFF
            $typeText = if ($typeIsName) { '(命名类型)' } else { '类型=' + $typeValue }
            $typeSubdir = $resourceRootOffset + ($typeId -band 0x7FFFFFFF)
            $subCount = Get-UInt16 $bytes ($typeSubdir + 12)
            for ($nameIndex = 0; $nameIndex -lt $subCount; $nameIndex++) {
                $nameEntry = $typeSubdir + 16 + $nameIndex * 8
                $nameId = Get-UInt32 $bytes $nameEntry
                $nameSubdir = $resourceRootOffset + ($nameId -band 0x7FFFFFFF)
                $languageCount = Get-UInt16 $bytes ($nameSubdir + 12)
                for ($languageIndex = 0; $languageIndex -lt $languageCount; $languageIndex++) {
                    $languageEntry = $nameSubdir + 16 + $languageIndex * 8
                    $languageId = Get-UInt32 $bytes $languageEntry
                    $dataEntry = $resourceRootOffset + ($languageId -band 0x7FFFFFFF)
                    $dataRva = Get-UInt32 $bytes $dataEntry
                    $dataSize = Get-UInt32 $bytes ($dataEntry + 4)
                    $resourceLines.Add($typeText + ' 名称=' + ($nameId -band 0x7FFFFFFF) + ' 语言=' + ($languageId -band 0x7FFFFFFF) + ' 数据RVA=' + (Get-Hex $dataRva) + ' 大小=' + $dataSize)
                    if (-not $typeIsName -and $typeValue -eq 24 -and $manifestOffset -lt 0) {
                        $manifestOffset = Convert-RvaToOffset $dataRva
                        $manifestSize = $dataSize
                    }
                }
            }
        }
    }
    $resourceLines | Out-File -LiteralPath (Join-Path $OutDir 'resources.txt') -Encoding utf8
    if ($manifestOffset -ge 0 -and $manifestSize -gt 0) {
        $manifestBytes = New-Object byte[] $manifestSize
        [Array]::Copy($bytes, $manifestOffset, $manifestBytes, 0, $manifestSize)
        [System.IO.File]::WriteAllBytes((Join-Path $OutDir 'manifest.xml'), $manifestBytes)
        Write-Log ('清单资源已提取 文件偏移=' + (Get-Hex $manifestOffset) + ' 大小=' + $manifestSize)
    }

    # 版本信息
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($TargetPath)
    $versionLines = New-Object System.Collections.Generic.List[string]
    $versionLines.Add('FileVersion=' + $versionInfo.FileVersion)
    $versionLines.Add('ProductVersion=' + $versionInfo.ProductVersion)
    $versionLines.Add('FileDescription=' + $versionInfo.FileDescription)
    $versionLines.Add('CompanyName=' + $versionInfo.CompanyName)
    $versionLines.Add('ProductName=' + $versionInfo.ProductName)
    $versionLines.Add('LegalCopyright=' + $versionInfo.LegalCopyright)
    $versionLines.Add('OriginalFilename=' + $versionInfo.OriginalFilename)
    $versionLines.Add('InternalName=' + $versionInfo.InternalName)
    $versionLines.Add('Comments=' + $versionInfo.Comments)
    $versionLines | Out-File -LiteralPath (Join-Path $OutDir 'version-info.txt') -Encoding utf8
    Write-Log ('版本信息 文件版本=' + $versionInfo.FileVersion + ' 产品版本=' + $versionInfo.ProductVersion + ' 描述=' + $versionInfo.FileDescription + ' 公司=' + $versionInfo.CompanyName)

    $peInfo | Out-File -LiteralPath (Join-Path $OutDir 'pe-info.md') -Encoding utf8

    # 字符串扫描
    if (-not $SkipStrings) {
        $asciiLines = New-Object System.Collections.Generic.List[string]
        $utf16Lines = New-Object System.Collections.Generic.List[string]
        $asciiLength = 0
        $asciiStart = 0
        for ($offset = 0; $offset -lt $bytes.Length; $offset++) {
            $value = $bytes[$offset]
            $isPrintable = ($value -ge 0x20 -and $value -le 0x7E)
            if ($isPrintable) {
                if ($asciiLength -eq 0) { $asciiStart = $offset }
                $asciiLength++
            } else {
                if ($asciiLength -ge 5) {
                    $text = [System.Text.Encoding]::ASCII.GetString($bytes, $asciiStart, $asciiLength)
                    $asciiLines.Add((Get-Hex $asciiStart) + ' ' + $text)
                }
                $asciiLength = 0
            }
        }
        $asciiLines | Out-File -LiteralPath (Join-Path $OutDir 'strings-ascii.txt') -Encoding utf8
        Write-Log ('ASCII 字符串扫描完成 条数=' + $asciiLines.Count)

        $utf16Length = 0
        $utf16Start = 0
        for ($offset = 0; $offset -lt ($bytes.Length - 1); $offset += 2) {
            $isAsciiChar = ($bytes[$offset] -ge 0x20 -and $bytes[$offset] -le 0x7E -and $bytes[$offset + 1] -eq 0)
            $isWideChar = ($bytes[$offset] -ge 0x00 -and $bytes[$offset + 1] -ge 0x04 -and $bytes[$offset + 1] -le 0x9F)
            if ($isAsciiChar -or $isWideChar) {
                if ($utf16Length -eq 0) { $utf16Start = $offset }
                $utf16Length += 2
            } else {
                if ($utf16Length -ge 8) {
                    $text = [System.Text.Encoding]::Unicode.GetString($bytes, $utf16Start, $utf16Length)
                    $utf16Lines.Add((Get-Hex $utf16Start) + ' ' + $text)
                }
                $utf16Length = 0
            }
        }
        $utf16Lines | Out-File -LiteralPath (Join-Path $OutDir 'strings-utf16.txt') -Encoding utf8
        Write-Log ('UTF16 字符串扫描完成 条数=' + $utf16Lines.Count)

        # 关键字命中
        $keywordGroups = [ordered]@{
            '滚动截图入口' = @('ScrollingCapture', 'Scrolling Window', 'Scroll Capture', 'Capture Scrolling', 'AutoScroll', 'Auto Scroll',
                '滚动', '长截图', '滚动截图', 'CaptureScrollingWindow', 'ScrollingWindow', 'scrolling')
            '滚动条与滚动位置' = @('GetScrollInfo', 'GetScrollPos', 'GetScrollRange', 'SetScrollPos', 'ScrollBar', 'SIF_ALL', 'SIF_POS',
                'SB_VERT', 'SB_HORZ', 'WS_VSCROLL', 'VSCROLL', 'HSCROLL', 'ScrollInfo', 'SCROLLINFO')
            '滚动消息注入' = @('WM_MOUSEWHEEL', 'WM_VSCROLL', 'WM_HSCROLL', 'WM_KEYDOWN', 'WM_SYSKEYDOWN', 'PostMessage', 'SendMessage',
                'mouse_event', 'SendInput', 'SetCursorPos', 'PageDown', 'VK_NEXT', 'VK_PRIOR', 'WM_COMMAND')
            'UI 自动化' = @('UIAutomation', 'IUIAutomation', 'ScrollPattern', 'ScrollItemPattern', 'TextPattern', 'AccessibleObject',
                'IAccessible', 'MSAA', 'oleacc', 'UiaCoreApi', 'UIAutomationCore')
            '图像匹配与拼接' = @('overlap', 'Overlap', 'template', 'Template', 'matchTemplate', 'MatchTemplate', 'cross correlation',
                'Correlation', 'PhaseCorrelation', 'SSIM', 'similar', 'Similarity', 'stitch', 'Stitch', 'Stitching',
                'join', 'Join', 'Merge', 'merge', 'Align', 'align', 'offset', 'Offset', 'diff', 'Diff')
            '固定区域与浮动元素' = @('sticky', 'Sticky', 'fixed header', 'FixedHeader', 'footer', 'Footer', 'header', 'Header',
                'floating', 'Floating', 'pin', 'Pin')
            '限制与重试' = @('maxHeight', 'MaxHeight', 'MaxFrames', 'MaxFrame', 'timeout', 'Timeout', 'retry', 'Retry',
                'attempt', 'Attempt', 'delay', 'Delay', 'wait', 'Wait', 'interval', 'Interval')
            '图像编解码' = @('GDI+', 'gdiplus', 'BitBlt', 'CreateCompatibleBitmap', 'GetDIBits', 'PrintWindow', 'StretchBlt',
                'CImage', 'OpenCV', 'cv::', 'opencv')
            '窗口与进程' = @('EnumWindows', 'EnumChildWindows', 'GetWindowRect', 'GetClientRect', 'GetClassName', 'GetWindowText',
                'WindowFromPoint', 'ChildWindowFromPoint', 'FindWindow', 'GetWindowThreadProcessId', 'DwmGetWindowAttribute')
        }
        $hitLines = New-Object System.Collections.Generic.List[string]
        $hitLines.Add('# 关键字命中')
        $hitLines.Add('')
        $hitLines.Add('说明：ASCII 与 UTF16 字符串表全量正则检索，行尾标注出现次数与首个偏移。')
        $hitLines.Add('')
        foreach ($groupName in $keywordGroups.Keys) {
            $hitLines.Add('## ' + $groupName)
            $hitLines.Add('')
            $hitLines.Add('| 关键字 | 命中次数 | 首个偏移 | 示例 |')
            $hitLines.Add('| --- | --- | --- | --- |')
            foreach ($keyword in $keywordGroups[$groupName]) {
                $asciiMatches = @($asciiLines | Where-Object { $_ -like ('*' + $keyword + '*') })
                $utf16Matches = @($utf16Lines | Where-Object { $_ -like ('*' + $keyword + '*') })
                $total = $asciiMatches.Count + $utf16Matches.Count
                if ($total -gt 0) {
                    $firstOffset = ''
                    $example = ''
                    if ($asciiMatches.Count -gt 0) {
                        $firstOffset = ($asciiMatches[0] -split ' ')[0] + '（ASCII）'
                        $example = $asciiMatches[0].Substring([Math]::Min($asciiMatches[0].Length, 60))
                    } elseif ($utf16Matches.Count -gt 0) {
                        $firstOffset = ($utf16Matches[0] -split ' ')[0] + '（UTF16）'
                        $example = $utf16Matches[0].Substring([Math]::Min($utf16Matches[0].Length, 60))
                    }
                    $example = $example -replace '\|', '/'
                    $hitLines.Add('| ' + $keyword + ' | ' + $total + ' | ' + $firstOffset + ' | ' + $example + ' |')
                }
            }
            $hitLines.Add('')
        }
        $hitLines | Out-File -LiteralPath (Join-Path $OutDir 'keyword-hits.md') -Encoding utf8
        Write-Log '关键字命中统计已写入 keyword-hits.md'
    }

    $summary = [ordered]@{
        TargetPath = $TargetPath
        FileSize = $targetItem.Length
        Machine = $machineText
        IsManaged = ($clrRva -ne 0)
        TimeStamp = $timeStamp
        CompileTime = [DateTimeOffset]::FromUnixTimeSeconds([int64]$timeStamp).ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')
        Sections = $script:Sections.Count
        ImportedDlls = $dllCount
        ImportedFunctions = $functionCount
        FileVersion = $versionInfo.FileVersion
        CompanyName = $versionInfo.CompanyName
    }
    $summary | ConvertTo-Json | Out-File -LiteralPath (Join-Path $OutDir 'static-probe-summary.json') -Encoding utf8

    Write-Log '静态取证完成'
    Set-Content -LiteralPath $script:ExitCodePath -Value 0 -Encoding utf8
    exit 0
} catch {
    $message = $_.Exception.Message
    Write-Log ('静态取证失败 原因=' + $message)
    Set-Content -LiteralPath $script:ExitCodePath -Value 1 -Encoding utf8
    exit 1
}
