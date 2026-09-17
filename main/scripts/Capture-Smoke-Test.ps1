param([string]$AppPath = (Join-Path $PSScriptRoot '../ZZZBuffTracker.App/bin/Debug/net10.0-windows10.0.19041.0/ZZZBuffTracker.App.exe'))
$ErrorActionPreference = 'Stop'
$artifact = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../artifacts/capture-' + [guid]::NewGuid().ToString('N'))))
$process = Start-Process -FilePath $AppPath -ArgumentList @('--capture-smoke', ('"' + $artifact + '"')) -PassThru -WindowStyle Hidden
try {
    if (-not $process.WaitForExit(30000)) { throw 'Capture smoke timed out.' }
    $report = Get-Content -LiteralPath (Join-Path $artifact 'result.json') -Raw | ConvertFrom-Json
    if (-not $report.Passed) { throw $report.Error }
    Write-Output ($report | ConvertTo-Json)
    Write-Output "PASS: WGC frames, template event, resize, target close. Artifacts: $artifact"
} finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id } }
