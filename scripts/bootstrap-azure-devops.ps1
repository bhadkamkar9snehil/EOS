$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# EOS Azure / Azure DevOps bootstrap.
# Safe to rerun. Existing Azure and Azure DevOps resources are discovered and reused.
#
# Design choice:
# Azure access is least-privilege at the EOS resource-group boundary. The ARM service
# connection is made available to all pipelines in this *single-member EOS DevOps
# project* because the preview pipeline-specific protected-resource API can return 403
# for Microsoft Entra tokens even when the caller can create/manage the endpoint.
# The Azure identity itself still has only Virtual Machine Contributor on the EOS RG.

$OrganizationUrl = 'https://dev.azure.com/apexasnehil'
$ProjectName = 'EOS'
$SubscriptionId = '69f506f7-b17d-404d-b814-b03d9b1a0d0d'
$VmName = 'EOS'
$ManagedIdentityName = 'eos-devops-control'
$FederatedCredentialName = 'azure-devops-eos-vm-control'
$ServiceConnectionName = 'eos-vm-arm'
$VariableGroupName = 'eos-vm-control'
$ControlPipelineName = 'EOS VM Control'
$GitHubRepository = 'bhadkamkar9snehil/EOS'
$ControlYamlPath = 'azure-vm-control.yml'

function Step([string]$Text) {
    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
}

function AzJson([string[]]$CliArgs) {
    $raw = & az @CliArgs --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed: az $($CliArgs -join ' ')" }
    if ([string]::IsNullOrWhiteSpace(($raw -join "`n"))) { return $null }
    return (($raw -join "`n") | ConvertFrom-Json)
}

function AzTsv([string[]]$CliArgs) {
    $raw = & az @CliArgs --only-show-errors --output tsv
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed: az $($CliArgs -join ' ')" }
    return (($raw -join "`n").Trim())
}

Step 'Azure account and EOS VM discovery'
& az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) { throw "Cannot select Azure subscription $SubscriptionId." }

$Account = AzJson @('account','show')
$TenantId = [string]$Account.tenantId
$SubscriptionName = [string]$Account.name
$SignedInUser = [string]$Account.user.name
$ResourceGroup = AzTsv @('vm','list','--query',"[?name=='$VmName'].resourceGroup | [0]")
if ([string]::IsNullOrWhiteSpace($ResourceGroup)) { throw "Could not find Azure VM '$VmName'." }

Write-Host "Signed in:       $SignedInUser"
Write-Host "Tenant:          $TenantId"
Write-Host "Subscription:    $SubscriptionName ($SubscriptionId)"
Write-Host "VM:              $VmName"
Write-Host "Resource group:  $ResourceGroup"

Step 'Azure DevOps CLI authentication'
& az extension show --name azure-devops --only-show-errors *> $null
if ($LASTEXITCODE -eq 0) {
    & az extension update --name azure-devops --only-show-errors | Out-Null
} else {
    & az extension add --name azure-devops --only-show-errors | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw 'Could not install/update the azure-devops CLI extension.' }

& az devops configure --defaults organization=$OrganizationUrl project=$ProjectName
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Azure DevOps CLI defaults.' }

$ProjectRaw = & az devops project show --project $ProjectName --organization $OrganizationUrl --only-show-errors --output json 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Azure DevOps rejected the current Entra login: $($ProjectRaw -join ' ')"
}
$Project = (($ProjectRaw -join "`n") | ConvertFrom-Json)
$ProjectId = [string]$Project.id
Write-Host "Azure DevOps project authenticated: $ProjectName ($ProjectId)"

Step 'Dedicated secretless Azure workload identity'
$IdentityRaw = & az identity show --name $ManagedIdentityName --resource-group $ResourceGroup --subscription $SubscriptionId --only-show-errors --output json 2>$null
if ($LASTEXITCODE -eq 0) {
    $Identity = (($IdentityRaw -join "`n") | ConvertFrom-Json)
    Write-Host "Reusing managed identity: $ManagedIdentityName"
} else {
    $Identity = AzJson @('identity','create','--name',$ManagedIdentityName,'--resource-group',$ResourceGroup,'--subscription',$SubscriptionId)
    Write-Host "Created managed identity: $ManagedIdentityName"
}
$ClientId = [string]$Identity.clientId
$PrincipalId = [string]$Identity.principalId

Step 'Azure Resource Manager workload-identity service connection'
$EndpointsRaw = & az devops service-endpoint list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list Azure DevOps service connections.' }
$Endpoints = @(($EndpointsRaw -join "`n") | ConvertFrom-Json)
$Endpoint = $Endpoints | Where-Object { $_.name -eq $ServiceConnectionName -and $_.type -eq 'AzureRM' } | Select-Object -First 1

