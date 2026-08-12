param(
    [Parameter(Mandatory = $true)][string]$SubscriptionId,
    [Parameter(Mandatory = $true)][string]$ResourceGroupName,
    [Parameter(Mandatory = $true)][string]$VmName,
    [int]$StartAttempts = 3,
    [int]$SecondsBetweenAttempts = 45,
    [switch]$SkipAgentRepair
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Disable-AzContextAutosave -Scope Process | Out-Null
$connection = Connect-AzAccount -Identity
$context = Set-AzContext -SubscriptionId $SubscriptionId -DefaultProfile $connection.Context

function Get-PowerState {
    $vm = Get-AzVM `
        -ResourceGroupName $ResourceGroupName `
        -Name $VmName `
        -Status `
        -DefaultProfile $context

    $state = $vm.Statuses |
        Where-Object Code -Like 'PowerState/*' |
        Select-Object -ExpandProperty DisplayStatus -First 1

    [pscustomobject]@{
        Vm = $vm
        State = if ($state) { $state } else { 'Unknown' }
    }
}

Write-Output "CI VM watchdog starting for $ResourceGroupName/$VmName"
$power = Get-PowerState
Write-Output "Current power state: $($power.State)"

if ($power.State -ne 'VM running') {
    $started = $false
    for ($attempt = 1; $attempt -le [Math]::Max(1, $StartAttempts); $attempt++) {
        try {
            Write-Output "Start attempt $attempt of $StartAttempts..."
            Start-AzVM `
                -ResourceGroupName $ResourceGroupName `
                -Name $VmName `
                -DefaultProfile $context | Out-Null
            $started = $true
            break
        }
        catch {
            Write-Warning "VM start attempt $attempt failed: $($_.Exception.Message)"
            if ($attempt -lt $StartAttempts) {
                Start-Sleep -Seconds ([Math]::Max(5, $SecondsBetweenAttempts))
            }
        }
    }

    if (-not $started) {
        # This is a normal possible state for Spot capacity. Leave a loud audit trail but do not
        # mutate anything else; the next scheduled/event-triggered watchdog run can retry.
        Write-Warning 'The VM could not be allocated. If this is a Spot VM, Azure may currently have no Spot capacity.'
        return
    }
}

$deadline = (Get-Date).AddMinutes(5)
do {
    Start-Sleep -Seconds 10
    $power = Get-PowerState
    Write-Output "Power state: $($power.State)"
} while ($power.State -ne 'VM running' -and (Get-Date) -lt $deadline)

if ($power.State -ne 'VM running') {
    throw "VM did not reach the running state within five minutes. Last state: $($power.State)"
}

if (-not $SkipAgentRepair) {
    $repairScript = @'
$ErrorActionPreference = 'Stop'
$services = @(Get-Service -Name 'vstsagent*' -ErrorAction SilentlyContinue)
if ($services.Count -eq 0) {
    throw 'No Azure Pipelines vstsagent service is installed.'
}

foreach ($service in $services) {
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
'@

    Write-Output 'Repairing Azure Pipelines agent service through Azure VM Run Command...'
    $result = Invoke-AzVMRunCommand `
        -ResourceGroupName $ResourceGroupName `
        -VMName $VmName `
        -CommandId 'RunPowerShellScript' `
        -ScriptString $repairScript `
        -DefaultProfile $context

    foreach ($message in $result.Value) {
        if ($message.Message) { Write-Output $message.Message }
    }
}

Write-Output 'CI VM watchdog completed.'
