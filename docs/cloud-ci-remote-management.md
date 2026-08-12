# Cloud CI and resilient Windows VM operations

This document is deliberately **generic**. It captures the reusable pattern behind EOS so the same setup can be reproduced for another GitHub repository, Azure DevOps project, or Windows build VM without relying on one person's browser history or a one-off token.

## Architecture to remember

Use four independent layers:

1. **GitHub = source of truth**
   - source code, branches, pull requests, review history
   - Azure Pipelines GitHub App connection for repository access and GitHub Checks
2. **Azure DevOps = CI orchestrator**
   - YAML pipeline in the GitHub repository
   - build/test history, test results, pipeline policy, agent pools
3. **Windows Azure VM = self-hosted build machine**
   - required for WPF/WebView2/Windows-targeted builds
   - Azure Pipelines agent runs as a Windows service and survives RDP disconnects/reboots
4. **Azure control plane = break-glass management path**
   - Azure Bastion for interactive RDP without exposing 3389 publicly
   - Azure VM Run Command for remote PowerShell even when RDP is unavailable
   - Serial Console + Boot Diagnostics if networking/RDP/guest-agent access is broken

The important design rule is that **CI and VM recovery must not depend on the same channel**. A self-hosted agent is excellent for normal CI, but if that agent is offline it cannot repair itself. Azure Run Command/Bastion/Serial Console provide an independent recovery path.

---

## 1. GitHub -> Azure Pipelines connection

Microsoft's recommended GitHub authentication for Azure Pipelines CI is the **Azure Pipelines GitHub App**, not a GitHub PAT.

General setup:

1. Create an Azure DevOps organization and project.
2. In Azure Pipelines, create a new pipeline and choose **GitHub** as the repository source.
3. Install/authorize the **Azure Pipelines** GitHub App for only the repositories that need CI access.
4. Select an existing YAML file in the repository, normally `azure-pipelines.yml`.
5. Save and run once.
6. Verify that pushes/PRs create an **Azure Pipelines GitHub Check** on the GitHub commit.

Security rule: grant the GitHub App only the repositories required by the project; do not create a broad GitHub PAT just for ordinary CI.

Official reference:

- https://learn.microsoft.com/azure/devops/pipelines/repos/github
- https://learn.microsoft.com/azure/devops/pipelines/security/secure-access-to-repos

---

## 2. Provision a Windows self-hosted agent

A Windows Azure VM is useful when the build genuinely requires Windows (WPF, WinUI, COM, native Windows SDK, WebView2, installer tooling, etc.). Do not use a self-hosted VM merely because it exists; use it when the workload benefits from persistent Windows state or cannot run on Linux.

### VM prerequisites

- supported Windows/Windows Server image
- PowerShell
- outbound HTTPS access to Azure DevOps/GitHub/tool download endpoints
- enough disk for source, NuGet packages, build outputs and cached SDK/tooling
- an administrator available for one-time agent installation

The Azure Pipelines agent carries its own .NET runtime. Project SDKs should still be installed deterministically by the pipeline (`UseDotNet@2` + `global.json`).

### Agent install pattern

Use a short path with no spaces, for example:

```powershell
New-Item -ItemType Directory -Force C:\agents\build01
Set-Location C:\agents\build01
```

Download and extract the current Windows Azure Pipelines agent from Azure DevOps **Organization settings -> Agent pools -> New agent**, then configure it from an elevated PowerShell window:

```powershell
.\config.cmd
```

For Azure DevOps Services the server URL is:

```text
https://dev.azure.com/<organization>
```

Choose the required pool and a stable agent name.

### Run the agent as a Windows service

Service mode is the preferred CI configuration. It keeps the agent alive when the RDP session closes and starts it again after a reboot.

During `config.cmd`, choose **run as service**. A built-in low-privilege account such as `NT AUTHORITY\NETWORK SERVICE` is often sufficient for build agents; use a dedicated account only when the workload requires additional local/network permissions.

Useful commands after configuration:

```powershell
Get-Service 'vstsagent*'
Get-Service 'vstsagent*' | Format-Table Name,Status,StartType
Restart-Service 'vstsagent*'
```

Agent diagnostics:

```powershell
.\run.cmd --diagnostics
```

Agent removal/reconfiguration:

```powershell
.\config.cmd remove
```

Official reference:

- https://learn.microsoft.com/azure/devops/pipelines/agents/windows-agent

### PAT rule for agent registration

A PAT is **not** required for normal agent communication after registration. If PAT authentication is chosen during initial registration, use a short-lived token with only **Agent Pools (read, manage)** scope. Microsoft documents that the PAT is used only during registration, not for subsequent agent communication.

Therefore:

- never store the registration PAT in YAML, source control, screenshots or docs
- revoke it after successful registration if it is no longer needed
- prefer device-code or service-principal registration where appropriate

Official reference:

- https://learn.microsoft.com/azure/devops/pipelines/agents/personal-access-token-agent-registration
- https://learn.microsoft.com/azure/devops/pipelines/agents/agent-authentication-options

---

## 3. The CI YAML pattern

A minimal Windows self-hosted pipeline looks like this:

