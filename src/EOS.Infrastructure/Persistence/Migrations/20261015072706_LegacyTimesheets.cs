using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LegacyTimesheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Band",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsConsultant",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsNonBillable",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnProbation",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Seniority",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Timesheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProjectNo = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: true),
                    Activity = table.Column<string>(type: "TEXT", nullable: true),
                    Milestone = table.Column<string>(type: "TEXT", nullable: true),
                    Task = table.Column<string>(type: "TEXT", nullable: true),
                    TaskType = table.Column<string>(type: "TEXT", nullable: true),
                    Band = table.Column<string>(type: "TEXT", nullable: true),
                    Hours = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timesheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Timesheets_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Timesheets_Key",
                table: "Timesheets",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Timesheets_PersonId_Date",
                table: "Timesheets",
                columns: new[] { "PersonId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Timesheets_ProjectNo",
                table: "Timesheets",
                column: "ProjectNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Timesheets");

            migrationBuilder.DropColumn(
                name: "Band",
                table: "People");

            migrationBuilder.DropColumn(
                name: "IsConsultant",
                table: "People");

            migrationBuilder.DropColumn(
                name: "IsNonBillable",
                table: "People");

            migrationBuilder.DropColumn(
                name: "IsOnProbation",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Seniority",
                table: "People");
        }
    }
}
