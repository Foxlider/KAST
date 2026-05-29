using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KAST.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MigrateClientModFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsClientSide",
                table: "ServerInstanceMods",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Existing rows where IsServerSide=0 were the old "client-side" category.
            // Migrate them to IsClientSide=1 so they keep their -mod= behaviour.
            migrationBuilder.Sql(
                "UPDATE \"ServerInstanceMods\" SET \"IsClientSide\" = 1 WHERE \"IsServerSide\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsClientSide",
                table: "ServerInstanceMods");
        }
    }
}