```yaml
trigger:
  batch: true
  branches:
    include:
      - main

pr:
  autoCancel: true
  branches:
    include:
      - main

jobs:
- job: windows_ci
  pool:
    name: Default
    demands:
      - Agent.Name -equals <agent-name>

  steps:
  - checkout: self
    clean: true

  - task: UseDotNet@2
    inputs:
      packageType: sdk
      useGlobalJson: true
      workingDirectory: $(Build.SourcesDirectory)

  - powershell: dotnet restore <solution>
    displayName: Restore

  - powershell: dotnet build <solution> -c Release --no-restore
    displayName: Build

  - powershell: dotnet test <test-project> -c Release --no-build --no-restore
    displayName: Test
```

Important details:

- `batch: true` prevents a burst of pushes from creating a long queue of stale CI builds; while one CI run is active, newer commits are coalesced into the next run.
- PR `autoCancel: true` cancels obsolete PR validation when a newer commit arrives.
- `checkout.clean: true` is important on persistent self-hosted agents because workspaces survive across jobs.
- pin the .NET SDK with `global.json` and `UseDotNet@2`; do not rely on whatever SDK happens to be installed on the VM.
- restore once, build with `--no-restore`, then test with `--no-build --no-restore`.
- publish TRX/JUnit/etc. using the platform's test-results task so failures are visible as first-class test results rather than buried in console text.

For EOS, `azure-pipelines.yml` is the authoritative CI definition.

---

## 4. Remote management: use a ladder, not one fragile RDP path

### Level A - Azure Pipelines agent (normal remote execution)

If the self-hosted agent is online, the pipeline itself is a remote execution channel. PowerShell steps execute on the VM under the agent service account.

This is ideal for:

- checking installed SDKs/tooling
- repairing caches
- collecting diagnostics
- running builds/tests
- installing deterministic build dependencies
- updating the repository workspace

A reusable local/Run-Command health script lives at:

```text
build/vm-health.ps1
```

Inspect without modifying:

```powershell
.\build\vm-health.ps1
```

Repair/restart Azure Pipelines agent service:

```powershell
.\build\vm-health.ps1 -RepairAgent
```

Enable Windows RDP service/firewall (Azure NSG/Bastion still controls network reachability):

```powershell
.\build\vm-health.ps1 -EnableRdp
```

Disable Windows RDP again:

```powershell
.\build\vm-health.ps1 -DisableRdp
```

### Level B - Azure VM Run Command (break-glass PowerShell)

Run Command uses the **Azure VM Agent**, not the Azure Pipelines agent. This is exactly what we want when CI is offline.

Typical Azure CLI operations:

```powershell
az vm run-command invoke `
  --resource-group <resource-group> `
  --name <vm-name> `
  --command-id RunPowerShellScript `
  --scripts "Get-Service 'vstsagent*' | Format-Table Name,Status,StartType"
```

Restart a stopped Azure Pipelines agent:

```powershell
az vm run-command invoke `
  --resource-group <resource-group> `
  --name <vm-name> `
  --command-id RunPowerShellScript `
  --scripts "Get-Service 'vstsagent*' | Start-Service"
```

Or, if the repository already exists on the VM, invoke the reusable recovery script:

```powershell
az vm run-command invoke `
  --resource-group <resource-group> `
  --name <vm-name> `
  --command-id RunPowerShellScript `
  --scripts "& '<repo-path>\build\vm-health.ps1' -RepairAgent"
```

VM lifecycle commands:

```powershell
az vm start      --resource-group <resource-group> --name <vm-name>
az vm restart    --resource-group <resource-group> --name <vm-name>
az vm deallocate --resource-group <resource-group> --name <vm-name>
```

Official reference:

- https://learn.microsoft.com/azure/virtual-machines/run-command-overview
- https://learn.microsoft.com/azure/virtual-machines/windows/run-command

### Level C - Azure Bastion Developer (interactive RDP without public 3389)

For a dev/test Windows VM in a supported region, **Azure Bastion Developer is free** and provides browser-based RDP over the Azure control plane. It does not require the VM to have a public IP. It supports one VM connection at a time and is intentionally limited compared with paid Bastion SKUs.

Current supported India regions include **Central India** and **South India**.

Preferred interactive-access pattern:

1. VM -> **Connect -> Bastion** in Azure portal.
2. Use **Bastion Developer** if the VNet/region supports it.
3. Connect through the browser using the VM's private network path.
4. Do not leave public TCP/3389 open to the Internet.

Official reference:

- https://learn.microsoft.com/azure/bastion/quickstart-developer
- https://learn.microsoft.com/azure/bastion/bastion-sku-comparison

### Level D - Serial Console + Boot Diagnostics

If RDP is broken and Azure VM Run Command cannot recover the machine, use Azure Serial Console / Boot Diagnostics.

Boot diagnostics gives hypervisor screenshots and console information. Serial Console can provide a Windows SAC command channel on supported Windows Server images and is useful for repairing RDP/network configuration.

Official reference:

- https://learn.microsoft.com/troubleshoot/azure/virtual-machines/windows/boot-diagnostics
- https://learn.microsoft.com/troubleshoot/azure/virtual-machines/windows/serial-console-windows

