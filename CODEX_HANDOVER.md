# EOS — Codex Autonomous Development Handover

**Handover date:** 2026-08-13  
**Repository:** `bhadkamkar9snehil/EOS`  
**Primary branch:** `main`  
**Pre-handover code HEAD:** `7054229426a56be65888055459f87cb4e42fa6bc` (`Add repeatable Azure DevOps bootstrap script`)  
**Azure DevOps organization/project:** `https://dev.azure.com/apexasnehil/EOS`  
**Product:** EOS — Windows desktop engineering-performance analytics application (.NET/WPF/BlazorWebView2)

---

## 0. Mission and authority

This file is the operational handover to Codex. Treat it as the starting context for continuing EOS without requiring Snehil to operate pipelines, paste logs, manually inspect Azure, babysit RDP sessions, or perform routine engineering steps.

### Primary mission

Continue developing EOS autonomously, with the immediate priorities:

1. **Finish the Azure/Azure DevOps automation setup** so the Windows VM can be controlled and recovered without manual RDP.
2. **Immediately reconcile and continue PR #12** (`Add real Windows desktop visual validation to Tailwind CI`) on top of current `main` without regressing the newly unified logging system or current CI improvements.
3. **Return to the original product task:** materially improve the Tailwind-based EOS UI using screenshots from the actual WPF + WebView2 application, not CSS-only reasoning.
4. Iterate autonomously: change code -> push -> inspect Azure/GitHub CI -> fix -> rerun -> collect screenshots/logs -> visually inspect -> improve -> repeat.

### User-involvement policy

Do **not** ask Snehil to:

- monitor Azure Pipelines;
- paste build logs that Codex can retrieve through GitHub Checks, Azure DevOps CLI/REST, or artifacts;
- click through Azure/DevOps screens for routine configuration;
- RDP into the EOS VM for ordinary builds, diagnostics, restarts, or agent repair;
- run commands merely because Codex has not yet attempted an API/CLI/Run Command route;
- recreate state that already exists in GitHub/Azure DevOps/Azure.

Exhaust automation first: GitHub APIs, `az`, Azure DevOps CLI, Azure DevOps REST APIs, Azure Resource Manager, Azure VM Run Command, PowerShell Remoting/SSH if reachable, and pipeline/server jobs.

Only involve the user if there is a genuine human consent/authentication boundary that cannot be crossed programmatically. If that happens, request **one exact action** and resume immediately afterward. Do not turn it into a multi-screen tutorial.

Snehil is comfortable with implementation-first work and direct repository actions. Use PRs for large/risky changes; small infrastructure fixes may be committed directly when appropriate. Never leave a change at “pushed”; own the resulting CI outcome.

---

## 1. Product and repository map

EOS is a Windows engineering-performance analysis desktop application.

Key projects:

```text
EngineeringPerformance.slnx
src/
  EngineeringPerformance.Domain/
  EngineeringPerformance.Application/
  EngineeringPerformance.Infrastructure/
  EngineeringPerformance.UI/
  EngineeringPerformance.DesktopHost/
tests/
  EngineeringPerformance.Domain.Tests/
  EngineeringPerformance.Infrastructure.Tests/
  EngineeringPerformance.UI.Tests/
```

The Windows host is WPF with embedded Blazor/WebView2. Full product validation therefore belongs on Windows.

The .NET SDK is pinned through:

```json
{"sdk":{"version":"10.0.203","rollForward":"latestPatch"}}
```

Do not assume the VM’s global SDK state; normal CI uses `UseDotNet@2` against `global.json`.

---

## 2. Non-negotiable UI / product-design direction

The Tailwind/UI work is the original reason the Windows visual-lab effort exists. Do not lose this thread to infrastructure work.

Snehil explicitly does **not** want a generic SaaS dashboard. EOS should feel like a dense, precise analytical instrument.

### Visual language

The authoritative source is:

`src/EngineeringPerformance.UI/wwwroot/tailwind-input.css`

Its own header defines the intended system:

