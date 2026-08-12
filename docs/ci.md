# CI/CD (GitHub Actions)

This repo is public, so GitHub Actions is free and unbounded here: no minutes cap, standard
hosted runners (Linux, Windows, macOS) all included at no cost. That's a blanket GitHub policy for
public repositories, unrelated to account plan — see `docs/tailwind-grid-ci-plan.md` section 3 for
the earlier non-Actions plan this supersedes (kept there for reference in case the repo ever goes
private and the free-minutes calculus changes).

## `.github/workflows/ci.yml` — runs on every push to `main` and every PR

- **`test` job** (`ubuntu-latest`): restores, builds `Domain`/`Application`/`Infrastructure`/`UI`,
  and runs all three test suites (`Domain.Tests`, `Infrastructure.Tests`, `UI.Tests`). Fast, cheap,
  catches regressions before merge.
- **`windows-build` job** (`windows-latest`): builds `EngineeringPerformance.DesktopHost` (the WPF
  host) for real, on real Windows. This is the one project the development sandbox that built most
  of this codebase could never verify — Linux has no WPF/Windows-target support, and the sandbox
  has no virtualization access to run a Windows VM. This job closes that gap on every push. It only
  builds, doesn't publish/pack — that's reserved for tagged releases (below) so ordinary pushes
  stay fast.

## `.github/workflows/release.yml` — runs on pushing a tag like `v1.2.0`

Runs `build/release.ps1` (test → `dotnet publish` win-x64/self-contained/ReadyToRun → Velopack
`vpk pack`) on `windows-latest`, then attaches the resulting installer + delta update packages to
a new GitHub Release via `softprops/action-gh-release`. This is the actual "download and it
installs" artifact — see `docs/installer.md`.

**To cut a release:**
```
git tag v1.0.0
git push origin v1.0.0
```
That's it — the workflow builds, packs, and publishes the GitHub Release automatically. Release
notes are auto-generated from commits/PRs since the last tag (`generate_release_notes: true`);
edit them after the fact on the Release page if you want something more curated.

## Feed URL

`UpdateSettings.FeedUrl` (`src/EngineeringPerformance.DesktopHost/UpdateSettings.cs`) now points at
`https://github.com/bhadkamkar9snehil/EOS` — Velopack's `UpdateManager` reads GitHub Releases
directly from that URL. Until the first tag is pushed and the release workflow runs, there's
nothing there yet and in-app update checks fail harmlessly (swallowed in
`App.xaml.cs`'s `CheckForUpdatesAsync`).

## What still can't be verified outside Windows

The `windows-latest` GitHub-hosted runner solves the "can't build WPF" problem, but nobody has
actually **run** the installed app yet — first-launch behavior, the Velopack install/update flow
end-to-end, and general UI smoke-testing still need a human on a real Windows machine (or someone
downloading the release asset) at least once before this is fully trusted.
