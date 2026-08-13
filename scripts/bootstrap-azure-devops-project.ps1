$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# EOS Azure DevOps project bootstrap.
#
# Purpose:
# - keep GitHub as the canonical code/PR system;
# - use Azure Boards for work planning/traceability;
# - make Azure Pipelines/test results the engineering execution signal;
# - create a useful engineering dashboard instead of Azure-Repos-centric widgets;
# - remain safe to rerun.

$OrganizationUrl = 'https://dev.azure.com/apexasnehil'
$ProjectName = 'EOS'
$AzureDevOpsResourceId = '499b84ac-1321-427f-aa17-267ca6975798'
$DashboardName = 'EOS Engineering'
$CiPipelineName = 'EOS CI'
$ControlPipelineName = 'EOS VM Control'
$GitHubRepositoryUrl = 'https://github.com/bhadkamkar9snehil/EOS'
$GitHubPr12Url = 'https://github.com/bhadkamkar9snehil/EOS/pull/12'
$AzureVmUrl = 'https://portal.azure.com/#view/HubsExtension/BrowseResource/resourceType/Microsoft.Compute%2FvirtualMachines'

function Step([string]$Text) {
    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
}

function Convert-JsonText($Value) {
    if ($null -eq $Value) { return $null }
    return (($Value -join "`n") | ConvertFrom-Json)
}

function Invoke-AdoRest {
    param(
        [Parameter(Mandatory)][ValidateSet('GET','POST','PUT','PATCH','DELETE')][string]$Method,
        [Parameter(Mandatory)][string]$Uri,
        [object]$Body = $null
    )

    $parameters = @{
        Uri = $Uri
        Method = $Method
        Headers = $script:AdoHeaders
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ($Body | ConvertTo-Json -Depth 40)
    }
    return Invoke-RestMethod @parameters
}

function Get-ExactWorkItem {
    param(
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][string]$Title
    )

    $safeTitle = $Title.Replace("'", "''")
    $wiql = "Select [System.Id], [System.Title], [System.WorkItemType], [System.State] From WorkItems Where [System.TeamProject] = '$ProjectName' AND [System.WorkItemType] = '$Type' AND [System.Title] = '$safeTitle'"
    $raw = & az boards query --wiql $wiql --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not query Azure Boards for '$Title'." }
    $items = @(Convert-JsonText $raw)
    return $items | Select-Object -First 1
}

