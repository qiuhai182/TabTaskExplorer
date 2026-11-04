[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Windows.Forms
$dataObject = [System.Windows.Forms.Clipboard]::GetDataObject()
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
if ($null -eq $dataObject) {
    $lines.Add('剪贴板为空')
} else {
    $lines.Add('剪贴板格式清单：')
    foreach ($format in $dataObject.GetFormats($true)) {
        $valueDescription = '（无内容）'
        try {
            $value = $dataObject.GetData($format)
            if ($null -ne $value) {
                $valueDescription = $value.GetType().FullName
                if ($value -is [string]) {
                    $valueDescription = $valueDescription + ' 长度=' + $value.Length
                }
            }
        } catch {
            $valueDescription = '读取异常'
        }
        $lines.Add('  格式=' + $format + ' 类型=' + $valueDescription)
    }
    $image = [System.Windows.Forms.Clipboard]::GetImage()
    if ($null -eq $image) {
        $lines.Add('GetImage 结果=空')
    } else {
        $lines.Add('GetImage 结果=非空 宽度=' + $image.Width + ' 高度=' + $image.Height)
        $pixel = $image.GetPixel([Math]::Min(3, $image.Width - 1), [Math]::Min(3, $image.Height - 1))
        $lines.Add('左上角像素 ARGB=' + $pixel.ToArgb())
    }
    $files = [System.Windows.Forms.Clipboard]::GetFileDropList()
    $lines.Add('文件列表数量=' + $files.Count)
    foreach ($file in $files) {
        $lines.Add('  文件=' + $file)
    }
}
$lines | Out-File -LiteralPath 'D:\TabTaskExplorer\output\clipboard-formats.txt' -Encoding utf8
