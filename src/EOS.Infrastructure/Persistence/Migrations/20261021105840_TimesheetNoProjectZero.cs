using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TimesheetNoProjectZero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The ERP writes project number 0 for work that belongs to no project; it is not a project.
            migrationBuilder.Sql("UPDATE Timesheets SET ProjectNo = NULL WHERE ProjectNo = '0';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