function Ensure-WorkItem {
    param(
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$State,
        [Parameter(Mandatory)][string]$Tags,
        [Parameter(Mandatory)][string]$Description
    )

    $existing = Get-ExactWorkItem -Type $Type -Title $Title
    if ($null -eq $existing) {
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
        $item = Convert-JsonText $raw
        Write-Host "Created $Type #$($item.id): $Title"
    } else {
        $item = $existing
        Write-Host "Reusing $Type #$($item.id): $Title"
    }

    $updateRaw = & az boards work-item update `
        --id ([int]$item.id) `
        --state $State `
        --description $Description `
        --fields "System.Tags=$Tags" `
        --organization $OrganizationUrl `
        --only-show-errors `
        --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not update work item '$Title'." }
    return (Convert-JsonText $updateRaw)
}

function Ensure-Parent {
    param(
        [Parameter(Mandatory)][int]$ChildId,
        [Parameter(Mandatory)][int]$ParentId
    )

    $itemUri = "$OrganizationUrl/$ProjectName/_apis/wit/workitems/${ChildId}?`$expand=relations&api-version=7.1"
    $item = Invoke-AdoRest -Method GET -Uri $itemUri
    $targetSuffix = "/$ParentId"
    $alreadyLinked = @($item.relations | Where-Object {
        $_.rel -eq 'System.LinkTypes.Hierarchy-Reverse' -and ([string]$_.url).EndsWith($targetSuffix)
    }).Count -gt 0

    if (-not $alreadyLinked) {
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
}

function Ensure-SharedQuery {
    param(
        [Parameter(Mandatory)][string]$FolderId,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Wiql
    )

    $folder = Invoke-AdoRest -Method GET -Uri "$OrganizationUrl/$ProjectName/_apis/wit/queries/${FolderId}?`$depth=2&api-version=7.1"
    $existing = @($folder.children | Where-Object { $_.name -eq $Name }) | Select-Object -First 1
    if ($null -ne $existing) {
        Write-Host "Reusing shared query: $Name"
        return $existing
    }

    $created = Invoke-AdoRest -Method POST -Uri "$OrganizationUrl/$ProjectName/_apis/wit/queries/${FolderId}?api-version=7.1" -Body ([ordered]@{
        name = $Name
        wiql = $Wiql
    })
    Write-Host "Created shared query: $Name"
    return $created
}

function Ensure-DashboardWidget {
    param(
        [Parameter(Mandatory)][string]$DashboardId,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ContributionId,
        [Parameter(Mandatory)][hashtable]$Position,
        [Parameter(Mandatory)][hashtable]$Size,
        [string]$Settings = $null,
        [string]$ConfigurationContributionId = $null,
        [object]$SettingsVersion = $null
    )

    $widgetsUri = "$OrganizationUrl/$ProjectName/_apis/dashboard/dashboards/$DashboardId/widgets?api-version=7.1-preview.2"
    $widgets = Invoke-AdoRest -Method GET -Uri $widgetsUri
    $existing = @($widgets.value | Where-Object { $_.name -eq $Name }) | Select-Object -First 1
    if ($null -ne $existing) {
        Write-Host "Reusing dashboard widget: $Name"
        return $existing
    }

    $body = [ordered]@{
        name = $Name
        position = $Position
        size = $Size
        settings = $Settings
        contributionId = $ContributionId
    }
    if ($null -ne $ConfigurationContributionId) { $body.configurationContributionId = $ConfigurationContributionId }
    if ($null -ne $SettingsVersion) { $body.settingsVersion = $SettingsVersion }

    $created = Invoke-AdoRest -Method POST -Uri $widgetsUri -Body $body
    Write-Host "Created dashboard widget: $Name"
    return $created
}

Step 'Authenticate Azure DevOps CLI and REST'
& az extension add --name azure-devops --upgrade --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not install/update azure-devops CLI extension.' }
& az devops configure --defaults organization=$OrganizationUrl project=$ProjectName
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Azure DevOps CLI defaults.' }

$projectRaw = & az devops project show --project $ProjectName --organization $OrganizationUrl --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Azure DevOps project authentication failed.' }
$project = Convert-JsonText $projectRaw
$ProjectId = [string]$project.id

$AdoToken = (& az account get-access-token --resource $AzureDevOpsResourceId --query accessToken --output tsv --only-show-errors).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($AdoToken)) { throw 'Could not acquire Azure DevOps Entra access token.' }
$script:AdoHeaders = @{
    Authorization = "Bearer $AdoToken"
    Accept = 'application/json'
}
Write-Host "Authenticated project: $ProjectName ($ProjectId)"

