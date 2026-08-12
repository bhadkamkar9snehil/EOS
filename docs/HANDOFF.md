# EOS — Handoff Document

Status as of: 2026-08-12. Written to hand this project off cleanly — to yourself picking it back
up later, to another person, or to another AI session with no prior context.

---

## 1. What EOS is

**EOS (Engineering Performance Analyzer)** — a local-only Windows desktop tool for engineering
managers/team leads. Ingests ERP evidence exports (timesheets, attendance, approvals) and
peer-review Excel workbooks, computes an "operational score" per employee/team, and produces
dashboards, peer-collaboration analytics, and Excel report/template exports. Single-user,
single-tenant, no server, no multi-tenant auth, no cloud dependency for its core function.

**Stack**: .NET 10, Blazor UI (`EngineeringPerformance.UI`) hosted in a WPF `BlazorWebView`
desktop app (`EngineeringPerformance.DesktopHost`), EF Core + SQLite (`EngineeringPerformance.Infrastructure`),
ClosedXML for Excel I/O. Repo: `github.com/bhadkamkar9snehil/EOS` (public).

---

## 2. Where things stand right now

**`main` has everything merged and is the source of truth.** Two parallel workstreams landed on
it this session:
1. A large modernization pass (performance fixes, installer, logging, backend hardening, 5 new
   features, CI/CD) — done via 5 parallel background agents, each reviewed/merged individually.
2. Your local visual-design work (Codex session + local Claude Code session from the day before)
   — Atlas-only theme (Realist theme fully removed), peer relationship explorer, an "execution
   discipline" feature, and real bug fixes (a peer-review month-bucketing bug). Merged after a
   substantial manual conflict resolution since it had diverged from `main` before any of #1
   landed.

**Verified**: all three .NET test suites pass (Domain, Infrastructure, UI.Tests — exact current
counts may have grown since; run them to check), and the app has been run locally on Windows via
`dotnet run` and clicked through — multiple screenshots confirmed correct rendering after two
follow-up CSS bugs (found post-merge, not caught by any test) were fixed.

**Not yet done**: a real installer has not been successfully built. See §5.

---

## 3. What shipped, by area

### Performance (docs/performance-tech-installer-logging-audit.md, docs/tailwind-grid-ci-plan.md)
- `AppState.RefreshAsync` parallelized (was 7 sequential DB calls, now `Task.WhenAll`) — later
  superseded again by the visual-lab merge, which added version-stamped concurrency guarding on
  top and defers 2 of 8 loads to not block initial paint. Current version is the better one.
- N+1 import queries fixed (dictionary preloads instead of per-row `SingleOrDefaultAsync`).
- Non-sargable `Year*100+Month` history query fixed to a sargable range filter.
- `PublishReadyToRun=true` added to the desktop publish profile.
- `PeopleWorkspace.razor`'s `Virtualize` no longer allocates a new list every render.
- ECharts (1.1MB) lazy-loaded per chart-rendering route instead of loaded on every page
  (`echarts-loader.js`), verified this survived the visual-lab CSS/JS rewrite intact.
- CSS `<link>` audit: no dead links, no orphaned files (at time of audit).

### Tech evaluations (docs/tailwind-grid-ci-plan.md)
- **Tailwind CSS**: evaluated, recommended, **not implemented** — deliberately deferred until
  after visual-design work landed, to avoid fighting the same files twice. That landing has now
  happened (visual-lab is merged), so Tailwind adoption is unblocked if still wanted.
- **Grid component**: evaluated QuickGrid vs MudBlazor/Radzen/Syncfusion/Telerik — kept QuickGrid,
  no replacement justified.
- **EFCore.BulkExtensions**: tried, found a real bug (throws `UNIQUE constraint failed` on SQLite
  for mixed insert+update batches), reverted in favor of the existing dictionary-preload approach.
  Don't re-attempt this without solving that SQLite-specific issue first.
- **FluentValidation**: added, backs structured import-skip tracking (`ImportSkipReason`).

### Installer (docs/installer.md)
- **Velopack** integrated: `VelopackApp.Build().Run()` wired into `App.xaml.cs` (must run first,
  before any other app code), non-blocking startup update-check with a restart-prompt dialog.
