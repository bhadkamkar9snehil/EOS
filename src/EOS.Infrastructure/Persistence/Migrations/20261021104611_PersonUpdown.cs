using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersonUpdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsUpdownFromErp",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UpdownOverride",
                table: "People",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsUpdownFromErp",
                table: "People");

            migrationBuilder.DropColumn(
                name: "UpdownOverride",
                table: "People");
        }
    }
}
