# EOS — Data Import Guide

What EOS ingests, exactly which columns it reads, and where the gotchas are. Every claim here was
verified by running the real parser (`WorkbookService`) against the sample files in
[`docs/sample-data/`](sample-data/) — see the generator script referenced at the bottom if you need
to regenerate them.

EOS never enforces the filenames below — report type is detected by scanning the first 10 rows ×
40 columns of the workbook for a signature pair of column headers (see each section). Filenames are
shown in the Data Imports page purely as a hint for which ERP export to run.

---

## 1. Monthly utilization summary

- **Expected filename**: `RPwiseTimesheetUtilazationReport<dd-MMM-yyyy>_<hh_mm_ss>.xlsx`
- **ERP source**: RP-wise Timesheet Utilization Report
- **Sample**: `sample-RPwiseTimesheetUtilazationReport-Jul2026.xlsx`
- **Detected by**: a cell containing `Total Month Hours` and another containing `Utilization`
  anywhere in the first 10 rows.
- **One aggregate row per employee**, for whichever period the ERP was asked to export.

**Required columns** (header row, anywhere in rows 1–10, marked by `Employee Name`):

| Column | Meaning |
|---|---|
| `Employee Name` | Matched against the roster by normalized name. |
| `Timsheet Compliance hours` | Compliance hours (note: this misspelling is the ERP's own header text, not a typo to fix). |
| `Total\nEntered Timesheet Hours` | Entered hours — the header cell has a literal line break. |
| `Approved Timesheet Hours` | Approved hours. |
| `Billable Hours` / `Non Billable Hours` | Split of entered hours. |
| `Sum of Training` / `Sum of Office Working Hours` | Training and office time. |
| `Utilization` | A **fraction**, not a percentage number — the ERP formats the cell as `%`, so `0.76` means 76%. EOS multiplies by 100 on read. |

**Gotcha — single month only**: cell A1 is expected to read `Month` with the period in B1 as
`dd-MMM-yyyy to dd-MMM-yyyy`. If that range spans more than one calendar month, EOS **rejects the
whole file** rather than guessing which month the totals belong to. Re-export one summary per
calendar month from the ERP.

---

## 2. Detailed timesheet

- **Expected filename**: `LV_Timesheet_ManagerHead_Rpt.xlsx`
- **ERP source**: Timesheet Manager/Head Report
- **Sample**: `sample-LV_Timesheet_ManagerHead_Rpt.xlsx`
- **Detected by**: a cell containing `Project No` and another containing `Total work Hours`.
- **One row per work-log entry** (not per employee) — this is a full historical dump. Every row
  carries its own date, so EOS buckets each row into its own (employee, year, month), independent
  of whatever reporting month you selected on import. A single export can span years.

**Required columns** (header row marked by `Employee`):

| Column | Meaning |
|---|---|
| `Employee` | Matched against the roster by normalized name. |
| `Date` | The work date — determines which month this row is bucketed into. |
| `Project No` | Used to count unique projects worked per employee-month. |
| `Total work Hours` | Hours logged on that entry. |

**Optional but important — `Filled Date`**: the timestamp the entry was actually *submitted*, as
opposed to the work date. This is separate from the four columns above and is **not required** for
the core operational score, but the **Timesheets page's "Timesheet filing" section** needs it to
compute filing delay (how many days after the work date the entry was actually filed) — without it,
that section has nothing to show. If your ERP export omits this column, filing delay simply won't
be tracked that month; nothing else breaks.

---

## 3. Attendance and leave

- **Expected filename**: `LV_LeaveSummaryforRP.xlsx`
- **ERP source**: Leave Summary for RP
- **Sample**: `sample-LV_LeaveSummaryforRP.xlsx`
- **Detected by**: a cell containing `Punch Duration` and another containing `UAA Status`.
- **One row per employee per day** — same multi-month bucketing as the detailed timesheet above:
  each row's own `Date` decides which month it lands in.

**Required columns** (header row marked by `Date`):

| Column | Meaning |
|---|---|
| `Employee` | Matched by normalized name. |
| `Emp No` | Employee code — backfills `EmployeeCode` on the performance row if not already set. |
| `Date` | The attendance date. |
| `Attend Day` | Attendance weight for the day (normally `1`, `0.5` on a half day). |
| `Position` / `Duty Type` / `Leave status` | Used together to classify the day — see gotchas below. |
| `Punch Duration` | Raw in/out punch hours. |
| `Timesheet Hrs` | Hours from the linked timesheet, reconciled against punch hours. |
| `Timesheet` | `Filled` marks the day as having a submitted timesheet. |
| `Flg Punch not Found`, `Flg Late Coming`, `Flg Early Going`, `Flg Less Duration` | Boolean exception flags (`TRUE`/`FALSE`, `1`/`0`, or an Excel boolean cell all work). |

**Gotchas — day classification rules** (these are EOS's own rules, not just a pass-through of the
ERP's flags):

- **Sunday is always a week-off**, regardless of what `Duty Type` says. `Duty Type = "WOFF"` also
  marks a week-off on any other day.
- **Saturday counts as a half working day** (0.5× weight) if it's not itself a week-off.
- A week-off day **never** counts as leave taken, even if `Position` or `Leave status` on that same
  row says otherwise — a day nobody could have worked isn't a day of leave. This overrides the raw
  ERP flags, which do occasionally tag Sunday rows as `Position = "Leave"` at the same time as
  `Duty Type = "WOFF"`.
- A day only counts toward attendance/expected-timesheet totals if `Attend Day > 0`, it isn't a
  week-off, and `Position` isn't `"Leave"`.

---

## 4. Employee roster

- **No fixed expected filename** — imported from the Employees & Teams page ("Import ERP roster").
- **Sample**: `sample-EmployeeRoster.xlsx`
- **Detected by**: the presence of `Employee No`, `Full Name`, and `Band Level` columns in row 1
  exactly (no scanning — the header must be the literal first row, unlike the three reports above).
- **One row per employee** — this is a live roster snapshot, not tied to a reporting month.

**Required columns** (row 1):

| Column | Meaning |
|---|---|
| `Employee No` | Employee code — the join key used everywhere else. |
| `Full Name` | Normalized and matched against timesheet/attendance rows by name. |
| `Band Level` | Free text; EOS extracts the first run of digits (`"Level 2"` → `2`). Non-numeric or out-of-range values default to `1`. |

**Optional columns**:

| Column | Meaning |
|---|---|
| `Official Email` | Stored if present. |
| `IsConsultant` | Marks an externally contracted consultant rather than a direct employee. |
| `Flg Probation` + `Probation Complete Date` | See gotcha below. |
| `IsUpdown` | Absent in older roster exports — treated as `false`, not an error, if missing. |

**Gotcha — probation status**: `Flg Probation` alone is unreliable — the ERP frequently leaves it
set long after someone has actually completed probation. EOS instead treats someone as on
probation only if `Flg Probation` is set **and** `Probation Complete Date` is either blank or still
in the future. A stale flag with a past completion date is correctly read as "no longer on
probation."

---

## 5. Engineer review workbook

- **Expected filename**: `<Code>_<Name>_<yyyy_MM>_Review.xlsx`
- **Source**: generated by EOS itself (Reports → "Generate employee workbook", or the bulk
  generator), handed to each engineer, and returned filled in — not an ERP export.
- **Sample**: `sample-2001_Asha_Kapoor_2026_07_Review.xlsx` (a genuine blank template, produced by
  EOS's own generator — no ratings filled in, since that step is manual).
- **Detected by**: the presence of a very-hidden `Template Metadata` worksheet, which carries the
  employee code, name, and reporting year/month the workbook was generated for.

**How it's read**: the visible `Peer Review` sheet (row 6 onward) holds one row per colleague
rated, with columns `Peer Code`, `Peer Name`, `Collaboration (1-5)`, `Communication (1-5)`,
`Reliability (1-5)`, `Technical Help (1-5)`, `Comment`. A row where every rating is blank is treated
as "did not work with this person," not a zero score. Ratings outside 1–5 are also treated as not
given, rather than clamped.

**Gotcha — reporting month must match**: the workbook's `Template Metadata` records the year/month
it was generated for. If you import it against a different reporting month than that, EOS rejects
it outright rather than silently filing it under the wrong period — this is what the "peer reviews
filed under the wrong month" bug (fixed earlier) was actually protecting against once identified.

**Bulk import**: the Data Imports page accepts one workbook, several workbooks selected at once, or
a single ZIP containing many — each reviewer's workbook updates only that reviewer's own entries,
leaving everyone else's data untouched ("safe merge").

---

## Regenerating the sample files

The samples above are not hand-typed spreadsheets — they were generated by a small script that
calls the real `WorkbookService` (for the review template) and hand-builds the other four to the
exact column names the parser expects, then **round-tripped every one of them back through
`WorkbookService.DetectReportType` / `ReadPerformance` / `ReadEmployeeRoster` /
`ReadPeerReviews` / `ReadTimesheetDayEvidence` / `ReadAccountableWorkdays`** to confirm they parse
correctly before being committed. If the parser's expected columns ever change, regenerate rather
than hand-edit: see `WorkbookService.cs` and `WorkbookService.Compliance.cs` in
`src/EngineeringPerformance.Infrastructure/` for the authoritative column list.
