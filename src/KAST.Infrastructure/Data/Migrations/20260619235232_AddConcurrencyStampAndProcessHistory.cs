using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyStampAndProcessHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "ServerInstances",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "HeadlessClients",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ServerInstanceProcessHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TerminationReason = table.Column<string>(type: "TEXT", nullable: false),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerInstanceProcessHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerInstanceProcessHistories_ServerInstances_ServerInstanceId",
                        column: x => x.ServerInstanceId,
                        principalTable: "ServerInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerInstanceProcessHistories_ServerInstanceId",
                table: "ServerInstanceProcessHistories",
                column: "ServerInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerInstanceProcessHistories_ServerInstanceId_EndedAt",
                table: "ServerInstanceProcessHistories",
                columns: new[] { "ServerInstanceId", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerInstanceProcessHistories");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "HeadlessClients");
        }
    }
}
