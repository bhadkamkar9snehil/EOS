# Azure / Azure DevOps live provisioning state

Last updated: 2026-08-13 after running `scripts/bootstrap-azure-devops.ps1` from authenticated Azure Cloud Shell.

This is an operational state note for Codex/automation. Verify live state before mutating it, but **do not recreate these resources blindly**.

## Proven existing resources

Azure account discovery succeeded:

- subscription: `Azure subscription 1`
- subscription ID: `69f506f7-b17d-404d-b814-b03d9b1a0d0d`
- tenant ID: `8a2f828d-ac24-494f-a13d-3925fc417612`
- VM: `EOS`
- resource group: `VISUALSTUDIOONLINE-FA4B8E34518844659C2CAABE9BF09BB8`

Azure DevOps Microsoft Entra authentication also succeeded:

- organization: `https://dev.azure.com/apexasnehil`
- project: `EOS`
- project ID: `86924d70-ad42-4615-87ac-529965287f0f`

The bootstrap created successfully:

1. user-assigned managed identity: `eos-devops-control`
2. Azure Resource Manager service connection: `eos-vm-arm`
3. authentication scheme: Workload Identity Federation
4. managed-identity federated credential: `azure-devops-eos-vm-control`
5. Azure role assignment: `Virtual Machine Contributor`
6. RBAC scope: EOS resource group only, **not** the full subscription
7. Azure DevOps variable group: `eos-vm-control`
8. Azure DevOps manual pipeline: `EOS VM Control`

## Failure encountered by the first bootstrap revision

The original bootstrap then attempted to authorize only the `EOS VM Control` pipeline through the Azure DevOps preview protected-resource batch API:

`PATCH /_apis/pipelines/pipelinepermissions?api-version=7.1-preview.1`

Azure DevOps returned HTTP 403 `The requested operation is not allowed`, despite the same signed-in Entra identity being able to create/manage the service connection and other project resources.

This was an Azure DevOps protected-resource authorization issue, **not** an Azure identity/RBAC/WIF creation failure.

## Fix committed

`main` now contains a revised `scripts/bootstrap-azure-devops.ps1` that is safe to rerun and reuses all resources above.

The revised script uses the documented GA Azure DevOps CLI route:

```powershell
az devops service-endpoint update --id <endpoint-id> --enable-for-all true
```

This makes `eos-vm-arm` available to pipelines only within the EOS Azure DevOps project. The Azure blast radius remains constrained independently by the managed identity's `Virtual Machine Contributor` role at the EOS resource-group scope.

The tradeoff is intentional: reliable autonomous control in this single-member/private DevOps project is preferable to blocking on the preview fine-grained pipeline-permissions endpoint. If a future multi-user/multi-trust project requires stricter per-pipeline authorization, tighten it after confirming a supported token/permission route.

## Next validation

Rerun the current bootstrap script from an authenticated Azure Cloud Shell:

```powershell
irm https://raw.githubusercontent.com/bhadkamkar9snehil/EOS/main/scripts/bootstrap-azure-devops.ps1 | iex
```

It should reuse every already-created resource, enable the service endpoint for EOS project pipelines, queue `EOS VM Control` with `operation=status`, poll it, and finish only after the agentless control plane succeeds.

After that, use the control pipeline for VM `status`, `start`, `restart`, `health`, `repair-agent`, `enable-rdp`, and `disable-rdp`; ordinary build/test/render CI remains `azure-pipelines.yml` on the self-hosted Windows agent.
