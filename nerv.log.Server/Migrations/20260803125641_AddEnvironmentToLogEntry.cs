using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace nerv.log.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironmentToLogEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "environment",
                table: "Logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "environment",
                table: "Logs");
        }
    }
}
