$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Idempotent EOS Azure DevOps project bootstrap.
# GitHub remains canonical for source code and pull requests.
# Azure DevOps owns Boards, Windows CI/test history, dashboards, and Azure VM operations.

$OrganizationUrl = 'https://dev.azure.com/apexasnehil'
$ProjectName = 'EOS'
$DashboardName = 'EOS Engineering'
$CiPipelineName = 'EOS CI'
$ControlPipelineName = 'EOS VM Control'
$GitHubPr12Url = 'https://github.com/bhadkamkar9snehil/EOS/pull/12'
$StableApiVersion = '7.1'
$PreviewApiVersion = '7.1-preview'

function Step([string]$Text) {
    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
}

function Convert-JsonText($Value) {
    if ($null -eq $Value) { return $null }
    $text = ($Value -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return ($text | ConvertFrom-Json)
}

function Get-OptionalProperty {
    param(
        [object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Invoke-AdoCli {
    param(
        [Parameter(Mandatory)][string]$Area,
        [Parameter(Mandatory)][string]$Resource,
        [ValidateSet('GET','POST','PUT','PATCH','DELETE','HEAD','OPTIONS')][string]$Method = 'GET',
        [Parameter(Mandatory)][string]$ApiVersion,
        [hashtable]$RouteParameters = @{},
        [hashtable]$QueryParameters = @{},
        [object]$Body = $null
    )

    # azure-devops CLI's az devops invoke parser accepts major.minor and
    # major.minor-preview, but not revision suffixes such as 7.1-preview.3.
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
        $Body | ConvertTo-Json -Depth 40 | Set-Content -Path $bodyPath -Encoding utf8
        $commandArgs += @('--in-file',$bodyPath)
    }

    try {
        $raw = & az @commandArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Azure DevOps CLI invoke failed [$Area/$Resource $Method]: $($raw -join ' ')"
        }
        return (Convert-JsonText $raw)
    }
    finally {
        if ($null -ne $bodyPath) {
            Remove-Item $bodyPath -Force -ErrorAction SilentlyContinue
        }
    }
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
    return @(Convert-JsonText $raw) | Select-Object -First 1
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
    if ($null -eq $item) {
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
    }
    else {
        Write-Host "Reusing $Type #$($item.id): $Title"
    }

    # az boards work-item update does not support --project.
    $raw = & az boards work-item update `
        --id ([int]$item.id) `
        --state $State `
        --description $Description `
        --fields "System.Tags=$Tags" `
        --organization $OrganizationUrl `
        --only-show-errors `
        --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not update work item '$Title'." }
    return (Convert-JsonText $raw)
}

function Ensure-Parent {
    param(
        [Parameter(Mandatory)][int]$ChildId,
        [Parameter(Mandatory)][int]$ParentId
    )

    $raw = & az boards work-item relation show --id $ChildId --organization $OrganizationUrl --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect relations for work item #$ChildId." }
    $item = Convert-JsonText $raw

    $relations = Get-OptionalProperty -Object $item -Name 'relations'
    foreach ($relation in @($relations)) {
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

function Get-QueryFolder([string]$QueryId) {
    return Invoke-AdoCli `
        -Area 'wit' `
        -Resource 'queries' `
        -ApiVersion $StableApiVersion `
        -RouteParameters @{ project=$ProjectName; query=$QueryId } `
        -QueryParameters @{ '$depth'='2' }
}

function Ensure-SharedQuery {
    param(
        [Parameter(Mandatory)][string]$FolderId,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Wiql
    )

    $folder = Get-QueryFolder -QueryId $FolderId
    $children = Get-OptionalProperty -Object $folder -Name 'children'
    $existing = @($children) | Where-Object { $_.name -eq $Name } | Select-Object -First 1
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

function Get-Dashboards {
    return Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'dashboards' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters @{ project=$ProjectName }
}

function Get-DashboardRouteParameters {
    param(
        [Parameter(Mandatory)][object]$Dashboard,
        [string]$WidgetId = $null
    )

    $dashboardId = [string](Get-OptionalProperty -Object $Dashboard -Name 'id')
    if ([string]::IsNullOrWhiteSpace($dashboardId)) {
        throw 'Dashboard object has no id.'
    }

    $route = @{ project=$ProjectName; dashboardId=$dashboardId }
    $scope = [string](Get-OptionalProperty -Object $Dashboard -Name 'dashboardScope')
    if ($scope -eq 'project_Team') {
        $teamId = [string](Get-OptionalProperty -Object $Dashboard -Name 'groupId')
        if ([string]::IsNullOrWhiteSpace($teamId)) {
            throw "Team-scoped dashboard '$dashboardId' has no groupId."
        }
        $route.team = $teamId
    }
    if (-not [string]::IsNullOrWhiteSpace($WidgetId)) {
        $route.widgetId = $WidgetId
    }
    return $route
}

function Get-DashboardWidgets {
    param([Parameter(Mandatory)][object]$Dashboard)

    return Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'widgets' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters (Get-DashboardRouteParameters -Dashboard $Dashboard)
}

function Try-GetDashboardWidgets {
    param([Parameter(Mandatory)][object]$Dashboard)

    try {
        return Get-DashboardWidgets -Dashboard $Dashboard
    }
    catch {
        $id = [string](Get-OptionalProperty -Object $Dashboard -Name 'id')
        $name = [string](Get-OptionalProperty -Object $Dashboard -Name 'name')
        Write-Warning "Skipping dashboard '$name' ($id): $($_.Exception.Message)"
        return $null
    }
}

function Ensure-DashboardWidget {
    param(
        [Parameter(Mandatory)][object]$Dashboard,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ContributionId,
        [Parameter(Mandatory)][hashtable]$Position,
        [Parameter(Mandatory)][hashtable]$Size,
        [string]$Settings = $null,
        [string]$ConfigurationContributionId = $null,
        [object]$SettingsVersion = $null
    )

    $widgets = Get-DashboardWidgets -Dashboard $Dashboard
    $values = Get-OptionalProperty -Object $widgets -Name 'value'
    $existing = @($values) | Where-Object { $_.name -eq $Name } | Select-Object -First 1
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
    if (-not [string]::IsNullOrWhiteSpace($ConfigurationContributionId)) {
        $body.configurationContributionId = $ConfigurationContributionId
    }
    if ($null -ne $SettingsVersion) {
        $body.settingsVersion = $SettingsVersion
    }

    $widget = Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'widgets' `
        -Method 'POST' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters (Get-DashboardRouteParameters -Dashboard $Dashboard) `
        -Body $body
    Write-Host "Created dashboard widget: $Name"
    return $widget
}

Step 'Authenticate and preflight Azure DevOps automation'
& az extension add --name azure-devops --upgrade --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not install/update the azure-devops CLI extension.' }

& az devops configure --defaults organization=$OrganizationUrl project=$ProjectName
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Azure DevOps CLI defaults.' }

$projectRaw = & az devops project show --project $ProjectName --organization $OrganizationUrl --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Azure DevOps project authentication failed.' }
$project = Convert-JsonText $projectRaw
Write-Host "Authenticated project: $ProjectName ($($project.id))"

& az boards work-item relation list-type --organization $OrganizationUrl --only-show-errors --output none
if ($LASTEXITCODE -ne 0) { throw 'Boards relation CLI preflight failed.' }

$preflightQueries = Invoke-AdoCli `
    -Area 'wit' `
    -Resource 'queries' `
    -ApiVersion $StableApiVersion `
    -RouteParameters @{ project=$ProjectName } `
    -QueryParameters @{ '$depth'='1' }
if ($null -eq $preflightQueries) { throw 'Shared-query API preflight returned no data.' }

$preflightDashboards = Get-Dashboards
if ($null -eq $preflightDashboards) { throw 'Dashboard API preflight returned no data.' }
$preflightDashboardValues = @(Get-OptionalProperty -Object $preflightDashboards -Name 'value')
$accessibleDashboardCount = 0
foreach ($candidate in $preflightDashboardValues) {
    $widgets = Try-GetDashboardWidgets -Dashboard $candidate
    if ($null -ne $widgets) { $accessibleDashboardCount++; break }
}
if ($preflightDashboardValues.Count -gt 0 -and $accessibleDashboardCount -eq 0) {
    throw 'Dashboards were listed, but none could be read with their proper project/team scope.'
}
Write-Host 'Preflight passed: Boards, queries, dashboards, and scoped widget reads are callable.'

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
        $ciPipeline.name = $CiPipelineName
        Write-Host "Renamed main pipeline to '$CiPipelineName'."
    }
}
else {
    Write-Host "Main pipeline already named '$CiPipelineName'."
}
$controlPipeline = $pipelines | Where-Object { $_.name -eq $ControlPipelineName } | Select-Object -First 1

Step 'Seed Azure Boards with the EOS engineering roadmap'
$uiEpic = Ensure-WorkItem -Type 'Epic' -Title 'UI & Design System' -State 'Doing' -Tags 'eos;ui;tailwind' -Description 'Own the EOS material system, information density, chart legibility, accessibility, light/dark themes, and screenshot-driven UI quality.'
$uiIssue = Ensure-WorkItem -Type 'Issue' -Title 'Real desktop visual validation and Tailwind polish' -State 'Doing' -Tags 'eos;ui;tailwind;visual-validation' -Description "Finish the real WPF + WebView2 visual-validation loop and use rendered evidence to continue Tailwind polish. GitHub PR: $GitHubPr12Url"
$uiTask1 = Ensure-WorkItem -Type 'Task' -Title 'Reconcile PR #12 with current main' -State 'Doing' -Tags 'eos;ui;ci;visual-validation' -Description 'Port PR #12 visual-capture behavior onto current main without regressing unified logging or CI observability.'
$uiTask2 = Ensure-WorkItem -Type 'Task' -Title 'Capture real WPF + WebView2 screenshots in Azure CI' -State 'To Do' -Tags 'eos;ui;azure;visual-validation' -Description 'Produce deterministic screenshots from the actual desktop host in light/dark modes and retain diagnostics beside the images.'
$uiTask3 = Ensure-WorkItem -Type 'Task' -Title 'Inspect rendered evidence and fix Tailwind defects' -State 'To Do' -Tags 'eos;ui;tailwind;visual-validation' -Description 'Inspect screenshots for hierarchy, contrast, typography, density, clipping, charts and theme behavior, then iterate on the canonical Tailwind/Razor/chart sources.'
$uiTask4 = Ensure-WorkItem -Type 'Task' -Title 'Harden visual evidence publishing and automated review' -State 'To Do' -Tags 'eos;ci;visual-validation' -Description 'Make screenshot/log evidence reliably available as Azure Pipeline Artifacts and to automated review tooling.'
Ensure-Parent -ChildId ([int]$uiIssue.id) -ParentId ([int]$uiEpic.id)
foreach ($task in @($uiTask1,$uiTask2,$uiTask3,$uiTask4)) { Ensure-Parent -ChildId ([int]$task.id) -ParentId ([int]$uiIssue.id) }

$platformEpic = Ensure-WorkItem -Type 'Epic' -Title 'Engineering Platform' -State 'Done' -Tags 'eos;devops;azure;platform' -Description 'Windows CI, Azure VM control, GitHub integration and autonomous EOS engineering automation.'
$platformIssue = Ensure-WorkItem -Type 'Issue' -Title 'Azure DevOps Windows CI and VM control' -State 'Done' -Tags 'eos;devops;azure;ci' -Description 'Operate EOS through GitHub + Azure Pipelines on the self-hosted Windows VM, plus a secretless agentless VM control plane.'
$platformTask1 = Ensure-WorkItem -Type 'Task' -Title 'Run EOS CI on the self-hosted Windows agent' -State 'Done' -Tags 'eos;devops;azure;ci' -Description 'Authoritative EOS build/test CI runs on Windows agent EOS in the Default pool.'
$platformTask2 = Ensure-WorkItem -Type 'Task' -Title 'Create secretless agentless EOS VM control plane' -State 'Done' -Tags 'eos;devops;azure;wif' -Description 'Managed identity + WIF + resource-group-scoped Virtual Machine Contributor + EOS VM Control server pipeline.'
$platformTask3 = Ensure-WorkItem -Type 'Task' -Title 'Expose compiler diagnostics through GitHub checks' -State 'Done' -Tags 'eos;devops;ci;observability' -Description 'Build failures surface actionable compiler/MSBuild diagnostics rather than only a generic exit code.'
Ensure-Parent -ChildId ([int]$platformIssue.id) -ParentId ([int]$platformEpic.id)
foreach ($task in @($platformTask1,$platformTask2,$platformTask3)) { Ensure-Parent -ChildId ([int]$task.id) -ParentId ([int]$platformIssue.id) }

$reliabilityEpic = Ensure-WorkItem -Type 'Epic' -Title 'Reliability & Diagnostics' -State 'Done' -Tags 'eos;reliability;logging;diagnostics' -Description 'Application observability, diagnostics and failure handling.'
$loggingIssue = Ensure-WorkItem -Type 'Issue' -Title 'Unified logging and diagnostics architecture' -State 'Done' -Tags 'eos;logging;diagnostics' -Description 'One application-facing ILogger<T> API, Serilog only at the host composition root, one rolling log family, and one diagnostics service boundary.'
$loggingTask1 = Ensure-WorkItem -Type 'Task' -Title 'Remove the InteractionLog split' -State 'Done' -Tags 'eos;logging' -Description 'Retire the direct interaction.log writer and route interaction events through structured ILogger<T>.'
$loggingTask2 = Ensure-WorkItem -Type 'Task' -Title 'Standardize ILogger<T> with Serilog at the composition root' -State 'Done' -Tags 'eos;logging;architecture' -Description 'Application code uses Microsoft.Extensions.Logging; Serilog is a DesktopHost provider/sink detail.'
$loggingTask3 = Ensure-WorkItem -Type 'Task' -Title 'Centralize diagnostics paths and support bundle creation' -State 'Done' -Tags 'eos;diagnostics;architecture' -Description 'LocalApplicationPaths and IApplicationDiagnostics own log discovery, tailing and support-bundle generation.'
Ensure-Parent -ChildId ([int]$loggingIssue.id) -ParentId ([int]$reliabilityEpic.id)
foreach ($task in @($loggingTask1,$loggingTask2,$loggingTask3)) { Ensure-Parent -ChildId ([int]$task.id) -ParentId ([int]$loggingIssue.id) }

Step 'Create useful shared queries'
$rootQueries = Invoke-AdoCli `
    -Area 'wit' `
    -Resource 'queries' `
    -ApiVersion $StableApiVersion `
    -RouteParameters @{ project=$ProjectName } `
    -QueryParameters @{ '$depth'='2' }
$rootValues = @(Get-OptionalProperty -Object $rootQueries -Name 'value')
$sharedRoot = $rootValues | Where-Object { $_.name -eq 'Shared Queries' } | Select-Object -First 1
if ($null -eq $sharedRoot) { throw 'Shared Queries root was not found.' }

$sharedChildren = @(Get-OptionalProperty -Object $sharedRoot -Name 'children')
$eosFolder = $sharedChildren | Where-Object { $_.name -eq 'EOS' -and $_.isFolder } | Select-Object -First 1
if ($null -eq $eosFolder) {
    $eosFolder = Invoke-AdoCli `
        -Area 'wit' `
        -Resource 'queries' `
        -Method 'POST' `
        -ApiVersion $StableApiVersion `
        -RouteParameters @{ project=$ProjectName; query=[string]$sharedRoot.id } `
        -Body ([ordered]@{ name='EOS'; isFolder=$true })
    Write-Host 'Created Shared Queries/EOS folder.'
}
else {
    Write-Host 'Reusing Shared Queries/EOS folder.'
}

$currentFocus = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'Current Focus' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.State] <> 'Done' Order By [System.ChangedDate] Desc"
$uiQuery = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'UI & Visual Validation' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.Tags] Contains 'ui' Order By [System.State], [System.ChangedDate] Desc"
$platformQuery = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'Platform & DevOps' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND ([System.Tags] Contains 'devops' OR [System.Tags] Contains 'azure') Order By [System.State], [System.ChangedDate] Desc"
$recentDone = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'Recently Completed' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.ChangedDate] From WorkItems Where [System.TeamProject] = @Project AND [System.State] = 'Done' AND [System.ChangedDate] >= @Today - 30 Order By [System.ChangedDate] Desc"
$blocked = Ensure-SharedQuery -FolderId ([string]$eosFolder.id) -Name 'Blocked' -Wiql "Select [System.Id], [System.WorkItemType], [System.Title], [System.State], [System.Tags] From WorkItems Where [System.TeamProject] = @Project AND [System.Tags] Contains 'blocked' AND [System.State] <> 'Done' Order By [System.ChangedDate] Desc"

