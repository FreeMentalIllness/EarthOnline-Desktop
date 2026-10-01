using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarthOnline.Desktop.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAtMore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeletedAt",
                table: "locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedAt",
                table: "items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedAt",
                table: "collections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "items");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "collections");
        }
    }
}
