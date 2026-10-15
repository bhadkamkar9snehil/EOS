$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dataDir = Join-Path $env:LOCALAPPDATA "EOS_V2"
New-Item -ItemType Directory -Force $dataDir | Out-Null
$env:EOS_DEV_NOAUTH = "1"
$env:EOS_DB = Join-Path $dataDir "eos.db"
$env:ASPNETCORE_URLS = "http://127.0.0.1:8095"
[Environment]::SetEnvironmentVariable("Logging__LogLevel__Microsoft.EntityFrameworkCore", "Warning")
Get-Process EOS.Server -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build (Join-Path $root "src\EOS.Server") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { exit 1 }
Start-Process dotnet -ArgumentList "run", "--project", (Join-Path $root "src\EOS.Server"), "-c", "Release", "--no-build", "--no-launch-profile" -WindowStyle Minimized
for ($i = 0; $i -lt 30; $i++) {
    try { Invoke-RestMethod http://127.0.0.1:8095/health | Out-Null; break } catch { Start-Sleep -Seconds 1 }
}
Start-Process "http://127.0.0.1:8095/"
