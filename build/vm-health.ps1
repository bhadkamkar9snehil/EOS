param(
    [switch]$RepairAgent,
    [switch]$EnableRdp,
    [switch]$DisableRdp
)

$ErrorActionPreference = 'Stop'

Write-Host '=== EOS Windows VM health ==='
Write-Host "Computer: $env:COMPUTERNAME"
Write-Host "User:     $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Host "Time UTC: $([DateTime]::UtcNow.ToString('o'))"

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

Write-Host "`n--- Build toolchain ---"
try {
    dotnet --info
} catch {
    Write-Warning "dotnet --info failed: $($_.Exception.Message)"
}

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
