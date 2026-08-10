param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$StatusFile,
    [Parameter(Mandatory = $true)][string]$LogFile
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Write-Status {
    param([string]$Stage,[string]$Message,[string]$State = 'RUNNING')
    $payload = [ordered]@{ state=$State; stage=$Stage; message=$Message; time=(Get-Date).ToString('O') } | ConvertTo-Json -Compress
    $temp = "$StatusFile.tmp"
    [System.IO.File]::WriteAllText($temp, $payload, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $StatusFile -Force
}

function Write-Log {
    param([string]$Text)
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Add-Content -LiteralPath $LogFile -Value "[$stamp] $Text"
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$Message
    )
    Write-Status -Stage $Stage -Message $Message
    Write-Log "$FilePath $($Arguments -join ' ')"
    $output = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($output) { $output | ForEach-Object { Add-Content -LiteralPath $LogFile -Value $_ } }
    if ($exitCode -ne 0) { throw "$Stage failed with exit code $exitCode." }
}

function Stop-EpaProcesses {
    Write-Status -Stage 'PREPARE' -Message 'Closing the currently running EPA instance…'
    Get-Process -Name 'EngineeringPerformance.DesktopHost' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    try {
        Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction Stop |
            Where-Object { $_.CommandLine -like '*EngineeringPerformance.DesktopHost*' } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    } catch { Write-Log "Process scan warning: $($_.Exception.Message)" }
    Start-Sleep -Milliseconds 450
}

try {
    if (-not (Test-Path -LiteralPath $RepoRoot)) { throw "Repository folder not found: $RepoRoot" }
    Set-Location -LiteralPath $RepoRoot
    $logDir = Split-Path -Parent $LogFile
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Set-Content -LiteralPath $LogFile -Value "EPA Launcher run started $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

    Write-Status -Stage 'CHECK' -Message 'Checking the repository…'
    $inside = (& git rev-parse --is-inside-work-tree 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $inside -ne 'true') { throw 'This folder is not a Git repository.' }

    $dirty = (& git status --porcelain 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Git status.' }
    if (-not [string]::IsNullOrWhiteSpace($dirty)) {
        Write-Log "Local changes detected:`r`n$dirty"
        throw 'Local changes are present. EPA Launcher will not overwrite them. Commit, stash, or discard them first.'
    }

    Stop-EpaProcesses

    Invoke-Native 'git' @('fetch','origin','main') 'SYNC' 'Fetching the latest EPA from GitHub…'
    Invoke-Native 'git' @('switch','main') 'SYNC' 'Switching to the production main branch…'
    Invoke-Native 'git' @('pull','--ff-only','origin','main') 'SYNC' 'Updating EPA to the latest production version…'

    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($node) {
        $jsFiles = @(
            'src/EngineeringPerformance.UI/wwwroot/theme.js',
            'src/EngineeringPerformance.UI/wwwroot/skin.js',
            'src/EngineeringPerformance.UI/wwwroot/atlas-charts.js',
            'src/EngineeringPerformance.UI/wwwroot/realist-runtime.js'
        )
        foreach ($jsFile in $jsFiles) {
            Invoke-Native 'node' @('--check',$jsFile) 'VALIDATE' "Checking $([System.IO.Path]::GetFileName($jsFile))…"
        }
    } else {
        Write-Log 'Node.js is not installed; JavaScript syntax checks were skipped.'
    }

    Invoke-Native 'dotnet' @('clean','EngineeringPerformance.slnx','-c','Release') 'CLEAN' 'Cleaning the previous build…'
    Invoke-Native 'dotnet' @('restore','EngineeringPerformance.slnx') 'RESTORE' 'Restoring EPA dependencies…'
    Invoke-Native 'dotnet' @('build','EngineeringPerformance.slnx','-c','Release','--no-restore') 'BUILD' 'Building EPA…'
    Invoke-Native 'dotnet' @('test','EngineeringPerformance.slnx','-c','Release','--no-build') 'TEST' 'Running EPA validation tests…'

    Write-Status -Stage 'LAUNCH' -Message 'Starting EPA…'
    $releaseRoot = Join-Path $RepoRoot 'src\EngineeringPerformance.DesktopHost\bin\Release'
    $exe = Get-ChildItem -LiteralPath $releaseRoot -Filter 'EngineeringPerformance.DesktopHost.exe' -File -Recurse -ErrorAction Stop |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $exe) { throw 'EPA executable was not produced by the build.' }

    Write-Log "Launching $($exe.FullName)"
    Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName | Out-Null
    Write-Status -Stage 'READY' -Message 'EPA is ready.' -State 'LAUNCHED'
    Write-Log 'EPA launched successfully.'
}
catch {
    $message = $_.Exception.Message
    Write-Log "ERROR: $message"
    Write-Status -Stage 'ERROR' -Message $message -State 'ERROR'
    exit 1
}
