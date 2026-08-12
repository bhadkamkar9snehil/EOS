# EOS Comprehensive Audit: Performance, Tech Modernization, Installer, Logging, Features

Status: **research/planning document.** Update: item 3 in "Recommended fix order" below
(defer non-critical scripts; lazy-load `echarts.min.js`) is now implemented — see
`docs/tailwind-grid-ci-plan.md` sections 4-6 for what shipped (ECharts lazy-loading per chart
route, glue-script consolidation evaluated and skipped with reasons, CSS `<link>` audit found no
dead/orphaned files). Items 1 (CSS `<link>`/`<script>` blocking) and 4 (CSS consolidation) remain
as described below except for the defer/lazy-load slice now done; the rest of this document is
otherwise unchanged findings, not yet acted on.
Scope: `EngineeringPerformance` solution (Blazor UI hosted in a WPF `BlazorWebView` desktop app), EF Core + SQLite, ClosedXML for Excel I/O.

Product context: EOS is a local-only Windows desktop tool for engineering managers/team leads. It ingests ERP evidence exports (timesheets, attendance, approvals) and peer-review workbooks via Excel import, computes an "operational score" per employee/team, and produces dashboards, peer-collaboration analytics, and Excel report/template exports. Single-user, single-tenant, no server, no multi-tenant auth.

---

## 1. Performance Audit (findings)

### High impact
1. **Blocking asset loading** — `DesktopHost/wwwroot/index.html:8-28` loads 21 `<link rel="stylesheet">` and 8 `<script>` tags synchronously in `<head>`, none deferred/async, before Blazor's own bootstrap script. Every launch pays this cost cold.
2. **1.1MB `echarts.min.js`** loaded unconditionally on every page (`UI/wwwroot/echarts.min.js`), even on chart-free routes (Settings, Templates, Data Browser).
3. **Sequential 7-call DB waterfall** on every navigation/refresh — `UI/AppState.cs:36-48` awaits `GetDashboardAsync`, `GetEmployeesAsync`, `GetMonthlyPerformanceAsync`, `GetPerformanceHistoryAsync`, `GetWeeklyPerformanceAsync`, `GetExcludedNamesAsync`, `GetPeerReviewsAsync`, `GetTeamsAsync` one at a time instead of `Task.WhenAll`. Each uses its own `DbContext` from a factory, so they're safe to parallelize.
4. **N+1 queries in the import pipeline** — `Infrastructure/LocalApplicationDatabase.cs:249, 306, 312, 286` issue a per-row `SingleOrDefaultAsync`/`AnyAsync` inside `foreach` loops instead of one bulk preload + in-memory dictionary lookup. Import time scales linearly with row count instead of being ~flat.

### Medium impact
5. **Non-sargable date filter** — `GetPerformanceHistoryAsync` (`LocalApplicationDatabase.cs:434-436`) filters on `x.Year * 100 + x.Month`, a computed expression that can't use any index → full table scan on every trend/heatmap query. Fine today at small scale, degrades as history accumulates.
6. **Coarse-grained change notification** — every page subscribes to a single `AppState.Changed` event and calls `StateHasChanged()` unconditionally; no per-slice notifications, no `ShouldRender()` overrides anywhere. One mutation on one page re-renders the whole mounted tree.
7. **`Virtualize` fed a fresh `.ToList()` every render** — `PeopleWorkspace.razor:109` allocates a new list copy of the full employee collection inline in markup, partially defeating virtualization's allocation savings.
8. **Desktop publish not optimized** — `DesktopHost.csproj` is self-contained (`win-x64`) but has no `PublishReadyToRun`, no trimming, and `PublishSingleFile=false`. Full runtime ships, JIT cold-starts on every launch, and there are many loose files on disk instead of one bundle.

