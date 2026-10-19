# EOS V2 end-to-end functional test plan

Status: proposed, not yet built. Nothing here is guessed through screenshots or computer use: every check is a script that passes or fails.

Out of scope by owner decision (19 Oct 2026): direct MS Project import. Plans come from the workbook importers or are authored in the system.

## Principles
1. Tests drive the real running server through its real interfaces, never through screenshots or coordinate clicks.
2. Every test starts from a known database and ends with an assertion on stored data, not on what a page looks like.
3. A test that cannot fail is not a test: each suite includes a deliberately broken-case check.
4. One command runs everything and prints pass/fail per suite. CI on the self-hosted GitLab runner runs the same command.

## Layers
| # | Layer | Tool | What it proves |
|---|---|---|---|
| L1 | Engine and rules (exists) | xUnit, golden tests from spec 15.8 and 28.1 | Scheduler, what-if and capacity maths |
| L2 | Use-case and persistence | xUnit over EOS.Application and EOS.Infrastructure with a temp SQLite file | Every create, edit, delete, approve, import and backup path writes the right rows |
| L3 | HTTP contract | xUnit with WebApplicationFactory against the real EOS.Server host | Every REST endpoint under /api/v1, auth modes, validation errors, MCP tool list and calls |
| L4 | UI behaviour | Playwright (.NET) driving headless Chromium against the test host | Every page loads without console errors; every button, form, grid edit, drag and toast does what the page promises; Blazor circuit survives navigation |
| L5 | Cross-feature journeys | Playwright plus L3 for setup and assertions | The real owner workflows, end to end (below) |

L4 selects by role, label and a stable `data-testid` that we add to controls. No pixel coordinates. A failed step saves the page HTML, console log and a screenshot as evidence for a human, never as the assertion.

## Test data
- Seed builder (code, not a file): 6 engineers, 3 projects with WBS and dependencies, skills and ratings, 1 VM per project, one over-allocated engineer, one late project. Deterministic, built in under a second.
- A copy of the real database is used only for read-only smoke checks (every page loads, numbers reconcile), never for tests that write.
- Reconciliation checks (this found real bugs on 19 Oct): the same number shown on two pages must match. Slip on Command Center equals project finish minus plan on the planner. Over-allocated count equals the engineers flagged in the planner and Capacity page.

## Coverage matrix (one row per page; each needs happy path, validation error, empty state, persistence after reload)
Command Center, Projects, Project detail (WBS edit, assignments, dependencies, baseline, health override), Combined Gantt, Planner (add engineer, drag, relay, commit, reset, undo, draft restore), Capacity, Calendar (month, team, year), What-if (move engineer, move skill, analyse, approve, reject, apply), Demand and pipeline, People and person detail, Skills (manual rating only, no automation), Training, Infrastructure, Alerts, Reports (CSV and Excel content), Status pack, Register, Imports (preview then commit, bad file, credential columns rejected), Settings (cascade Global > Team > Person > Project > Scenario, calendars, vocabularies, custom fields, aliases, tokens, backups, audit).

## Cross-cutting checks run on every page
- No console errors, no failed network calls, no unhandled Blazor exception banner.
- Server restart while a page is open: the page shows a clear reconnect message and recovers (today it goes silently dead).
- Toasts clear themselves.
- Keyboard: every grid and form is operable with Tab, Enter, Esc.
- Layout: no horizontal page overflow at 1920, 1366 and 390 wide; Gantt and planner fill their container.
- Nothing hard-coded: changing an hours or threshold setting changes results.
- Credentials: no import or API response ever returns a credential column.

## Journeys (L5)
1. New project from scratch: create, build WBS, assign engineers, schedule, appears on combined Gantt with correct finish.
2. Import then extend: import the agility workbook, add tasks, recompute, baseline.
3. Resource crunch: over-allocate an engineer, see the alert, resolve it in the planner, alert clears.
4. What-if: move an engineer, analyse, check slip on all projects, reject (no change) then approve and apply (plans change, audit entry written).
5. Skill gap: set a requirement, find the best-fit bench, record a manual rating, gap closes.
6. Weekly status pack: update progress, generate the pack, check the numbers against the data.
7. Backup and restore: backup, change data, restore, data returns.

## Build order
1. Seed builder and test host fixture (L2 and L3 share it).
2. L3 contract tests for all endpoints, plus the reconciliation checks.
3. Playwright setup, `data-testid` pass over the UI, page smoke suite (loads, no console errors).
4. Per-page interaction suites, worst-covered pages first (Planner, What-if, Project detail).
5. Journeys.
6. GitLab pipeline job on the Windows runner running the full command, failing the pipeline on any red test.

## Definition of done
All suites green from a clean checkout with one command; every row of the coverage matrix has at least one passing test; the two known data mismatches are fixed with a regression test each.

## Decisions needed from the owner
- Playwright needs Chromium downloaded once (about 150 MB) on this PC and on the Windows runner. Acceptable?
- Reconnect behaviour on server restart: auto-reload the page, or show a banner with a Reload button?
