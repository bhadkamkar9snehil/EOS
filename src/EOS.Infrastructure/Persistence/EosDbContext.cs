using EOS.Domain.Audit;
using EOS.Domain.Capacity;
using EOS.Domain.Configuration;
using EOS.Domain.Hosting;
using EOS.Domain.People;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Domain.Skills;
using EOS.Domain.Training;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Persistence;

public sealed class SettingRow
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public int ScopeKind { get; set; }
    public string ScopeId { get; set; } = "";
    public string JsonValue { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? ChangedBy { get; set; }
    public string? Reason { get; set; }
}

public sealed partial class EosDbContext(DbContextOptions<EosDbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<PersonAlias> PersonAliases => Set<PersonAlias>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<PersonSkill> PersonSkills => Set<PersonSkill>();
    public DbSet<SkillTransaction> SkillTransactions => Set<SkillTransaction>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<PersonModule> PersonModules => Set<PersonModule>();
    public DbSet<Doubt> Doubts => Set<Doubt>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<SettingRow> Settings => Set<SettingRow>();

    public DbSet<VocabularyItem> Vocabulary => Set<VocabularyItem>();
    public DbSet<WorkCalendarDef> Calendars => Set<WorkCalendarDef>();
    public DbSet<CalendarException> CalendarExceptions => Set<CalendarException>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<RiskIssue> RiskIssues => Set<RiskIssue>();
    public DbSet<AmcTicket> AmcTickets => Set<AmcTicket>();

    public DbSet<PlanTask> Tasks => Set<PlanTask>();
    public DbSet<TaskLink> TaskLinks => Set<TaskLink>();
    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();
    public DbSet<Baseline> Baselines => Set<Baseline>();
    public DbSet<BaselineTask> BaselineTasks => Set<BaselineTask>();

    public DbSet<Leave> Leaves => Set<Leave>();
    public DbSet<Demand> Demands => Set<Demand>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<ResourceMovement> Movements => Set<ResourceMovement>();
    public DbSet<ChangeGapEntry> ChangeGapLog => Set<ChangeGapEntry>();

    public DbSet<Host> Hosts => Set<Host>();
    public DbSet<HostDisk> HostDisks => Set<HostDisk>();
    public DbSet<ProjectHost> ProjectHosts => Set<ProjectHost>();
    public DbSet<HostedSystem> HostedSystems => Set<HostedSystem>();

    public DbSet<EOS.Domain.Security.ApiToken> ApiTokens => Set<EOS.Domain.Security.ApiToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        OnModelCreatingDelivery(b);
        b.Entity<Team>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).IsRequired();
        });

        b.Entity<Person>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).IsRequired();
            e.Property(x => x.FullName).IsRequired();
            e.Property(x => x.AvailabilityFraction).HasPrecision(5, 4);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PersonAlias>(e =>
        {
            e.HasIndex(x => new { x.Alias, x.Source }).IsUnique();
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Skill>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).IsRequired();
        });

        b.Entity<PersonSkill>(e =>
        {
            e.HasIndex(x => new { x.PersonId, x.SkillId }).IsUnique();
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SkillTransaction>(e =>
        {
            e.HasIndex(x => new { x.PersonId, x.SkillId, x.AtUtc });
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Module>(e =>
        {
            e.HasIndex(x => new { x.Product, x.Name, x.SubModule }).IsUnique();
        });

        b.Entity<PersonModule>(e =>
        {
            e.HasIndex(x => new { x.PersonId, x.ModuleId }).IsUnique();
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Module>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Doubt>(e => e.HasIndex(x => x.SourceRow));

        b.Entity<AuditEvent>(e => e.HasIndex(x => x.AtUtc));

        b.Entity<SettingRow>(e =>
        {
            e.HasIndex(x => new { x.Key, x.ScopeKind, x.ScopeId, x.EffectiveFrom });
        });
    }
}
