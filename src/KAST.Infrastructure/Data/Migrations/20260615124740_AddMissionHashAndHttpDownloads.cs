using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMissionHashAndHttpDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MissionDownloadBaseUrl",
                table: "Settings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HttpDownloadsEnabled",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<uint>(
                name: "Hash",
                table: "Missions",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MissionDownloadBaseUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "HttpDownloadsEnabled",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "Missions");
        }
    }
}
