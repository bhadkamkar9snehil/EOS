# Agentless Azure VM control from Azure DevOps

This is the preferred out-of-band recovery pattern for a self-hosted Azure Pipelines VM.

The problem with a normal self-hosted pipeline is circular: if the VM is stopped or its Azure Pipelines agent is broken, a job scheduled to that same agent cannot repair it. A Microsoft-hosted runner is one possible escape path, but it is not required.

Azure Pipelines supports **server (agentless) jobs**. These tasks execute in Azure DevOps itself and do not consume or depend on a build agent. The built-in `InvokeRESTAPI@1` task supports an **Azure Resource Manager** service connection, so a server job can call Azure management APIs directly.

That gives two independent control paths:

```text
GitHub -> Azure Pipelines -> Windows self-hosted agent -> build/test/capture

Azure DevOps server job -> Azure Resource Manager -> VM lifecycle / Run Command
```

If the first path is unavailable, the second still exists.

## One-time prerequisite: secretless Azure Resource Manager service connection

Create an **Azure Resource Manager** service connection in Azure DevOps using **Workload Identity Federation**. Prefer the current Microsoft Entra issuer flow; do not create a long-lived client secret.

General setup:

1. Azure DevOps project -> **Project settings -> Service connections -> New service connection**.
2. Choose **Azure Resource Manager**.
3. Choose a Workload Identity Federation option using either an app registration or managed identity.
4. Scope the identity to the smallest useful Azure scope — ideally the resource group containing the CI VM, rather than the whole subscription.
5. Give the identity only the Azure role(s) required for VM lifecycle and Run Command operations.
6. Do **not** grant the connection to every pipeline. Explicitly authorize only the recovery/control pipeline.
7. Record the service-connection name in pipeline configuration; no secret is stored in Git.

Microsoft recommends workload identity federation for Azure Resource Manager service connections because it avoids storing and rotating service-principal secrets.

Official references:

- https://learn.microsoft.com/azure/devops/pipelines/release/configure-workload-identity
- https://learn.microsoft.com/azure/devops/pipelines/release/azure-rm-endpoint
- https://learn.microsoft.com/azure/devops/pipelines/tasks/reference/invoke-rest-api-v1
- https://learn.microsoft.com/azure/devops/pipelines/process/phases

## Required Azure identifiers

Keep these as Azure DevOps variables or variable-group values, not hard-coded into reusable templates:

```text
AZURE_SUBSCRIPTION_ID
AZURE_RESOURCE_GROUP
AZURE_VM_NAME
AZURE_VM_SERVICE_CONNECTION
```

None of these values is a password or bearer token.

## Agentless server-job syntax

A server job is selected with the reserved pool value `server`:

```yaml
jobs:
- job: control_vm
  pool: server
  steps:
  - task: InvokeRESTAPI@1
    inputs:
      connectionType: connectedServiceNameARM
      azureServiceConnection: '<service-connection-name>'
      method: POST
      urlSuffix: '<Azure Resource Manager path>'
      waitForCompletion: false
```

`InvokeRESTAPI@1` is intentionally an agentless-only task. For Azure Resource Manager connections it uses the selected Azure environment's Resource Manager endpoint (normally `https://management.azure.com`) and authenticates through the service connection.

## Start a stopped/deallocated VM

Azure Compute exposes VM lifecycle operations through Resource Manager. A start request is:

```text
POST /subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Compute/virtualMachines/<vm-name>/start?api-version=2025-11-01
```

Agentless pipeline pattern:

```yaml
- task: InvokeRESTAPI@1
  displayName: Start VM
  inputs:
    connectionType: connectedServiceNameARM
    azureServiceConnection: '$(AZURE_VM_SERVICE_CONNECTION)'
    method: POST
    urlSuffix: '/subscriptions/$(AZURE_SUBSCRIPTION_ID)/resourceGroups/$(AZURE_RESOURCE_GROUP)/providers/Microsoft.Compute/virtualMachines/$(AZURE_VM_NAME)/start?api-version=2025-11-01'
    waitForCompletion: false
```

