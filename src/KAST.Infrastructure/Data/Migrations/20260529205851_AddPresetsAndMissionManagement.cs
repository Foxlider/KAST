using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPresetsAndMissionManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Campaigns_ServerInstances_ServerInstanceId",
                        column: x => x.ServerInstanceId,
                        principalTable: "ServerInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MissionTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissionTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MissionTags_ServerInstances_ServerInstanceId",
                        column: x => x.ServerInstanceId,
                        principalTable: "ServerInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModPresets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    RawHtmlContent = table.Column<string>(type: "TEXT", nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModPresets_ServerInstances_ServerInstanceId",
                        column: x => x.ServerInstanceId,
                        principalTable: "ServerInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sets_ServerInstances_ServerInstanceId",
                        column: x => x.ServerInstanceId,
                        principalTable: "ServerInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Missions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    PhysicalPath = table.Column<string>(type: "TEXT", nullable: false),
                    ModPresetId = table.Column<int>(type: "INTEGER", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Missions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Missions_ModPresets_ModPresetId",
                        column: x => x.ModPresetId,
                        principalTable: "ModPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Missions_ServerInstances_ServerInstanceId",
                        column: x => x.ServerInstanceId,
                        principalTable: "ServerInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModPresetEntries",
                columns: table => new
                {
                    ModPresetId = table.Column<int>(type: "INTEGER", nullable: false),
                    SteamModId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsClientSide = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsServerSide = table.Column<bool>(type: "INTEGER", nullable: false),
                    LoadOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModPresetEntries", x => new { x.ModPresetId, x.SteamModId });
                    table.ForeignKey(
                        name: "FK_ModPresetEntries_ModPresets_ModPresetId",
                        column: x => x.ModPresetId,
                        principalTable: "ModPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ModPresetEntries_Mods_SteamModId",
                        column: x => x.SteamModId,
                        principalTable: "Mods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignMissions",
                columns: table => new
                {
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false),
                    MissionId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignMissions", x => new { x.CampaignId, x.MissionId });
                    table.ForeignKey(
                        name: "FK_CampaignMissions_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignMissions_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MissionTagAssignments",
                columns: table => new
                {
                    MissionId = table.Column<int>(type: "INTEGER", nullable: false),
                    MissionTagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissionTagAssignments", x => new { x.MissionId, x.MissionTagId });
                    table.ForeignKey(
                        name: "FK_MissionTagAssignments_MissionTags_MissionTagId",
                        column: x => x.MissionTagId,
                        principalTable: "MissionTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MissionTagAssignments_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SetMissions",
                columns: table => new
                {
                    SetId = table.Column<int>(type: "INTEGER", nullable: false),
                    MissionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetMissions", x => new { x.SetId, x.MissionId });
                    table.ForeignKey(
                        name: "FK_SetMissions_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetMissions_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignMissions_MissionId",
                table: "CampaignMissions",
                column: "MissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_ServerInstanceId",
                table: "Campaigns",
                column: "ServerInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Missions_ModPresetId",
                table: "Missions",
                column: "ModPresetId");

            migrationBuilder.CreateIndex(
                name: "IX_Missions_ServerInstanceId",
                table: "Missions",
                column: "ServerInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Missions_ServerInstanceId_FileName",
                table: "Missions",
                columns: new[] { "ServerInstanceId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MissionTagAssignments_MissionTagId",
                table: "MissionTagAssignments",
                column: "MissionTagId");

            migrationBuilder.CreateIndex(
                name: "IX_MissionTags_ServerInstanceId_Name",
                table: "MissionTags",
                columns: new[] { "ServerInstanceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModPresetEntries_SteamModId",
                table: "ModPresetEntries",
                column: "SteamModId");

            migrationBuilder.CreateIndex(
                name: "IX_ModPresets_ServerInstanceId",
                table: "ModPresets",
                column: "ServerInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SetMissions_MissionId",
                table: "SetMissions",
                column: "MissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sets_ServerInstanceId",
                table: "Sets",
                column: "ServerInstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignMissions");

            migrationBuilder.DropTable(
                name: "MissionTagAssignments");

            migrationBuilder.DropTable(
                name: "ModPresetEntries");

            migrationBuilder.DropTable(
                name: "SetMissions");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "MissionTags");

            migrationBuilder.DropTable(
                name: "Missions");

            migrationBuilder.DropTable(
                name: "Sets");

            migrationBuilder.DropTable(
                name: "ModPresets");
        }
    }
}