- graphite chassis;
- ivory mounted plates;
- recessed chart/gauge wells;
- fixed upper-left light source;
- believable physical depth rather than arbitrary shadows;
- tight radii;
- technical/display typography (Bahnschrift where appropriate, Segoe UI for general text);
- tabular/numeric instrument-like presentation;
- orange used sparingly as selection / primary action / one analytical emphasis;
- semantic red/amber/green/orange only for real semantic meaning;
- cool categorical chart palette;
- information-dense data UX, not oversized cards and whitespace;
- charts and annotations treated as analytical instruments, not decoration.

### CSS architecture rule

`tailwind-input.css` is the **single source of truth**.

Do not create another `ui-polish.css`, override layer, theme patch file, or parallel styling system. A previous attempt to add `ui-polish.css` was rejected and then correctly consolidated back into `tailwind-input.css`.

The generated file is `wwwroot/tailwind.css`; do not manually edit generated output.

Chart JS reads CSS custom properties with `getComputedStyle`, so CSS tokens and chart colors must remain one system.

### Existing polish already consolidated

The source now contains stronger readability/contrast/focus/table/material rules, including:

- derived muted/chassis-muted colors;
- stronger semantic text colors;
- minimum small-text readability improvements;
- typography/kerning/font-synthesis refinements;
- tabular numerics;
- stronger material rims and well rims;
- field hover/focus/caret/placeholder states;
- stronger table separators and selection rail;
- `:focus-visible` treatment;
- scrollbar styling;
- reduced-motion handling.

Do not undo those changes merely to make PR #12 easier to merge.

### UI acceptance criteria

A visual change is not accepted because it compiles or because the CSS looks plausible.

It must be rendered through the **actual WPF + WebView2 host**, captured to screenshots, and visually inspected. Inspect at minimum:

- hierarchy between chassis / plate / recess / control;
- readability and contrast;
- typography size and density;
- alignment and spacing;
- chart legibility / annotations / legends;
- clipping and horizontal overflow;
- dark/light theme behavior;
- generic-SaaS artifacts such as gratuitous pills, giant cards, excessive whitespace, soft rounded everything;
- desktop viewport behavior at the required resolutions.

---

## 3. Current logging architecture — do not regress

Logging was recently identified as a Frankenstein combination and fully refactored in PR #14.

**Merged PR:** #14 — `Unify EOS logging and diagnostics architecture`  
**Merge commit:** `d04625813cac3136aac14207bdfcb25a4096c65d`

Read `docs/logging.md` before touching logging-related code.

### Current rule

Application-facing logging API everywhere:

```csharp
Microsoft.Extensions.Logging.ILogger<T>
```

Serilog is **only a backend/provider implementation** at the DesktopHost composition root, configured in:

`src/EngineeringPerformance.DesktopHost/EosLogging.cs`

The host explicitly clears default logging providers and installs Serilog as the sole provider, so there are not duplicate Console/Debug/EventSource/Serilog pipelines.

### One log family

```text
%LOCALAPPDATA%\EngineeringPerformance\logs\eos-YYYYMMDD.log
```

Policy:

- daily rolling;
- 30 retained files;
- 25 MB per-file limit with rollover;
- shared read access;
- structured SourceContext;
- Debug sink + rolling file sink.

### Removed system

The hand-written `InteractionLog` / `interaction.log` pipeline is retired and must not return.

Old `interaction.log` files may be included in diagnostics bundles as historical evidence, but nothing writes to that file anymore.

### Diagnostics boundary

Canonical application paths are represented by `LocalApplicationPaths`.

The UI uses `IApplicationDiagnostics`; infrastructure owns log discovery, safe tailing, metadata, and bundle generation. Razor components must not hard-code Serilog filenames or implement ZIP/log filesystem behavior.

### Process failure coverage

`App.xaml.cs` owns process-level boundaries (dispatcher exceptions, AppDomain unhandled exceptions, unobserved task exceptions, startup failures) but those events still flow through `ILogger<App>`.

**PR #12 predates this refactor and edits `App.xaml.cs`. Never resolve its conflict by restoring the old Serilog-direct App implementation. Port the visual-capture behavior into the new architecture instead.**

---

## 4. CI architecture — authoritative path

### Authoritative CI

`azure-pipelines.yml`

GitHub is the source/review system. Azure Pipelines is the authoritative push/PR build system and reports results back to GitHub Checks through the Azure Pipelines GitHub App.

