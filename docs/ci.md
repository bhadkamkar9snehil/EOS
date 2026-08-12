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
        +-> launch deterministic desktop visual-capture mode
        +-> capture real WPF-hosted WebView2 PNGs
        +-> capture browser diagnostics + app logs
        +-> publish visual-evidence Pipeline Artifact
        +-> mirror latest evidence to ci-evidence when permitted
```

The push trigger uses `batch: true`, so a burst of commits is coalesced instead of filling a one-agent queue with obsolete builds. PR validation uses `autoCancel: true` for the same reason.

The VM itself is probed at the start of CI with `build/vm-health.ps1`, which reports machine, storage, agent-service, .NET and RDP state without mutating the system.

## Visual validation is part of CI, not a manual afterthought

Tailwind/UI changes are validated against actual screenshots from the compiled desktop product. `scripts/capture-ui.ps1` launches `EngineeringPerformance.DesktopHost.exe` in a dedicated `EOS_VISUAL_CAPTURE=1` mode against deterministic synthetic data. The desktop host captures its real embedded WebView2 surface to PNG and records browser diagnostics.

The baseline covers:

- Overview
- Employee Portrait
- Timesheets
- Peer Insights
- 1536 x 1024
- 1280 x 800
- dark-mode coverage on the densest analytical routes

No real employee/user SQLite data is used for CI screenshots.

See `docs/VISUAL_VALIDATION.md` for the acceptance loop and evidence rules.

## Why this is preferable for EOS

- Windows-targeted desktop code is built on actual Windows.
- The self-hosted VM can retain heavyweight caches/tooling between runs.
- `global.json` + `UseDotNet@2` still keep the SDK deterministic rather than trusting machine state.
- Azure DevOps keeps build history, test results and visual artifacts while GitHub stays the source/review system.
- The same Azure account can provide independent VM recovery through the Azure control plane instead of making CI depend on RDP.
- Visual changes can be inspected from real desktop screenshots without asking a person to manually launch and photograph the app after every iteration.

The reusable cloud-CI setup and remote-management runbook is in:

- `docs/cloud-ci-remote-management.md`

The stronger out-of-band design using an Azure DevOps **agentless server job** and a secretless Azure Resource Manager service connection is documented in:

- `docs/agentless-azure-vm-control.md`

That control path does not need the EOS VM agent to be alive, so it can start/restart the VM or call Azure Run Command when the normal self-hosted CI path is unavailable.

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

Do not make a person manually watch CI as part of normal development. A change is not finished when it is merely pushed; the implementation loop includes reading the resulting CI signal, fixing failures, rerunning until the relevant checks are green, and — for visual work — inspecting the resulting rendered evidence rather than inferring quality from source code.
