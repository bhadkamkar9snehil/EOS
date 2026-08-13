$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# EOS Azure / Azure DevOps bootstrap.
# Safe to rerun: existing resources are discovered and reused.

$OrganizationUrl = 'https://dev.azure.com/apexasnehil'
$OrganizationName = 'apexasnehil'
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
$AzureDevOpsResourceId = '499b84ac-1321-427f-aa17-267ca6975798'

function Write-Step([string]$Message) {
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $raw = & az @Arguments --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')"
    }
    if ([string]::IsNullOrWhiteSpace(($raw -join "`n"))) { return $null }
    return (($raw -join "`n") | ConvertFrom-Json)
}

function Invoke-AzTsv {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $raw = & az @Arguments --only-show-errors --output tsv
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')"
    }
    return (($raw -join "`n").Trim())
}

Write-Step 'Azure account and EOS VM discovery'
& az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) { throw "Cannot select Azure subscription $SubscriptionId." }

$account = Invoke-AzJson @('account','show')
$TenantId = [string]$account.tenantId
$SubscriptionName = [string]$account.name
$SignedInUser = [string]$account.user.name

$ResourceGroup = Invoke-AzTsv @(
    'vm','list',
    '--query', "[?name=='$VmName'].resourceGroup | [0]"
)
if ([string]::IsNullOrWhiteSpace($ResourceGroup)) {
    throw "Could not find Azure VM '$VmName' in subscription $SubscriptionId."
}

Write-Host "Signed in:       $SignedInUser"
Write-Host "Tenant:          $TenantId"
Write-Host "Subscription:    $SubscriptionName ($SubscriptionId)"
Write-Host "VM:              $VmName"
Write-Host "Resource group:  $ResourceGroup"

Write-Step 'Azure DevOps CLI and Microsoft Entra authentication'
$extensionExists = (& az extension show --name azure-devops --only-show-errors 2>$null)
if ($LASTEXITCODE -eq 0) {
    & az extension update --name azure-devops --only-show-errors | Out-Null
} else {
    & az extension add --name azure-devops --only-show-errors | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw 'Could not install/update the azure-devops Azure CLI extension.' }

& az devops configure --defaults organization=$OrganizationUrl project=$ProjectName
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Azure DevOps CLI defaults.' }

# Azure DevOps CLI can reuse `az login` only when the organization is backed by the same Entra tenant.
$projectProbe = & az devops project show --project $ProjectName --organization $OrganizationUrl --only-show-errors --output json 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nAzure itself is authenticated, but Azure DevOps rejected Microsoft Entra authentication." -ForegroundColor Yellow
    Write-Host 'This normally means the Azure DevOps organization is still not connected to this Entra directory.' -ForegroundColor Yellow
    Write-Host "Tenant ID discovered automatically: $TenantId" -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'ONE-TIME UI ACTION:' -ForegroundColor White
    Write-Host '  Azure DevOps -> Organization settings -> Microsoft Entra ID -> Connect directory' -ForegroundColor White
    Write-Host '  Select the directory containing the Azure subscription above, connect it, then sign out/in.' -ForegroundColor White
    Write-Host ''
    Write-Host 'After that, rerun this exact bootstrap script. It is idempotent.' -ForegroundColor Green
    Write-Host "`nAzure DevOps returned:`n$($projectProbe -join "`n")" -ForegroundColor DarkGray
    exit 20
}
$Project = (($projectProbe -join "`n") | ConvertFrom-Json)
$ProjectId = [string]$Project.id
Write-Host "Azure DevOps project authenticated: $ProjectName ($ProjectId)"

# Also acquire a short-lived bearer token for the few Azure DevOps REST calls that do not have
# convenient CLI wrappers. No PAT is created or stored.
$AdoAccessToken = Invoke-AzTsv @(
    'account','get-access-token',
    '--resource', $AzureDevOpsResourceId,
    '--query', 'accessToken'
)
$AdoHeaders = @{
    Authorization = "Bearer $AdoAccessToken"
    Accept = 'application/json'
    'Content-Type' = 'application/json'
}