Step 'Normalize pipeline names'
$pipelinesRaw = & az pipelines list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list pipelines.' }
$pipelines = @(Convert-JsonText $pipelinesRaw)
$ciPipeline = $pipelines | Where-Object { $_.name -eq $CiPipelineName } | Select-Object -First 1
if ($null -eq $ciPipeline) {
    $ciPipeline = $pipelines | Where-Object { $_.name -eq 'bhadkamkar9snehil.EOS' } | Select-Object -First 1
    if ($null -eq $ciPipeline) {
        $ciPipeline = $pipelines | Where-Object { $_.name -ne $ControlPipelineName } | Select-Object -First 1
    }
    if ($null -ne $ciPipeline) {
        & az pipelines update `
            --id ([int]$ciPipeline.id) `
            --new-name $CiPipelineName `
            --description 'Authoritative EOS Windows build, test and visual-validation pipeline sourced from GitHub.' `
            --organization $OrganizationUrl `
            --project $ProjectName `
            --only-show-errors `
            --output none
        if ($LASTEXITCODE -ne 0) { throw 'Could not rename the main EOS pipeline.' }
        Write-Host "Renamed main pipeline to '$CiPipelineName'."
        $ciPipeline.name = $CiPipelineName
    }
} else {
    Write-Host "Main pipeline already named '$CiPipelineName'."
}
$controlPipeline = $pipelines | Where-Object { $_.name -eq $ControlPipelineName } | Select-Object -First 1

Step 'Seed Azure Boards with the real EOS engineering roadmap'
$uiEpic = Ensure-WorkItem -Type 'Epic' -Title 'UI & Design System' -State 'Doing' -Tags 'eos;ui;tailwind' -Description 'Own the EOS material system, information density, chart legibility, accessibility, light/dark themes, and screenshot-driven UI quality.'
$uiIssue = Ensure-WorkItem -Type 'Issue' -Title 'Real desktop visual validation and Tailwind polish' -State 'Doing' -Tags 'eos;ui;tailwind;visual-validation' -Description 'Finish the real WPF + WebView2 visual-validation loop and use rendered evidence to continue Tailwind polish. GitHub PR: https://github.com/bhadkamkar9snehil/EOS/pull/12'
$uiTask1 = Ensure-WorkItem -Type 'Task' -Title 'Reconcile PR #12 with current main' -State 'Doing' -Tags 'eos;ui;ci;visual-validation' -Description 'Port PR #12 visual-capture behavior onto current main without regressing the unified logging architecture or current CI observability.'
$uiTask2 = Ensure-WorkItem -Type 'Task' -Title 'Capture real WPF + WebView2 screenshots in Azure CI' -State 'To Do' -Tags 'eos;ui;azure;visual-validation' -Description 'Produce deterministic screenshots from the actual desktop host at the required light/dark resolutions and retain diagnostics beside the images.'
$uiTask3 = Ensure-WorkItem -Type 'Task' -Title 'Inspect rendered evidence and fix Tailwind defects' -State 'To Do' -Tags 'eos;ui;tailwind;visual-validation' -Description 'Inspect screenshots for hierarchy, contrast, typography, density, clipping, charts and theme behavior; fix only in the Tailwind/Razor/chart sources and rerun evidence.'
$uiTask4 = Ensure-WorkItem -Type 'Task' -Title 'Harden visual evidence publishing and automated review' -State 'To Do' -Tags 'eos;ci;visual-validation' -Description 'Make screenshot/log evidence reliably available as Azure Pipeline Artifacts and to automated review tooling without requiring manual downloads.'
Ensure-Parent -ChildId ([int]$uiIssue.id) -ParentId ([int]$uiEpic.id)
foreach ($task in @($uiTask1,$uiTask2,$uiTask3,$uiTask4)) { Ensure-Parent -ChildId ([int]$task.id) -ParentId ([int]$uiIssue.id) }

$platformEpic = Ensure-WorkItem -Type 'Epic' -Title 'Engineering Platform' -State 'Done' -Tags 'eos;devops;azure;platform' -Description 'Windows CI, Azure VM control, GitHub integration and engineering automation for autonomous EOS development.'
$platformIssue = Ensure-WorkItem -Type 'Issue' -Title 'Azure DevOps Windows CI and VM control' -State 'Done' -Tags 'eos;devops;azure;ci' -Description 'Operate EOS through GitHub + Azure Pipelines on the self-hosted Windows VM, with a separate secretless agentless VM control plane.'
$platformTask1 = Ensure-WorkItem -Type 'Task' -Title 'Run EOS CI on the self-hosted Windows agent' -State 'Done' -Tags 'eos;devops;azure;ci' -Description 'Authoritative EOS build/test CI runs on Windows agent EOS in the Default pool.'
$platformTask2 = Ensure-WorkItem -Type 'Task' -Title 'Create secretless agentless EOS VM control plane' -State 'Done' -Tags 'eos;devops;azure;wif' -Description 'Managed identity + Workload Identity Federation + resource-group-scoped Virtual Machine Contributor + EOS VM Control server pipeline.'
$platformTask3 = Ensure-WorkItem -Type 'Task' -Title 'Expose compiler diagnostics through GitHub checks' -State 'Done' -Tags 'eos;devops;ci;observability' -Description 'Build failures emit actionable compiler/MSBuild diagnostics instead of only a generic PowerShell exit code.'
Ensure-Parent -ChildId ([int]$platformIssue.id) -ParentId ([int]$platformEpic.id)
foreach ($task in @($platformTask1,$platformTask2,$platformTask3)) { Ensure-Parent -ChildId ([int]$task.id) -ParentId ([int]$platformIssue.id) }

$reliabilityEpic = Ensure-WorkItem -Type 'Epic' -Title 'Reliability & Diagnostics' -State 'Done' -Tags 'eos;reliability;logging;diagnostics' -Description 'Application observability, support diagnostics and failure handling.'
$loggingIssue = Ensure-WorkItem -Type 'Issue' -Title 'Unified logging and diagnostics architecture' -State 'Done' -Tags 'eos;logging;diagnostics' -Description 'One application-facing ILogger<T> API, Serilog only at the host composition root, one rolling log family, and one diagnostics service boundary.'
$loggingTask1 = Ensure-WorkItem -Type 'Task' -Title 'Remove the InteractionLog split' -State 'Done' -Tags 'eos;logging' -Description 'Retire the direct interaction.log writer and route navigation/interaction events through structured ILogger<T>.'
$loggingTask2 = Ensure-WorkItem -Type 'Task' -Title 'Standardize ILogger<T> with Serilog at the composition root' -State 'Done' -Tags 'eos;logging;architecture' -Description 'Application code uses Microsoft.Extensions.Logging; Serilog is a sink/provider implementation detail of DesktopHost.'
$loggingTask3 = Ensure-WorkItem -Type 'Task' -Title 'Centralize diagnostics paths and support bundle creation' -State 'Done' -Tags 'eos;diagnostics;architecture' -Description 'LocalApplicationPaths and IApplicationDiagnostics own log discovery, tailing and diagnostics bundle generation.'
Ensure-Parent -ChildId ([int]$loggingIssue.id) -ParentId ([int]$reliabilityEpic.id)
foreach ($task in @($loggingTask1,$loggingTask2,$loggingTask3)) { Ensure-Parent -ChildId ([int]$task.id) -ParentId ([int]$loggingIssue.id) }

Step 'Create useful shared queries'
$rootQueries = Invoke-AdoRest -Method GET -Uri "$OrganizationUrl/$ProjectName/_apis/wit/queries?`$depth=2&api-version=7.1"
$sharedRoot = @($rootQueries.value | Where-Object { $_.name -eq 'Shared Queries' }) | Select-Object -First 1
if ($null -eq $sharedRoot) { throw 'Shared Queries root was not found.' }
$eosFolder = @($sharedRoot.children | Where-Object { $_.name -eq 'EOS' -and $_.isFolder }) | Select-Object -First 1
if ($null -eq $eosFolder) {
    $eosFolder = Invoke-AdoRest -Method POST -Uri "$OrganizationUrl/$ProjectName/_apis/wit/queries/$($sharedRoot.id)?api-version=7.1" -Body ([ordered]@{ name='EOS'; isFolder=$true })
    Write-Host 'Created Shared Queries/EOS folder.'
} else {
    Write-Host 'Reusing Shared Queries/EOS folder.'
}

$currentFocus = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'Current Focus' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.State] <> 'Done' Order By [System.ChangedDate] Desc"
$uiQuery = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'UI & Visual Validation' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.Tags] Contains 'ui' Order By [System.State], [System.ChangedDate] Desc"
$platformQuery = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'Platform & DevOps' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND ([System.Tags] Contains 'devops' OR [System.Tags] Contains 'azure') Order By [System.State], [System.ChangedDate] Desc"
$recentDone = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'Recently Completed' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.ChangedDate] From WorkItems Where [System.TeamProject] = @Project AND [System.State] = 'Done' AND [System.ChangedDate] >= @Today - 30 Order By [System.ChangedDate] Desc"
$blocked = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'Blocked' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.Tags] Contains 'blocked' AND [System.State] <> 'Done' Order By [System.ChangedDate] Desc"

