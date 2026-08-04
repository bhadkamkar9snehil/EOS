param([int]$ObservationSeconds = 15)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'src\EngineeringPerformance.DesktopHost\bin\Release\net10.0-windows10.0.19041.0\win-x64\EngineeringPerformance.DesktopHost.exe'
$readyMarker = Join-Path $env:LOCALAPPDATA 'EngineeringPerformance\ui-ready.marker'

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Release executable not found. Run dotnet build -c Release first."
}

Remove-Item -LiteralPath $readyMarker -ErrorAction SilentlyContinue
$process = Start-Process -FilePath $executable -PassThru
try {
    $deadline = (Get-Date).AddSeconds($ObservationSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) {
            throw "Startup smoke test failed: the desktop process exited with code $($process.ExitCode)."
        }
    } while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        throw "Startup smoke test failed: the process remained alive but did not create a visible main window."
    }

    $renderDeadline = (Get-Date).AddSeconds($ObservationSeconds)
    while (-not (Test-Path -LiteralPath $readyMarker) -and (Get-Date) -lt $renderDeadline) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $readyMarker)) {
        throw "Startup smoke test failed: the window opened but the Blazor dashboard did not render."
    }
    Write-Output "Startup smoke test passed: a visible window and rendered dashboard were confirmed."
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit()
    }
}