- `UpdateSettings.FeedUrl` (`src/EngineeringPerformance.DesktopHost/UpdateSettings.cs`) points at
  `https://github.com/bhadkamkar9snehil/EOS` (GitHub Releases as the update source).
- `build/release.ps1`: test → `dotnet publish` (win-x64, self-contained, ReadyToRun) →
  `vpk pack`. **This has never successfully completed** — see §5, this is the main open item.

### Logging (docs/performance-tech-installer-logging-audit.md §4)
- Serilog wired through `Microsoft.Extensions.Logging`, rolling daily file sink to
  `%LocalAppData%\EngineeringPerformance\logs\eos-.log`, 30-day retention.
- All three exception catch-points now covered: `DispatcherUnhandledException` (existing, now
  logs via Serilog instead of raw file writes), plus the two that were previously missing —
  `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`.
- Structured logging at import/scoring boundaries (row counts, skip reasons, weight changes).
- New in-app `/diagnostics` page: app version, DB path/size, log directory, tails today's log,
  "copy diagnostics bundle" button (zips recent logs + info.txt).

### Backend modernization
- QuickGrid audit: confirmed no further pages need conversion.
- New `tests/EngineeringPerformance.UI.Tests` project (bUnit), 8 component tests.

### Features (all except dark mode, per explicit instruction)
- **Backup/restore**: `/backup` page, one-click DB export to a timestamped zip, restore with a
  safety backup taken first. Found and fixed a real bug during implementation: SQLite connection
  pooling needed `ClearAllPools()` after restore or stale connections served pre-restore data.
- **Scoring presets**: named, saved weight-configuration presets on the Scoring page, ships two
  built-ins ("Individual Contributor," "Team Lead").
- **Import preview**: diffs a workbook against the DB (in a discarded `DbContext`) before commit,
  shows rows added/updated/unchanged, requires confirmation.
- **Trend alerts**: score-drop/missing-evidence detection surfaced on Overview.
- **Team comparison**: new `/team-comparison` page, per-team scorecards.
- **Not implemented** (explicitly deferred with design notes, not built): scheduled report
  generation, peer-review email reminders (no email channel exists in the app), multi-workspace
  support (would need real architecture work — DI/file-layout/nav changes).

### CI/CD — currently non-functional, see §5
- `.github/workflows/ci.yml` and `.github/workflows/release.yml` exist and are syntactically
  valid, but **every run has failed instantly** (2-3 seconds, no runner ever assigned) since they
  were added, on both `ubuntu-latest` and `windows-latest`. This points to an account/repo-level
  Actions restriction (permissions or billing), not a code problem — never diagnosed further
  because the decision was made to abandon GitHub Actions and build the installer locally instead.

### Visual-lab merge (your local work, Aug 11 session)
- Realist theme completely removed — Atlas is now the sole UI. ~20 `realist-*.css/.js/.svg`
  files deleted.
- `PeerRelationshipExplorer.razor` — peer-rating network visualization, now embedded in both
  Overview and Peer Insights.
- **Execution discipline** feature — a new domain concept (`ExecutionDiscipline.cs`, obligation
  tracking: Pending/OnTime/Late/Overdue/Excused/NotApplicable/Waived), its own page, CSS,
  persistence layer, and Excel evidence-parsing (`WorkbookService.Compliance.cs`). Origin unclear
  — not mentioned in Codex's session transcript, so it came from your local Claude Code session
  separately. Nobody has deeply reviewed this feature's correctness; it compiles and has no test
  failures, but wasn't specifically exercised.
