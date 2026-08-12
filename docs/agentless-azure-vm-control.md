# Agentless Azure VM control from Azure DevOps

This document captures the reusable out-of-band management pattern for a Windows Azure VM that also hosts a self-hosted Azure Pipelines agent.

The key problem is circular dependency: if the VM is stopped or its build-agent service is broken, a pipeline job scheduled to that same VM cannot repair it. The recovery channel must therefore run somewhere else.

Azure Pipelines provides **server jobs** (`pool: server`). They execute on Azure DevOps itself and require neither a self-hosted agent nor a Microsoft-hosted runner. The built-in `InvokeRESTAPI@1` task can authenticate to Azure Resource Manager through an Azure Resource Manager service connection. With Workload Identity Federation, this is also a zero-secret control plane.

The resulting architecture is:

```text
NORMAL DEVELOPMENT
GitHub -> Azure Pipelines -> self-hosted Windows agent -> build/test/render

OUT-OF-BAND RECOVERY
Azure DevOps server job -> Azure Resource Manager -> VM start/restart/Run Command
```

If the first path is unavailable, the second path remains independent.

---

## 1. One-time Azure Resource Manager connection

In Azure DevOps:

1. Open the project.
2. Go to **Project settings -> Service connections -> New service connection**.
3. Choose **Azure Resource Manager**.
4. Prefer **App registration (automatic) + Workload identity federation** when available. A managed-identity WIF connection is also valid when that better matches the Azure tenant/permissions model.
5. Scope the connection to the smallest useful Azure scope, normally the resource group containing the CI/dev VM rather than the entire subscription.
6. Give the identity only the role/permissions required for VM lifecycle and Run Command operations.
7. Do not enable unrestricted "Grant access permission to all pipelines" unless that is genuinely intended. Authorize the dedicated control pipeline explicitly.

Workload Identity Federation avoids client secrets/certificates that need storage and rotation. Microsoft currently recommends the Microsoft Entra issuer flow for Resource Manager WIF service connections; the older Azure DevOps issuer form is being deprecated.

Official references:

- https://learn.microsoft.com/azure/devops/pipelines/library/connect-to-azure
- https://learn.microsoft.com/azure/devops/pipelines/release/configure-workload-identity

---

## 2. Store identifiers, not secrets

Create one Azure DevOps variable group for the target VM. For the EOS implementation the expected group is named `eos-vm-control`, but the pattern is generic.

Store:

```text
AZURE_VM_SERVICE_CONNECTION   Azure DevOps service-connection name
AZURE_SUBSCRIPTION_ID         subscription GUID
AZURE_RESOURCE_GROUP          resource group containing the VM
AZURE_VM_NAME                 Azure VM resource name
```

These are resource identifiers, not credentials. Authentication stays in the Workload Identity Federation service connection.

---

## 3. Why `pool: server` matters

A normal YAML job needs an agent. A server job does not:

```yaml
jobs:
- job: control_vm
  pool: server
  steps:
  - task: InvokeRESTAPI@1
    inputs:
      connectionType: connectedServiceNameARM
      azureServiceConnection: '$(AZURE_VM_SERVICE_CONNECTION)'
      method: GET
      urlSuffix: '<resource-manager-path>'
      waitForCompletion: false
```

Azure DevOps documents server jobs as executing on the server without an agent or target computer. `InvokeRESTAPI@1` is one of the built-in agentless tasks and supports an Azure Resource Manager service connection.

Official references:

- https://learn.microsoft.com/azure/devops/pipelines/process/phases
- https://learn.microsoft.com/azure/devops/pipelines/tasks/reference/invoke-rest-api-v1

---

## 4. VM power-state and lifecycle operations

The Azure Compute REST API exposes lifecycle operations through Azure Resource Manager.

Current API shape:

```text
GET  /subscriptions/<subscription>/resourceGroups/<rg>/providers/Microsoft.Compute/virtualMachines/<vm>/instanceView?api-version=2026-03-01
POST /subscriptions/<subscription>/resourceGroups/<rg>/providers/Microsoft.Compute/virtualMachines/<vm>/start?api-version=2026-03-01
POST /subscriptions/<subscription>/resourceGroups/<rg>/providers/Microsoft.Compute/virtualMachines/<vm>/restart?api-version=2026-03-01
```

The start/restart calls can return `202 Accepted`, because the actual Azure fabric operation may continue asynchronously.

Official references:

- https://learn.microsoft.com/rest/api/compute/virtual-machines/start
- https://learn.microsoft.com/rest/api/compute/virtual-machines/restart

---

## 5. Guest PowerShell without RDP, SSH or WinRM

