param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Branch = 'visual-artifacts'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    throw "Visual QA output directory does not exist: $OutputDirectory"
}

$tempRoot = if ($env:AGENT_TEMPDIRECTORY) { $env:AGENT_TEMPDIRECTORY } else { [System.IO.Path]::GetTempPath() }
$worktree = Join-Path $tempRoot 'EOSVisualArtifactsWorktree'

try {
    & git -C $projectRoot worktree remove --force $worktree 2>$null
    Remove-Item -LiteralPath $worktree -Recurse -Force -ErrorAction SilentlyContinue

    & git -C $projectRoot worktree add --detach $worktree HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the visual-artifacts Git worktree.' }

    & git -C $worktree checkout --orphan $Branch
    if ($LASTEXITCODE -ne 0) { throw "Could not create orphan branch '$Branch'." }

    & git -C $worktree rm -rf . 2>$null
    Get-ChildItem -LiteralPath $worktree -Force |
        Where-Object { $_.Name -ne '.git' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    $latest = Join-Path $worktree 'latest'
    New-Item -ItemType Directory -Path $latest -Force | Out-Null
    Copy-Item -Path (Join-Path $OutputDirectory '*') -Destination $latest -Recurse -Force

    $source = if ($env:BUILD_SOURCEVERSION) { $env:BUILD_SOURCEVERSION } else { (& git -C $projectRoot rev-parse HEAD).Trim() }
    $build = if ($env:BUILD_BUILDID) { $env:BUILD_BUILDID } else { 'local' }
    @"
# EOS visual QA evidence

This branch is generated automatically. Do not edit it manually.

- Source commit: `$source`
- Azure build: `$build`
- Generated UTC: $([DateTime]::UtcNow.ToString('O'))

`latest/` contains the most recent synthetic-data screenshots, DOM diagnostics, manifest, and application logs captured by the Windows visual-QA run.
"@ | Set-Content -LiteralPath (Join-Path $worktree 'README.md') -Encoding UTF8

    & git -C $worktree config user.name 'EOS Visual QA'
    & git -C $worktree config user.email 'visual-qa@eos.invalid'
    & git -C $worktree add -A
    & git -C $worktree commit -m "Visual QA for $source [skip ci]"
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit visual QA evidence.' }

    & git -C $worktree push --force origin "HEAD:refs/heads/$Branch"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not push '$Branch'. Ensure checkout.persistCredentials is true and the GitHub connection can write repository contents."
    }
}
finally {
    & git -C $projectRoot worktree remove --force $worktree 2>$null
    Remove-Item -LiteralPath $worktree -Recurse -Force -ErrorAction SilentlyContinue
}
