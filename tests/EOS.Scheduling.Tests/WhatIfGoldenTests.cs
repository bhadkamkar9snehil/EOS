using EOS.Scheduling;
using static EOS.Scheduling.Tests.GoldenSampleProjects;

namespace EOS.Scheduling.Tests;

/// <summary>Golden tests G2–G5 through the what-if engine (spec §15.8, §28.1).</summary>
public class WhatIfGoldenTests
{
    private static List<ProjectPlan> Plans() =>
    [
        new("P1", "Project 1", Start, Project1()),
        new("P2", "Project 2", Start, Project2()),
        new("P3", "Project 3", Start, Project3()),
    ];

    private static ProjectImpact Impact(ScenarioOutcome o, string id) => o.Projects.Single(p => p.ProjectId == id);

    [Fact]
    public void G2_Scenario1_Move_Mahesh_No_Replacement()
    {
        var o = WhatIf.Run(Plans(), [new MoveRequest("Mahesh", "P1", "P2", D(10, 26))], Calendar);

        var p1 = Impact(o, "P1");
        Assert.Equal(D(11, 18), p1.BaselineFinish);
        Assert.Equal(D(11, 28), p1.ScenarioFinish);
        Assert.Equal(9, p1.SlipWorkingDays);
        Assert.Equal(6, p1.Tasks.Single(t => t.TaskId == "1.2").SlipWorkingDays);
        Assert.Equal(3, p1.Tasks.Single(t => t.TaskId == "1.4").SlipWorkingDays);

        var p2 = Impact(o, "P2");
        Assert.Equal(D(11, 25), p2.ScenarioFinish);
        Assert.Equal(-18, p2.SlipWorkingDays);

        Assert.Equal(0, Impact(o, "P3").SlipWorkingDays);       // untouched project is unchanged
    }

    [Fact]
    public void Scenario_Does_Not_Mutate_The_Baseline_Plans()
    {
        var plans = Plans();
        var before = Scheduler.Compute(plans[0].Tasks, Calendar, Start).Finish;
        WhatIf.Run(plans, [new MoveRequest("Mahesh", "P1", "P2", D(10, 26))], Calendar);
        Assert.Equal(before, Scheduler.Compute(plans[0].Tasks, Calendar, Start).Finish);
        Assert.All(plans[0].Tasks.SelectMany(t => t.Crew), c => Assert.Null(c.To));
    }

    [Fact]
    public void G3_Move_On_19_Nov_Costs_Nothing_And_Gains_Eleven()
    {
        var o = WhatIf.Run(Plans(), [new MoveRequest("Mahesh", "P1", "P2", D(11, 19))], Calendar);
        Assert.Equal(0, Impact(o, "P1").SlipWorkingDays);
        Assert.Equal(D(12, 3), Impact(o, "P2").ScenarioFinish);
        Assert.Equal(-11, Impact(o, "P2").SlipWorkingDays);
    }

    [Fact]
    public void G5_Push_Plan_Moves_Three_Engineers_With_No_Delay_To_Others()
    {
        var o = WhatIf.Run(Plans(),
        [
            new MoveRequest("Twinkle", "P1", "P2", D(10, 26)),
            new MoveRequest("Dharati", "P3", "P2", D(11, 6)),
            new MoveRequest("Sagar", "P3", "P2", D(11, 9)),
        ], Calendar);

        Assert.Equal(D(11, 18), Impact(o, "P2").ScenarioFinish);
        Assert.Equal(-24, Impact(o, "P2").SlipWorkingDays);
        Assert.Equal(0, Impact(o, "P1").SlipWorkingDays);        // Twinkle's task is picked up by the P1 team in time
        Assert.Equal(0, Impact(o, "P3").SlipWorkingDays);        // Dharati and Sagar leave after their P3 work
    }

