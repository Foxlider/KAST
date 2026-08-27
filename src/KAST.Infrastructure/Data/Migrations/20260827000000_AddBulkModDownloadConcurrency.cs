using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations;

[DbContext(typeof(KastDbContext))]
[Migration("20260827000000_AddBulkModDownloadConcurrency")]
public partial class AddBulkModDownloadConcurrency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "BulkModDownloadConcurrency",
            table: "Settings",
            type: "INTEGER",
            nullable: false,
            defaultValue: 4);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BulkModDownloadConcurrency",
            table: "Settings");
    }
}