Step 'Build a useful EOS engineering dashboard'
$dashboardsUri = "$OrganizationUrl/$ProjectName/_apis/dashboard/dashboards?api-version=7.1-preview.3"
$dashboards = Invoke-AdoRest -Method GET -Uri $dashboardsUri
$dashboard = @($dashboards.value | Where-Object { $_.name -eq $DashboardName }) | Select-Object -First 1
if ($null -eq $dashboard) {
    $dashboard = Invoke-AdoRest -Method POST -Uri $dashboardsUri -Body ([ordered]@{ name=$DashboardName; position=1 })
    Write-Host "Created dashboard: $DashboardName"
} else {
    Write-Host "Reusing dashboard: $DashboardName"
}
$DashboardId = [string]$dashboard.id

# Remove Code Tile widgets everywhere: they target Azure Repos, while EOS source is GitHub.
foreach ($candidateDashboard in @($dashboards.value)) {
    $candidateId = [string]$candidateDashboard.id
    $widgetsUri = "$OrganizationUrl/$ProjectName/_apis/dashboard/dashboards/$candidateId/widgets?api-version=7.1-preview.2"
    $widgets = Invoke-AdoRest -Method GET -Uri $widgetsUri
    foreach ($widget in @($widgets.value | Where-Object { $_.name -eq 'Code Tile' -or ([string]$_.contributionId) -match '(?i)CodeTile' })) {
        Invoke-AdoRest -Method DELETE -Uri "$OrganizationUrl/$ProjectName/_apis/dashboard/dashboards/$candidateId/widgets/$($widget.id)?api-version=7.1-preview.2" | Out-Null
        Write-Host "Removed Azure-Repos Code Tile from dashboard '$($candidateDashboard.name)'."
    }
}

