using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace nerv.log.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataToLogEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "metadata",
                table: "Logs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "metadata",
                table: "Logs");
        }
    }
}
