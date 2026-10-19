using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V1PerformanceAndPeerReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonthlyPerformance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    ComplianceHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    EnteredHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    ApprovedHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    BillableHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    NonBillableHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    TrainingHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    OfficeHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    Utilization = table.Column<decimal>(type: "TEXT", nullable: false),
                    UniqueProjects = table.Column<int>(type: "INTEGER", nullable: false),
                    AttendanceDays = table.Column<decimal>(type: "TEXT", nullable: false),
                    LeaveDays = table.Column<decimal>(type: "TEXT", nullable: false),
                    PunchHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    TimesheetFilledDays = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExpectedTimesheetDays = table.Column<decimal>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_MonthlyPerformance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyPerformance_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PeerReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewerPersonId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReviewerName = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectPersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Collaboration = table.Column<decimal>(type: "TEXT", nullable: false),
                    Communication = table.Column<decimal>(type: "TEXT", nullable: false),
                    Reliability = table.Column<decimal>(type: "TEXT", nullable: false),
                    TechnicalHelp = table.Column<decimal>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeerReviews_People_SubjectPersonId",
                        column: x => x.SubjectPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPerformance_PersonId_Year_Month",
                table: "MonthlyPerformance",
                columns: new[] { "PersonId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeerReviews_SubjectPersonId_Year_Month",
                table: "PeerReviews",
                columns: new[] { "SubjectPersonId", "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyPerformance");

            migrationBuilder.DropTable(
                name: "PeerReviews");
        }
    }
}
