[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('EOS', 'APS')]
    [string]$Repository,

    [Parameter(Mandatory)]
    [string]$SourceRoot,

    [string]$Configuration = 'Release',

    [Parameter(Mandatory)]
    [string]$ResultsDirectory,

    [Parameter(Mandatory)]
    [string]$ArtifactDirectory,

    [switch]$RunVisualValidation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][scriptblock]$Command
    )

    Write-Host "`n=== $Label ===" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Invoke-BuildWithDiagnostics {
    param(
        [Parameter(Mandatory)][string]$Solution,
        [Parameter(Mandatory)][string]$LogPath
    )

    Write-Host "`n=== Build $Solution ===" -ForegroundColor Cyan
    dotnet build $Solution --configuration $Configuration --no-restore 2>&1 |
        Tee-Object -FilePath $LogPath
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $diagnostics = Get-Content $LogPath | Where-Object {
            $_ -match '\berror\s+(CS|MSB|NU|NETSDK)\d+' -or
            $_ -match ':\s+error\s+' -or
            $_ -match '\bBuild FAILED\b'
        } | Select-Object -Unique

        foreach ($line in $diagnostics) {
            $safe = ([string]$line).Replace("`r", ' ').Replace("`n", ' ')
            Write-Host "##vso[task.logissue type=error]$safe"
        }

        throw "Build failed with exit code $exitCode."
    }
}

$source = [System.IO.Path]::GetFullPath($SourceRoot)
$results = [System.IO.Path]::GetFullPath($ResultsDirectory)
$artifacts = [System.IO.Path]::GetFullPath($ArtifactDirectory)

if (-not (Test-Path -LiteralPath $source)) {
    throw "Source root does not exist: $source"
}

New-Item -ItemType Directory -Force -Path $results | Out-Null
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

$buildLog = Join-Path $artifacts 'build.log'
$contextPath = Join-Path $artifacts 'run-context.txt'

Push-Location $source
try {
    @(
        "Repository=$Repository",
        "SourceRoot=$source",
        "Configuration=$Configuration",
        "Commit=$(git rev-parse HEAD)",
        "Branch=$(git branch --show-current)",
        "Machine=$env:COMPUTERNAME",
        "Identity=$([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"
    ) | Out-File $contextPath -Encoding utf8

    dotnet --info | Out-File (Join-Path $artifacts 'dotnet-info.txt') -Encoding utf8

    switch ($Repository) {
        'EOS' {
            Invoke-Checked -Label 'Restore EOS solution' -Command {
                dotnet restore EngineeringPerformance.slnx
            }

            Invoke-BuildWithDiagnostics -Solution 'EngineeringPerformance.slnx' -LogPath $buildLog

            Invoke-Checked -Label 'EOS domain tests' -Command {
                dotnet test tests/EngineeringPerformance.Domain.Tests/EngineeringPerformance.Domain.Tests.csproj --configuration $Configuration --no-build --no-restore --logger "trx;LogFileName=domain.trx" --results-directory $results
            }
            Invoke-Checked -Label 'EOS infrastructure tests' -Command {
                dotnet test tests/EngineeringPerformance.Infrastructure.Tests/EngineeringPerformance.Infrastructure.Tests.csproj --configuration $Configuration --no-build --no-restore --logger "trx;LogFileName=infrastructure.trx" --results-directory $results
            }
            Invoke-Checked -Label 'EOS UI tests' -Command {
                dotnet test tests/EngineeringPerformance.UI.Tests/EngineeringPerformance.UI.Tests.csproj --configuration $Configuration --no-build --no-restore --logger "trx;LogFileName=ui.trx" --results-directory $results
            }

            if ($RunVisualValidation) {
                $visual = Join-Path $artifacts 'visual-evidence'
                & (Join-Path $source 'scripts\capture-ui.ps1') -OutputDirectory $visual
                if ($LASTEXITCODE -ne 0) {
                    throw "EOS visual validation failed with exit code $LASTEXITCODE."
                }
            }
        }

        'APS' {
            Invoke-Checked -Label 'Restore APS solution' -Command {
                dotnet restore APS.slnx
            }

            Invoke-BuildWithDiagnostics -Solution 'APS.slnx' -LogPath $buildLog

            Invoke-Checked -Label 'APS planning tests' -Command {
                dotnet test tests/APS.Planning.Tests/APS.Planning.Tests.csproj --configuration $Configuration --no-build --no-restore --logger "trx;LogFileName=planning.trx" --results-directory $results
            }
            Invoke-Checked -Label 'APS UI tests' -Command {
                dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --configuration $Configuration --no-build --no-restore --logger "trx;LogFileName=ui.trx" --results-directory $results
            }

            $publish = Join-Path $artifacts 'desktop-publish'
            if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
            Invoke-Checked -Label 'APS Windows desktop publish smoke test' -Command {
                dotnet publish src/APS.DesktopHost/APS.DesktopHost.csproj `
                    --configuration $Configuration `
                    --runtime win-x64 `
                    --self-contained true `
                    --no-restore `
                    -p:PublishReadyToRun=true `
                    --output $publish
            }
        }
    }

    git status --porcelain=v1 --branch | Out-File (Join-Path $artifacts 'git-status-after-build.txt') -Encoding utf8
    Write-Host "`n$Repository verification completed successfully."
}
finally {
    Pop-Location
}
