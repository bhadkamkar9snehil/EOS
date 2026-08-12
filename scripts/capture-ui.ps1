param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\visual')
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$executable = Join-Path $projectRoot 'src\EngineeringPerformance.DesktopHost\bin\Release\net10.0-windows10.0.19041.0\win-x64\EngineeringPerformance.DesktopHost.exe'

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Release executable not found at '$executable'. Build the complete solution in Release first."
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

Write-Host "=== EOS desktop visual capture ==="
Write-Host "Executable: $executable"
Write-Host "Evidence:   $output"
Write-Host "Session:    $((Get-Process -Id $PID).SessionId)"
Write-Host "Identity:   $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"

$previousCapture = $env:EOS_VISUAL_CAPTURE
$previousOutput = $env:EOS_VISUAL_OUTPUT
try {
    $env:EOS_VISUAL_CAPTURE = '1'
    $env:EOS_VISUAL_OUTPUT = $output

    $process = Start-Process -FilePath $executable -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        $failure = Join-Path $output 'capture-failure.txt'
        $startupFailure = Join-Path $output 'startup-failure.txt'
        if (Test-Path $failure) { Get-Content $failure -Raw | Write-Host }
        if (Test-Path $startupFailure) { Get-Content $startupFailure -Raw | Write-Host }
        throw "Desktop visual capture exited with code $($process.ExitCode)."
    }

    $reportPath = Join-Path $output 'visual-report.json'
    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw "Desktop process exited successfully but did not create visual-report.json."
    }

    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $screenshots = @(Get-ChildItem -LiteralPath $output -Filter '*.png' -File)
    if ($screenshots.Count -lt 10) {
        throw "Expected at least 10 screenshots; found $($screenshots.Count)."
    }

    $records = @($report.records)
    $browserErrors = @($records | ForEach-Object { @($_.errors) } | Where-Object { $_ })
    $overflow = @($records | Where-Object { $_.horizontalOverflow -or $_.clippedPlateCount -gt 0 })
    if ($browserErrors.Count -gt 0) {
        throw "Browser diagnostics captured $($browserErrors.Count) JavaScript/console errors."
    }
    if ($overflow.Count -gt 0) {
        $routes = ($overflow | ForEach-Object { "$($_.route)/$($_.theme)/$($_.requestedWidth)x$($_.requestedHeight)" }) -join ', '
        throw "Visual layout diagnostics found horizontal clipping/overflow: $routes"
    }

    Write-Host "Visual capture passed: $($screenshots.Count) real WebView2 screenshots and diagnostics were produced."
}
finally {
    $env:EOS_VISUAL_CAPTURE = $previousCapture
    $env:EOS_VISUAL_OUTPUT = $previousOutput
}
