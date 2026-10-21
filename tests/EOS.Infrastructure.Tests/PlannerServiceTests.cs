using EOS.Domain.People;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;

namespace EOS.Infrastructure.Tests;

public sealed class PlannerServiceTests
{
    [Fact]
    public async Task OtherProjectLoadClipsAssignmentsAndKeepsCalendarHoursAndFilters()
    {
        var (db, connection) = EosDatabase.InMemory();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var monday = new DateOnly(2026, 10, 19);
        var primary = new Project { ProjectNo = "P1", Name = "Primary", PoStart = monday };
        var other = new Project { ProjectNo = "P2", Name = "Other", PoStart = monday };
        var engineer = new Person { Code = "E1", FullName = "Engineer" };
        var inactive = new Person { Code = "E2", FullName = "Inactive", IsActive = false };
        db.AddRange(primary, other, engineer, inactive);
        await db.SaveChangesAsync();

        var current = new PlanTask { ProjectId = primary.Id, Name = "Current", WorkHours = 8, FixedDurationDays = 1 };
        var scheduled = new PlanTask
        {
            ProjectId = other.Id, Name = "Scheduled",
            ScheduledStart = monday, ScheduledFinish = monday.AddDays(7)
        };
        var completed = new PlanTask
        {
            ProjectId = other.Id, Name = "Completed", PercentComplete = 1,
            ScheduledStart = monday, ScheduledFinish = monday.AddDays(7)
        };
        var unscheduled = new PlanTask { ProjectId = other.Id, Name = "Unscheduled" };
        db.AddRange(current, scheduled, completed, unscheduled);
        await db.SaveChangesAsync();
        db.TaskAssignments.Add(new TaskAssignment
        {
            TaskId = scheduled.Id, PersonId = engineer.Id, Units = 0.5m,
            FromDate = monday.AddDays(4), ToDate = monday.AddDays(8)
        });
        foreach (var task in new[] { current, completed, unscheduled })
            db.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, PersonId = engineer.Id });
        db.TaskAssignments.Add(new TaskAssignment { TaskId = scheduled.Id, PersonId = inactive.Id });
        await db.SaveChangesAsync();

        var session = await new PlannerService(db).PrepareAsync(primary.Id, monday);

        // Friday 4 h + half-day Saturday 2.25 h; following Monday 4 h. Sunday is excluded.
        Assert.Equal(6.25m, session.OtherLoad[engineer.Id][session.Weeks.ToList().IndexOf(monday)]);
        Assert.Equal(4m, session.OtherLoad[engineer.Id][session.Weeks.ToList().IndexOf(monday.AddDays(7))]);
        Assert.Equal(10.25m, session.OtherLoad[engineer.Id].Sum());
        Assert.False(session.OtherLoad.ContainsKey(inactive.Id));
    }
}