A successful lifecycle request can be asynchronous (`202 Accepted`). The VM may need time to boot and for the Azure VM Agent / Azure Pipelines service to become healthy afterward.

Official REST reference:

- https://learn.microsoft.com/rest/api/compute/virtual-machines/start

## Restart a VM

```text
POST /subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Compute/virtualMachines/<vm-name>/restart?api-version=2025-11-01
```

Official REST reference:

- https://learn.microsoft.com/rest/api/compute/virtual-machines/restart

## Repair the guest without RDP: Azure Run Command

Once the VM is running and its Azure VM Agent is healthy, Azure Run Command can execute PowerShell inside Windows even if RDP, SSH, WinRM or the Azure Pipelines agent is unavailable.

REST shape:

```text
POST /subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Compute/virtualMachines/<vm-name>/runCommand?api-version=2025-04-01
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

Agentless task pattern:

```yaml
- task: InvokeRESTAPI@1
  displayName: Repair Azure Pipelines agent
  inputs:
    connectionType: connectedServiceNameARM
    azureServiceConnection: '$(AZURE_VM_SERVICE_CONNECTION)'
    method: POST
    urlSuffix: '/subscriptions/$(AZURE_SUBSCRIPTION_ID)/resourceGroups/$(AZURE_RESOURCE_GROUP)/providers/Microsoft.Compute/virtualMachines/$(AZURE_VM_NAME)/runCommand?api-version=2025-04-01'
    headers: |
      {
        "Content-Type": "application/json"
      }
    body: |
      {
        "commandId": "RunPowerShellScript",
        "script": [
          "Get-Service 'vstsagent*' | Set-Service -StartupType Automatic",
          "Get-Service 'vstsagent*' | Start-Service"
        ]
      }
    waitForCompletion: false
```

Official references:

- https://learn.microsoft.com/azure/virtual-machines/run-command-overview
- https://learn.microsoft.com/rest/api/compute/virtual-machines/run-command

## Why this is stronger than public WinRM/SSH

This pattern does not require inbound TCP access from the orchestration environment. The Azure control plane authenticates the request and the VM Agent performs guest execution. It therefore still works when:

- port 3389 is closed
- SSH/WinRM is disabled
- NSG rules reject inbound management traffic
- the self-hosted Azure Pipelines service has stopped

For a dev VM, public RDP can remain an optional human fallback rather than the automation backbone.

## Recommended recovery pipeline operations

A small manual/parameterized control pipeline should expose only bounded operations such as:

- `start`
- `restart`
- `repair-agent`
- `enable-rdp`
- `disable-rdp`
- `health`

Do not turn an agentless pipeline into an unrestricted arbitrary-command endpoint. Keep normal maintenance scripts in source control and make the recovery pipeline invoke known operations.

## Additional independent Azure control plane

Azure Automation is another useful layer. A cloud runbook can start the VM and call VM Run Command, and an extension-based Hybrid Runbook Worker can run operational runbooks directly on a running Windows machine. The current extension-based worker is the supported path; the older agent-based User Hybrid Runbook Worker has retired.

Azure Automation is useful for:

- scheduled start/stop
- periodic housekeeping
- service repair
- log collection
- webhook-triggered operational jobs
- jobs that should exist independently of the source-build pipeline

Official references:

- https://learn.microsoft.com/azure/automation/extension-based-hybrid-runbook-worker-install
- https://learn.microsoft.com/azure/automation/automation-hrw-run-runbooks

## Interactive fallback

Automation and Run Command solve remote **management**, not human GUI interaction. For interactive Windows access, prefer Azure Bastion rather than depending on a globally exposed RDP port. Bastion Developer is free for dev/test but only available in supported regions and supports one VM connection at a time.

This gives a clean hierarchy:

```text
1. Azure Pipelines self-hosted agent      normal build/development automation
2. Azure DevOps agentless ARM job         start/restart/repair when agent is unavailable
3. Azure VM Run Command                   guest PowerShell through Azure VM Agent
4. Azure Automation                       scheduled/independent operational workflows
5. Azure Bastion                          interactive human RDP fallback
6. Serial Console + Boot Diagnostics      last-resort recovery
```