Azure **Run Command** uses the Azure VM Agent to run PowerShell inside a Windows VM. It is specifically useful for diagnosis/recovery when the guest is not reachable through ordinary management ports.

REST shape:

```text
POST /subscriptions/<subscription>/resourceGroups/<rg>/providers/Microsoft.Compute/virtualMachines/<vm>/runCommand?api-version=2026-03-01
```

Body:

```json
{
  "commandId": "RunPowerShellScript",
  "script": [
    "Get-Service 'vstsagent*' | Format-Table Name,Status,StartType",
    "Get-Service 'vstsagent*' | Set-Service -StartupType Automatic",
    "Get-Service 'vstsagent*' | Start-Service"
  ]
}
```

This path does **not** require inbound RDP/SSH/WinRM. It does require the Azure VM Agent to be healthy and able to communicate with Azure.

Useful recovery commands include:

```powershell
Get-Service 'vstsagent*'
Get-Service 'vstsagent*' | Set-Service -StartupType Automatic
Get-Service 'vstsagent*' | Start-Service
Get-Service 'vstsagent*' | Restart-Service -Force
```

RDP guest-side recovery:

```powershell
Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0
Set-Service TermService -StartupType Automatic
Start-Service TermService
Enable-NetFirewallRule -DisplayGroup 'Remote Desktop'
```

Official references:

- https://learn.microsoft.com/azure/virtual-machines/run-command-overview
- https://learn.microsoft.com/azure/virtual-machines/windows/run-command
- https://learn.microsoft.com/rest/api/compute/virtual-machines/run-command

---

## 6. Repository implementation

`azure-vm-control.yml` is the manual-only agentless control pipeline.

It exposes bounded operations:

```text
status
start
restart
health
repair-agent
enable-rdp
disable-rdp
```

It deliberately does **not** expose arbitrary user-supplied PowerShell. Normal maintenance logic should live in reviewed source-controlled scripts; the recovery pipeline should remain a small, constrained control surface.

`status` is special: it is pure Azure control-plane state and works even if the guest OS or Azure VM Agent is unhealthy.

`health`, `repair-agent`, `enable-rdp`, and `disable-rdp` use Azure Run Command, so they require the Azure VM Agent.

---

## 7. Spot VM consideration

A Spot VM is appropriate for interruptible dev/test/CI work, but Azure can evict it when capacity is required. With `Deallocate` eviction policy, an evicted VM becomes stopped/deallocated and is **not automatically restarted** later. A later start is possible only when capacity/quota are available.

That means a Spot-backed self-hosted CI system should assume this state can occur:

```text
GitHub push -> Azure pipeline queued -> no self-hosted agent available
```

The agentless control pipeline gives us a way to query the VM state and request a start without relying on the missing agent. If Azure cannot allocate Spot capacity, that failure is a real infrastructure constraint rather than a pipeline-code failure.

Official reference:

- https://learn.microsoft.com/azure/virtual-machines/spot-vms

---

## 8. Interactive Windows access

Azure Run Command solves **remote management**; it is not a graphical RDP session.

For occasional human GUI access, use this hierarchy:

1. Azure Bastion when available/appropriate.
2. Direct RDP only when deliberately enabled and network-scoped.
3. Serial Console + Boot Diagnostics for last-resort recovery.

Bastion Developer is a free dev/test SKU in supported regions and allows one VM connection at a time. Regional availability is limited, so check the current supported-region list before designing around it.

Official references:

- https://learn.microsoft.com/azure/bastion/quickstart-developer
- https://learn.microsoft.com/azure/bastion/bastion-sku-comparison

---

## 9. Optional next Azure layer: Automation

Azure Automation can host cloud runbooks for scheduled operations and extension-based Hybrid Runbook Workers for scripts that should run locally on a machine.

Useful cases:

- scheduled start before a known CI window
- scheduled deallocation after a quiet period
- periodic service health/repair
- log collection/housekeeping
- independent operational runbooks that should not be coupled to source builds

Do not use scheduled jobs as a substitute for event-driven CI, but they are useful for cost and maintenance policy.

---

## 10. Recovery decision tree

```text
Pipeline queued / agent unavailable
        |
        v
agentless STATUS (Azure instance view)
        |
        +-- VM deallocated --> START
        |
        +-- VM running ------> guest HEALTH via Run Command
                                   |
                                   +-- vstsagent stopped --> REPAIR-AGENT
                                   |
                                   +-- RDP broken --------> ENABLE-RDP if human GUI is actually needed
                                   |
                                   +-- VM Agent unhealthy -> Boot Diagnostics / Serial Console / redeploy path
```

This is the key operating principle: **build execution and machine recovery must not depend on the same agent.**
