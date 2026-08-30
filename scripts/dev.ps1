# 开发辅助：构建并重启 WithWindows（Debug），省去手工三步
# 用法：powershell -ExecutionPolicy Bypass -File scripts/dev.ps1
Set-Location (Join-Path $PSScriptRoot "..")

# 1. 停掉运行中的实例（否则 exe 被锁无法构建）
taskkill /IM WithWindows.exe /F 2>$null | Out-Null

# 2. 增量构建（只编译改动部分）
dotnet build src/WithWindows.UI/WithWindows.UI.csproj -p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 3. 启动
$exe = "src/WithWindows.UI/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/WithWindows.exe"
Start-Process -FilePath $exe
Write-Host "已启动（Debug）"
