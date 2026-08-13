# EOS logging architecture

EOS has **one application-facing logging API** and **one configured logging backend**.

## The rule

Application, infrastructure and UI code log through:

```csharp
Microsoft.Extensions.Logging.ILogger<T>
```

Serilog is an implementation detail of the Windows desktop composition root. It configures sinks,
formatting and retention in `EngineeringPerformance.DesktopHost/EosLogging.cs`, then plugs into the
standard Microsoft logging pipeline through `Serilog.Extensions.Hosting`.

Application code must not:

- import or call `Serilog.Log` / `Serilog.ILogger`
- append its own diagnostic log files with `File.AppendAllText` / `WriteAllText`
- invent a second event-log abstraction for navigation, interactions or analytics
- hard-code the physical rolling-log filename from a Razor component

If an EOS class needs to emit an event, inject `ILogger<T>`.

## Data flow

```text
DesktopHost / Infrastructure / UI
             |
             v
Microsoft.Extensions.Logging ILogger<T>
             |
             v
Serilog provider (DesktopHost only)
             |
             +--> Debug sink
             |
             +--> rolling local file sink
```

The canonical local paths are represented by `LocalApplicationPaths` rather than reconstructed
independently by several layers.

Default log location:

```text
%LOCALAPPDATA%\EngineeringPerformance\logs\eos-YYYYMMDD.log
```

Policy:

- daily rolling files
- 30 retained files
- 25 MB per-file limit, with size rollover
- shared file access so the Diagnostics page can read a live log safely
- source context attached to every event

The retired hand-written `interaction.log` is never written again. If an older installation still
has that file, the diagnostics bundle includes it as `legacy-interaction.log` so historical evidence
is not discarded.

## Levels

Use levels consistently:

| Level | Use in EOS |
|---|---|
| `Trace` | Extremely detailed temporary investigation; normally avoid in committed code. |
| `Debug` | Navigation, refresh lifecycle, layout diagnostics, expected update-check outcomes. |
| `Information` | Meaningful state changes: imports, backups, scoring changes, startup/shutdown, generated diagnostic bundle. |
| `Warning` | Recoverable malformed input, corrupt optional configuration with fallback, skipped source file. |
| `Error` | Failed operation/background load where EOS can continue or surface an error to the user. |
| `Critical` | Process-level startup failure or unhandled exception that may terminate the application. |

Do not promote ordinary user activity to `Information`; high-volume interaction traces belong at
`Debug` so production logs remain useful.

## Structured events

Prefer structured message templates:

```csharp
logger.LogInformation(
    "Imported {ReportType} for {Year:D4}-{Month:D2}: {NewCount} new rows and {UpdatedCount} updated rows.",
    reportType,
    year,
    month,
    newCount,
    updatedCount);
```

Do not interpolate values into the message string. Named properties make future filtering/searching
possible without changing the sink.

For exceptions, pass the exception object to the logging method:

```csharp
logger.LogError(exception, "Weekly performance load failed for {Year:D4}-{Month:D2}.", year, month);
```

## What not to log

EOS operates on employee/workbook evidence. Logs are diagnostics, not a second data store.

Do not log:

- workbook row contents
- peer-review narrative text
- full imported datasets
- credentials, tokens, connection secrets or VM passwords
- arbitrary file contents

Reasonable diagnostic properties include source filename, report type, month, aggregate row counts,
operation duration and exception details.

## Diagnostics UI

`Diagnostics.razor` consumes the application-level `IApplicationDiagnostics` contract.
`LocalApplicationDiagnostics` owns:

- log-file discovery
- safe tail reads of the live rolling file
- database/log path metadata
- diagnostics ZIP creation

This separation is deliberate: the UI should not know that Serilog is the current sink or that its
rolling files use a particular filename convention.

## Process-level exception coverage

`DesktopHost/App.xaml.cs` remains responsible for the process boundaries that ordinary application
code cannot catch:

- WPF dispatcher unhandled exceptions
- `AppDomain.UnhandledException`
- `TaskScheduler.UnobservedTaskException`
- startup failure

Those handlers still write through `ILogger<App>`, so even crash logging follows the same pipeline.

## Composition-root boundary

Direct Serilog references belong only in `EosLogging.cs` (plus project package references needed to
compile that configuration). If another project needs a new sink, add/configure it there rather than
introducing a new logging library or file writer in the consuming project.

This keeps a future backend change cheap: application code remains on the standard Microsoft logging
abstraction while the host can replace Serilog/file sinks without rewriting the rest of EOS.
