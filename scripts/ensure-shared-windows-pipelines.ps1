[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$OrganizationUrl = 'https://dev.azure.com/apexasnehil'
$ProjectName = 'EOS'
$PoolName = 'Default'
$AgentName = 'EOS'
$EosPipelineName = 'EOS CI'
$GenericPipelineName = 'Windows Build Lab'
$ApsPipelineName = 'APS CI'

function Convert-JsonText {
    param([object]$Value)
    if ($null -eq $Value) { return $null }
    $text = ($Value -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return ($text | ConvertFrom-Json)
}

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $raw = & az @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')`n$($raw -join "`n")"
    }
    return Convert-JsonText -Value $raw
}

function Get-PropertyValue {
    param([object]$Object, [Parameter(Mandatory)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-Pipelines {
    return @(Invoke-AzJson -Arguments @(
        'pipelines','list',
        '--organization',$OrganizationUrl,
        '--project',$ProjectName,
        '--only-show-errors',
        '--output','json'
    ))
}

function Assert-SharedAgentOnline {
    $pools = @(Invoke-AzJson -Arguments @(
        'pipelines','pool','list',
        '--pool-name',$PoolName,
        '--organization',$OrganizationUrl,
        '--only-show-errors',
        '--output','json'
    ))
    $pool = $pools | Where-Object { [string]$_.name -eq $PoolName } | Select-Object -First 1
    if ($null -eq $pool) { throw "Azure DevOps agent pool '$PoolName' does not exist." }

    $agents = @(Invoke-AzJson -Arguments @(
        'pipelines','agent','list',
        '--pool-id',([string]$pool.id),
        '--agent-name',$AgentName,
        '--include-capabilities','true',
        '--organization',$OrganizationUrl,
        '--only-show-errors',
        '--output','json'
    ))
    $agent = $agents | Where-Object { [string]$_.name -eq $AgentName } | Select-Object -First 1
    if ($null -eq $agent) { throw "Agent '$AgentName' is not registered in pool '$PoolName'." }
    if ($agent.enabled -ne $true) { throw "Agent '$AgentName' is registered but disabled." }
    if ([string]$agent.status -ne 'online') { throw "Agent '$AgentName' is not online; current status is '$($agent.status)'." }

    Write-Host "Shared Windows agent healthy: pool '$PoolName' (#$($pool.id)), agent '$AgentName' (#$($agent.id)), status=$($agent.status)."
}

function Ensure-Pipeline {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$YamlPath,
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][string]$ServiceConnectionId
    )

    $expectedRepository = ($Repository -replace '^https://github\.com/','' -replace '\.git$','').Trim('/')
    $existing = Get-Pipelines | Where-Object { [string]$_.name -eq $Name } | Select-Object -First 1

    if ($null -ne $existing) {
        $pipelineId = [int]$existing.id
        $definition = Invoke-AzJson -Arguments @(
            'pipelines','show',
            '--id',([string]$pipelineId),
            '--organization',$OrganizationUrl,
            '--project',$ProjectName,
            '--only-show-errors',
            '--output','json'
        )
        $repositoryDefinition = Get-PropertyValue -Object $definition -Name 'repository'
        $actualName = [string](Get-PropertyValue -Object $repositoryDefinition -Name 'name')
        $actualUrl = [string](Get-PropertyValue -Object $repositoryDefinition -Name 'url')
        $repositoryMatches = $actualName -eq $expectedRepository -or $actualUrl.TrimEnd('/') -eq $Repository.TrimEnd('/') -or $actualUrl -like "*$expectedRepository*"
        if (-not $repositoryMatches) {
            throw "Pipeline '$Name' (#$pipelineId) points at repository '$actualName' / '$actualUrl', expected '$expectedRepository'. Refusing to reuse the wrong repository definition."
        }

        & az pipelines update `
            --id $pipelineId `
            --new-name $Name `
            --description $Description `
            --branch main `
            --yml-path $YamlPath `
            --organization $OrganizationUrl `
            --project $ProjectName `
            --only-show-errors `
            --output none
        if ($LASTEXITCODE -ne 0) { throw "Could not reconcile pipeline '$Name'." }

        Write-Host "Reconciled pipeline: $Name (#$pipelineId), repo=$expectedRepository, yaml=$YamlPath"
        return [pscustomobject]@{ Id = $pipelineId; Created = $false }
    }

    Write-Host "Creating pipeline: $Name"
    $created = Invoke-AzJson -Arguments @(
        'pipelines','create',
        '--name',$Name,
        '--description',$Description,
        '--repository',$Repository,
        '--branch','main',
        '--repository-type','github',
        '--yml-path',$YamlPath,
        '--service-connection',$ServiceConnectionId,
        '--skip-first-run','true',
        '--organization',$OrganizationUrl,
        '--project',$ProjectName,
        '--only-show-errors',
        '--output','json'
    )
    if ($null -eq $created -or [int]$created.id -le 0) { throw "Pipeline '$Name' was not created correctly." }

    $pipelineId = [int]$created.id
    Write-Host "Created pipeline: $Name (#$pipelineId), repo=$expectedRepository, yaml=$YamlPath"
    return [pscustomobject]@{ Id = $pipelineId; Created = $true }
}

function Ensure-InitialApsRun {
    param([Parameter(Mandatory)][int]$PipelineId)

    $runs = @(Invoke-AzJson -Arguments @(
        'pipelines','runs','list',
        '--pipeline-ids',([string]$PipelineId),
        '--top','1',
        '--query-order','QueueTimeDesc',
        '--organization',$OrganizationUrl,
        '--project',$ProjectName,
        '--only-show-errors',
        '--output','json'
    ))
    if ($runs.Count -gt 0) {
        Write-Host "APS CI already has run history; latest run is #$($runs[0].id), status=$($runs[0].status), result=$($runs[0].result)."
        return
    }

    Write-Host "APS CI has no run history; queuing initial APS main verification on pipeline #$PipelineId."
    $initialRun = Invoke-AzJson -Arguments @(
        'pipelines','run',
        '--id',([string]$PipelineId),
        '--branch','main',
        '--organization',$OrganizationUrl,
        '--project',$ProjectName,
        '--only-show-errors',
        '--output','json'
    )
    if ($null -eq $initialRun -or [int]$initialRun.id -le 0) { throw 'APS CI initial main verification could not be queued.' }
    Write-Host "Queued APS main validation run #$($initialRun.id)."
}

if ([string]::IsNullOrWhiteSpace($env:AZURE_DEVOPS_EXT_PAT) -and -not [string]::IsNullOrWhiteSpace($env:SYSTEM_ACCESSTOKEN)) {
    $env:AZURE_DEVOPS_EXT_PAT = $env:SYSTEM_ACCESSTOKEN
}
if ([string]::IsNullOrWhiteSpace($env:AZURE_DEVOPS_EXT_PAT)) {
    throw 'Azure DevOps authentication is unavailable. Map $(System.AccessToken) to SYSTEM_ACCESSTOKEN.'
}

& az extension add --name azure-devops --upgrade --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not install/update the Azure DevOps CLI extension.' }
& az devops configure --defaults organization=$OrganizationUrl project=$ProjectName
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Azure DevOps CLI defaults.' }

Assert-SharedAgentOnline

$pipelines = Get-Pipelines
$eosPipeline = $pipelines | Where-Object { [string]$_.name -eq $EosPipelineName } | Select-Object -First 1
if ($null -eq $eosPipeline) {
    $eosPipeline = $pipelines | Where-Object { [string]$_.name -eq 'bhadkamkar9snehil.EOS' } | Select-Object -First 1
}
if ($null -eq $eosPipeline) { throw 'Could not find the existing EOS CI pipeline needed to discover the GitHub service connection.' }

$eosDefinition = Invoke-AzJson -Arguments @(
    'pipelines','show',
    '--id',([string]$eosPipeline.id),
    '--organization',$OrganizationUrl,
    '--project',$ProjectName,
    '--only-show-errors',
    '--output','json'
)
$repositoryDefinition = Get-PropertyValue -Object $eosDefinition -Name 'repository'
$properties = Get-PropertyValue -Object $repositoryDefinition -Name 'properties'
$serviceConnectionId = [string](Get-PropertyValue -Object $properties -Name 'connectedServiceId')

if ([string]::IsNullOrWhiteSpace($serviceConnectionId)) {
    Write-Host 'EOS pipeline did not expose connectedServiceId; searching project GitHub service endpoints.'
    $endpoints = @(Invoke-AzJson -Arguments @(
        'devops','service-endpoint','list',
        '--organization',$OrganizationUrl,
        '--project',$ProjectName,
        '--only-show-errors',
        '--output','json'
    ))
    $githubEndpoint = $endpoints | Where-Object { [string]$_.type -eq 'github' } | Select-Object -First 1
    if ($null -ne $githubEndpoint) { $serviceConnectionId = [string]$githubEndpoint.id }
}
if ([string]::IsNullOrWhiteSpace($serviceConnectionId)) { throw 'No Azure DevOps GitHub service connection could be discovered.' }
Write-Host "Using GitHub service connection: $serviceConnectionId"

$genericPipeline = Ensure-Pipeline `
    -Name $GenericPipelineName `
    -Repository 'https://github.com/bhadkamkar9snehil/EOS' `
    -YamlPath 'azure-generic-windows-build.yml' `
    -Description 'Manual branch/tag/SHA-selectable Windows verification for EOS and APS on the shared EOS Azure VM agent.' `
    -ServiceConnectionId $serviceConnectionId

$apsPipeline = Ensure-Pipeline `
    -Name $ApsPipelineName `
    -Repository 'https://github.com/bhadkamkar9snehil/APS' `
    -YamlPath 'azure-pipelines.yml' `
    -Description 'Branch-agnostic APS Windows build, tests and desktop publish validation on the shared EOS Azure VM agent.' `
    -ServiceConnectionId $serviceConnectionId

Ensure-InitialApsRun -PipelineId $apsPipeline.Id
Write-Host "Shared Windows pipeline reconciliation complete. Windows Build Lab #$($genericPipeline.Id); APS CI #$($apsPipeline.Id)."
