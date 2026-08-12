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
        +-> capture browser/layout/contrast diagnostics + app logs
        +-> publish visual-evidence Pipeline Artifact
        +-> mirror latest evidence to ci-evidence when permitted
```

The push trigger uses `batch: true`, so a burst of commits is coalesced instead of filling a one-agent queue with obsolete builds. PR validation uses `autoCancel: true` for the same reason.

The VM itself is probed at the start of CI with `build/vm-health.ps1`, which reports machine/Spot metadata, storage, Windows sessions, agent services, .NET/WebView2 state and management ports without mutating the system.

## Visual validation is part of CI

Tailwind/UI work is validated against actual screenshots from the compiled desktop product rather than inferred from CSS or a browser-only mock.

`scripts/capture-ui.ps1` launches `EngineeringPerformance.DesktopHost.exe` with `EOS_VISUAL_CAPTURE=1` against deterministic synthetic data. The desktop host captures its real embedded WebView2 surface to PNG and writes `visual-report.json`.

The baseline covers:

- Overview
- Employee Portrait
- Timesheets
- Peer Insights
- 1536 x 1024
- 1280 x 800
- dark-mode coverage on the densest analytical routes

Evidence includes:

- PNG screenshots
- JavaScript / unhandled-rejection / `console.error` diagnostics
- horizontal overflow and clipped-plate checks
- chart canvas/SVG presence
- visible sub-11px text samples
- likely low-contrast text samples with computed contrast ratios
- resolved core Tailwind design tokens
- isolated capture-mode application logs

No real employee/user SQLite data is used for CI screenshots.

Azure publishes this as the `visual-evidence` Pipeline Artifact. CI also attempts to mirror the latest evidence to the non-CI `ci-evidence` GitHub branch so automated review tooling can inspect the actual PNGs without requiring a person to download artifacts. The Azure artifact remains authoritative if that mirror cannot push.

See `docs/VISUAL_VALIDATION.md` for the acceptance loop and evidence rules.

## Independent VM recovery

Normal CI and machine recovery are deliberately different channels.

`azure-vm-control.yml` is a **manual-only Azure DevOps server/agentless pipeline**. Once its one-time Azure Resource Manager Workload Identity Federation service connection + variable group are configured, it can query/start/restart the VM and use Azure VM Run Command to repair the self-hosted agent without requiring that agent or a Microsoft-hosted runner.

```text
normal:   GitHub -> Azure Pipelines -> EOS Windows agent -> build/test/render
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
- Azure DevOps keeps build history, test results and visual artifacts while GitHub stays the source/review system.
- Azure Resource Manager provides an independent control plane instead of making CI depend on RDP or on the build agent being alive.
- UI changes can be reviewed from repeatable real desktop evidence rather than manual screenshots after every iteration.

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

Do not make a person manually watch CI as part of normal development. A change is not finished when it is merely pushed; the implementation loop includes reading the resulting CI signal, fixing failures, rerunning until the relevant checks are green, and — for visual work — inspecting the rendered evidence rather than inferring quality from source code.
