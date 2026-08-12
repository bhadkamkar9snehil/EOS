using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;

namespace EngineeringPerformance.Infrastructure;

public sealed class LocalApplicationDatabase(
    IDbContextFactory<PerformanceDbContext> contextFactory,
    IWorkbookService workbookService,
    ILogger<LocalApplicationDatabase>? logger = null) : IApplicationDatabase
{
    private readonly ILogger<LocalApplicationDatabase> _logger = logger ?? NullLogger<LocalApplicationDatabase>.Instance;
    private readonly WorkbookInspectionValidator _inspectionValidator = new();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await BaselineExistingDatabaseAsync(context, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        await SeedDefaultExclusionsAsync(context, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
    }

    /// <summary>
    /// A database built by the pre-migrations code (raw CREATE TABLE / ALTER TABLE calls) already
    /// has every table and column the InitialBaseline migration would create — so running that
    /// migration's Up() against it would fail on "table already exists". Detected by the absence
    /// of the migrations history table alongside the presence of the old "employee" table, this
    /// marks InitialBaseline as already applied without executing it, exactly the documented
    /// approach for adopting migrations on an existing database. A genuinely fresh install has
    /// neither table yet, so MigrateAsync proceeds normally and creates everything from scratch.
    /// </summary>
    private static async Task BaselineExistingDatabaseAsync(PerformanceDbContext context, CancellationToken cancellationToken)
    {
        var historyExists = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'").SingleAsync(cancellationToken);
        if (historyExists > 0) return;

        var employeeTableExists = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='employee'").SingleAsync(cancellationToken);
        if (employeeTableExists == 0) return;

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """, cancellationToken);
        var baselineMigrationId = context.Database.GetMigrations().Single();
        await context.Database.ExecuteSqlRawAsync(
            """INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({0}, {1})""",
            [baselineMigrationId, "10.0.10"], cancellationToken);
    }

    /// <summary>
    /// Names that are never part of the analysis. Seeded once, on a database that has
    /// no exclusion table yet, so a name the user later re-includes stays re-included.
    /// </summary>
    private static readonly string[] DefaultExclusions = ["Dhruv Varachhiya", "Snehil Bhadkamkar"];

    private static async Task SeedDefaultExclusionsAsync(PerformanceDbContext context, CancellationToken cancellationToken)
    {
        if (await context.AnalysisExclusions.AnyAsync(cancellationToken)) return;
        foreach (var name in DefaultExclusions)
            context.AnalysisExclusions.Add(new AnalysisExclusion(name));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> ReadExclusionsAsync(PerformanceDbContext context, CancellationToken cancellationToken)
    {
        var names = await context.AnalysisExclusions.Select(x => x.EmployeeName).ToListAsync(cancellationToken);
        // Compared on normalized names: the exports spell the same person with varying spacing.
        return new HashSet<string>(names.Select(PersonName.Normalize), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> GetExcludedNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await ReadExclusionsAsync(context, cancellationToken)).OrderBy(x => x).ToArray();
    }

    public async Task SetExclusionAsync(string employeeName, bool excluded, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeName)) return;
        var trimmed = employeeName.Trim();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.AnalysisExclusions.SingleOrDefaultAsync(
            x => x.EmployeeName.ToLower() == trimmed.ToLower(), cancellationToken);
        if (excluded)
        {
            if (existing is null) context.AnalysisExclusions.Add(new AnalysisExclusion(trimmed));
        }
        else if (existing is not null)
        {
            context.AnalysisExclusions.Remove(existing);
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var activeEmployees = await context.Employees.CountAsync(x => x.IsActive, cancellationToken);
        var selected = new DateTime(year ?? DateTime.Today.Year, month ?? DateTime.Today.Month, 1);
        var files = await context.ImportedSourceFiles.Where(x => x.Year == selected.Year && x.Month == selected.Month).ToListAsync(cancellationToken);
        var slots = Enum.GetValues<ReportType>().Select(type =>
        {
            var file = files.SingleOrDefault(x => x.ReportType == type);
            return new SourceSlot(type, file is null ? SourceStatus.NotUploaded : SourceStatus.Uploaded, file?.OriginalFileName);
        }).ToArray();
        var excluded = await ReadExclusionsAsync(context, cancellationToken);
        var scored = await context.EmployeeMonthlyPerformances
            .Where(x => x.Year == selected.Year && x.Month == selected.Month && x.OperationalScore > 0)
            .Select(x => new { x.EmployeeName, x.OperationalScore }).ToListAsync(cancellationToken);
        var scores = scored.Where(x => !excluded.Contains(PersonName.Normalize(x.EmployeeName))).Select(x => x.OperationalScore).ToList();
        decimal? overall = scores.Count == 0 ? null : decimal.Round(scores.Average(), 2);
        return new DashboardSnapshot($"{selected:MMMM yyyy}", activeEmployees, slots, slots.Count(x => x.Status == SourceStatus.NotUploaded), overall);
    }

    public async Task<IReadOnlyList<EmployeeListItem>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var excluded = await ReadExclusionsAsync(context, cancellationToken);
        var teams = await context.Teams.ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var employees = await context.Employees.OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.EmployeeCode, x.Name, x.SeniorityLevel, x.Email, x.IsConsultant, x.IsOnProbationFromRoster, x.ProbationOverride, x.IsUpdownFromRoster, x.UpdownOverride, x.IsNonBillable, x.TeamId })
            .ToListAsync(cancellationToken);
        return employees.Select(x => new EmployeeListItem(
            x.Id, x.EmployeeCode, x.Name, x.SeniorityLevel, excluded.Contains(PersonName.Normalize(x.Name)),
            // Consultants are always non-billable — a fact about the engagement, not a per-person
            // preference — so it's forced here regardless of the manual flag.
            x.Email, x.IsConsultant, x.ProbationOverride ?? x.IsOnProbationFromRoster, x.ProbationOverride, x.IsConsultant || x.IsNonBillable,
            x.TeamId, x.TeamId.HasValue && teams.TryGetValue(x.TeamId.Value, out var teamName) ? teamName : null,
            x.UpdownOverride ?? x.IsUpdownFromRoster, x.UpdownOverride)).ToArray();
    }

    public async Task SetProbationAsync(int employeeId, bool? value, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await context.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null) return;
        employee.SetProbationOverride(value);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetUpdownAsync(int employeeId, bool? value, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await context.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null) return;
        employee.SetUpdownOverride(value);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetNonBillableAsync(int employeeId, bool value, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await context.Employees.FindAsync([employeeId], cancellationToken)
            ?? throw new InvalidOperationException("The employee no longer exists.");
        employee.SetNonBillable(value);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignTeamAsync(int employeeId, int? teamId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await context.Employees.FindAsync([employeeId], cancellationToken)
            ?? throw new InvalidOperationException("The employee no longer exists.");
        employee.AssignTeam(teamId);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ImportHistoryItem>> GetImportHistoryAsync(int? year = null, int? month = null, int take = 200, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.ImportAuditEntries.AsQueryable();
        if (year is not null && month is not null) query = query.Where(x => x.Year == year && x.Month == month);
        var rows = await query.OrderByDescending(x => x.ImportedUtc).ThenByDescending(x => x.Id).Take(take).ToListAsync(cancellationToken);
        return rows.Select(x => new ImportHistoryItem(
            x.Id, x.ReportType, x.Year, x.Month, x.OriginalFileName, x.RowCount, x.ReplacedExisting, x.ImportedUtc)).ToArray();
    }

    public async Task<IReadOnlyList<TeamItem>> GetTeamsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var counts = await context.Employees.Where(x => x.TeamId != null)
            .GroupBy(x => x.TeamId!.Value).Select(g => new { TeamId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.TeamId, x => x.Count, cancellationToken);
        var teams = await context.Teams.OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return teams.Select(x => new TeamItem(x.Id, x.Name, counts.GetValueOrDefault(x.Id))).ToArray();
    }

    public async Task<int> AddTeamAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var team = new Team(name);
        context.Teams.Add(team);
        await context.SaveChangesAsync(cancellationToken);
        return team.Id;
    }

    public async Task RenameTeamAsync(int teamId, string name, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var team = await context.Teams.FindAsync([teamId], cancellationToken) ?? throw new InvalidOperationException("The team no longer exists.");
        team.Rename(name);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteTeamAsync(int teamId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var team = await context.Teams.FindAsync([teamId], cancellationToken);
        if (team is null) return;
        var members = await context.Employees.Where(x => x.TeamId == teamId).ToListAsync(cancellationToken);
        foreach (var member in members) member.AssignTeam(null);
        context.Teams.Remove(team);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddEmployeeAsync(string employeeCode, string name, int seniorityLevel, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Employees.Add(new Employee(employeeCode, name, seniorityLevel));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateEmployeeAsync(int employeeId, string name, int seniorityLevel, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await context.Employees.FindAsync([employeeId], cancellationToken)
            ?? throw new InvalidOperationException("The employee no longer exists.");
        employee.Rename(name);
        employee.SetSeniorityLevel(seniorityLevel);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await context.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null) return;
        context.Employees.Remove(employee);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ImportEmployeeRosterAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // NOTE: EFCore.BulkExtensions' SQLite adapter was evaluated here (BulkInsertOrUpdateAsync)
        // and rejected — see tests/EngineeringPerformance.Infrastructure.Tests/DatabaseTests.cs's
        // RosterImportBulkInsertsAndUpdatesEmployees comment / docs/tailwind-grid-ci-plan.md. A
        // batch mixing a fresh insert with an update to an existing row throws a UNIQUE constraint
        // violation on SQLite (it emits a plain bulk INSERT rather than a true merge/upsert for
        // that mix), so the existing dictionary-preload + per-entity SaveChangesAsync approach is
        // kept as-is: it already solved the N+1 read problem, and SaveChangesAsync's batched write
        // is correct where the bulk-upsert path was not.
        var roster = workbookService.ReadEmployeeRoster(filePath);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var employeesByCode = await context.Employees.ToDictionaryAsync(x => x.EmployeeCode, x => x, cancellationToken);
        var changed = 0;
        foreach (var entry in roster)
        {
            var existing = employeesByCode.GetValueOrDefault(entry.EmployeeCode);
            if (existing is null)
            {
                var created = new Employee(entry.EmployeeCode, entry.Name, entry.SeniorityLevel);
                created.SyncRosterFacts(entry.Email, entry.IsConsultant, entry.IsOnProbation, entry.IsUpdown);
                context.Employees.Add(created);
                employeesByCode[entry.EmployeeCode] = created;
                changed++;
            }
            else
            {
                var before = (existing.SeniorityLevel, existing.Email, existing.IsConsultant, existing.IsOnProbationFromRoster);
                existing.SetSeniorityLevel(entry.SeniorityLevel);
                existing.SyncRosterFacts(entry.Email, entry.IsConsultant, entry.IsOnProbation, entry.IsUpdown);
                if (before != (existing.SeniorityLevel, existing.Email, existing.IsConsultant, existing.IsOnProbationFromRoster)) changed++;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Imported employee roster from {FilePath}: {ChangedCount} of {TotalCount} rows changed.", filePath, changed, roster.Count);
        return changed;
    }

    public async Task ImportSourceAsync(ReportType reportType, int year, int month, string sourcePath, CancellationToken cancellationToken = default)
    {
        var inspection = workbookService.Inspect(sourcePath);
        var detectedType = workbookService.DetectReportType(sourcePath);
        if (detectedType != reportType) throw new InvalidDataException($"This file is {detectedType}, not {reportType}.");
        var performance = workbookService.ReadPerformance(sourcePath, reportType, year, month);
        var importDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngineeringPerformance", "Imports", $"{year:D4}-{month:D2}");
        Directory.CreateDirectory(importDirectory);
        var storedPath = Path.Combine(importDirectory, $"{(int)reportType}-{Path.GetFileName(sourcePath)}");
        File.Copy(sourcePath, storedPath, true);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // A multi-month export (attendance/detailed timesheet) covers every month it contains data
        // for, not just the UI's currently selected month — recording only the selected month would
        // make the readiness indicator wrong for the other months once weekly re-uploads start.
        var coveredMonths = performance.Select(x => (Year: x.Year, Month: x.Month)).Distinct().DefaultIfEmpty((Year: year, Month: month)).ToArray();
        var existingSourceFiles = await context.ImportedSourceFiles
            .Where(x => x.ReportType == reportType && coveredMonths.Select(m => m.Year).Contains(x.Year))
            .ToDictionaryAsync(x => (x.Year, x.Month), x => x, cancellationToken);
        foreach (var (coveredYear, coveredMonth) in coveredMonths)
        {
            var previous = existingSourceFiles.GetValueOrDefault((coveredYear, coveredMonth));
            if (previous is not null) context.ImportedSourceFiles.Remove(previous);
            context.ImportedSourceFiles.Add(new ImportedSourceFile(reportType, coveredYear, coveredMonth, inspection.FileName, storedPath, inspection.SheetNames.Count));
            // The slot row above is replaced on every re-upload; this log line is not, so a daily
            // upload routine leaves a reviewable trail of what landed when.
            var rowsForMonth = performance.Count(x => x.Year == coveredYear && x.Month == coveredMonth);
            context.ImportAuditEntries.Add(new ImportAuditEntry(
                reportType, coveredYear, coveredMonth, inspection.FileName, rowsForMonth, previous is not null));
        }

        var coveredYears = coveredMonths.Select(m => m.Year).Distinct().ToArray();
        var existingPerformance = await context.EmployeeMonthlyPerformances
            .Where(x => coveredYears.Contains(x.Year))
            .ToDictionaryAsync(x => (x.Year, x.Month, x.EmployeeName), x => x, cancellationToken);
        var existingEmployeeCodes = new HashSet<string>(
            await context.Employees.Select(x => x.EmployeeCode).ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        // A multi-month export yields one row per employee per month, so the same employee code
        // recurs many times; codes queued earlier in this batch aren't visible to a database
        // query yet, so they're tracked here to avoid inserting a duplicate employee.
        var queuedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newRows = 0;
        var updatedRows = 0;
        foreach (var incoming in performance)
        {
            // Detailed timesheet and attendance rows carry their own real date, which can span many
            // months in one export; each row lands in its own actual month, never the UI's selected one.
            var rowYear = incoming.Year;
            var rowMonth = incoming.Month;
            var key = (rowYear, rowMonth, incoming.EmployeeName);
            if (!existingPerformance.TryGetValue(key, out var current))
            {
                current = new EmployeeMonthlyPerformance { Year = rowYear, Month = rowMonth, EmployeeName = incoming.EmployeeName };
                context.EmployeeMonthlyPerformances.Add(current);
                existingPerformance[key] = current;
                newRows++;
            }
            else
            {
                updatedRows++;
            }
            Merge(current, incoming, reportType);
            if (reportType == ReportType.AttendanceLeaveUaaTimesheet && !string.IsNullOrWhiteSpace(incoming.EmployeeCode)
                && queuedCodes.Add(incoming.EmployeeCode))
            {
                if (existingEmployeeCodes.Add(incoming.EmployeeCode)) context.Employees.Add(new Employee(incoming.EmployeeCode, incoming.EmployeeName, 1));
            }
        }
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Imported {ReportType} for {Year:D4}-{Month:D2} from {FileName}: {NewCount} new rows, {UpdatedCount} updated rows.",
            reportType, year, month, inspection.FileName, newRows, updatedRows);
    }

    public async Task<int> ImportEngineerReviewsAsync(int year, int month, string path, CancellationToken cancellationToken = default)
    {
        var isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var temporaryDirectory = isZip ? Path.Combine(Path.GetTempPath(), $"epa-reviews-{Guid.NewGuid():N}") : null;
        try
        {
            string[] workbooks;
            if (temporaryDirectory is not null)
            {
                Directory.CreateDirectory(temporaryDirectory);
                ZipFile.ExtractToDirectory(path, temporaryDirectory);
                workbooks = [.. Directory.EnumerateFiles(temporaryDirectory, "*.xls*", SearchOption.AllDirectories)
                    .Where(x => !Path.GetFileName(x).StartsWith("~$", StringComparison.Ordinal))];
            }
            else
            {
                workbooks = [path];
            }

            var reviews = new List<PeerReview>();
            var skipped = new List<ImportSkipReason>();
            var accepted = 0;
            foreach (var workbook in workbooks)
            {
                var fileName = Path.GetFileName(workbook);
                try
                {
                    var workbookInspection = workbookService.Inspect(workbook);
                    var inspectionResult = _inspectionValidator.Validate(workbookInspection);
                    if (!inspectionResult.IsValid)
                    {
                        ImportSkipLog.Record(skipped, fileName, string.Join("; ", inspectionResult.Errors.Select(e => e.ErrorMessage)));
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    ImportSkipLog.Record(skipped, fileName, $"Workbook could not be opened: {ex.Message}");
                    continue;
                }

                IReadOnlyList<PeerReview> fromFile;
                try { fromFile = workbookService.ReadPeerReviews(workbook, year, month); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipped {FileName} while importing peer reviews: not a review workbook, or unreadable.", Path.GetFileName(workbook));
                    // Structured skip: this is not a Peer Review sheet, or its shape doesn't parse.
                    ImportSkipLog.Record(skipped, fileName, $"Not a readable Peer Review sheet: {ex.Message}");
                    continue;
                }
                if (fromFile.Count == 0)
                {
                    ImportSkipLog.Record(skipped, fileName, "Peer Review sheet parsed but contained no completed rows.");
                    continue;
                }
                reviews.AddRange(fromFile);
                accepted++;
            }

            if (accepted == 0)
            {
                var detail = skipped.Count > 0
                    ? " Skipped: " + string.Join(" | ", skipped.Select(s => $"{s.FileName} ({s.Reason})"))
                    : string.Empty;
                throw new InvalidDataException("No completed peer review sheets were found. Generate templates on the Templates screen, have engineers fill the Peer Review sheet, then upload the workbooks or a ZIP of them." + detail);
            }

            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var previousRows = await context.PeerReviews.Where(x => x.Year == year && x.Month == month).ToListAsync(cancellationToken);
            context.PeerReviews.RemoveRange(previousRows);
            // Last row wins if the same pair appears twice across workbooks.
            foreach (var review in reviews
                .GroupBy(x => (x.ReviewerCode.ToLowerInvariant(), x.SubjectCode.ToLowerInvariant()))
                .Select(x => x.Last()))
                context.PeerReviews.Add(review);

            var inspection = workbookService.Inspect(path);
            var previousFile = await context.ImportedSourceFiles.SingleOrDefaultAsync(
                x => x.Year == year && x.Month == month && x.ReportType == ReportType.EngineerReviewWorkbook, cancellationToken);
            if (previousFile is not null) context.ImportedSourceFiles.Remove(previousFile);
            var storedDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngineeringPerformance", "Imports", $"{year:D4}-{month:D2}");
            Directory.CreateDirectory(storedDirectory);
            var storedPath = Path.Combine(storedDirectory, $"{(int)ReportType.EngineerReviewWorkbook}-{Path.GetFileName(path)}");
            File.Copy(path, storedPath, true);
            context.ImportedSourceFiles.Add(new ImportedSourceFile(ReportType.EngineerReviewWorkbook, year, month, inspection.FileName, storedPath, accepted));
            context.ImportAuditEntries.Add(new ImportAuditEntry(
                ReportType.EngineerReviewWorkbook, year, month, inspection.FileName, accepted, previousFile is not null));

            await context.SaveChangesAsync(cancellationToken);
            return accepted;
        }
        finally
        {
            if (temporaryDirectory is not null && Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }

    public async Task<IReadOnlyList<PeerReviewItem>> GetPeerReviewsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var excluded = await ReadExclusionsAsync(context, cancellationToken);
        var rows = await context.PeerReviews.Where(x => x.Year == year && x.Month == month).ToListAsync(cancellationToken);
        return rows
            .Where(x => !excluded.Contains(PersonName.Normalize(x.ReviewerName)) && !excluded.Contains(PersonName.Normalize(x.SubjectName)))
            .Select(x => new PeerReviewItem(x.ReviewerName, x.ReviewerCode, x.SubjectName, x.SubjectCode,
                x.Collaboration, x.Communication, x.Reliability, x.TechnicalHelp, x.Average, x.Comment))
            .ToArray();
    }

    public async Task<int> ImportPackageAsync(int year, int month, string zipPath, CancellationToken cancellationToken = default)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"epa-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, temporaryDirectory);
            var imported = 0;
            var skipped = new List<ImportSkipReason>();
            foreach (var file in Directory.EnumerateFiles(temporaryDirectory, "*.xls*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                ReportType type;
                try { type = workbookService.DetectReportType(file); }
                catch (InvalidDataException ex)
                {
                    _logger.LogWarning(ex, "Skipped {FileName} in package {ZipPath}: not a recognized report type.", fileName, zipPath);
                    // Structured skip: file inside the package isn't a recognizable report workbook.
                    ImportSkipLog.Record(skipped, fileName, $"Unrecognized report type: {ex.Message}");
                    continue;
                }
                await ImportSourceAsync(type, year, month, file, cancellationToken);
                imported++;
            }
            _logger.LogInformation("Imported package {ZipPath} for {Year:D4}-{Month:D2}: {ImportedCount} files.", zipPath, year, month, imported);
            return imported;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    public async Task<IReadOnlyList<MonthlyPerformanceItem>> GetMonthlyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var excluded = await ReadExclusionsAsync(context, cancellationToken);
        var rows = await context.EmployeeMonthlyPerformances.Where(x => x.Year == year && x.Month == month)
            .OrderByDescending(x => x.OperationalScore).ThenBy(x => x.EmployeeName).ToListAsync(cancellationToken);
        return rows.Where(x => !excluded.Contains(PersonName.Normalize(x.EmployeeName))).Select(Project).ToArray();
    }

    public async Task<IReadOnlyList<MonthlyPerformanceItem>> GetPerformanceHistoryAsync(int year, int month, int monthsBack, CancellationToken cancellationToken = default)
    {
        var newest = new DateTime(year, month, 1);
        var oldest = newest.AddMonths(-Math.Max(0, monthsBack - 1));
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var excluded = await ReadExclusionsAsync(context, cancellationToken);
        // Sargable range filter (year/month compared directly, no computed expression) so the
        // (Year, Month, EmployeeName) index can be used instead of a full table scan.
        var rows = await context.EmployeeMonthlyPerformances
            .Where(x => (x.Year > oldest.Year || (x.Year == oldest.Year && x.Month >= oldest.Month))
                     && (x.Year < newest.Year || (x.Year == newest.Year && x.Month <= newest.Month)))
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.EmployeeName).ToListAsync(cancellationToken);
        return rows.Where(x => !excluded.Contains(PersonName.Normalize(x.EmployeeName))).Select(Project).ToArray();
    }

    private static MonthlyPerformanceItem Project(EmployeeMonthlyPerformance x) => new(
        x.EmployeeName, x.EmployeeCode, x.OperationalScore, x.TimesheetCompletionScore,
        x.ApprovalScore, x.AttendanceDisciplineScore, x.EnteredHours, x.ComplianceHours, x.BillableHours, x.DetailedHours,
        x.DetailedEntries, x.UniqueProjects, x.AttendanceDays, x.LeaveDays, x.MissingPunchDays, x.LateDays, x.EarlyDays, x.LessDurationDays,
        x.Year, x.Month, x.PunchHours, x.AttendanceTimesheetHours, x.TimesheetFilledDays, x.ExpectedTimesheetDays,
        x.NonBillableHours, x.TrainingHours, x.ApprovedHours, RawUtilization: x.Utilization);

    private static void Merge(EmployeeMonthlyPerformance current, EmployeeMonthlyPerformance incoming, ReportType type)
    {
        if (!string.IsNullOrWhiteSpace(incoming.EmployeeCode)) current.EmployeeCode = incoming.EmployeeCode;
        if (type == ReportType.MonthlyTimesheetSummary)
        {
            current.ComplianceHours = incoming.ComplianceHours; current.EnteredHours = incoming.EnteredHours; current.ApprovedHours = incoming.ApprovedHours;
            current.BillableHours = incoming.BillableHours; current.NonBillableHours = incoming.NonBillableHours; current.TrainingHours = incoming.TrainingHours; current.OfficeHours = incoming.OfficeHours;
            current.Utilization = incoming.Utilization;
        }
        else if (type == ReportType.DetailedTimesheetTransactions)
        { current.DetailedHours = incoming.DetailedHours; current.DetailedEntries = incoming.DetailedEntries; current.UniqueProjects = incoming.UniqueProjects; }
        else if (type == ReportType.AttendanceLeaveUaaTimesheet)
        {
            current.AttendanceDays = incoming.AttendanceDays; current.LeaveDays = incoming.LeaveDays; current.PunchHours = incoming.PunchHours;
            current.AttendanceTimesheetHours = incoming.AttendanceTimesheetHours; current.TimesheetFilledDays = incoming.TimesheetFilledDays;
            current.ExpectedTimesheetDays = incoming.ExpectedTimesheetDays; current.MissingPunchDays = incoming.MissingPunchDays;
            current.LateDays = incoming.LateDays; current.EarlyDays = incoming.EarlyDays; current.LessDurationDays = incoming.LessDurationDays;
        }
        current.Recalculate();
    }
}
