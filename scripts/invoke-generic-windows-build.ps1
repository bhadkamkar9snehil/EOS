[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
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

function Resolve-Uv {
    $command = Get-Command uv -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    Write-Host "`n=== Install uv ===" -ForegroundColor Cyan
    $installer = Join-Path $env:TEMP 'install-uv.ps1'
    Invoke-WebRequest 'https://astral.sh/uv/install.ps1' -OutFile $installer
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
    if ($LASTEXITCODE -ne 0) {
        throw "uv installer failed with exit code $LASTEXITCODE."
    }

    $candidate = Join-Path $env:USERPROFILE '.local\bin\uv.exe'
    if (-not (Test-Path -LiteralPath $candidate)) {
        $command = Get-Command uv -ErrorAction SilentlyContinue
        if (-not $command) {
            throw 'uv installed but uv.exe could not be located.'
        }
        return $command.Source
    }

    $env:Path = "$(Split-Path -Parent $candidate);$env:Path"
    return $candidate
}

function Invoke-UvVerification {
    param(
        [Parameter(Mandatory)][string]$UvPath,
        [Parameter(Mandatory)][string]$Results,
        [Parameter(Mandatory)][string]$Artifacts
    )

    # Keep scientific Python deterministic and avoid oversubscribing the shared VM.
    $env:OMP_NUM_THREADS = '1'
    $env:OPENBLAS_NUM_THREADS = '1'
    $env:MKL_NUM_THREADS = '1'
    $env:NUMEXPR_NUM_THREADS = '1'

    & $UvPath --version | Out-File (Join-Path $Artifacts 'uv-version.txt') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'uv --version failed.' }

    Invoke-Checked -Label 'Sync locked Python environment' -Command {
        & $UvPath sync --locked
    }

    $fastXml = Join-Path $Results 'pytest-fast.xml'
    Invoke-Checked -Label 'Python fast tests' -Command {
        & $UvPath run pytest -m 'not statistical' --tb=short "--junitxml=$fastXml"
    }

    $markers = & $UvPath run pytest --markers 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enumerate pytest markers.'
    }
    $markers | Out-File (Join-Path $Artifacts 'pytest-markers.txt') -Encoding utf8

    if ($markers -match '(?m)^@pytest\.mark\.statistical\b') {
        $statXml = Join-Path $Results 'pytest-statistical.xml'
        Invoke-Checked -Label 'Python statistical acceptance tests' -Command {
            & $UvPath run pytest -m statistical --tb=short "--junitxml=$statXml"
        }
    }
    else {
        Write-Host 'No registered statistical pytest marker; fast lane already covered all unmarked tests.'
    }
}

function Invoke-GenericDotNetVerification {
    param(
        [Parameter(Mandatory)][string]$Solution,
        [Parameter(Mandatory)][string]$Results,
        [Parameter(Mandatory)][string]$BuildLog
    )

    Invoke-Checked -Label "Restore $Solution" -Command {
        dotnet restore $Solution
    }
    Invoke-BuildWithDiagnostics -Solution $Solution -LogPath $BuildLog
    Invoke-Checked -Label "Test $Solution" -Command {
        dotnet test $Solution --configuration $Configuration --no-build --no-restore --logger 'trx;LogFileName=solution.trx' --results-directory $Results
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
$normalizedRepository = $Repository.ToLowerInvariant()

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

    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        dotnet --info | Out-File (Join-Path $artifacts 'dotnet-info.txt') -Encoding utf8
    }

    if ($normalizedRepository -eq 'bhadkamkar9snehil/eos') {
        Invoke-Checked -Label 'Restore EOS solution' -Command {
            dotnet restore EngineeringPerformance.slnx
        }

        Invoke-BuildWithDiagnostics -Solution 'EngineeringPerformance.slnx' -LogPath $buildLog

        Invoke-Checked -Label 'EOS domain tests' -Command {
            dotnet test tests/EngineeringPerformance.Domain.Tests/EngineeringPerformance.Domain.Tests.csproj --configuration $Configuration --no-build --no-restore --logger 'trx;LogFileName=domain.trx' --results-directory $results
        }
        Invoke-Checked -Label 'EOS infrastructure tests' -Command {
            dotnet test tests/EngineeringPerformance.Infrastructure.Tests/EngineeringPerformance.Infrastructure.Tests.csproj --configuration $Configuration --no-build --no-restore --logger 'trx;LogFileName=infrastructure.trx' --results-directory $results
        }
        Invoke-Checked -Label 'EOS UI tests' -Command {
            dotnet test tests/EngineeringPerformance.UI.Tests/EngineeringPerformance.UI.Tests.csproj --configuration $Configuration --no-build --no-restore --logger 'trx;LogFileName=ui.trx' --results-directory $results
        }

        if ($RunVisualValidation) {
            $visual = Join-Path $artifacts 'visual-evidence'
            & (Join-Path $source 'scripts\capture-ui.ps1') -OutputDirectory $visual
            if ($LASTEXITCODE -ne 0) {
                throw "EOS visual validation failed with exit code $LASTEXITCODE."
            }
        }
    }
    elseif ($normalizedRepository -eq 'bhadkamkar9snehil/aps') {
        $apsVerifier = Join-Path $source 'build\verify.ps1'
        if (Test-Path -LiteralPath $apsVerifier) {
            Write-Host "Using APS ref's authoritative build/verify.ps1 contract."
            $publish = Join-Path $artifacts 'desktop-publish'
            $diagnostics = Join-Path $artifacts 'aps-diagnostics'
            & $apsVerifier `
                -Configuration $Configuration `
                -ResultsDirectory $results `
                -PublishDirectory $publish `
                -DiagnosticsDirectory $diagnostics
        }
        else {
            Write-Host 'APS ref predates build/verify.ps1; using legacy-compatible Build Lab fallback.'
            Invoke-GenericDotNetVerification -Solution 'APS.slnx' -Results $results -BuildLog $buildLog

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
    elseif ((Test-Path -LiteralPath 'pyproject.toml') -and (Test-Path -LiteralPath 'uv.lock')) {
        Write-Host 'Detected uv-managed Python repository.'
        $uv = Resolve-Uv
        Invoke-UvVerification -UvPath $uv -Results $results -Artifacts $artifacts
    }
    else {
        $solution = @(
            Get-ChildItem -LiteralPath $source -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -in '.sln', '.slnx' }
        ) | Select-Object -First 1

        if ($solution) {
            Write-Host "Detected generic .NET solution: $($solution.Name)"
            Invoke-GenericDotNetVerification -Solution $solution.Name -Results $results -BuildLog $buildLog
        }
        else {
            throw "Unsupported repository contract for '$Repository'. Add a repository-owned verifier or extend the Build Lab detector."
        }
    }

    git status --porcelain=v1 --branch | Out-File (Join-Path $artifacts 'git-status-after-build.txt') -Encoding utf8
    Write-Host "`n$Repository verification completed successfully."
}
finally {
    Pop-Location
}
