# Workbook delta: SSM_Operational_Agility_21102026.xlsx vs EOS V2

Source: `Documents\Copy of SSM_Operational_Agility_21102026.xlsx` (33 sheets). Compared with `docs/SPEC.md` and the code on 21 Oct 2026.
Rule used: adopt only what the workbook shows that V2 does not already do; every adoption is a setting, a seed or one small feature, never a new subsystem.

## What the workbook is
The Excel prototype grew from v2 (14 Oct) into a working model: `Settings` as the single rule sheet, per-project `WBS_*` sheets, `Calc_*` engine sheets, `WhatIf_Moves` + `Gantt` with Original vs With-moves rows, and a hours-based `Training_Log` / `Training_Plan` / `Skill_Current` chain. Projects, demands, allocations and WBS are still SAMPLE filler; names, teams and skill ratings are real.

## Already in V2 (nothing to do)
| Workbook | V2 |
|---|---|
| WBS tasks, predecessors, units, slack, Critical?, late finish | `Scheduler` (float, `IsCritical`, FS/SS/FF/SF links) |
| Move a person, "With moves" finish, change in working days | `WhatIf.Run`, `Leave`, `Join`, `ScenarioOutcome` |
| Calc_Suggest: who can be released, cost to source, gain to target | `WhatIf.ReleaseAnalysis` |
| "Would stop a task, nobody else is on it" | setting `LostAllEngineersRule` |
| Settings: hours/day, working days, holidays, target utilisation, resource/deployable min level, top level | `SettingCatalog` + Calendars |
| Demands (Filled / Partially / Gap-Escalated / Open), Change_Gap_Log, Resource_Allocation, Upcoming with probability and required skills | SPEC §12, §13, §17, domain entities, `AgilityWorkbookImporter` (Engineers, Skills, Skill_Matrix) |
| Skill_Summary resources / deployable / competency % | SPEC §9.4, dashboards |

## Worth adopting (small, in this order)
1. **Real efficiency factors as the "realistic" profile seed.** Workbook: productivity by skill level 1..5 = 0.40, 0.55, 0.70, 0.85, 1.00 (level 5 and 3 are the manager's numbers, the rest are assumptions); experience factor by years 0 / 2 / 5 / 10 = 0.85 / 0.92 / 0.97 / 1.00; efficiency = product. `CrewMember.Factor` and `scheduling.factorProfile` already exist, so this is seed data plus a function that fills `Factor`. SPEC §9.6 "Realistic" uses different numbers (0.60 / ramp-up); replace them with these, keep ramp-up as an optional second factor. Default profile stays "compat" so SPEC §15.8 golden tests keep passing.
2. **Training hours as evidence and capacity only.** A `TrainingHours` log (person, skill, date, hours, topic) and planned training hours per engineer-month that reduce free capacity. DECIDED 21 Oct (owner): no projected level-up date, no hours-per-level rule, no auto-level. Ratings are manual (AGENTS.md rule 2, SPEC §9.5).
3. **Per-task "movers can join" flag.** Workbook has Y/N per task; V2 `Join` uses one default rule (SPEC §15 step 4 says an explicit task list is the override). Add the flag as that override on the plan task. Nothing else.
4. **Plan check on a project plan.** Workbook column "Plan check" validates a task row (predecessor missing or listed below, no crew, skill below demand). V2 has load alerts but verify a per-task check list exists before building; if not, a single validation method on the plan service.
5. **Training hours in free capacity.** `Free = Available - Planned - Training` per engineer-month (Engineers sheet). Add the training term once item 2 lands.

## Not adopting
- 6-day week, weekly-off code 11, per-sheet holiday codes: already expressible as working days + calendars.
- Auto-levelling from training hours (see item 2).
- `Calc_*` sheets, rank-key helpers, `99999` filler rows, 24-week fixed columns: Excel workarounds the engine replaces.
- Gantt Original/With-moves row pairs: V2 Gantt already has a baseline ghost.
- Required skill % (`Skill Set Required %`): still blank in the workbook; keep as the planned `RequiredSkillLevel`, do not seed.

## Data to refresh (import, not code)
Skill ratings, assessment date (15 Mar 2026 dummy) and teams in `Engineers` / `Skill_Matrix` may have changed since 14 Oct. Run the existing agility import on the new file; re-import never overwrites a rating a person changed manually. `Skill_Transactions` (rating history) has two real-looking rows (TX001, TX002) and could feed the rating-change ledger.

## Open questions
- Q1 Are the productivity numbers final, or still the manager's assumption for levels 1, 2, 4?
- Q2 ~~Projected level-up date~~ Resolved 21 Oct: not wanted.
- Q3 Import `Skill_Transactions` as history, or ignore until real data exists?
