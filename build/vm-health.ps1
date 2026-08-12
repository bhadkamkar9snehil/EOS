param(
    [switch]$RepairAgent,
    [switch]$EnableRdp,
    [switch]$DisableRdp
)

$ErrorActionPreference = 'Stop'

Write-Host '=== EOS Windows VM health ==='
Write-Host "Computer: $env:COMPUTERNAME"
Write-Host "User:     $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Host "Session:  $((Get-Process -Id $PID).SessionId)"
Write-Host "Time UTC: $([DateTime]::UtcNow.ToString('o'))"

Write-Host "`n--- Azure instance metadata ---"
try {
    $metadata = Invoke-RestMethod -Headers @{ Metadata = 'true' } -Method GET -TimeoutSec 3 -Uri 'http://169.254.169.254/metadata/instance?api-version=2025-04-07'
    $metadata.compute |
        Select-Object name, location, resourceGroupName, subscriptionId, vmSize, priority, evictionPolicy, osType |
        Format-List
} catch {
    Write-Warning "Azure IMDS query failed: $($_.Exception.Message)"
}

Write-Host "`n--- OS / storage ---"
Get-CimInstance Win32_OperatingSystem |
    Select-Object Caption, Version, LastBootUpTime |
    Format-List

Get-PSDrive -PSProvider FileSystem |
    Select-Object Name,
        @{n='UsedGB';e={[math]::Round($_.Used / 1GB, 2)}},
        @{n='FreeGB';e={[math]::Round($_.Free / 1GB, 2)}} |
    Format-Table -AutoSize

Write-Host "`n--- Azure Pipelines agent ---"
$agentServices = @(Get-Service -Name 'vstsagent*' -ErrorAction SilentlyContinue)
if ($agentServices.Count -eq 0) {
    Write-Warning 'No vstsagent service was found.'
} else {
    $agentServices | Select-Object Name, Status, StartType | Format-Table -AutoSize

    if ($RepairAgent) {
        foreach ($service in $agentServices) {
            Write-Host "Repairing $($service.Name)..."
            Set-Service -Name $service.Name -StartupType Automatic
            if ($service.Status -eq 'Running') {
                Restart-Service -Name $service.Name -Force
            } else {
                Start-Service -Name $service.Name
            }
        }
        Get-Service -Name 'vstsagent*' |
            Select-Object Name, Status, StartType |
            Format-Table -AutoSize
    }
}

Write-Host "`n--- Windows sessions / desktop availability ---"
try {
    & quser.exe 2>&1 | ForEach-Object { Write-Host $_ }
} catch {
    Write-Warning "quser failed: $($_.Exception.Message)"
}
$explorers = @(Get-Process explorer -ErrorAction SilentlyContinue)
if ($explorers.Count -eq 0) {
    Write-Host 'No explorer.exe process is active; no normal interactive desktop is currently logged on.'
} else {
    $explorers | Select-Object Id, SessionId, StartTime | Format-Table -AutoSize
}

Write-Host "`n--- Build / visual toolchain ---"
try {
    dotnet --info
} catch {
    Write-Warning "dotnet --info failed: $($_.Exception.Message)"
}

$webViewRoots = @(
    'C:\Program Files (x86)\Microsoft\EdgeWebView\Application',
    'C:\Program Files\Microsoft\EdgeWebView\Application'
)
$webViewExecutables = @($webViewRoots |
    Where-Object { Test-Path -LiteralPath $_ } |
    ForEach-Object { Get-ChildItem -LiteralPath $_ -Directory -ErrorAction SilentlyContinue } |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'msedgewebview2.exe' } |
    Where-Object { Test-Path -LiteralPath $_ })
if ($webViewExecutables.Count -gt 0) {
    $webViewExecutables | Select-Object -First 3 | ForEach-Object { Write-Host "WebView2: $_" }
} else {
    Write-Warning 'WebView2 Evergreen Runtime executable was not found in the standard machine locations.'
}

Write-Host "`n--- Remote-management services ---"
Get-Service -Name TermService, WinRM, sshd -ErrorAction SilentlyContinue |
    Select-Object Name, Status, StartType |
    Format-Table -AutoSize

Write-Host "`nListening management ports:"
$managementPorts = 22, 3389, 443, 5985, 5986
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -in $managementPorts } |
    Select-Object LocalAddress, LocalPort, OwningProcess |
    Sort-Object LocalPort |
    Format-Table -AutoSize

Write-Host "`n--- RDP state ---"
$terminalServerKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server'
$fDeny = (Get-ItemProperty -Path $terminalServerKey -Name fDenyTSConnections).fDenyTSConnections
$termService = Get-Service -Name TermService -ErrorAction SilentlyContinue
Write-Host "fDenyTSConnections: $fDeny"
if ($termService) {
    $termService | Select-Object Name, Status, StartType | Format-Table -AutoSize
}

if ($EnableRdp -and $DisableRdp) {
    throw 'Choose either -EnableRdp or -DisableRdp, not both.'
}

if ($EnableRdp) {
    Write-Host 'Enabling Windows RDP service and Windows Firewall rules. Azure NSG/Bastion policy still controls network reachability.'
    Set-ItemProperty -Path $terminalServerKey -Name fDenyTSConnections -Value 0
    Set-Service -Name TermService -StartupType Automatic
    Start-Service -Name TermService
    Enable-NetFirewallRule -DisplayGroup 'Remote Desktop' -ErrorAction SilentlyContinue
}

if ($DisableRdp) {
    Write-Host 'Disabling Windows RDP and Windows Firewall rules.'
    Disable-NetFirewallRule -DisplayGroup 'Remote Desktop' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $terminalServerKey -Name fDenyTSConnections -Value 1
}

Write-Host "`nHealth probe complete."