- **Real bug fix**: peer review workbooks imported inside a ZIP package were being bucketed into
  the wrong month (a July workbook was appearing as August's reviews). Fixed by routing review
  workbooks through a dedicated multi-path `ImportEngineerReviewsAsync(paths, ReviewImportMode)`
  API instead of the generic per-file import path. Has a regression test
  (`ReviewWorkbookCannotBeImportedIntoTheWrongMonth`).
- `InteractionLog.cs` — lightweight diagnostic event log to `%LocalAppData%\...\interaction.log`,
  separate from Serilog, wired into `AppState`'s refresh lifecycle.

### Post-merge bug fixes (found via actual screenshots, not caught by tests)
CSS/text-based merges can produce code that compiles and passes tests but renders wrong — this
happened twice:
1. **Trend Alerts rendered as raw unstyled text.** Root cause: that feature borrowed
   `.atlas-primary` as a generic "card" class; visual-lab's CSS rewrite turned `.atlas-primary`
   into a specific 2-column grid for a different section, breaking the reuse. Fixed with scoped
   CSS for the alert list instead of the wrong shared class.
2. **Backup & Restore and Team Comparison pages had no padding**, content flush against the
   window edge — found via code audit (not yet screenshotted at the time), same root cause:
   missing `.route-backup`/`.route-team-comparison` scoped rules that every other page has.
   Confirmed fixed via a follow-up screenshot.

**Lesson for future merges of independently-developed UI branches**: always get real screenshots
of every touched page, not just a clean build + passing tests. Class-name reuse across
independently-evolved CSS is a silent failure mode that nothing automated catches.

---

## 4. Repo state / branches

- `main` — the only branch that matters going forward. Has everything above.
- All feature/worktree branches from this session (`worktree-agent-*`, `claude/perf-tailwind-grid-work`,
  `claude/github-actions-ci`, etc.) are merged and can be deleted/ignored.
- `agent/visual-lab` (origin) — merged into `main`, can be deleted once you've confirmed you don't
  need it as a reference anymore.
- Other stale branches exist on origin from earlier exploration
  (`agent/realist-*`, `agent/one-click-launcher-responsive-fix`, `agent/theme-readability-refactor`)
  — these predate this session's work and were never merged; worth a cleanup pass but not touched
  here since their relevance wasn't assessed.

No PRs were used — everything was merged directly to `main` via fast-forward or explicit merge
commits, per how this session's collaboration worked (multiple agents + you working in parallel,
PRs would have added friction without benefit for a single-maintainer repo).

---

## 5. Open item: the installer has never successfully built

This is the one substantive thing left unfinished. Timeline:

1. Velopack + `build/release.ps1` were built and code-reviewed, but **could not be tested** from
   the Linux sandbox that did most of this session's work (no Windows, no virtualization access
   confirmed and explained at length earlier in this session).
2. Tried to close that gap with GitHub Actions (free on this public repo, gives a real Windows
   runner). Both `ci.yml` and `release.yml` were added, and a `v1.0.0` tag was pushed to trigger
   the release workflow.
3. **Every workflow run failed instantly** — 2-3 seconds, no runner ever assigned, no logs
   captured (404 on log download — consistent with the job never actually starting). Happened on
   both `ubuntu-latest` and `windows-latest`, on both workflows, on every run. This pattern points
   to an **account or repository-level Actions restriction**, not a workflow syntax problem:
   - Check `https://github.com/bhadkamkar9snehil/EOS/settings/actions` — "Actions permissions"
     should be "Allow all actions and reusable workflows," not disabled/restricted.
   - Check `https://github.com/settings/billing` — Actions spending limit; some accounts default
     to a $0 limit that blocks Actions entirely even though public-repo minutes are free.
   - **This was never actually diagnosed** — the decision was made to abandon the GitHub Actions
     path rather than keep debugging it, since local build is simpler anyway.
4. **Current plan: build locally.** Instructions were handed to local Claude Code (running in
   VS Code on the Windows machine) to run `build/release.ps1` directly:
   ```powershell
   vpk --version                          # or: dotnet tool install -g vpk
   cd 'C:\Users\Admin\Documents\Office\EmployeeOperatingSystem'
   git checkout main; git pull origin main
   pwsh build/release.ps1 -Version 1.0.0
   dir build\Releases
   ```
   **As of this document, there is no confirmation this ran or what its output was** — the
   conversation moved to an unrelated question (trying to move a cloud chat session to a local
   environment) before that result came back. **This is the next thing to check.**

### If picking this back up
- First: ask whatever local Claude Code session is active whether `build/release.ps1` was ever
  actually run, and what happened.
