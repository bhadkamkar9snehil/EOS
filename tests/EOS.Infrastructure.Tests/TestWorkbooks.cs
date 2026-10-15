using ClosedXML.Excel;

namespace EOS.Infrastructure.Tests;

internal static class TestWorkbooks
{
    public static string TempPath() => Path.Combine(Path.GetTempPath(), $"eos-test-{Guid.NewGuid():N}.xlsx");

    public static string Agility(int sqlRatingForAnna = 3, string annaRole = "Engineer")
    {
        using var wb = new XLWorkbook();

        var eng = wb.AddWorksheet("Engineers");
        eng.Cell(1, 1).Value = "Engineers master";
        string[] h = ["Emp ID", "Full Name", "Team", "Role", "Base Location", "Availability %"];
        for (var c = 0; c < h.Length; c++) eng.Cell(3, c + 1).Value = h[c];
        object[][] people =
        [
            ["E001", "Anna Lead", "Alpha", annaRole, "Office", 1.0],
            ["E002", "Bela Dev", "Alpha", "Engineer", "Site", 0.5],
            ["E003", "Kartik Patil", "Beta", "Engineer", "Office", 1.0],
        ];
        for (var r = 0; r < people.Length; r++)
            for (var c = 0; c < people[r].Length; c++)
                eng.Cell(4 + r, c + 1).Value = XLCellValue.FromObject(people[r][c]);
        eng.Cell(7, 2).Value = "TOTAL";

        var skills = wb.AddWorksheet("Skills");
        string[] sh = ["Skill ID", "Skill Name", "Category", "Description"];
        for (var c = 0; c < sh.Length; c++) skills.Cell(3, c + 1).Value = sh[c];
        skills.Cell(4, 1).Value = "SK01"; skills.Cell(4, 2).Value = "SQL"; skills.Cell(4, 3).Value = "Database"; skills.Cell(4, 4).Value = "Queries";
        skills.Cell(5, 1).Value = "SK02"; skills.Cell(5, 2).Value = "Historian"; skills.Cell(5, 3).Value = "Data"; skills.Cell(5, 4).Value = "Tags";

        var m = wb.AddWorksheet("Skill_Matrix");
        string[] mh = ["Emp ID", "Full Name", "Team", "SQL", "Historian", "Avg Rating"];
        for (var c = 0; c < mh.Length; c++) m.Cell(4, c + 1).Value = mh[c];
        object[][] rows =
        [
            ["E001", "Anna Lead", "Alpha", sqlRatingForAnna, 5],
            ["E002", "Bela Dev", "Alpha", 2, 1],
            ["E099", "Kartik Patil", "Beta", 2, 2],
        ];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                m.Cell(5 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);

        var path = TempPath();
        wb.SaveAs(path);
        return path;
    }

    public static string Training(string state = "Know")
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Module");
        string[] h = ["Sr No", "Product", "Module", "Sub Module", "Anna Lead", "Bela", "Zed"];
        for (var c = 0; c < h.Length; c++) ws.Cell(1, c + 1).Value = h[c];
        object[][] rows =
        [
            [1, "X-Force Framework", "LV", "Create", state, "don't Know", "Know"],
            [2, "X-Force Framework", "LV", "Pass Parameter", "Need to discuss", "Know", "don’t Know"],
            [3, "X-Force Historian", "Tag", "Create", "Know", "Know", "Know"],
        ];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(2 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);

        var d = wb.AddWorksheet("Need to Discuss");
        string[] dh = ["Sr No", "Module", "Sub Module", "Doute In Detail", "Doute date", "Raise Engineer", "Assign Date", "Assign Engineer", "Solved Date", "Result", "Remarks"];
        for (var c = 0; c < dh.Length; c++) d.Cell(1, c + 1).Value = dh[c];
        d.Cell(2, 1).Value = 1; d.Cell(2, 2).Value = "LV"; d.Cell(2, 3).Value = "Pass Parameter"; d.Cell(2, 4).Value = "How do parameters work?";
        d.Cell(2, 5).Value = new DateTime(2026, 7, 6); d.Cell(2, 6).Value = "Bela"; d.Cell(2, 7).Value = new DateTime(2026, 7, 8);
        d.Cell(2, 8).Value = "Anna Lead"; d.Cell(2, 9).Value = new DateTime(2026, 7, 8); d.Cell(2, 10).Value = "Solved";
        d.Cell(3, 1).Value = 2; d.Cell(3, 2).Value = "LV"; d.Cell(3, 3).Value = "Create"; d.Cell(3, 4).Value = "Still confused"; d.Cell(3, 6).Value = "Zed";

        var p = wb.AddWorksheet("Module Proficiency Level");     // default-filled "Beginner": must be ignored
        p.Cell(1, 1).Value = "Sr No"; p.Cell(2, 1).Value = 1; p.Cell(2, 5).Value = "Beginner";

        var path = TempPath();
        wb.SaveAs(path);
        return path;
    }
}
