# Tailwind, Grid, and CI/CD Plan

Status: **planning document — most items intentionally not implemented yet.**
Branch: `claude/perf-tailwind-grid-work` (backend perf fixes only; see below for why the rest is deferred).

## Sequencing note — read first

You have significant, unmerged visual-design work sitting locally (on top of the "Realist"
instrument-console CSS/theme work already in `main`). That work is not on GitHub, so I cannot see
its diff or account for conflicts with it. Tailwind adoption and any CSS/markup grid replacement
both touch exactly the surface that kind of work touches (`index.html`, `.razor` markup, `wwwroot`
CSS). Landing either now risks a large, painful merge against your local branch.

**What's actually implemented on this branch**: all four "performance" items from the audit that
are pure C#/config and don't touch CSS/markup structure (`AppState.RefreshAsync` parallelization,
the three N+1 import fixes, the sargable history-query fix, `PublishReadyToRun`), plus the
`Virtualize` allocation fix and script `defer` ordering in `index.html` (a small, low-conflict
diff — flagged below in case it collides with your local work).

**What's a plan only, not code**: Tailwind, grid consolidation/replacement, GitHub Actions
alternatives. Recommended order:
1. Push your local visual-design branch to GitHub.
2. Merge/rebase it against `main` (or this branch, once merged).
3. *Then* execute the Tailwind and grid plans below as a follow-up branch, so the two efforts don't
   fight over the same files twice.

If you'd rather I proceed with Tailwind/grid now regardless, say so explicitly and I will, but the
risk above is real and worth avoiding if the local work is close to done.

---

## 1. Tailwind CSS — evaluation

### What it would fix
- Today: 21 hand-written CSS files (~350KB) spanning at least 3 overlapping design-system eras
  (`theme-*`, `realist-*`, `atlas-*`), unclear which rules are live vs dead, no single source of
  truth for spacing/color/typography tokens.
- Tailwind gives: one utility vocabulary, a single token config (`tailwind.config.js`) as the
  actual source of truth for the palette that's currently duplicated across `theme.js`'s inline
  palette objects and multiple CSS files, and **automatic dead-code elimination** — its JIT
  compiler only emits CSS for classes actually used in your `.razor`/`.html` files, which directly
  solves "which of these 21 files is still referenced."

### Cost / friction, specific to this repo
- **Introduces a Node build step.** This repo currently has zero `package.json` — it's a pure
  .NET solution built via `dotnet build`/`publish`. Tailwind's CLI (even the standalone binary
  that doesn't require Node) needs to run as a pre-build step and its output needs to land in
  `wwwroot` before `dotnet publish` runs. Concretely:
  - Use the **Tailwind standalone CLI binary** (no Node/npm required) — avoids adding an npm
    toolchain dependency to a project that has never had one. Invoke it via an MSBuild
    `<Target BeforeTargets="Build">` that shells out to the binary, similar in spirit to the
    existing `CopyCompatibleWindowsSdkFacadeForBuild` target already in `DesktopHost.csproj`.
  - This is the single biggest integration cost: someone has to maintain that MSBuild target and
    make sure CI/build machines have the binary available (vendor it into the repo or fetch it in
    a setup step).
- **Existing hand-rolled theme system (`theme.js`) is a runtime, user-switchable palette engine**
  (14 themes, light/dark, density/motion toggles, all switched at runtime via CSS custom
  properties). Tailwind's utility classes are static at build time — Tailwind doesn't replace
  `theme.js`'s runtime theme-switching; it would sit *alongside* it, generating utilities that
  reference the same CSS custom properties `theme.js` already sets (e.g.
  `bg-[var(--epa-surface)]`). This is very doable but means Tailwind adoption here is "utility
  layer for layout/spacing," not "replaces the theme system" — worth being explicit about so
  expectations are calibrated.
- **Migration is incremental-friendly**: Tailwind can be introduced page-by-page (it doesn't
  require touching all 21 CSS files at once) — start with one page's markup, verify visually,
  move to the next. This lowers risk relative to a big-bang rewrite, but the total migration
  across every `.razor` file with hand-written CSS classes is still a substantial, visible-surface
  change — exactly the kind of change that should happen once, after the visual-design branch
  lands, not twice.

### Recommendation
**Adopt it, but sequence it after the visual-design merge**, using the standalone CLI binary (no
Node dependency) wired through an MSBuild pre-build target. Migrate incrementally, page by page,
starting with the pages that have the least CSS specificity to untangle (`ScoringPage`,
`TemplatesPage` looked like reasonable low-complexity starting points from the earlier audit — a
proper migration order should be picked after seeing final visual-design markup, since it may
change what "least complex" means).

