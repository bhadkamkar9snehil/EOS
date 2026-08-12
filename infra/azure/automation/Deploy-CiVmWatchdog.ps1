param(
    [Parameter(Mandatory = $true)][string]$SubscriptionId,
    [Parameter(Mandatory = $true)][string]$ResourceGroupName,
    [Parameter(Mandatory = $true)][string]$VmName,
    [Parameter(Mandatory = $true)][string]$Location,
    [string]$AutomationAccountName = 'eos-ci-watchdog',
    [string]$RunbookName = 'Ensure-CiVm',
    [string]$RunbookPath = '',
    [string]$RunbookUri = 'https://raw.githubusercontent.com/bhadkamkar9snehil/EOS/main/infra/azure/automation/Ensure-CiVm.ps1'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if (-not (Get-Module -ListAvailable Az.Accounts)) {
    throw 'Az PowerShell modules are required. Run this from Azure Cloud Shell (PowerShell) or install the Az module locally.'
}

$context = Get-AzContext -ErrorAction SilentlyContinue
if (-not $context -or $context.Subscription.Id -ne $SubscriptionId) {
    Connect-AzAccount | Out-Null
}
$context = Set-AzContext -SubscriptionId $SubscriptionId

$vm = Get-AzVM -ResourceGroupName $ResourceGroupName -Name $VmName -DefaultProfile $context
Write-Host "Target VM: $($vm.Id)"

$automation = Get-AzAutomationAccount `
    -ResourceGroupName $ResourceGroupName `
    -Name $AutomationAccountName `
    -ErrorAction SilentlyContinue `
    -DefaultProfile $context

if (-not $automation) {
    Write-Host "Creating Automation account '$AutomationAccountName'..."
    $automation = New-AzAutomationAccount `
        -ResourceGroupName $ResourceGroupName `
        -Name $AutomationAccountName `
        -Location $Location `
        -Plan Basic `
        -AssignSystemIdentity `
        -DefaultProfile $context
} elseif (-not $automation.Identity -or -not $automation.Identity.PrincipalId) {
    Write-Host 'Enabling system-assigned managed identity...'
    $automation = Set-AzAutomationAccount `
        -ResourceGroupName $ResourceGroupName `
        -Name $AutomationAccountName `
        -AssignSystemIdentity `
        -DefaultProfile $context
}

$principalId = $automation.Identity.PrincipalId
if (-not $principalId) {
    # Identity projection can lag the create/update result briefly.
    Start-Sleep -Seconds 5
    $automation = Get-AzAutomationAccount -ResourceGroupName $ResourceGroupName -Name $AutomationAccountName -DefaultProfile $context
    $principalId = $automation.Identity.PrincipalId
}
if (-not $principalId) { throw 'Automation account managed identity did not become available.' }

$role = Get-AzRoleAssignment `
    -ObjectId $principalId `
    -Scope $vm.Id `
    -RoleDefinitionName 'Virtual Machine Contributor' `
    -ErrorAction SilentlyContinue `
    -DefaultProfile $context

if (-not $role) {
    Write-Host 'Granting the watchdog Virtual Machine Contributor on this VM only...'
    New-AzRoleAssignment `
        -ObjectId $principalId `
        -Scope $vm.Id `
        -RoleDefinitionName 'Virtual Machine Contributor' `
        -DefaultProfile $context | Out-Null
}

$tempRunbook = $null
if ([string]::IsNullOrWhiteSpace($RunbookPath)) {
    $tempRunbook = Join-Path ([System.IO.Path]::GetTempPath()) 'Ensure-CiVm.ps1'
    Write-Host "Downloading runbook source from $RunbookUri"
    Invoke-WebRequest -Uri $RunbookUri -OutFile $tempRunbook
    $RunbookPath = $tempRunbook
}
$RunbookPath = [System.IO.Path]::GetFullPath($RunbookPath)
if (-not (Test-Path -LiteralPath $RunbookPath)) { throw "Runbook source not found: $RunbookPath" }

Write-Host 'Importing and publishing watchdog runbook...'
Import-AzAutomationRunbook `
    -ResourceGroupName $ResourceGroupName `
    -AutomationAccountName $AutomationAccountName `
    -Name $RunbookName `
    -Path $RunbookPath `
    -Type PowerShell `
    -Description 'Keeps the Windows CI VM running and repairs its Azure Pipelines agent service.' `
    -Published `
    -Force `
    -DefaultProfile $context | Out-Null

$runbookParameters = @{
    SubscriptionId = $SubscriptionId
    ResourceGroupName = $ResourceGroupName
    VmName = $VmName
}

# Azure Automation's normal recurring schedule bottoms out at one hour. Four hourly schedules,
# offset by 15 minutes, give a supported 15-minute recovery cadence without a Logic App.
$offsets = @(5, 20, 35, 50)
foreach ($offset in $offsets) {
    $scheduleName = "${RunbookName}-Every15-$offset"
    $existing = Get-AzAutomationSchedule `
        -ResourceGroupName $ResourceGroupName `
        -AutomationAccountName $AutomationAccountName `
        -Name $scheduleName `
        -ErrorAction SilentlyContinue `
        -DefaultProfile $context

    if (-not $existing) {
        $start = (Get-Date).AddMinutes($offset)
        Write-Host "Creating hourly watchdog schedule '$scheduleName' starting $start..."
        New-AzAutomationSchedule `
            -ResourceGroupName $ResourceGroupName `
            -AutomationAccountName $AutomationAccountName `
            -Name $scheduleName `
            -StartTime $start `
            -HourInterval 1 `
            -TimeZone 'Etc/UTC' `
            -DefaultProfile $context | Out-Null

        Register-AzAutomationScheduledRunbook `
            -ResourceGroupName $ResourceGroupName `
            -AutomationAccountName $AutomationAccountName `
            -RunbookName $RunbookName `
            -ScheduleName $scheduleName `
            -Parameters $runbookParameters `
            -DefaultProfile $context | Out-Null
    }
}

Write-Host 'Starting the watchdog immediately so it can recover the VM now...'
$job = Start-AzAutomationRunbook `
    -ResourceGroupName $ResourceGroupName `
    -AutomationAccountName $AutomationAccountName `
    -Name $RunbookName `
    -Parameters $runbookParameters `
    -DefaultProfile $context

Write-Host "Automation job submitted: $($job.JobId)"
Write-Host 'Future checks are scheduled approximately every 15 minutes.'

if ($tempRunbook) { Remove-Item -LiteralPath $tempRunbook -Force -ErrorAction SilentlyContinue }
