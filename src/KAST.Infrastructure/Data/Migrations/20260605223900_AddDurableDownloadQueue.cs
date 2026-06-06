using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableDownloadQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM DownloadTasks");

            migrationBuilder.AddColumn<string>(
                name: "DestinationPath",
                table: "DownloadTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<ulong>(
                name: "InstalledManifestId",
                table: "DownloadTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.AddColumn<bool>(
                name: "IsUpdate",
                table: "DownloadTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetries",
                table: "DownloadTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "ModId",
                table: "DownloadTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "DownloadTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "DownloadTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "DownloadTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTasks_ModId",
                table: "DownloadTasks",
                column: "ModId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTasks_Status",
                table: "DownloadTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTasks_Status_CreatedAt",
                table: "DownloadTasks",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadTasks_Mods_ModId",
                table: "DownloadTasks",
                column: "ModId",
                principalTable: "Mods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DownloadTasks_Mods_ModId",
                table: "DownloadTasks");

            migrationBuilder.DropIndex(
                name: "IX_DownloadTasks_ModId",
                table: "DownloadTasks");

            migrationBuilder.DropIndex(
                name: "IX_DownloadTasks_Status",
                table: "DownloadTasks");

            migrationBuilder.DropIndex(
                name: "IX_DownloadTasks_Status_CreatedAt",
                table: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "DestinationPath",
                table: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "InstalledManifestId",
                table: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "IsUpdate",
                table: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "MaxRetries",
                table: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "ModId",
                table: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "DownloadTasks");
        }
    }
}
