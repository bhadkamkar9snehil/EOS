# EOS V2 — Engineering Operating System

Command center for SSM engineering management: projects and combined Gantt, engineers and capacity,
skills and training, what-if analysis of moving engineers or skills between projects, resource-movement
tracking, infrastructure (servers/VMs per project), reports and alerts. Runs as an always-on server with a
web UI, REST API, CLI (`eos`) and MCP.

EOS V1 (performance reviews) lives in the separate repository `ssm/EOS` and stays untouched.

## Screenshots

Light on the left, dark on the right.

### Command Center
Team heat-map, hours, peer reviews, alerts and drives in one view.

| Light | Dark |
|---|---|
| ![Command Center light](docs/img/command-center-light.png) | ![Command Center dark](docs/img/command-center-dark.png) |

### Projects
Portfolio list with status, dates and risk.

| Light | Dark |
|---|---|
| ![Projects light](docs/img/projects-light.png) | ![Projects dark](docs/img/projects-dark.png) |

### Gantt
Combined Gantt by project or engineer, critical path, baseline and links.

| Light | Dark |
|---|---|
| ![Gantt light](docs/img/gantt-light.png) | ![Gantt dark](docs/img/gantt-dark.png) |

### People
Engineers, roles, load and skills.

| Light | Dark |
|---|---|
| ![People light](docs/img/people-light.png) | ![People dark](docs/img/people-dark.png) |

### Capacity
Supply versus demand per engineer and month.

| Light | Dark |
|---|---|
| ![Capacity light](docs/img/capacity-light.png) | ![Capacity dark](docs/img/capacity-dark.png) |

### Skills
Skills matrix; progression is recorded manually.

| Light | Dark |
|---|---|
| ![Skills light](docs/img/skills-light.png) | ![Skills dark](docs/img/skills-dark.png) |

### What-if
Move engineers or skills between projects and see the effect.

| Light | Dark |
|---|---|
| ![What-if light](docs/img/whatif-light.png) | ![What-if dark](docs/img/whatif-dark.png) |

### Infrastructure
Servers and VMs per project.

| Light | Dark |
|---|---|
| ![Infrastructure light](docs/img/infrastructure-light.png) | ![Infrastructure dark](docs/img/infrastructure-dark.png) |

### Reports
Status packs and exports.

| Light | Dark |
|---|---|
| ![Reports light](docs/img/reports-light.png) | ![Reports dark](docs/img/reports-dark.png) |

### Settings
Hours, calendars, thresholds and vocabularies, all configurable.

| Light | Dark |
|---|---|
| ![Settings light](docs/img/settings-light.png) | ![Settings dark](docs/img/settings-dark.png) |

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
  EOS.Infrastructure/  use-cases, persistence (SQLite), importers, backup
  EOS.Server/          ASP.NET Core host (REST, Blazor UI, MCP)
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
- CLI supports local commands and API requests (`eos api <method> <route>`); legacy V1 data can be migrated with `eos import legacy`.
- Not yet: MeridianPlan UI embedding and report extras. Direct MS Project import is out of scope.

## Hosting
Target: Ubuntu VM `ubuntusb`, systemd unit `eos`, port 8095, LAN + Tailscale only (spec §25). Files and steps: `deploy/README.md`.

## Versioning
`main` = latest. One commit per iteration (`area: change`). Annotated tags `vX.Y.Z`.
