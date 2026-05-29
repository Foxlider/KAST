using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArmaProfileAndDlcColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ServerProfileContent",
                table: "ServerInstances",
                newName: "ArmaProfileContent");

            migrationBuilder.AddColumn<bool>(
                name: "ContactDlc",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CslaDlc",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EfDlc",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "GmDlc",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PfDlc",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RfDlc",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SpeDlc",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WsDlc",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactDlc",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "CslaDlc",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "EfDlc",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "GmDlc",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "PfDlc",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "RfDlc",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "SpeDlc",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "WsDlc",
                table: "ServerInstances");

            migrationBuilder.RenameColumn(
                name: "ArmaProfileContent",
                table: "ServerInstances",
                newName: "ServerProfileContent");
        }
    }
}
