# Cloud CI and resilient Windows VM operations

This is the reusable cloud-CI/runbook pattern behind EOS. It is intentionally generic: the same architecture can be recreated for another GitHub repository, Azure DevOps project, or Windows build VM without depending on remembered UI clicks or a one-off token.

## Architecture to remember

Use independent layers with different failure domains:

1. **GitHub = source of truth**
   - source, branches, pull requests, review history
   - Azure Pipelines GitHub App for repository access and GitHub Checks
2. **Azure DevOps = orchestration**
   - YAML pipelines stored with source
   - build/test history, artifacts, policies, agent pools
3. **Windows Azure VM = execution lab**
   - self-hosted agent for Windows-specific builds, tests, installers, WebView2 rendering, screenshots
   - agent normally runs as a Windows service so CI survives RDP disconnect/reboot
4. **Azure Resource Manager = independent machine control**
   - Azure DevOps **server/agentless job** can query/start/restart the VM without the VM's build agent
   - Azure VM Run Command can execute guest PowerShell through the Azure VM Agent
5. **Interactive/recovery fallbacks**
   - Azure Bastion or deliberately enabled RDP for human GUI access
   - Serial Console + Boot Diagnostics for last-resort recovery

The core rule is:

> **Build execution and machine recovery must not depend on the same agent.**

A self-hosted runner is an excellent execution environment, but it cannot repair itself while stopped/offline. The Azure control plane is the escape hatch.

---

## 1. Connect GitHub to Azure Pipelines

For ordinary GitHub CI, prefer the **Azure Pipelines GitHub App** rather than a GitHub PAT.

General setup:

1. Create an Azure DevOps organization and project.
2. Azure DevOps -> **Pipelines -> New pipeline**.
3. Choose **GitHub** as the source.
4. Install/authorize the Azure Pipelines GitHub App for only the repository/repositories that need CI.
5. Select the repository.
6. Choose an existing YAML file, normally `azure-pipelines.yml`, or create one.
7. Save/run once.
8. Confirm that a push/PR produces an Azure Pipelines **Check** on the GitHub commit.

Keep the YAML in GitHub. Azure DevOps should orchestrate source-controlled configuration, not become a second hidden copy of build logic.

Official references:

- https://learn.microsoft.com/azure/devops/pipelines/repos/github
- https://learn.microsoft.com/azure/devops/pipelines/security/secure-access-to-repos

---

## 2. Provision a Windows self-hosted agent

Use a Windows VM when the workload genuinely benefits from Windows or persistent machine state: WPF/WinUI, WebView2, COM, Windows SDKs, installer/signing tools, GUI capture, heavyweight caches, etc.

### VM prerequisites

- supported Windows/Windows Server image
- PowerShell
- outbound HTTPS to Azure DevOps/GitHub/tool/package endpoints
- enough OS-disk space for source, NuGet/tool caches and artifacts
- one-time administrator access for agent installation

The Azure Pipelines agent includes its own runtime, but the **project SDK/toolchain should still be deterministic**. For .NET, pin `global.json` and use `UseDotNet@2` in CI rather than trusting whichever SDK happens to be installed.

### Agent installation pattern

Use a short path without spaces:

```powershell
New-Item -ItemType Directory -Force C:\agents\build01
Set-Location C:\agents\build01
```

Download/extract the current Windows Azure Pipelines agent from:

```text
Azure DevOps -> Organization settings -> Agent pools -> New agent
```

Then configure from elevated PowerShell:

```powershell
.\config.cmd
```

Azure DevOps Services server URL:

```text
https://dev.azure.com/<organization>
```

Choose the desired pool and a stable agent name.

### Run the agent as a Windows service

Service mode is the normal build configuration. It starts after reboot and continues after RDP disconnect.

Useful diagnostics/repair commands:

```powershell
Get-Service 'vstsagent*'
Get-Service 'vstsagent*' | Format-Table Name,Status,StartType
Restart-Service 'vstsagent*' -Force
```

From the agent directory:

```powershell
.\run.cmd --diagnostics
```

Remove/reconfigure:

```powershell
.\config.cmd remove
```

Official reference:

- https://learn.microsoft.com/azure/devops/pipelines/agents/windows-agent

### Initial registration authentication

If a PAT is used during `config.cmd`, use a short-lived token with only the documented agent-registration scope (Agent Pools read/manage), and do not store it in YAML/source/docs. The registered agent subsequently uses its own credentials.

Official references:

- https://learn.microsoft.com/azure/devops/pipelines/agents/personal-access-token-agent-registration
- https://learn.microsoft.com/azure/devops/pipelines/agents/agent-authentication-options

---

## 3. A robust self-hosted Windows CI YAML

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

- `batch: true` coalesces rapid pushes rather than queuing obsolete main builds behind a single agent.
- PR `autoCancel: true` cancels superseded PR validations.
- `checkout.clean: true` matters because self-hosted workspaces persist between runs.
- restore once, then build with `--no-restore`, test with `--no-build --no-restore`.
- publish TRX/JUnit/etc. with Azure's test-result task rather than burying results in console text.
- publish screenshots, logs, installers and other evidence as Pipeline Artifacts.
- do not make a person manually watch the pipeline as part of the development protocol; the implementation loop includes reading CI and fixing it.

