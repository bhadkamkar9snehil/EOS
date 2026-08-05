using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure;

public sealed class PerformanceDbContext(DbContextOptions<PerformanceDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<ReportingMonth> ReportingMonths => Set<ReportingMonth>();
    public DbSet<ImportedSourceFile> ImportedSourceFiles => Set<ImportedSourceFile>();
    public DbSet<EmployeeMonthlyPerformance> EmployeeMonthlyPerformances => Set<EmployeeMonthlyPerformance>();
    public DbSet<PeerReview> PeerReviews => Set<PeerReview>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<AnalysisExclusion> AnalysisExclusions => Set<AnalysisExclusion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employee");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EmployeeCode).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.EmployeeCode).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320);
            // The column predates the roster/override split and keeps its original name.
            entity.Property(x => x.IsOnProbationFromRoster).HasColumnName("IsOnProbation");
            entity.Ignore(x => x.IsOnProbation);
        });
        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("team");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });
        modelBuilder.Entity<ReportingMonth>(entity =>
        {
            entity.ToTable("reporting_month");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Year, x.Month }).IsUnique();
        });
        modelBuilder.Entity<ImportedSourceFile>(entity =>
        {
            entity.ToTable("imported_source_file");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.StoredPath).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => new { x.Year, x.Month, x.ReportType }).IsUnique();
        });
        modelBuilder.Entity<PeerReview>(entity =>
        {
            entity.ToTable("peer_review");
            entity.HasKey(x => x.Id);
            entity.Ignore(x => x.Average);
            entity.Ignore(x => x.HasAnyRating);
            entity.Property(x => x.ReviewerCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ReviewerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SubjectCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SubjectName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Comment).HasMaxLength(2000);
            entity.HasIndex(x => new { x.Year, x.Month, x.ReviewerCode, x.SubjectCode }).IsUnique();
        });
        modelBuilder.Entity<AnalysisExclusion>(entity =>
        {
            entity.ToTable("analysis_exclusion");
            entity.HasKey(x => x.EmployeeName);
            entity.Property(x => x.EmployeeName).HasMaxLength(200);
        });
        modelBuilder.Entity<EmployeeMonthlyPerformance>(entity =>
        {
            entity.ToTable("employee_monthly_performance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EmployeeName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.EmployeeCode).HasMaxLength(50);
            entity.HasIndex(x => new { x.Year, x.Month, x.EmployeeName }).IsUnique();
        });
    }
}
