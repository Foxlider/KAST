using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerInstallFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InstalledAt",
                table: "ServerInstances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstalledBuildId",
                table: "ServerInstances",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstalledAt",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "InstalledBuildId",
                table: "ServerInstances");
        }
    }
}