- If it hasn't been run yet, run it — this is the critical path to a real, downloadable installer.
- If `vpk pack` fails, the most likely culprits: `vpk` not installed/on PATH, the
  `CopyCompatibleWindowsSdkFacadeForBuild`/`CopyCompatibleWindowsSdkFacadeForPublish` MSBuild
  targets in `DesktopHost.csproj` failing on a machine without the exact NuGet package cache the
  original dev environment had (this is a known pre-existing fragility, documented as audit
  finding #13 in `docs/performance-tech-installer-logging-audit.md`), or a `PublishReadyToRun`
  interaction with self-contained publish that behaves differently on a real Windows box than
  it did in cross-compilation checks from Linux.
- Once an installer is produced (`build/Releases/*.exe` or similar), **run it** — nobody has
  verified the actual Velopack install flow end-to-end. That's the real acceptance test.
- `UpdateSettings.FeedUrl` currently points at GitHub Releases, but no release has ever been
  published there (the tag `v1.0.0` exists on the repo, but the workflow that would create a
  Release from it never ran successfully). If you want in-app auto-update to actually work,
  you'll need to either get GitHub Actions working, or manually create a GitHub Release and
  upload the `vpk pack` output to it, or point `FeedUrl` at a different feed (e.g., a file share)
  and update accordingly.

---

## 6. Known unknowns / things nobody has verified

- **`ExecutionDiscipline` feature**: compiles, has no failing tests, but its actual business logic
  (obligation evaluation, grace-minute handling, deadline assessment) has not been reviewed by
  anyone in this session — it arrived as part of the visual-lab merge from an untraced source.
  Worth a dedicated look if it's going into real use.
- **Most pages have not been screenshotted.** Only Overview (Peer Rating Network drilldown),
  Backup & Restore, and Team Comparison have been visually confirmed post-merge. Given the CSS
  bug pattern found (§3, "Post-merge bug fixes"), assume other pages might have similar undetected
  styling gaps until actually looked at — especially `ExecutionDiscipline.razor` and
  `PeerRelationshipExplorer.razor`'s standalone view, which haven't been screenshotted at all.
- **Tailwind, CSS consolidation**: still not done. The blocking reason (visual-design work being
  unmerged) no longer applies — it's just not been prioritized since.
- **The stale `agent/*` branches on origin** (realist-fidelity-pass, realist-hi-fi, etc.) were
  never evaluated for whether they contain anything worth salvaging before this session's Atlas
  consolidation. Probably safe to delete, but not confirmed.
- **GitHub Actions failure root cause**: genuinely undiagnosed, not just deprioritized. If you
  want CI back at some point, start there rather than re-adding the workflow files blind.

---

## 7. Quick reference

**Build/test locally** (Windows, from repo root):
```powershell
dotnet build
dotnet test tests/EngineeringPerformance.Domain.Tests/
dotnet test tests/EngineeringPerformance.Infrastructure.Tests/
dotnet test tests/EngineeringPerformance.UI.Tests/
dotnet run --project src/EngineeringPerformance.DesktopHost
```

**Cut a release** (once the installer path is confirmed working):
```powershell
pwsh build/release.ps1 -Version 1.0.0
```

**Key docs**:
- `docs/performance-tech-installer-logging-audit.md` — original comprehensive audit
- `docs/tailwind-grid-ci-plan.md` — Tailwind/grid/CI evaluation + implementation status table
- `docs/installer.md` — Velopack setup details
- `docs/ci.md` — GitHub Actions workflow docs (currently non-functional, see §5)
- `docs/git-hosting-alternatives.md` — Gitea/Forgejo/GitLab evaluation (repo stayed on GitHub)
- `docs/VISUAL_VALIDATION.md`, `CONTEXT.md` — from the visual-lab merge, discipline/visual QA notes

**Key files if debugging the installer**:
- `src/EngineeringPerformance.DesktopHost/App.xaml.cs` — Velopack init, Serilog setup, exception handlers
- `src/EngineeringPerformance.DesktopHost/UpdateSettings.cs` — update feed URL
- `src/EngineeringPerformance.DesktopHost/EngineeringPerformance.DesktopHost.csproj` — publish settings, the fragile SDK-copy MSBuild targets
- `build/release.ps1` — the release pipeline script itself
