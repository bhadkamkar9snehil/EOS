[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$OrganizationUrl = 'https://dev.azure.com/apexasnehil'
$ProjectName = 'EOS'
$EosPipelineName = 'EOS CI'
$GenericPipelineName = 'Windows Build Lab'
$ApsPipelineName = 'APS CI'

function Convert-JsonText {
    param([object]$Value)
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

function Ensure-Pipeline {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$YamlPath,
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][string]$ServiceConnectionId
    )

    $pipelines = @(Invoke-AzJson -Arguments @(
        'pipelines','list',
        '--organization',$OrganizationUrl,
        '--project',$ProjectName,
        '--only-show-errors',
        '--output','json'
    ))

    $existing = $pipelines | Where-Object { [string]$_.name -eq $Name } | Select-Object -First 1
    if ($null -ne $existing) {
        $pipelineId = [int]$existing.id
        & az pipelines update `
            --id $pipelineId `
            --new-name $Name `
            --description $Description `
            --organization $OrganizationUrl `
            --project $ProjectName `
            --only-show-errors `
            --output none
        if ($LASTEXITCODE -ne 0) { throw "Could not update pipeline '$Name'." }
        Write-Host "Pipeline already present: $Name (#$pipelineId)"
        return
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

    if ($null -eq $created -or [int]$created.id -le 0) {
        throw "Pipeline '$Name' was not created correctly."
    }
    Write-Host "Created pipeline: $Name (#$($created.id))"
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

$pipelines = @(Invoke-AzJson -Arguments @(
    'pipelines','list',
    '--organization',$OrganizationUrl,
    '--project',$ProjectName,
    '--only-show-errors',
    '--output','json'
))

$eosPipeline = $pipelines | Where-Object { [string]$_.name -eq $EosPipelineName } | Select-Object -First 1
if ($null -eq $eosPipeline) {
    $eosPipeline = $pipelines | Where-Object { [string]$_.name -eq 'bhadkamkar9snehil.EOS' } | Select-Object -First 1
}
if ($null -eq $eosPipeline) {
    throw "Could not find the existing EOS CI pipeline needed to discover the GitHub service connection."
}

$eosDefinition = Invoke-AzJson -Arguments @(
    'pipelines','show',
    '--id',([string]$eosPipeline.id),
    '--organization',$OrganizationUrl,
    '--project',$ProjectName,
    '--only-show-errors',
    '--output','json'
)

$repository = Get-PropertyValue -Object $eosDefinition -Name 'repository'
$properties = Get-PropertyValue -Object $repository -Name 'properties'
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

if ([string]::IsNullOrWhiteSpace($serviceConnectionId)) {
    throw 'No Azure DevOps GitHub service connection could be discovered.'
}

Write-Host "Using GitHub service connection: $serviceConnectionId"

Ensure-Pipeline `
    -Name $GenericPipelineName `
    -Repository 'https://github.com/bhadkamkar9snehil/EOS' `
    -YamlPath 'azure-generic-windows-build.yml' `
    -Description 'Manual branch/tag/SHA-selectable Windows verification for EOS and APS on the shared EOS Azure VM agent.' `
    -ServiceConnectionId $serviceConnectionId

Ensure-Pipeline `
    -Name $ApsPipelineName `
    -Repository 'https://github.com/bhadkamkar9snehil/APS' `
    -YamlPath 'azure-pipelines.yml' `
    -Description 'Branch-agnostic APS Windows build, tests and desktop publish validation on the shared EOS Azure VM agent.' `
    -ServiceConnectionId $serviceConnectionId

Write-Host 'Shared Windows pipeline reconciliation complete.'
