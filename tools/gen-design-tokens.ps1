#requires -Version 5.1
# 界面令牌生成器：从 design/tokens/ui-tokens.json 生成
#   1) src/TabTaskExplorer.App/Themes/Tokens.axaml   （桌面 Avalonia 资源字典：颜色深浅两套 + 尺寸/密度单套）
#   2) src/TabTaskExplorer.App/Assets/web/tokens.css （网页端 CSS 变量）
#   3) WebPageHtml.cs 内联回退样式里的颜色令牌块（TTE-TOKENS-BEGIN / TTE-TOKENS-END 之间）
#      回退页只用颜色变量，尺寸沿用固定 px，故注入时只同步颜色块，避免改动该文件；
#      尺寸令牌的消费方是 Assets/web/app.css，下一轮替换时使用。
# 生成物入库，修改令牌后重新运行本脚本；不要手改生成物。

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tokensPath = Join-Path $root 'design\tokens\ui-tokens.json'
$axamlPath = Join-Path $root 'src\TabTaskExplorer.App\Themes\Tokens.axaml'
$cssPath = Join-Path $root 'src\TabTaskExplorer.App\Assets\web\tokens.css'
$pagePath = Join-Path $root 'src\TabTaskExplorer.App\Services\WebPageHtml.cs'
$newLine = "`r`n"
$utf8 = New-Object System.Text.UTF8Encoding($false)
# C# 源文件保留 BOM：与仓库其余 .cs 文件一致，也避免 PowerShell 5.1 以 ANSI 读取时中文乱码
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

if (-not (Test-Path -LiteralPath $tokensPath)) {
    throw "找不到令牌文件：$tokensPath"
}

$tokens = Get-Content -LiteralPath $tokensPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($tokens.color.PSObject.Properties)
$sizeEntries = @($tokens.size.PSObject.Properties)

# 校验颜色：每个令牌必须同时给出 light / dark 与 avalonia 键名
foreach ($entry in $entries) {
    $value = $entry.Value
    foreach ($field in @('light', 'dark', 'avalonia')) {
        $current = [string]$value.$field
        if ([string]::IsNullOrWhiteSpace($current)) {
            throw "颜色令牌 $($entry.Name) 缺少字段 $field"
        }
    }
    foreach ($variant in @('light', 'dark')) {
        $hex = [string]$value.$variant
        if ($hex -notmatch '^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$') {
            throw "颜色令牌 $($entry.Name) 的 $variant 值不是合法颜色：$hex"
        }
    }
}

# 校验尺寸：type 只能取 length / thickness / cornerRadius，且必须给出 avalonia 键名
foreach ($entry in $sizeEntries) {
    $value = $entry.Value
    $type = [string]$value.type
    if ([string]::IsNullOrWhiteSpace([string]$value.avalonia)) {
        throw "尺寸令牌 $($entry.Name) 缺少字段 avalonia"
    }
    switch ($type) {
        'length' {
            $number = 0.0
            if (-not [double]::TryParse([string]$value.value, [ref]$number)) {
                throw "尺寸令牌 $($entry.Name) 的 value 不是数字：$($value.value)"
            }
        }
        'cornerRadius' {
            $number = 0.0
            if (-not [double]::TryParse([string]$value.value, [ref]$number)) {
                throw "尺寸令牌 $($entry.Name) 的 value 不是数字：$($value.value)"
            }
        }
        'thickness' {
            $parts = @($value.value)
            if ($parts.Count -ne 2) {
                throw "尺寸令牌 $($entry.Name) 的 thickness 必须是 [水平, 垂直] 两个数字"
            }
            foreach ($part in $parts) {
                $number = 0.0
                if (-not [double]::TryParse([string]$part, [ref]$number)) {
                    throw "尺寸令牌 $($entry.Name) 的 thickness 含非数字项：$part"
                }
            }
        }
        default {
            throw "尺寸令牌 $($entry.Name) 的 type 非法：$type（只支持 length / thickness / cornerRadius）"
        }
    }
}

