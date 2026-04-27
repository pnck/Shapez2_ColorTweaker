param(
    [Parameter(Mandatory = $true)]
    [string]$ContentPath
)

$CurrentDir = (Get-Location).Path
$PreviewImg  = "$CurrentDir\Steam\preview.png"


$ContentPath = $ContentPath.Trim('"').TrimEnd('\', '/') -replace '/', '\'


Write-Host "CONTENT_PATH: $ContentPath"
Write-Host "PREVIEW_IMG:  $PreviewImg"

$TmpVdfPath  = "$CurrentDir\Steam\base.tmp.vdf"
$BaseVdfPath = "$CurrentDir\Steam\base.vdf"


$VdfContent = (Get-Content $BaseVdfPath -Raw)
$VdfContent = $VdfContent.Replace('${CONTENT_PATH}', $ContentPath).Replace('$CONTENT_PATH', $ContentPath)
$VdfContent = $VdfContent.Replace('${PREVIEW_IMG}',  $PreviewImg ).Replace('$PREVIEW_IMG',  $PreviewImg)
[System.IO.File]::WriteAllText($TmpVdfPath, $VdfContent)

Write-Host $VdfContent

& steamcmd +login pnckxx +workshop_build_item $TmpVdfPath +quit

$VdfAfter = Get-Content $TmpVdfPath -Raw
Write-Host $VdfAfter

$Match  = [regex]::Match($VdfAfter, '"publishedfileid"\s+"(\d+)"')
$FileId = $Match.Groups[1].Value
Write-Host "New published file ID: $FileId"

if ($FileId) {
    $BaseVdf = (Get-Content $BaseVdfPath -Raw)
    $BaseVdf = [regex]::Replace($BaseVdf, '("publishedfileid"\s+")[0-9]+"', '${1}' + $FileId + '"')
    [System.IO.File]::WriteAllText($BaseVdfPath, $BaseVdf)
}

Remove-Item $TmpVdfPath