Current pipeline:

```text
GitHub push / PR
  -> Azure Pipelines GitHub App
  -> azure-pipelines.yml
  -> pool: Default
  -> demand: Agent.Name == EOS
  -> build/vm-health.ps1
  -> UseDotNet@2 / global.json
  -> dotnet restore EngineeringPerformance.slnx
  -> dotnet build complete solution
  -> Domain tests
  -> Infrastructure tests
  -> UI tests
  -> PublishTestResults
```

Push builds use `batch: true`; PR builds use `autoCancel: true` to keep a single-agent queue from accumulating obsolete runs.

### CI observability improvement already merged

The build step tees the full output and re-emits compiler/MSBuild/NuGet errors as Azure logging issues. Those become GitHub Check annotations.

This was added specifically so Codex can retrieve a real compile error from GitHub instead of asking a person to open Azure DevOps and paste logs.

Use this path first on failures:

1. obtain the commit SHA;
2. inspect GitHub check-runs;
3. inspect the Azure Pipelines check annotations;
4. fix the actual diagnostic;
5. push and monitor the new run.

If information is still insufficient, use Azure DevOps CLI/REST to fetch the run/job logs directly.

### Recent validated state

The unified logging change successfully built on the Windows VM with:

- 0 errors;
- 0 warnings;
- 50/50 tests passed.

The Azure DevOps UI also showed the subsequent main rolling build (`#20260813.9`, merge commit `d046258...`) green with 100% tests passed.

Two small infrastructure commits followed (`7863d2a...`, `7054229...`). Verify the current main-head run before assuming their CI state.

### GitHub Actions

`.github/workflows/ci.yml` is **manual fallback only** (`workflow_dispatch`). It is intentionally not automatic.

Azure is authoritative. Do not confuse historical GitHub Actions billing/account failures with EOS build failures.

`.github/workflows/release.yml` still represents an older GitHub-hosted release route and can be migrated later; this is not the immediate priority.

---

## 5. Azure Windows VM / self-hosted agent inventory

### Azure VM

Known configuration from setup:

```text
VM name:             EOS
OS:                  Windows Server 2025 Datacenter, x64 Gen 2
Region:              South Central India
Zone:                1
Size:                Standard_DS2_v2 (2 vCPU, 7 GiB)
Priority:            Spot
Eviction policy:     Stop / Deallocate
Security:            Trusted launch, Secure Boot, vTPM
OS disk:             Standard SSD managed disk
Subscription:        Azure subscription 1
Subscription ID:     69f506f7-b17d-404d-b814-b03d9b1a0d0d
Resource group:      VisualStudioOnline-FA4B8E34518844659C2CAABE9BF09BB8
VNet:                vnet-indiasouthcentral-1
Subnet:              snet-indiasouthcentral-1
Public IP resource:  EOS-ip
NIC:                 eos387
NSG:                 EOS-nsg
Historically seen public IP: 172.198.136.13
```

Do not rely on the historical public IP without querying Azure; verify with `az vm list -d` / NIC/PIP APIs.

The VM is Spot. It can be evicted or deallocated. A queued pipeline may simply mean the agent/VM is offline.

### Inbound management state

The user intentionally left these available for future VM use:

- 22 SSH;
- 3389 RDP;
- 443 HTTPS.

OpenSSH Server was installed and `sshd` was configured to start automatically. PowerShell was set as the OpenSSH default shell. Port 22 was confirmed listening.

Azure `EnableRemotePS` was also run successfully, enabling WinRM HTTPS.

RDP was tested successfully from the Windows/iOS client.

Do not assume the ChatGPT sandbox’s inability to open arbitrary outbound TCP applies to Codex. **Test direct SSH/WinRM from the Codex runtime first.** If it works, it is a useful maintenance channel. If it does not, use Azure Resource Manager / VM Run Command rather than asking Snehil to RDP.

### Azure Pipelines agent

```text
Agent version at install: 5.277.0
Agent root:               C:\agents\EOS
Pool:                     Default
Agent name:               EOS
Work folder:              _work
Installed as service:     yes
Service account:          NT AUTHORITY\NETWORK SERVICE
Service name:             vstsagent.apexasnehil.Default.EOS
Startup:                  automatic / delayed auto start configured
```

