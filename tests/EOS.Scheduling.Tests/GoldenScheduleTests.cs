using EOS.Scheduling;
using static EOS.Scheduling.Tests.GoldenSampleProjects;

namespace EOS.Scheduling.Tests;

/// <summary>Golden tests G1, G2, G3, G5 (assignment-window part) and G6 from spec §28.1 / §15.8.</summary>
public class GoldenScheduleTests
{
    private static ScheduleResult Run(List<ScheduleTask> tasks) => Scheduler.Compute(tasks, Calendar, Start);

    private static void AssertDates(ScheduleResult r, string id, DateOnly start, DateOnly finish)
    {
        Assert.Equal(start, r.Tasks[id].Start);
        Assert.Equal(finish, r.Tasks[id].Finish);
    }

    // ---- G1: baseline -------------------------------------------------------------------------------

    [Fact]
    public void G1_Project1_Baseline()
    {
        var r = Run(Project1());
        AssertDates(r, "1.1", D(10, 5), D(10, 17));
        AssertDates(r, "1.2", D(10, 19), D(10, 31));
        AssertDates(r, "1.3", D(10, 19), D(11, 7));
        AssertDates(r, "1.4", D(11, 9), D(11, 11));
        AssertDates(r, "1.5", D(11, 12), D(11, 18));
        Assert.Equal(D(11, 18), r.Finish);
    }

    [Fact]
    public void G1_Project1_CriticalPathAndSlack()
    {
        var r = Run(Project1());
        Assert.Equal(6, r.Tasks["1.2"].TotalFloatDays);      // 6 working days of slack (spec §14.5)
        Assert.False(r.Tasks["1.2"].IsCritical);
        foreach (var id in new[] { "1.1", "1.3", "1.4", "1.5" })
            Assert.True(r.Tasks[id].IsCritical, $"{id} should be critical");
    }

    [Fact]
    public void G1_Project2_Baseline()
    {
        var r = Run(Project2());
        AssertDates(r, "2.1", D(10, 5), D(10, 15));
        AssertDates(r, "2.2", D(10, 16), D(11, 19));
        AssertDates(r, "2.3", D(11, 20), D(12, 12));
        AssertDates(r, "2.4", D(12, 14), D(12, 16));
        Assert.Equal(D(12, 16), r.Finish);
    }

    [Fact]
    public void G1_Project3_Baseline()
    {
        var r = Run(Project3());
        AssertDates(r, "3.1", D(10, 5), D(10, 13));
        AssertDates(r, "3.2", D(10, 14), D(11, 5));
        AssertDates(r, "3.3", D(10, 14), D(11, 5));
        AssertDates(r, "3.4", D(11, 6), D(11, 7));
        Assert.Equal(D(11, 7), r.Finish);
    }

    // ---- G2: Mahesh moves P1 -> P2 on 26 Oct, no replacement ----------------------------------------

    [Fact]
    public void G2_Scenario1_Project1_Slips_Nine_Days()
    {
        var r = Run(Project1(maheshLeavesOn: D(10, 26)));
        AssertDates(r, "1.2", D(10, 19), D(11, 7));      // +6 days
        AssertDates(r, "1.4", D(11, 9), D(11, 14));      // +3 days
        AssertDates(r, "1.5", D(11, 16), D(11, 28));     // +9 days
        Assert.Equal(D(11, 28), r.Finish);
        Assert.Equal(9, Calendar.WorkingDaysBetween(D(11, 18), r.Finish));
    }

    [Fact]
    public void G2_Scenario1_Project2_Gains_Eighteen_Days()
    {
        var r = Run(Project2(("Mahesh", D(10, 26))));
        AssertDates(r, "2.2", D(10, 16), D(11, 11));
        AssertDates(r, "2.3", D(11, 12), D(11, 23));
        AssertDates(r, "2.4", D(11, 24), D(11, 25));
        Assert.Equal(D(11, 25), r.Finish);
        Assert.Equal(-18, Calendar.WorkingDaysBetween(D(12, 16), r.Finish));
    }

    // ---- G3: the alternative, Mahesh moves on 19 Nov -------------------------------------------------

    [Fact]
    public void G3_Alternative_Move_After_Project1_Work_Is_Done()
    {
        var p1 = Run(Project1(maheshLeavesOn: D(11, 19)));
        Assert.Equal(D(11, 18), p1.Finish);              // Project 1 unaffected

        var p2 = Run(Project2(("Mahesh", D(11, 19))));
        Assert.Equal(D(12, 3), p2.Finish);
        Assert.Equal(-11, Calendar.WorkingDaysBetween(D(12, 16), p2.Finish));
    }

