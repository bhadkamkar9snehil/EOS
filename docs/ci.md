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

## Why this is preferable for EOS

- Windows-targeted desktop code is built on actual Windows.
- The self-hosted VM can retain heavyweight caches/tooling between runs.
- `global.json` + `UseDotNet@2` still keep the SDK deterministic rather than trusting machine state.
- Azure DevOps keeps build history and test results while GitHub stays the source/review system.
- The same Azure account can provide independent VM recovery through Run Command/Bastion instead of making CI depend on RDP.

The complete reusable setup and remote-management runbook is in:

- `docs/cloud-ci-remote-management.md`

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
