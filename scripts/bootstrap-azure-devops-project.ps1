$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Idempotent EOS Azure DevOps project bootstrap.
#
# Authentication:
# - Interactive use: existing `az login` / Azure DevOps CLI session.
# - Azure Pipelines: map $(System.AccessToken) to SYSTEM_ACCESSTOKEN. This script
#   promotes it to AZURE_DEVOPS_EXT_PAT for non-interactive Azure DevOps CLI use.
#
# GitHub remains canonical for code and pull requests. Azure DevOps owns Boards,
# Windows CI/test history, dashboards, and Azure VM operations.

$OrganizationUrl = 'https://dev.azure.com/apexasnehil'
$ProjectName = 'EOS'
$DashboardName = 'EOS Engineering'
$DashboardDescription = 'EOS engineering cockpit for Boards, CI, tests, and operations. GitHub remains canonical for code and pull requests.'
$CiPipelineName = 'EOS CI'
$ControlPipelineName = 'EOS VM Control'
$GitHubRepositoryUrl = 'https://github.com/bhadkamkar9snehil/EOS'
$GitHubPr12Url = 'https://github.com/bhadkamkar9snehil/EOS/pull/12'
$AzureVmUrl = 'https://portal.azure.com/#view/HubsExtension/BrowseResource/resourceType/Microsoft.Compute%2FvirtualMachines'
$StableApiVersion = '7.1'
$PreviewApiVersion = '7.1-preview'

if ($DashboardDescription.Length -gt 128) {
    throw "Dashboard description exceeds Azure DevOps' 128-character limit: $($DashboardDescription.Length)."
}

function Step {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
}

