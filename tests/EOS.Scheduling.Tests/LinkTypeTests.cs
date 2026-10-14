using EOS.Scheduling;
using static EOS.Scheduling.Tests.GoldenSampleProjects;

namespace EOS.Scheduling.Tests;

/// <summary>Link types, lag, fixed duration and start constraints (spec §14.4). Calendar: Mon-Sat, 8 h. Start Mon 5 Oct 2026.</summary>
public class LinkTypeTests
{
    private static CrewMember A => new("A");
    private static CrewMember B => new("B");

    private static ScheduleResult Run(params ScheduleTask[] tasks) => Scheduler.Compute(tasks, Calendar, Start);

    // Task X: 5 working days (40 h, one person): Mon 5 Oct - Fri 9 Oct.
    private static ScheduleTask X => new("X", "X", 40, [], [A]);

    [Fact]
    public void FinishToStart_With_Positive_Lag_Delays_The_Successor()
    {
        var y = new ScheduleTask("Y", "Y", 16, [], [B]) { Dependencies = [new TaskDependency("X", LinkType.FinishToStart, 2)] };
        var r = Run(X, y);
        Assert.Equal(D(10, 9), r.Tasks["X"].Finish);
        // Mon-Sat calendar: X ends Fri 9, natural FS start is Sat 10, plus 2 working days (Mon 12, Tue 13) = Tue 13.
        Assert.Equal(D(10, 13), r.Tasks["Y"].Start);
    }

    [Fact]
    public void FinishToStart_Lag_Zero_Equals_Plain_Predecessor()
    {
        var plain = Run(X, new ScheduleTask("Y", "Y", 16, ["X"], [B]));
        var linked = Run(X, new ScheduleTask("Y", "Y", 16, [], [B]) { Dependencies = [new TaskDependency("X")] });
        Assert.Equal(plain.Tasks["Y"].Start, linked.Tasks["Y"].Start);
        Assert.Equal(plain.Tasks["Y"].Finish, linked.Tasks["Y"].Finish);
    }

    [Fact]
    public void StartToStart_Lets_Tasks_Run_In_Parallel_With_An_Offset()
    {
        var y = new ScheduleTask("Y", "Y", 16, [], [B]) { Dependencies = [new TaskDependency("X", LinkType.StartToStart, 2)] };
        var r = Run(X, y);
        Assert.Equal(D(10, 5), r.Tasks["X"].Start);
        Assert.Equal(D(10, 7), r.Tasks["Y"].Start);           // X start Mon 5 + 2 working days
        Assert.Equal(D(10, 8), r.Tasks["Y"].Finish);
    }

    [Fact]
    public void FinishToFinish_Prevents_The_Successor_Finishing_Before_The_Predecessor()
    {
        // Y needs only 1 day but may not finish before X (Fri 9 Oct): it is pushed to finish on that day.
        var y = new ScheduleTask("Y", "Y", 8, [], [B]) { Dependencies = [new TaskDependency("X", LinkType.FinishToFinish)] };
        var r = Run(X, y);
        Assert.Equal(D(10, 9), r.Tasks["Y"].Finish);
        Assert.Equal(D(10, 9), r.Tasks["Y"].Start);
    }

    [Fact]
    public void StartToFinish_Constrains_Successor_Finish_By_Predecessor_Start()
    {
        var later = new ScheduleTask("L", "L", 8, [], [B]) { StartNoEarlierThan = D(10, 12) };
        var y = new ScheduleTask("Y", "Y", 8, [], [A]) { Dependencies = [new TaskDependency("L", LinkType.StartToFinish)] };
        var r = Run(later, y);
        Assert.True(r.Tasks["Y"].Finish >= r.Tasks["L"].Start);
    }

    [Fact]
    public void FixedDuration_Ignores_Crew_Size()
    {
        var fat = new ScheduleTask("FAT", "FAT", 0, [], [A, B]) { FixedDurationDays = 3 };
        var r = Run(fat);
        Assert.Equal(3, r.Tasks["FAT"].DurationDays);
        Assert.Equal(D(10, 7), r.Tasks["FAT"].Finish);
    }

    [Fact]
    public void StartNoEarlierThan_Is_Respected()
    {
        var t = new ScheduleTask("T", "T", 8, [], [A]) { StartNoEarlierThan = D(10, 14) };
        Assert.Equal(D(10, 14), Run(t).Tasks["T"].Start);
    }

    [Fact]
    public void Milestone_With_Finish_To_Start_Takes_Predecessor_Finish_Day()
    {
        var m = new ScheduleTask("M", "Go-live", 0, ["X"], []);
        var r = Run(X, m);
        Assert.Equal(D(10, 9), r.Tasks["M"].Start);
        Assert.Equal(0, r.Tasks["M"].DurationDays);
    }

    [Fact]
    public void Backward_Pass_Honours_Lag_And_Float()
    {
        // X(5d) ->FS+0-> Y(2d); Z(1d) is independent: Z has float, X and Y are critical.
        var y = new ScheduleTask("Y", "Y", 16, ["X"], [A]);
        var z = new ScheduleTask("Z", "Z", 8, [], [B]);
        var r = Run(X, y, z);
        Assert.True(r.Tasks["X"].IsCritical && r.Tasks["Y"].IsCritical);
        Assert.False(r.Tasks["Z"].IsCritical);
        Assert.Equal(r.Tasks["Y"].Finish, r.Finish);
    }

    [Fact]
    public void Self_Dependency_And_Unknown_Predecessor_Are_Rejected()
    {
        Assert.Throws<SchedulingException>(() => Run(new ScheduleTask("A", "A", 8, [], [A]) { Dependencies = [new TaskDependency("A")] }));
        Assert.Throws<SchedulingException>(() => Run(new ScheduleTask("A", "A", 8, ["nope"], [A])));
    }

    [Fact]
    public void Mixed_Link_Cycle_Is_Reported()
    {
        var a = new ScheduleTask("A", "A", 8, [], [A]) { Dependencies = [new TaskDependency("B", LinkType.StartToStart)] };
        var b = new ScheduleTask("B", "B", 8, [], [B]) { Dependencies = [new TaskDependency("A", LinkType.FinishToFinish)] };
        Assert.Throws<SchedulingException>(() => Run(a, b));
    }
}
