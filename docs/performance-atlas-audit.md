# EOS Performance Atlas — visual acceptance matrix

This document is the screenshot and interaction acceptance checklist for the Performance Atlas redesign. It complements `theme-validation.md`.

## Product hierarchy

EOS routes now use three distinct interaction models:

- **Analytical instruments:** Overview / Team performance, Employee portrait, Peer Insights.
- **Operational workbenches:** Timesheets, Data imports, Imported data, Employees & teams, Reports, Review templates.
- **Configuration benches:** Scoring and Settings.

The application fails the visual review if these routes collapse back into the same repeated card-grid composition.

## Overview / Team performance

At 1920×1080, capture the complete first analytical viewport in Graphite or Sandstone and in Amethyst Night.

Verify:

- Team Pulse reads as one uninterrupted instrument, not five cards.
- Team score is the first visual anchor.
- Previous-month score is visible but subordinate.
- Every engineer is represented as a score-distribution dot.
- readiness is encoded as signal lights.
- alert pressure is a narrow signal rather than a KPI card.
- Workforce Performance Field is the dominant visualization.
- selected engineer has a visible halo / movement emphasis.
- missing monthly utilization is rendered as a hollow/missing-source mark, not a measured zero.
- Attention Lens contains five diagnostic portraits, not generic alert cards.
- Movement River shows the selected engineer strongly, attention people moderately and the rest as context.
- Operational Fingerprint uses aligned horizontal comparison; no radar chart is present.
- the Overview employee table, alert wall, Top Quartile card and Needs Attention card are not in the primary composition.
- drill-down links reach the correct evidence workspaces.

## Employee portrait

Capture one high-performing and one attention-ranked engineer.

Verify:

- identity/current score forms the left anchor.
- operational fingerprint reads without a radar chart.
- weekly attendance rhythm is visible when weekly rows exist.
- performance history is the dominant central evidence plot.
- alert/exception evidence is concise and aligned on the right.
- work mix, peer signal and recent monthly ledger form the lower evidence band.
- the page reads as one person-specific data portrait, not a reusable dashboard grid.

## Timesheet control ledger

Verify:

- pulse is continuous and compact.
- row height is comfortable at 100% Windows scaling.
- missing-day exceptions have a leading semantic signal.
- there is no large dead region caused by an artificially short table.
- identity/team remains easy to scan while numeric columns remain aligned.
- normal rows are not coloured merely because data exists.

## Imported evidence worksheet

Capture the worksheet at the leftmost position and after horizontal scroll.

Verify:

- the group band distinguishes identity, calculated performance, capacity/allocation, activity, attendance/reconciliation, exceptions and source coverage.
- Engineer remains sticky.
- source gaps remain em dashes rather than zeros.
- numeric columns do not have a box around every cell.
- group boundaries remain visible after horizontal scrolling.
- calculated columns are distinct without repeated bright orange fills.

## Employees & teams

Verify both read mode and one row in edit mode.

- normal rows look like employee records, not permanent configuration forms.
- controls appear only on the row being edited.
- Portrait is the primary analytical action.
- edit/remove are visually subordinate.
- Add employee / Create team forms are collapsed until requested.
- team structure can be expanded without dominating the roster.

## Peer Insights / Collaboration Atlas

Verify:

- peer pulse is concise.
- reviewer × colleague matrix is the primary relationship view.
- missing relationship and self-review cells are distinct.
- matrix cells are keyboard-focusable when interactive.
- standings provide ranking context without becoming a second dashboard.
- clicking a person opens their employee portrait.
- written comments are evidence below the relationship model, not the primary visual.

## Reports

Verify:

- team and individual output are presented as two clear export lanes.
- the team report is visibly the normal primary path.
- output facts are visible without card clutter.
- workbook generation behavior is unchanged.

## Review templates

Verify:

- normal team-round generation is the dominant workflow.
- one-person generation reads as an exception/reissue path.
- workbook contract and return path are visible below the workflow.
- generated workbook behavior is unchanged.

## Data imports

Verify:

- workflow state reads left-to-right.
- import source table is the primary work surface.
- validation/activity is subordinate.
- source state is visible without large status-filled cards.

## Scoring

Verify:

- operational score configuration remains the dominant editable configuration.
- current weighting is visible beside it without competing equally.
- review-weight preview is clearly secondary and still labelled as preview-only.
- save/recalculate behavior is unchanged.

## Settings

Verify Graphite, Sandstone, Amethyst Night, Forest Night, Solar Night and both High Contrast themes.

- no production theme reports a text-contrast failure.
- focus diagnostic is at least 3:1.
- theme preview approximates the resolved theme closely enough to make a selection decision.
- Comfortable and Compact density remain meaningfully different without reducing operational text below 10.5 px.
- Follow Windows reacts while the application is open.

## Required screenshot set for each major review

1. Overview — Graphite or Sandstone — 1920×1080.
2. Overview — Amethyst Night — 1920×1080.
3. Overview — light theme — 1366×768.
4. Timesheets — 1920×1080.
5. Imported data — left edge.
6. Imported data — horizontally scrolled.
7. Employees — read mode.
8. Employees — one row in edit mode.
9. Peer Insights with review data.
10. Employee portrait for one attention-ranked engineer.
11. Data imports.
12. Reports.
13. Review templates.
14. Scoring.
15. Settings — Recommended themes.

## Local technical validation

Run from the repository root on Windows:

```powershell
node --check src/EngineeringPerformance.UI/wwwroot/theme.js
node --check src/EngineeringPerformance.UI/wwwroot/theme-audit.js
node --check src/EngineeringPerformance.UI/wwwroot/charts.js
node --check src/EngineeringPerformance.UI/wwwroot/analytics-charts.js
node --check src/EngineeringPerformance.UI/wwwroot/atlas-charts.js

dotnet restore EngineeringPerformance.slnx
dotnet build EngineeringPerformance.slnx -c Release --no-restore
dotnet test EngineeringPerformance.slnx -c Release --no-build
```

With developer tools attached to the running application, run:

```js
epaThemeAudit.run()
epaThemeAudit.allPalettes()
```

A visual audit is not complete until the technical validation and the screenshot matrix have both been reviewed.
