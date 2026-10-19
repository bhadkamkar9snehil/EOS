# EOS V2 — Engineering Operating System

Command center for SSM engineering management: projects and combined Gantt, engineers and capacity,
skills and training, what-if analysis of moving engineers or skills between projects, resource-movement
tracking, infrastructure (servers/VMs per project), reports and alerts. Runs as an always-on server with a
web UI, REST API, CLI (`eos`) and MCP.

EOS V1 (performance reviews) lives in the separate repository `ssm/EOS` and stays untouched.

## Read first
- [`docs/SPEC.md`](docs/SPEC.md) — the master specification (what, why, formulas, data, reports, roadmap). **It is the source of truth.**
- [`AGENTS.md`](AGENTS.md) — working agreements for humans and agents.

## Principles (from the spec, §4)
Everything is **configurable** (hours, calendars, thresholds, scales, vocabularies). Plans (WBS/tasks) can be
**authored here or imported then extended**. **Skill progression is manual.** Everything is available through
UI, API, CLI and MCP. No secrets are ever stored.

## Layout
```
src/
  EOS.Domain/          entities, enumerations, settings model (pure)
  EOS.Scheduling/      scheduling + what-if engine (pure, no I/O)
  EOS.Application/     use-cases and interfaces
  EOS.Infrastructure/  persistence (SQLite), importers, backup
  EOS.Server/          ASP.NET Core host (REST, later Blazor UI, MCP)
  EOS.Cli/             `eos` command line
tests/                 xUnit (golden scheduling tests from spec §15.8 / §28.1)
third_party/meridianplan/   MIT-licensed Gantt/scheduler reference (see NOTICE.eos)
docs/SPEC.md
```

## Build and test
```powershell
dotnet build EOS.slnx
dotnet test EOS.slnx
.\run-tests.ps1
dotnet run --project src\EOS.Server
dotnet run --project src\EOS.Cli -- version
```

## What works today
- Web UI (Blazor Server): Command Center, Projects (editable WBS, assignments, dependencies, baseline), Combined Gantt, Capacity & Load, What-if workbench, Demand & Pipeline, People, Skills matrix (manual ratings only), Training, Infrastructure, Alerts, Reports (CSV/Excel), Imports (preview then commit), Settings (settings, calendars, vocabularies, custom fields, aliases, tokens, backups, audit).
- REST API at `/api/v1` (catalogue at `/openapi/v1.json`), MCP at `/mcp`, CLI `eos` (local mode: `eos help`).
- Importers for the agility workbook, training tracker, ERP master, task tracker and VM inventory; no credentials are ever read.
- Not yet: CLI as API client, V1 data migration, MS Project import, MeridianPlan UI embedding, report extras.

## Hosting
Target: Ubuntu VM `ubuntusb`, systemd unit `eos`, port 8095, LAN + Tailscale only (spec §25). Files and steps: `deploy/README.md`.

## Versioning
`main` = latest. One commit per iteration (`area: change`). Annotated tags `vX.Y.Z`.