### Low impact
9. **408KB `app-icon.png`** — oversized for an icon asset.
10. **Exclusions list re-queried/re-normalized 5x per refresh cycle** (`LocalApplicationDatabase.cs:65-70`) — called from nearly every read method instead of cached once per request.
11. **Overlapping/likely-superseded CSS** — `realist-*.css`, `atlas-*.css`, `theme-*.css` (~350KB across 21 files) suggest multiple design-system iterations left in place rather than consolidated.
12. Version skew across `Microsoft.AspNetCore.Components.*` (10.0.7) vs EF Core/WebView.Wpf (10.0.10) vs `Microsoft.Extensions.Hosting` (10.0.0) — not a perf bug directly, but worth aligning to avoid subtle behavioral mismatches.
13. Custom MSBuild target copying `Microsoft.Windows.SDK.NET.dll` post-build/publish in `DesktopHost.csproj` — a workaround indicating a Windows SDK targeting-pack version mismatch; worth root-causing rather than patching around indefinitely.

### Recommended fix order
1. Parallelize `AppState.RefreshAsync` with `Task.WhenAll` — cheap, immediate win.
2. Fix import N+1s with dictionary preloads.
3. Defer non-critical `<script>` tags; lazy-load `echarts.min.js` only on chart-using routes.
4. Consolidate/remove dead CSS; bundle+minify what remains.
5. Fix the `Year*100+Month` query to a sargable range filter + supporting index.
6. Add `PublishReadyToRun` (and evaluate trimming) to the desktop publish profile.

---

## 2. Tech to leverage instead of hand-crafting

The app currently hand-rolls its front-end pipeline (raw `<script>`/`<link>` tags, no bundler), its own theme/CSS system (3 overlapping design iterations), manual chart wiring around raw ECharts, and has zero logging/telemetry/installer tooling. Below are concrete swaps, each chosen for being low-friction in a .NET/Blazor desktop context (no need to adopt a Node toolchain unless noted).

### Front-end build & asset pipeline
- **Problem**: 21 raw `<link>` tags, 8 raw `<script>` tags, manual `?v=N` cache-busting, no minification/bundling.
- **Options**:
  - **`Microsoft.AspNetCore.StaticWebAssets` + BuildBundlerMinifier / `LibManBundle`** — stays entirely in the .NET build, no Node dependency. Adequate for consolidating CSS/JS into fewer, minified, content-hashed files.
  - **Vite (with `Microsoft.AspNetCore.Components` + a thin JS entry)** — more powerful, better tree-shaking/code-splitting, but introduces a Node build step into a currently Node-free repo. Worth it if the app's front-end complexity keeps growing; overkill otherwise.
  - Recommendation: start with the .NET-native bundler/minifier (keeps build simple), revisit Vite only if JS surface area grows materially.

### Charting
- **Problem**: raw ECharts (1.1MB) + hand-written glue scripts (`charts.js`, `analytics-charts.js`, `atlas-charts.js`, `realist-runtime.js`) totaling ~90KB of custom interop code, loaded on every page.
- **Options**:
  - Keep ECharts (it's capable and already integrated) but **lazy-load it only on routes that chart** via dynamic `<script>` injection from Blazor's `IJSRuntime`, and **delete the bespoke `*-charts.js`/`*-runtime.js` glue** in favor of a single small, well-tested interop wrapper.
  - **`Plotly.Blazor`** or **`ChartJs.Blazor`** — native Blazor component wrappers avoid hand-written JS interop entirely, at the cost of some flexibility versus raw ECharts. Given the app's charts look bespoke/branded, likely not a full replacement, but worth it for simpler pages (e.g. small sparkline/trend widgets).
  - Recommendation: keep ECharts for the flagship dashboards, lazy-load it, and replace only the ad-hoc interop scaffolding with one maintained wrapper library.

### CSS/theming
- **Problem**: 3 parallel hand-written theme systems (`theme-*`, `realist-*`, `atlas-*`) — ~350KB, unclear which is current.
- **Options**: consolidate onto **one** design system. If staying custom, adopt CSS custom properties + a single token file rather than duplicated files per "era." If open to a framework, **Tailwind CSS** (via a small PostCSS/Node step, or `Tailwind.Blazor` bindings) gives utility-first consistency and purges unused CSS automatically at build time — directly solves the "which CSS file is dead" problem going forward.

### Virtualization/grids
- **Problem**: hand-rolled `.ToList()` allocation feeding `Virtualize`; `QuickGrid` is already used in places.
- **Options**: standardize on `Microsoft.AspNetCore.Components.QuickGrid` (already a dependency) everywhere tabular data is virtualized, rather than mixing raw `Virtualize` + manual sorting/filtering logic. QuickGrid gives virtualization, sorting, and paging with far less hand-written code.
- **Status: ✅ Done.** `PeopleWorkspace.razor`'s `Virtualize` allocation was fixed (materializes a
  snapshot once per state refresh). A follow-up audit re-checked every remaining non-QuickGrid
  `.razor` page (`PeerInsights`, `Reports`, `EmployeeDetail`, `EmployeeMetricsExtension`) for
  hand-rolled tabular rendering that should become QuickGrid — none qualify (a cross-tab matrix, a
  `<select>` dropdown, a capped single-employee card list, and metric tiles, respectively; no rows
  of records to grid). `DataBrowserPage.razor`/`Timesheets.razor` remain the only real tables and
  were already on QuickGrid. See `docs/tailwind-grid-ci-plan.md`'s "Grid standardization audit."

