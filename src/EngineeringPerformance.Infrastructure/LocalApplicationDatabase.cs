using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace EngineeringPerformance.Infrastructure;

public sealed class LocalApplicationDatabase(IDbContextFactory<PerformanceDbContext> contextFactory, IWorkbookService workbookService) : IApplicationDatabase
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS imported_source_file (
                Id INTEGER NOT NULL CONSTRAINT PK_imported_source_file PRIMARY KEY AUTOINCREMENT,
                ReportType INTEGER NOT NULL,
                Year INTEGER NOT NULL,
                Month INTEGER NOT NULL,
                OriginalFileName TEXT NOT NULL,
                StoredPath TEXT NOT NULL,
                SheetCount INTEGER NOT NULL,
                ImportedUtc TEXT NOT NULL
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_imported_source_file_Year_Month_ReportType
            ON imported_source_file (Year, Month, ReportType);
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS employee_monthly_performance (
                Id INTEGER NOT NULL CONSTRAINT PK_employee_monthly_performance PRIMARY KEY AUTOINCREMENT,
                Year INTEGER NOT NULL, Month INTEGER NOT NULL, EmployeeName TEXT NOT NULL, EmployeeCode TEXT NULL,
                ComplianceHours TEXT NOT NULL DEFAULT '0', EnteredHours TEXT NOT NULL DEFAULT '0', ApprovedHours TEXT NOT NULL DEFAULT '0',
                BillableHours TEXT NOT NULL DEFAULT '0', NonBillableHours TEXT NOT NULL DEFAULT '0', TrainingHours TEXT NOT NULL DEFAULT '0',
                OfficeHours TEXT NOT NULL DEFAULT '0', DetailedHours TEXT NOT NULL DEFAULT '0', DetailedEntries INTEGER NOT NULL DEFAULT 0,
                UniqueProjects INTEGER NOT NULL DEFAULT 0, AttendanceDays TEXT NOT NULL DEFAULT '0', LeaveDays TEXT NOT NULL DEFAULT '0',
                PunchHours TEXT NOT NULL DEFAULT '0', AttendanceTimesheetHours TEXT NOT NULL DEFAULT '0', TimesheetFilledDays INTEGER NOT NULL DEFAULT 0,
                ExpectedTimesheetDays INTEGER NOT NULL DEFAULT 0, MissingPunchDays INTEGER NOT NULL DEFAULT 0, LateDays INTEGER NOT NULL DEFAULT 0,
                EarlyDays INTEGER NOT NULL DEFAULT 0, LessDurationDays INTEGER NOT NULL DEFAULT 0, TimesheetCompletionScore TEXT NOT NULL DEFAULT '0',
                ApprovalScore TEXT NOT NULL DEFAULT '0', AttendanceDisciplineScore TEXT NOT NULL DEFAULT '0', OperationalScore TEXT NOT NULL DEFAULT '0'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_employee_monthly_performance_Year_Month_EmployeeName
            ON employee_monthly_performance (Year, Month, EmployeeName);
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS analysis_exclusion (
                EmployeeName TEXT NOT NULL CONSTRAINT PK_analysis_exclusion PRIMARY KEY,
                CreatedUtc TEXT NOT NULL
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS peer_review (
                Id INTEGER NOT NULL CONSTRAINT PK_peer_review PRIMARY KEY AUTOINCREMENT,
                Year INTEGER NOT NULL, Month INTEGER NOT NULL,
                ReviewerCode TEXT NOT NULL, ReviewerName TEXT NOT NULL,
                SubjectCode TEXT NOT NULL, SubjectName TEXT NOT NULL,
                Collaboration TEXT NOT NULL DEFAULT '0', Communication TEXT NOT NULL DEFAULT '0',
                Reliability TEXT NOT NULL DEFAULT '0', TechnicalHelp TEXT NOT NULL DEFAULT '0',
                Comment TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_peer_review_Year_Month_Reviewer_Subject
            ON peer_review (Year, Month, ReviewerCode, SubjectCode);
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS team (
                Id INTEGER NOT NULL CONSTRAINT PK_team PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_team_Name ON team (Name);
            """, cancellationToken);

        // The employee table predates these columns; EnsureCreatedAsync only creates a
        // table that doesn't exist yet, so an existing database needs them added by hand.
        await EnsureColumnAsync(context, "employee", "Email", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "employee", "IsConsultant", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(context, "employee", "IsOnProbation", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(context, "employee", "IsNonBillable", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(context, "employee", "TeamId", "INTEGER NULL", cancellationToken);

        await SeedDefaultExclusionsAsync(context, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
    }

    /// <summary>
    /// Adds a column to an existing table if it is missing. SQLite cannot parameterize DDL
    /// identifiers, so these are interpolated — safe because every caller passes a compile-time
    /// literal, never user input.
    /// </summary>
#pragma warning disable EF1002 // Fixed internal literals; no user input reaches this SQL.
    private static async Task EnsureColumnAsync(PerformanceDbContext context, string table, string column, string definition, CancellationToken cancellationToken)
    {
        var existing = await context.Database.SqlQueryRaw<string>($"SELECT name AS Value FROM pragma_table_info('{table}')").ToListAsync(cancellationToken);
        if (existing.Contains(column, StringComparer.OrdinalIgnoreCase)) return;
        await context.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition};", cancellationToken);
    }
#pragma warning restore EF1002

    /// <summary>
    /// Names that are never part of the analysis. Seeded once, on a database that has
    /// no exclusion table yet, so a name the user later re-includes stays re-included.
    /// </summary>
    private static readonly string[] DefaultExclusions = ["Dhruv Varachhiya", "Snehil Bhadkamkar"];

    private static async Task SeedDefaultExclusionsAsync(PerformanceDbContext context, CancellationToken cancellationToken)
    {
        var seeded = await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM analysis_exclusion").SingleAsync(cancellationToken);
        if (seeded > 0) return;
        foreach (var name in DefaultExclusions)
            await context.Database.ExecuteSqlRawAsync(
                "INSERT OR IGNORE INTO analysis_exclusion (EmployeeName, CreatedUtc) VALUES ({0}, {1})",
                [name, DateTime.UtcNow.ToString("O")], cancellationToken);
    }

    private static async Task<HashSet<string>> ReadExclusionsAsync(PerformanceDbContext context, CancellationToken cancellationToken)
    {
        var names = await context.Database.SqlQueryRaw<string>("SELECT EmployeeName AS Value FROM analysis_exclusion").ToListAsync(cancellationToken);
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
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (excluded)
            await context.Database.ExecuteSqlRawAsync(
                "INSERT OR IGNORE INTO analysis_exclusion (EmployeeName, CreatedUtc) VALUES ({0}, {1})",
                [employeeName.Trim(), DateTime.UtcNow.ToString("O")], cancellationToken);
        else
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM analysis_exclusion WHERE EmployeeName = {0} COLLATE NOCASE", [employeeName.Trim()], cancellationToken);
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
            .Select(x => new { x.Id, x.EmployeeCode, x.Name, x.SeniorityLevel, x.Email, x.IsConsultant, x.IsOnProbation, x.IsNonBillable, x.TeamId })
            .ToListAsync(cancellationToken);
        return employees.Select(x => new EmployeeListItem(
            x.Id, x.EmployeeCode, x.Name, x.SeniorityLevel, excluded.Contains(PersonName.Normalize(x.Name)),
            x.Email, x.IsConsultant, x.IsOnProbation, x.IsNonBillable,
            x.TeamId, x.TeamId.HasValue && teams.TryGetValue(x.TeamId.Value, out var teamName) ? teamName : null)).ToArray();
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
        var roster = workbookService.ReadEmployeeRoster(filePath);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var changed = 0;
        foreach (var entry in roster)
        {
            var existing = await context.Employees.SingleOrDefaultAsync(x => x.EmployeeCode == entry.EmployeeCode, cancellationToken);
            if (existing is null)
            {
                var created = new Employee(entry.EmployeeCode, entry.Name, entry.SeniorityLevel);
                created.SyncRosterFacts(entry.Email, entry.IsConsultant, entry.IsOnProbation);
                context.Employees.Add(created);
                changed++;
            }
            else
            {
                var before = (existing.SeniorityLevel, existing.Email, existing.IsConsultant, existing.IsOnProbation);
                existing.SetSeniorityLevel(entry.SeniorityLevel);
                existing.SyncRosterFacts(entry.Email, entry.IsConsultant, entry.IsOnProbation);
                if (before != (existing.SeniorityLevel, existing.Email, existing.IsConsultant, existing.IsOnProbation)) changed++;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
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
        var previous = await context.ImportedSourceFiles.SingleOrDefaultAsync(x => x.Year == year && x.Month == month && x.ReportType == reportType, cancellationToken);
        if (previous is not null) context.ImportedSourceFiles.Remove(previous);
        context.ImportedSourceFiles.Add(new ImportedSourceFile(reportType, year, month, inspection.FileName, storedPath, inspection.SheetNames.Count));
        // A multi-month export yields one row per employee per month, so the same employee code
        // recurs many times; codes queued earlier in this batch aren't visible to a database
        // query yet, so they're tracked here to avoid inserting a duplicate employee.
        var queuedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var incoming in performance)
        {
            // Detailed timesheet and attendance rows carry their own real date, which can span many
            // months in one export; each row lands in its own actual month, never the UI's selected one.
            var rowYear = incoming.Year;
            var rowMonth = incoming.Month;
            var current = await context.EmployeeMonthlyPerformances.SingleOrDefaultAsync(x => x.Year == rowYear && x.Month == rowMonth && x.EmployeeName == incoming.EmployeeName, cancellationToken);
            if (current is null) { current = new EmployeeMonthlyPerformance { Year = rowYear, Month = rowMonth, EmployeeName = incoming.EmployeeName }; context.EmployeeMonthlyPerformances.Add(current); }
            Merge(current, incoming, reportType);
            if (reportType == ReportType.AttendanceLeaveUaaTimesheet && !string.IsNullOrWhiteSpace(incoming.EmployeeCode)
                && queuedCodes.Add(incoming.EmployeeCode))
            {
                var exists = await context.Employees.AnyAsync(x => x.EmployeeCode == incoming.EmployeeCode, cancellationToken);
                if (!exists) context.Employees.Add(new Employee(incoming.EmployeeCode, incoming.EmployeeName, 1));
            }
        }
        await context.SaveChangesAsync(cancellationToken);
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
            var accepted = 0;
            foreach (var workbook in workbooks)
            {
                IReadOnlyList<PeerReview> fromFile;
                try { fromFile = workbookService.ReadPeerReviews(workbook, year, month); }
                catch (Exception) { continue; }   // not a review workbook, or unreadable
                if (fromFile.Count == 0) continue;
                reviews.AddRange(fromFile);
                accepted++;
            }

            if (accepted == 0)
                throw new InvalidDataException("No completed peer review sheets were found. Generate templates on the Templates screen, have engineers fill the Peer Review sheet, then upload the workbooks or a ZIP of them.");

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
            foreach (var file in Directory.EnumerateFiles(temporaryDirectory, "*.xls*", SearchOption.AllDirectories))
            {
                ReportType type;
                try { type = workbookService.DetectReportType(file); } catch (InvalidDataException) { continue; }
                await ImportSourceAsync(type, year, month, file, cancellationToken);
                imported++;
            }
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
        var rows = await context.EmployeeMonthlyPerformances
            .Where(x => x.Year * 100 + x.Month >= oldest.Year * 100 + oldest.Month
                     && x.Year * 100 + x.Month <= newest.Year * 100 + newest.Month)
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.EmployeeName).ToListAsync(cancellationToken);
        return rows.Where(x => !excluded.Contains(PersonName.Normalize(x.EmployeeName))).Select(Project).ToArray();
    }

    private static MonthlyPerformanceItem Project(EmployeeMonthlyPerformance x) => new(
        x.EmployeeName, x.EmployeeCode, x.OperationalScore, x.TimesheetCompletionScore,
        x.ApprovalScore, x.AttendanceDisciplineScore, x.EnteredHours, x.ComplianceHours, x.BillableHours, x.DetailedHours,
        x.DetailedEntries, x.UniqueProjects, x.AttendanceDays, x.LeaveDays, x.MissingPunchDays, x.LateDays, x.EarlyDays, x.LessDurationDays,
        x.Year, x.Month, x.PunchHours, x.AttendanceTimesheetHours, x.TimesheetFilledDays, x.ExpectedTimesheetDays,
        x.NonBillableHours, x.TrainingHours, x.ApprovedHours);

    private static void Merge(EmployeeMonthlyPerformance current, EmployeeMonthlyPerformance incoming, ReportType type)
    {
        if (!string.IsNullOrWhiteSpace(incoming.EmployeeCode)) current.EmployeeCode = incoming.EmployeeCode;
        if (type == ReportType.MonthlyTimesheetSummary)
        {
            current.ComplianceHours = incoming.ComplianceHours; current.EnteredHours = incoming.EnteredHours; current.ApprovedHours = incoming.ApprovedHours;
            current.BillableHours = incoming.BillableHours; current.NonBillableHours = incoming.NonBillableHours; current.TrainingHours = incoming.TrainingHours; current.OfficeHours = incoming.OfficeHours;
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
