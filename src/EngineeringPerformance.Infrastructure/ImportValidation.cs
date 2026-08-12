using EngineeringPerformance.Application;
using FluentValidation;

namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Structured reason a candidate import file was skipped, replacing the old silent
/// "catch (Exception) { continue; }" pattern. Callers currently surface these via
/// <see cref="System.Diagnostics.Debug.WriteLine(string?)"/> as a placeholder — a later
/// Serilog pass can swap that for real structured logging without changing this shape.
/// </summary>
public sealed record ImportSkipReason(string FileName, string Reason);

/// <summary>
/// Validates that a workbook is at least shaped like something worth reading before the
/// (comparatively expensive) row-level parse is attempted. Intentionally shallow: this checks
/// readability/shape, not business rules — those still live in <see cref="IWorkbookService"/>.
/// </summary>
public sealed class WorkbookInspectionValidator : AbstractValidator<WorkbookInspection>
{
    public WorkbookInspectionValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().WithMessage("Workbook has no file name.");
        RuleFor(x => x.SheetNames).NotEmpty().WithMessage("Workbook has no sheets.");
    }
}

internal static class ImportSkipLog
{
    /// <summary>
    /// TODO(logging): replace with structured Serilog logging once the logging pass lands.
    /// Kept as a single choke point so that swap only touches this method.
    /// </summary>
    public static void Record(List<ImportSkipReason> sink, string fileName, string reason)
    {
        var entry = new ImportSkipReason(fileName, reason);
        sink.Add(entry);
        System.Diagnostics.Debug.WriteLine($"[import-skip] {entry.FileName}: {entry.Reason}");
    }
}
