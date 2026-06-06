using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(KastDbContext))]
    [Migration("20260606130000_AddSystemAuthSettings")]
    public partial class AddSystemAuthSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemAuthDomain",
                table: "Settings",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SystemAuthEnabled",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SystemAuthSource",
                table: "Settings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Auto");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemAuthDomain",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SystemAuthEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SystemAuthSource",
                table: "Settings");
        }
    }
}
