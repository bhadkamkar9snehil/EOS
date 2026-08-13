# Installer & Auto-Update (Velopack)

Status: **implemented** (see `docs/performance-tech-installer-logging-audit.md` §3 and
`docs/tailwind-grid-ci-plan.md` §3 for the earlier planning/rationale).

## What's in place

- **Package**: `Velopack` referenced in `src/EngineeringPerformance.DesktopHost/EngineeringPerformance.DesktopHost.csproj`.
- **Startup hook**: `App.xaml.cs`'s static constructor calls `VelopackApp.Build().Run()` before
  anything else in the process, per Velopack's WPF integration docs. This intercepts the special
  command-line invocations Velopack uses during install/uninstall/update (e.g. creating shortcuts)
  and exits early when one is detected — it must run before any other app code.
- **Update check**: `App.OnStartup` fires `CheckForUpdatesAsync()` as a non-blocking, fire-and-forget
  task *after* the main window is already shown, so a slow/unreachable feed never delays launch. If
  an update is found it's downloaded, then the user is asked (via `MessageBox`) whether to restart
  into it now.
- **Feed URL**: configured in one place, `src/EngineeringPerformance.DesktopHost/UpdateSettings.cs`
  (`UpdateSettings.FeedUrl`). It currently holds a placeholder (`https://example.invalid/...`) — an
  unreachable/misconfigured feed is caught and swallowed silently (not a real error state, since no
  feed is configured yet). Point it at a real feed before relying on auto-update:
  - a local/network file share, e.g. `\\fileserver\EOS-Releases`, or
  - a GitHub Releases URL, e.g. `https://github.com/your-org/EOS`.

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

After packing, publish the contents of `build/Releases/` to wherever `UpdateSettings.FeedUrl`
points (copy to the file share, or upload as a GitHub Release) so installed copies can discover it.

## What's not verified in this sandbox

This repo's dev sandbox is Linux and `EngineeringPerformance.DesktopHost` targets
`net10.0-windows10.0.19041.0`, so neither `dotnet publish` nor `vpk pack` can run here — both
require Windows. What *was* verified on Linux:
- The `Velopack` package reference resolves (added to the `.csproj`; the win-x64/Windows-target
  restore itself is blocked here by `NETSDK1100`, which is pre-existing/expected, not caused by
  this change).
- `App.xaml.cs` compiles conceptually against Velopack's documented WPF integration API
  (`VelopackApp.Build().Run()`, `UpdateManager`, `CheckForUpdatesAsync`, `DownloadUpdatesAsync`,
  `ApplyUpdatesAndRestart`) — actual compilation needs a Windows build, which was not possible here.
- Both test projects (`Infrastructure.Tests`, `Domain.Tests`) still build and pass after these
  changes, and the non-Windows-targeted projects (`Domain`, `Application`, `UI`) still build clean.

The end-to-end installer/update flow (install via `Setup.exe`, delta-update apply, restart) needs
to be exercised on a Windows machine before relying on it.
