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
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
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
        var scores = await context.EmployeeMonthlyPerformances.Where(x => x.Year == selected.Year && x.Month == selected.Month && x.OperationalScore > 0).Select(x => x.OperationalScore).ToListAsync(cancellationToken);
        decimal? overall = scores.Count == 0 ? null : decimal.Round(scores.Average(), 2);
        return new DashboardSnapshot($"{selected:MMMM yyyy}", activeEmployees, slots, slots.Count(x => x.Status == SourceStatus.NotUploaded), overall);
    }

    public async Task<IReadOnlyList<EmployeeListItem>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Employees.OrderBy(x => x.Name).Select(x => new EmployeeListItem(x.Id, x.EmployeeCode, x.Name, x.SeniorityLevel)).ToListAsync(cancellationToken);
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
        foreach (var incoming in performance)
        {
            var current = await context.EmployeeMonthlyPerformances.SingleOrDefaultAsync(x => x.Year == year && x.Month == month && x.EmployeeName == incoming.EmployeeName, cancellationToken);
            if (current is null) { current = new EmployeeMonthlyPerformance { Year = year, Month = month, EmployeeName = incoming.EmployeeName }; context.EmployeeMonthlyPerformances.Add(current); }
            Merge(current, incoming, reportType);
            if (reportType == ReportType.AttendanceLeaveUaaTimesheet && !string.IsNullOrWhiteSpace(incoming.EmployeeCode))
            {
                var exists = await context.Employees.AnyAsync(x => x.EmployeeCode == incoming.EmployeeCode, cancellationToken);
                if (!exists) context.Employees.Add(new Employee(incoming.EmployeeCode, incoming.EmployeeName, 1));
            }
        }
        await context.SaveChangesAsync(cancellationToken);
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
        return await context.EmployeeMonthlyPerformances.Where(x => x.Year == year && x.Month == month)
            .OrderByDescending(x => x.OperationalScore).ThenBy(x => x.EmployeeName)
            .Select(x => new MonthlyPerformanceItem(x.EmployeeName, x.EmployeeCode, x.OperationalScore, x.TimesheetCompletionScore,
                x.ApprovalScore, x.AttendanceDisciplineScore, x.EnteredHours, x.ComplianceHours, x.BillableHours, x.DetailedHours,
                x.DetailedEntries, x.UniqueProjects, x.AttendanceDays, x.LeaveDays, x.MissingPunchDays, x.LateDays, x.EarlyDays, x.LessDurationDays))
            .ToListAsync(cancellationToken);
    }

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
        current.TimesheetCompletionScore = current.ComplianceHours <= 0 ? 0 : Math.Clamp(decimal.Round(current.EnteredHours / current.ComplianceHours * 100, 2), 0, 100);
        current.ApprovalScore = current.EnteredHours <= 0 ? 0 : Math.Clamp(decimal.Round(current.ApprovedHours / current.EnteredHours * 100, 2), 0, 100);
        if (current.ExpectedTimesheetDays > 0)
        {
            decimal P(decimal value) => Math.Clamp(decimal.Round(value / current.ExpectedTimesheetDays * 100, 2), 0, 100);
            current.AttendanceDisciplineScore = decimal.Round(P(current.TimesheetFilledDays) * .40m + (100 - P(current.MissingPunchDays)) * .25m + (100 - P(current.LessDurationDays)) * .20m + (100 - P((current.LateDays + current.EarlyDays) / 2m)) * .15m, 2);
        }
        current.OperationalScore = decimal.Round(current.TimesheetCompletionScore * .55m + current.ApprovalScore * .15m + current.AttendanceDisciplineScore * .30m, 2);
    }
}