Step 'Build the EOS Engineering dashboard'
$dashboards = Get-Dashboards
$dashboardValues = @(Get-OptionalProperty -Object $dashboards -Name 'value')
$dashboard = $dashboardValues | Where-Object { $_.name -eq $DashboardName -and ([string](Get-OptionalProperty -Object $_ -Name 'dashboardScope')) -ne 'project_Team' } | Select-Object -First 1
if ($null -eq $dashboard) {
    $dashboard = Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'dashboards' `
        -Method 'POST' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters @{ project=$ProjectName } `
        -Body ([ordered]@{
            name=$DashboardName
            description='EOS engineering cockpit: Azure Boards, EOS CI, test history, and operational controls. GitHub remains the source-of-truth for code and pull requests.'
            position=1
        })
    Write-Host "Created project-scoped dashboard: $DashboardName"
}
else {
    Write-Host "Reusing project-scoped dashboard: $DashboardName"
}

Ensure-DashboardWidget `
    -Dashboard $dashboard `
    -Name 'New EOS Work Item' `
    -ContributionId 'ms.vss-dashboards-web.Microsoft.VisualStudioOnline.Dashboards.NewWorkItemWidget' `
    -Position @{row=1;column=1} `
    -Size @{rowSpan=1;columnSpan=2} `
    -ConfigurationContributionId 'ms.vss-dashboards-web.Microsoft.VisualStudioOnline.Dashboards.NewWorkItemWidget.Configuration' `
    -SettingsVersion @{major=1;minor=0;patch=0} | Out-Null

# Reuse an existing Build History widget's known-good settings. Existing dashboards
# may be team-scoped, so all reads use dashboardScope + groupId routing.
$sourceBuildWidget = $null
foreach ($candidate in $dashboardValues) {
    $candidateId = [string](Get-OptionalProperty -Object $candidate -Name 'id')
    $targetId = [string](Get-OptionalProperty -Object $dashboard -Name 'id')
    if ([string]::IsNullOrWhiteSpace($candidateId) -or $candidateId -eq $targetId) { continue }

    $widgets = Try-GetDashboardWidgets -Dashboard $candidate
    if ($null -eq $widgets) { continue }
    $candidateWidgets = @(Get-OptionalProperty -Object $widgets -Name 'value')

    if ($null -ne $ciPipeline) {
        $sourceBuildWidget = $candidateWidgets | Where-Object {
            (([string](Get-OptionalProperty -Object $_ -Name 'settings')) -like "*$($ciPipeline.id)*") -and
            (([string](Get-OptionalProperty -Object $_ -Name 'contributionId')) -match '(?i)build')
        } | Select-Object -First 1
    }
    if ($null -eq $sourceBuildWidget) {
        $sourceBuildWidget = $candidateWidgets | Where-Object {
            ([string](Get-OptionalProperty -Object $_ -Name 'contributionId')) -match '(?i)build'
        } | Select-Object -First 1
    }
    if ($null -ne $sourceBuildWidget) { break }
}

if ($null -ne $sourceBuildWidget) {
    $configurationContributionId = [string](Get-OptionalProperty -Object $sourceBuildWidget -Name 'configurationContributionId')
    $settingsVersion = Get-OptionalProperty -Object $sourceBuildWidget -Name 'settingsVersion'
    $sourceSize = Get-OptionalProperty -Object $sourceBuildWidget -Name 'size'
    $rowSpan = [int](Get-OptionalProperty -Object $sourceSize -Name 'rowSpan')
    $columnSpan = [int](Get-OptionalProperty -Object $sourceSize -Name 'columnSpan')
    if ($rowSpan -le 0) { $rowSpan = 2 }
    if ($columnSpan -le 0) { $columnSpan = 4 }

    Ensure-DashboardWidget `
        -Dashboard $dashboard `
        -Name 'EOS CI — Build History' `
        -ContributionId ([string](Get-OptionalProperty -Object $sourceBuildWidget -Name 'contributionId')) `
        -Position @{row=1;column=3} `
        -Size @{rowSpan=$rowSpan;columnSpan=$columnSpan} `
        -Settings ([string](Get-OptionalProperty -Object $sourceBuildWidget -Name 'settings')) `
        -ConfigurationContributionId $configurationContributionId `
        -SettingsVersion $settingsVersion | Out-Null
}
else {
    Write-Warning 'No existing Build History widget was available to clone. The dashboard itself is configured; EOS CI remains available under Pipelines.'
}

Step 'Project setup complete'
Write-Host "Dashboard:      $DashboardName"
Write-Host "Main pipeline:  $CiPipelineName"
Write-Host 'Boards:         EOS UI, platform and reliability work seeded and linked'
Write-Host 'Shared queries: Current Focus, UI & Visual Validation, Platform & DevOps, Recently Completed, Blocked'
Write-Host 'Repos:          intentionally unused; GitHub remains canonical'
Write-Host 'VM control:     EOS VM Control remains separate from normal CI'
Write-Host 'Traceability:   use AB#<id> in GitHub commits and PR descriptions' -ForegroundColor Green