function Convert-JsonText {
    param([object]$Value)
    if ($null -eq $Value) { return $null }
    $text = ($Value -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return ($text | ConvertFrom-Json)
}

function Get-OptionalProperty {
    param([object]$Object, [Parameter(Mandatory)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-Items {
    param([object]$Object, [string]$PropertyName = 'value')
    $value = Get-OptionalProperty -Object $Object -Name $PropertyName
    if ($null -eq $value) { return @() }
    return @($value)
}

function Invoke-AdoCli {
    param(
        [Parameter(Mandatory)][string]$Area,
        [Parameter(Mandatory)][string]$Resource,
        [ValidateSet('GET','POST','PUT','PATCH','DELETE')][string]$Method = 'GET',
        [Parameter(Mandatory)][string]$ApiVersion,
        [hashtable]$RouteParameters = @{},
        [hashtable]$QueryParameters = @{},
        [object]$Body = $null
    )

    if ($ApiVersion -notmatch '^\d+\.\d+(-preview)?$') {
        throw "Unsupported az devops invoke API version '$ApiVersion'."
    }

    $commandArgs = @(
        'devops','invoke',
        '--area',$Area,
        '--resource',$Resource,
        '--http-method',$Method,
        '--api-version',$ApiVersion,
        '--organization',$OrganizationUrl,
        '--only-show-errors',
        '--output','json'
    )

    if ($RouteParameters.Count -gt 0) {
        $commandArgs += '--route-parameters'
        foreach ($entry in $RouteParameters.GetEnumerator()) {
            $commandArgs += "$($entry.Key)=$($entry.Value)"
        }
    }

    if ($QueryParameters.Count -gt 0) {
        $commandArgs += '--query-parameters'
        foreach ($entry in $QueryParameters.GetEnumerator()) {
            $commandArgs += "$($entry.Key)=$($entry.Value)"
        }
    }

    $bodyPath = $null
    if ($null -ne $Body) {
        $bodyPath = Join-Path ([System.IO.Path]::GetTempPath()) ("eos-ado-{0}.json" -f ([guid]::NewGuid().ToString('N')))
        $Body | ConvertTo-Json -Depth 30 | Set-Content -Path $bodyPath -Encoding utf8
        $commandArgs += @('--in-file',$bodyPath)
    }

    try {
        $raw = & az @commandArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Azure DevOps CLI invoke failed [$Area/$Resource $Method]: $($raw -join ' ')"
        }
        return (Convert-JsonText -Value $raw)
    }
    finally {
        if ($null -ne $bodyPath) {
            Remove-Item $bodyPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-ExactWorkItem {
    param([Parameter(Mandatory)][string]$Type, [Parameter(Mandatory)][string]$Title)
    $safeTitle = $Title.Replace("'", "''")
    $wiql = "Select [System.Id], [System.Title], [System.WorkItemType], [System.State] From WorkItems Where [System.TeamProject] = '$ProjectName' AND [System.WorkItemType] = '$Type' AND [System.Title] = '$safeTitle'"
    $raw = & az boards query --wiql $wiql --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not query Azure Boards for '$Title'." }
    return @(Convert-JsonText -Value $raw) | Select-Object -First 1
}

function Ensure-WorkItem {
    param(
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$State,
        [Parameter(Mandatory)][string]$Tags,
        [Parameter(Mandatory)][string]$Description
    )

    $item = Get-ExactWorkItem -Type $Type -Title $Title
    if ($null -ne $item) {
        Write-Host "Reusing $Type #$([int](Get-OptionalProperty -Object $item -Name 'id')): $Title"
        return $item
    }

    $raw = & az boards work-item create `
        --type $Type `
        --title $Title `
        --description $Description `
        --fields "System.Tags=$Tags" `
        --organization $OrganizationUrl `
        --project $ProjectName `
        --only-show-errors `
        --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not create work item '$Title'." }
    $item = Convert-JsonText -Value $raw
    $itemId = [int](Get-OptionalProperty -Object $item -Name 'id')
    if ($itemId -le 0) { throw "Created work item '$Title' has no id." }

    & az boards work-item update `
        --id $itemId `
        --state $State `
        --organization $OrganizationUrl `
        --only-show-errors `
        --output none
    if ($LASTEXITCODE -ne 0) { throw "Could not set state for work item '$Title'." }

    Write-Host "Created $Type #$itemId: $Title"
    return (Get-ExactWorkItem -Type $Type -Title $Title)
}

function Ensure-Parent {
    param([Parameter(Mandatory)][int]$ChildId, [Parameter(Mandatory)][int]$ParentId)

    $raw = & az boards work-item relation show --id $ChildId --organization $OrganizationUrl --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect relations for work item #$ChildId." }
    $item = Convert-JsonText -Value $raw

    foreach ($relation in @(Get-OptionalProperty -Object $item -Name 'relations')) {
        $attributes = Get-OptionalProperty -Object $relation -Name 'attributes'
        $name = [string](Get-OptionalProperty -Object $attributes -Name 'name')
        $url = [string](Get-OptionalProperty -Object $relation -Name 'url')
        if ($name -eq 'Parent' -and $url.EndsWith("/$ParentId")) {
            Write-Host "Hierarchy already linked: #$ChildId -> parent #$ParentId"
            return
        }
    }

    & az boards work-item relation add `
        --id $ChildId `
        --relation-type Parent `
        --target-id $ParentId `
        --organization $OrganizationUrl `
        --only-show-errors `
        --output none
    if ($LASTEXITCODE -ne 0) { throw "Could not parent work item #$ChildId under #$ParentId." }
    Write-Host "Linked #$ChildId -> parent #$ParentId"
}

function Get-QueryFolder {
    param([Parameter(Mandatory)][string]$Query)
    return Invoke-AdoCli `
        -Area 'wit' `
        -Resource 'queries' `
        -ApiVersion $StableApiVersion `
        -RouteParameters @{ project=$ProjectName; query=$Query } `
        -QueryParameters @{ '$depth'='2' }
}

function Find-ChildByName {
    param([Parameter(Mandatory)][object]$Folder, [Parameter(Mandatory)][string]$Name, [switch]$FolderOnly)
    foreach ($child in @(Get-OptionalProperty -Object $Folder -Name 'children')) {
        if ([string](Get-OptionalProperty -Object $child -Name 'name') -ne $Name) { continue }
        if ($FolderOnly -and (Get-OptionalProperty -Object $child -Name 'isFolder') -ne $true) { continue }
        return $child
    }
    return $null
}

function Ensure-SharedQuery {
    param([Parameter(Mandatory)][string]$FolderId, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Wiql)
    $folder = Get-QueryFolder -Query $FolderId
    $existing = Find-ChildByName -Folder $folder -Name $Name
    if ($null -ne $existing) {
        Write-Host "Reusing shared query: $Name"
        return $existing
    }

    $query = Invoke-AdoCli `
        -Area 'wit' `
        -Resource 'queries' `
        -Method 'POST' `
        -ApiVersion $StableApiVersion `
        -RouteParameters @{ project=$ProjectName; query=$FolderId } `
        -Body ([ordered]@{ name=$Name; wiql=$Wiql })
    Write-Host "Created shared query: $Name"
    return $query
}

function Get-TeamDashboards {
    return Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'dashboards' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters @{ project=$ProjectName; team=$ProjectTeamId }
}

function Get-TeamDashboardWidgets {
    param([Parameter(Mandatory)][string]$DashboardId)
    return Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'widgets' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters @{ project=$ProjectName; team=$ProjectTeamId; dashboardId=$DashboardId }
}

function Ensure-DashboardWidget {
    param(
        [Parameter(Mandatory)][string]$DashboardId,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ContributionId,
        [Parameter(Mandatory)][hashtable]$Position,
        [Parameter(Mandatory)][hashtable]$Size,
        [string]$Settings = $null,
        [object]$SettingsVersion = $null
    )

    $widgets = Get-TeamDashboardWidgets -DashboardId $DashboardId
    foreach ($widget in @(Get-Items -Object $widgets)) {
        if ([string](Get-OptionalProperty -Object $widget -Name 'name') -eq $Name) {
            Write-Host "Reusing dashboard widget: $Name"
            return $widget
        }
    }

    $body = [ordered]@{
        name=$Name
        position=$Position
        size=$Size
        settings=$Settings
        contributionId=$ContributionId
    }
    if ($null -ne $SettingsVersion) { $body.settingsVersion = $SettingsVersion }

    $widget = Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'widgets' `
        -Method 'POST' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters @{ project=$ProjectName; team=$ProjectTeamId; dashboardId=$DashboardId } `
        -Body $body
    Write-Host "Created dashboard widget: $Name"
    return $widget
}

Step -Text 'Authenticate and preflight Azure DevOps automation'

if ([string]::IsNullOrWhiteSpace($env:AZURE_DEVOPS_EXT_PAT) -and -not [string]::IsNullOrWhiteSpace($env:SYSTEM_ACCESSTOKEN)) {
    $env:AZURE_DEVOPS_EXT_PAT = $env:SYSTEM_ACCESSTOKEN
}

& az extension add --name azure-devops --upgrade --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not install/update the azure-devops CLI extension.' }
& az devops configure --defaults organization=$OrganizationUrl project=$ProjectName
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Azure DevOps CLI defaults.' }

$projectRaw = & az devops project show --project $ProjectName --organization $OrganizationUrl --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Azure DevOps project authentication failed.' }
$project = Convert-JsonText -Value $projectRaw
$projectId = [string](Get-OptionalProperty -Object $project -Name 'id')
if ([string]::IsNullOrWhiteSpace($projectId)) { throw 'EOS project has no id.' }
Write-Host "Authenticated project: $ProjectName ($projectId)"

$teamsRaw = & az devops team list --project $ProjectName --organization $OrganizationUrl --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list Azure DevOps teams.' }
$projectTeam = @(Convert-JsonText -Value $teamsRaw) | Where-Object { [string](Get-OptionalProperty -Object $_ -Name 'name') -eq "$ProjectName Team" } | Select-Object -First 1
if ($null -eq $projectTeam) { $projectTeam = @(Convert-JsonText -Value $teamsRaw) | Select-Object -First 1 }
if ($null -eq $projectTeam) { throw 'No Azure DevOps team exists for EOS.' }
$ProjectTeamId = [string](Get-OptionalProperty -Object $projectTeam -Name 'id')
$ProjectTeamName = [string](Get-OptionalProperty -Object $projectTeam -Name 'name')
if ([string]::IsNullOrWhiteSpace($ProjectTeamId)) { throw 'EOS Team has no id.' }
Write-Host "Dashboard team: $ProjectTeamName ($ProjectTeamId)"

& az boards work-item relation list-type --organization $OrganizationUrl --only-show-errors --output none
if ($LASTEXITCODE -ne 0) { throw 'Boards relation CLI preflight failed.' }
$sharedRoot = Get-QueryFolder -Query 'Shared Queries'
if ([string]::IsNullOrWhiteSpace([string](Get-OptionalProperty -Object $sharedRoot -Name 'id'))) { throw 'Shared Queries preflight failed.' }
$teamDashboards = Get-TeamDashboards
if ($null -eq $teamDashboards) { throw 'EOS Team dashboard preflight failed.' }
Write-Host 'Preflight passed.'

Step -Text 'Normalize pipeline name'
$pipelinesRaw = & az pipelines list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list pipelines.' }
$pipelines = @(Convert-JsonText -Value $pipelinesRaw)
$ciPipeline = $pipelines | Where-Object { [string](Get-OptionalProperty -Object $_ -Name 'name') -eq $CiPipelineName } | Select-Object -First 1
if ($null -eq $ciPipeline) {
    $ciPipeline = $pipelines | Where-Object { [string](Get-OptionalProperty -Object $_ -Name 'name') -eq 'bhadkamkar9snehil.EOS' } | Select-Object -First 1
}
if ($null -ne $ciPipeline -and [string](Get-OptionalProperty -Object $ciPipeline -Name 'name') -ne $CiPipelineName) {
    & az pipelines update `
        --id ([int](Get-OptionalProperty -Object $ciPipeline -Name 'id')) `
        --new-name $CiPipelineName `
        --description 'Authoritative EOS Windows build, test, and visual-validation pipeline sourced from GitHub.' `
        --organization $OrganizationUrl `
        --project $ProjectName `
        --only-show-errors `
        --output none
    if ($LASTEXITCODE -ne 0) { throw 'Could not rename the main EOS pipeline.' }
    Write-Host "Renamed main pipeline to '$CiPipelineName'."
}
else {
    Write-Host "Main pipeline already named '$CiPipelineName'."
}
$controlPipeline = $pipelines | Where-Object { [string](Get-OptionalProperty -Object $_ -Name 'name') -eq $ControlPipelineName } | Select-Object -First 1

Step -Text 'Seed Azure Boards roadmap'
$roadmap = @(
    [ordered]@{ Key='uiEpic'; Type='Epic'; Title='UI & Design System'; State='Doing'; Tags='eos;ui;tailwind'; Description='Own the EOS material system, information density, chart legibility, accessibility, light/dark themes, and screenshot-driven UI quality.'; Parent=$null },
    [ordered]@{ Key='uiIssue'; Type='Issue'; Title='Real desktop visual validation and Tailwind polish'; State='Doing'; Tags='eos;ui;tailwind;visual-validation'; Description="Finish the real WPF + WebView2 visual-validation loop and use rendered evidence to continue Tailwind polish. GitHub PR: $GitHubPr12Url"; Parent='uiEpic' },
    [ordered]@{ Key='uiTask1'; Type='Task'; Title='Reconcile PR #12 with current main'; State='Doing'; Tags='eos;ui;ci;visual-validation'; Description='Port PR #12 visual-capture behavior onto current main without regressing unified logging or CI observability.'; Parent='uiIssue' },
    [ordered]@{ Key='uiTask2'; Type='Task'; Title='Capture real WPF + WebView2 screenshots in Azure CI'; State='To Do'; Tags='eos;ui;azure;visual-validation'; Description='Produce deterministic screenshots from the actual desktop host in light/dark modes and retain diagnostics beside the images.'; Parent='uiIssue' },
    [ordered]@{ Key='uiTask3'; Type='Task'; Title='Inspect rendered evidence and fix Tailwind defects'; State='To Do'; Tags='eos;ui;tailwind;visual-validation'; Description='Inspect screenshots for hierarchy, contrast, typography, density, clipping, charts, and theme behavior, then iterate on canonical Tailwind/Razor/chart sources.'; Parent='uiIssue' },
    [ordered]@{ Key='uiTask4'; Type='Task'; Title='Harden visual evidence publishing and automated review'; State='To Do'; Tags='eos;ci;visual-validation'; Description='Make screenshot/log evidence reliably available as Azure Pipeline Artifacts and to automated review tooling.'; Parent='uiIssue' },
    [ordered]@{ Key='platformEpic'; Type='Epic'; Title='Engineering Platform'; State='Done'; Tags='eos;devops;azure;platform'; Description='Windows CI, Azure VM control, GitHub integration, and autonomous EOS engineering automation.'; Parent=$null },
    [ordered]@{ Key='platformIssue'; Type='Issue'; Title='Azure DevOps Windows CI and VM control'; State='Done'; Tags='eos;devops;azure;ci'; Description='Operate EOS through GitHub + Azure Pipelines on the self-hosted Windows VM, plus a secretless agentless VM control plane.'; Parent='platformEpic' },
    [ordered]@{ Key='platformTask1'; Type='Task'; Title='Run EOS CI on the self-hosted Windows agent'; State='Done'; Tags='eos;devops;azure;ci'; Description='Authoritative EOS build/test CI runs on Windows agent EOS in the Default pool.'; Parent='platformIssue' },
    [ordered]@{ Key='platformTask2'; Type='Task'; Title='Create secretless agentless EOS VM control plane'; State='Done'; Tags='eos;devops;azure;wif'; Description='Managed identity + WIF + resource-group-scoped Virtual Machine Contributor + EOS VM Control server pipeline.'; Parent='platformIssue' },
    [ordered]@{ Key='platformTask3'; Type='Task'; Title='Expose compiler diagnostics through GitHub checks'; State='Done'; Tags='eos;devops;ci;observability'; Description='Build failures surface actionable compiler/MSBuild diagnostics rather than only a generic exit code.'; Parent='platformIssue' },
    [ordered]@{ Key='reliabilityEpic'; Type='Epic'; Title='Reliability & Diagnostics'; State='Done'; Tags='eos;reliability;logging;diagnostics'; Description='Application observability, diagnostics, and failure handling.'; Parent=$null },
    [ordered]@{ Key='loggingIssue'; Type='Issue'; Title='Unified logging and diagnostics architecture'; State='Done'; Tags='eos;logging;diagnostics'; Description='One application-facing ILogger<T> API, Serilog only at the host composition root, one rolling log family, and one diagnostics service boundary.'; Parent='reliabilityEpic' },
    [ordered]@{ Key='loggingTask1'; Type='Task'; Title='Remove the InteractionLog split'; State='Done'; Tags='eos;logging'; Description='Retire the direct interaction.log writer and route interaction events through structured ILogger<T>.'; Parent='loggingIssue' },
    [ordered]@{ Key='loggingTask2'; Type='Task'; Title='Standardize ILogger<T> with Serilog at the composition root'; State='Done'; Tags='eos;logging;architecture'; Description='Application code uses Microsoft.Extensions.Logging; Serilog is a DesktopHost provider/sink detail.'; Parent='loggingIssue' },
    [ordered]@{ Key='loggingTask3'; Type='Task'; Title='Centralize diagnostics paths and support bundle creation'; State='Done'; Tags='eos;diagnostics;architecture'; Description='LocalApplicationPaths and IApplicationDiagnostics own log discovery, tailing, and support-bundle generation.'; Parent='loggingIssue' }
)

$workItems = @{}
foreach ($spec in $roadmap) {
    $workItems[$spec.Key] = Ensure-WorkItem -Type $spec.Type -Title $spec.Title -State $spec.State -Tags $spec.Tags -Description $spec.Description
}
foreach ($spec in $roadmap) {
    if ($null -eq $spec.Parent) { continue }
    Ensure-Parent `
        -ChildId ([int](Get-OptionalProperty -Object $workItems[$spec.Key] -Name 'id')) `
        -ParentId ([int](Get-OptionalProperty -Object $workItems[$spec.Parent] -Name 'id'))
}

Step -Text 'Create useful shared queries'
$eosFolder = Find-ChildByName -Folder $sharedRoot -Name 'EOS' -FolderOnly
if ($null -eq $eosFolder) {
    $eosFolder = Invoke-AdoCli `
        -Area 'wit' `
        -Resource 'queries' `
        -Method 'POST' `
        -ApiVersion $StableApiVersion `
        -RouteParameters @{ project=$ProjectName; query=[string](Get-OptionalProperty -Object $sharedRoot -Name 'id') } `
        -Body ([ordered]@{ name='EOS'; isFolder=$true })
    Write-Host 'Created Shared Queries/EOS folder.'
}
else {
    Write-Host 'Reusing Shared Queries/EOS folder.'
}
$eosFolderId = [string](Get-OptionalProperty -Object $eosFolder -Name 'id')
if ([string]::IsNullOrWhiteSpace($eosFolderId)) { throw 'Shared Queries/EOS has no id.' }

$currentFocus = Ensure-SharedQuery -FolderId $eosFolderId -Name 'Current Focus' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.State] <> 'Done' Order By [System.ChangedDate] Desc"
$uiQuery = Ensure-SharedQuery -FolderId $eosFolderId -Name 'UI & Visual Validation' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.Tags] Contains 'ui' Order By [System.State], [System.ChangedDate] Desc"
$platformQuery = Ensure-SharedQuery -FolderId $eosFolderId -Name 'Platform & DevOps' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND ([System.Tags] Contains 'devops' OR [System.Tags] Contains 'azure') Order By [System.State], [System.ChangedDate] Desc"
$recentDone = Ensure-SharedQuery -FolderId $eosFolderId -Name 'Recently Completed' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.ChangedDate] From WorkItems Where [System.TeamProject] = @Project AND [System.State] = 'Done' AND [System.ChangedDate] >= @Today - 30 Order By [System.ChangedDate] Desc"
$blocked = Ensure-SharedQuery -FolderId $eosFolderId -Name 'Blocked' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.Tags] Contains 'blocked' AND [System.State] <> 'Done' Order By [System.ChangedDate] Desc"

Step -Text 'Build the EOS Engineering dashboard'
$teamDashboards = Get-TeamDashboards
$dashboard = Get-Items -Object $teamDashboards | Where-Object { [string](Get-OptionalProperty -Object $_ -Name 'name') -eq $DashboardName } | Select-Object -First 1
if ($null -eq $dashboard) {
    $dashboard = Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'dashboards' `
        -Method 'POST' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters @{ project=$ProjectName; team=$ProjectTeamId } `
        -Body ([ordered]@{ name=$DashboardName; description=$DashboardDescription; position=1 })
    Write-Host "Created team dashboard: $DashboardName"
}
else {
    Write-Host "Reusing team dashboard: $DashboardName"
}
$dashboardId = [string](Get-OptionalProperty -Object $dashboard -Name 'id')
if ([string]::IsNullOrWhiteSpace($dashboardId)) { throw 'EOS Engineering dashboard has no id.' }

function QueryUrl([object]$Query) {
    return "$OrganizationUrl/$ProjectName/_queries/query/$([string](Get-OptionalProperty -Object $Query -Name 'id'))/"
}
$ciPipelineId = if ($null -ne $ciPipeline) { [string](Get-OptionalProperty -Object $ciPipeline -Name 'id') } else { '' }
$controlPipelineId = if ($null -ne $controlPipeline) { [string](Get-OptionalProperty -Object $controlPipeline -Name 'id') } else { '' }
$ciPipelineUrl = if ($ciPipelineId) { "$OrganizationUrl/$ProjectName/_build?definitionId=$ciPipelineId" } else { "$OrganizationUrl/$ProjectName/_build" }
$controlPipelineUrl = if ($controlPipelineId) { "$OrganizationUrl/$ProjectName/_build?definitionId=$controlPipelineId" } else { "$OrganizationUrl/$ProjectName/_build" }

$markdown = @"
## EOS engineering control center

**Code & review**
- [GitHub repository]($GitHubRepositoryUrl)
- [Visual-validation PR #12]($GitHubPr12Url)

**Build & operations**
- [EOS CI]($ciPipelineUrl)
- [EOS VM Control]($controlPipelineUrl)
- [Azure VM]($AzureVmUrl)

**Boards**
- [Current Focus]($(QueryUrl $currentFocus))
- [UI & Visual Validation]($(QueryUrl $uiQuery))
- [Platform & DevOps]($(QueryUrl $platformQuery))
- [Recently Completed]($(QueryUrl $recentDone))
- [Blocked]($(QueryUrl $blocked))

Use `AB#<id>` in GitHub commits and PR descriptions for traceability.
"@

Ensure-DashboardWidget `
    -DashboardId $dashboardId `
    -Name 'EOS Control Center' `
    -ContributionId 'ms.vss-dashboards-web.Microsoft.VisualStudioOnline.Dashboards.MarkdownWidget' `
    -Position @{row=1;column=1} `
    -Size @{rowSpan=3;columnSpan=4} `
    -Settings $markdown `
    -SettingsVersion @{major=1;minor=0;patch=0} | Out-Null

Ensure-DashboardWidget `
    -DashboardId $dashboardId `
    -Name 'New EOS Work Item' `
    -ContributionId 'ms.vss-dashboards-web.Microsoft.VisualStudioOnline.Dashboards.NewWorkItemWidget' `
    -Position @{row=1;column=5} `
    -Size @{rowSpan=1;columnSpan=2} `
    -SettingsVersion @{major=1;minor=0;patch=0} | Out-Null

$sourceBuildWidget = $null
foreach ($candidateDashboard in @(Get-Items -Object $teamDashboards)) {
    $candidateId = [string](Get-OptionalProperty -Object $candidateDashboard -Name 'id')
    if (-not $candidateId -or $candidateId -eq $dashboardId) { continue }
    foreach ($candidateWidget in @(Get-Items -Object (Get-TeamDashboardWidgets -DashboardId $candidateId))) {
        if ([string](Get-OptionalProperty -Object $candidateWidget -Name 'contributionId') -match '(?i)build') {
            $sourceBuildWidget = $candidateWidget
            break
        }
    }
    if ($null -ne $sourceBuildWidget) { break }
}

if ($null -ne $sourceBuildWidget) {
    $sourceSize = Get-OptionalProperty -Object $sourceBuildWidget -Name 'size'
    $rowSpan = [int](Get-OptionalProperty -Object $sourceSize -Name 'rowSpan')
    $columnSpan = [int](Get-OptionalProperty -Object $sourceSize -Name 'columnSpan')
    if ($rowSpan -le 0) { $rowSpan = 2 }
    if ($columnSpan -le 0) { $columnSpan = 4 }

    Ensure-DashboardWidget `
        -DashboardId $dashboardId `
        -Name 'EOS CI — Build History' `
        -ContributionId ([string](Get-OptionalProperty -Object $sourceBuildWidget -Name 'contributionId')) `
        -Position @{row=1;column=7} `
        -Size @{rowSpan=$rowSpan;columnSpan=$columnSpan} `
        -Settings ([string](Get-OptionalProperty -Object $sourceBuildWidget -Name 'settings')) `
        -SettingsVersion (Get-OptionalProperty -Object $sourceBuildWidget -Name 'settingsVersion') | Out-Null
}
else {
    Write-Warning 'No existing Build History widget was available to clone.'
}

Step -Text 'Project setup complete'
Write-Host "Dashboard:      $DashboardName"
Write-Host "Dashboard team: $ProjectTeamName"
Write-Host "Main pipeline:  $CiPipelineName"
Write-Host 'Boards:         EOS roadmap exists and hierarchy is linked'
Write-Host 'Shared queries: Current Focus, UI & Visual Validation, Platform & DevOps, Recently Completed, Blocked'
Write-Host 'Repos:          intentionally unused; GitHub remains canonical'
Write-Host 'Traceability:   use AB#<id> in GitHub commits and PR descriptions' -ForegroundColor Green
