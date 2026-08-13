$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# EOS Azure DevOps project bootstrap.
#
# Design:
# - GitHub remains canonical for source code and pull requests.
# - Azure DevOps owns Boards, Windows CI/test history, dashboards, and VM operations.
# - The script is idempotent and uses only the authenticated Azure DevOps CLI session.
# - All read paths used later are exercised before any mutation.

$OrganizationUrl = 'https://dev.azure.com/apexasnehil'
$ProjectName = 'EOS'
$DashboardName = 'EOS Engineering'
$CiPipelineName = 'EOS CI'
$ControlPipelineName = 'EOS VM Control'
$GitHubRepositoryUrl = 'https://github.com/bhadkamkar9snehil/EOS'
$GitHubPr12Url = 'https://github.com/bhadkamkar9snehil/EOS/pull/12'
$AzureVmUrl = 'https://portal.azure.com/#view/HubsExtension/BrowseResource/resourceType/Microsoft.Compute%2FvirtualMachines'
$StableApiVersion = '7.1'
$PreviewApiVersion = '7.1-preview'

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
    param(
        [object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-Items {
    param(
        [object]$Object,
        [string]$PropertyName = 'value'
    )

    if ($null -eq $Object) { return @() }
    $value = Get-OptionalProperty -Object $Object -Name $PropertyName
    if ($null -eq $value) { return @() }
    return @($value)
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

    # azure-devops CLI converts the API version to a float internally. Revision
    # suffixes such as 7.1-preview.3 therefore fail inside the extension.
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
        return (Convert-JsonText -Value $raw)
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
        $item = Convert-JsonText -Value $raw
        Write-Host "Created $Type #$([int](Get-OptionalProperty -Object $item -Name 'id')): $Title"
    }
    else {
        Write-Host "Reusing $Type #$([int](Get-OptionalProperty -Object $item -Name 'id')): $Title"
    }

    $itemId = [int](Get-OptionalProperty -Object $item -Name 'id')
    if ($itemId -le 0) { throw "Work item '$Title' has no valid id." }

    # az boards work-item update intentionally has no --project option.
    $raw = & az boards work-item update `
        --id $itemId `
        --state $State `
        --description $Description `
        --fields "System.Tags=$Tags" `
        --organization $OrganizationUrl `
        --only-show-errors `
        --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not update work item '$Title'." }
    return (Convert-JsonText -Value $raw)
}

function Ensure-Parent {
    param(
        [Parameter(Mandatory)][int]$ChildId,
        [Parameter(Mandatory)][int]$ParentId
    )

    $raw = & az boards work-item relation show --id $ChildId --organization $OrganizationUrl --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect relations for work item #$ChildId." }
    $item = Convert-JsonText -Value $raw

    foreach ($relation in @(Get-OptionalProperty -Object $item -Name 'relations')) {
        $attributes = Get-OptionalProperty -Object $relation -Name 'attributes'
        $name = [string](Get-OptionalProperty -Object $attributes -Name 'name')
        $url = [string](Get-OptionalProperty -Object $relation -Name 'url')
        if ($name -eq 'Parent' -and -not [string]::IsNullOrWhiteSpace($url) -and $url.EndsWith("/$ParentId")) {
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
    param(
        [Parameter(Mandatory)][object]$Folder,
        [Parameter(Mandatory)][string]$Name,
        [switch]$FolderOnly
    )

    foreach ($child in @(Get-OptionalProperty -Object $Folder -Name 'children')) {
        $childName = [string](Get-OptionalProperty -Object $child -Name 'name')
        if ($childName -ne $Name) { continue }
        if ($FolderOnly) {
            $isFolder = Get-OptionalProperty -Object $child -Name 'isFolder'
            if ($isFolder -ne $true) { continue }
        }
        return $child
    }
    return $null
}

function Ensure-SharedQuery {
    param(
        [Parameter(Mandatory)][string]$FolderId,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Wiql
    )

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
    $existing = $null
    foreach ($widget in @(Get-Items -Object $widgets)) {
        if ([string](Get-OptionalProperty -Object $widget -Name 'name') -eq $Name) {
            $existing = $widget
            break
        }
    }
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
$teams = @(Convert-JsonText -Value $teamsRaw)
$projectTeam = $null
foreach ($team in $teams) {
    if ([string](Get-OptionalProperty -Object $team -Name 'name') -eq "$ProjectName Team") {
        $projectTeam = $team
        break
    }
}
if ($null -eq $projectTeam) { $projectTeam = $teams | Select-Object -First 1 }
if ($null -eq $projectTeam) { throw 'No Azure DevOps team exists for EOS.' }
$ProjectTeamId = [string](Get-OptionalProperty -Object $projectTeam -Name 'id')
$ProjectTeamName = [string](Get-OptionalProperty -Object $projectTeam -Name 'name')
if ([string]::IsNullOrWhiteSpace($ProjectTeamId)) { throw 'EOS Team has no id.' }
Write-Host "Dashboard team: $ProjectTeamName ($ProjectTeamId)"

& az boards work-item relation list-type --organization $OrganizationUrl --only-show-errors --output none
if ($LASTEXITCODE -ne 0) { throw 'Boards relation CLI preflight failed.' }

# Exercise the exact query path that the mutation phase uses. The Queries Get API
# accepts either a query id or a path; this avoids assumptions about list response shape.
$sharedQueriesPreflight = Get-QueryFolder -Query 'Shared Queries'
if ($null -eq $sharedQueriesPreflight) { throw 'Shared Queries preflight returned no data.' }
$sharedQueriesId = [string](Get-OptionalProperty -Object $sharedQueriesPreflight -Name 'id')
if ([string]::IsNullOrWhiteSpace($sharedQueriesId)) { throw 'Shared Queries preflight returned no id.' }

$teamDashboardsPreflight = Get-TeamDashboards
if ($null -eq $teamDashboardsPreflight) { throw 'EOS Team dashboard preflight returned no data.' }
$teamDashboardItems = @(Get-Items -Object $teamDashboardsPreflight)
if ($teamDashboardItems.Count -gt 0) {
    $firstDashboardId = [string](Get-OptionalProperty -Object $teamDashboardItems[0] -Name 'id')
    if (-not [string]::IsNullOrWhiteSpace($firstDashboardId)) {
        Get-TeamDashboardWidgets -DashboardId $firstDashboardId | Out-Null
    }
}
Write-Host 'Preflight passed: Boards, Shared Queries path, EOS Team dashboards, and widget reads are callable.'

Step -Text 'Normalize pipeline names'
$pipelinesRaw = & az pipelines list --organization $OrganizationUrl --project $ProjectName --only-show-errors --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list pipelines.' }
$pipelines = @(Convert-JsonText -Value $pipelinesRaw)
$ciPipeline = $null
$controlPipeline = $null
foreach ($pipeline in $pipelines) {
    $name = [string](Get-OptionalProperty -Object $pipeline -Name 'name')
    if ($name -eq $CiPipelineName) { $ciPipeline = $pipeline }
    if ($name -eq $ControlPipelineName) { $controlPipeline = $pipeline }
}

if ($null -eq $ciPipeline) {
    foreach ($pipeline in $pipelines) {
        $name = [string](Get-OptionalProperty -Object $pipeline -Name 'name')
        if ($name -eq 'bhadkamkar9snehil.EOS') {
            $ciPipeline = $pipeline
            break
        }
    }
}
if ($null -eq $ciPipeline) {
    foreach ($pipeline in $pipelines) {
        if ([string](Get-OptionalProperty -Object $pipeline -Name 'name') -ne $ControlPipelineName) {
            $ciPipeline = $pipeline
            break
        }
    }
}

if ($null -ne $ciPipeline -and [string](Get-OptionalProperty -Object $ciPipeline -Name 'name') -ne $CiPipelineName) {
    $ciPipelineId = [int](Get-OptionalProperty -Object $ciPipeline -Name 'id')
    & az pipelines update `
        --id $ciPipelineId `
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

Step -Text 'Seed Azure Boards with the EOS engineering roadmap'
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
    $workItems[$spec.Key] = Ensure-WorkItem `
        -Type $spec.Type `
        -Title $spec.Title `
        -State $spec.State `
        -Tags $spec.Tags `
        -Description $spec.Description
}
foreach ($spec in $roadmap) {
    if ($null -eq $spec.Parent) { continue }
    $childId = [int](Get-OptionalProperty -Object $workItems[$spec.Key] -Name 'id')
    $parentId = [int](Get-OptionalProperty -Object $workItems[$spec.Parent] -Name 'id')
    if ($childId -le 0 -or $parentId -le 0) { throw "Invalid hierarchy ids for '$($spec.Title)'." }
    Ensure-Parent -ChildId $childId -ParentId $parentId
}

Step -Text 'Create useful shared queries'
$sharedRoot = Get-QueryFolder -Query 'Shared Queries'
$eosFolder = Find-ChildByName -Folder $sharedRoot -Name 'EOS' -FolderOnly
if ($null -eq $eosFolder) {
    $sharedRootId = [string](Get-OptionalProperty -Object $sharedRoot -Name 'id')
    if ([string]::IsNullOrWhiteSpace($sharedRootId)) { throw 'Shared Queries has no id.' }
    $eosFolder = Invoke-AdoCli `
        -Area 'wit' `
        -Resource 'queries' `
        -Method 'POST' `
        -ApiVersion $StableApiVersion `
        -RouteParameters @{ project=$ProjectName; query=$sharedRootId } `
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
$dashboard = $null
foreach ($candidate in @(Get-Items -Object $teamDashboards)) {
    if ([string](Get-OptionalProperty -Object $candidate -Name 'name') -eq $DashboardName) {
        $dashboard = $candidate
        break
    }
}

if ($null -eq $dashboard) {
    $dashboard = Invoke-AdoCli `
        -Area 'dashboard' `
        -Resource 'dashboards' `
        -Method 'POST' `
        -ApiVersion $PreviewApiVersion `
        -RouteParameters @{ project=$ProjectName; team=$ProjectTeamId } `
        -Body ([ordered]@{
            name=$DashboardName
            description='EOS engineering cockpit: Boards, EOS CI/test history, and operational controls. GitHub remains canonical for code and pull requests.'
            position=1
        })
    Write-Host "Created team dashboard: $DashboardName"
}
else {
    Write-Host "Reusing team dashboard: $DashboardName"
}
$dashboardId = [string](Get-OptionalProperty -Object $dashboard -Name 'id')
if ([string]::IsNullOrWhiteSpace($dashboardId)) { throw 'EOS Engineering dashboard has no id.' }

$currentFocusId = [string](Get-OptionalProperty -Object $currentFocus -Name 'id')
$uiQueryId = [string](Get-OptionalProperty -Object $uiQuery -Name 'id')
$platformQueryId = [string](Get-OptionalProperty -Object $platformQuery -Name 'id')
$recentDoneId = [string](Get-OptionalProperty -Object $recentDone -Name 'id')
$blockedId = [string](Get-OptionalProperty -Object $blocked -Name 'id')
$ciPipelineId = if ($null -ne $ciPipeline) { [string](Get-OptionalProperty -Object $ciPipeline -Name 'id') } else { '' }
$controlPipelineId = if ($null -ne $controlPipeline) { [string](Get-OptionalProperty -Object $controlPipeline -Name 'id') } else { '' }

$ciPipelineUrl = if ([string]::IsNullOrWhiteSpace($ciPipelineId)) { "$OrganizationUrl/$ProjectName/_build" } else { "$OrganizationUrl/$ProjectName/_build?definitionId=$ciPipelineId" }
$controlPipelineUrl = if ([string]::IsNullOrWhiteSpace($controlPipelineId)) { "$OrganizationUrl/$ProjectName/_build" } else { "$OrganizationUrl/$ProjectName/_build?definitionId=$controlPipelineId" }
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
- [Current Focus]($OrganizationUrl/$ProjectName/_queries/query/$currentFocusId/)
- [UI & Visual Validation]($OrganizationUrl/$ProjectName/_queries/query/$uiQueryId/)
- [Platform & DevOps]($OrganizationUrl/$ProjectName/_queries/query/$platformQueryId/)
- [Recently Completed]($OrganizationUrl/$ProjectName/_queries/query/$recentDoneId/)
- [Blocked]($OrganizationUrl/$ProjectName/_queries/query/$blockedId/)

GitHub is canonical for code/PRs. Azure DevOps owns Boards, Windows CI/test history, dashboards, and Azure VM operations. Use `AB#<id>` in GitHub commits and PR descriptions.
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

# Clone an existing Build History widget from another EOS Team dashboard instead
# of guessing its private settings schema.
$sourceBuildWidget = $null
foreach ($candidateDashboard in @(Get-Items -Object $teamDashboards)) {
    $candidateId = [string](Get-OptionalProperty -Object $candidateDashboard -Name 'id')
    if ([string]::IsNullOrWhiteSpace($candidateId) -or $candidateId -eq $dashboardId) { continue }
    $candidateWidgets = Get-TeamDashboardWidgets -DashboardId $candidateId
    foreach ($candidateWidget in @(Get-Items -Object $candidateWidgets)) {
        $contributionId = [string](Get-OptionalProperty -Object $candidateWidget -Name 'contributionId')
        if ($contributionId -match '(?i)build') {
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
    Write-Warning 'No existing Build History widget was available to clone. EOS Control Center still links directly to EOS CI.'
}

Step -Text 'Project setup complete'
Write-Host "Dashboard:      $DashboardName"
Write-Host "Dashboard team: $ProjectTeamName"
Write-Host "Main pipeline:  $CiPipelineName"
Write-Host 'Boards:         EOS UI, platform, and reliability work seeded and linked'
Write-Host 'Shared queries: Current Focus, UI & Visual Validation, Platform & DevOps, Recently Completed, Blocked'
Write-Host 'Repos:          intentionally unused; GitHub remains canonical'
Write-Host 'VM control:     EOS VM Control remains separate from normal CI'
Write-Host 'Traceability:   use AB#<id> in GitHub commits and PR descriptions' -ForegroundColor Green
