using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceColumnsToServerInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CpuCount",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CpuCountOverride",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableHT",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableRanking",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxMem",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MaxMemOverride",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuCount",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "CpuCountOverride",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "EnableHT",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "EnableRanking",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "MaxMem",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "MaxMemOverride",
                table: "ServerInstances");
        }
    }
}
