using EOS.Scheduling;

namespace EOS.Scheduling.Tests;

public sealed class HalfDaySaturdayTests
{
    private static WorkCalendar HalfSaturday() => new(8m,
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday],
        hoursByDay: new Dictionary<DayOfWeek, decimal> { [DayOfWeek.Saturday] = 4.5m });

    [Fact]
    public void Saturday_has_its_own_hours_and_sunday_none()
    {
        var cal = HalfSaturday();
        Assert.Equal(8m, cal.HoursOn(new DateOnly(2026, 10, 5)));
        Assert.Equal(4.5m, cal.HoursOn(new DateOnly(2026, 10, 10)));
        Assert.Equal(0m, cal.HoursOn(new DateOnly(2026, 10, 11)));
        Assert.Equal(44.5m, cal.HoursBetween(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11)));
    }

    [Fact]
    public void A_task_of_44_5_hours_for_one_engineer_finishes_on_the_saturday_but_one_more_hour_spills_to_monday()
    {
        var crew = new[] { new CrewMember("A") };
        var start = new DateOnly(2026, 10, 5);
        var week = Scheduler.Compute([new ScheduleTask("t", "t", 44.5m, [], crew)], HalfSaturday(), start);
        var spill = Scheduler.Compute([new ScheduleTask("t", "t", 45.5m, [], crew)], HalfSaturday(), start);
        Assert.Equal(new DateOnly(2026, 10, 10), week.Tasks["t"].Finish);
        Assert.Equal(new DateOnly(2026, 10, 12), spill.Tasks["t"].Finish);
    }
}