---

## 4. Normal remote execution while the build agent is healthy

An online self-hosted agent is already a remote command channel: PowerShell steps execute on the VM under the agent service identity.

Use it for:

- SDK/tool inventory
- cache repair
- build/test/package work
- VM diagnostics
- collecting logs
- deterministic dependency installation
- rendering/capturing the Windows product

For EOS, `build/vm-health.ps1` is deliberately reusable both from CI and Azure Run Command.

Examples:

```powershell
.\build\vm-health.ps1
.\build\vm-health.ps1 -RepairAgent
.\build\vm-health.ps1 -EnableRdp
.\build\vm-health.ps1 -DisableRdp
```

This path is **normal operations**, not the recovery path. If the agent is unavailable, move to Azure Resource Manager.

---

## 5. Preferred out-of-band recovery: Azure DevOps server job -> Azure Resource Manager

This is the strongest improvement to the basic self-hosted-agent design.

Azure Pipelines supports **server jobs**:

```yaml
jobs:
- job: control_vm
  pool: server
```

A server job executes on Azure DevOps itself. It requires **no build agent and no target computer**. The built-in `InvokeRESTAPI@1` task is agentless and can use an **Azure Resource Manager** service connection.

Create one Azure Resource Manager service connection using **Workload Identity Federation**, scoped as narrowly as practical (usually the VM's resource group). This gives Azure DevOps an Azure control-plane identity without storing a client secret.

Then an agentless pipeline can:

- query VM instance/power/VM-Agent state
- request VM start
- request VM restart
- call Azure VM Run Command to inspect/repair the self-hosted agent
- enable/disable guest RDP configuration if human access is actually needed

Example shape:

```yaml
- task: InvokeRESTAPI@1
  inputs:
    connectionType: connectedServiceNameARM
    azureServiceConnection: '<wif-service-connection>'
    method: GET
    urlSuffix: '/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Compute/virtualMachines/<vm>/instanceView?api-version=2026-03-01'
    waitForCompletion: false
```

The repository's concrete manual control pipeline is `azure-vm-control.yml`. The complete reusable design is documented in `docs/agentless-azure-vm-control.md`.

Official references:

- https://learn.microsoft.com/azure/devops/pipelines/process/phases
- https://learn.microsoft.com/azure/devops/pipelines/tasks/reference/invoke-rest-api-v1
- https://learn.microsoft.com/azure/devops/pipelines/library/connect-to-azure
- https://learn.microsoft.com/azure/devops/pipelines/release/configure-workload-identity

---

## 6. Azure VM Run Command: guest PowerShell without inbound management ports

Azure Run Command uses the **Azure VM Agent**, not the Azure Pipelines agent. It is designed for machine/application management and can be used when RDP or SSH is unavailable.

Azure CLI examples from any authenticated control environment:

```powershell
az vm run-command invoke `
  --resource-group <resource-group> `
  --name <vm-name> `
  --command-id RunPowerShellScript `
  --scripts "Get-Service 'vstsagent*' | Format-Table Name,Status,StartType"
```

Repair agent:

```powershell
az vm run-command invoke `
  --resource-group <resource-group> `
  --name <vm-name> `
  --command-id RunPowerShellScript `
  --scripts "Get-Service 'vstsagent*' | Set-Service -StartupType Automatic; Get-Service 'vstsagent*' | Start-Service"
```

REST shape for agentless Azure DevOps use:

```text
POST /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Compute/virtualMachines/<vm>/runCommand?api-version=2026-03-01
```

with body:

```json
{
  "commandId": "RunPowerShellScript",
  "script": ["Get-Service 'vstsagent*'"]
}
```

Run Command does not depend on inbound 22/3389/5985/5986. It does require the Azure VM Agent to be healthy and able to communicate with Azure.

Official references:

- https://learn.microsoft.com/azure/virtual-machines/run-command-overview
- https://learn.microsoft.com/azure/virtual-machines/windows/run-command
- https://learn.microsoft.com/rest/api/compute/virtual-machines/run-command

---

## 7. VM lifecycle operations

From Azure CLI:

```powershell
az vm start      --resource-group <resource-group> --name <vm-name>
az vm restart    --resource-group <resource-group> --name <vm-name>
az vm deallocate --resource-group <resource-group> --name <vm-name>
```

From an agentless ARM task, call the equivalent Azure Compute REST operations.

Start:

```text
POST /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Compute/virtualMachines/<vm>/start?api-version=2026-03-01
```

Restart:

```text
POST /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Compute/virtualMachines/<vm>/restart?api-version=2026-03-01
```

These may return `202 Accepted` because Azure fabric work continues asynchronously.

---

## 8. Spot VM behavior must be designed for, not treated as a surprise

Spot is suitable for interruptible dev/test/CI workloads, but Azure can evict the VM. With `Deallocate` eviction policy, the VM becomes stopped/deallocated and **does not automatically restart** later. A later start succeeds only if capacity/quota are available.

That means this can be a legitimate state:

```text
push -> pipeline queued -> no matching self-hosted agent
```

The correct reaction is not to debug application code first. Query Azure instance view through the **agentless control pipeline**. If the VM is deallocated, request a start. If Azure cannot allocate Spot capacity, the blocker is infrastructure/capacity rather than source code.

Official reference:

- https://learn.microsoft.com/azure/virtual-machines/spot-vms

---

## 9. Interactive Windows access: separate GUI access from automation

Remote **management** does not require RDP. Use the Azure control plane for automation; use RDP/Bastion only when a human actually needs to see/control the desktop.

Preferred ladder:

1. **Azure Bastion** when appropriate.
2. Deliberately enabled/scoped direct RDP as a fallback.
3. **Serial Console + Boot Diagnostics** when ordinary guest/network paths are broken.

Bastion Developer is free for dev/test and supports one VM connection at a time, but only in supported regions. Check the current region list before building around it; regional support changes over time.

Official references:

- https://learn.microsoft.com/azure/bastion/quickstart-developer
- https://learn.microsoft.com/azure/bastion/bastion-sku-comparison
- https://learn.microsoft.com/troubleshoot/azure/virtual-machines/windows/boot-diagnostics
- https://learn.microsoft.com/troubleshoot/azure/virtual-machines/windows/serial-console-windows

---

## 10. Useful Azure additions

### Azure Automation

Use for independent scheduled/operational workflows such as:

- start before a known CI window
- deallocate after a quiet period
- service repair
- housekeeping/log collection

Extension-based Hybrid Runbook Worker is useful when a runbook should execute locally on the Windows machine.

### Azure Monitor / VM Insights

Add Azure Monitor Agent + a Data Collection Rule when ongoing telemetry is worth the cost/complexity. Useful signals:

- VM heartbeat
- CPU/memory/disk pressure
- Windows event/service failures
- agent service failures

Alerts should be actionable, not a noisy dashboard for its own sake.

### Key Vault

Keep long-lived deployment secrets/signing material/feed credentials in Key Vault. Prefer workload identity so workflows need fewer secrets at all.

### Blob Storage / artifact feed

Azure Blob Storage can be a stable installer/update feed while GitHub remains source control. A release pipeline can publish versioned Velopack/install packages there.

### Pipeline artifacts

Publish generated evidence rather than committing normal build outputs:

- test reports
- installers
- logs
- screenshots
- diagnostics

A small dedicated evidence branch can be useful only when another automated system needs repository-level access to latest rendered evidence; it should not become the canonical artifact store.

---

## 11. Troubleshooting matrix

| Symptom | First question | Recovery path |
|---|---|---|
| Pipeline queued indefinitely | Is the VM/agent available? | Agentless `status`; if deallocated -> `start` |
| VM running but agent offline | Is `vstsagent*` healthy? | Agentless Run Command -> `repair-agent` |
| RDP fails | Guest RDP service/firewall or Azure network? | Run Command -> enable guest RDP, then inspect NSG/Bastion |
| No inbound ports reachable | Is Azure VM Agent healthy? | Run Command; inbound ports are not required |
| Azure VM Agent unhealthy | Is VM/network/boot healthy? | Boot Diagnostics / Serial Console / redeploy investigation |
| Spot VM disappeared from CI | Was it evicted/deallocated? | Agentless instance view -> start when capacity permits |
| Rapid commits create stale queue | Are triggers batched? | `trigger.batch: true`, PR `autoCancel: true` |
| CI auth requires rotating token | Can it use app/WIF instead? | GitHub App + Azure Resource Manager WIF |

---

## 12. Security baseline

- Do not commit PATs, VM passwords, client secrets, signing keys or bearer tokens.
- Prefer GitHub App authentication for source integration.
- Prefer Azure Resource Manager Workload Identity Federation for Azure access.
- Scope Azure roles/service connections to the smallest practical resource scope.
- Authorize the recovery connection only for the control pipeline(s) that need it.
- A self-hosted agent executes repository-controlled code: protect pipeline YAML and agent administration.
- Keep agent install/work directories writable only by appropriate identities.
- Do not treat a globally exposed RDP port as the automation API.
- Keep recovery operations bounded and source-controlled rather than exposing unrestricted arbitrary PowerShell through a pipeline UI.

---

## 13. What “done” looks like

A resilient cloud CI setup is complete when:

- GitHub push/PR automatically creates Azure Pipelines validation
- build environment is reproducible from repository/toolchain manifests
- Windows-specific code actually builds/tests on Windows
- self-hosted agent survives RDP disconnects and reboots
- stale builds are batched/cancelled
- tests/artifacts/logs/screenshots are first-class pipeline evidence
- there is an **agentless Azure control path** that works when the build agent does not
- VM state can be queried without RDP
- VM can be started/restarted through ARM
- guest services can be repaired through Run Command
- human GUI access is a fallback, not a prerequisite for automation
- the whole setup is documented well enough to recreate without relying on memory
