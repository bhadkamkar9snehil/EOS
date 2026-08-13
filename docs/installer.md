# Installer & Auto-Update (Velopack)

Status: **implemented** (see `docs/performance-tech-installer-logging-audit.md` §3 and
`docs/tailwind-grid-ci-plan.md` §3 for the earlier planning/rationale).

## What's in place

- **Package**: `Velopack` referenced in `src/EngineeringPerformance.DesktopHost/EngineeringPerformance.DesktopHost.csproj`.
- **Startup hook**: `Program.Main` calls `VelopackApp.Build().Run()` before constructing WPF,
  per Velopack's integration contract. This intercepts the special
  command-line invocations Velopack uses during install/uninstall/update (e.g. creating shortcuts)
  and exits early when one is detected — it must run before any other app code.
- **Update source**: `VelopackUpdateBackend` uses `GithubSource` against the public EOS GitHub
  Releases repository configured in `UpdateSettings.RepositoryUrl`.
- **Update lifecycle**: `UpdateCheckWorker` checks when the host starts, then roughly hourly with
  jitter while EOS is open. Feed failures back off to two, four, then six hours. Checks never
  download. `IUpdateService` exposes immutable state to the footer, Settings, and Diagnostics.
- **Explicit actions**: an available release remains available until Download is clicked. A
  prepared release survives application restarts through Velopack's `UpdatePendingRestart` marker.
  Restart uses `WaitExitThenApplyUpdates`, then closes EOS normally before files are replaced.
- **Development builds**: non-installed launches report `Unsupported` and do not contact GitHub.

## Building a release

Run `build/release.ps1` from Windows (win-x64 publish + `vpk pack` both require Windows):

```powershell
pwsh build/release.ps1 -Version 1.3.0
```

It runs, in order:
1. `dotnet test` on both test projects — aborts on any failure.
2. `dotnet publish` of `EngineeringPerformance.DesktopHost.csproj` for `win-x64`,
   self-contained, `PublishReadyToRun=true`.
3. `vpk pack` on the publish output, producing a `Setup.exe` installer plus delta update packages
   in `build/Releases/`.

The installer registers **EOS - Engineering Performance Analyzer** in Windows Installed Apps,
creates Start Menu and Desktop shortcuts, and installs per-user without mixing application binaries
under `%LocalAppData%\EngineeringPerformance` with persistent data under `%LocalAppData%\EOS\Data`.

If `-Version` is omitted, the script reads `<Version>` from the DesktopHost `.csproj` instead.

**Prerequisite**: the Velopack CLI must be installed once per machine:

```powershell
dotnet tool install -g vpk
```

For tags, `.github/workflows/release.yml` downloads the preceding Velopack release, packages the
new version, and publishes it with `vpk upload github`. Tag and project versions must match.

## Release verification

Verify each stable release from an installed preceding version: discover, download, close and
reopen before applying, confirm the prepared update is recovered, restart, and verify the new
version in EOS, executable metadata, and Windows Installed Apps. Persistent data under
`%LOCALAPPDATA%\EOS\Data` must remain unchanged. Exercise delta and full-package fallback.