function ConvertTo-CssVariable([string]$name) {
    $kebab = [regex]::Replace($name, '([a-z0-9])([A-Z])', '$1-$2')
    return '--tte-' + $kebab.ToLowerInvariant()
}

function Format-Number($value) {
    $number = [double]$value
    if ($number -eq [math]::Floor($number)) {
        return ([int]$number).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }
    return $number.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

# ---------- 1) Avalonia 资源字典 ----------
$axaml = New-Object System.Text.StringBuilder
[void]$axaml.Append('<ResourceDictionary xmlns="https://github.com/avaloniaui"')
[void]$axaml.Append($newLine)
[void]$axaml.Append('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"')
[void]$axaml.Append($newLine)
[void]$axaml.Append('                    xmlns:sys="using:System">')
[void]$axaml.Append($newLine).Append($newLine)
[void]$axaml.Append('  <!-- 由 tools/gen-design-tokens.ps1 从 design/tokens/ui-tokens.json 生成，请勿手工编辑。 -->')
[void]$axaml.Append($newLine)
[void]$axaml.Append('  <!-- 尺寸 / 密度：与主题无关，深浅两套共用。 -->')
[void]$axaml.Append($newLine)
foreach ($entry in $sizeEntries) {
    $value = $entry.Value
    $type = [string]$value.type
    $key = [string]$value.avalonia
    if ($type -eq 'length') {
        [void]$axaml.Append('  <sys:Double x:Key="' + $key + '">' + (Format-Number $value.value) + '</sys:Double>')
    }
    elseif ($type -eq 'cornerRadius') {
        [void]$axaml.Append('  <CornerRadius x:Key="' + $key + '">' + (Format-Number $value.value) + '</CornerRadius>')
    }
    else {
        $parts = @($value.value)
        [void]$axaml.Append('  <Thickness x:Key="' + $key + '">' + (Format-Number $parts[0]) + ',' + (Format-Number $parts[1]) + '</Thickness>')
    }
    [void]$axaml.Append($newLine)
}
[void]$axaml.Append($newLine)
[void]$axaml.Append('  <ResourceDictionary.ThemeDictionaries>')
[void]$axaml.Append($newLine)
foreach ($variant in @('Dark', 'Light')) {
    [void]$axaml.Append($newLine)
    [void]$axaml.Append('    <ResourceDictionary x:Key="' + $variant + '">')
    [void]$axaml.Append($newLine)
    foreach ($entry in $entries) {
        $hex = [string]$entry.Value.$($variant.ToLowerInvariant())
        [void]$axaml.Append('      <SolidColorBrush x:Key="' + $entry.Value.avalonia + '" Color="' + $hex + '"/>')
        [void]$axaml.Append($newLine)
    }
    [void]$axaml.Append('    </ResourceDictionary>')
    [void]$axaml.Append($newLine)
}
[void]$axaml.Append('  </ResourceDictionary.ThemeDictionaries>')
[void]$axaml.Append($newLine)
[void]$axaml.Append('</ResourceDictionary>')
[void]$axaml.Append($newLine)

# ---------- 2) 网页端 CSS 变量 ----------
# 颜色块单独成串：既要写进 tokens.css，也要注入 WebPageHtml.cs 的回退页
$colorCss = New-Object System.Text.StringBuilder
[void]$colorCss.Append('/* 由 tools/gen-design-tokens.ps1 从 design/tokens/ui-tokens.json 生成，请勿手工编辑。 */')
[void]$colorCss.Append($newLine)
[void]$colorCss.Append(':root {')
[void]$colorCss.Append($newLine)
foreach ($entry in $entries) {
    [void]$colorCss.Append('  ' + (ConvertTo-CssVariable $entry.Name) + ': ' + [string]$entry.Value.light + ';')
    [void]$colorCss.Append($newLine)
}
[void]$colorCss.Append('}')
[void]$colorCss.Append($newLine).Append($newLine)
[void]$colorCss.Append('body.dark {')
[void]$colorCss.Append($newLine)
foreach ($entry in $entries) {
    [void]$colorCss.Append('  ' + (ConvertTo-CssVariable $entry.Name) + ': ' + [string]$entry.Value.dark + ';')
    [void]$colorCss.Append($newLine)
}
[void]$colorCss.Append('}')
[void]$colorCss.Append($newLine)

$css = New-Object System.Text.StringBuilder
[void]$css.Append('/* 由 tools/gen-design-tokens.ps1 从 design/tokens/ui-tokens.json 生成，请勿手工编辑。 */')
[void]$css.Append($newLine)
[void]$css.Append(':root {')
[void]$css.Append($newLine)
foreach ($entry in $entries) {
    [void]$css.Append('  ' + (ConvertTo-CssVariable $entry.Name) + ': ' + [string]$entry.Value.light + ';')
    [void]$css.Append($newLine)
}
[void]$css.Append($newLine)
[void]$css.Append('  /* 尺寸 / 密度：与主题无关。 */')
[void]$css.Append($newLine)
foreach ($entry in $sizeEntries) {
    $value = $entry.Value
    $type = [string]$value.type
    $variable = ConvertTo-CssVariable $entry.Name
    if ($type -eq 'thickness') {
        $parts = @($value.value)
        # Avalonia Thickness(水平, 垂直) 对应 CSS 简写「上下 左右」
        $text = (Format-Number $parts[1]) + 'px ' + (Format-Number $parts[0]) + 'px'
    }
    else {
        $text = (Format-Number $value.value) + 'px'
    }
    [void]$css.Append('  ' + $variable + ': ' + $text + ';')
    [void]$css.Append($newLine)
}
[void]$css.Append('}')
[void]$css.Append($newLine).Append($newLine)
[void]$css.Append('body.dark {')
[void]$css.Append($newLine)
foreach ($entry in $entries) {
    [void]$css.Append('  ' + (ConvertTo-CssVariable $entry.Name) + ': ' + [string]$entry.Value.dark + ';')
    [void]$css.Append($newLine)
}
[void]$css.Append('}')
[void]$css.Append($newLine)

$axamlDir = Split-Path -Parent $axamlPath
if (-not (Test-Path -LiteralPath $axamlDir)) {
    New-Item -ItemType Directory -Path $axamlDir -Force | Out-Null
}
[System.IO.File]::WriteAllText($axamlPath, $axaml.ToString(), $utf8)
[System.IO.File]::WriteAllText($cssPath, $css.ToString(), $utf8)

# ---------- 3) 内联回退页的颜色令牌块 ----------
$beginMarker = 'TTE-TOKENS-BEGIN'
$endMarker = 'TTE-TOKENS-END'
$lines = [System.IO.File]::ReadAllLines($pagePath, [System.Text.Encoding]::UTF8)
$beginIndex = -1
$endIndex = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i].IndexOf($beginMarker, [System.StringComparison]::Ordinal) -ge 0) { $beginIndex = $i }
    if ($lines[$i].IndexOf($endMarker, [System.StringComparison]::Ordinal) -ge 0) { $endIndex = $i }
}
if ($beginIndex -lt 0 -or $endIndex -lt 0 -or $endIndex -le $beginIndex) {
    throw "WebPageHtml.cs 缺少令牌同步标记：$beginMarker / $endMarker"
}

$inlined = New-Object System.Collections.Generic.List[string]
foreach ($line in ($colorCss.ToString() -split "`r?`n")) {
    if ($line.Length -eq 0) {
        $inlined.Add('')
    }
    else {
        $inlined.Add('  ' + $line)
    }
}
$result = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -le $beginIndex; $i++) { $result.Add($lines[$i]) }
foreach ($line in $inlined) { $result.Add($line) }
for ($i = $endIndex; $i -lt $lines.Length; $i++) { $result.Add($lines[$i]) }
[System.IO.File]::WriteAllText($pagePath, ($result -join $newLine) + $newLine, $utf8Bom)

Write-Host ("令牌生成完成：{0} 个颜色 × 2 套主题，{1} 个尺寸" -f $entries.Count, $sizeEntries.Count)
Write-Host ("  " + $axamlPath)
Write-Host ("  " + $cssPath)
Write-Host ("  " + $pagePath + " 内联颜色令牌块")