Write-Step 'Dedicated secretless Azure workload identity'
$identityRaw = & az identity show --name $ManagedIdentityName --resource-group $ResourceGroup --subscription $SubscriptionId --only-show-errors --output json 2>$null
if ($LASTEXITCODE -eq 0) {
    $Identity = (($identityRaw -join "`n") | ConvertFrom-Json)
    Write-Host "Reusing managed identity: $ManagedIdentityName"
} else {
    $Identity = Invoke-AzJson @(
        'identity','create',
        '--name', $ManagedIdentityName,
        '--resource-group', $ResourceGroup,
        '--subscription', $SubscriptionId
    )
    Write-Host "Created managed identity: $ManagedIdentityName"
}
$ClientId = [string]$Identity.clientId
$PrincipalId = [string]$Identity.principalId

Write-Step 'Azure Resource Manager workload-identity service connection'
$existingEndpointRaw = & az devops service-endpoint list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list Azure DevOps service connections.' }
$existingEndpoints = @(($existingEndpointRaw -join "`n") | ConvertFrom-Json)
$Endpoint = $existingEndpoints | Where-Object { $_.name -eq $ServiceConnectionName -and $_.type -eq 'AzureRM' } | Select-Object -First 1

if ($null -eq $Endpoint) {
    $endpointConfig = [ordered]@{
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
                projectReference = [ordered]@{
                    id = $ProjectId
                    name = $ProjectName
                }
                name = $ServiceConnectionName
            }
        )
    }

    $configPath = Join-Path $HOME 'eos-service-connection.json'
    $endpointConfig | ConvertTo-Json -Depth 20 | Set-Content -Path $configPath -Encoding utf8

    $createdRaw = & az devops service-endpoint create `
        --service-endpoint-configuration $configPath `
        --organization $OrganizationUrl `
        --project $ProjectName `
        --only-show-errors `
        --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not create eos-vm-arm service connection.' }
    $Endpoint = (($createdRaw -join "`n") | ConvertFrom-Json)
    Remove-Item $configPath -Force -ErrorAction SilentlyContinue
    Write-Host "Created service connection: $ServiceConnectionName"
} else {
    Write-Host "Reusing service connection: $ServiceConnectionName"
}

$ServiceConnectionId = [string]$Endpoint.id
$endpointDetailRaw = & az devops service-endpoint show --id $ServiceConnectionId --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not read eos-vm-arm service connection details.' }
$EndpointDetail = (($endpointDetailRaw -join "`n") | ConvertFrom-Json)
if ([string]$EndpointDetail.authorization.scheme -ne 'WorkloadIdentityFederation') {
    throw "Existing '$ServiceConnectionName' is not a Workload Identity Federation connection. Delete/rename it and rerun."
}
$Issuer = [string]$EndpointDetail.authorization.parameters.workloadIdentityFederationIssuer
$Subject = [string]$EndpointDetail.authorization.parameters.workloadIdentityFederationSubject
if ([string]::IsNullOrWhiteSpace($Issuer) -or [string]::IsNullOrWhiteSpace($Subject)) {
    throw 'Azure DevOps did not return the WIF issuer/subject for eos-vm-arm.'
}

Write-Step 'Federated credential linking Azure DevOps to the managed identity'
$ficRaw = & az identity federated-credential list --identity-name $ManagedIdentityName --resource-group $ResourceGroup --subscription $SubscriptionId --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list managed-identity federated credentials.' }
$fics = @(($ficRaw -join "`n") | ConvertFrom-Json)
$fic = $fics | Where-Object { $_.name -eq $FederatedCredentialName } | Select-Object -First 1
if ($null -eq $fic) {
    & az identity federated-credential create `
        --name $FederatedCredentialName `
        --identity-name $ManagedIdentityName `
        --resource-group $ResourceGroup `
        --subscription $SubscriptionId `
        --issuer $Issuer `
        --subject $Subject `
        --audiences 'api://AzureADTokenExchange' `
        --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create managed-identity federated credential.' }
    Write-Host "Created federated credential: $FederatedCredentialName"
} else {
    if ([string]$fic.issuer -ne $Issuer -or [string]$fic.subject -ne $Subject) {
        throw "Federated credential '$FederatedCredentialName' already exists but points at a different issuer/subject."
    }
    Write-Host "Reusing federated credential: $FederatedCredentialName"
}