The service was successfully registered, installed, and started.

Important historical note: the device-code (`AAD`) agent configuration path failed because Azure DevOps reported the organization was not backed by Azure Active Directory/Entra at that time. PAT registration was used once to bootstrap the agent. Do **not** reuse or expose any old PAT from prior chat history; treat it as compromised historical material. Prefer Entra/WIF for all new automation.

### Historical agent install command shape

The Windows x64 agent was downloaded/verified into `C:\agents\EOS`, then configured from an elevated PowerShell session:

```powershell
cd C:\agents\EOS
.\config.cmd
```

Configuration used:

```text
server:       https://dev.azure.com/apexasnehil
pool:         Default
agent name:   EOS
work folder:  _work
run service:  yes
service user: NT AUTHORITY\NETWORK SERVICE
```

Do not repeat manual registration unless the existing agent is actually lost.

---

## 6. Azure DevOps organization/project state

```text
Organization: apexasnehil
Project:      EOS
URL:          https://dev.azure.com/apexasnehil/EOS
GitHub repo:  bhadkamkar9snehil/EOS
```

The GitHub App connection exists and normal Azure CI proves that repository checkout/webhook integration works.

At handover time the Azure DevOps Library showed **no variable groups**, which means the independent VM-control bootstrap described below has **not yet been confirmed complete**.

The user currently has access to the Azure Portal / Azure DevOps Portal from their laptop and successfully opened Azure Cloud Shell. Do not rely on them to operate it; use the bootstrap script / CLI yourself whenever the runtime has the appropriate Azure authentication context.

---

## 7. Independent Azure VM control plane — unfinished immediate setup

Normal CI works without this. This control plane is for recovery/autonomy when the VM is stopped, Spot-evicted, or the self-hosted agent service is unhealthy.

### Why it exists

Avoid this circular failure:

```text
VM/agent is offline -> CI cannot run -> CI is needed to repair VM/agent
```

Desired architecture:

```text
Normal:
GitHub -> Azure Pipelines -> EOS Windows self-hosted agent -> build/test/render

Recovery:
Azure DevOps agentless server job -> Azure Resource Manager -> EOS VM
```

The recovery route does **not** depend on the EOS VM agent or on Microsoft-hosted build agents.

### Existing files

- `azure-vm-control.yml`
- `scripts/bootstrap-azure-devops.ps1`
- `docs/agentless-azure-vm-control.md`
- `docs/cloud-ci-remote-management.md`

### `azure-vm-control.yml`

Manual-only (`trigger: none`, `pr: none`), `pool: server`.

Operations:

- `status`
- `start`
- `restart`
- `health`
- `repair-agent`
- `enable-rdp`
- `disable-rdp`

The service connection name is now statically specified in YAML as `eos-vm-arm`. This is intentional: service connections are protected resources resolved before variable-group runtime values, so the connection name must not be hidden behind the variable group.

The variable group should contain only non-secret identifiers:

```text
AZURE_SUBSCRIPTION_ID
AZURE_RESOURCE_GROUP
AZURE_VM_NAME
```

### Bootstrap script

`scripts/bootstrap-azure-devops.ps1` is intended to be idempotent and safe to rerun.

Canonical Cloud Shell invocation:

```powershell
irm https://raw.githubusercontent.com/bhadkamkar9snehil/EOS/main/scripts/bootstrap-azure-devops.ps1 | iex
```

The script attempts to:

1. select/discover the Azure subscription and EOS VM;
2. discover tenant ID and real resource group automatically;
3. install/update the `azure-devops` CLI extension;
4. authenticate Azure DevOps using the current `az login` session;
5. create/reuse a user-assigned managed identity named `eos-devops-control`;
6. create/reuse ARM service connection `eos-vm-arm` using Workload Identity Federation;
7. create the federated credential `azure-devops-eos-vm-control`;
8. grant `Virtual Machine Contributor` at the EOS resource-group scope only;
9. create/update variable group `eos-vm-control`;
10. create/reuse the manual `EOS VM Control` pipeline from `azure-vm-control.yml`;
11. authorize the pipeline to use the protected ARM service connection;
12. queue a `status` operation as a smoke test.

No client secret should be created or persisted.