if ($null -eq $Endpoint) {
    $Config = [ordered]@{
        data = [ordered]@{
            subscriptionId = $SubscriptionId
            subscriptionName = $SubscriptionName
            environment = 'AzureCloud'
            scopeLevel = 'Subscription'
            creationMode = 'Manual'
        }
        name = $ServiceConnectionName
        type = 'AzureRM'
        url = 'https://management.azure.com/'
        authorization = [ordered]@{
            parameters = [ordered]@{
                tenantid = $TenantId
                serviceprincipalid = $ClientId
            }
            scheme = 'WorkloadIdentityFederation'
        }
        isShared = $false
        isReady = $true
        serviceEndpointProjectReferences = @(
            [ordered]@{
                projectReference = [ordered]@{ id = $ProjectId; name = $ProjectName }
                name = $ServiceConnectionName
            }
        )
    }

    $ConfigPath = Join-Path $HOME 'eos-service-connection.json'
    $Config | ConvertTo-Json -Depth 20 | Set-Content -Path $ConfigPath -Encoding utf8
    try {
        $CreatedRaw = & az devops service-endpoint create --service-endpoint-configuration $ConfigPath --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
        if ($LASTEXITCODE -ne 0) { throw 'Could not create eos-vm-arm service connection.' }
        $Endpoint = (($CreatedRaw -join "`n") | ConvertFrom-Json)
        Write-Host "Created service connection: $ServiceConnectionName"
    } finally {
        Remove-Item $ConfigPath -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "Reusing service connection: $ServiceConnectionName"
}

$ServiceConnectionId = [string]$Endpoint.id
$EndpointDetailRaw = & az devops service-endpoint show --id $ServiceConnectionId --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not read eos-vm-arm service connection.' }
$EndpointDetail = (($EndpointDetailRaw -join "`n") | ConvertFrom-Json)
if ([string]$EndpointDetail.authorization.scheme -ne 'WorkloadIdentityFederation') {
    throw "Existing '$ServiceConnectionName' is not a Workload Identity Federation connection."
}
$Issuer = [string]$EndpointDetail.authorization.parameters.workloadIdentityFederationIssuer
$Subject = [string]$EndpointDetail.authorization.parameters.workloadIdentityFederationSubject
if ([string]::IsNullOrWhiteSpace($Issuer) -or [string]::IsNullOrWhiteSpace($Subject)) {
    throw 'Azure DevOps did not return the WIF issuer/subject.'
}

Step 'Federated credential linking Azure DevOps to the managed identity'
$FicsRaw = & az identity federated-credential list --identity-name $ManagedIdentityName --resource-group $ResourceGroup --subscription $SubscriptionId --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list federated credentials.' }
$Fics = @(($FicsRaw -join "`n") | ConvertFrom-Json)
$Fic = $Fics | Where-Object { $_.name -eq $FederatedCredentialName } | Select-Object -First 1
if ($null -eq $Fic) {
    & az identity federated-credential create --name $FederatedCredentialName --identity-name $ManagedIdentityName --resource-group $ResourceGroup --subscription $SubscriptionId --issuer $Issuer --subject $Subject --audiences 'api://AzureADTokenExchange' --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create managed-identity federated credential.' }
    Write-Host "Created federated credential: $FederatedCredentialName"
} else {
    Write-Host "Reusing federated credential: $FederatedCredentialName"
}

Step 'Least-privilege Azure RBAC for VM control'
$AzureScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
$RoleId = & az role assignment list --assignee $PrincipalId --scope $AzureScope --query "[?roleDefinitionName=='Virtual Machine Contributor'].id | [0]" --only-show-errors --output tsv
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Azure role assignments.' }
if ([string]::IsNullOrWhiteSpace(($RoleId -join "`n").Trim())) {
    & az role assignment create --assignee-object-id $PrincipalId --assignee-principal-type ServicePrincipal --role 'Virtual Machine Contributor' --scope $AzureScope --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not assign Virtual Machine Contributor.' }
    Write-Host "Granted Virtual Machine Contributor on EOS resource group only."
} else {
    Write-Host 'Virtual Machine Contributor role already present.'
}

Step 'EOS VM control variable group'
$VgsRaw = & az pipelines variable-group list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list variable groups.' }
$Vgs = @(($VgsRaw -join "`n") | ConvertFrom-Json)
$VariableGroup = $Vgs | Where-Object { $_.name -eq $VariableGroupName } | Select-Object -First 1
if ($null -eq $VariableGroup) {
    $CreatedVgRaw = & az pipelines variable-group create --name $VariableGroupName --description 'Non-secret EOS VM control identifiers.' --authorize true --variables "AZURE_SUBSCRIPTION_ID=$SubscriptionId" "AZURE_RESOURCE_GROUP=$ResourceGroup" "AZURE_VM_NAME=$VmName" --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not create eos-vm-control variable group.' }
    $VariableGroup = (($CreatedVgRaw -join "`n") | ConvertFrom-Json)
    Write-Host "Created variable group: $VariableGroupName"
} else {
    $GroupId = [string]$VariableGroup.id
    $Desired = [ordered]@{
        AZURE_SUBSCRIPTION_ID = $SubscriptionId
        AZURE_RESOURCE_GROUP = $ResourceGroup
        AZURE_VM_NAME = $VmName
    }
    $ExistingVarsRaw = & az pipelines variable-group variable list --group-id $GroupId --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not read eos-vm-control variables.' }
    $ExistingVars = (($ExistingVarsRaw -join "`n") | ConvertFrom-Json)
    foreach ($Entry in $Desired.GetEnumerator()) {
        if ($null -ne $ExistingVars.PSObject.Properties[$Entry.Key]) {
            & az pipelines variable-group variable update --group-id $GroupId --name $Entry.Key --value $Entry.Value --secret false --organization $OrganizationUrl --project $ProjectName --only-show-errors | Out-Null
        } else {
            & az pipelines variable-group variable create --group-id $GroupId --name $Entry.Key --value $Entry.Value --secret false --organization $OrganizationUrl --project $ProjectName --only-show-errors | Out-Null
        }
        if ($LASTEXITCODE -ne 0) { throw "Could not set variable $($Entry.Key)." }
    }
    Write-Host "Updated variable group: $VariableGroupName"
}

Step 'Create/reuse the manual EOS VM Control pipeline'
$PipelinesRaw = & az pipelines list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list pipelines.' }
$Pipelines = @(($PipelinesRaw -join "`n") | ConvertFrom-Json)
$ControlPipeline = $Pipelines | Where-Object { $_.name -eq $ControlPipelineName } | Select-Object -First 1
if ($null -eq $ControlPipeline) {
    $EndpointsRaw = & az devops service-endpoint list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not list GitHub service endpoints.' }
    $AllEndpoints = @(($EndpointsRaw -join "`n") | ConvertFrom-Json)
    $GitHubEndpoint = $AllEndpoints | Where-Object { $_.type -eq 'github' } | Select-Object -First 1
    if ($null -eq $GitHubEndpoint) { throw 'No GitHub service connection exists in EOS.' }

    $CreatedPipelineRaw = & az pipelines create --name $ControlPipelineName --description 'Manual agentless control/recovery for the EOS Windows VM.' --repository $GitHubRepository --repository-type github --branch main --service-connection ([string]$GitHubEndpoint.id) --yml-path $ControlYamlPath --skip-first-run true --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not create EOS VM Control pipeline.' }
    $ControlPipeline = (($CreatedPipelineRaw -join "`n") | ConvertFrom-Json)
    Write-Host "Created pipeline: $ControlPipelineName"
} else {
    Write-Host "Reusing pipeline: $ControlPipelineName"
}
$ControlPipelineId = [int]$ControlPipeline.id

Step 'Authorize EOS project pipelines to use the VM-control service connection'
# The documented GA CLI route is intentionally used here. Azure-side blast radius remains
# constrained by the managed identity's Virtual Machine Contributor role at the EOS RG only.
& az devops service-endpoint update --id $ServiceConnectionId --enable-for-all true --organization $OrganizationUrl --project $ProjectName --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not enable eos-vm-arm for EOS project pipelines.' }
Write-Host "Service connection '$ServiceConnectionName' is available to pipelines in project '$ProjectName'."

Step 'Run an agentless status check to validate the control plane'
$RunRaw = & az pipelines run --id $ControlPipelineId --branch main --parameters operation=status --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not queue EOS VM Control status run.' }
$Run = (($RunRaw -join "`n") | ConvertFrom-Json)
$RunId = [int]$Run.id
Write-Host "Queued validation run: $RunId"

$Deadline = (Get-Date).AddMinutes(7)
$Status = $null
do {
    Start-Sleep -Seconds 5
    $StatusRaw = & az pipelines runs show --id $RunId --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not read validation run status.' }
    $Status = (($StatusRaw -join "`n") | ConvertFrom-Json)
    Write-Host "  status=$($Status.status) result=$($Status.result)"
} while ($Status.status -ne 'completed' -and (Get-Date) -lt $Deadline)

if ($Status.status -ne 'completed') { throw "Validation run $RunId did not finish within seven minutes." }
if ($Status.result -ne 'succeeded') { throw "Validation run $RunId completed with result '$($Status.result)'." }

Step 'Bootstrap complete'
Write-Host 'Azure DevOps VM control is operational.' -ForegroundColor Green
Write-Host "Managed identity:       $ManagedIdentityName"
Write-Host "Azure RBAC:             Virtual Machine Contributor @ EOS resource group only"
Write-Host "ARM WIF connection:     $ServiceConnectionName"
Write-Host "Variable group:         $VariableGroupName"
Write-Host "Control pipeline:       $ControlPipelineName"
Write-Host "Validated operation:    status"
Write-Host 'No PAT or client secret is used by this control path.' -ForegroundColor Green
