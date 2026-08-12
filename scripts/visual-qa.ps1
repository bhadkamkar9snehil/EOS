param(
    [string]$OutputDirectory = "",
    [string]$FixtureDirectory = ""
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $base = if ($env:AGENT_TEMPDIRECTORY) { $env:AGENT_TEMPDIRECTORY } else { [System.IO.Path]::GetTempPath() }
    $OutputDirectory = Join-Path $base 'EOSVisualQA'
}
if ([string]::IsNullOrWhiteSpace($FixtureDirectory)) {
    $FixtureDirectory = Join-Path $projectRoot 'docs\sample-data'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$FixtureDirectory = [System.IO.Path]::GetFullPath($FixtureDirectory)
$executable = Join-Path $projectRoot 'src\EngineeringPerformance.DesktopHost\bin\Release\net10.0-windows10.0.19041.0\win-x64\EngineeringPerformance.DesktopHost.exe'
$dataDirectory = Join-Path ([System.IO.Path]::GetDirectoryName($OutputDirectory)) 'EOSVisualQAData'

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Release executable not found: $executable. Build the complete solution in Release first."
}
if (-not (Test-Path -LiteralPath $FixtureDirectory)) {
    throw "Visual fixture directory not found: $FixtureDirectory"
}

Remove-Item -LiteralPath $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $dataDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

$captures = @(
    @{ Name = 'overview-light';       Route = '/overview';              Theme = 'light' },
    @{ Name = 'overview-dark';        Route = '/overview';              Theme = 'dark'  },
    @{ Name = 'timesheets-light';     Route = '/timesheets';            Theme = 'light' },
    @{ Name = 'peer-insights-light';  Route = '/peer-insights';         Theme = 'light' },
    @{ Name = 'employee-asha-light';  Route = '/employee/Asha%20Kapoor'; Theme = 'light' },
    @{ Name = 'employees-light';      Route = '/employees';             Theme = 'light' },
    @{ Name = 'imports-light';        Route = '/imports';               Theme = 'light' }
)

$manifest = [ordered]@{
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    machine = $env:COMPUTERNAME
    commit = $env:BUILD_SOURCEVERSION
    viewport = @{ width = 1600; height = 1100 }
    captures = @()
}

try {
    foreach ($capture in $captures) {
        $png = Join-Path $OutputDirectory ($capture.Name + '.png')
        Write-Host "Capturing $($capture.Route) [$($capture.Theme)] -> $png"

        $env:EOS_DATA_DIRECTORY = $dataDirectory
        $env:EOS_VISUAL_FIXTURE_DIR = $FixtureDirectory
        $env:EOS_VISUAL_CAPTURE_FILE = $png
        $env:EOS_VISUAL_ROUTE = $capture.Route
        $env:EOS_VISUAL_THEME = $capture.Theme
        $env:EOS_VISUAL_WIDTH = '1600'
        $env:EOS_VISUAL_HEIGHT = '1100'

        $process = Start-Process -FilePath $executable -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            throw "Visual capture '$($capture.Name)' failed with exit code $($process.ExitCode)."
        }
        if (-not (Test-Path -LiteralPath $png)) {
            throw "Visual capture '$($capture.Name)' reported success but did not produce $png."
        }

        $manifest.captures += [ordered]@{
            name = $capture.Name
            route = $capture.Route
            theme = $capture.Theme
            image = [System.IO.Path]::GetFileName($png)
            diagnostics = [System.IO.Path]::GetFileName([System.IO.Path]::ChangeExtension($png, '.json'))
        }
    }
}
finally {
    'EOS_DATA_DIRECTORY','EOS_VISUAL_FIXTURE_DIR','EOS_VISUAL_CAPTURE_FILE','EOS_VISUAL_ROUTE','EOS_VISUAL_THEME','EOS_VISUAL_WIDTH','EOS_VISUAL_HEIGHT' |
        ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }

    $logDirectory = Join-Path $dataDirectory 'logs'
    if (Test-Path -LiteralPath $logDirectory) {
        Copy-Item -LiteralPath $logDirectory -Destination (Join-Path $OutputDirectory 'logs') -Recurse -Force
    }

    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8
}

Write-Host "Visual QA bundle: $OutputDirectory"
Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse |
    Select-Object FullName, Length |
    Format-Table -AutoSize
