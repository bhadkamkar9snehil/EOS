# EOS Azure DevOps project model

EOS deliberately uses GitHub and Azure DevOps together rather than duplicating the same responsibility in both systems.

## Responsibility split

| Concern | System of record |
|---|---|
| Source code | GitHub `bhadkamkar9snehil/EOS` |
| Pull requests / code review | GitHub |
| Engineering backlog / traceability | Azure Boards |
| Windows CI / test history | Azure Pipelines |
| Build / screenshot artifacts | Azure Pipeline Artifacts |
| Windows VM lifecycle / recovery | Azure + `EOS VM Control` pipeline |
| Engineering overview | Azure DevOps dashboard |

Azure Repos is intentionally not used for EOS source. Importing GitHub into Azure Repos would create two repositories that can diverge.

## Azure Boards model

EOS uses the Basic process and keeps the hierarchy deliberately small:

```text
Epic
  -> Issue
       -> Task
```

Epics represent durable engineering domains, Issues represent meaningful deliverables, and Tasks are concrete implementation steps.

The project bootstrap seeds known current/historical work in three domains:

- UI & Design System
- Engineering Platform
- Reliability & Diagnostics

Shared queries live under `Shared Queries/EOS`:

- Current Focus
- UI & Visual Validation
- Platform & DevOps
- Recently Completed
- Blocked

Do not create work items for every tiny code edit. Boards should preserve intent, delivery status and traceability rather than become a second commit log.

## GitHub <-> Boards traceability

The GitHub connection is already installed for EOS. Link commits and pull requests to Azure Boards by including:

```text
AB#123
```

in the GitHub commit message or pull request description, where `123` is the Azure Boards work-item ID.

The useful chain is:

```text
Azure Boards work item
        <-> GitHub PR / commit
        -> Azure Pipelines build/test evidence
```

## Pipelines

`EOS CI` is the authoritative normal engineering pipeline. It is sourced from GitHub and runs on the self-hosted Windows agent.

`EOS VM Control` is separate and manual/agentless. It exists only for VM status/start/restart/health/repair operations through Azure Resource Manager and Workload Identity Federation.

Do not collapse those responsibilities into one pipeline.

## Dashboard

`EOS Engineering` is the intended day-to-day portal dashboard. It contains/links to:

- GitHub repository and active visual-validation PR;
- EOS CI;
- EOS VM Control;
- Azure VM;
- current/shared Boards queries;
- new-work-item creation;
- build history when an existing Build History widget configuration is available to clone.

Azure-Repos `Code Tile` widgets are removed because they cannot represent the GitHub repository correctly.

## Services intentionally not forced into use

### Azure Repos

Left empty. GitHub is canonical.

### Test Plans

Use when EOS has a real manual acceptance/regression test suite that benefits from formal test cases and test runs. Unit/integration/UI automation remains in normal CI.

### Azure Artifacts feeds

Use when EOS begins publishing or consuming private reusable NuGet/npm packages. Pipeline Artifacts are sufficient for build outputs and visual evidence.

### Environments / deployment jobs

Introduce when EOS has a real deployment target or release promotion workflow. A build VM is not automatically a deployment environment.

### Wiki

Repository documentation under `docs/` remains canonical. Avoid creating a second documentation corpus merely because Azure DevOps offers Wiki.

## Reproducible setup

After authenticating Azure CLI/Cloud Shell to the same Microsoft Entra tenant as Azure DevOps, run:

```powershell
irm https://raw.githubusercontent.com/bhadkamkar9snehil/EOS/main/scripts/bootstrap-azure-devops-project.ps1 | iex
```

The script is intended to be rerunnable: it reuses exact work items, queries, dashboard and pipeline definitions where they already exist.
