using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarthOnline.Desktop.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "achievements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Desc = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "manual"),
                    AutoKey = table.Column<string>(type: "TEXT", nullable: true),
                    Unlocked = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    UnlockedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Time = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bag_categories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bag_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "collections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    FileMetaJson = table.Column<string>(type: "TEXT", nullable: true),
                    FileUri = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "physical"),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Lat = table.Column<double>(type: "REAL", nullable: false),
                    Lng = table.Column<double>(type: "REAL", nullable: false),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "memos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "note"),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "profile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    AvatarKey = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    AvatarPath = table.Column<string>(type: "TEXT", nullable: true),
                    AvatarData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Gender = table.Column<string>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", nullable: false),
                    Province = table.Column<string>(type: "TEXT", nullable: false),
                    Signature = table.Column<string>(type: "TEXT", nullable: false),
                    BirthDate = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    CustomFieldsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "todo"),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "planning"),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModified = table.Column<string>(type: "TEXT", nullable: false),
                    DoneAt = table.Column<string>(type: "TEXT", nullable: true),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_achievements_Category",
                table: "achievements",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_activities_Time",
                table: "activities",
                column: "Time");

            migrationBuilder.CreateIndex(
                name: "IX_bag_categories_Scope_SortOrder",
                table: "bag_categories",
                columns: new[] { "Scope", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_collections_Category",
                table: "collections",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_items_Category",
                table: "items",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_locations_Date",
                table: "locations",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_memos_CreatedAt",
                table: "memos",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_ParentId",
                table: "tasks",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_Status",
                table: "tasks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "achievements");

            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropTable(
                name: "bag_categories");

            migrationBuilder.DropTable(
                name: "collections");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "memos");

            migrationBuilder.DropTable(
                name: "profile");

            migrationBuilder.DropTable(
                name: "tasks");
        }
    }
}
