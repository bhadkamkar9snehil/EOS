param([switch]$SkipE2E, [switch]$SkipDotnet)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$failed = @()

function Ensure-Node {
    if (Get-Command node -ErrorAction SilentlyContinue) { return }
    $version = "v22.11.0"
    $dir = Join-Path $root ".tools\node-$version-win-x64"
    if (-not (Test-Path (Join-Path $dir "node.exe"))) {
        Write-Host "Node is not installed; fetching a portable copy ($version)" -ForegroundColor Yellow
        New-Item -ItemType Directory -Force (Join-Path $root ".tools") | Out-Null
        $zip = Join-Path $root ".tools\node.zip"
        Invoke-WebRequest "https://nodejs.org/dist/$version/node-$version-win-x64.zip" -OutFile $zip -UseBasicParsing
        Expand-Archive $zip -DestinationPath (Join-Path $root ".tools") -Force
        Remove-Item $zip
    }
    $env:PATH = "$dir;$env:PATH"
}
Ensure-Node

function Step($name, [scriptblock]$body) {
    Write-Host "`n== $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { $script:failed += $name }
}

if (-not $SkipDotnet) {
    Step "Scheduling tests" { dotnet test (Join-Path $root "tests\EOS.Scheduling.Tests") --nologo -v q }
    Step "Infrastructure tests" { dotnet test (Join-Path $root "tests\EOS.Infrastructure.Tests") --nologo -v q }
}
Step "Grid unit and DOM tests" {
    if (-not (Test-Path (Join-Path $root "tests\js\node_modules"))) { npm --prefix (Join-Path $root "tests\js") install --no-audit --no-fund }
    Push-Location (Join-Path $root "tests\js"); try { node --test eos-grid-model.test.js eos-grid-dom.test.js } finally { Pop-Location }
}
if (-not $SkipE2E) {
    Step "End-to-end (Playwright)" {
        if (-not (Test-Path (Join-Path $root "tests\e2e\node_modules"))) { npm --prefix (Join-Path $root "tests\e2e") install --no-audit --no-fund }
        Push-Location (Join-Path $root "tests\e2e")
        try {
            $browsers = Join-Path $env:LOCALAPPDATA "ms-playwright"
            if (-not (Get-ChildItem $browsers -Filter "chromium-*" -ErrorAction SilentlyContinue)) { npx playwright install chromium }
            npx playwright test
        } finally { Pop-Location }
    }
}

if ($failed.Count) { Write-Host "`nFAILED: $($failed -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host "`nAll test suites passed." -ForegroundColor Green