### Data access / import pipeline
- **Problem**: manual per-row EF Core lookups (N+1), hand-rolled Excel parsing via ClosedXML with ad-hoc validation.
- **Options**:
  - Keep EF Core (appropriate for SQLite + this data volume) but adopt **`EFCore.BulkExtensions`** or manual `ToDictionaryAsync` preloads for the import hot paths instead of per-row queries.
  - For Excel import validation, consider **`FluentValidation`** to replace ad-hoc `try/catch { continue; }` swallowing with structured, reportable validation results — directly improves both reliability and the eventual logging/error-reporting story below.
- **Status:** N+1 reads already fixed via `ToDictionaryAsync` preloads (prior branch). **Bulk
  writes: ❌ evaluated and skipped** — `EFCore.BulkExtensions.Sqlite` 10.0.1 throws a UNIQUE
  constraint violation on SQLite for a batch mixing inserts and updates (the real shape
  `ImportEmployeeRosterAsync` needs), confirmed with a test before reverting; the existing
  dictionary-preload + `SaveChangesAsync` approach was kept. **FluentValidation: ✅ done** —
  `ImportEngineerReviewsAsync`/`ImportPackageAsync`'s silent `catch { continue; }` blocks now
  produce structured `ImportSkipReason` records (workbook shape/readability validated via
  FluentValidation), logged today through a `Debug.WriteLine` placeholder pending the Serilog work
  below. See `docs/tailwind-grid-ci-plan.md`'s "Bulk-write evaluation."

### Logging (see §4 for full design)
- **`Serilog`** with `Serilog.Sinks.File` (rolling), `Serilog.Sinks.Debug`, and `Microsoft.Extensions.Logging` integration — replaces the two ad-hoc `File.WriteAllTextAsync`/`File.AppendAllText` crash handlers entirely. **Status: not started here** — owned by a separate logging-focused branch/agent; the `ImportSkipReason` structured records above are deliberately left as a `Debug.WriteLine` placeholder for that work to pick up.

### Installer/deployment (see §3 for full design)
- **Velopack** (formerly Squirrel.Windows successor) — modern, actively maintained, purpose-built for exactly this scenario (self-contained WPF/.NET desktop app, win-x64, wants installer + auto-update). Strong recommendation over WiX/MSIX for this project's size and team (WiX is powerful but heavyweight to maintain by hand; MSIX has Store/sideloading friction for a niche internal tool). **Status: not started here** — owned by a separate installer-focused branch/agent.

