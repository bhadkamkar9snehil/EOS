using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskRequiredSkill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredLevel",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredSkillId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredLevel",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RequiredSkillId",
                table: "Tasks");
        }
    }
}