$currentFocusUrl = "$OrganizationUrl/$ProjectName/_queries/query/$($currentFocus.id)/"
$uiQueryUrl = "$OrganizationUrl/$ProjectName/_queries/query/$($uiQuery.id)/"
$platformQueryUrl = "$OrganizationUrl/$ProjectName/_queries/query/$($platformQuery.id)/"
$recentDoneUrl = "$OrganizationUrl/$ProjectName/_queries/query/$($recentDone.id)/"
$blockedUrl = "$OrganizationUrl/$ProjectName/_queries/query/$($blocked.id)/"
$ciPipelineUrl = if ($null -ne $ciPipeline) { "$OrganizationUrl/$ProjectName/_build?definitionId=$($ciPipeline.id)" } else { "$OrganizationUrl/$ProjectName/_build" }
$controlPipelineUrl = if ($null -ne $controlPipeline) { "$OrganizationUrl/$ProjectName/_build?definitionId=$($controlPipeline.id)" } else { "$OrganizationUrl/$ProjectName/_build" }

$markdown = @"
## EOS engineering control center

**Code & review**
- [GitHub repository]($GitHubRepositoryUrl)
- [Current visual-validation PR #12]($GitHubPr12Url)

**Build & operations**
- [EOS CI]($ciPipelineUrl)
- [EOS VM Control]($controlPipelineUrl)
- [Azure VM]($AzureVmUrl)

**Boards**
- [Current Focus]($currentFocusUrl)
- [UI & Visual Validation]($uiQueryUrl)
- [Platform & DevOps]($platformQueryUrl)
- [Recently Completed]($recentDoneUrl)
- [Blocked]($blockedUrl)

**Operating model**
GitHub is the source-of-truth for code and pull requests. Azure DevOps owns Boards, Windows CI/test history, engineering dashboards and Azure VM operations. Use `AB#<work-item-id>` in GitHub commits/PR descriptions to link code back to Boards.
"@
Ensure-DashboardWidget -DashboardId $DashboardId -Name 'EOS Control Center' -ContributionId 'ms.vss-dashboards-web.Microsoft.VisualStudioOnline.Dashboards.MarkdownWidget' -Position @{row=1;column=1} -Size @{rowSpan=3;columnSpan=4} -Settings $markdown -ConfigurationContributionId 'ms.vss-dashboards-web.Microsoft.VisualStudioOnline.Dashboards.MarkdownWidget.Configuration' -SettingsVersion @{major=1;minor=0;patch=0} | Out-Null
Ensure-DashboardWidget -DashboardId $DashboardId -Name 'New EOS Work Item' -ContributionId 'ms.vss-dashboards-web.Microsoft.VisualStudioOnline.Dashboards.NewWorkItemWidget' -Position @{row=1;column=5} -Size @{rowSpan=1;columnSpan=2} -ConfigurationContributionId 'ms.vss-dashboards-web.Microsoft.VisualStudioOnline.Dashboards.NewWorkItemWidget.Configuration' -SettingsVersion @{major=1;minor=0;patch=0} | Out-Null

# Reuse the already-working Build History widget configuration if one exists on another dashboard.
$sourceBuildWidget = $null
foreach ($candidateDashboard in @($dashboards.value)) {
    $detail = Invoke-AdoRest -Method GET -Uri "$OrganizationUrl/$ProjectName/_apis/dashboard/dashboards/$($candidateDashboard.id)?api-version=7.1-preview.3"
    $candidateWidgets = @($detail.widgets)
    if ($null -ne $ciPipeline) {
        $sourceBuildWidget = $candidateWidgets | Where-Object {
            (([string]$_.settings) -like "*$($ciPipeline.id)*") -and (([string]$_.contributionId) -match '(?i)build')
        } | Select-Object -First 1
    }
    if ($null -eq $sourceBuildWidget) {
        $sourceBuildWidget = $candidateWidgets | Where-Object { ([string]$_.contributionId) -match '(?i)build' } | Select-Object -First 1
    }
    if ($null -ne $sourceBuildWidget) { break }
}

if ($null -ne $sourceBuildWidget) {
    Ensure-DashboardWidget `
        -DashboardId $DashboardId `
        -Name 'EOS CI — Build History' `
        -ContributionId ([string]$sourceBuildWidget.contributionId) `
        -Position @{row=1;column=7} `
        -Size @{rowSpan=[int]$sourceBuildWidget.size.rowSpan;columnSpan=[int]$sourceBuildWidget.size.columnSpan} `
        -Settings ([string]$sourceBuildWidget.settings) `
        -ConfigurationContributionId ([string]$sourceBuildWidget.configurationContributionId) `
        -SettingsVersion $sourceBuildWidget.settingsVersion | Out-Null
} else {
    Write-Warning 'No existing Build History widget configuration was found to clone. The dashboard links to EOS CI directly.'
}

Step 'Project setup complete'
Write-Host "Dashboard:       $DashboardName"
Write-Host "Main pipeline:   $CiPipelineName"
Write-Host 'Boards:          seeded with current EOS UI, platform and reliability work'
Write-Host 'Shared queries:  Current Focus, UI & Visual Validation, Platform & DevOps, Recently Completed, Blocked'
Write-Host 'Repos:           intentionally left unused; GitHub remains canonical'
Write-Host 'VM control:      existing EOS VM Control pipeline remains separate from normal CI'
Write-Host ''
Write-Host 'Use AB#<id> in GitHub commit messages and PR descriptions to connect code changes to Azure Boards.' -ForegroundColor Green
