using EOS.Domain.Capacity;
using EOS.Domain.Configuration;
using EOS.Domain.Hosting;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Persistence;

public sealed partial class EosDbContext
{
    private static void OnModelCreatingDelivery(ModelBuilder b)
    {
        b.Entity<VocabularyItem>(e => e.HasIndex(x => new { x.Kind, x.Value }).IsUnique());

        b.Entity<WorkCalendarDef>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.HoursPerDay).HasPrecision(5, 2);
        });
        b.Entity<CalendarException>(e =>
        {
            e.HasIndex(x => new { x.CalendarId, x.Date }).IsUnique();
            e.HasOne<WorkCalendarDef>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CustomFieldDefinition>(e => e.HasIndex(x => new { x.EntityKind, x.Key }).IsUnique());
        b.Entity<CustomFieldValue>(e => e.HasIndex(x => new { x.EntityKind, x.EntityId, x.Key }).IsUnique());

        b.Entity<Project>(e =>
        {
            e.HasIndex(x => x.ProjectNo).IsUnique();
            e.Property(x => x.ProjectNo).IsRequired();
            e.Property(x => x.OrderValue).HasPrecision(18, 2);
            e.Property(x => x.MarginPercent).HasPrecision(9, 4);
            e.Property(x => x.ProgressOverride).HasPrecision(5, 4);
        });
        b.Entity<Opportunity>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.WinProbability).HasPrecision(5, 4);
            e.Property(x => x.ExpectedHours).HasPrecision(12, 2);
            e.Property(x => x.ExpectedValueLakh).HasPrecision(12, 2);
        });
        b.Entity<RiskIssue>(e => e.HasIndex(x => x.ProjectId));
        b.Entity<AmcTicket>(e => e.HasIndex(x => x.ProjectNo));

        b.Entity<PlanTask>(e =>
        {
            e.HasIndex(x => new { x.ProjectId, x.SortOrder });
            e.HasIndex(x => new { x.ProjectId, x.ExternalKey });
            e.Property(x => x.WorkHours).HasPrecision(12, 2);
            e.Property(x => x.PercentComplete).HasPrecision(5, 4);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlanTask>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TaskLink>(e =>
        {
            e.HasIndex(x => new { x.ProjectId, x.PredecessorId, x.SuccessorId, x.Type }).IsUnique();
            e.HasOne<PlanTask>().WithMany().HasForeignKey(x => x.PredecessorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlanTask>().WithMany().HasForeignKey(x => x.SuccessorId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TaskAssignment>(e =>
        {
            e.HasIndex(x => x.TaskId);
            e.Property(x => x.Units).HasPrecision(5, 4);
            e.HasOne<PlanTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Baseline>(e =>
        {
            e.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<BaselineTask>(e =>
        {
            e.HasIndex(x => x.BaselineId);
            e.Property(x => x.WorkHours).HasPrecision(12, 2);
            e.Property(x => x.PercentComplete).HasPrecision(5, 4);
            e.HasOne<Baseline>().WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Leave>(e => e.HasIndex(x => new { x.PersonId, x.FromDate }));
        b.Entity<Demand>(e =>
        {
            e.HasIndex(x => x.ProjectId);
            e.Property(x => x.HoursPerWeek).HasPrecision(8, 2);
        });
        b.Entity<Allocation>(e =>
        {
            e.HasIndex(x => new { x.PersonId, x.ProjectId, x.Month, x.Status });
            e.Property(x => x.Hours).HasPrecision(9, 2);
        });
        b.Entity<Scenario>(e => e.HasIndex(x => x.CreatedUtc));
        b.Entity<ResourceMovement>(e =>
        {
            e.HasIndex(x => new { x.PersonId, x.EffectiveDate });
            e.Property(x => x.Percent).HasPrecision(5, 4);
        });
        b.Entity<ChangeGapEntry>(e =>
        {
            e.Property(x => x.ImpactHours).HasPrecision(10, 2);
            e.Property(x => x.ImpactDays).HasPrecision(8, 2);
        });

        b.Entity<Host>(e =>
        {
            e.HasIndex(x => x.Ip);
            e.Property(x => x.RamGb).HasPrecision(8, 2);
        });
        b.Entity<HostDisk>(e =>
        {
            e.HasIndex(x => new { x.HostId, x.Drive, x.CapturedOn });
            e.Property(x => x.FreeGb).HasPrecision(10, 2);
            e.Property(x => x.TotalGb).HasPrecision(10, 2);
        });
        b.Entity<ProjectHost>(e => e.HasIndex(x => new { x.ProjectId, x.HostId, x.Role }).IsUnique());
        b.Entity<HostedSystem>(e => e.HasIndex(x => x.ProjectId));
    }
}
