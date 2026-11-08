param(
    [string] $SvgPath = (Join-Path $PSScriptRoot "..\assets\icon.svg"),
    [string] $IcoPath = (Join-Path $PSScriptRoot "..\src\WithWindows.App\Assets\with-windows.ico")
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$SvgPath = (Resolve-Path $SvgPath).Path
$IcoPath = [IO.Path]::GetFullPath($IcoPath)
$readmeImage = Join-Path $root "docs\images\with-windows.png"
$inkscape = Get-Command inkscape -ErrorAction Stop

& $inkscape.Source $SvgPath `
    --export-type=png `
    "--export-filename=$readmeImage" `
    --export-width=256 `
    --export-height=256

if ($LASTEXITCODE -ne 0) {
    throw "Inkscape 导出 PNG 失败（退出码 $LASTEXITCODE）"
}

& dotnet run --project (Join-Path $root "scripts\IconGen\IconGen.csproj") -- $readmeImage $IcoPath
if ($LASTEXITCODE -ne 0) {
    throw "ICO 生成失败（退出码 $LASTEXITCODE）"
}

Write-Output "已更新：$readmeImage"
Write-Output "已更新：$IcoPath"
