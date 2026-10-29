param(
    [Parameter(Mandatory = $true)]
    [string] $ExePath,

    [int] $WaitSeconds = 6,

    [switch] $KeepRunning
)

$ErrorActionPreference = "Stop"

$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
$existing = @(Get-Process -Name WithWindows -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    throw "A WithWindows process is already running: $($existing.Id -join ', ')."
}

$logPath = Join-Path $env:APPDATA "WithWindows\log.txt"
$startedAt = Get-Date

$process = Start-Process `
    -FilePath $resolvedExe `
    -ArgumentList "--tray" `
    -WorkingDirectory (Split-Path -Parent $resolvedExe) `
    -PassThru

try {
    $deadline = $startedAt.AddSeconds($WaitSeconds)
    $newLines = @()
    $sawTrayStartup = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()

        if ($process.HasExited) {
            throw "The --tray process exited with code $($process.ExitCode)."
        }

        if (Test-Path -LiteralPath $logPath) {
            $newLines = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -Tail 100 |
                Where-Object {
                    if ($_ -match '^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') {
                        [datetime]::ParseExact(
                            $Matches.time,
                            'yyyy-MM-dd HH:mm:ss',
                            [System.Globalization.CultureInfo]::InvariantCulture) -ge $startedAt.AddSeconds(-1)
                    }
                    else {
                        $false
                    }
                })
        }

        if ($newLines -match '\u672A\u5904\u7406\u5F02\u5E38') {
            throw "The --tray startup logged an unhandled exception."
        }

        if ($newLines -match '\u4EE5 --tray \u542F\u52A8\uFF1A\u9690\u85CF\u5230\u6258\u76D8') {
            $sawTrayStartup = $true
        }
    }

    if (-not $sawTrayStartup) {
        throw "No --tray startup log appeared within $WaitSeconds seconds."
    }

    Write-Output "--tray startup regression passed: PID $($process.Id) is still running."
}
finally {
    if (-not $KeepRunning -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}