---

## 5. Make Azure itself capable of repairing the build VM

The strongest long-term setup is a **second Azure DevOps control-plane pipeline** that does **not** run on the target VM.

Create an **Azure Resource Manager service connection using Workload Identity Federation**. This gives Azure Pipelines access to the resource group/VM without storing a client secret.

Recommended scope: grant only the minimum role required on the resource group containing the CI VM, not the entire subscription unless necessary.

Then a Microsoft-hosted/control agent can execute commands such as:

```yaml
- task: AzureCLI@2
  inputs:
    azureSubscription: '<workload-identity-service-connection>'
    scriptType: pscore
    scriptLocation: inlineScript
    inlineScript: |
      az vm run-command invoke `
        --resource-group '<resource-group>' `
        --name '<vm-name>' `
        --command-id RunPowerShellScript `
        --scripts "Get-Service 'vstsagent*' | Start-Service"
```

That produces two independent paths:

```text
GitHub -> Azure Pipelines -> self-hosted agent -> VM          (normal CI)
Azure Pipelines control job -> Azure Resource Manager -> VM   (recovery)
```

If the first path fails because the agent is offline, the second path can start/restart it.

Microsoft recommends workload identity federation for Azure Resource Manager service connections because it avoids stored secrets.

Official reference:

- https://learn.microsoft.com/azure/devops/pipelines/library/connect-to-azure
- https://learn.microsoft.com/azure/devops/pipelines/release/configure-workload-identity

---

## 6. Useful Azure additions

### Azure Monitor + VM Insights

Install Azure Monitor Agent and a Data Collection Rule when VM telemetry becomes useful. At minimum, monitor:

- VM heartbeat
- CPU/memory/disk pressure
- Windows event/service failures
- Azure Pipelines agent service failures if collected

Use alerts only for conditions that require action; avoid noisy dashboards for a single dev VM.

### Auto-shutdown

For a CI/dev VM that does not need to stay online 24/7, Azure VM auto-shutdown can reduce cost. If scheduled CI is required, pair shutdown with a start mechanism before the build window.

Official reference:

- https://learn.microsoft.com/azure/virtual-machines/auto-shutdown-vm

### Key Vault

Put long-lived deployment secrets, signing material and feed credentials in Azure Key Vault rather than repository variables. Prefer workload identities so many workflows need no secret at all.

### Azure Storage as an installer/update feed

For desktop releases, Azure Blob Storage can become a stable artifact/update feed. A release pipeline can publish the Velopack packages to a versioned container while GitHub remains the source repository. This cleanly separates source hosting from binary distribution.

### Pipeline artifacts/test history

Publish installers, logs, screenshots and test results as pipeline artifacts. Keep source-controlled files deterministic and keep generated build outputs out of Git.

---

## 7. Troubleshooting matrix

| Symptom | First check | Independent recovery path |
|---|---|---|
| Build is queued indefinitely | Agent online/enabled? Exact `demands` match? Parallel job available? | VM Run Command -> inspect/restart `vstsagent*` |
| Agent service exists but CI cannot connect | Agent diagnostics, outbound HTTPS/DNS, service account | Azure Run Command / Bastion |
| RDP fails | `TermService`, `fDenyTSConnections`, Windows Firewall, NSG | Azure Run Command -> `vm-health.ps1 -EnableRdp` |
| RDP port should not be public | Remove public NSG rule | Bastion Developer |
| VM networking is broken | Boot diagnostics / effective NSG rules | Serial Console |
| VM does not boot cleanly | Boot Diagnostics screenshot/logs | Serial Console / disk repair |
| Multiple commits create stale queued builds | Enable `trigger.batch: true` | N/A |
| CI depends on an expired token | Replace token-backed connection | GitHub App / workload identity federation |

---

## 8. Security baseline

- Do not publish PATs, VM passwords, client secrets or signing keys.
- A token accidentally pasted into chat/logs/screenshots should be treated as exposed and revoked.
- A self-hosted build agent executes repository-controlled code. Restrict who can modify pipeline YAML and who can administer its machine/pool.
- Keep the agent installation/work folders writable only by administrators and the agent identity.
- Avoid a permanent Internet-facing RDP rule. Prefer Bastion or tightly scoped JIT access.
- Scope Azure service connections to the smallest practical resource scope and explicitly authorize only the pipelines that need them.
- Prefer workload identity federation over stored service-principal secrets.

---

## 9. What 'done' looks like

A resilient setup is complete when all of these are true:

- a GitHub push/PR automatically creates an Azure Pipelines run
- the Windows build is reproducible from `global.json` + repository files
- the self-hosted agent runs as a service and survives RDP disconnect/reboot
- stale commits are batched/cancelled rather than filling the queue
- there is a non-RDP recovery path (`az vm run-command`)
- there is a secure interactive path (preferably Bastion Developer for dev/test)
- Boot Diagnostics/Serial Console are available as last-resort tools
- Azure credentials are federated or stored in Key Vault, not committed
- the process is documented well enough to recreate without remembering one-off UI clicks
