# Shared Windows build agent

The Azure VM currently named `EOS` is a reusable Windows build worker, not an EOS-only machine.

## Agent contract

Azure DevOps organization/project:

- organization: `https://dev.azure.com/apexasnehil`
- project: `EOS`
- pool: `Default`
- agent demand: `Agent.Name -equals EOS`

The single agent serializes jobs. Every pipeline that uses it must also request a clean workspace and clean Git checkout so branch/repository state never leaks between runs.

## Pipelines

### EOS CI

`azure-pipelines.yml`

- builds pushes from any branch;
- validates PRs targeting any branch;
- builds the complete Windows solution;
- runs all EOS test projects;
- performs real WPF/WebView2 visual validation;
- publishes test, visual and build-diagnostic artifacts.

Azure DevOps project/bootstrap mutation is restricted to canonical `main` runs. Feature branches only build/test/render.

### APS CI

The APS repository owns its own `azure-pipelines.yml` and `build/verify.ps1`, but targets this same Windows agent. It builds APS on any branch, runs planning/UI tests and performs a self-contained Windows desktop publish smoke test.

### Windows Build Lab

`azure-generic-windows-build.yml` is a manual branch/tag/SHA-selectable pipeline for EOS and APS. It exists specifically so an arbitrary ref can be verified even when that branch does not contain the latest CI YAML.

The Build Lab clones the requested public GitHub ref into `$(Agent.TempDirectory)\WindowsBuildLab`, runs the repository-specific verification contract, publishes TRX results and preserves diagnostics/artifacts.

It does **not** package or publish releases.

## Reconciliation

`scripts/ensure-shared-windows-pipelines.ps1` runs only from EOS `main` and idempotently ensures these Azure DevOps pipeline definitions exist:

- `Windows Build Lab`
- `APS CI`

It discovers and reuses the same GitHub service connection already used by `EOS CI`, avoiding a second credential path.

## Safety rules

1. No repository gets a persistent shared working directory.
2. No feature branch mutates Azure DevOps project configuration.
3. One agent means one Windows job at a time; queueing is preferred to concurrent filesystem mutation.
4. CI verifies; release scripts package/publish explicitly.
5. Repository/branch selection is data, not a reason to reconfigure the VM.