### Testing/quality
- Current test projects only cover `Infrastructure`/`Domain` — no UI/component tests. Consider **`bUnit`** for Blazor component tests (render `Overview.razor`, `PeopleWorkspace.razor` etc. in isolation) to catch regressions from the re-render/virtualization fixes above.
- **Status: ✅ Done.** New `tests/EngineeringPerformance.UI.Tests` project (bUnit 2.9.0 + xunit,
  versions matched to `Domain.Tests`) with 8 tests against `ScoringPage.razor` (form
  validation/persistence, error-state surfacing) and `EmployeeMetricsExtension.razor` (renders
  nothing without data, shows latest-month metrics, expanded history section) via a
  `FakeApplicationDatabase` stand-in for `IApplicationDatabase`. All passing.

### Telemetry (optional, given local-only/no-network posture)
- If the team ever wants aggregate usage/crash visibility across installs: **Sentry** (self-hostable, has a first-class .NET SDK) is lighter-weight than Application Insights for a desktop app with no existing Azure footprint. Purely optional given the product's local-only design — flagging as a decision point, not a recommendation to adopt.

---

## 3. Installer Design

**Current state**: none. Only `EPA Launcher.cmd` (a raw launcher script) and manual `dotnet publish` with `win-x64`, self-contained, `PublishSingleFile=false`. No CI/CD (`.github/workflows` is empty/absent).

### Recommendation: Velopack
Velopack is purpose-built for exactly this shape of app (self-contained .NET desktop, WPF, single machine architecture target) and gives you, with modest setup:
- A proper Windows installer (`.exe`) with Start Menu/desktop shortcuts, uninstall entry in "Apps & Features."
- **Delta-patch auto-updates** — subsequent releases ship only the diff, not a full reinstall; updates can be checked/applied from within the app.
- Cross-platform story for free if EOS ever needs macOS/Linux builds (unlikely here, but zero-cost optionality).
- Simple release pipeline: `vpk pack` produces the installer + update feed from a normal `dotnet publish` output; publishes to a folder, GitHub Releases, S3, Azure Blob, etc.

**Why not WiX**: WiX Toolset gives more control (MSI, complex install logic, enterprise GPO deployment) but requires hand-authoring XML install logic and has no built-in auto-update story — you'd need to bolt on a separate updater. Worth reconsidering only if this ever needs enterprise MSI/GPO deployment requirements.

**Why not MSIX**: MSIX is Microsoft's modern packaging format with good update/Store integration, but sideloading (needed here since this isn't going through the Microsoft Store) requires trusted certificates and has more device-provisioning friction for a small-team internal tool than Velopack's plain installer.

### Proposed installer pipeline
1. `dotnet publish` the `DesktopHost` project with `PublishReadyToRun=true` (from the perf audit) — produces a faster-starting, self-contained win-x64 output.
2. `vpk pack` (Velopack CLI) wraps that publish output into a versioned release + Setup.exe + delta packages.
3. **GitHub Actions workflow** (currently absent entirely) to:
   - Build + test on every push/PR.
   - On tagged release: publish, pack with Velopack, attach `Setup.exe` + Velopack release feed to a GitHub Release.
