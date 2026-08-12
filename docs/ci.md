# CI/CD

## Authoritative CI: Azure Pipelines on the Windows build VM

EOS uses `azure-pipelines.yml` as its primary continuous-integration definition. GitHub remains the source repository; Azure Pipelines is connected to it through the Azure Pipelines GitHub App and reports results back to the GitHub commit as a Check.

The build runs on the self-hosted Windows Azure VM agent because the complete solution includes the WPF/WebView2 desktop host and must be validated on Windows.

Current CI flow:

```text
GitHub push / pull request
        |
        v
Azure Pipelines GitHub App
        |
        v
azure-pipelines.yml
        |
        v
Default agent pool -> Windows agent
        |
        +-> VM/agent preflight
        +-> Use .NET SDK from global.json
        +-> restore solution
        +-> build complete solution
        +-> run Domain / Infrastructure / UI tests
        +-> publish TRX test results
```

The push trigger uses `batch: true`, so a burst of commits is coalesced instead of filling a one-agent queue with obsolete builds. PR validation uses `autoCancel: true` for the same reason.

The VM itself is probed at the start of CI with `build/vm-health.ps1`, which reports machine, storage, agent-service, .NET and RDP state without mutating the system.

## Independent VM recovery

Normal CI and machine recovery are deliberately different channels.

`azure-vm-control.yml` is a **manual-only Azure DevOps server/agentless pipeline**. Once its one-time Azure Resource Manager Workload Identity Federation service connection + variable group are configured, it can query/start/restart the VM and use Azure VM Run Command to repair the self-hosted agent without requiring that agent or a Microsoft-hosted runner.

```text
normal:   GitHub -> Azure Pipelines -> EOS Windows agent -> build/test
recovery: Azure DevOps server job -> Azure Resource Manager -> EOS VM
```

That removes the circular failure mode where a stopped VM would otherwise need its own stopped agent to fix it.

See:

- `docs/agentless-azure-vm-control.md`
- `docs/cloud-ci-remote-management.md`

## Why this is preferable for EOS

- Windows-targeted desktop code is built on actual Windows.
- The self-hosted VM can retain heavyweight caches/tooling between runs.
- `global.json` + `UseDotNet@2` still keep the SDK deterministic rather than trusting machine state.
- Azure DevOps keeps build history and test results while GitHub stays the source/review system.
- Azure Resource Manager provides an independent control plane instead of making CI depend on RDP or on the build agent being alive.

## GitHub Actions

`.github/workflows/ci.yml` is retained only as a manual fallback/reference workflow. It is not the authoritative push/PR CI path.

`.github/workflows/release.yml` currently describes the older GitHub-hosted release path for tagged releases. It can be migrated to Azure Pipelines/Blob Storage later if we want the entire build/release chain under Azure.

## Release packaging

`build/release.ps1` remains the canonical local packaging script:

```text
tests -> dotnet publish (win-x64, self-contained, ReadyToRun) -> Velopack pack
```

See `docs/installer.md` for installer/update details.

## Operating principle

Do not make a person manually watch CI as part of normal development. A change is not finished when it is merely pushed; the implementation loop includes reading the resulting CI signal, fixing failures, and rerunning until the relevant checks are green or an external infrastructure blocker is identified and documented.