    [Fact]
    public void Moves_Are_Applied_In_Date_Order_Regardless_Of_Input_Order()
    {
        var a = WhatIf.Run(Plans(),
        [
            new MoveRequest("Sagar", "P3", "P2", D(11, 9)),
            new MoveRequest("Twinkle", "P1", "P2", D(10, 26)),
            new MoveRequest("Dharati", "P3", "P2", D(11, 6)),
        ], Calendar);
        Assert.Equal(D(11, 18), Impact(a, "P2").ScenarioFinish);
    }

    [Fact]
    public void Partial_Move_Splits_The_Persons_Time()
    {
        // Half of Mahesh moves: P1 loses half a person from 26 Oct, P2 gains half a person.
        var o = WhatIf.Run(Plans(), [new MoveRequest("Mahesh", "P1", "P2", D(10, 26), 0.5m)], Calendar);
        var full = WhatIf.Run(Plans(), [new MoveRequest("Mahesh", "P1", "P2", D(10, 26))], Calendar);
        Assert.InRange(Impact(o, "P1").SlipWorkingDays, 1, Impact(full, "P1").SlipWorkingDays - 1);
        Assert.InRange(Impact(o, "P2").SlipWorkingDays, Impact(full, "P2").SlipWorkingDays + 1, -1);
    }

    // ---- G4: the release table of Scenario 2 --------------------------------------------------------------

    [Fact]
    public void G4_Release_Analysis_Matches_The_Source_Table()
    {
        var rows = WhatIf.ReleaseAnalysis(Plans(), "P2", D(10, 26), Calendar);
        ReleaseRow Row(string who) => rows.Single(r => r.PersonId == who);

        // Twinkle: free now, no cost.
        Assert.Equal(D(11, 7), Row("Twinkle").LastDayOnCurrentWork);
        Assert.Equal(D(10, 26), Row("Twinkle").EarliestNoDelayMove);
        Assert.Equal(0, Row("Twinkle").CostIfMovedOnAnalysisDate);

        // Dharati: last day 5 Nov, earliest 6 Nov, +10 if moved on 26 Oct.
        Assert.Equal(D(11, 5), Row("Dharati").LastDayOnCurrentWork);
        Assert.Equal(D(11, 6), Row("Dharati").EarliestNoDelayMove);
        Assert.Equal(10, Row("Dharati").CostIfMovedOnAnalysisDate);

        // Sagar: last day 7 Nov, earliest Mon 9 Nov, +7 if moved on 26 Oct.
        Assert.Equal(D(11, 7), Row("Sagar").LastDayOnCurrentWork);
        Assert.Equal(D(11, 9), Row("Sagar").EarliestNoDelayMove);
        Assert.Equal(7, Row("Sagar").CostIfMovedOnAnalysisDate);

        // Mahesh and Lalji: last day 18 Nov, earliest 19 Nov, +9.
        foreach (var who in new[] { "Mahesh", "Lalji" })
        {
            Assert.Equal(D(11, 18), Row(who).LastDayOnCurrentWork);
            Assert.Equal(D(11, 19), Row(who).EarliestNoDelayMove);
            Assert.Equal(9, Row(who).CostIfMovedOnAnalysisDate);
        }

        // Each single move gains 18 working days on the target (every engineer adds 8 h/day).
        foreach (var who in new[] { "Twinkle", "Dharati", "Sagar", "Mahesh", "Lalji" })
            Assert.Equal(-18, Row(who).GainToTargetIfMovedOnAnalysisDate);
    }

    [Fact]
    public void Pickup_Rule_Can_Be_Switched_Off()
    {
        // Without the pickup rule Twinkle's orphaned task has no crew and cannot complete: reported, not faked.
        var plans = Plans();
        var tasks = WhatIf.Leave(plans[0].Tasks, "Twinkle", D(10, 26), 1m);
        Assert.Throws<SchedulingException>(() =>
            Scheduler.Compute(tasks, Calendar, Start, new SchedulingOptions(PickupRule.None)));

        var withPickup = Scheduler.Compute(tasks, Calendar, Start);
        Assert.Equal(D(11, 18), withPickup.Finish);
    }
}