### Most likely remaining blocker

Earlier Azure DevOps reported that the organization was not backed by Azure Active Directory/Entra. The bootstrap script probes this. If Azure itself is authenticated but:

```text
az devops project show ...
```

fails due organization/tenant identity, the organization may still need to be connected to the Azure subscription’s Entra directory.

Codex should **first attempt to automate or API-drive this**. Investigate supported Azure DevOps organization/Entra connection APIs/CLI before involving the user.

If it is genuinely impossible without owner consent/UI, that is one of the rare acceptable escalation boundaries. Request only the exact directory-connect consent, then rerun the same idempotent script.

Do not fall back to PATs casually. The preferred end state is Entra authentication + workload identity federation.

### Direct Azure fallback if the DevOps control pipeline is not ready

If Codex has an authenticated Azure CLI context, it does not need the VM-control pipeline just to recover the machine. Use ARM directly:

```powershell
az vm list -d --query "[?name=='EOS'].{name:name,rg:resourceGroup,power:powerState,ip:publicIps}" -o table
az vm start -g <resource-group> -n EOS
```

For guest repair, use Azure VM Run Command, e.g. a PowerShell script that finds `vstsagent*`, sets startup automatic, and restarts it. This route works through Azure Resource Manager + Azure VM Agent and does not require RDP/SSH/WinRM ingress.

---

## 8. PR #12 — immediate next code task after DevOps bootstrap

**PR:** #12  
**Title:** `Add real Windows desktop visual validation to Tailwind CI`  
**State:** open, draft  
**Branch:** `agent/tailwind-visual-validation`  
**Head:** `5ee62f1ed4aa28ca68d9e6f4435d1ac939b0744e`  
**Current status:** not mergeable against present `main`; it predates the logging refactor and later CI changes.

Changed files currently include:

```text
azure-pipelines.yml
build/vm-health.ps1
docs/VISUAL_VALIDATION.md
docs/ci.md
scripts/capture-ui.ps1
src/EngineeringPerformance.DesktopHost/App.xaml.cs
src/EngineeringPerformance.DesktopHost/MainWindow.xaml
src/EngineeringPerformance.DesktopHost/MainWindow.xaml.cs
src/EngineeringPerformance.DesktopHost/VisualCaptureApplicationDatabase.cs
```

### Intended capability

PR #12 turns the Windows build VM into a real visual-validation lab.

It is designed to:

- run deterministic synthetic data (never the user’s normal SQLite data);
- launch the actual WPF desktop host;
- render the embedded Blazor/WebView2 surface;
- capture the real WebView2 surface to PNG;
- cover Overview, Employee Portrait, Timesheets, Peer Insights;
- capture 1536x1024 and 1280x800;
- include dark-mode coverage on dense analytical routes;
- record JavaScript errors/unhandled rejections/`console.error`;
- detect horizontal overflow and clipped plates;
- count chart canvas/SVG presence;
- report sub-11px visible text;
- report likely low-contrast text and computed contrast ratios;
- record resolved design tokens;
- publish app logs and visual diagnostics with the screenshots;
- publish a `visual-evidence` Azure Pipeline Artifact;
- mirror latest evidence to the existing non-CI `ci-evidence` GitHub branch when checkout credentials permit.

The `ci-evidence` branch exists.

### Correct reconciliation strategy

Do **not** merge PR #12 by accepting “theirs” wholesale on conflicted files.

Preferred approach:

1. start from current `main`;
2. inspect each PR #12 patch;
3. port the visual-capture behavior onto current architecture file-by-file;
4. preserve all current logging and current CI build-annotation logic;
5. preserve the single Tailwind source architecture;
6. run Windows CI;
7. keep the PR draft until real screenshots exist and have been inspected.

A fresh replacement branch from current `main` is acceptable if rebasing the old 13-commit branch is noisier than reapplying the intended deltas. If creating a replacement PR, close/supersede #12 explicitly so history is clear.

### Critical App.xaml.cs conflict

PR #12 was written when `App.xaml.cs` directly held Serilog state (`Serilog.ILogger`, `Log.CloseAndFlush`, manual data/log path setup). Current main no longer uses that architecture.

Port only the useful visual-capture concepts:

- `EOS_VISUAL_CAPTURE=1`;
- `EOS_VISUAL_OUTPUT`;
- isolated capture data directory;
- synthetic `IApplicationDatabase` registration;
- non-interactive startup failure reporting to evidence files;
- automatic capture + process exit code;
- suppress modal dialogs during capture mode.

Implement those concepts using current:

- `LocalApplicationPaths`;
- `EosLogging`;
- `ILogger<App>`;
- current DI/composition root.

Never restore direct Serilog usage outside `EosLogging.cs`.

### Critical azure-pipelines.yml conflict

Current main already has improved build diagnostics that surface compiler errors to GitHub Checks.

PR #12 adds:

- visual evidence directory;
- `persistCredentials: true`;
- capture step;
- `PublishPipelineArtifact@1`;
- optional GitHub evidence mirror;
- longer timeout.

Merge those **into** the current pipeline; do not replace the current error-annotation build step with the older simple build command.

### Visual-session risk

The Azure Pipelines agent runs as a Windows service under `NETWORK SERVICE`, likely in Session 0. WPF/WebView2 visual capture may or may not initialize/render successfully there.

Test it rather than assuming.

If capture fails because no interactive desktop/session is available:

1. retain the service agent for normal build/test;
2. establish a dedicated interactive visual-automation mechanism on the same VM or a separate Windows VM;
3. favor an autonomous low-privilege visual-test account and a persistent interactive session over requiring Snehil to RDP manually;
4. consider a second Azure Pipelines agent that runs interactively for visual work, or a scheduled/bootstrapped UI-test runner launched in a real desktop session;
5. keep credentials isolated and do not embed a user password in the repo;
6. use Azure Run Command / VM automation to repair/start the visual runner.

Do not abandon real desktop validation and fall back to a browser-only mock just because Session 0 is inconvenient. The whole point is to validate the shipped WPF/WebView2 surface.

---

## 9. Visual evidence retrieval — Codex must inspect images itself

Publishing screenshots is not enough. Codex must download/open the evidence and reason about the actual images.

Preferred evidence sources, in order:

1. Azure Pipeline Artifact `visual-evidence` for the exact build;
2. mirrored `ci-evidence/visual-evidence/latest` files in GitHub;
3. direct capture output from the VM when debugging.

Use Azure DevOps CLI/REST to enumerate builds and download artifacts. If a specific CLI subcommand differs in the installed extension version, inspect `az pipelines -h` / REST API rather than asking the user to download the artifact.

For each Tailwind iteration, inspect:

- all PNGs, not just one overview image;
- `visual-report.json`;
- capture app logs;
- any `startup-failure.txt` / `capture-failure.txt`;
- CI VM/session diagnostics.

Then make targeted changes to `tailwind-input.css`, Razor structure, or chart source based on evidence.

---

## 10. Tailwind build system details

`EngineeringPerformance.UI.csproj` compiles Tailwind using the standalone Windows CLI rather than introducing a Node/npm build toolchain.

Key paths:

```text
Tailwind input:  src/EngineeringPerformance.UI/wwwroot/tailwind-input.css
Tailwind output: src/EngineeringPerformance.UI/wwwroot/tailwind.css
CLI cache:       .tailwind/tailwindcss.exe
```

The build fetches the Tailwind standalone CLI if absent and compiles/minifies before Build/Publish.

There is an advisory JavaScript syntax check using `node --check` only when Node exists. Node is not a required EOS build dependency.

Potential future hardening: the current Tailwind CLI URL uses GitHub `releases/latest`. If reproducibility becomes important, pin a known version + checksum. Do not let that distract from the immediate visual-polish task unless it causes actual CI variance.

---

## 11. Known historical failure modes / lessons

### Obsolete Windows SDK copy target

A previous full Windows build produced the DesktopHost DLL and then failed in a custom post-build copy target trying to copy:

```text
Microsoft.Windows.SDK.NET.Ref 10.0.17763.10
```

from a hard-coded NuGet cache path that did not exist under `NetworkService`.

That brittle target was removed. Do not reintroduce path assumptions about a particular user’s NuGet cache.

### Node warning

A previous build emitted a warning because `where node` exited 1. The JavaScript syntax check was made advisory. EOS must build cleanly without Node.

