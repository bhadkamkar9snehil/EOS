using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure;

public sealed class PerformanceDbContext(DbContextOptions<PerformanceDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<ReportingMonth> ReportingMonths => Set<ReportingMonth>();
    public DbSet<ImportedSourceFile> ImportedSourceFiles => Set<ImportedSourceFile>();
    public DbSet<EmployeeMonthlyPerformance> EmployeeMonthlyPerformances => Set<EmployeeMonthlyPerformance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employee");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EmployeeCode).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.EmployeeCode).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
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
