using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalendarDayHoursLeaveAndTaskActuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualHours",
                table: "Tasks",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Fraction",
                table: "Leaves",
                type: "TEXT",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Leaves",
                type: "TEXT",
                nullable: false,
                defaultValue: "eos");

            migrationBuilder.AddColumn<string>(
                name: "DayHours",
                table: "Calendars",
                type: "TEXT",
                nullable: true);

            // Owner decision 21 Oct 2026: Saturday officially ends at 1:30 pm (4.5 h). Applies to existing Mon-Sat calendars.
            migrationBuilder.Sql("UPDATE Calendars SET DayHours = '6=4.5' WHERE WorkDays = '1,2,3,4,5,6' AND DayHours IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualHours",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Fraction",
                table: "Leaves");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Leaves");

            migrationBuilder.DropColumn(
                name: "DayHours",
                table: "Calendars");
        }
    }
}