Write-Step 'Least-privilege Azure RBAC for VM control'
$AzureScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
$roleId = & az role assignment list `
    --assignee $PrincipalId `
    --scope $AzureScope `
    --query "[?roleDefinitionName=='Virtual Machine Contributor'].id | [0]" `
    --only-show-errors `
    --output tsv
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Azure role assignments.' }
if ([string]::IsNullOrWhiteSpace(($roleId -join "`n").Trim())) {
    & az role assignment create `
        --assignee-object-id $PrincipalId `
        --assignee-principal-type ServicePrincipal `
        --role 'Virtual Machine Contributor' `
        --scope $AzureScope `
        --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not assign Virtual Machine Contributor. Your Azure account needs permission to create role assignments.'
    }
    Write-Host "Granted Virtual Machine Contributor only on: $AzureScope"
} else {
    Write-Host 'Virtual Machine Contributor role already present at the EOS resource-group scope.'
}

Write-Step 'EOS VM control variable group'
$vgRaw = & az pipelines variable-group list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list Azure DevOps variable groups.' }
$vgs = @(($vgRaw -join "`n") | ConvertFrom-Json)
$VariableGroup = $vgs | Where-Object { $_.name -eq $VariableGroupName } | Select-Object -First 1

if ($null -eq $VariableGroup) {
    $vgCreatedRaw = & az pipelines variable-group create `
        --name $VariableGroupName `
        --description 'Non-secret identifiers used by the agentless EOS VM control pipeline.' `
        --authorize true `
        --variables `
            "AZURE_SUBSCRIPTION_ID=$SubscriptionId" `
            "AZURE_RESOURCE_GROUP=$ResourceGroup" `
            "AZURE_VM_NAME=$VmName" `
        --organization $OrganizationUrl `
        --project $ProjectName `
        --only-show-errors `
        --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not create eos-vm-control variable group.' }
    $VariableGroup = (($vgCreatedRaw -join "`n") | ConvertFrom-Json)
    Write-Host "Created variable group: $VariableGroupName"
} else {
    $VariableGroupId = [string]$VariableGroup.id
    $desired = [ordered]@{
        AZURE_SUBSCRIPTION_ID = $SubscriptionId
        AZURE_RESOURCE_GROUP = $ResourceGroup
        AZURE_VM_NAME = $VmName
    }
    $existingVarsRaw = & az pipelines variable-group variable list --group-id $VariableGroupId --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not read eos-vm-control variables.' }
    $existingVars = (($existingVarsRaw -join "`n") | ConvertFrom-Json)
    foreach ($entry in $desired.GetEnumerator()) {
        if ($null -ne $existingVars.PSObject.Properties[$entry.Key]) {
            & az pipelines variable-group variable update --group-id $VariableGroupId --name $entry.Key --value $entry.Value --secret false --organization $OrganizationUrl --project $ProjectName --only-show-errors | Out-Null
        } else {
            & az pipelines variable-group variable create --group-id $VariableGroupId --name $entry.Key --value $entry.Value --secret false --organization $OrganizationUrl --project $ProjectName --only-show-errors | Out-Null
        }
        if ($LASTEXITCODE -ne 0) { throw "Could not set variable $($entry.Key)." }
    }
    Write-Host "Updated variable group: $VariableGroupName"
}
$VariableGroupId = [string]$VariableGroup.id

Write-Step 'Create/reuse the manual EOS VM Control pipeline'
$pipelinesRaw = & az pipelines list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list Azure Pipelines definitions.' }
$pipelines = @(($pipelinesRaw -join "`n") | ConvertFrom-Json)
$ControlPipeline = $pipelines | Where-Object { $_.name -eq $ControlPipelineName } | Select-Object -First 1

