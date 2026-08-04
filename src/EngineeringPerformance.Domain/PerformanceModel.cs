namespace EngineeringPerformance.Domain;

public sealed class Employee
{
    private Employee() { }

    public Employee(string employeeCode, string name, int seniorityLevel)
    {
        EmployeeCode = RequireText(employeeCode, nameof(employeeCode));
        Name = RequireText(name, nameof(name));
        SetSeniorityLevel(seniorityLevel);
    }

    public int Id { get; private set; }
    public string EmployeeCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int SeniorityLevel { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Email { get; private set; }
    /// <summary>True for an externally contracted consultant rather than a direct employee. From the ERP roster.</summary>
    public bool IsConsultant { get; private set; }
    /// <summary>True while still on probation — the app's definition of "fresher". From the ERP roster.</summary>
    public bool IsOnProbation { get; private set; }
    /// <summary>Manually classified: excluded from billable-capacity tracking (200 h/month baseline).</summary>
    public bool IsNonBillable { get; private set; }
    public int? TeamId { get; private set; }

    public void Rename(string name) => Name = RequireText(name, nameof(name));

    public void SetSeniorityLevel(int level)
    {
        if (level < 1 || level > 99)
            throw new ArgumentOutOfRangeException(nameof(level), "Seniority level must be between 1 and 99.");
        SeniorityLevel = level;
    }

    /// <summary>Applies facts the ERP roster is authoritative for — synced on every roster import, not user-editable.</summary>
    public void SyncRosterFacts(string? email, bool isConsultant, bool isOnProbation)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        IsConsultant = isConsultant;
        IsOnProbation = isOnProbation;
    }

    public void SetNonBillable(bool value) => IsNonBillable = value;
    public void AssignTeam(int? teamId) => TeamId = teamId;

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}

/// <summary>A group of engineers, e.g. by project or reporting line. Purely organizational — scoring is unaffected.</summary>
public sealed class Team
{
    private Team() { }
    public Team(string name) => Name = RequireText(name);
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public void Rename(string name) => Name = RequireText(name);
    private static string RequireText(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A team name is required.", nameof(value)) : value.Trim();
}

/// <summary>
/// Person names arrive from the ERP exports with inconsistent spacing — the same
/// engineer appears as "Dhruv Varachhiya" and "Dhruv  Varachhiya". Every name is
/// collapsed through here so identity, joins and exclusions match.
/// </summary>
public static class PersonName
{
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool Matches(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}

public sealed class ReportingMonth
{
    private ReportingMonth() { }
    public ReportingMonth(int year, int month)
    {
        if (year is < 2000 or > 2200) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        Year = year;
        Month = month;
    }
    public int Id { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public DateTime CreatedUtc { get; private set; } = DateTime.UtcNow;
}

public enum ReportType { MonthlyTimesheetSummary = 1, DetailedTimesheetTransactions = 2, AttendanceLeaveUaaTimesheet = 3, EngineerReviewWorkbook = 4 }
public enum SourceStatus { NotUploaded = 0, Uploaded = 1, Warnings = 2, BlockingErrors = 3, Superseded = 4 }

public sealed class ImportedSourceFile
{
    private ImportedSourceFile() { }
    public ImportedSourceFile(ReportType reportType, int year, int month, string originalFileName, string storedPath, int sheetCount)
    {
        ReportType = reportType; Year = year; Month = month; OriginalFileName = originalFileName;
        StoredPath = storedPath; SheetCount = sheetCount; ImportedUtc = DateTime.UtcNow;
    }
    public int Id { get; private set; }
    public ReportType ReportType { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredPath { get; private set; } = string.Empty;
    public int SheetCount { get; private set; }
    public DateTime ImportedUtc { get; private set; }
}

public sealed class EmployeeMonthlyPerformance
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public decimal ComplianceHours { get; set; }
    public decimal EnteredHours { get; set; }
    public decimal ApprovedHours { get; set; }
    public decimal BillableHours { get; set; }
    public decimal NonBillableHours { get; set; }
    public decimal TrainingHours { get; set; }
    public decimal OfficeHours { get; set; }
    public decimal DetailedHours { get; set; }
    public int DetailedEntries { get; set; }
    public int UniqueProjects { get; set; }
    public decimal AttendanceDays { get; set; }
    public decimal LeaveDays { get; set; }
    public decimal PunchHours { get; set; }
    public decimal AttendanceTimesheetHours { get; set; }
    public int TimesheetFilledDays { get; set; }
    public int ExpectedTimesheetDays { get; set; }
    public int MissingPunchDays { get; set; }
    public int LateDays { get; set; }
    public int EarlyDays { get; set; }
    public int LessDurationDays { get; set; }
    public decimal TimesheetCompletionScore { get; set; }
    public decimal ApprovalScore { get; set; }
    public decimal AttendanceDisciplineScore { get; set; }
    public decimal OperationalScore { get; set; }

