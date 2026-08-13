$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$scriptRoots = @(
    (Join-Path $repoRoot 'scripts'),
    (Join-Path $repoRoot 'build')
) | Where-Object { Test-Path $_ }

$files = @($scriptRoots | ForEach-Object { Get-ChildItem $_ -Filter '*.ps1' -File -Recurse })
if ($files.Count -eq 0) { throw 'No PowerShell scripts found to validate.' }

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
    foreach ($parseError in @($parseErrors)) {
        $failures.Add("$($file.FullName):$($parseError.Extent.StartLineNumber): $($parseError.Message)")
    }
}

$projectBootstrap = Join-Path $repoRoot 'scripts/bootstrap-azure-devops-project.ps1'
if (Test-Path $projectBootstrap) {
    $content = Get-Content $projectBootstrap -Raw

    if ($content -match '-ApiVersion\s+[''"]\d+\.\d+-preview\.\d+[''"]') {
        $failures.Add('bootstrap-azure-devops-project.ps1: az devops invoke must not receive preview revision suffixes (for example 7.1-preview.3); the extension parser cannot parse them.')
    }
    if ($content -match '(?i)Invoke-RestMethod') {
        $failures.Add('bootstrap-azure-devops-project.ps1: do not bypass the authenticated Azure DevOps CLI with raw Invoke-RestMethod calls.')
    }
    if ($content -match '(?i)\$Args\b') {
        $failures.Add('bootstrap-azure-devops-project.ps1: do not use $Args as a named variable or parameter; it collides with the PowerShell automatic $args variable.')
    }

    $lines = Get-Content $projectBootstrap
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'az boards work-item update') {
            $end = [Math]::Min($i + 12, $lines.Count - 1)
            $block = ($lines[$i..$end] -join "`n")
            if ($block -match '--project') {
                $failures.Add("bootstrap-azure-devops-project.ps1:$($i + 1): az boards work-item update does not support --project.")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "##vso[task.logissue type=error]$failure" }
    throw "PowerShell validation failed with $($failures.Count) error(s)."
}

Write-Host "PowerShell validation passed for $($files.Count) script(s)."