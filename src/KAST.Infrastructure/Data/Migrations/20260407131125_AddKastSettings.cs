using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKastSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModsDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    ServersDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    Arma3ServerAppId = table.Column<int>(type: "INTEGER", nullable: false),
                    ThemeMode = table.Column<string>(type: "TEXT", nullable: false),
                    MetricsIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