4. App checks the release feed on startup (Velopack's `UpdateManager`) and offers "Update available — restart to apply."

This closes two gaps at once: "proper installer" and "no CI/CD at all" (currently zero workflow files).

---

## 4. Logging Design

**Current state**: effectively none.
- Two ad-hoc handlers in `DesktopHost/App.xaml.cs`: startup failure writes `exception.ToString()` to `%LocalAppData%\EngineeringPerformance\startup-error.log`; unhandled dispatcher exceptions append to `runtime-error.log`. Both are raw file writes, no rotation, no structure, no levels.
- No `AppDomain.UnhandledException` or `TaskScheduler.UnobservedTaskException` handlers — background-thread and unobserved async exceptions are **not caught anywhere** today.
- Elsewhere: several silent `catch (Exception) { continue; }` / `catch (JsonException) { }` blocks that swallow errors with zero record — e.g. `LocalApplicationDatabase.cs` skips unparseable import files with no log of what was skipped or why.
- `AppState.cs` surfaces `ex.Message` to the UI on failure but never persists it.

### Proposed architecture: Serilog + Microsoft.Extensions.Logging
1. **Adopt `Serilog`** as the logging backend, wired through `Microsoft.Extensions.Logging` (`Serilog.Extensions.Hosting`) so both the WPF host and the `Microsoft.Extensions.Hosting`-based Blazor pieces share one `ILogger` abstraction — no more hand-rolled file writes.
2. **Sinks**:
   - `Serilog.Sinks.File`, rolling daily, retained ~14-30 days, structured (JSON or compact text) to `%LocalAppData%\EngineeringPerformance\logs\eos-.log`.
   - `Serilog.Sinks.Debug` for `Debug.WriteLine` visibility during development.
   - Optional: `Serilog.Sinks.Seq` or file-based only, given this is a local-only desktop app — a centralized log server is unnecessary unless multiple installs need aggregate visibility.
3. **Global exception coverage** — replace/extend `App.xaml.cs`'s two handlers with all three .NET catch-all points:
   - `Application.DispatcherUnhandledException` (already present — route through Serilog instead of raw file write).
   - `AppDomain.CurrentDomain.UnhandledException` (missing today — catches background-thread crashes).
   - `TaskScheduler.UnobservedTaskException` (missing today — catches fire-and-forget `async void`/unawaited task failures, a known async pitfall for exactly this kind of "await X; await Y" sequential code seen in `AppState.RefreshAsync`).
4. **Structured logging at key boundaries**:
   - Import pipeline (`LocalApplicationDatabase.ImportSourceAsync`, `ImportEmployeeRosterAsync`): log every skipped/invalid file with the reason (replacing the current silent `continue`), plus row counts imported/updated/skipped per run — directly useful for the "Import Ledger" feature already in `DataImportsPage`/`DataBrowserPage`.
   - Scoring engine (`ScoringPage` weight changes): log configuration changes with old/new values — an audit trail for "why did this employee's score change."
   - `AppState.RefreshAsync` failures: log with context (which of the 7 loads failed) instead of only surfacing `ex.Message` to the UI.
5. **Log levels**: `Debug` for interop/render diagnostics (dev only, filtered out in release), `Information` for imports/exports/scoring changes, `Warning` for skipped/invalid rows, `Error` for caught exceptions, `Fatal` for startup failures.
6. **In-app log viewer** (optional, pairs well with feature list below): a simple "Diagnostics" page in Settings that tails the current day's log file — helps a non-technical manager describe a problem accurately when reporting issues, without needing to find `%LocalAppData%` manually.
7. **Retention/size caps**: cap total log directory size (Serilog's `retainedFileCountLimit`) so a long-running desktop install doesn't accumulate unbounded log files.

---

## 5. Feature Suggestions

Grounded in what the app already does (evidence-based scoring, peer review workbooks, team dashboards, Excel import/export pipeline):

> **Implementation status (2026-08-12):** items 1–5 below were implemented end to end
> (code + unit tests, 28 new tests total, all passing alongside the pre-existing 8/8+8/8
> suites). Items 6–8 were investigated but not built — see the notes under each and the
> honest status write-up at the end of this section for why, and what remains.

**Data & analytics**
- ✅ **Trend alerts** — *implemented.* The score-drop (≥10 pts month-over-month, see
  `Analytics.ScoreDropAlertThreshold`) and missing-evidence anomaly rules already existed in
  `Analytics.Anomalies` but were only summarized as a count on Overview's "Alert pressure"
  gauge; they're now also listed by name in a new "Trend alerts" section on Overview
  (`Overview.razor`), reusing the list markup `EmployeeDetail.razor` already used per-person.
- ✅ **Team comparison view** — *implemented.* New `/team-comparison` page
  (`TeamComparison.razor`) aggregates `AppState.Performance`/`AppState.Teams` (already loaded
  for Overview, not re-fetched) into per-team scorecards: average operational score, the three
  component scores, timesheet fill rate, and open trend-alert count.
- ⏳ **Export scheduling** — *not implemented*, see §6 below.
- ✅ **Configurable scoring presets** — *implemented.* `ScoringPage.razor` now has a presets
  section: save the current weights under a name, apply a saved preset (saves + recalculates,
  same as the existing button), delete a saved preset. Ships "Individual Contributor" (55/15/30)
  and "Team Lead" (35/35/30) as built-in defaults. Persisted as `scoring-presets.json` next to
  `operational-scoring.json`, matching how the live weights are already stored — no new DB table
  or migration needed.

**Import/data quality**
- ✅ **Import validation preview** — *implemented.* `IApplicationDatabase.PreviewImportSourceAsync`
  reads and diffs a workbook against the current database (rows added/updated/unchanged) inside a
  `DbContext` that's discarded instead of saved. Wired into `DataImportsPage.razor`'s existing
  per-slot upload button as a confirmation summary shown before the real import commits.
- ⏳ **Import history/rollback** — *not implemented.* Partially covered by the new
  backup/restore feature (a full-database restore undoes a bad import along with everything
  else), but a per-import, surgical rollback (undo just the last upload for one slot) is a
  separate, unbuilt feature.

**Peer review workflow**
- ⏳ **In-app review status tracking / reminder nudges** — *not implemented*, see §7 below.
- ⏳ **Anonymization controls** — *not implemented*, out of scope for this pass.

**Operational**
- ⏳ **Auto-update** — *not implemented*, out of scope for this pass (installer-owned).
- ✅ **Backup/restore** — *implemented.* New `/backup` page: one-click export of the SQLite
  database + operational-scoring/preset config to a timestamped zip (default
  `Documents/EngineeringPerformance-Backups`, or a chosen folder), and a restore flow that picks
  a backup file, confirms (destructive — replaces all current data), automatically backs up the
  *current* database first as a safety net, then restores. A full app restart is recommended
  after restore for every open connection to pick up the new file cleanly.
- ⏳ **Multi-profile/multi-team support** — *not implemented*, design sketch only, see §8 below.
- ❌ **Dark mode / theme toggle** — explicitly excluded from this pass by the user (separate,
  in-progress local visual-design work).

**Diagnostics**
- ⏳ **In-app "Diagnostics" page** — *not implemented*, out of scope for this pass (logging-owned).

### 6. Export scheduling — feasibility notes (not implemented)

Two realistic approaches for a WPF desktop app that isn't always running:
1. **Windows Task Scheduler integration** — the installer (or a "Schedule reports" button)
   registers a scheduled task that launches the exe with a `--generate-monthly-report` CLI flag.
   Correct even if the app isn't open, but needs installer/elevation work and a headless code
   path through `App.xaml.cs` that skips `MainWindow.Show()`.
2. **In-app timer / check-on-launch** — on startup, compare "today" against "last auto-generated
   month" (a small marker file); if it's a new month past the 1st, generate the packet via the
   existing `WorkbookService.GenerateTeamReport` and drop it in a configured folder. Simpler, no
   installer changes, but only fires when the app happens to be opened that day.
Given the constraints of this pass (time, and "don't touch the installer"), neither was built.
Approach 2 is the lower-risk one to pick up next — it only touches `App.xaml.cs`.

### 7. Peer-review reminder nudges — feasibility notes (not implemented)

No email/notification channel exists anywhere in the codebase (no SMTP, no webhook client, no
`System.Net.Mail` usage). Per the task instructions, this should become an **in-app "pending
reviewers" list/report** rather than actually sending emails/Teams messages — `PeerInsights.razor`
already computes coverage stats (`Analytics.Peers`), so a "who hasn't submitted yet" view is a
straightforward addition on top of data already loaded. Not implemented in this pass; flagged
honestly as untouched rather than half-built.

### 8. Multi-workspace support — design sketch only (not implemented)

This is the most architecturally invasive item and was **not implemented** — it needs dedicated
planning, not a quick add, per the task instructions. Sketch of the shape it would take:
- The database path is currently a single fixed file:
  `%LOCALAPPDATA%\EngineeringPerformance\engineering-performance.db`
  (`ServiceCollectionExtensions.AddLocalInfrastructure`). Multi-workspace means N such files, one
  per workspace, e.g. `%LOCALAPPDATA%\EngineeringPerformance\Workspaces\<slug>\engineering-performance.db`.
- `IDbContextFactory<PerformanceDbContext>` is currently a DI singleton bound to one connection
  string at host startup. Supporting a workspace switch at runtime (without restarting the app)
  means either (a) rebuilding the DI container's data layer on switch, or (b) making the
  connection string resolvable per-request from an `ICurrentWorkspace` service rather than fixed
  at registration — (b) is architecturally cleaner but touches every `contextFactory.CreateDbContextAsync`
  call site indirectly through the options builder.
- `operational-scoring.json` / `scoring-presets.json` / the Imports folder / backup defaults all
  currently hang off one shared `dataDirectory` — each would need to move under the
  per-workspace folder too, and the new backup/restore feature built in this pass would need a
  "which workspace" dimension added to it.
- UI: a workspace switcher (like the existing month/team selectors in `MainLayout`'s topbar)
  and a "new workspace" flow, gated so it doesn't force existing single-workspace users through
  a migration on next launch — the existing single DB should become "the default workspace"
  transparently.
- Given the CSS/theme conflict-risk instructions for this task, and that this touches DI wiring,
  the file layout, and the app shell nav, it's the one item that most needs its own dedicated
  session rather than being folded into this pass.

---

## Summary table

| Area | Current state | Recommendation |
|---|---|---|
| Perf: asset loading | 21 blocking CSS + 8 blocking JS, unbundled | Bundle/minify via .NET-native bundler, defer/lazy-load |
| Perf: charts | 1.1MB ECharts loaded everywhere | Lazy-load per route, consolidate glue scripts into one wrapper |
| Perf: data fetch | 7 sequential DB calls per refresh | `Task.WhenAll` |
| Perf: imports | N+1 per-row queries | Bulk preload + dictionary lookup |
| Perf: publish | No RTO/trim/single-file | Enable `PublishReadyToRun` |
| CSS/theming | 3 overlapping hand-written systems | Consolidate to one; consider Tailwind |
| Grids | Mixed raw `Virtualize` + QuickGrid | Standardize on QuickGrid |
| Logging | **Implemented.** Serilog (rolling daily file sink to `%LocalAppData%\EngineeringPerformance\logs\eos-.log`, 30-day retention, plus a Debug sink) wired through `Microsoft.Extensions.Logging`/`ILogger<T>` across `DesktopHost`, `Infrastructure`, and `UI`. `App.xaml.cs`'s two ad-hoc handlers now log through Serilog instead of raw file I/O; `AppDomain.UnhandledException` (Fatal) and `TaskScheduler.UnobservedTaskException` (Error, `SetObserved()`) were added, closing the background-thread/unobserved-task gap. `LocalApplicationDatabase`'s silent `catch { continue; }` blocks now log a Warning with the file/reason before skipping, and import runs log new/updated row counts (Information). Scoring weight changes (`ConfigurableApplicationDatabase.SaveOperationalScoringSettingsAsync`) log old→new values. `AppState.RunAsync` now logs caught exceptions (Error) in addition to the existing UI message. An in-app **Diagnostics** page (`/diagnostics`, linked from Settings and the main nav) shows version/DB/log info, tails today's log, and can zip the last 5 days of logs + `info.txt` into a bundle for bug reports (written to a temp folder, path shown in the toast — no Explorer-launch integration, kept simple per scope). Still open: `TemplatesPage.razor`'s two generic `catch (Exception ex) { State.ShowMessage(...) }` blocks were left as UI-only surfacing (not in the agreed task scope) rather than also being routed through `ILogger`. |
| Installer | None | Velopack (installer + auto-update) |
| CI/CD | None | GitHub Actions: build/test on push, release pipeline on tag |
| Testing | Domain/Infra only | Add bUnit for Blazor component coverage |
| Features | — | Trend alerts, team comparisons, scheduled exports, import preview/rollback, backup/restore, in-app diagnostics, dark mode |

No code changes were made in producing this document.
