using ClosedXML.Excel;
using EngineeringPerformance.Domain;
using EngineeringPerformance.Infrastructure;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class WorkbookCanonicalProjectTests
{
    [Fact]
    public void Detailed_timesheet_unions_projects_across_case_and_spacing_variants()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eos-project-union-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "details.xlsx");

        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                sheet.Cell(1, 1).Value = "Employee";
                sheet.Cell(1, 2).Value = "Date";
                sheet.Cell(1, 3).Value = "Project No";
                sheet.Cell(1, 4).Value = "Total work Hours";

                sheet.Cell(2, 1).Value = "Asha Nair";
                sheet.Cell(2, 2).Value = new DateTime(2026, 7, 1);
                sheet.Cell(2, 3).Value = "PROJECT-A";
                sheet.Cell(2, 4).Value = 2m;

                sheet.Cell(3, 1).Value = "ASHA   NAIR";
                sheet.Cell(3, 2).Value = new DateTime(2026, 7, 2);
                sheet.Cell(3, 3).Value = "PROJECT-B";
                sheet.Cell(3, 4).Value = 3m;
                workbook.SaveAs(path);
            }

            var rows = new WorkbookService().ReadPerformance(
                path,
                ReportType.DetailedTimesheetTransactions,
                2026,
                7);

            var row = Assert.Single(rows);
            Assert.True(PersonName.Matches(row.EmployeeName, "Asha Nair"));
            Assert.Equal(2, row.DetailedEntries);
            Assert.Equal(5m, row.DetailedHours);
            Assert.Equal(2, row.UniqueProjects);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