### GitHub Actions billing issue

Historical automatic GitHub Actions jobs failed immediately because the GitHub account was locked for a billing issue. That was infrastructure, not EOS code. GitHub Actions is now manual fallback only.

### Azure Microsoft-hosted agents unavailable

The Azure DevOps project did not have Microsoft-hosted agent entitlement. The user explicitly does **not** want pay-as-you-go suggested as the solution. The existing Azure Windows VM is intentionally used as the self-hosted agent.

### Do not create parallel styling/logging architectures

Two explicit architectural mistakes were corrected:

- `ui-polish.css` override layer -> deleted; all styling returned to `tailwind-input.css`;
- `InteractionLog` + Serilog/MEL split -> removed; one `ILogger<T>` pipeline.

Treat these as precedents: fix the source architecture, do not bolt on shortcut layers.

---

## 12. Security rules

Never print, commit, echo, or document:

- VM passwords;
- PATs;
- Azure access tokens;
- GitHub tokens;
- cookies/session material;
- employee dataset contents.

A full-access Azure DevOps PAT was previously shared during initial agent setup. Do not reuse it from chat history. It should be treated as exposed and revoked if it has not already been revoked.

The VM password was also shared historically. It is not included here and must not be copied into code/docs.

Preferred auth architecture:

- Azure CLI / Entra interactive identity for operator automation;
- Workload Identity Federation for Azure DevOps -> Azure Resource Manager;
- GitHub App connection for Azure Pipelines -> GitHub;
- no long-lived secrets in YAML;
- least-privilege Azure role scoped to the EOS resource group where possible.

EOS processes employee/workbook evidence. Logs and synthetic screenshot data must not become a second store of user data. Never use real employee data merely to make CI screenshots.

---

## 13. Cost / Azure constraints

The Azure subscription currently has trial credit. The user explicitly rejected suggestions to “upgrade to pay-as-you-go” as the normal solution.

Use the existing resources efficiently.

Because the VM is Spot:

- expect possible eviction/deallocation;
- avoid designing pipelines that silently require the VM to be permanently running;
- use independent control/recovery automation;
- monitor storage/cache growth on the long-lived self-hosted agent;
- consider start/stop scheduling only after it does not impede autonomous CI.

Do not optimize cost by breaking the development loop.

---

## 14. General cloud-CI recipe learned from this setup

For recreating the approach on another Windows desktop repo:

1. Keep GitHub as source control.
2. Create Azure DevOps organization/project.
3. Connect the GitHub repo through the Azure Pipelines GitHub App.
4. Create a Windows Azure VM appropriate for the target desktop stack.
5. Install Azure Pipelines Windows x64 agent on the VM.
6. Register it in a self-hosted agent pool and install it as an automatic Windows service.
7. Point YAML to that pool/agent using explicit demands when a dedicated machine is intended.
8. Keep SDK/toolchain versions in repo (`global.json`, pinned tools) rather than trusting machine state.
9. Build the complete Windows solution and publish test results.
10. Make CI diagnostics programmatically retrievable (GitHub Check annotations / Azure REST).
11. Separate normal CI from machine recovery.
12. Create an Azure Resource Manager WIF service connection with a managed identity.
13. Give it minimum required RBAC at VM/resource-group scope.
14. Use an Azure DevOps `pool: server` agentless pipeline for VM start/restart/status/Run Command repair.
15. Add real rendered UI evidence for desktop UI work.
16. Store/publish screenshots and machine-readable visual diagnostics so an autonomous engineering agent can inspect them.
17. Never require a human to manually monitor the build as part of the normal development loop.

---

## 15. Recommended autonomous execution sequence from this handover

### P0 — establish the actual current state

1. Pull current `main`.
2. Read this file, `docs/ci.md`, `docs/logging.md`, `docs/VISUAL_VALIDATION.md`, `azure-pipelines.yml`, `azure-vm-control.yml` and `scripts/bootstrap-azure-devops.ps1`.
3. Inspect the GitHub Check on current main HEAD.
4. Inspect current Azure VM power state and agent status through Azure CLI/API if credentials are available.
5. Inspect Azure DevOps service connections, variable groups and pipelines via CLI/REST; do not trust old screenshots over live state.