---

## 2. Grid component — evaluation

### Current state
Already mostly consistent: `QuickGrid` (`Microsoft.AspNetCore.Components.QuickGrid`, already a
project dependency) is used in `DataBrowserPage.razor` (2 grids) and `Timesheets.razor`. Only
`PeopleWorkspace.razor` used raw `<Virtualize>` instead — **fixed on this branch** (see below).

### Options evaluated

| Option | License | Virtualization | Sort/filter/group | Excel export | Notes |
|---|---|---|---|---|---|
| **QuickGrid** (current) | Free, MS-official | Built-in | Sort ✓, filter via custom column templates, no grouping | No | Already a dependency; minimal API surface; official long-term support as part of ASP.NET Core |
| **MudBlazor DataGrid** | Free, MIT | Built-in | Sort ✓, filter ✓, group ✓ | Via export libs, not built-in | Full component library (buttons/dialogs/forms too) — but adopting it as a *component library* overlaps with theming/visual-design surface, not just the grid |
| **Radzen Blazor DataGrid** | Free, MIT | Built-in | Sort/filter/group ✓ | Built-in Excel export | Also a full component suite; same overlap concern as MudBlazor |
| **Syncfusion Blazor Grid** | Free community license (revenue-gated, <$1M/yr) | Built-in | Full-featured, best-in-class | Built-in | Commercial-grade features but a licensing dependency to track (revenue threshold), and a much heavier component to adopt for what's currently a modest set of grids |
| **Telerik/DevExpress Blazor Grid** | Paid | Built-in | Full-featured | Built-in | Not justified for this app's current grid complexity/scale (a handful of grids, local SQLite data, no huge datasets) |

### Recommendation: keep and standardize on QuickGrid — do not replace
Reasoning:
- The app's grid needs today are modest: a handful of virtualized tables over local SQLite data,
  no complex grouping/pivoting requirements evident in the pages reviewed.
- QuickGrid already covers 3 of 4 grid usages; it's free, officially maintained, and has zero
  licensing overhead.
- Adopting MudBlazor/Radzen "for the grid" in practice means adopting their whole component
  library if you want visual consistency (buttons, inputs, dialogs styled to match) — that is a
  much bigger surface-level change than "replace the grid," and it's exactly the kind of decision
  that should wait until the visual-design direction is locked in (a new component library brings
  its own default styling that would fight with a hand-tuned custom theme system).
- If a genuine feature gap shows up later (e.g., built-in Excel export directly from the grid, or
  grouping), Radzen is the better fallback (MIT, free, built-in export) over Syncfusion (licensing
  to track) or Telerik/DevExpress (paid, unjustified at this scale).

**Action taken on this branch**: fixed `PeopleWorkspace.razor`'s `<Virtualize Items="State.Employees.ToList()">`,
which allocated a full list copy on every render. Now materializes `_employeesSnapshot` once per
`AppState.Changed` event instead of once per render (`PeopleWorkspace.razor:109` and its `@code`
block). No grid replacement — the fix is allocation-locality, not the component choice.

---

## 3. CI/CD — GitHub Actions alternatives

GitHub Actions is explicitly off the table. Given this is a small-team/single-maintainer desktop
app with no existing cloud infrastructure footprint, options in order of recommendation:

### Recommended: local build/release script, no hosted CI
For a project at this scale (one repo, likely 1-2 active contributors, a desktop installer as the
release artifact), the honest default is: **you don't need a CI service at all.** A single
PowerShell script (`build/release.ps1`) that:
1. Runs `dotnet test` across the solution.
2. Runs `dotnet publish` with `PublishReadyToRun`.
3. Invokes Velopack's `vpk pack` (from the installer plan) to produce the installer + update feed.
4. Optionally pushes the release feed to wherever it's hosted (a file share, GitHub Releases via
   authenticated API call, Azure Blob, etc.)

...run manually before each release, or wired into a **local pre-push git hook** that at minimum
runs `dotnet test` so broken code never reaches the remote. This has zero recurring cost, zero
new service to trust, and matches the project's current all-local tooling posture (no CI files
exist today).

### If some automation is wanted without Actions
- **Azure DevOps Pipelines** — free tier (1,800 build-minutes/month) is comfortably enough for a
  project this size; can build/test/pack on every push and publish releases; integrates with a
  GitHub-hosted repo without needing GitHub Actions specifically. Reasonable middle ground if you
  want cloud-triggered builds without Actions.
- **A self-hosted runner via a simple webhook + script** (e.g. a small always-on machine or a
  scheduled task that polls the repo) — more maintenance burden than it's worth here; only makes
  sense if you already have infrastructure sitting idle for this purpose.