    // ---- G5/G6: pushing Project 2 --------------------------------------------------------------------

    [Fact]
    public void G6_Single_Move_Cuts_Eighteen_Days_From_Project2()
    {
        var r = Run(Project2(("Twinkle", D(10, 26))));
        Assert.Equal(D(11, 25), r.Finish);
        Assert.Equal(-18, Calendar.WorkingDaysBetween(D(12, 16), r.Finish));
    }

    [Fact]
    public void G5_Push_Plan_Finishes_Project2_On_18_Nov()
    {
        var r = Run(Project2(("Twinkle", D(10, 26)), ("Dharati", D(11, 6)), ("Sagar", D(11, 9))));
        AssertDates(r, "2.2", D(10, 16), D(11, 10));
        AssertDates(r, "2.3", D(11, 11), D(11, 16));
        AssertDates(r, "2.4", D(11, 17), D(11, 18));
        Assert.Equal(D(11, 18), r.Finish);
        Assert.Equal(-24, Calendar.WorkingDaysBetween(D(12, 16), r.Finish));
    }

    // ---- Engine behaviour that must be configurable ---------------------------------------------------

    [Fact]
    public void Hours_Per_Day_And_Working_Days_Are_Configurable()
    {
        // 96 h, one engineer: 12 days at 8 h/day, 8 days at 12 h/day.
        var tasks = new List<ScheduleTask> { new("T", "Task", 96, [], [new CrewMember("A")]) };
        var eightHours = Scheduler.Compute(tasks, WorkCalendar.MondayToSaturday(8m), D(10, 5));
        var twelveHours = Scheduler.Compute(tasks, WorkCalendar.MondayToSaturday(12m), D(10, 5));
        Assert.Equal(12, eightHours.Tasks["T"].DurationDays);
        Assert.Equal(8, twelveHours.Tasks["T"].DurationDays);

        // Five-day week pushes the same task past the weekend sooner.
        var fiveDay = Scheduler.Compute(tasks, WorkCalendar.MondayToFriday(8m), D(10, 5));
        Assert.Equal(D(10, 20), fiveDay.Tasks["T"].Finish);   // 12 weekdays from Mon 5 Oct
    }

    [Fact]
    public void Holidays_Are_Skipped()
    {
        var cal = new WorkCalendar(8m, [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            holidays: [D(10, 7)]);
        var tasks = new List<ScheduleTask> { new("T", "Task", 24, [], [new CrewMember("A")]) };
        var r = Scheduler.Compute(tasks, cal, D(10, 5));
        Assert.Equal(D(10, 8), r.Tasks["T"].Finish);          // Mon, Tue, (Wed holiday), Thu
    }

    [Fact]
    public void Productivity_Factor_Scales_Work_Done()
    {
        var full = new List<ScheduleTask> { new("T", "Task", 80, [], [new CrewMember("A")]) };
        var half = new List<ScheduleTask> { new("T", "Task", 80, [], [new CrewMember("A", Factor: 0.5m)]) };
        Assert.Equal(10, Scheduler.Compute(full, Calendar, D(10, 5)).Tasks["T"].DurationDays);
        Assert.Equal(20, Scheduler.Compute(half, Calendar, D(10, 5)).Tasks["T"].DurationDays);
    }

    [Fact]
    public void Cycle_Is_Reported()
    {
        var tasks = new List<ScheduleTask>
        {
            new("A", "A", 8, ["B"], [new CrewMember("X")]),
            new("B", "B", 8, ["A"], [new CrewMember("X")]),
        };
        var ex = Assert.Throws<SchedulingException>(() => Scheduler.Compute(tasks, Calendar, Start));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Task_With_No_Crew_Is_Reported_Not_Given_A_Fake_Date()
    {
        var tasks = new List<ScheduleTask> { new("A", "A", 8, [], []) };
        Assert.Throws<SchedulingException>(() => Scheduler.Compute(tasks, Calendar, Start));
    }

    [Fact]
    public void Milestone_Takes_Predecessor_Finish_And_Zero_Duration()
    {
        var tasks = new List<ScheduleTask>
        {
            new("A", "A", 16, [], [new CrewMember("X")]),
            new("M", "Milestone", 0, ["A"], []),
        };
        var r = Scheduler.Compute(tasks, Calendar, Start);
        Assert.Equal(r.Tasks["A"].Finish, r.Tasks["M"].Start);
        Assert.Equal(r.Tasks["M"].Start, r.Tasks["M"].Finish);
    }
}
