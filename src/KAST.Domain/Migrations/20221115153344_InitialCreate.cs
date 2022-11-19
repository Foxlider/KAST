﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Domain.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Authors",
                columns: table => new
                {
                    AuthorID = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    URL = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authors", x => x.AuthorID);
                });

            migrationBuilder.CreateTable(
                name: "Mods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsLocal = table.Column<bool>(type: "bit", nullable: false),
                    AuthorID = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SteamLastUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LocalLastUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpectedSize = table.Column<decimal>(type: "decimal(20,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mods", x => x.Id);
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Mods");

            migrationBuilder.DropTable(
                name: "Authors");
        }
    }
}
