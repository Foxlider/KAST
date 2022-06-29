using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Core.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Authors",
                columns: table => new
                {
                    AuthorID = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    URL = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authors", x => x.AuthorID);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArmaPath = table.Column<string>(type: "TEXT", nullable: true),
                    ModStagingDir = table.Column<string>(type: "TEXT", nullable: true),
                    UsingContactDlc = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsingGmDlc = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsingPfDlc = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsingClsaDlc = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseWsDlc = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    CliWorkers = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Login = table.Column<string>(type: "TEXT", nullable: false),
                    Pass = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mods",
                columns: table => new
                {
                    ModID = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorID = table.Column<ulong>(type: "INTEGER", nullable: true),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    SteamLastUpdated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LocalLastUpdated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsLocal = table.Column<bool>(type: "INTEGER", nullable: true),
                    ModStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedSize = table.Column<ulong>(type: "INTEGER", nullable: true),
                    ActualSize = table.Column<ulong>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mods", x => x.ModID);
                    table.ForeignKey(
                        name: "FK_Mods_Authors_AuthorID",
                        column: x => x.AuthorID,
                        principalTable: "Authors",
                        principalColumn: "AuthorID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Mods_AuthorID",
                table: "Mods",
                column: "AuthorID");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Mods");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Authors");
        }
    }
}