- **Jenkins/TeamCity** — enterprise-grade, but real overkill for a single-installer desktop app;
  not recommended unless there's a pre-existing instance you already run for other projects.

### Recommendation
Start with the local script + pre-push hook. Revisit Azure DevOps Pipelines only if/when you
want releases triggered automatically on tag-push without running anything locally — it's the
lowest-friction "real CI" option that isn't GitHub Actions.

---

## Summary of what changed on this branch vs. what's still a plan

| Item | Status |
|---|---|
| `AppState.RefreshAsync` parallelized (`Task.WhenAll`) | ✅ Done |
| Import N+1 fixes (roster + source import, 3 loops) | ✅ Done |
| Non-sargable `Year*100+Month` history query | ✅ Done (sargable range filter) |
| `PublishReadyToRun` | ✅ Done |
| `PeopleWorkspace` Virtualize allocation | ✅ Done |
| Script `defer` ordering in `index.html` | ✅ Done (small diff, flagged for conflict risk) |
| Tailwind adoption | 📝 Planned — do after visual-design branch merges |
| Grid replacement | 📝 Evaluated — **recommendation is no replacement**, keep QuickGrid |
| Grid standardization audit (`PeerInsights`, `Reports`, `EmployeeDetail`, `EmployeeMetricsExtension`) | ✅ Done — audited, **no changes needed**; see "Grid standardization audit" below |
| CSS consolidation (3 overlapping theme systems) | 📝 Not started — depends on visual-design branch outcome |
| CI/CD (non-Actions) | 📝 Planned — local script + optional Azure DevOps Pipelines |
| EFCore.BulkExtensions for import batch writes | ❌ Evaluated and **skipped** — throws on SQLite for mixed insert+update batches; existing dictionary-preload + `SaveChangesAsync` approach kept. See "Bulk-write evaluation" below |
| FluentValidation for import skip tracking | ✅ Done — `ImportEngineerReviewsAsync`/`ImportPackageAsync` now record structured `ImportSkipReason`s instead of silently swallowing exceptions |
| bUnit component tests | ✅ Done — new `tests/EngineeringPerformance.UI.Tests` project, 8 tests (`ScoringPage` × 5, `EmployeeMetricsExtension` × 3), all passing |

### Grid standardization audit (this branch)

Re-checked every `.razor` page not already using QuickGrid for hand-rolled tabular rendering
(`PeerInsights.razor`, `Reports.razor`, `EmployeeDetail.razor`, `EmployeeMetricsExtension.razor`).
None of them are QuickGrid candidates:
- `PeerInsights.razor` renders a peer-by-peer cross-tab matrix (`@foreach` inside `@foreach`), not
  a row-per-record table — QuickGrid's row/column model doesn't fit a matrix.
- `Reports.razor`'s only `@foreach` populates an `<select>` dropdown of employees, not a table.
- `EmployeeDetail.razor`'s "Monthly ledger" is a capped (`Take(6)`), single-employee `<ul>` of
  evidence cards, not tabular data.
- `EmployeeMetricsExtension.razor` renders metric ribbons/tiles and chart containers, no rows at
  all.

No refactor performed — forcing QuickGrid onto any of these would be a worse fit than what's there
today. `DataBrowserPage.razor` and `Timesheets.razor` remain the only genuine tables, and both were
already on QuickGrid before this branch. `PeopleWorkspace.razor`'s roster list stays on
`<Virtualize>` (card-style list, not tabular; its allocation issue was already fixed).

### Bulk-write evaluation (this branch)

`EFCore.BulkExtensions.Sqlite` 10.0.1 (matching this repo's EF Core 10.0.10) was added and tried
against `ImportEmployeeRosterAsync`'s upsert — the cleanest candidate, since it's already a
straightforward preload-then-upsert-by-key loop. `BulkInsertOrUpdateAsync` throws `SQLite Error 19:
'UNIQUE constraint failed: employee.EmployeeCode'` whenever a single batch mixes a brand-new row
with an update to an existing row (confirmed with a dedicated test,
`RosterImportInsertsAndUpdatesEmployees`, before reverting) — the SQLite adapter emits a plain bulk
`INSERT` rather than a real merge for that shape, so it silently corrupts exactly the case this
method needs every time the roster has both new hires and existing employees in one file. Reverted
in favor of the existing dictionary-preload + `SaveChangesAsync` approach, which was already
correct and reasonably efficient for roster-sized batches (typically hundreds of rows, not
millions). Package reference removed; not adopted anywhere in the codebase.
