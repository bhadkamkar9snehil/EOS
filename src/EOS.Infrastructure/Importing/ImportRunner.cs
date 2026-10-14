using EOS.Domain.Audit;
using EOS.Infrastructure.Persistence;

namespace EOS.Infrastructure.Importing;

/// <summary>
/// Shared shell for importers: one transaction, an audit record, commit or rollback (preview), and a clean change tracker
/// after a preview. Spec §24.1: preview first, idempotent, provenance recorded.
/// </summary>
internal static class ImportRunner
{
    public static async Task<ImportReport> RunAsync(
        EosDbContext db, string kind, string path, bool commit, string actor, Func<ImportReport, Task> work, CancellationToken ct)
    {
        var report = new ImportReport(kind, path, commit);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await work(report);
        db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = DateTime.UtcNow, Actor = actor, Action = commit ? "import.commit" : "import.preview",
            SubjectKind = kind, SubjectId = System.IO.Path.GetFileName(path),
            Detail = string.Join("; ", report.Counts.Select(c => $"{c.Key} +{c.Value.Added}/~{c.Value.Updated}/={c.Value.Unchanged}")),
        });
        await db.SaveChangesAsync(ct);
        if (commit && kind == "task tracker workbook")
        {
            // Imported plans have no dates until scheduled; schedule every project that has tasks so Gantt, load and health work at once.
            await new Planning.PlanService(db).RecomputeAllAsync(null, ct);
        }
        if (commit) await tx.CommitAsync(ct);
        else { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); }
        return report;
    }
}