    /// <summary>
    /// Recomputes every derived score from the raw hours and day counts.
    /// Components without a source are left out of the weighting rather than
    /// scored zero, so an engineer missing from the utilization export is not
    /// penalised for data the import never supplied.
    /// </summary>
    public void Recalculate()
    {
        TimesheetCompletionScore = Percentage(EnteredHours, ComplianceHours);
        ApprovalScore = Percentage(ApprovedHours, EnteredHours);
        AttendanceDisciplineScore = 0;
        if (ExpectedTimesheetDays > 0)
        {
            var fill = Percentage(TimesheetFilledDays, ExpectedTimesheetDays);
            var punch = 100m - Percentage(MissingPunchDays, ExpectedTimesheetDays);
            var duration = 100m - Percentage(LessDurationDays, ExpectedTimesheetDays);
            var punctuality = 100m - Percentage(LateDays + EarlyDays, ExpectedTimesheetDays * 2m);
            AttendanceDisciplineScore = decimal.Round(fill * .40m + punch * .25m + duration * .20m + punctuality * .15m, 2);
        }

        MetricInput[] metrics =
        [
            new("timesheet", TimesheetCompletionScore, .55m, ComplianceHours > 0),
            new("approval", ApprovalScore, .15m, EnteredHours > 0),
            new("attendance", AttendanceDisciplineScore, .30m, ExpectedTimesheetDays > 0)
        ];
        OperationalScore = metrics.Any(x => x.IsApplicable) ? WeightedScoreCalculator.Calculate(metrics) : 0;
    }

    /// <summary>True when the monthly utilization export supplied this engineer's capacity.</summary>
    public bool HasSummaryData => ComplianceHours > 0;

    private static decimal Percentage(decimal value, decimal denominator) =>
        denominator <= 0 ? 0 : Math.Clamp(decimal.Round(value / denominator * 100m, 2), 0, 100);
}

/// <summary>
/// One engineer's rating of a colleague for a month, read back from the peer
/// review sheet of a generated review workbook.
/// </summary>
public sealed class PeerReview
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string ReviewerCode { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public decimal Collaboration { get; set; }
    public decimal Communication { get; set; }
    public decimal Reliability { get; set; }
    public decimal TechnicalHelp { get; set; }
    public string? Comment { get; set; }

    /// <summary>Mean of the rated dimensions; dimensions left blank do not count.</summary>
    public decimal Average
    {
        get
        {
            decimal[] rated = [Collaboration, Communication, Reliability, TechnicalHelp];
            var given = rated.Where(x => x > 0).ToArray();
            return given.Length == 0 ? 0 : decimal.Round(given.Average(), 2);
        }
    }

    public bool HasAnyRating => Collaboration > 0 || Communication > 0 || Reliability > 0 || TechnicalHelp > 0;
}

public sealed record MetricInput(string Code, decimal Score, decimal Weight, bool IsApplicable = true);

public static class WeightedScoreCalculator
{
    public static decimal Calculate(IEnumerable<MetricInput> inputs)
    {
        var applicable = inputs.Where(x => x.IsApplicable).ToArray();
        if (applicable.Length == 0) throw new InvalidOperationException("At least one applicable metric is required.");
        if (applicable.Any(x => x.Score is < 0 or > 100)) throw new ArgumentOutOfRangeException(nameof(inputs), "Scores must be between 0 and 100.");
        if (applicable.Any(x => x.Weight < 0)) throw new ArgumentOutOfRangeException(nameof(inputs), "Weights cannot be negative.");
        var totalWeight = applicable.Sum(x => x.Weight);
        if (totalWeight <= 0) throw new InvalidOperationException("Applicable weights must total more than zero.");
        return decimal.Round(applicable.Sum(x => x.Score * x.Weight) / totalWeight, 2, MidpointRounding.AwayFromZero);
    }
}
