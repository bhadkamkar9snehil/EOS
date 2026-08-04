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

- **Overview** — team KPIs, score distribution, top performers, engineers needing attention, attendance exceptions and the full engineer table.
- **Data imports** — the four inputs, their expected ERP exports, upload/replace actions and validation.
- **Employees** — employee master; names and seniority levels are editable per row.
- **Templates** — one workbook per engineer for the whole team in a single action, or a single personalized workbook.
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
