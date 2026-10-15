using EOS.Scheduling;

namespace EOS.Scheduling.Tests;

internal static class GoldenSampleProjects
{
    public static readonly DateOnly Start = new(2026, 10, 5);
    public static WorkCalendar Calendar => WorkCalendar.MondayToSaturday(8m);

    public static DateOnly D(int month, int day) => new(2026, month, day);

    private static CrewMember C(string who, DateOnly? from = null, DateOnly? to = null) => new(who, From: from, To: to);

    public static List<ScheduleTask> Project1(DateOnly? maheshLeavesOn = null)
    {
        DateOnly? to = maheshLeavesOn?.AddDays(-1);
        return
        [
            new("1.1", "Design & detail engineering", 96, [], [C("Mahesh", to: to)]),
            new("1.2", "Historian configuration", 192, ["1.1"], [C("Mahesh", to: to), C("Lalji")]),
            new("1.3", "Dashboard development", 144, ["1.1"], [C("Twinkle")]),
            new("1.4", "FAT", 48, ["1.2", "1.3"], [C("Mahesh", to: to), C("Lalji")]),
            new("1.5", "Site installation & commissioning", 96, ["1.4"], [C("Mahesh", to: to), C("Lalji")]),
        ];
    }

    public static List<ScheduleTask> Project2(params (string Who, DateOnly From)[] joiners)
    {
        CrewMember[] Join() => joiners.Select(j => C(j.Who, from: j.From)).ToArray();
        return
        [
            new("2.1", "Design & detail engineering", 80, [], [C("Chirag")]),
            new("2.2", "MES configuration", 480, ["2.1"], [C("Chirag"), C("Deep"), .. Join()]),
            new("2.3", "MES-SAP integration", 160, ["2.2"], [C("Deep"), .. Join()]),
            new("2.4", "FAT", 48, ["2.3"], [C("Chirag"), C("Deep"), .. Join()]),
        ];
    }

    public static List<ScheduleTask> Project3() =>
    [
        new("3.1", "As-Is study", 64, [], [C("Sagar")]),
        new("3.2", "Report development", 320, ["3.1"], [C("Priyanka"), C("Dharati")]),
        new("3.3", "Custom pages", 160, ["3.1"], [C("Sagar")]),
        new("3.4", "FAT", 32, ["3.2", "3.3"], [C("Sagar"), C("Priyanka")]),
    ];
}
