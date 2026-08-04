# Engineering Performance Analyzer

Windows-first local performance-analysis software built around three system-generated Excel reports and configurable engineer review workbooks.

## Working application

- .NET 10 WPF desktop host with Blazor Hybrid UI
- SQLite persistence through EF Core
- ClosedXML workbook adapter
- Numeric seniority and configurable weighted scoring
- Individual and ZIP import for the three supplied system reports
- Column-signature report detection and grouped attendance-row cleaning
- Monthly employee KPI aggregation across utilization, detailed work, attendance, leave and punch data
- Numeric seniority, weighted scoring and personalized review-template generation
- Dashboard performance table and immediate SQLite recalculation
- Unit, SQLite and real-reference-workbook integration tests

## Normal use

Launch **Engineering Performance Analyzer** from the desktop shortcut. The published application is self-contained and does not require a terminal.

The packaged executable is under `dist/Engineering Performance Analyzer/`.

The application opens on the **last completed month**, which is the month the newest ERP exports describe. Confirm the reporting month in the title bar before importing.

### Which ERP export goes where

ERP exports keep their system-generated names, so the **Data imports** screen lists the expected export beside every input. Detection uses workbook columns rather than file names, so a renamed file still lands in the right slot.

| Input | ERP export | File name |
| --- | --- | --- |
| Monthly utilization summary | RP-wise Timesheet Utilization Report | `RPwiseTimesheetUtilazationReport<dd-MMM-yyyy>_<hh_mm_ss>.xlsx` |
| Detailed timesheet | Timesheet Manager/Head Report | `LV_Timesheet_ManagerHead_Rpt.xlsx` |
| Attendance and leave | Leave Summary for RP | `LV_LeaveSummaryforRP.xlsx` |
| Engineer reviews | Generated on the Templates screen | `<Code>_<Name>_<yyyy_MM>_Review.xlsx` |

The file name carries the **export date**, not the reporting month: the sample `…04-Aug-2026…` utilization export contains July 2026 data. The workbooks in `Reference Excel Files/` are the July 2026 exports.

### Screens

- **Overview** — six KPI tiles, score distribution and attendance exceptions, a workload-vs-utilization scatter with quadrants, a category-performance radar comparing team average against top and bottom quartile, top quartile and needs-attention lists, attendance-vs-timesheet reconciliation with variance bars, derived alerts by severity, and the full engineer table. A month-over-month score heatmap, trend line and next-month forecast appear once a second month is imported.
- **Data imports** — the four inputs, their expected ERP exports, upload/replace actions and validation.
- **Employees** — employee master; names and seniority levels are editable per row, and each person can be included in or excluded from the analysis.

### Analysis rules

Scores combine timesheet completion (55), approval (15) and attendance discipline (30). A component with no source is **left out of the weighting rather than scored zero** — an engineer absent from the monthly utilization export is ranked on attendance alone and shows `—` for timesheet and approval, instead of being pushed to the bottom by missing data.

Names are normalized before matching, because the exports spell the same person both `Dhruv Varachhiya` and `Dhruv  Varachhiya`.

`Dhruv Varachhiya` and `Snehil Bhadkamkar` are excluded from the analysis by default. Exclusions are stored in the database and editable on the Employees screen; they are seeded only on a database that has no exclusion table yet, so re-including someone sticks.
- **Templates** — one workbook per engineer for the whole team in a single action, or a single personalized workbook.

### Peer review

Each generated workbook has three sheets: **Self Review**, **Peer Review**, and hidden metadata.

The Peer Review sheet is pre-filled with the rest of the team, one row per colleague, so a reviewer only enters ratings and the codes coming back match the employee master exactly. Four dimensions are rated 1–5 — collaboration, communication, reliability, technical help — with a free-text comment. Cells are validated to 1–5; a row left blank is not counted as feedback.

Collect the completed workbooks and upload them into the **Engineer reviews** input on Data imports. That slot accepts a single workbook or a ZIP of them, and re-importing replaces the month's peer feedback rather than duplicating it. The Peer review card on Overview then shows feedback volume, reviewer count, average rating and the peer standings.
- **Scoring** — category weights.

## Developer run

```powershell
dotnet restore
dotnet run --project src/EngineeringPerformance.DesktopHost
```

## Verify

```powershell
dotnet build -c Release
dotnet test -c Release --no-build
```

Application data is stored under `%LOCALAPPDATA%\EngineeringPerformance`.

## Solution layout

- `Domain` — entities, value objects and scoring rules
- `Application` — use cases and ports
- `Infrastructure` — SQLite, ZIP, files and Excel
- `UI` — Blazor pages and components
- `DesktopHost` — WPF shell and composition root
- `tests` — domain and integration verification
