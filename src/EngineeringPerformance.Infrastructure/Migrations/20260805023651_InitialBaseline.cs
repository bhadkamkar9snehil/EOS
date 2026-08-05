using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EngineeringPerformance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analysis_exclusion",
                columns: table => new
                {
                    EmployeeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_exclusion", x => x.EmployeeName);
                });

            migrationBuilder.CreateTable(
                name: "employee",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SeniorityLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    IsConsultant = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOnProbation = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProbationOverride = table.Column<bool>(type: "INTEGER", nullable: true),
                    IsNonBillable = table.Column<bool>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employee_monthly_performance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EmployeeCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ComplianceHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    EnteredHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    ApprovedHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    BillableHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    NonBillableHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    TrainingHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    OfficeHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    DetailedHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    DetailedEntries = table.Column<int>(type: "INTEGER", nullable: false),
                    UniqueProjects = table.Column<int>(type: "INTEGER", nullable: false),
                    AttendanceDays = table.Column<decimal>(type: "TEXT", nullable: false),
                    LeaveDays = table.Column<decimal>(type: "TEXT", nullable: false),
                    PunchHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    AttendanceTimesheetHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    TimesheetFilledDays = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpectedTimesheetDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MissingPunchDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LateDays = table.Column<int>(type: "INTEGER", nullable: false),
                    EarlyDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LessDurationDays = table.Column<int>(type: "INTEGER", nullable: false),
                    TimesheetCompletionScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    ApprovalScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    AttendanceDisciplineScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    OperationalScore = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_monthly_performance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "imported_source_file",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportType = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    StoredPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SheetCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imported_source_file", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "peer_review",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewerCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReviewerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SubjectCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SubjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Collaboration = table.Column<decimal>(type: "TEXT", nullable: false),
                    Communication = table.Column<decimal>(type: "TEXT", nullable: false),
                    Reliability = table.Column<decimal>(type: "TEXT", nullable: false),
                    TechnicalHelp = table.Column<decimal>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_peer_review", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reporting_month",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporting_month", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "team",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_EmployeeCode",
                table: "employee",
                column: "EmployeeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_monthly_performance_Year_Month_EmployeeName",
                table: "employee_monthly_performance",
                columns: new[] { "Year", "Month", "EmployeeName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_imported_source_file_Year_Month_ReportType",
                table: "imported_source_file",
                columns: new[] { "Year", "Month", "ReportType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_peer_review_Year_Month_ReviewerCode_SubjectCode",
                table: "peer_review",
                columns: new[] { "Year", "Month", "ReviewerCode", "SubjectCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reporting_month_Year_Month",
                table: "reporting_month",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_Name",
                table: "team",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysis_exclusion");

            migrationBuilder.DropTable(
                name: "employee");

            migrationBuilder.DropTable(
                name: "employee_monthly_performance");

            migrationBuilder.DropTable(
                name: "imported_source_file");

            migrationBuilder.DropTable(
                name: "peer_review");

            migrationBuilder.DropTable(
                name: "reporting_month");

            migrationBuilder.DropTable(
                name: "team");
        }
    }
}