### P1 — finish DevOps automation

1. Run/fix `scripts/bootstrap-azure-devops.ps1`.
2. Make it fully idempotent.
3. Verify `eos-devops-control` managed identity exists.
4. Verify `eos-vm-arm` is WIF, not a secret-based connection.
5. Verify RBAC is resource-group scoped and sufficient for VM operations/Run Command.
6. Verify `eos-vm-control` variable group exists with the three non-secret identifiers.
7. Verify `EOS VM Control` pipeline exists and can run `status` without the EOS agent.
8. Run `status`.
9. Run `health`.
10. If safe, test `repair-agent` and verify the self-hosted agent reconnects.
11. Document any changes in existing Azure/CI docs rather than creating ad-hoc setup notes.

### P2 — reconcile PR #12

1. Rebase/port PR #12 onto current main.
2. Preserve unified logging.
3. Preserve current GitHub Check compiler-annotation logic.
4. Preserve current Tailwind source architecture.
5. Build/test on the Windows VM.
6. Fix every CI error autonomously.

### P3 — make real screenshots happen

1. Execute `capture-ui.ps1` through Azure CI.
2. Determine whether WPF/WebView2 capture works under the service session.
3. If it fails, solve the interactive-session problem with a dedicated automated visual runner, not manual RDP.
4. Publish `visual-evidence`.
5. Ensure Codex can retrieve it directly through Azure DevOps API/CLI and/or `ci-evidence`.
6. Open and inspect the PNGs.

### P4 — resume Tailwind polish

Use the screenshots/reports to make evidence-driven changes to `tailwind-input.css` and related UI/chart markup.

Prioritize:

- contrast/readability;
- information density;
- plate/chassis/recess material hierarchy;
- chart readability/data storytelling;
- removal of unnecessary pill/capsule/generic-SaaS patterns;
- consistent spacing/alignment;
- dark/light equivalence;
- target viewport fit;
- professional desktop instrument feel.

Run the visual loop repeatedly until the images are genuinely good.

### P5 — merge and leave the system better

Do not merge visual work until:

- full Windows build is green;
- all tests pass;
- screenshot capture completes;
- no blocking JS/render failures exist;
- screenshots have been visually inspected;
- important contrast/overflow/readability findings are resolved or explicitly justified;
- PR #12 (or its superseding PR) no longer conflicts with main;
- logging architecture remains singular;
- no second CSS architecture exists.

---

## 16. Operating style Snehil expects

- Be precise and implementation-oriented.
- Prefer doing the work over discussing hypothetical options indefinitely.
- Do not patronize.
- Do not repeatedly ask for screenshots/logs if APIs can retrieve them.
- Give direct URLs/commands when a human action is truly unavoidable.
- Do not send the user down pay-as-you-go or unnecessary setup paths.
- Treat the Azure VM investment as useful infrastructure and exploit it intelligently.
- Use the VM not only to compile, but as a Windows integration/visual-test lab.
- Maintain clean architecture: one source of truth, one logging abstraction, one CI authority.
- Use realistic data UX and professional engineering aesthetics, not fashionable generic dashboard patterns.

---

## 17. Final “do not forget” list

- **Original goal is Tailwind UI polish.** Infrastructure exists to make that loop autonomous; it is not the product goal.
- **PR #12 is next after the DevOps bootstrap.** It is currently stale/conflicted and must be reconciled carefully.
- **Actual rendered screenshots are mandatory** for visual acceptance.
- **Codex must inspect the screenshots itself.** Do not ask Snehil to visually QA routine iterations.
- **`tailwind-input.css` is the styling source of truth.** No override CSS shortcut.
- **`ILogger<T>` is the application logging API.** Serilog stays in `EosLogging.cs`; no `InteractionLog` resurrection.
- **Azure Pipelines is authoritative CI.** GitHub Actions is manual fallback.
- **Build failures must be inspected programmatically.** GitHub Check annotations exist for this reason.
- **The Windows VM is Spot.** Handle offline/eviction autonomously.
- **Do not expose secrets.** Prefer Entra/WIF and resource-scoped RBAC.
- **Do not involve Snehil in normal pipeline operation.** Own the full engineering feedback loop.