if ($null -eq $ControlPipeline) {
    $endpointsRaw = & az devops service-endpoint list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not list service endpoints to locate the existing GitHub App connection.' }
    $endpoints = @(($endpointsRaw -join "`n") | ConvertFrom-Json)
    $GitHubEndpoint = $endpoints | Where-Object { $_.type -eq 'github' } | Select-Object -First 1
    if ($null -eq $GitHubEndpoint) {
        throw 'No GitHub service connection exists in the EOS project. The normal CI pipeline currently proves one should exist.'
    }

    $pipelineCreatedRaw = & az pipelines create `
        --name $ControlPipelineName `
        --description 'Manual agentless start/restart/health/repair control plane for the EOS Windows VM.' `
        --repository $GitHubRepository `
        --repository-type github `
        --branch main `
        --service-connection ([string]$GitHubEndpoint.id) `
        --yml-path $ControlYamlPath `
        --skip-first-run true `
        --organization $OrganizationUrl `
        --project $ProjectName `
        --only-show-errors `
        --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not create EOS VM Control pipeline.' }
    $ControlPipeline = (($pipelineCreatedRaw -join "`n") | ConvertFrom-Json)
    Write-Host "Created pipeline: $ControlPipelineName"
} else {
    Write-Host "Reusing pipeline: $ControlPipelineName"
}
$ControlPipelineId = [int]$ControlPipeline.id

Write-Step 'Authorize only the VM Control pipeline to use eos-vm-arm'
$permissionBody = @(
    [ordered]@{
        resource = [ordered]@{
            type = 'endpoint'
            id = $ServiceConnectionId
            name = $ServiceConnectionName
        }
        pipelines = @(
            [ordered]@{
                id = $ControlPipelineId
                authorized = $true
            }
        )
    }
) | ConvertTo-Json -Depth 10
$permissionUri = "$OrganizationUrl/$ProjectName/_apis/pipelines/pipelinepermissions?api-version=7.1-preview.1"
try {
    Invoke-RestMethod -Uri $permissionUri -Headers $AdoHeaders -Method Patch -Body $permissionBody | Out-Null
    Write-Host "Authorized pipeline '$ControlPipelineName' for '$ServiceConnectionName' only."
} catch {
    throw "Service-connection creation succeeded, but pipeline-specific authorization failed: $($_.Exception.Message)"
}

Write-Step 'Run an agentless status check to validate the control plane'
$runRaw = & az pipelines run `
    --id $ControlPipelineId `
    --branch main `
    --parameters operation=status `
    --organization $OrganizationUrl `
    --project $ProjectName `
    --only-show-errors `
    --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not queue EOS VM Control status run.' }
$run = (($runRaw -join "`n") | ConvertFrom-Json)
$runId = [int]$run.id
Write-Host "Queued validation run: $runId"

$deadline = (Get-Date).AddMinutes(5)
do {
    Start-Sleep -Seconds 5
    $statusRaw = & az pipelines runs show --id $runId --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Could not read validation run status.' }
    $status = (($statusRaw -join "`n") | ConvertFrom-Json)
    Write-Host "  status=$($status.status) result=$($status.result)"
} while ($status.status -ne 'completed' -and (Get-Date) -lt $deadline)

if ($status.status -ne 'completed') {
    throw "Validation run $runId did not finish within five minutes."
}
if ($status.result -ne 'succeeded') {
    throw "Validation run $runId completed with result '$($status.result)'."
}

Write-Step 'Bootstrap complete'
Write-Host 'Azure DevOps now has:' -ForegroundColor Green
Write-Host "  - existing EOS Windows self-hosted CI agent/pipeline (unchanged)"
Write-Host "  - managed identity:          $ManagedIdentityName"
Write-Host "  - ARM WIF service connection: $ServiceConnectionName"
Write-Host "  - Azure RBAC:                 Virtual Machine Contributor @ EOS resource group only"
Write-Host "  - variable group:             $VariableGroupName (non-secret identifiers only)"
Write-Host "  - agentless control pipeline: $ControlPipelineName"
Write-Host "  - validated operation:        status"
Write-Host ''
Write-Host 'No PAT or client secret is used by this control path.' -ForegroundColor Green
