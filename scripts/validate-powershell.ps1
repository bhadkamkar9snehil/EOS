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
        $failures.Add('bootstrap-azure-devops-project.ps1: az devops invoke must not receive preview revision suffixes such as 7.1-preview.3.')
    }
    if ($content -match '(?i)Invoke-RestMethod') {
        $failures.Add('bootstrap-azure-devops-project.ps1: use the authenticated Azure DevOps CLI rather than raw Invoke-RestMethod calls.')
    }
    if ($content -match '(?i)\$Args\b') {
        $failures.Add('bootstrap-azure-devops-project.ps1: do not use $Args as a named variable or parameter; it collides with PowerShell automatic $args.')
    }

    $dashboardDescriptionMatch = [regex]::Match($content, '\$DashboardDescription\s*=\s*''([^'']*)''')
    if (-not $dashboardDescriptionMatch.Success) {
        $failures.Add('bootstrap-azure-devops-project.ps1: DashboardDescription must be a statically validated single-quoted literal.')
    }
    elseif ($dashboardDescriptionMatch.Groups[1].Value.Length -gt 128) {
        $failures.Add("bootstrap-azure-devops-project.ps1: Azure DevOps dashboard descriptions are limited to 128 characters; found $($dashboardDescriptionMatch.Groups[1].Value.Length).")
    }

    $lines = Get-Content $projectBootstrap
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*(?:\$[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?&\s+az\s+boards\s+work-item\s+update\b') {
            $end = [Math]::Min($i + 12, $lines.Count - 1)
            $blockLines = @($lines[$i..$end] | Where-Object { $_ -notmatch '^\s*#' })
            $block = ($blockLines -join "`n")
            if ($block -match '(?m)^\s*--project\b') {
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
