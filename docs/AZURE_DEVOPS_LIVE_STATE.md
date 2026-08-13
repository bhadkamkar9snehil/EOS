# Azure / Azure DevOps live provisioning state

Last updated: 2026-08-13 after successful end-to-end validation of `scripts/bootstrap-azure-devops.ps1` from authenticated Azure Cloud Shell.

This is an operational state note for Codex/automation. Verify live state before mutating it, but **do not recreate these resources blindly**.

## Proven existing resources

Azure account discovery succeeded:

- subscription: `Azure subscription 1`
- subscription ID: `69f506f7-b17d-404d-b814-b03d9b1a0d0d`
- tenant ID: `8a2f828d-ac24-494f-a13d-3925fc417612`
- VM: `EOS`
- resource group: `VISUALSTUDIOONLINE-FA4B8E34518844659C2CAABE9BF09BB8`

Azure DevOps Microsoft Entra authentication succeeded:

- organization: `https://dev.azure.com/apexasnehil`
- project: `EOS`
- project ID: `86924d70-ad42-4615-87ac-529965287f0f`

The bootstrap has created and validated:

1. user-assigned managed identity: `eos-devops-control`
2. Azure Resource Manager service connection: `eos-vm-arm`
3. authentication scheme: Workload Identity Federation
4. managed-identity federated credential: `azure-devops-eos-vm-control`
5. Azure role assignment: `Virtual Machine Contributor`
6. RBAC scope: EOS resource group only, **not** the full subscription
7. Azure DevOps variable group: `eos-vm-control`
8. Azure DevOps manual pipeline: `EOS VM Control`
9. service connection enabled for pipelines within the EOS Azure DevOps project
10. successful agentless validation run using `operation=status`

Validation run ID: `33`

Observed terminal result:

```text
=== Run an agentless status check to validate the control plane ===
Queued validation run: 33
  status=inProgress result=
  status=completed result=succeeded

=== Bootstrap complete ===
Azure DevOps VM control is operational.
```

## Authorization design

The first bootstrap revision attempted to authorize only the `EOS VM Control` pipeline through the Azure DevOps preview protected-resource batch API:

`PATCH /_apis/pipelines/pipelinepermissions?api-version=7.1-preview.1`

Azure DevOps returned HTTP 403 `The requested operation is not allowed`, despite the same signed-in Entra identity being able to create/manage the service connection and other project resources.

The repaired bootstrap uses the documented Azure DevOps CLI route:

```powershell
az devops service-endpoint update --id <endpoint-id> --enable-for-all true
```

This makes `eos-vm-arm` available to pipelines only within the EOS Azure DevOps project. The Azure blast radius remains independently constrained by the managed identity's `Virtual Machine Contributor` role at the EOS resource-group scope.

The tradeoff is intentional: reliable autonomous control in this single-member/private DevOps project is preferable to blocking on the preview fine-grained pipeline-permissions endpoint. If the project later becomes multi-user/multi-trust, revisit per-pipeline authorization.

## Bootstrap script status

Current script:

`scripts/bootstrap-azure-devops.ps1`

It is intended to be idempotent and should discover/reuse the resources above. The PowerShell helper collision with automatic `$args` was fixed; the successfully validated revision was invoked from commit `30321e3b4914c8de7781afe1febaf296f91527eb`.

Do not ask the user to repeat the bootstrap unless live inspection proves one of the resources is missing or broken.

## Operational model now available

Normal CI remains:

```text
GitHub -> Azure Pipelines -> EOS self-hosted Windows agent -> build/test/render
```

Independent recovery/control is now:

```text
Azure DevOps server/agentless job -> eos-vm-arm WIF identity -> Azure Resource Manager -> EOS VM
```

Use `EOS VM Control` for:

- `status`
- `start`
- `restart`
- `health`
- `repair-agent`
- `enable-rdp`
- `disable-rdp`

This channel does **not** depend on the EOS self-hosted build agent being online.

## Immediate next engineering task

Azure/DevOps bootstrap is complete. The next task is no longer infrastructure setup.

Reconcile draft PR #12 (`Add real Windows desktop visual validation to Tailwind CI`) onto current `main`, preserving:

- the unified `ILogger<T>` / Serilog-backend logging architecture from PR #14;
- the improved current Azure CI compiler-error annotations;
- current `tailwind-input.css` as the single styling source of truth.

Then run the real WPF + WebView2 screenshot pipeline, inspect the generated PNGs/logs/diagnostics, and resume screenshot-driven Tailwind polish autonomously.
